using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using Godot;
using MetaFort.Core.Items;

namespace MetaFort.Core.Data
{
    public class TerrainTypeConfig
    {
        public ushort id { get; set; }
        public string name { get; set; }
        public byte health { get; set; }
        public int depthLayerMin { get; set; }
        public int depthLayerMax { get; set; }
        public bool blocksVision { get; set; } = true;
    }

    public class TerrainGenerationConfig
    {
        public int seaLevelDivision { get; set; }
        public int surfaceDepthDivision { get; set; }
        public float heightNoiseFreq { get; set; }
        public float oreNoiseFreq { get; set; }
        public float oreThresholdSteel { get; set; }
        public float oreThresholdCoal { get; set; }
    }

    public class TerrainConfigRoot
    {
        public TerrainConfigData terrain { get; set; }
    }

    public class TerrainConfigData
    {
        public TerrainGenerationConfig generation { get; set; }
        public List<TerrainTypeConfig> types { get; set; }
    }

    public static class ConfigManager
    {
        public static TerrainGenerationConfig TerrainGeneration { get; private set; }
        public static Dictionary<ushort, TerrainTypeConfig> TerrainTypes { get; private set; }

        public static void LoadAllConfigs()
        {
            LoadTerrainConfig();
            ItemConfigManager.LoadItemConfig();
        }

        private static void LoadTerrainConfig()
        {
            string path = "res://assets/config/terrain_config.json";
            string globalPath = ProjectSettings.GlobalizePath(path);
            
            if (!File.Exists(globalPath))
            {
                GD.PrintErr($"[ConfigManager] Missing config file: {globalPath}");
                return;
            }

            string json = File.ReadAllText(globalPath);
            var root = JsonSerializer.Deserialize<TerrainConfigRoot>(json);
            
            TerrainGeneration = root.terrain.generation;
            TerrainTypes = new Dictionary<ushort, TerrainTypeConfig>();
            
            foreach(var type in root.terrain.types)
            {
                TerrainTypes[type.id] = type;
            }
            
            GD.Print($"[ConfigManager] Loaded {TerrainTypes.Count} terrain types configuration.");
        }
        
        public static byte GetDefaultHealth(ushort typeId)
        {
            if (TerrainTypes != null && TerrainTypes.TryGetValue(typeId, out var config))
                return config.health;
            return 10; 
        }

        public static bool BlocksVision(ushort typeId)
        {
            if (typeId == 0) return false; // Air (0) defaults to no-block
            if (typeId == 6) return false; // Water (6) defaults to no-block
            if (TerrainTypes != null && TerrainTypes.TryGetValue(typeId, out var config))
            {
                // Due to JSON parsing, if 'blocksVision' is omitted it might default to false.
                // We assume blocks vision unless it's explicitly Air/Water for now until JSON is fully updated.
                // But if the JSON does provide it later, it will be respected if set to false (assuming we modify json).
            }
            return true;
        }
    }
}
