using System.IO;
using System.Text.Json;
using static System.Environment;

namespace DustyAutoCAD.Core
{
    public static class LayerPresetStore
    {
        private static string PresetFolder =>
            Path.Combine(GetFolderPath(SpecialFolder.ApplicationData),
                         "BIMOne", "DustyAutoCAD", "Presets");

        public static List<string> GetPresetNames()
        {
            if (!Directory.Exists(PresetFolder)) return new();
            return Directory.GetFiles(PresetFolder, "*.json")
                            .Select(f => Path.GetFileNameWithoutExtension(f))
                            .OrderBy(n => n)
                            .ToList();
        }

        public static LayerPreset? Load(string name)
        {
            var path = Path.Combine(PresetFolder, name + ".json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LayerPreset>(json);
        }

        public static void Save(LayerPreset preset)
        {
            Directory.CreateDirectory(PresetFolder);
            var path = Path.Combine(PresetFolder, preset.Name + ".json");
            var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static void Delete(string name)
        {
            var path = Path.Combine(PresetFolder, name + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
