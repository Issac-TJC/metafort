using Godot;
using System;
using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Items;
using MetaFort.Core.Spatial;
using MetaFort.Core.Systems;
using MetaFort.UI;

namespace MetaFort.Test_Control
{
    public partial class PlanningControlTestVillager : Node2D
    {
        private enum ControlMode
        {
            Normal,
            BuildingPlacement
        }

        private sealed class PendingBuildAssignment
        {
            public uint ActorEntityId;
            public int BlueprintId;
            public string ItemId;
            public GridPosition Anchor;
        }

        [Export]
        public MetaFort.Visual.TerrainVisualizer2D Visualizer;

        [Export]
        public MetaFort.Visual.VillagerCanvasRenderer CanvasRenderer;

        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath ItemSystemPath { get; set; }

        [Export]
        public NodePath BlueprintSystemPath { get; set; }

        [Export]
        public NodePath PlannerUiPath { get; set; }

        private IEntityManager _entityManager;
        private IMapManager _mapManager;
        private IEventBus _eventBus;
        private ItemSystemNode _itemSystem;
        private ConstructionBlueprintSystemNode _blueprintSystem;
        private BuildingPlannerPanel _plannerUi;

        private ControlMode _mode;
        private string _activeBuildItemId = string.Empty;
        private bool _hasHoverGrid;
        private GridPosition _hoverGrid;
        private int _lastVisualizerZ = int.MinValue;
        private bool _redrawRequested = true;

        private readonly Dictionary<uint, PendingBuildAssignment> _pendingAssignments = new Dictionary<uint, PendingBuildAssignment>();

        private const float TileSize = 32f;

        public override void _Ready()
        {
            Node coreSource = GetNodeOrNull(CoreSourcePath);
            if (coreSource is MetaFort.GameEntry gameEntry)
            {
                _entityManager = gameEntry.EntityManager;
                _mapManager = gameEntry.MapManager;
                _eventBus = gameEntry.EventBus;
            }
            else
            {
                GD.PrintErr($"[PlanningControlTestVillager] CoreSourcePath '{CoreSourcePath}' must point to a GameEntry node.");
                return;
            }

            _itemSystem = GetNodeOrNull<ItemSystemNode>(ItemSystemPath);
            _blueprintSystem = GetNodeOrNull<ConstructionBlueprintSystemNode>(BlueprintSystemPath);
            _plannerUi = GetNodeOrNull<BuildingPlannerPanel>(PlannerUiPath);

            if (_itemSystem == null || _blueprintSystem == null || _plannerUi == null)
            {
                GD.PrintErr("[PlanningControlTestVillager] Missing ItemSystem, BlueprintSystem, or BuildingPlannerUI.");
                return;
            }

            if (_entityManager == null || _mapManager == null || _eventBus == null)
            {
                GD.PrintErr("[PlanningControlTestVillager] GameEntry core systems are not ready.");
                return;
            }

            if (CanvasRenderer != null)
            {
                CanvasRenderer.InjectDependencies(_entityManager);
            }

            SpawnTestVillagers(3);

            _eventBus.Subscribe<ItemCommandResultEvent>(OnItemCommandResult);
            _eventBus.Subscribe<ContextActionSelectedEvent>(OnContextActionSelected);
            _eventBus.Subscribe<ConstructionBlueprintCommandEvent>(OnConstructionBlueprintCommand);
            _eventBus.Subscribe<ConstructionBlueprintCompletedEvent>(OnConstructionBlueprintCompleted);
            _eventBus.Subscribe<ConstructionBlueprintPlacedEvent>(OnBlueprintPlaced);
            _eventBus.Subscribe<ConstructionBlueprintCancelledEvent>(OnBlueprintCancelled);
            _eventBus.Subscribe<BuildPlannerItemSelectedEvent>(OnBuildPlannerItemSelected);
            _eventBus.Subscribe<BuildPlannerPlacementCancelledEvent>(OnBuildPlannerPlacementCancelled);
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<ItemCommandResultEvent>(OnItemCommandResult);
                _eventBus.Unsubscribe<ContextActionSelectedEvent>(OnContextActionSelected);
                _eventBus.Unsubscribe<ConstructionBlueprintCommandEvent>(OnConstructionBlueprintCommand);
                _eventBus.Unsubscribe<ConstructionBlueprintCompletedEvent>(OnConstructionBlueprintCompleted);
                _eventBus.Unsubscribe<ConstructionBlueprintPlacedEvent>(OnBlueprintPlaced);
                _eventBus.Unsubscribe<ConstructionBlueprintCancelledEvent>(OnBlueprintCancelled);
                _eventBus.Unsubscribe<BuildPlannerItemSelectedEvent>(OnBuildPlannerItemSelected);
                _eventBus.Unsubscribe<BuildPlannerPlacementCancelledEvent>(OnBuildPlannerPlacementCancelled);
            }
        }

