using Autodesk.AutoCAD.DatabaseServices;

namespace DustyAutoCAD.Core
{
    public class LayerInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsUsed { get; set; }
        public int ObjectCount { get; set; }
    }

    public static class LayerScanner
    {
        public static List<LayerInfo> ScanLayers(Database db)
        {
            var usageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using var tr = db.TransactionManager.StartOpenCloseTransaction();

            // Count entities per layer across all block table records
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                foreach (ObjectId entId in btr)
                {
                    if (tr.GetObject(entId, OpenMode.ForRead) is Entity ent)
                    {
                        var layerName = ent.Layer;
                        usageCounts[layerName] = usageCounts.TryGetValue(layerName, out var c) ? c + 1 : 1;
                    }
                }
            }

            var layers = new List<LayerInfo>();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                var count = usageCounts.TryGetValue(ltr.Name, out var n) ? n : 0;
                layers.Add(new LayerInfo
                {
                    Name = ltr.Name,
                    ObjectCount = count,
                    IsUsed = count > 0
                });
            }

            tr.Commit();

            layers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return layers;
        }
    }
}
