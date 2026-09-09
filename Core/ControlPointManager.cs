using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DustyAutoCAD.Core
{
    public static class ControlPointManager
    {
        public const  string BlockName   = "DUSTY_CP";
        public const  string LayerName   = "DUSTY-CP";
        private const string TagName     = "POINTNAME";
        private const string TagDesc     = "DESCRIPTION";
        private const double MarkerRadius = 1.0;

        // ----------------------------------------------------------------
        // Block / layer setup
        // ----------------------------------------------------------------

        public static void EnsureBlockExists(Database db, Transaction tr)
        {
            EnsureLayer(db, tr);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlockName)) return;

            bt.UpgradeOpen();
            var btr = new BlockTableRecord { Name = BlockName, Origin = Point3d.Origin };
            bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            double r = MarkerRadius;

            var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, r) { Layer = LayerName };
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            var hLine = new Line(new Point3d(-r * 1.4, 0, 0), new Point3d(r * 1.4, 0, 0)) { Layer = LayerName };
            var vLine = new Line(new Point3d(0, -r * 1.4, 0), new Point3d(0, r * 1.4, 0)) { Layer = LayerName };
            btr.AppendEntity(hLine); tr.AddNewlyCreatedDBObject(hLine, true);
            btr.AppendEntity(vLine); tr.AddNewlyCreatedDBObject(vLine, true);

            // POINTNAME attribute — visible, shown right of marker
            var atName = new AttributeDefinition
            {
                Tag       = TagName,
                Prompt    = "Point Name:",
                TextString = TagName,
                Position  = new Point3d(r * 1.6, r * 0.4, 0),
                Height    = r * 0.7,
                Layer     = LayerName,
                Justify   = AttachmentPoint.MiddleLeft,
            };
            atName.SetDatabaseDefaults();
            btr.AppendEntity(atName);
            tr.AddNewlyCreatedDBObject(atName, true);

            // DESCRIPTION attribute — invisible (data only)
            var atDesc = new AttributeDefinition
            {
                Tag       = TagDesc,
                Prompt    = "Description:",
                TextString = TagDesc,
                Position  = new Point3d(r * 1.6, -r * 0.4, 0),
                Height    = r * 0.5,
                Layer     = LayerName,
                Justify   = AttachmentPoint.MiddleLeft,
                Invisible = true,
            };
            atDesc.SetDatabaseDefaults();
            btr.AppendEntity(atDesc);
            tr.AddNewlyCreatedDBObject(atDesc, true);
        }

        private static void EnsureLayer(Database db, Transaction tr)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName)) return;
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = LayerName };
            // Cyan color (index 4) — stands out on typical construction drawings
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 4);
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        // ----------------------------------------------------------------
        // Place
        // ----------------------------------------------------------------

        public static ObjectId PlaceControlPoint(Database db, Point3d location,
                                                  string name, string description)
        {
            var doc = Application.DocumentManager.GetDocument(db);
            using var docLock = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();

            EnsureBlockExists(db, tr);

            var bt  = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms  = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockName], OpenMode.ForRead);

            var bref = new BlockReference(location, bt[BlockName]) { Layer = LayerName };
            ms.AppendEntity(bref);
            tr.AddNewlyCreatedDBObject(bref, true);

            foreach (ObjectId attId in btr)
            {
                if (tr.GetObject(attId, OpenMode.ForRead) is not AttributeDefinition ad) continue;
                var ar = new AttributeReference();
                ar.SetAttributeFromBlock(ad, bref.BlockTransform);
                ar.TextString = ad.Tag.Equals(TagName, StringComparison.OrdinalIgnoreCase)
                    ? name : description;
                bref.AttributeCollection.AppendAttribute(ar);
                tr.AddNewlyCreatedDBObject(ar, true);
            }

            var id = bref.ObjectId;
            tr.Commit();
            return id;
        }

        // ----------------------------------------------------------------
        // Read
        // ----------------------------------------------------------------

        public static List<ControlPoint> GetAll(Database db)
        {
            var result = new List<ControlPoint>();
            using var tr = db.TransactionManager.StartOpenCloseTransaction();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BlockName)) return result;

            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId eid in ms)
            {
                if (tr.GetObject(eid, OpenMode.ForRead) is not BlockReference bref) continue;

                // Match by block definition name
                if (tr.GetObject(bref.BlockTableRecord, OpenMode.ForRead) is not BlockTableRecord def) continue;
                if (!def.Name.Equals(BlockName, StringComparison.OrdinalIgnoreCase)) continue;

                var cp = new ControlPoint
                {
                    BlockRefId = eid,
                    Easting    = bref.Position.X,
                    Northing   = bref.Position.Y,
                    Elevation  = bref.Position.Z,
                };

                foreach (ObjectId aid in bref.AttributeCollection)
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not AttributeReference ar) continue;
                    if (ar.Tag.Equals(TagName, StringComparison.OrdinalIgnoreCase))
                        cp.Name = ar.TextString;
                    else if (ar.Tag.Equals(TagDesc, StringComparison.OrdinalIgnoreCase))
                        cp.Description = ar.TextString;
                }

                result.Add(cp);
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // ----------------------------------------------------------------
        // Export
        // ----------------------------------------------------------------

        public static string ToCsv(IEnumerable<ControlPoint> points)
        {
            var sb = new StringBuilder();
            sb.Append("Point Name,X,Y,Z,Description\n");
            foreach (var p in points)
                sb.Append($"{CsvField(p.Name)},{p.Easting:F3},{p.Northing:F3},{p.Elevation:F3},{CsvField(p.Description)}\n");
            return sb.ToString();
        }

        private static string CsvField(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        // ----------------------------------------------------------------
        // Next auto-name helper
        // ----------------------------------------------------------------

        public static string NextName(Database db)
        {
            var existing = GetAll(db);
            return $"CP{existing.Count + 1}";
        }
    }
}
