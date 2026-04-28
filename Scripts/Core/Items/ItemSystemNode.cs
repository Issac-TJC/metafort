using Godot;
using System;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Spatial;
using System.Collections.Generic;
using MetaFort.Core.Systems;

namespace MetaFort.Core.Items
{
    public partial class ItemSystemNode : Node
    {
        public struct ItemEnvironmentalProtection
        {
            public float ExposureMultiplier;
            public float HumidityBuffer;
            public float TemperatureBuffer;
            public float LightningShieldFactor;
            public bool BlocksRain;
            public string ProtectionTag;
        }

        public sealed class PlacedItemRecord
        {
            public string ItemId;
            public int Durability;
            public uint OwnerEntityId;
            public float Condition;
            public float AccumulatedWear;
            public float Wetness;
            public float TemperatureStress;
            public int LastProcessedDay;
            public int LastProcessedHour;
            public float ExposureMultiplier = 1.0f;
            public float HumidityBuffer;
            public float TemperatureBuffer;
            public float LightningShieldFactor;
            public bool BlocksRain;
            public string ProtectionTag = string.Empty;
            public bool IsBroken;
        }

        [Export]
        public NodePath CoreSourcePath { get; set; }

        private IEventBus _eventBus;
        private IEntityManager _entityManager;
        private IMapManager _mapManager;
        private bool _initialized;

        // Runtime inventory storage: actor -> (itemId -> count)
        private readonly Dictionary<uint, Dictionary<string, int>> _inventories = new Dictionary<uint, Dictionary<string, int>>();

        // Runtime world placed item anchors and occupied cells
        private readonly Dictionary<GridPosition, PlacedItemRecord> _placedAnchors = new Dictionary<GridPosition, PlacedItemRecord>();
        private readonly Dictionary<GridPosition, GridPosition> _occupiedToAnchor = new Dictionary<GridPosition, GridPosition>();

        public override void _Ready()
        {
            if (_initialized) return;

            if (CoreSourcePath == null || CoreSourcePath.IsEmpty)
            {
                GD.PrintErr($"[ItemSystem] Missing CoreSourcePath on node '{GetPath()}'.");
                return;
            }

            Node source = GetNodeOrNull(CoreSourcePath);
            if (source is not MetaFort.GameEntry gameEntry)
            {
                GD.PrintErr($"[ItemSystem] CoreSourcePath '{CoreSourcePath}' must point to a GameEntry node.");
                return;
            }

            if (gameEntry.EventBus == null || gameEntry.EntityManager == null || gameEntry.MapManager == null)
            {
                GD.PrintErr($"[ItemSystem] GameEntry at '{CoreSourcePath}' is missing required core systems.");
                return;
            }

            Initialize(gameEntry.EventBus, gameEntry.EntityManager, gameEntry.MapManager);
        }

        public override void _ExitTree()
        {
            if (_initialized && _eventBus != null)
            {
                _eventBus.Unsubscribe<ItemCommandEvent>(OnItemCommand);
                _eventBus.Unsubscribe<ContextActionSelectedEvent>(OnContextActionSelected);
            }

            _initialized = false;
            _eventBus = null;
            _entityManager = null;
            _mapManager = null;
        }

        public void Initialize(IEventBus eventBus, IEntityManager entityManager, IMapManager mapManager)
        {
            if (_initialized) return;
            if (eventBus == null || entityManager == null || mapManager == null)
            {
                GD.PrintErr("[ItemSystem] Initialize failed because one or more dependencies are null.");
                return;
            }

            _eventBus = eventBus;
            _entityManager = entityManager;
            _mapManager = mapManager;
            ItemBehaviorRegistry.EnsureBuiltInsRegistered();

            _eventBus.Subscribe<ItemCommandEvent>(OnItemCommand);
            _eventBus.Subscribe<ContextActionSelectedEvent>(OnContextActionSelected);
            _initialized = true;

            GD.Print("[ItemSystem] Initialized and subscribed to item/context events.");
        }

