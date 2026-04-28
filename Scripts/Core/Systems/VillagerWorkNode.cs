using Godot;
using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Items;
using MetaFort.Core.Spatial;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class VillagerWorkNode : Node
    {
        private sealed class PendingWorkAssignment
        {
            public uint ActorEntityId;
            public VillagerWorkType WorkType;
            public GridPosition Target;
            public GridPosition WorkPosition;
            public int BlueprintId;
            public string ItemId = string.Empty;
            public float RequiredSeconds;
            public float ProgressSeconds;
            public bool StartedWorking;
        }

        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath TimeSourcePath { get; set; }

        [Export]
        public NodePath ItemSystemPath { get; set; }

        [Export]
        public NodePath BlueprintSystemPath { get; set; }

        [Export]
        public NodePath StockpilePath { get; set; }

        [Export]
        public NodePath DesignationPath { get; set; }

        [Export]
        public float BuildWorkSeconds { get; set; } = 1.0f;

        [Export]
        public float DigWorkSeconds { get; set; } = 0.75f;

        [Export]
        public float DemolishWorkSeconds { get; set; } = 0.75f;

        private IEntityManager _entityManager;
        private IEventBus _eventBus;
        private IMapManager _mapManager;
        private SimulationTimeNode _timeSource;
        private ItemSystemNode _itemSystem;
        private ConstructionBlueprintSystemNode _blueprintSystem;
        private PlayerStockpileNode _stockpile;
        private CommandDesignationNode _designationNode;

        private readonly Dictionary<uint, PendingWorkAssignment> _pendingAssignments = new Dictionary<uint, PendingWorkAssignment>();

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry == null || gameEntry.EntityManager == null || gameEntry.EventBus == null || gameEntry.MapManager == null)
            {
                GD.PrintErr("[VillagerWorkNode] Missing GameEntry dependencies.");
                SetProcess(false);
                return;
            }

            _entityManager = gameEntry.EntityManager;
            _eventBus = gameEntry.EventBus;
            _mapManager = gameEntry.MapManager;
            _timeSource = GetNodeOrNull<SimulationTimeNode>(TimeSourcePath);
            _itemSystem = GetNodeOrNull<ItemSystemNode>(ItemSystemPath);
            _blueprintSystem = GetNodeOrNull<ConstructionBlueprintSystemNode>(BlueprintSystemPath);
            _stockpile = GetNodeOrNull<PlayerStockpileNode>(StockpilePath);
            _designationNode = GetNodeOrNull<CommandDesignationNode>(DesignationPath);

            if (_timeSource == null || _itemSystem == null || _blueprintSystem == null || _stockpile == null || _designationNode == null)
            {
                GD.PrintErr("[VillagerWorkNode] Missing one or more required node dependencies.");
                SetProcess(false);
                return;
            }

            _eventBus.Subscribe<ConstructionBlueprintCommandEvent>(OnConstructionBlueprintCommand);
            _eventBus.Subscribe<VillagerWorkRequestEvent>(OnVillagerWorkRequest);
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<ConstructionBlueprintCommandEvent>(OnConstructionBlueprintCommand);
                _eventBus.Unsubscribe<VillagerWorkRequestEvent>(OnVillagerWorkRequest);
            }
        }

        public override void _Process(double delta)
        {
            if (_pendingAssignments.Count == 0 || _timeSource == null)
            {
                return;
            }

            List<uint> actors = new List<uint>(_pendingAssignments.Keys);
            for (int i = 0; i < actors.Count; i++)
            {
                uint actorId = actors[i];
                if (!_pendingAssignments.TryGetValue(actorId, out PendingWorkAssignment assignment))
                {
                    continue;
                }

                if (!_entityManager.IsAlive(actorId))
                {
                    ClearBuildReservationIfNeeded(assignment);
                    _pendingAssignments.Remove(actorId);
                    continue;
                }

                if (!IsEntityNear(actorId, assignment.WorkPosition))
                {
                    continue;
                }

                if (!assignment.StartedWorking)
                {
                    assignment.StartedWorking = true;
                    ref VillagerStateComponent state = ref _entityManager.GetComponent<VillagerStateComponent>(actorId);
                    state.CurrentAction = assignment.WorkType == VillagerWorkType.Build ? VillagerAction.Building : VillagerAction.Digging;

                    if (assignment.WorkType == VillagerWorkType.Build)
                    {
                        _blueprintSystem.MarkBlueprintBuilding(assignment.BlueprintId, actorId);
                    }
                }

                assignment.ProgressSeconds += _timeSource.ScaledDeltaTime;
                if (assignment.ProgressSeconds < assignment.RequiredSeconds)
                {
                    continue;
                }

                CompleteAssignment(assignment);
                ref VillagerStateComponent completionState = ref _entityManager.GetComponent<VillagerStateComponent>(actorId);
                completionState.CurrentAction = VillagerAction.Idle;
                _pendingAssignments.Remove(actorId);
            }
        }

        private void OnConstructionBlueprintCommand(ref ConstructionBlueprintCommandEvent evt)
        {
            if (!_blueprintSystem.TryGetBlueprint(evt.BlueprintId, out ConstructionBlueprintSystemNode.BlueprintRecord blueprint))
            {
                GD.Print("[VillagerWorkNode] Blueprint no longer exists.");
                return;
            }

            if (!_blueprintSystem.TryAssignBuilder(evt.BlueprintId, evt.ActorEntityId, out string failureReason))
            {
                GD.Print($"[VillagerWorkNode] Cannot assign builder: {failureReason}");
                return;
            }

            StartAssignment(new PendingWorkAssignment
            {
                ActorEntityId = evt.ActorEntityId,
                WorkType = VillagerWorkType.Build,
                Target = evt.BlueprintAnchor,
                WorkPosition = evt.BlueprintAnchor,
                BlueprintId = evt.BlueprintId,
                ItemId = blueprint.ItemId,
                RequiredSeconds = BuildWorkSeconds
            });
        }

        private void OnVillagerWorkRequest(ref VillagerWorkRequestEvent evt)
        {
            switch (evt.WorkType)
            {
                case VillagerWorkType.Dig:
                    if (!_designationNode.TryGetDigDesignation(evt.ResolvedTarget, out CommandDesignationNode.DigDesignation digDesignation))
                    {
                        GD.Print("[VillagerWorkNode] No dig designation exists at target.");
                        return;
                    }

                    if (!TryResolveDigWorkPosition(evt.ResolvedTarget, evt.DigTargetKind == DigTargetKind.None ? digDesignation.Kind : evt.DigTargetKind, out GridPosition digWorkPosition))
                    {
                        GD.Print("[VillagerWorkNode] No valid work position for dig target.");
                        return;
                    }

                    StartAssignment(new PendingWorkAssignment
                    {
                        ActorEntityId = evt.ActorEntityId,
                        WorkType = VillagerWorkType.Dig,
                        Target = evt.ResolvedTarget,
                        WorkPosition = digWorkPosition,
                        RequiredSeconds = DigWorkSeconds
                    });
                    break;

                case VillagerWorkType.Demolish:
                    if (!_designationNode.TryGetDemolishDesignation(evt.ResolvedTarget, out CommandDesignationNode.DemolishDesignation demolishDesignation))
                    {
                        GD.Print("[VillagerWorkNode] No demolish designation exists at target.");
                        return;
                    }

                    if (!TryResolveAdjacentWorkPosition(demolishDesignation.Anchor, out GridPosition demolishWorkPosition))
                    {
                        GD.Print("[VillagerWorkNode] No valid work position for demolish target.");
                        return;
                    }

                    StartAssignment(new PendingWorkAssignment
                    {
                        ActorEntityId = evt.ActorEntityId,
                        WorkType = VillagerWorkType.Demolish,
                        Target = demolishDesignation.Anchor,
                        WorkPosition = demolishWorkPosition,
                        ItemId = demolishDesignation.ItemId,
                        RequiredSeconds = DemolishWorkSeconds
                    });
                    break;
            }
        }

        private void StartAssignment(PendingWorkAssignment assignment)
        {
            if (!_entityManager.IsAlive(assignment.ActorEntityId))
            {
                return;
            }

            if (_pendingAssignments.TryGetValue(assignment.ActorEntityId, out PendingWorkAssignment existing))
            {
                ClearBuildReservationIfNeeded(existing);
            }

            _pendingAssignments[assignment.ActorEntityId] = assignment;

            ref VillagerStateComponent state = ref _entityManager.GetComponent<VillagerStateComponent>(assignment.ActorEntityId);
            state.CurrentAction = VillagerAction.Moving;
            state.TargetX = assignment.WorkPosition.X;
            state.TargetY = assignment.WorkPosition.Y;
            state.TargetZ = assignment.WorkPosition.Z;

            var moveCmd = new MoveCommandEvent
            {
                EntityId = assignment.ActorEntityId,
                Target = assignment.WorkPosition
            };
            _eventBus.Publish(ref moveCmd);
        }

        private void CompleteAssignment(PendingWorkAssignment assignment)
        {
            switch (assignment.WorkType)
            {
                case VillagerWorkType.Build:
                    CompleteBuild(assignment);
                    break;
                case VillagerWorkType.Dig:
                    CompleteDig(assignment);
                    break;
                case VillagerWorkType.Demolish:
                    CompleteDemolish(assignment);
                    break;
            }
        }

        private void CompleteBuild(PendingWorkAssignment assignment)
        {
            if (!_blueprintSystem.TryGetBlueprint(assignment.BlueprintId, out _))
            {
                return;
            }

            if (!ItemConfigManager.TryGetItem(assignment.ItemId, out ItemDefinition definition))
            {
                _blueprintSystem.ClearAssignment(assignment.BlueprintId);
                GD.Print("[VillagerWorkNode] Build item definition missing.");
                return;
            }

            if (!_itemSystem.CanPlaceItemDefinition(assignment.ItemId, assignment.Target))
            {
                _blueprintSystem.ClearAssignment(assignment.BlueprintId);
                GD.Print("[VillagerWorkNode] Build failed: build location is no longer valid.");
                return;
            }

            if (!_stockpile.TryConsumeRequirements(definition.requiredMaterials, out string failureReason))
            {
                _blueprintSystem.ClearAssignment(assignment.BlueprintId);
                GD.Print($"[VillagerWorkNode] Build failed: {failureReason}");
                return;
            }

            if (_itemSystem.TryPlaceConstructedItem(assignment.ActorEntityId, assignment.ItemId, assignment.Target, out string message))
            {
                _blueprintSystem.TryCompleteBlueprintBuild(assignment.BlueprintId, assignment.ActorEntityId);
                GD.Print($"[VillagerWorkNode] {message}");
            }
            else
            {
                _stockpile.AddRequirements(definition.requiredMaterials);
                _blueprintSystem.ClearAssignment(assignment.BlueprintId);
                GD.Print($"[VillagerWorkNode] Build failed: {message}");
            }
        }

        private void CompleteDig(PendingWorkAssignment assignment)
        {
            if (!_mapManager.IsWithinBounds(assignment.Target))
            {
                return;
            }

            TileData tile = _mapManager.GetTile(assignment.Target.X, assignment.Target.Y, assignment.Target.Z);
            if (tile.Type == TerrainType.Air || tile.Type == TerrainType.Bedrock)
            {
                _designationNode.RemoveDigDesignation(assignment.Target);
                return;
            }

            if (_mapManager.ReplaceTile(assignment.Target.X, assignment.Target.Y, assignment.Target.Z, TerrainType.Air))
            {
                _stockpile.AddTerrainDrops(tile.Type);
                _designationNode.RemoveDigDesignation(assignment.Target);
                GD.Print($"[VillagerWorkNode] Dug tile {assignment.Target}.");
            }
        }

        private void CompleteDemolish(PendingWorkAssignment assignment)
        {
            if (_itemSystem.TryRemovePlacedItem(assignment.Target, assignment.ActorEntityId, out string itemId))
            {
                _designationNode.RemoveDemolishDesignation(assignment.Target);
                GD.Print($"[VillagerWorkNode] Demolished {itemId} at {assignment.Target}.");
            }
        }

        private bool TryResolveDigWorkPosition(GridPosition target, DigTargetKind kind, out GridPosition workPosition)
        {
            workPosition = default;
            if (kind == DigTargetKind.Floor)
            {
                GridPosition above = new GridPosition(target.X, target.Y, target.Z + 1);
                if (IsWalkable(above))
                {
                    workPosition = above;
                    return true;
                }

                return false;
            }

            return TryResolveAdjacentWorkPosition(target, out workPosition);
        }

        private bool TryResolveAdjacentWorkPosition(GridPosition target, out GridPosition workPosition)
        {
            GridPosition[] candidates =
            {
                new GridPosition(target.X + 1, target.Y, target.Z),
                new GridPosition(target.X - 1, target.Y, target.Z),
                new GridPosition(target.X, target.Y + 1, target.Z),
                new GridPosition(target.X, target.Y - 1, target.Z)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (IsWalkable(candidates[i]))
                {
                    workPosition = candidates[i];
                    return true;
                }
            }

            workPosition = default;
            return false;
        }

        private bool IsWalkable(GridPosition position)
        {
            if (!_mapManager.IsWithinBounds(position))
            {
                return false;
            }

            TileData tile = _mapManager.GetTile(position.X, position.Y, position.Z);
            if (tile.Type != TerrainType.Air)
            {
                return false;
            }

            GridPosition below = new GridPosition(position.X, position.Y, position.Z - 1);
            if (!_mapManager.IsWithinBounds(below))
            {
                return false;
            }

            TileData belowTile = _mapManager.GetTile(below.X, below.Y, below.Z);
            return belowTile.Type != TerrainType.Air && belowTile.Type != TerrainType.Water;
        }

        private bool IsEntityNear(uint actorId, GridPosition anchor)
        {
            if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(actorId))
            {
                return false;
            }

            ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(actorId);
            return Mathf.RoundToInt(pos.X) == anchor.X
                && Mathf.RoundToInt(pos.Y) == anchor.Y
                && Mathf.RoundToInt(pos.Z) == anchor.Z;
        }

        private void ClearBuildReservationIfNeeded(PendingWorkAssignment assignment)
        {
            if (assignment.WorkType == VillagerWorkType.Build)
            {
                _blueprintSystem.ClearAssignment(assignment.BlueprintId);
            }
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