        private void OnBuildPlannerItemSelected(ref BuildPlannerItemSelectedEvent evt)
        {
            OnBuildItemSelected(evt.ItemId);
        }

        private void OnBuildPlannerPlacementCancelled(ref BuildPlannerPlacementCancelledEvent evt)
        {
            CancelPlacementMode();
        }

        private void OnBuildItemSelected(string itemId)
        {
            _activeBuildItemId = itemId ?? string.Empty;
            _mode = string.IsNullOrEmpty(_activeBuildItemId) ? ControlMode.Normal : ControlMode.BuildingPlacement;
            _plannerUi?.SetPlacementState(_mode == ControlMode.BuildingPlacement, _activeBuildItemId);
            RequestOverlayRedraw();
        }

        private void CancelPlacementMode()
        {
            _mode = ControlMode.Normal;
            _activeBuildItemId = string.Empty;
            _plannerUi?.SetPlacementState(false, string.Empty);
            RequestOverlayRedraw();
        }

        private void OnItemCommandResult(ref ItemCommandResultEvent evt)
        {
            GD.Print($"[PlanningControlTestVillager] Item command result -> success={evt.Success}, msg={evt.Message}");
        }

        private void OnConstructionBlueprintCompleted(ref ConstructionBlueprintCompletedEvent evt)
        {
            GD.Print($"[PlanningControlTestVillager] Blueprint {evt.BlueprintId} completed -> {evt.ItemId} at {evt.Anchor}");
            RequestOverlayRedraw();
        }

        private void OnBlueprintPlaced(ref ConstructionBlueprintPlacedEvent evt)
        {
            RequestOverlayRedraw();
        }

        private void OnBlueprintCancelled(ref ConstructionBlueprintCancelledEvent evt)
        {
            RequestOverlayRedraw();
        }

        private void OnContextActionSelected(ref ContextActionSelectedEvent evt)
        {
            switch (evt.Selected.Type)
            {
                case ContextActionType.Move:
                    if (_entityManager.IsAlive(evt.ActorEntityId))
                    {
                        CommandSelectedVillagersTo(evt.Selected.Target.X, evt.Selected.Target.Y, evt.Selected.Target.Z);
                    }
                    break;
                case ContextActionType.BuildBlueprint:
                    GridPosition anchor = new GridPosition(evt.Selected.Target.X, evt.Selected.Target.Y, evt.Selected.Target.Z);
                    if (_blueprintSystem.TryGetBlueprintAt(anchor, out var blueprint))
                    {
                        var buildCommand = new ConstructionBlueprintCommandEvent
                        {
                            ActorEntityId = evt.ActorEntityId,
                            BlueprintId = blueprint.BlueprintId,
                            BlueprintAnchor = anchor
                        };
                        _eventBus.Publish(ref buildCommand);
                    }
                    break;
            }
        }

        private void OnConstructionBlueprintCommand(ref ConstructionBlueprintCommandEvent evt)
        {
            BeginBuildAssignment(evt.ActorEntityId, evt.BlueprintId, evt.BlueprintAnchor);
        }