        private void EnsureInventory(uint actorEntityId)
        {
            if (_inventories.ContainsKey(actorEntityId)) return;

            _inventories[actorEntityId] = new Dictionary<string, int>
            {
                ["res_wood"] = 20,
                ["res_stone"] = 20,
                ["res_metal"] = 20,
                ["res_coal"] = 12,
                ["build_ladder_wood"] = 0,
                ["debug_bell"] = 0,
                ["build_blast_furnace"] = 0,
                ["build_generator"] = 0,
                ["build_refinery"] = 0
            };

            if (_entityManager != null && _entityManager.IsAlive(actorEntityId) && !_entityManager.HasComponent<InventoryTagComponent>(actorEntityId))
            {
                _entityManager.AddComponent(actorEntityId, new InventoryTagComponent());
            }

            GD.Print($"[ItemSystem] Created starter inventory for actor={actorEntityId}. res_wood=20");
        }

        private int GetInventoryCount(uint actorEntityId, string itemId)
        {
            EnsureInventory(actorEntityId);
            var inv = _inventories[actorEntityId];
            return inv.TryGetValue(itemId, out int count) ? count : 0;
        }

        private bool TryConsumeMaterials(uint actorEntityId, ItemDefinition def)
        {
            EnsureInventory(actorEntityId);
            var inv = _inventories[actorEntityId];

            foreach (var requirement in def.requiredMaterials)
            {
                if (!inv.TryGetValue(requirement.itemId, out int have) || have < requirement.count)
                {
                    GD.Print($"[ItemSystem] Material check failed for {def.id}. Need {requirement.itemId} x{requirement.count}, have {have}.");
                    return false;
                }
            }

            foreach (var requirement in def.requiredMaterials)
            {
                inv[requirement.itemId] -= requirement.count;
            }

            return true;
        }

        private void AddItemToInventory(uint actorEntityId, string itemId, int count)
        {
            EnsureInventory(actorEntityId);
            var inv = _inventories[actorEntityId];

            if (!inv.ContainsKey(itemId)) inv[itemId] = 0;
            inv[itemId] += count;

            GD.Print($"[ItemSystem] Inventory +{count} {itemId} for actor={actorEntityId}. total={inv[itemId]}");
        }

        private bool RemoveItemFromInventory(uint actorEntityId, string itemId, int count)
        {
            EnsureInventory(actorEntityId);
            var inv = _inventories[actorEntityId];

            if (!inv.TryGetValue(itemId, out int have) || have < count)
            {
                GD.Print($"[ItemSystem] Inventory remove failed actor={actorEntityId}, item={itemId}, need={count}, have={have}");
                return false;
            }

            inv[itemId] -= count;
            return true;
        }

        private List<GridPosition> GetOccupiedCells(GridPosition anchor, ItemDefinition def)
        {
            var cells = new List<GridPosition>();

            if (def.occupiedOffsets != null && def.occupiedOffsets.Count > 0)
            {
                foreach (var offset in def.occupiedOffsets)
                {
                    cells.Add(new GridPosition(anchor.X + offset.x, anchor.Y + offset.y, anchor.Z + offset.z));
                }
                return cells;
            }

            for (int ox = 0; ox < def.footprintX; ox++)
            {
                for (int oy = 0; oy < def.footprintY; oy++)
                {
                    cells.Add(new GridPosition(anchor.X + ox, anchor.Y + oy, anchor.Z));
                }
            }

            return cells;
        }

        public bool CanCraft(uint actorEntityId, string itemId)
        {
            if (!ItemConfigManager.TryGetItem(itemId, out var def)) return false;
            EnsureInventory(actorEntityId);
            var inv = _inventories[actorEntityId];

            foreach (var requirement in def.requiredMaterials)
            {
                if (!inv.TryGetValue(requirement.itemId, out int have) || have < requirement.count)
                    return false;
            }

            return true;
        }

        public bool CanPlaceItem(uint actorEntityId, string itemId, GridPosition anchor)
        {
            return CanPlaceItemInternal(actorEntityId, itemId, anchor, requireInventoryItem: true);
        }

        public bool CanPlaceItemDefinition(string itemId, GridPosition anchor)
        {
            return CanPlaceItemInternal(0, itemId, anchor, requireInventoryItem: false);
        }

