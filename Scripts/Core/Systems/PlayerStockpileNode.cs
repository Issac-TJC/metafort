using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using MetaFort.Core.Data;
using MetaFort.Core.Enemy;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Items;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class PlayerStockpileNode : Node, IResourceRaidTarget
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public bool SeedDebugResources { get; set; } = true;

        [Export]
        public int DebugStartingWood { get; set; } = 20;

        private IEventBus _eventBus;
        private bool _initialized;
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public override void _Ready()
        {
            if (_initialized)
            {
                return;
            }

            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry == null || gameEntry.EventBus == null)
            {
                GD.PrintErr("[PlayerStockpileNode] Missing GameEntry/EventBus. Node disabled.");
                SetProcess(false);
                return;
            }

            Initialize(gameEntry.EventBus);
        }

        public void Initialize(IEventBus eventBus)
        {
            if (_initialized)
            {
                return;
            }

            _eventBus = eventBus;
            SeedTrackedItems();

            if (SeedDebugResources)
            {
                Add("res_wood", DebugStartingWood);
            }
            else
            {
                PublishChanged();
            }

            _initialized = true;
        }

        public int GetCount(string itemId)
        {
            EnsureTrackedItemsSeeded();
            return _counts.TryGetValue(itemId, out int count) ? count : 0;
        }

        public bool TryConsume(string itemId, int count)
        {
            EnsureTrackedItemsSeeded();
            if (count <= 0)
            {
                return true;
            }

            if (!_counts.TryGetValue(itemId, out int have) || have < count)
            {
                return false;
            }

            _counts[itemId] = have - count;
            PublishChanged();
            return true;
        }

        public bool TryConsumeRequirements(IEnumerable<ItemMaterialRequirement> requirements, out string failureReason)
        {
            EnsureTrackedItemsSeeded();
            failureReason = string.Empty;

            List<ItemMaterialRequirement> materialList = requirements?.ToList() ?? new List<ItemMaterialRequirement>();
            for (int i = 0; i < materialList.Count; i++)
            {
                ItemMaterialRequirement requirement = materialList[i];
                if (string.IsNullOrWhiteSpace(requirement.itemId) || requirement.count <= 0)
                {
                    continue;
                }

                int have = GetCount(requirement.itemId);
                if (have < requirement.count)
                {
                    failureReason = $"Need {requirement.itemId} x{requirement.count}, have {have}.";
                    return false;
                }
            }

            for (int i = 0; i < materialList.Count; i++)
            {
                ItemMaterialRequirement requirement = materialList[i];
                if (string.IsNullOrWhiteSpace(requirement.itemId) || requirement.count <= 0)
                {
                    continue;
                }

                _counts[requirement.itemId] = GetCount(requirement.itemId) - requirement.count;
            }

            PublishChanged();
            return true;
        }

        public bool TryExtract(string itemId, int requested, out int extracted)
        {
            extracted = 0;
            EnsureTrackedItemsSeeded();

            if (requested <= 0 || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            int have = GetCount(itemId);
            extracted = Math.Min(have, requested);
            if (extracted <= 0)
            {
                return false;
            }

            _counts[itemId] = have - extracted;
            PublishChanged();
            return true;
        }

        public void Add(string itemId, int count)
        {
            EnsureTrackedItemsSeeded();
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            {
                return;
            }

            if (!_counts.ContainsKey(itemId))
            {
                _counts[itemId] = 0;
            }

            _counts[itemId] += count;
            PublishChanged();
        }

        public void AddRequirements(IEnumerable<ItemMaterialRequirement> requirements)
        {
            EnsureTrackedItemsSeeded();
            bool changed = false;
            foreach (ItemMaterialRequirement requirement in requirements ?? new List<ItemMaterialRequirement>())
            {
                if (string.IsNullOrWhiteSpace(requirement.itemId) || requirement.count <= 0)
                {
                    continue;
                }

                if (!_counts.ContainsKey(requirement.itemId))
                {
                    _counts[requirement.itemId] = 0;
                }

                _counts[requirement.itemId] += requirement.count;
                changed = true;
            }

            if (changed)
            {
                PublishChanged();
            }
        }

        public void AddTerrainDrops(TerrainType terrainType)
        {
            if (!ConfigManager.TryGetTerrainTypeConfig((ushort)terrainType, out TerrainTypeConfig config) || config == null)
            {
                return;
            }

            bool changed = false;
            changed |= AddDrop(config.primaryDropItemId, config.primaryDropCount);
            changed |= AddDrop(config.secondaryDropItemId, config.secondaryDropCount);

            if (changed)
            {
                PublishChanged();
            }
        }

        public StockpileEntryData[] GetDisplayEntries()
        {
            EnsureTrackedItemsSeeded();
            return BuildEntries();
        }

        private bool AddDrop(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            {
                return false;
            }

            if (!_counts.ContainsKey(itemId))
            {
                _counts[itemId] = 0;
            }

            _counts[itemId] += count;
            return true;
        }

        private void EnsureTrackedItemsSeeded()
        {
            if (_counts.Count == 0)
            {
                SeedTrackedItems();
            }
        }

        private void SeedTrackedItems()
        {
            foreach (ItemDefinition item in ItemConfigManager.GetStockpileItems())
            {
                if (!_counts.ContainsKey(item.id))
                {
                    _counts[item.id] = 0;
                }
            }
        }

        private void PublishChanged()
        {
            if (_eventBus == null)
            {
                return;
            }

            var evt = new StockpileChangedEvent
            {
                Entries = BuildEntries()
            };
            _eventBus.Publish(ref evt);
        }

        private StockpileEntryData[] BuildEntries()
        {
            return ItemConfigManager.GetStockpileItems()
                .Select(item => new StockpileEntryData
                {
                    ItemId = item.id,
                    Label = item.ResolveStockpileLabel(),
                    Count = GetCount(item.id),
                    Order = item.stockpileOrder
                })
                .ToArray();
        }

        private MetaFort.GameEntry ResolveGameEntry()
        {
            if (CoreSourcePath != null && !CoreSourcePath.IsEmpty)
            {
                return GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            }

            return GetNodeOrNull<MetaFort.GameEntry>("..") ?? MetaFort.GameEntry.Instance;
        }
    }
}
