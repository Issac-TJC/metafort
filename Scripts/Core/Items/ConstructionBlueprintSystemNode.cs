using Godot;
using System.Collections.Generic;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Spatial;
using MetaFort.Core.Systems;

namespace MetaFort.Core.Items
{
    public partial class ConstructionBlueprintSystemNode : Node
    {
        public sealed class BlueprintRecord
        {
            public int BlueprintId;
            public string ItemId;
            public GridPosition Anchor;
            public uint PlacedByActorId;
            public int PlacedDay;
            public int PlacedHour;
            public ConstructionBlueprintStatus Status;
            public uint AssignedBuilderId;
            public List<GridPosition> OccupiedCells = new List<GridPosition>();
        }

        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath ItemSystemPath { get; set; }

        [Export]
        public NodePath TimeSourcePath { get; set; }

        private IEventBus _eventBus;
        private ItemSystemNode _itemSystem;
        private SimulationTimeNode _timeSource;
        private bool _initialized;
        private int _nextBlueprintId = 1;

        private readonly Dictionary<int, BlueprintRecord> _blueprintsById = new Dictionary<int, BlueprintRecord>();
        private readonly Dictionary<GridPosition, int> _occupiedCells = new Dictionary<GridPosition, int>();

        public override void _Ready()
        {
            if (_initialized) return;

            Node coreSource = GetNodeOrNull(CoreSourcePath);
            if (coreSource is not MetaFort.GameEntry gameEntry || gameEntry.EventBus == null)
            {
                GD.PrintErr("[ConstructionBlueprintSystem] CoreSourcePath must point to a valid GameEntry.");
                return;
            }

            _itemSystem = GetNodeOrNull<ItemSystemNode>(ItemSystemPath);
            if (_itemSystem == null)
            {
                GD.PrintErr("[ConstructionBlueprintSystem] ItemSystemPath must point to ItemSystemNode.");
                return;
            }

            _timeSource = GetNodeOrNull<SimulationTimeNode>(TimeSourcePath);
            _eventBus = gameEntry.EventBus;
            _initialized = true;
        }

        public IEnumerable<BlueprintRecord> EnumerateBlueprints()
        {
            return _blueprintsById.Values;
        }

        public bool TryGetBlueprint(int blueprintId, out BlueprintRecord blueprint)
        {
            return _blueprintsById.TryGetValue(blueprintId, out blueprint);
        }

        public bool TryGetBlueprintAt(GridPosition position, out BlueprintRecord blueprint)
        {
            blueprint = null;
            if (!_occupiedCells.TryGetValue(position, out int blueprintId))
            {
                return false;
            }

            return _blueprintsById.TryGetValue(blueprintId, out blueprint);
        }

        public bool TryPlaceBlueprint(string itemId, GridPosition anchor, uint placedByActorId, out int blueprintId, out string failureReason)
        {
            blueprintId = -1;
            if (!CanPlaceBlueprint(itemId, anchor, out failureReason, out List<GridPosition> occupiedCells))
            {
                return false;
            }

            blueprintId = _nextBlueprintId++;
            BlueprintRecord record = new BlueprintRecord
            {
                BlueprintId = blueprintId,
                ItemId = itemId,
                Anchor = anchor,
                PlacedByActorId = placedByActorId,
                PlacedDay = _timeSource?.Day ?? 0,
                PlacedHour = _timeSource?.Hour ?? 0,
                Status = ConstructionBlueprintStatus.Planned,
                AssignedBuilderId = 0,
                OccupiedCells = occupiedCells
            };

            _blueprintsById[blueprintId] = record;
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                _occupiedCells[occupiedCells[i]] = blueprintId;
            }

            var evt = new ConstructionBlueprintPlacedEvent
            {
                BlueprintId = blueprintId,
                ItemId = itemId,
                Anchor = anchor,
                PlacedByActorId = placedByActorId,
                Day = record.PlacedDay,
                Hour = record.PlacedHour
            };
            _eventBus.Publish(ref evt);
            return true;
        }

        public bool CanPlaceBlueprint(string itemId, GridPosition anchor, out string failureReason)
        {
            return CanPlaceBlueprint(itemId, anchor, out failureReason, out _);
        }

