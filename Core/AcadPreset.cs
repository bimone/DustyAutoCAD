using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DustyAutoCAD.Core
{
    public class AcadPreset
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("preset_name")]
        public string PresetName { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "autocad";

        [JsonPropertyName("mappings")]
        public List<AcadLayerMapping> Mappings { get; set; } = new();

        public static string DefaultPresetDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DustyExports", "Presets");

        public static AcadPreset Load(string path)
        {
            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Empty or invalid JSON file.");

            // Detect preset type by checking for "mappings" (autocad) vs "mapping" (revit)
            if (root["mappings"] != null)
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<AcadPreset>(json, opts)
                    ?? throw new InvalidOperationException("Failed to deserialize AutoCAD preset.");
            }
            else if (root["mapping"] != null)
            {
                return FromRevitPreset(json);
            }

            throw new InvalidOperationException("Unrecognized preset format - expected 'mappings' or 'mapping' key.");
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
        }

        public static AcadPreset FromRevitPreset(string json)
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var revit = JsonSerializer.Deserialize<RevitPresetRoot>(json, opts)
                ?? throw new InvalidOperationException("Failed to deserialize Revit preset.");

            var preset = new AcadPreset
            {
                Version = "1.0",
                PresetName = revit.PresetName,
                Source = "autocad",
                Mappings = new List<AcadLayerMapping>()
            };

            foreach (var discipline in revit.Mapping)
            {
                if (!discipline.Enabled) continue;
                foreach (var cat in discipline.Categories)
                {
                    if (!cat.Enabled) continue;
                    preset.Mappings.Add(new AcadLayerMapping
                    {
                        Source = cat.Name,
                        Target = cat.Layer,
                        Linestyle = cat.Linestyle,
                        Intent = cat.Intent
                    });
                }
            }

            return preset;
        }

        public static List<string> ListPresets()
        {
            var dir = DefaultPresetDir;
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.json").ToList();
        }

        // Internal models for Revit preset deserialization
        private class RevitPresetRoot
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;

            [JsonPropertyName("preset_name")]
            public string PresetName { get; set; } = string.Empty;

            [JsonPropertyName("source")]
            public string Source { get; set; } = string.Empty;

            [JsonPropertyName("mapping")]
            public List<RevitDiscipline> Mapping { get; set; } = new();
        }

        private class RevitDiscipline
        {
            [JsonPropertyName("discipline")]
            public string Discipline { get; set; } = string.Empty;

            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; }

            [JsonPropertyName("categories")]
            public List<RevitCategory> Categories { get; set; } = new();
        }

        private class RevitCategory
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("layer")]
            public string Layer { get; set; } = string.Empty;

            [JsonPropertyName("intent")]
            public string Intent { get; set; } = string.Empty;

            [JsonPropertyName("linestyle")]
            public string Linestyle { get; set; } = string.Empty;

            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; }
        }
    }

    public class AcadLayerMapping
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public string Target { get; set; } = string.Empty;

        [JsonPropertyName("linestyle")]
        public string Linestyle { get; set; } = "Solid";

        [JsonPropertyName("intent")]
        public string Intent { get; set; } = "printable";

        [JsonPropertyName("include")]
        public bool Include { get; set; } = true;
    }
}
