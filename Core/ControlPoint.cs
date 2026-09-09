using Autodesk.AutoCAD.DatabaseServices;

namespace DustyAutoCAD.Core
{
    public class ControlPoint
    {
        public string   Name        { get; set; } = string.Empty;
        public double   Northing    { get; set; }  // Y
        public double   Easting     { get; set; }  // X
        public double   Elevation   { get; set; }  // Z
        public string   Description { get; set; } = string.Empty;
        public ObjectId BlockRefId  { get; set; }

        public string NorthingDisplay => $"{Northing:F3}";
        public string EastingDisplay  => $"{Easting:F3}";
        public string ElevDisplay     => $"{Elevation:F3}";
    }
}