        public bool TryAssignBuilder(int blueprintId, uint actorId, out string failureReason)
        {
            failureReason = string.Empty;
            if (!_blueprintsById.TryGetValue(blueprintId, out BlueprintRecord blueprint))
            {
                failureReason = "Blueprint not found.";
                return false;
            }

            if (blueprint.Status == ConstructionBlueprintStatus.Completed || blueprint.Status == ConstructionBlueprintStatus.Cancelled)
            {
                failureReason = "Blueprint is no longer available.";
                return false;
            }

            if (blueprint.AssignedBuilderId != 0 && blueprint.AssignedBuilderId != actorId)
            {
                failureReason = "Blueprint is already assigned.";
                return false;
            }

            blueprint.AssignedBuilderId = actorId;
            blueprint.Status = ConstructionBlueprintStatus.Assigned;
            return true;
        }

        public bool MarkBlueprintBuilding(int blueprintId, uint actorId)
        {
            if (!_blueprintsById.TryGetValue(blueprintId, out BlueprintRecord blueprint))
            {
                return false;
            }

            if (blueprint.AssignedBuilderId != actorId)
            {
                return false;
            }

            blueprint.Status = ConstructionBlueprintStatus.Building;
            return true;
        }

        public bool ClearAssignment(int blueprintId)
        {
            if (!_blueprintsById.TryGetValue(blueprintId, out BlueprintRecord blueprint))
            {
                return false;
            }

            blueprint.AssignedBuilderId = 0;
            blueprint.Status = ConstructionBlueprintStatus.Planned;
            return true;
        }

        public bool TryCancelBlueprint(int blueprintId)
        {
            if (!_blueprintsById.TryGetValue(blueprintId, out BlueprintRecord blueprint))
            {
                return false;
            }

            blueprint.Status = ConstructionBlueprintStatus.Cancelled;
            RemoveBlueprint(blueprint);

            var evt = new ConstructionBlueprintCancelledEvent
            {
                BlueprintId = blueprintId,
                ItemId = blueprint.ItemId,
                Anchor = blueprint.Anchor
            };
            _eventBus.Publish(ref evt);
            return true;
        }

        public bool TryCompleteBlueprintBuild(int blueprintId, uint actorId)
        {
            if (!_blueprintsById.TryGetValue(blueprintId, out BlueprintRecord blueprint))
            {
                return false;
            }

            if (blueprint.AssignedBuilderId != actorId)
            {
                return false;
            }

            blueprint.Status = ConstructionBlueprintStatus.Completed;
            RemoveBlueprint(blueprint);

            var evt = new ConstructionBlueprintCompletedEvent
            {
                BlueprintId = blueprintId,
                ItemId = blueprint.ItemId,
                Anchor = blueprint.Anchor,
                BuiltByActorId = actorId
            };
            _eventBus.Publish(ref evt);
            return true;
        }

        private void RemoveBlueprint(BlueprintRecord blueprint)
        {
            _blueprintsById.Remove(blueprint.BlueprintId);
            for (int i = 0; i < blueprint.OccupiedCells.Count; i++)
            {
                _occupiedCells.Remove(blueprint.OccupiedCells[i]);
            }
        }

        private bool CanPlaceBlueprint(string itemId, GridPosition anchor, out string failureReason, out List<GridPosition> occupiedCells)
        {
            occupiedCells = null;
            failureReason = string.Empty;

            if (!_initialized)
            {
                failureReason = "Blueprint system is not initialized.";
                return false;
            }

            if (!ItemConfigManager.TryGetItem(itemId, out ItemDefinition definition))
            {
                failureReason = "Unknown build item.";
                return false;
            }

            if (!definition.isBuildable)
            {
                failureReason = "Item is not buildable from planner.";
                return false;
            }

            if (!_itemSystem.CanPlaceItemDefinition(itemId, anchor))
            {
                failureReason = "Cannot place blueprint at this location.";
                return false;
            }

            occupiedCells = _itemSystem.GetOccupiedCellsForItem(itemId, anchor);
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                if (_occupiedCells.ContainsKey(occupiedCells[i]))
                {
                    failureReason = "Another blueprint already occupies this tile.";
                    return false;
                }
            }

            return true;
        }
    }
}
