using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MetaFort.Core.Items
{
    public static class ItemConfigManager
    {
        public static Dictionary<string, ItemDefinition> ItemDefinitions { get; private set; } = new Dictionary<string, ItemDefinition>();

        public static void LoadItemConfig()
        {
            string path = "res://assets/config/item_config.json";
            string globalPath = ProjectSettings.GlobalizePath(path);

            if (!File.Exists(globalPath))
            {
                GD.PrintErr($"[ItemConfigManager] Missing item config file: {globalPath}");
                return;
            }

            string json = File.ReadAllText(globalPath);
            var root = JsonSerializer.Deserialize<ItemConfigRoot>(json);

            ItemDefinitions.Clear();
            if (root?.items != null)
            {
                foreach (var item in root.items)
                {
                    if (string.IsNullOrEmpty(item.id)) continue;
                    ItemDefinitions[item.id] = item;
                }
            }

            GD.Print($"[ItemConfigManager] Loaded {ItemDefinitions.Count} item definitions.");
        }

        public static bool TryGetItem(string itemId, out ItemDefinition definition)
        {
            return ItemDefinitions.TryGetValue(itemId, out definition);
        }
    }
}
