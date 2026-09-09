using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.AutoCAD.DatabaseServices;
using DustyAutoCAD.Core;
using Microsoft.Win32;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DustyAutoCAD.UI
{
    public partial class LayerManagerWindow : Window
    {
        private readonly Database _db;
        private readonly ObservableCollection<LayerManagerRow> _rows = new();
        private ICollectionView _view = null!;

        // Map preset file paths (parallel to combo_map items)
        private List<string> _mapPresetPaths = new();

        private static string LastUsedMapPath =>
            Path.Combine(AcadPreset.DefaultPresetDir, "_layer_mgr_last.json");

        public LayerManagerWindow(Database db)
        {
            _db = db;
            InitializeComponent();

            // Restore last-used map preset if available
            AcadPreset? lastMap = null;
            if (File.Exists(LastUsedMapPath))
            {
                try { lastMap = AcadPreset.Load(LastUsedMapPath); } catch { }
            }

            Refresh(lastMap);
            PopulateVisPresetCombo();
            PopulateMapPresetCombo();

            if (lastMap != null)
                txt_map_preset.Text = lastMap.PresetName;

            Closing += OnWindowClosing;
        }

        // ── Data loading ─────────────────────────────────────────────────────

        private void Refresh(AcadPreset? mapPreset = null)
        {
            // Preserve current user-entered mapping data
            var savedMappings = _rows.ToDictionary(
                r => r.SourceLayer,
                r => (r.TargetLayer, r.Linestyle, r.Intent, r.IsIncluded),
                StringComparer.OrdinalIgnoreCase);

            _rows.Clear();

            // Scan layers
            var layerInfos = LayerScanner.ScanLayers(_db);

            // Build preset lookup for map preset
            var presetLookup = new Dictionary<string, AcadLayerMapping>(StringComparer.OrdinalIgnoreCase);
            if (mapPreset != null)
                foreach (var m in mapPreset.Mappings)
                    presetLookup[m.Source] = m;

            // Read color + visibility from LayerTable
            using var tr = _db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);

            foreach (var info in layerInfos)
            {
                Color wpfColor = Colors.Gray;
                bool isVisible = true;

                if (lt.Has(info.Name))
                {
                    var ltr = (LayerTableRecord)tr.GetObject(lt[info.Name], OpenMode.ForRead);
                    wpfColor  = AciToWpf(ltr.Color.ColorIndex);
                    isVisible = !ltr.IsOff;
                }

                var row = new LayerManagerRow(info, wpfColor, isVisible);

                // Apply map preset first, then existing user edits take priority
                if (presetLookup.TryGetValue(info.Name, out var mapping))
                {
                    row.TargetLayer = mapping.Target;
                    row.Linestyle   = mapping.Linestyle;
                    row.Intent      = mapping.Intent;
                    row.IsIncluded  = mapping.Include;
                }

                // Override with previously entered values if no preset supplied
                if (mapPreset == null && savedMappings.TryGetValue(info.Name, out var saved))
                {
                    row.TargetLayer = saved.TargetLayer;
                    row.Linestyle   = saved.Linestyle;
                    row.Intent      = saved.Intent;
                    row.IsIncluded  = saved.IsIncluded;
                }

                _rows.Add(row);
            }

            tr.Commit();

            _view = CollectionViewSource.GetDefaultView(_rows);
            grid_layers.ItemsSource = _view;

            ApplySearchFilter(txt_search?.Text ?? string.Empty);
            UpdateCount();
        }

        private void UpdateCount() =>
            txt_count.Text = $"{_rows.Count} layer(s)";

        // ── Preset combos ────────────────────────────────────────────────────

        private void PopulateVisPresetCombo()
        {
            combo_vis.Items.Clear();
            foreach (var name in LayerPresetStore.GetPresetNames())
                combo_vis.Items.Add(name);
        }

        private void PopulateMapPresetCombo()
        {
            combo_map.Items.Clear();
            _mapPresetPaths.Clear();
            foreach (var path in AcadPreset.ListPresets())
            {
                _mapPresetPaths.Add(path);
                combo_map.Items.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        // ── Search filter ─────────────────────────────────────────────────────

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter(txt_search.Text);
        }

        private void ApplySearchFilter(string text)
        {
            if (_view == null) return;
            _view.Filter = string.IsNullOrWhiteSpace(text)
                ? null
                : obj => obj is LayerManagerRow r &&
                         r.SourceLayer.Contains(text, StringComparison.OrdinalIgnoreCase);
            _view.Refresh();
        }

        // ── Toolbar: Scan ────────────────────────────────────────────────────

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
            txt_status.Text = $"{_rows.Count} layer(s) refreshed from drawing.";
        }

        // ── Color swatch in DataGridRow ───────────────────────────────────────

        private void DataGridRow_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DataGridRow row) return;
            if (row.DataContext is not LayerManagerRow lmr) return;

            // Color swatch is column index 2
            var cell = GetCell(row, 2);
            if (cell == null) return;

            var border = FindVisualChild<Border>(cell);
            if (border != null)
                border.Background = new SolidColorBrush(lmr.LayerColor);
        }

        private static DataGridCell? GetCell(DataGridRow row, int columnIndex)
        {
            var presenter = FindVisualChild<DataGridCellsPresenter>(row);
            if (presenter == null) return null;
            return presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        // ── Inspection panel ──────────────────────────────────────────────────

        private string? _selectedLayerName;

        private void Grid_Layers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (grid_layers.SelectedItem is not LayerManagerRow row)
            {
                txt_info_name.Text             = "Select a layer row to inspect its entities.";
                txt_info_detail.Visibility     = System.Windows.Visibility.Collapsed;
                btn_select_in_drawing.IsEnabled = false;
                _selectedLayerName             = null;
                return;
            }

            _selectedLayerName              = row.SourceLayer;
            btn_select_in_drawing.IsEnabled = true;
            txt_info_name.Text              = row.SourceLayer;

            try
            {
                using var tr = _db.TransactionManager.StartTransaction();
                var bt  = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int total  = 0;

                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    if (!string.Equals(ent.Layer, row.SourceLayer, StringComparison.OrdinalIgnoreCase)) continue;
                    total++;
                    var typeName = ent.GetType().Name;
                    groups[typeName] = groups.TryGetValue(typeName, out var n) ? n + 1 : 1;
                }
                tr.Commit();

                if (total == 0)
                {
                    txt_info_detail.Text       = "No entities in model space on this layer.";
                    txt_info_detail.Visibility = System.Windows.Visibility.Visible;
                    return;
                }

                var breakdown = string.Join("  ·  ",
                    groups.OrderByDescending(kv => kv.Value)
                          .Select(kv => $"{kv.Key}: {kv.Value}"));
                txt_info_detail.Text       = $"{total} entit{(total == 1 ? "y" : "ies")}  ·  {breakdown}";
                txt_info_detail.Visibility = System.Windows.Visibility.Visible;
            }
            catch (Exception ex)
            {
                txt_info_detail.Text       = $"(Query failed: {ex.Message})";
                txt_info_detail.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void BtnSelectInDrawing_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLayerName == null) return;
            var acDoc = Application.DocumentManager.MdiActiveDocument;
            if (acDoc == null) return;

            try
            {
                var ids = new List<ObjectId>();

                using var tr = _db.TransactionManager.StartTransaction();
                var bt  = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                        string.Equals(ent.Layer, _selectedLayerName, StringComparison.OrdinalIgnoreCase))
                        ids.Add(id);
                }
                tr.Commit();

                if (ids.Count == 0)
                {
                    txt_status.Text = "No entities found on that layer.";
                    return;
                }

                using var docLock = acDoc.LockDocument();
                acDoc.Editor.SetImpliedSelection(ids.ToArray());
                txt_status.Text = $"Selected {ids.Count} entit{(ids.Count == 1 ? "y" : "ies")} on \"{_selectedLayerName}\".";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Select failed: {ex.Message}";
            }
        }

        // ── Bulk visibility ────────────────────────────────────────────────────

        private void BtnAllVisOn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsVisible = true;
            txt_status.Text = "All layers set visible.";
        }

        private void BtnAllVisOff_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsVisible = false;
            txt_status.Text = "All layers hidden.";
        }

        private void BtnVisInvert_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsVisible = !r.IsVisible;
            txt_status.Text = "Visibility inverted.";
        }

        // ── Bulk include ───────────────────────────────────────────────────────

        private void BtnAllIncl_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsIncluded = true;
            txt_status.Text = "All layers included.";
        }

        private void BtnNoneIncl_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsIncluded = false;
            txt_status.Text = "All layers excluded.";
        }

        // ── Vis presets ───────────────────────────────────────────────────────

        private void BtnSaveVis_Click(object sender, RoutedEventArgs e)
        {
            var name = txt_vis_preset.Text.Trim();
            if (string.IsNullOrEmpty(name)) { txt_status.Text = "Enter a vis preset name first."; return; }

            var preset = new LayerPreset { Name = name };
            foreach (var r in _rows)
                preset.Layers[r.SourceLayer] = r.IsVisible;

            LayerPresetStore.Save(preset);
            PopulateVisPresetCombo();
            txt_status.Text = $"Vis preset \"{name}\" saved.";
        }

        private void BtnLoadVis_Click(object sender, RoutedEventArgs e)
        {
            var name = combo_vis.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { txt_status.Text = "Select a vis preset to load."; return; }

            var preset = LayerPresetStore.Load(name);
            if (preset == null) { txt_status.Text = "Could not load vis preset."; return; }

            foreach (var r in _rows)
            {
                if (preset.Layers.TryGetValue(r.SourceLayer, out var vis))
                    r.IsVisible = vis;
            }
            txt_status.Text = $"Vis preset \"{name}\" loaded.";
        }

        // ── Map presets ────────────────────────────────────────────────────────

        private void BtnSaveMap_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(AcadPreset.DefaultPresetDir);

            var dlg = new SaveFileDialog
            {
                Title            = "Save Map Preset",
                Filter           = "Preset files (*.json)|*.json",
                InitialDirectory = AcadPreset.DefaultPresetDir,
                FileName         = string.IsNullOrWhiteSpace(txt_map_preset.Text)
                                       ? "MyPreset"
                                       : txt_map_preset.Text.Trim()
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var preset = new AcadPreset
                {
                    PresetName = Path.GetFileNameWithoutExtension(dlg.FileName),
                    Mappings   = _rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.TargetLayer))
                        .Select(r => new AcadLayerMapping
                        {
                            Source    = r.SourceLayer,
                            Target    = r.TargetLayer,
                            Linestyle = r.Linestyle,
                            Intent    = r.Intent,
                            Include   = r.IsIncluded
                        })
                        .ToList()
                };

                preset.Save(dlg.FileName);
                txt_map_preset.Text = preset.PresetName;
                PopulateMapPresetCombo();
                txt_status.Text = $"Map preset saved: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Save failed: {ex.Message}";
            }
        }

        private void BtnLoadMap_Click(object sender, RoutedEventArgs e)
        {
            int idx = combo_map.SelectedIndex;
            if (idx < 0 || idx >= _mapPresetPaths.Count)
            {
                txt_status.Text = "Select a map preset to load.";
                return;
            }

            try
            {
                var preset = AcadPreset.Load(_mapPresetPaths[idx]);
                txt_map_preset.Text = preset.PresetName;

                // Rebuild with new preset
                Refresh(preset);

                var filled = _rows.Count(r => r.IsIncluded);
                txt_status.Text = $"Map preset \"{preset.PresetName}\" loaded — {filled} mapping(s).";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Load failed: {ex.Message}";
            }
        }

        // ── Apply mapping ─────────────────────────────────────────────────────

        private void BtnApplyMapping_Click(object sender, RoutedEventArgs e)
        {
            var included = _rows.Where(r => r.IsIncluded && !string.IsNullOrWhiteSpace(r.TargetLayer)).ToList();
            if (included.Count == 0)
            {
                txt_status.Text = "No layers included — nothing to apply.";
                return;
            }

            try
            {
                txt_status.Text = "Applying mapping...";

                // Project LayerManagerRow → LayerRowViewModel for LayerMapper
                var vmRows = ProjectToViewModels(_rows);
                var vmIncluded = ProjectToViewModels(included);

                var result = LayerMapper.ApplyMappings(_db, vmIncluded);

                if (chk_purge.IsChecked == true)
                    LayerMapper.PurgeLayers(_db, vmRows);

                if (chk_freeze_ref.IsChecked == true)
                    LayerMapper.FreezeLayers(_db, vmRows, "reference");

                var summary = $"Done — {result.Renamed} renamed, {result.Skipped} skipped.";
                if (result.Errors.Count > 0)
                    summary += $" {result.Errors.Count} error(s).";

                txt_status.Text = summary;

                if (result.Errors.Count > 0)
                {
                    MessageBox.Show("Completed with errors:\n\n" + string.Join("\n", result.Errors.Take(10)),
                        "Apply Result", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Re-scan to reflect new state
                Refresh();
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Apply failed: {ex.Message}";
                MessageBox.Show($"Apply failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Apply visibility ──────────────────────────────────────────────────

        private void BtnApplyVisibility_Click(object sender, RoutedEventArgs e)
        {
            var acDoc = Application.DocumentManager.MdiActiveDocument;
            if (acDoc == null) return;

            try
            {
                using var docLock = acDoc.LockDocument();
                using var tr = _db.TransactionManager.StartTransaction();
                var lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);

                foreach (var row in _rows)
                {
                    if (!lt.Has(row.SourceLayer)) continue;
                    var ltr = (LayerTableRecord)tr.GetObject(lt[row.SourceLayer], OpenMode.ForWrite);
                    ltr.IsOff = !row.IsVisible;
                }

                tr.Commit();
                txt_status.Text = "Layer visibility applied.";
                acDoc.Editor.Regen();
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Apply visibility failed: {ex.Message}";
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Projects LayerManagerRows to LayerRowViewModels for LayerMapper compatibility.</summary>
        private static List<LayerRowViewModel> ProjectToViewModels(IEnumerable<LayerManagerRow> source)
        {
            return source.Select(r =>
            {
                var vm = new LayerRowViewModel(new LayerInfo
                {
                    Name        = r.SourceLayer,
                    IsUsed      = r.IsUsed,
                    ObjectCount = r.ObjectCount
                });
                vm.TargetLayer = r.TargetLayer;
                vm.Linestyle   = r.Linestyle;
                vm.Intent      = r.Intent;
                vm.IsIncluded  = r.IsIncluded;
                return vm;
            }).ToList();
        }

        private static Color AciToWpf(int aci) => aci switch
        {
            1 => Colors.Red,
            2 => Colors.Yellow,
            3 => Colors.Green,
            4 => Colors.Cyan,
            5 => Colors.Blue,
            6 => Color.FromRgb(255, 0, 255),
            7 => Colors.White,
            _ => Color.FromRgb(128, 128, 128),
        };

        // ── Session state ──────────────────────────────────────────────────────

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AcadPreset.DefaultPresetDir);
                var snapshot = new AcadPreset
                {
                    PresetName = txt_map_preset.Text.Trim() is { Length: > 0 } n ? n : "Last Used",
                    Mappings   = _rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.TargetLayer))
                        .Select(r => new AcadLayerMapping
                        {
                            Source    = r.SourceLayer,
                            Target    = r.TargetLayer,
                            Linestyle = r.Linestyle,
                            Intent    = r.Intent,
                            Include   = r.IsIncluded
                        })
                        .ToList()
                };
                snapshot.Save(LastUsedMapPath);
            }
            catch { /* non-fatal */ }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }
    }
}
