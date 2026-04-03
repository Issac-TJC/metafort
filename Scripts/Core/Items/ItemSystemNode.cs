using Godot;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Spatial;
using System.Collections.Generic;

namespace MetaFort.Core.Items
{
    public partial class ItemSystemNode : Node
    {
        private IEventBus _eventBus;
        private IEntityManager _entityManager;
        private IMapManager _mapManager;

        // Runtime inventory storage: actor -> (itemId -> count)
        private readonly Dictionary<uint, Dictionary<string, int>> _inventories = new Dictionary<uint, Dictionary<string, int>>();

        // Runtime world placed item anchors and occupied cells
        private readonly Dictionary<GridPosition, PlacedItemRecord> _placedAnchors = new Dictionary<GridPosition, PlacedItemRecord>();
        private readonly Dictionary<GridPosition, GridPosition> _occupiedToAnchor = new Dictionary<GridPosition, GridPosition>();

        private struct PlacedItemRecord
        {
            public string ItemId;
            public int Durability;
            public uint OwnerEntityId;
        }

        public void Initialize(IEventBus eventBus, IEntityManager entityManager, IMapManager mapManager)
        {
            _eventBus = eventBus;
            _entityManager = entityManager;
            _mapManager = mapManager;

            _eventBus.Subscribe<ItemCommandEvent>(OnItemCommand);
            _eventBus.Subscribe<ContextActionSelectedEvent>(OnContextActionSelected);

            GD.Print("[ItemSystem] Initialized and subscribed to item/context events.");
        }

        private void EnsureInventory(uint actorEntityId)
        {
            if (_inventories.ContainsKey(actorEntityId)) return;

            _inventories[actorEntityId] = new Dictionary<string, int>
            {
                ["res_wood"] = 20,
                ["build_ladder_wood"] = 0,
                ["debug_bell"] = 0
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
            if (!ItemConfigManager.TryGetItem(itemId, out var def)) return false;
            if (GetInventoryCount(actorEntityId, itemId) < 1) return false;

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

        public bool HasInteractableAt(GridPosition pos)
        {
            if (!_occupiedToAnchor.TryGetValue(pos, out var anchor)) return false;
            if (!_placedAnchors.TryGetValue(anchor, out var record)) return false;

            return ItemConfigManager.TryGetItem(record.ItemId, out var def) && !string.IsNullOrEmpty(def.interactionScript);
        }

        private bool TryGetPlacedRecord(GridPosition at, out GridPosition anchor, out PlacedItemRecord record)
        {
            anchor = default;
            record = default;

            if (!_occupiedToAnchor.TryGetValue(at, out anchor)) return false;
            return _placedAnchors.TryGetValue(anchor, out record);
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

            _placedAnchors[anchor] = new PlacedItemRecord
            {
                ItemId = evt.ItemId,
                Durability = def.maxDurability,
                OwnerEntityId = evt.ActorEntityId
            };

            var occupiedCells = GetOccupiedCells(anchor, def);
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                _occupiedToAnchor[occupiedCells[i]] = anchor;
            }

            GD.Print($"[ItemSystem] Placed item '{evt.ItemId}' at anchor {anchor}, occupying {occupiedCells.Count} cells.");
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

            if (def.interactionScript == "DebugBellBehavior")
            {
                GD.Print($"[DebugBellBehavior] Ring Ring! actor={evt.ActorEntityId} at {target}, anchor={anchor}.");
            }
            else if (def.interactionScript == "LadderBehavior")
            {
                GD.Print($"[LadderBehavior] Future hook: actor={evt.ActorEntityId} can request cross-Z traversal at anchor={anchor}.");
            }
            else
            {
                GD.Print($"[ItemSystem] Used item with no special script. item={record.ItemId}");
            }

            PublishResult(true, $"Used {def.displayName}.", evt.ActorEntityId, record.ItemId, evt.Target);
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
    }
}
