namespace DustyAutoCAD.Core
{
    public class LayerPreset
    {
        public string Name { get; set; } = "";
        public Dictionary<string, bool> Layers { get; set; } = new(); // layerName → isVisible
    }
}
