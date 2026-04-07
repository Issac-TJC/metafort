using Godot;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace MetaFort.Core.Items
{
    public static class ItemConfigManager
    {
        public static Dictionary<string, ItemDefinition> ItemDefinitions { get; private set; } = new Dictionary<string, ItemDefinition>();
        public static string LastLoadError { get; private set; } = string.Empty;
        public static bool IsLoadedSuccessfully { get; private set; }

        public static bool LoadItemConfig()
        {
            LastLoadError = string.Empty;
            IsLoadedSuccessfully = false;

            string path = "res://assets/config/item_config.json";
            string globalPath = ProjectSettings.GlobalizePath(path);

            if (!File.Exists(globalPath))
            {
                LastLoadError = $"Missing item config file: {globalPath}";
                GD.PrintErr($"[ItemConfigManager] {LastLoadError}");
                ItemDefinitions.Clear();
                return false;
            }

            try
            {
                string json = File.ReadAllText(globalPath);
                var root = JsonSerializer.Deserialize<ItemConfigRoot>(json);

                ItemDefinitions.Clear();
                if (root?.items == null)
                {
                    LastLoadError = "Item config is missing the 'items' array.";
                    GD.PrintErr($"[ItemConfigManager] {LastLoadError}");
                    return false;
                }

                foreach (var item in root.items)
                {
                    if (!TryValidateItem(item, out string validationError))
                    {
                        LastLoadError = validationError;
                        GD.PrintErr($"[ItemConfigManager] {LastLoadError}");
                        ItemDefinitions.Clear();
                        return false;
                    }

                    ItemDefinitions[item.id] = item;
                }

                IsLoadedSuccessfully = true;
                GD.Print($"[ItemConfigManager] Loaded {ItemDefinitions.Count} item definitions.");
                return true;
            }
            catch (JsonException ex)
            {
                LastLoadError = $"Invalid item config JSON: {ex.Message}";
                GD.PrintErr($"[ItemConfigManager] {LastLoadError}");
            }
            catch (System.Exception ex)
            {
                LastLoadError = $"Failed to load item config: {ex.Message}";
                GD.PrintErr($"[ItemConfigManager] {LastLoadError}");
            }

            ItemDefinitions.Clear();
            return false;
        }

        public static bool TryGetItem(string itemId, out ItemDefinition definition)
        {
            return ItemDefinitions.TryGetValue(itemId, out definition);
        }

        public static IEnumerable<ItemDefinition> GetBuildableItems()
        {
            return ItemDefinitions.Values
                .Where(item => item.isBuildable)
                .OrderBy(item => item.ResolveBuildCategory())
                .ThenBy(item => item.ResolvePlannerLabel());
        }

        private static bool TryValidateItem(ItemDefinition item, out string error)
        {
            error = string.Empty;
            if (item == null)
            {
                error = "Encountered a null item definition.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.id))
            {
                error = "Item definition is missing 'id'.";
                return false;
            }

            if (ItemDefinitions.ContainsKey(item.id))
            {
                error = $"Duplicate item definition id '{item.id}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.displayName))
            {
                error = $"Item '{item.id}' is missing 'displayName'.";
                return false;
            }

            if (item.footprintX <= 0 || item.footprintY <= 0)
            {
                error = $"Item '{item.id}' has invalid footprint.";
                return false;
            }

            if (item.maxDurability < 0)
            {
                error = $"Item '{item.id}' has invalid maxDurability.";
                return false;
            }

            if (item.requiredMaterials == null)
            {
                item.requiredMaterials = new List<ItemMaterialRequirement>();
            }

            if (item.occupiedOffsets == null)
            {
                item.occupiedOffsets = new List<OccupiedCellOffset>();
            }

            return true;
        }
    }
}