        public bool CanBuildFromBlueprint(uint actorEntityId, string itemId, GridPosition anchor)
        {
            if (!ItemConfigManager.TryGetItem(itemId, out ItemDefinition def)) return false;
            if (!CanPlaceItemDefinition(itemId, anchor)) return false;
            return HasMaterials(actorEntityId, def);
        }

        public List<GridPosition> GetOccupiedCellsForItem(string itemId, GridPosition anchor)
        {
            if (!ItemConfigManager.TryGetItem(itemId, out ItemDefinition def))
            {
                return new List<GridPosition>();
            }

            return GetOccupiedCells(anchor, def);
        }

        public bool TryCompleteBlueprintBuild(uint actorEntityId, string itemId, GridPosition anchor, out string message)
        {
            message = string.Empty;
            if (!ItemConfigManager.TryGetItem(itemId, out ItemDefinition def))
            {
                message = "Unknown build item.";
                return false;
            }

            if (!CanPlaceItemDefinition(itemId, anchor))
            {
                message = "Build location is no longer valid.";
                return false;
            }

            if (!TryConsumeMaterials(actorEntityId, def))
            {
                message = "Not enough materials to build.";
                return false;
            }

            return TryPlaceConstructedItem(actorEntityId, itemId, anchor, out message);
        }

        public bool TryPlaceConstructedItem(uint actorEntityId, string itemId, GridPosition anchor, out string message)
        {
            message = string.Empty;
            if (!ItemConfigManager.TryGetItem(itemId, out ItemDefinition def))
            {
                message = "Unknown build item.";
                return false;
            }

            if (!CanPlaceItemDefinition(itemId, anchor))
            {
                message = "Build location is no longer valid.";
                return false;
            }

            PlaceItemRecord(actorEntityId, itemId, anchor, def);
            message = $"Built {def.displayName}.";
            return true;
        }

        private bool CanPlaceItemInternal(uint actorEntityId, string itemId, GridPosition anchor, bool requireInventoryItem)
        {
            if (!ItemConfigManager.TryGetItem(itemId, out var def)) return false;
            if (requireInventoryItem && GetInventoryCount(actorEntityId, itemId) < 1) return false;

            var flags = def.GetPlacementFlags();
            var occupiedCells = GetOccupiedCells(anchor, def);

            for (int i = 0; i < occupiedCells.Count; i++)
            {
                var cell = occupiedCells[i];
                if (!_mapManager.IsWithinBounds(cell)) return false;
                if (_occupiedToAnchor.ContainsKey(cell)) return false;

                var tile = _mapManager.GetTile(cell.X, cell.Y, cell.Z);
                if (tile.Type != TerrainType.Air && !flags.HasFlag(PlacementRuleFlags.AllowOnAir)) return false;

                if (flags.HasFlag(PlacementRuleFlags.RequireWalkableGround))
                {
                    if (!_mapManager.IsWithinBounds(cell.X, cell.Y, cell.Z - 1)) return false;
                    var below = _mapManager.GetTile(cell.X, cell.Y, cell.Z - 1);
                    if (below.Type == TerrainType.Air || below.Type == TerrainType.Water)
                        return false;
                }

                if (flags.HasFlag(PlacementRuleFlags.NeedsAdjacentSolid))
                {
                    bool hasSolidNeighbor = false;
                    var neighbors = new (int x, int y, int z)[]
                    {
                        (cell.X + 1, cell.Y, cell.Z), (cell.X - 1, cell.Y, cell.Z),
                        (cell.X, cell.Y + 1, cell.Z), (cell.X, cell.Y - 1, cell.Z)
                    };

                    foreach (var n in neighbors)
                    {
                        if (!_mapManager.IsWithinBounds(n.x, n.y, n.z)) continue;
                        var nTile = _mapManager.GetTile(n.x, n.y, n.z);
                        if (nTile.Type != TerrainType.Air && nTile.Type != TerrainType.Water)
                        {
                            hasSolidNeighbor = true;
                            break;
                        }
                    }

                    if (!hasSolidNeighbor) return false;
                }
            }

            return true;
        }