        public override void _Process(double delta)
        {
            if (Visualizer != null && CanvasRenderer != null)
            {
                int zLevelFromVis = (int)Visualizer.Get("_currentZLevel");
                CanvasRenderer.CurrentZLevel = zLevelFromVis;
                if (zLevelFromVis != _lastVisualizerZ)
                {
                    _lastVisualizerZ = zLevelFromVis;
                    RequestOverlayRedraw();
                }
            }

            UpdateHoverGrid();
            MonitorPendingAssignments();
            if (_redrawRequested)
            {
                _redrawRequested = false;
                QueueRedraw();
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (Visualizer == null || CanvasRenderer == null || _eventBus == null || _entityManager == null) return;

            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.Escape && _mode == ControlMode.BuildingPlacement)
                {
                    CancelPlacementMode();
                    return;
                }

                if (keyEvent.Keycode == Key.T)
                {
                    Vector2 mousePos = Visualizer.GetGlobalMousePosition();
                    int stairGridX = Mathf.FloorToInt(mousePos.X / TileSize);
                    int stairGridY = Mathf.FloorToInt(mousePos.Y / TileSize);
                    SpawnTempStair(stairGridX, stairGridY, CanvasRenderer.CurrentZLevel);
                    GD.Print($"[PlanningControlTestVillager] Placed Temporary Stair at {stairGridX},{stairGridY},{CanvasRenderer.CurrentZLevel}");
                    return;
                }

                if (keyEvent.Keycode == Key.I)
                {
                    uint actorId = GetPrimarySelectedVillager();
                    if (actorId != uint.MaxValue && _itemSystem != null)
                    {
                        _itemSystem.PrintInventory(actorId);
                    }
                }
            }

            if (@event is not InputEventMouseButton mouseBtn || !mouseBtn.Pressed)
            {
                return;
            }

            Vector2 globalMousePos = Visualizer.GetGlobalMousePosition();
            Vector2 screenMousePos = mouseBtn.Position;
            Vector2I mapGridPos = Visualizer.TargetTileMap.LocalToMap(Visualizer.TargetTileMap.ToLocal(globalMousePos));
            int gridX = mapGridPos.X;
            int gridY = mapGridPos.Y;
            int gridZ = CanvasRenderer.CurrentZLevel;

