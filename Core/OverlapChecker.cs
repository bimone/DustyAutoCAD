using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DustyAutoCAD.Core
{
    public enum FixStrategy
    {
        DeleteLine1,   // Line 1 is fully inside Line 2 - delete Line 1
        DeleteLine2,   // Line 2 is fully inside Line 1, or lines are identical - delete Line 2
        SelectOnly,    // Partial overlap - neither can be safely removed without creating a gap
    }

    public class OverlapPair
    {
        public ObjectId    Line1Id       { get; init; }
        public ObjectId    Line2Id       { get; init; }
        public string      Layer1        { get; init; } = string.Empty;
        public string      Layer2        { get; init; } = string.Empty;
        public double      OverlapLength { get; init; }
        public Point3d     ZoomCenter    { get; init; }
        public string      Location      { get; init; } = string.Empty;
        public FixStrategy Strategy      { get; init; }

        public string StrategyLabel => Strategy switch
        {
            FixStrategy.DeleteLine1 => "Delete Line 1 (contained)",
            FixStrategy.DeleteLine2 => "Delete Line 2 (contained)",
            FixStrategy.SelectOnly  => "Select only - partial overlap",
            _                       => string.Empty,
        };

        public bool CanAutoFix => Strategy != FixStrategy.SelectOnly;

        // The ID that will be erased by Fix
        public ObjectId IdToDelete => Strategy == FixStrategy.DeleteLine1 ? Line1Id : Line2Id;
    }

    public static class OverlapChecker
    {
        private const double DistTol = 1e-4;
        private const double AngTol  = 1e-6;

        public static List<OverlapPair> FindOverlaps(Database db)
        {
            var results = new List<OverlapPair>();

            using var tr = db.TransactionManager.StartOpenCloseTransaction();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            var lines = new List<(ObjectId id, Line line)>();
            foreach (ObjectId eid in ms)
            {
                if (tr.GetObject(eid, OpenMode.ForRead) is Line ln)
                    lines.Add((eid, ln));
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var (id1, l1) = lines[i];
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var (id2, l2) = lines[j];
                    if (!BBoxOverlap(l1, l2)) continue;

                    if (TryGetOverlap(l1, l2, out double overlapLen, out Point3d midPt, out FixStrategy strategy))
                    {
                        results.Add(new OverlapPair
                        {
                            Line1Id       = id1,
                            Line2Id       = id2,
                            Layer1        = l1.Layer,
                            Layer2        = l2.Layer,
                            OverlapLength = overlapLen,
                            ZoomCenter    = midPt,
                            Location      = $"({midPt.X:F2}, {midPt.Y:F2})",
                            Strategy      = strategy,
                        });
                    }
                }
            }

            return results;
        }

        private static bool BBoxOverlap(Line a, Line b)
        {
            double ax1 = Math.Min(a.StartPoint.X, a.EndPoint.X) - DistTol;
            double ax2 = Math.Max(a.StartPoint.X, a.EndPoint.X) + DistTol;
            double ay1 = Math.Min(a.StartPoint.Y, a.EndPoint.Y) - DistTol;
            double ay2 = Math.Max(a.StartPoint.Y, a.EndPoint.Y) + DistTol;
            double bx1 = Math.Min(b.StartPoint.X, b.EndPoint.X);
            double bx2 = Math.Max(b.StartPoint.X, b.EndPoint.X);
            double by1 = Math.Min(b.StartPoint.Y, b.EndPoint.Y);
            double by2 = Math.Max(b.StartPoint.Y, b.EndPoint.Y);
            return ax1 <= bx2 && ax2 >= bx1 && ay1 <= by2 && ay2 >= by1;
        }

        private static bool TryGetOverlap(Line a, Line b,
            out double overlapLen, out Point3d midPt, out FixStrategy strategy)
        {
            overlapLen = 0;
            midPt      = Point3d.Origin;
            strategy   = FixStrategy.SelectOnly;

            Vector3d da = a.EndPoint - a.StartPoint;
            double   la = da.Length;
            if (la < DistTol) return false;

            Vector3d dir = da.GetNormal();

            Vector3d db = b.EndPoint - b.StartPoint;
            double   lb = db.Length;
            if (lb < DistTol) return false;

            // Must be parallel
            double cross = dir.X * db.Y - dir.Y * db.X;
            if (Math.Abs(cross) > AngTol * lb) return false;

            // Must be collinear — perpendicular distance from b.Start to line A
            Vector3d ac  = b.StartPoint - a.StartPoint;
            double   perp = dir.X * ac.Y - dir.Y * ac.X;
            if (Math.Abs(perp) > DistTol) return false;

            // Project onto shared direction
            double tA1 = 0,  tA2 = la;
            double tB1 = (b.StartPoint - a.StartPoint).DotProduct(dir);
            double tB2 = (b.EndPoint   - a.StartPoint).DotProduct(dir);
            if (tB1 > tB2) (tB1, tB2) = (tB2, tB1);

            double overlapStart = Math.Max(tA1, tB1);
            double overlapEnd   = Math.Min(tA2, tB2);
            overlapLen          = overlapEnd - overlapStart;

            if (overlapLen <= DistTol) return false;

            double tMid = (overlapStart + overlapEnd) / 2.0;
            midPt = a.StartPoint + dir * tMid;

            // Determine fix strategy
            bool line2InsideLine1 = tB1 >= tA1 - DistTol && tB2 <= tA2 + DistTol;
            bool line1InsideLine2 = tA1 >= tB1 - DistTol && tA2 <= tB2 + DistTol;

            if (line2InsideLine1 && line1InsideLine2)
            {
                // Identical (or within tolerance) — delete Line 2
                strategy = FixStrategy.DeleteLine2;
            }
            else if (line2InsideLine1)
            {
                // Line 2 is fully contained inside Line 1 — safe to delete Line 2
                strategy = FixStrategy.DeleteLine2;
            }
            else if (line1InsideLine2)
            {
                // Line 1 is fully contained inside Line 2 — safe to delete Line 1
                strategy = FixStrategy.DeleteLine1;
            }
            else
            {
                // Partial overlap — deleting either would create a gap
                strategy = FixStrategy.SelectOnly;
            }

            return true;
        }
    }
}