        private bool HasMaterials(uint actorEntityId, ItemDefinition def)
        {
            EnsureInventory(actorEntityId);
            var inv = _inventories[actorEntityId];

            foreach (var requirement in def.requiredMaterials)
            {
                if (!inv.TryGetValue(requirement.itemId, out int have) || have < requirement.count)
                    return false;
            }

            return true;
        }

        public bool HasInteractableAt(GridPosition pos)
        {
            if (!_occupiedToAnchor.TryGetValue(pos, out var anchor)) return false;
            if (!_placedAnchors.TryGetValue(anchor, out var record)) return false;

            return ItemConfigManager.TryGetItem(record.ItemId, out var def) && !string.IsNullOrEmpty(def.interactionScript);
        }

        private bool TryGetPlacedRecord(GridPosition at, out GridPosition anchor, out PlacedItemRecord record)
        {
            anchor = default;
            record = null;

            if (!_occupiedToAnchor.TryGetValue(at, out anchor)) return false;
            return _placedAnchors.TryGetValue(anchor, out record);
        }

        public IEnumerable<KeyValuePair<GridPosition, PlacedItemRecord>> EnumeratePlacedItems()
        {
            return _placedAnchors;
        }

        public bool TryGetPlacedItemRecord(GridPosition anchor, out PlacedItemRecord record)
        {
            return _placedAnchors.TryGetValue(anchor, out record);
        }

        public bool TryGetPlacedItemAnchor(GridPosition at, out GridPosition anchor)
        {
            return _occupiedToAnchor.TryGetValue(at, out anchor);
        }

        public bool TryRemovePlacedItem(GridPosition at, uint removedByActorId, out string itemId)
        {
            itemId = string.Empty;
            if (!TryGetPlacedRecord(at, out GridPosition anchor, out PlacedItemRecord record))
            {
                return false;
            }

            itemId = record.ItemId;
            if (!ItemConfigManager.TryGetItem(record.ItemId, out ItemDefinition definition))
            {
                return false;
            }

            List<GridPosition> occupiedCells = GetOccupiedCells(anchor, definition);
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                _occupiedToAnchor.Remove(occupiedCells[i]);
            }

            _placedAnchors.Remove(anchor);

            var evt = new PlacedItemRemovedEvent
            {
                ItemId = record.ItemId,
                Anchor = anchor,
                RemovedByActorId = removedByActorId
            };
            _eventBus.Publish(ref evt);
            return true;
        }

        public bool SetItemEnvironmentalProtection(GridPosition anchor, ItemEnvironmentalProtection protection)
        {
            if (!_placedAnchors.TryGetValue(anchor, out var record))
                return false;

            record.ExposureMultiplier = Math.Clamp(protection.ExposureMultiplier <= 0f ? 1.0f : protection.ExposureMultiplier, 0.05f, 4.0f);
            record.HumidityBuffer = Math.Clamp(protection.HumidityBuffer, 0f, 1f);
            record.TemperatureBuffer = Math.Clamp(protection.TemperatureBuffer, 0f, 1f);
            record.LightningShieldFactor = Math.Clamp(protection.LightningShieldFactor, 0f, 1f);
            record.BlocksRain = protection.BlocksRain;
            record.ProtectionTag = protection.ProtectionTag ?? string.Empty;
            return true;
        }

        public bool ResetItemEnvironmentalProtection(GridPosition anchor)
        {
            if (!_placedAnchors.TryGetValue(anchor, out var record))
                return false;

            record.ExposureMultiplier = 1.0f;
            record.HumidityBuffer = 0f;
            record.TemperatureBuffer = 0f;
            record.LightningShieldFactor = 0f;
            record.BlocksRain = false;
            record.ProtectionTag = string.Empty;
            return true;
        }

        private void PublishResult(bool success, string message, uint actorId, string itemId, Vector3I target)
        {
            GD.Print($"[ItemSystem][Result] success={success} actor={actorId} item={itemId} target={target} msg={message}");
            var result = new ItemCommandResultEvent
            {
                Success = success,
                Message = message,
                ActorEntityId = actorId,
                ItemId = itemId,
                Target = target
            };
            _eventBus.Publish(ref result);
        }

