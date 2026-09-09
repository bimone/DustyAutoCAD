using System.ComponentModel;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace DustyAutoCAD.Core
{
    public class LayerRowViewModel : INotifyPropertyChanged
    {
        private bool _isIncluded;
        private string _targetLayer;
        private string _linestyle;
        private string _intent;

        public string SourceLayer { get; }
        public bool IsUsed { get; }

        public bool IsIncluded
        {
            get => _isIncluded;
            set { _isIncluded = value; OnPropertyChanged(nameof(IsIncluded)); }
        }

        public string TargetLayer
        {
            get => _targetLayer;
            set { _targetLayer = value; OnPropertyChanged(nameof(TargetLayer)); OnPropertyChanged(nameof(EffectiveTarget)); }
        }

        public string Linestyle
        {
            get => _linestyle;
            set { _linestyle = value; OnPropertyChanged(nameof(Linestyle)); }
        }

        public string Intent
        {
            get => _intent;
            set { _intent = value; OnPropertyChanged(nameof(Intent)); OnPropertyChanged(nameof(EffectiveTarget)); }
        }

        public string EffectiveTarget => _intent?.ToLowerInvariant() switch
        {
            "reference" => _targetLayer + "_REF",
            "obstacle"  => _targetLayer + "_OBS",
            _           => _targetLayer
        };

        public LayerRowViewModel(LayerInfo info)
        {
            SourceLayer  = info.Name;
            IsUsed       = info.IsUsed;
            _targetLayer = string.Empty;
            _linestyle   = "Solid";
            _intent      = "printable";
            _isIncluded  = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MapResult
    {
        public int Renamed { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public static class LayerMapper
    {
        private static readonly Dictionary<string, string> LinestyleMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Solid",          "Continuous" },
            { "1 Dash 1 Dot",   "DASHDOT" },
            { "2 Dash 1 Dot",   "DIVIDE" },
            { "Soffit Framing", "PHANTOM" },
            { "Soffit Finish",  "DASHED" },
            { "Duct CL",        "CENTER" },
            { "Lighting Fixt.", "PHANTOM2" },
        };

        public static string ToAcadLinetype(string linestyle) =>
            LinestyleMap.TryGetValue(linestyle, out var lt) ? lt : "Continuous";

        public static List<LayerRowViewModel> BuildRows(IEnumerable<LayerInfo> layers, AcadPreset? preset)
        {
            // Build lookup from source name -> mapping
            var presetLookup = new Dictionary<string, AcadLayerMapping>(StringComparer.OrdinalIgnoreCase);
            if (preset != null)
            {
                foreach (var m in preset.Mappings)
                    presetLookup[m.Source] = m;
            }

            var rows = new List<LayerRowViewModel>();
            foreach (var layer in layers)
            {
                var row = new LayerRowViewModel(layer);

                if (presetLookup.TryGetValue(layer.Name, out var mapping))
                {
                    row.TargetLayer = mapping.Target;
                    row.Linestyle   = mapping.Linestyle;
                    row.Intent      = mapping.Intent;
                    row.IsIncluded  = mapping.Include;
                }

                rows.Add(row);
            }

            return rows;
        }

        public static MapResult ApplyMappings(Database db, IEnumerable<LayerRowViewModel> rows)
        {
            var result = new MapResult();

            var doc = Application.DocumentManager.GetDocument(db);
            using var docLock = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            try
            {
                var lt  = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForWrite);

                foreach (var row in rows)
                {
                    if (!row.IsIncluded || string.IsNullOrWhiteSpace(row.TargetLayer))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (!lt.Has(row.SourceLayer))
                    {
                        result.Skipped++;
                        continue;
                    }

                    try
                    {
                        var linetype = ToAcadLinetype(row.Linestyle);
                        EnsureLinetype(db, tr, ltt, linetype);

                        var sourceId  = lt[row.SourceLayer];
                        var sourceLtr = (LayerTableRecord)tr.GetObject(sourceId, OpenMode.ForWrite);
                        var effective = row.EffectiveTarget;

                        if (!string.Equals(row.SourceLayer, effective, StringComparison.OrdinalIgnoreCase)
                            && lt.Has(effective))
                        {
                            // Merge into existing target layer
                            var targetId = lt[effective];
                            MergeLayer(db, tr, sourceId, targetId);
                            sourceLtr.Erase();
                        }
                        else
                        {
                            sourceLtr.Name              = effective;
                            sourceLtr.LinetypeObjectId  = ltt[linetype];
                            sourceLtr.Color             = Color.FromColorIndex(ColorMethod.ByAci, 7);
                        }

                        result.Renamed++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{row.SourceLayer}: {ex.Message}");
                    }
                }

                tr.Commit();
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Transaction failed: {ex.Message}");
            }

            return result;
        }

        public static void PurgeLayers(Database db, IEnumerable<LayerRowViewModel> rows)
        {
            var protect = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0", "Defpoints" };

            var doc = Application.DocumentManager.GetDocument(db);
            using var docLock = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);

            foreach (var row in rows)
            {
                if (row.IsIncluded) continue;
                if (protect.Contains(row.SourceLayer)) continue;
                if (!lt.Has(row.SourceLayer)) continue;

                try
                {
                    var ltr = (LayerTableRecord)tr.GetObject(lt[row.SourceLayer], OpenMode.ForWrite);
                    ltr.Erase();
                }
                catch { /* layer in use - skip */ }
            }

            tr.Commit();
        }

        public static void FreezeLayers(Database db, IEnumerable<LayerRowViewModel> rows, string intent)
        {
            var targets = rows
                .Where(r => string.Equals(r.Intent, intent, StringComparison.OrdinalIgnoreCase) && r.IsIncluded)
                .Select(r => r.EffectiveTarget)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (targets.Count == 0) return;

            var doc = Application.DocumentManager.GetDocument(db);
            using var docLock = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            foreach (ObjectId id in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (targets.Contains(ltr.Name))
                {
                    ltr.UpgradeOpen();
                    ltr.IsFrozen = true;
                }
            }

            tr.Commit();
        }

        private static void EnsureLinetype(Database db, Transaction tr, LinetypeTable ltt, string linetypeName)
        {
            if (ltt.Has(linetypeName)) return;
            if (string.Equals(linetypeName, "Continuous", StringComparison.OrdinalIgnoreCase)) return;

            var acadLin = FindAcadLin();
            if (acadLin != null)
                db.LoadLineTypeFile(linetypeName, acadLin);
        }

        /// <summary>
        /// Searches for acad.lin across installed AutoCAD versions so the add-in
        /// is not tied to a specific release year.
        /// </summary>
        private static string? FindAcadLin()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var autodesk = Path.Combine(programFiles, "Autodesk");
            if (!Directory.Exists(autodesk)) return null;

            // Prefer newest version: sort descending by directory name
            var candidates = Directory.GetDirectories(autodesk, "AutoCAD*")
                                      .OrderByDescending(d => d)
                                      .Select(d => Path.Combine(d, "acad.lin"))
                                      .FirstOrDefault(File.Exists);
            return candidates;
        }

        private static void MergeLayer(Database db, Transaction tr, ObjectId sourceLayerId, ObjectId targetLayerId)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                foreach (ObjectId entId in btr)
                {
                    if (tr.GetObject(entId, OpenMode.ForRead) is Entity ent && ent.LayerId == sourceLayerId)
                    {
                        ent.UpgradeOpen();
                        ent.LayerId = targetLayerId;
                    }
                }
            }
        }
    }
}