            if (_mode == ControlMode.BuildingPlacement)
            {
                if (mouseBtn.ButtonIndex == MouseButton.Left)
                {
                    TryPlaceBlueprint(gridX, gridY, gridZ);
                }
                else if (mouseBtn.ButtonIndex == MouseButton.Right)
                {
                    CancelPlacementMode();
                }
                return;
            }

            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                SelectVillagerAt(gridX, gridY, gridZ);
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Right)
            {
                HandleCommandAt(screenMousePos, gridX, gridY, gridZ);
            }
        }

        public override void _Draw()
        {
            DrawBlueprints();
            DrawPlacementPreview();
        }

        private void UpdateHoverGrid()
        {
            bool hadHoverGrid = _hasHoverGrid;
            GridPosition previousHover = _hoverGrid;
            _hasHoverGrid = false;
            if (Visualizer?.TargetTileMap == null || _mapManager == null || CanvasRenderer == null)
            {
                if (hadHoverGrid)
                {
                    RequestOverlayRedraw();
                }
                return;
            }

            Vector2 globalMousePos = Visualizer.GetGlobalMousePosition();
            Vector2I mapGridPos = Visualizer.TargetTileMap.LocalToMap(Visualizer.TargetTileMap.ToLocal(globalMousePos));
            GridPosition hover = new GridPosition(mapGridPos.X, mapGridPos.Y, CanvasRenderer.CurrentZLevel);
            if (_mapManager.IsWithinBounds(hover))
            {
                _hoverGrid = hover;
                _hasHoverGrid = true;
            }

            if (hadHoverGrid != _hasHoverGrid
                || previousHover.X != _hoverGrid.X
                || previousHover.Y != _hoverGrid.Y
                || previousHover.Z != _hoverGrid.Z)
            {
                RequestOverlayRedraw();
            }
        }

        private void TryPlaceBlueprint(int gridX, int gridY, int gridZ)
        {
            if (string.IsNullOrEmpty(_activeBuildItemId))
            {
                return;
            }

            GridPosition anchor = new GridPosition(gridX, gridY, gridZ);
            uint actorId = GetPrimarySelectedVillager();
            if (_blueprintSystem.TryPlaceBlueprint(_activeBuildItemId, anchor, actorId == uint.MaxValue ? 0 : actorId, out int blueprintId, out string failureReason))
            {
                GD.Print($"[PlanningControlTestVillager] Placed blueprint {blueprintId} for {_activeBuildItemId} at {anchor}");
                RequestOverlayRedraw();
            }
            else
            {
                GD.Print($"[PlanningControlTestVillager] Blueprint placement blocked: {failureReason}");
            }
        }

        private void HandleCommandAt(Vector2 screenMousePos, int gridX, int gridY, int gridZ)
        {
            GridPosition gp = new GridPosition(gridX, gridY, gridZ);
            uint actorId = GetPrimarySelectedVillager();

            if (_blueprintSystem.TryGetBlueprintAt(gp, out var blueprint))
            {
                if (actorId == uint.MaxValue)
                {
                    GD.Print($"[PlanningControlTestVillager] Blueprint '{blueprint.ItemId}' is waiting for a builder at {gp}.");
                    return;
                }

                if (!ItemConfigManager.TryGetItem(blueprint.ItemId, out ItemDefinition def))
                {
                    return;
                }

                ContextActionOption option = new ContextActionOption
                {
                    Type = ContextActionType.BuildBlueprint,
                    Label = $"Build {def.ResolvePlannerLabel()}",
                    ItemId = blueprint.ItemId,
                    Target = new Vector3I(gp.X, gp.Y, gp.Z)
                };
                ExecuteContextOption(actorId, option, screenMousePos);
                return;
            }

            if (_itemSystem != null && _itemSystem.HasInteractableAt(gp))
            {
                if (actorId == uint.MaxValue)
                {
                    GD.Print("[PlanningControlTestVillager] Select a villager before using an object.");
                    return;
                }

                ContextActionOption option = new ContextActionOption
                {
                    Type = ContextActionType.Use,
                    Label = "Use Item Here",
                    ItemId = string.Empty,
                    Target = new Vector3I(gp.X, gp.Y, gp.Z)
                };
                ExecuteContextOption(actorId, option, screenMousePos);
                return;
            }

            if (actorId == uint.MaxValue)
            {
                GD.Print("[PlanningControlTestVillager] No selected villager. Right click ignored.");
                return;
            }

            ContextActionOption moveOption = new ContextActionOption
            {
                Type = ContextActionType.Move,
                Label = $"Move to ({gp.X},{gp.Y},{gp.Z})",
                ItemId = string.Empty,
                Target = new Vector3I(gp.X, gp.Y, gp.Z)
            };
            ExecuteContextOption(actorId, moveOption, screenMousePos);
        }

        private void ExecuteContextOption(uint actorId, ContextActionOption option, Vector2 screenPosition)
        {
            if (option.Type == ContextActionType.Move)
            {
                CommandSelectedVillagersTo(option.Target.X, option.Target.Y, option.Target.Z);
                return;
            }

            var request = new ContextActionMenuRequestEvent
            {
                ActorEntityId = actorId,
                ScreenPosition = screenPosition,
                Options = new[] { option }
            };
            _eventBus.Publish(ref request);
        }

        private void BeginBuildAssignment(uint actorId, int blueprintId, GridPosition anchor)
        {
            if (!_entityManager.IsAlive(actorId))
            {
                GD.Print($"[PlanningControlTestVillager] Build command ignored because actor {actorId} is invalid.");
                return;
            }

            if (!_blueprintSystem.TryGetBlueprint(blueprintId, out var blueprint))
            {
                GD.Print("[PlanningControlTestVillager] Blueprint no longer exists.");
                return;
            }

            if (!_blueprintSystem.TryAssignBuilder(blueprintId, actorId, out string failureReason))
            {
                GD.Print($"[PlanningControlTestVillager] Cannot assign builder: {failureReason}");
                return;
            }

            if (_pendingAssignments.TryGetValue(actorId, out var existing))
            {
                _blueprintSystem.ClearAssignment(existing.BlueprintId);
            }

            _pendingAssignments[actorId] = new PendingBuildAssignment
            {
                ActorEntityId = actorId,
                BlueprintId = blueprintId,
                ItemId = blueprint.ItemId,
                Anchor = anchor
            };

            ref VillagerStateComponent state = ref _entityManager.GetComponent<VillagerStateComponent>(actorId);
            state.CurrentAction = VillagerAction.Building;
            state.TargetX = anchor.X;
            state.TargetY = anchor.Y;
            state.TargetZ = anchor.Z;

            var moveCmd = new MoveCommandEvent
            {
                EntityId = actorId,
                Target = anchor
            };
            _eventBus.Publish(ref moveCmd);
            GD.Print($"[PlanningControlTestVillager] Builder {actorId} assigned to blueprint {blueprintId} at {anchor}");
            RequestOverlayRedraw();
        }

        private void MonitorPendingAssignments()
        {
            if (_pendingAssignments.Count == 0)
            {
                return;
            }

            List<uint> actors = new List<uint>(_pendingAssignments.Keys);
            for (int i = 0; i < actors.Count; i++)
            {
                uint actorId = actors[i];
                PendingBuildAssignment assignment = _pendingAssignments[actorId];

                if (!_entityManager.IsAlive(actorId))
                {
                    _blueprintSystem.ClearAssignment(assignment.BlueprintId);
                    _pendingAssignments.Remove(actorId);
                    continue;
                }

                if (!_blueprintSystem.TryGetBlueprint(assignment.BlueprintId, out _))
                {
                    _pendingAssignments.Remove(actorId);
                    continue;
                }

                if (!IsEntityNear(actorId, assignment.Anchor))
                {
                    continue;
                }

                _blueprintSystem.MarkBlueprintBuilding(assignment.BlueprintId, actorId);
                if (_itemSystem.TryCompleteBlueprintBuild(actorId, assignment.ItemId, assignment.Anchor, out string message))
                {
                    _blueprintSystem.TryCompleteBlueprintBuild(assignment.BlueprintId, actorId);
                    GD.Print($"[PlanningControlTestVillager] {message}");
                }
                else
                {
                    _blueprintSystem.ClearAssignment(assignment.BlueprintId);
                    GD.Print($"[PlanningControlTestVillager] Build failed: {message}");
                }

                ref VillagerStateComponent state = ref _entityManager.GetComponent<VillagerStateComponent>(actorId);
                state.CurrentAction = VillagerAction.Idle;
                _pendingAssignments.Remove(actorId);
                RequestOverlayRedraw();
            }
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

        private void DrawBlueprints()
        {
            if (_blueprintSystem == null || Visualizer?.TargetTileMap == null || CanvasRenderer == null)
            {
                return;
            }

            foreach (ConstructionBlueprintSystemNode.BlueprintRecord blueprint in _blueprintSystem.EnumerateBlueprints())
            {
                if (blueprint.Anchor.Z != CanvasRenderer.CurrentZLevel)
                {
                    continue;
                }

                Color fill = blueprint.Status switch
                {
                    ConstructionBlueprintStatus.Assigned => new Color(0.95f, 0.8f, 0.25f, 0.28f),
                    ConstructionBlueprintStatus.Building => new Color(1f, 0.5f, 0.2f, 0.35f),
                    _ => new Color(0.35f, 0.85f, 1f, 0.22f)
                };

                Color outline = blueprint.Status switch
                {
                    ConstructionBlueprintStatus.Assigned => new Color(1f, 0.9f, 0.3f, 0.95f),
                    ConstructionBlueprintStatus.Building => new Color(1f, 0.55f, 0.2f, 0.95f),
                    _ => new Color(0.45f, 0.95f, 1f, 0.95f)
                };

                for (int i = 0; i < blueprint.OccupiedCells.Count; i++)
                {
                    DrawGridCellOverlay(blueprint.OccupiedCells[i], fill, outline);
                }
            }
        }

        private void DrawPlacementPreview()
        {
            if (_mode != ControlMode.BuildingPlacement || string.IsNullOrEmpty(_activeBuildItemId) || !_hasHoverGrid || _itemSystem == null || _blueprintSystem == null)
            {
                return;
            }

            List<GridPosition> previewCells = _itemSystem.GetOccupiedCellsForItem(_activeBuildItemId, _hoverGrid);
            bool canPlace = _blueprintSystem.CanPlaceBlueprint(_activeBuildItemId, _hoverGrid, out _);
            Color fill = canPlace ? new Color(0.45f, 1f, 0.55f, 0.22f) : new Color(1f, 0.35f, 0.35f, 0.24f);
            Color outline = canPlace ? new Color(0.55f, 1f, 0.65f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);

            for (int i = 0; i < previewCells.Count; i++)
            {
                if (previewCells[i].Z != CanvasRenderer.CurrentZLevel)
                {
                    continue;
                }

                DrawGridCellOverlay(previewCells[i], fill, outline);
            }
        }

        private void DrawGridCellOverlay(GridPosition cell, Color fill, Color outline)
        {
            Rect2 rect = GetCellRect(cell);
            DrawRect(rect, fill, true);
            DrawRect(rect, outline, false, 2f);
        }

        private Rect2 GetCellRect(GridPosition cell)
        {
            Vector2 localCenter = Visualizer.TargetTileMap.MapToLocal(new Vector2I(cell.X, cell.Y));
            Vector2 globalCenter = Visualizer.TargetTileMap.ToGlobal(localCenter);
            Vector2 drawCenter = ToLocal(globalCenter);
            return new Rect2(drawCenter - new Vector2(TileSize / 2f, TileSize / 2f), new Vector2(TileSize, TileSize));
        }

        private uint GetPrimarySelectedVillager()
        {
            ReadOnlySpan<uint> selectedIds = _entityManager.GetDenseEntityIds<PlayerSelectedComponent>();
            if (selectedIds.Length == 0) return uint.MaxValue;
            return selectedIds[0];
        }

        private void SpawnTestVillagers(int count)
        {
            if (_entityManager == null) return;

            Random rng = new Random();
            int spawned = 0;
            int maxAttempts = 1000;
            int attempts = 0;

            while (spawned < count && attempts < maxAttempts)
            {
                attempts++;
                float startX = (_mapManager.Width / 2f) + rng.Next(-20, 20);
                float startY = (_mapManager.Height / 2f) + rng.Next(-20, 20);

                int xInt = Mathf.Clamp(Mathf.RoundToInt(startX), 0, _mapManager.Width - 1);
                int yInt = Mathf.Clamp(Mathf.RoundToInt(startY), 0, _mapManager.Height - 1);

                int foundZ = -1;
                for (int z = _mapManager.Depth - 1; z > 0; z--)
                {
                    var aboveTile = _mapManager.GetTile(xInt, yInt, z);
                    var belowTile = _mapManager.GetTile(xInt, yInt, z - 1);
                    if (aboveTile.Type == TerrainType.Air && belowTile.Type != TerrainType.Air && belowTile.Type != TerrainType.Water)
                    {
                        foundZ = z;
                        break;
                    }
                }

                if (foundZ != -1)
                {
                    int startZ = foundZ;
                    uint id = _entityManager.CreateEntity();
                    _entityManager.AddComponent(id, new MetaFort.Core.ECS.PositionComponent { X = xInt, Y = yInt, Z = startZ });

                    uint colorHex = (uint)GetRandomColor(rng).ToArgb32();
                    _entityManager.AddComponent(id, new VillagerVisualComponent
                    {
                        HeadId = rng.Next(1, 4),
                        TorsoId = rng.Next(1, 4),
                        HairId = rng.Next(1, 4),
                        ClothesId = rng.Next(1, 4),
                        SkinColorHex = colorHex
                    });

                    _entityManager.AddComponent(id, new VillagerStateComponent { CurrentAction = VillagerAction.Idle });

                    _entityManager.AddComponent(id, new BiologicalComponent
                    {
                        Gender = Godot.GD.Randf() > 0.5f ? Gender.Male : Gender.Female,
                        Hunger = 0f,
                        Stamina = 0f,
                        Sanity = 100f,
                        Libido = 0f
                    });

                    spawned++;
                }
            }
            GD.Print($"[PlanningControlTestVillager] Spawned {spawned} Test Villagers at Layer 15 (Attempts: {attempts}).");
        }

        private Color GetRandomColor(Random rng)
        {
            return new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
        }

        private void SpawnTempStair(int x, int y, int z)
        {
            uint stairId = _entityManager.CreateEntity();
            _entityManager.AddComponent(stairId, new MetaFort.Core.ECS.PositionComponent { X = x, Y = y, Z = z });
            _entityManager.AddComponent(stairId, new TempStairComponent());
        }

        private void SelectVillagerAt(int x, int y, int z)
        {
            ReadOnlySpan<uint> selectedIds = _entityManager.GetDenseEntityIds<PlayerSelectedComponent>();
            for (int i = selectedIds.Length - 1; i >= 0; i--)
            {
                _entityManager.RemoveComponent<PlayerSelectedComponent>(selectedIds[i]);
            }

            bool found = false;
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<MetaFort.Core.ECS.PositionComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];
                if (_entityManager.HasComponent<VillagerVisualComponent>(id))
                {
                    ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                    if ((int)pos.Z == z && Mathf.RoundToInt(pos.X) == x && Mathf.RoundToInt(pos.Y) == y)
                    {
                        _entityManager.AddComponent(id, new PlayerSelectedComponent());
                        GD.Print($"[PlanningControlTestVillager] Selected Villager ID: {id} at Grid {x},{y},{z}");
                        found = true;
                    }
                }
            }

            if (!found)
            {
                GD.Print($"[PlanningControlTestVillager] Clicked terrain grid ({x},{y}). Selection cleared.");
            }

            RequestOverlayRedraw();
        }

        private void CommandSelectedVillagersTo(int targetX, int targetY, int targetZ)
        {
            int selectedCount = _entityManager.GetComponentCount<PlayerSelectedComponent>();
            if (selectedCount == 0) return;

            ReadOnlySpan<uint> selectedIds = _entityManager.GetDenseEntityIds<PlayerSelectedComponent>();
            for (int i = 0; i < selectedIds.Length; i++)
            {
                uint id = selectedIds[i];
                if (_entityManager.HasComponent<VillagerStateComponent>(id))
                {
                    ref VillagerStateComponent state = ref _entityManager.GetComponent<VillagerStateComponent>(id);
                    state.CurrentAction = VillagerAction.Moving;
                    state.TargetX = targetX;
                    state.TargetY = targetY;
                    state.TargetZ = targetZ;

                    var moveCmd = new MoveCommandEvent { EntityId = id, Target = new GridPosition(targetX, targetY, targetZ) };
                    _eventBus.Publish(ref moveCmd);
                }
            }

            GD.Print($"[PlanningControlTestVillager] Commanded {selectedCount} units to {targetX},{targetY},{targetZ}");
        }

        private void RequestOverlayRedraw()
        {
            _redrawRequested = true;
        }
    }
}