        private void OnItemCommand(ref ItemCommandEvent evt)
        {
            GD.Print($"[ItemSystem] Received command type={evt.Type} actor={evt.ActorEntityId} item={evt.ItemId} target={evt.Target}");

            switch (evt.Type)
            {
                case ItemCommandType.Craft:
                    HandleCraft(evt);
                    break;
                case ItemCommandType.Place:
                    HandlePlace(evt);
                    break;
                case ItemCommandType.Use:
                    HandleUse(evt);
                    break;
                case ItemCommandType.Remove:
                    PublishResult(false, "Remove not implemented in MVP.", evt.ActorEntityId, evt.ItemId, evt.Target);
                    break;
            }
        }

        private void HandleCraft(ItemCommandEvent evt)
        {
            if (!ItemConfigManager.TryGetItem(evt.ItemId, out var def))
            {
                PublishResult(false, "Unknown item id for crafting.", evt.ActorEntityId, evt.ItemId, evt.Target);
                return;
            }

            if (!TryConsumeMaterials(evt.ActorEntityId, def))
            {
                PublishResult(false, "Not enough materials.", evt.ActorEntityId, evt.ItemId, evt.Target);
                return;
            }

            AddItemToInventory(evt.ActorEntityId, evt.ItemId, 1);
            PublishResult(true, $"Crafted {def.displayName}.", evt.ActorEntityId, evt.ItemId, evt.Target);
        }

        private void HandlePlace(ItemCommandEvent evt)
        {
            var anchor = new GridPosition(evt.Target.X, evt.Target.Y, evt.Target.Z);
            if (!ItemConfigManager.TryGetItem(evt.ItemId, out var def))
            {
                PublishResult(false, "Unknown item id for placement.", evt.ActorEntityId, evt.ItemId, evt.Target);
                return;
            }

            if (!CanPlaceItem(evt.ActorEntityId, evt.ItemId, anchor))
            {
                PublishResult(false, "Placement rule check failed.", evt.ActorEntityId, evt.ItemId, evt.Target);
                return;
            }

            if (!RemoveItemFromInventory(evt.ActorEntityId, evt.ItemId, 1))
            {
                PublishResult(false, "No item in inventory for placement.", evt.ActorEntityId, evt.ItemId, evt.Target);
                return;
            }

            PlaceItemRecord(evt.ActorEntityId, evt.ItemId, anchor, def);
            PublishResult(true, $"Placed {def.displayName}.", evt.ActorEntityId, evt.ItemId, evt.Target);
        }

        private void HandleUse(ItemCommandEvent evt)
        {
            var target = new GridPosition(evt.Target.X, evt.Target.Y, evt.Target.Z);
            if (!TryGetPlacedRecord(target, out var anchor, out var record))
            {
                PublishResult(false, "No placed item at target.", evt.ActorEntityId, evt.ItemId, evt.Target);
                return;
            }

            if (!ItemConfigManager.TryGetItem(record.ItemId, out var def))
            {
                PublishResult(false, "Placed item definition missing.", evt.ActorEntityId, evt.ItemId, evt.Target);
                return;
            }

            if (record.IsBroken || record.Condition <= def.ResolveFailureThreshold())
            {
                PublishResult(false, $"{def.displayName} is too damaged to use.", evt.ActorEntityId, record.ItemId, evt.Target);
                return;
            }

            string resultMessage = $"Used {def.displayName}.";
            if (!string.IsNullOrWhiteSpace(def.interactionScript))
            {
                if (!ItemBehaviorRegistry.TryGet(def.interactionScript, out IItemBehavior behavior))
                {
                    PublishResult(false, $"Behavior '{def.interactionScript}' is not registered.", evt.ActorEntityId, record.ItemId, evt.Target);
                    return;
                }

                ItemInteractionContext context = new ItemInteractionContext
                {
                    ActorEntityId = evt.ActorEntityId,
                    Target = target,
                    Anchor = anchor,
                    Definition = def,
                    Record = record
                };

                if (!behavior.TryUse(context, out resultMessage))
                {
                    PublishResult(false, resultMessage, evt.ActorEntityId, record.ItemId, evt.Target);
                    return;
                }
            }

            PublishResult(true, resultMessage, evt.ActorEntityId, record.ItemId, evt.Target);
        }

        private void OnContextActionSelected(ref ContextActionSelectedEvent evt)
        {
            var cmd = new ItemCommandEvent
            {
                ActorEntityId = evt.ActorEntityId,
                ItemId = evt.Selected.ItemId,
                Target = evt.Selected.Target
            };

            switch (evt.Selected.Type)
            {
                case ContextActionType.Craft:
                    cmd.Type = ItemCommandType.Craft;
                    _eventBus.Publish(ref cmd);
                    break;
                case ContextActionType.Place:
                    cmd.Type = ItemCommandType.Place;
                    _eventBus.Publish(ref cmd);
                    break;
                case ContextActionType.Use:
                    cmd.Type = ItemCommandType.Use;
                    _eventBus.Publish(ref cmd);
                    break;
            }
        }

        public void PrintInventory(uint actorEntityId)
        {
            EnsureInventory(actorEntityId);
            var inv = _inventories[actorEntityId];
            GD.Print($"[ItemSystem] ===== Inventory actor={actorEntityId} =====");
            foreach (var kv in inv)
            {
                GD.Print($"[ItemSystem] {kv.Key} -> {kv.Value}");
            }
        }

        public void ApplyWeatherTick(in WeatherState weather, in WeatherState? previousWeather, int day, int hour)
        {
            if (!_initialized || _eventBus == null || _placedAnchors.Count == 0)
                return;

            List<GridPosition> anchors = new List<GridPosition>(_placedAnchors.Keys);
            foreach (GridPosition anchor in anchors)
            {
                if (!_placedAnchors.TryGetValue(anchor, out var record))
                    continue;

                ApplyWeatherTickToItem(anchor, record, weather, previousWeather, day, hour);
            }
        }

        public void ApplyLightningStrike(in LightningStrikeEvent strike, int day, int hour)
        {
            if (!_initialized || _eventBus == null)
                return;

            if (!TryFindLightningTarget(strike.Position, out GridPosition anchor))
                return;

            if (!_placedAnchors.TryGetValue(anchor, out var record))
                return;

            if (!ItemConfigManager.TryGetItem(record.ItemId, out ItemDefinition def))
                return;

            float shield = 1f - Math.Clamp(record.LightningShieldFactor, 0f, 1f);
            float lightningWear = strike.Power * def.ResolveLightningSensitivity() * def.ResolveWeatherSensitivity() * 100f * shield;
            if (def.GetDecayMode() == ItemDecayMode.ElectricalFragile)
            {
                lightningWear *= 1.35f;
            }

            ApplyWear(anchor, record, def, lightningWear, day, hour, ItemDamageSourceType.Lightning);

            if (!record.IsBroken && def.GetDecayMode() == ItemDecayMode.ElectricalFragile && strike.Power * shield > 0.85f)
            {
                ForceBreakItem(anchor, record, def, day, hour, ItemDamageSourceType.Lightning);
            }
        }

        private void ApplyWeatherTickToItem(
            GridPosition anchor,
            PlacedItemRecord record,
            in WeatherState weather,
            in WeatherState? previousWeather,
            int day,
            int hour)
        {
            if (!ItemConfigManager.TryGetItem(record.ItemId, out ItemDefinition def))
                return;

            if (record.LastProcessedDay == day && record.LastProcessedHour == hour)
                return;

            float maxCondition = def.ResolveMaxCondition();
            if (record.Condition <= 0f)
            {
                record.IsBroken = true;
                record.LastProcessedDay = day;
                record.LastProcessedHour = hour;
                return;
            }

            float exposure = Math.Clamp(record.ExposureMultiplier, 0.05f, 4.0f);
            float humidityBuffer = Math.Clamp(record.HumidityBuffer, 0f, 1f);
            float temperatureBuffer = Math.Clamp(record.TemperatureBuffer, 0f, 1f);

            float baseAging = def.ResolveBaseDecayRate();

            float wetness = record.Wetness;
            bool isRainy = weather.Type == WeatherType.HeavyRain || weather.Type == WeatherType.Thunderstorm;
            float rainGain = isRainy && !record.BlocksRain ? weather.Intensity * 0.18f * exposure : 0f;
            float ambientHumidityGain = Math.Max(0f, weather.HumidityDelta) * 0.08f * exposure * (1f - humidityBuffer);
            float dryRate = weather.Type == WeatherType.Clear ? 0.16f : 0.05f;
            dryRate += Math.Max(0f, -weather.HumidityDelta) * 0.03f;

            wetness += rainGain + ambientHumidityGain;
            wetness -= dryRate * (0.65f + 0.35f * exposure);
            wetness = Math.Clamp(wetness, 0f, 1f);

            float moistureWear = wetness * def.ResolveMoistureSensitivity() * def.ResolveWeatherSensitivity();

            float effectiveTempDelta = Math.Abs(weather.TemperatureDelta) * (1f - temperatureBuffer);
            float previousTempDelta = previousWeather.HasValue ? previousWeather.Value.TemperatureDelta : weather.TemperatureDelta;
            float thermalShock = Math.Abs(weather.TemperatureDelta - previousTempDelta) * def.ResolveThermalShockSensitivity() * 0.12f;

            float tempStress = record.TemperatureStress * 0.70f + effectiveTempDelta * 0.035f;
            if (weather.Type == WeatherType.Clear)
            {
                tempStress *= 0.90f;
            }
            tempStress = Math.Clamp(tempStress, 0f, 1f);

            float thermalWear = (tempStress * def.ResolveTemperatureSensitivity() * def.ResolveWeatherSensitivity()) + thermalShock;
            if (weather.Type == WeatherType.ColdWave && wetness > 0.40f)
            {
                thermalWear += wetness * def.ResolveMoistureSensitivity() * 0.60f;
            }

            float wearModifier = GetDecayModeModifier(def.GetDecayMode(), wetness, tempStress, weather);
            float conditionRatio = maxCondition <= 0f ? 0f : record.Condition / maxCondition;
            float stateFactor = 1.0f + (1.0f - conditionRatio) * 0.60f + wetness * 0.30f;

            float wearDelta = (baseAging + moistureWear + thermalWear) * exposure * stateFactor * wearModifier;

            record.Wetness = wetness;
            record.TemperatureStress = tempStress;
            record.LastProcessedDay = day;
            record.LastProcessedHour = hour;

            ApplyWear(anchor, record, def, wearDelta, day, hour, ItemDamageSourceType.Weather);
        }

        private float GetDecayModeModifier(ItemDecayMode mode, float wetness, float temperatureStress, in WeatherState weather)
        {
            return mode switch
            {
                ItemDecayMode.WetFirstThenRot => 1.0f + wetness * 0.85f,
                ItemDecayMode.HeatFatigue => 0.95f + temperatureStress * 0.90f,
                ItemDecayMode.RustLike => wetness > 0.25f ? 1.40f : 0.70f,
                ItemDecayMode.ElectricalFragile => 1.05f + wetness * 0.50f + Math.Abs(weather.TemperatureDelta) * 0.03f,
                _ => 1.0f
            };
        }

        private void ApplyWear(
            GridPosition anchor,
            PlacedItemRecord record,
            ItemDefinition def,
            float wearDelta,
            int day,
            int hour,
            ItemDamageSourceType damageSource)
        {
            float clampedDelta = Math.Max(0f, wearDelta);
            float previousCondition = record.Condition;
            float maxCondition = def.ResolveMaxCondition();

            record.AccumulatedWear += clampedDelta;
            record.Condition = Math.Clamp(maxCondition - record.AccumulatedWear, 0f, maxCondition);
            record.Durability = Mathf.RoundToInt(record.Condition);

            bool brokeNow = !record.IsBroken && record.Condition <= def.ResolveFailureThreshold();
            if (record.Condition <= 0f)
            {
                record.IsBroken = true;
            }
            else if (brokeNow)
            {
                record.IsBroken = true;
            }

            if (Math.Abs(record.Condition - previousCondition) > 0.001f)
            {
                var changed = new ItemConditionChangedEvent
                {
                    ItemId = record.ItemId,
                    Anchor = anchor,
                    PreviousCondition = previousCondition,
                    CurrentCondition = record.Condition,
                    WearDelta = clampedDelta,
                    Wetness = record.Wetness,
                    TemperatureStress = record.TemperatureStress,
                    Day = day,
                    Hour = hour,
                    IsBroken = record.IsBroken
                };
                _eventBus.Publish(ref changed);

                var damaged = new ItemWeatherDamagedEvent
                {
                    ItemId = record.ItemId,
                    Anchor = anchor,
                    DamageSource = damageSource,
                    WearDelta = clampedDelta,
                    CurrentCondition = record.Condition,
                    Wetness = record.Wetness,
                    TemperatureStress = record.TemperatureStress,
                    Day = day,
                    Hour = hour
                };
                _eventBus.Publish(ref damaged);
            }

            if (brokeNow)
            {
                var broken = new ItemBrokenEvent
                {
                    ItemId = record.ItemId,
                    Anchor = anchor,
                    DamageSource = damageSource,
                    Day = day,
                    Hour = hour
                };
                _eventBus.Publish(ref broken);
            }
        }

        private void ForceBreakItem(
            GridPosition anchor,
            PlacedItemRecord record,
            ItemDefinition def,
            int day,
            int hour,
            ItemDamageSourceType damageSource)
        {
            if (record.IsBroken)
                return;

            float previousCondition = record.Condition;
            record.Condition = 0f;
            record.AccumulatedWear = def.ResolveMaxCondition();
            record.Durability = 0;
            record.IsBroken = true;

            var changed = new ItemConditionChangedEvent
            {
                ItemId = record.ItemId,
                Anchor = anchor,
                PreviousCondition = previousCondition,
                CurrentCondition = record.Condition,
                WearDelta = previousCondition,
                Wetness = record.Wetness,
                TemperatureStress = record.TemperatureStress,
                Day = day,
                Hour = hour,
                IsBroken = true
            };
            _eventBus.Publish(ref changed);

            var broken = new ItemBrokenEvent
            {
                ItemId = record.ItemId,
                Anchor = anchor,
                DamageSource = damageSource,
                Day = day,
                Hour = hour
            };
            _eventBus.Publish(ref broken);
        }

        private bool TryFindLightningTarget(GridPosition strikePosition, out GridPosition anchor)
        {
            if (_occupiedToAnchor.TryGetValue(strikePosition, out anchor))
                return true;

            bool found = false;
            int bestZ = int.MinValue;
            GridPosition candidateAnchor = default;

            foreach (KeyValuePair<GridPosition, GridPosition> kvp in _occupiedToAnchor)
            {
                GridPosition occupied = kvp.Key;
                if (occupied.X != strikePosition.X || occupied.Y != strikePosition.Y)
                    continue;

                if (occupied.Z > bestZ)
                {
                    bestZ = occupied.Z;
                    candidateAnchor = kvp.Value;
                    found = true;
                }
            }

            anchor = candidateAnchor;
            return found;
        }

        private void PlaceItemRecord(uint actorEntityId, string itemId, GridPosition anchor, ItemDefinition def)
        {
            _placedAnchors[anchor] = new PlacedItemRecord
            {
                ItemId = itemId,
                Durability = def.maxDurability,
                OwnerEntityId = actorEntityId,
                Condition = def.ResolveMaxCondition(),
                AccumulatedWear = 0f,
                Wetness = 0f,
                TemperatureStress = 0f,
                LastProcessedDay = -1,
                LastProcessedHour = -1,
                ExposureMultiplier = 1.0f,
                HumidityBuffer = 0f,
                TemperatureBuffer = 0f,
                LightningShieldFactor = 0f,
                BlocksRain = false,
                ProtectionTag = string.Empty,
                IsBroken = false
            };

            var occupiedCells = GetOccupiedCells(anchor, def);
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                _occupiedToAnchor[occupiedCells[i]] = anchor;
            }

            GD.Print($"[ItemSystem] Placed item '{itemId}' at anchor {anchor}, occupying {occupiedCells.Count} cells.");

            if (_eventBus != null)
            {
                var evt = new PlacedItemAddedEvent
                {
                    ItemId = itemId,
                    Anchor = anchor,
                    OwnerEntityId = actorEntityId,
                    IsBroken = false
                };
                _eventBus.Publish(ref evt);
            }
        }
    }
}
