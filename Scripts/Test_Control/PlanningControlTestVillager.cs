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

        [Export]
        public NodePath DesignationSystemPath { get; set; }

        [Export]
        public NodePath StockpilePath { get; set; }

        private IEntityManager _entityManager;
        private IMapManager _mapManager;
        private IEventBus _eventBus;
        private ItemSystemNode _itemSystem;
        private ConstructionBlueprintSystemNode _blueprintSystem;
        private BuildingPlannerPanel _plannerUi;
        private CommandDesignationNode _designationSystem;
        private PlayerStockpileNode _stockpile;

        private MapCursorModeState _cursorMode;
        private bool _hasHoverGrid;
        private GridPosition _hoverGrid;
        private int _lastVisualizerZ = int.MinValue;
        private bool _redrawRequested = true;

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
            _designationSystem = GetNodeOrNull<CommandDesignationNode>(DesignationSystemPath);
            _stockpile = GetNodeOrNull<PlayerStockpileNode>(StockpilePath);

            if (_itemSystem == null || _blueprintSystem == null || _plannerUi == null || _designationSystem == null || _stockpile == null)
            {
                GD.PrintErr("[PlanningControlTestVillager] Missing ItemSystem, BlueprintSystem, BuildingPlannerUI, DesignationSystem, or Stockpile.");
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
            _eventBus.Subscribe<ConstructionBlueprintCompletedEvent>(OnConstructionBlueprintCompleted);
            _eventBus.Subscribe<ConstructionBlueprintPlacedEvent>(OnBlueprintPlaced);
            _eventBus.Subscribe<ConstructionBlueprintCancelledEvent>(OnBlueprintCancelled);
            _eventBus.Subscribe<MapCursorModeRequestEvent>(OnMapCursorModeRequested);
            _eventBus.Subscribe<DigDesignationChangedEvent>(OnDigDesignationChanged);
            _eventBus.Subscribe<DemolishDesignationChangedEvent>(OnDemolishDesignationChanged);
            _eventBus.Subscribe<PlacedItemRemovedEvent>(OnPlacedItemRemoved);
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<ItemCommandResultEvent>(OnItemCommandResult);
                _eventBus.Unsubscribe<ContextActionSelectedEvent>(OnContextActionSelected);
                _eventBus.Unsubscribe<ConstructionBlueprintCompletedEvent>(OnConstructionBlueprintCompleted);
                _eventBus.Unsubscribe<ConstructionBlueprintPlacedEvent>(OnBlueprintPlaced);
                _eventBus.Unsubscribe<ConstructionBlueprintCancelledEvent>(OnBlueprintCancelled);
                _eventBus.Unsubscribe<MapCursorModeRequestEvent>(OnMapCursorModeRequested);
                _eventBus.Unsubscribe<DigDesignationChangedEvent>(OnDigDesignationChanged);
                _eventBus.Unsubscribe<DemolishDesignationChangedEvent>(OnDemolishDesignationChanged);
                _eventBus.Unsubscribe<PlacedItemRemovedEvent>(OnPlacedItemRemoved);
            }
        }

        private void OnDigDesignationChanged(ref DigDesignationChangedEvent evt)
        {
            RequestOverlayRedraw();
        }

        private void OnDemolishDesignationChanged(ref DemolishDesignationChangedEvent evt)
        {
            RequestOverlayRedraw();
        }

        private void OnPlacedItemRemoved(ref PlacedItemRemovedEvent evt)
        {
            RequestOverlayRedraw();
        }

        private void OnMapCursorModeRequested(ref MapCursorModeRequestEvent evt)
        {
            SetCursorMode(evt.Mode);
        }

        private void SetCursorMode(MapCursorModeState mode)
        {
            bool changed = _cursorMode.Kind != mode.Kind
                || _cursorMode.ItemId != mode.ItemId
                || _cursorMode.MarkerKey != mode.MarkerKey
                || _cursorMode.DisplayLabel != mode.DisplayLabel;

            _cursorMode = mode;

            if (!changed)
            {
                return;
            }

            var changedEvent = new MapCursorModeChangedEvent { Mode = _cursorMode };
            _eventBus.Publish(ref changedEvent);
            RequestOverlayRedraw();
        }

        private void ClearCursorMode()
        {
            SetCursorMode(new MapCursorModeState { Kind = MapCursorModeKind.None });
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
                        CommandSelectedVillagersTo(evt.Selected.ResolvedTarget.X, evt.Selected.ResolvedTarget.Y, evt.Selected.ResolvedTarget.Z);
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
                case ContextActionType.DigDesignationWork:
                    var digRequest = new VillagerWorkRequestEvent
                    {
                        ActorEntityId = evt.ActorEntityId,
                        WorkType = VillagerWorkType.Dig,
                        Target = new GridPosition(evt.Selected.Target.X, evt.Selected.Target.Y, evt.Selected.Target.Z),
                        ResolvedTarget = new GridPosition(evt.Selected.ResolvedTarget.X, evt.Selected.ResolvedTarget.Y, evt.Selected.ResolvedTarget.Z),
                        DigTargetKind = evt.Selected.DigTargetKind
                    };
                    _eventBus.Publish(ref digRequest);
                    break;
                case ContextActionType.DemolishDesignationWork:
                    var demolishRequest = new VillagerWorkRequestEvent
                    {
                        ActorEntityId = evt.ActorEntityId,
                        WorkType = VillagerWorkType.Demolish,
                        Target = new GridPosition(evt.Selected.Target.X, evt.Selected.Target.Y, evt.Selected.Target.Z),
                        ResolvedTarget = new GridPosition(evt.Selected.ResolvedTarget.X, evt.Selected.ResolvedTarget.Y, evt.Selected.ResolvedTarget.Z),
                        PayloadId = evt.Selected.PayloadId
                    };
                    _eventBus.Publish(ref demolishRequest);
                    break;
                case ContextActionType.CancelDigDesignation:
                    _designationSystem?.RemoveDigDesignation(new GridPosition(evt.Selected.ResolvedTarget.X, evt.Selected.ResolvedTarget.Y, evt.Selected.ResolvedTarget.Z));
                    break;
                case ContextActionType.CancelDemolishDesignation:
                    _designationSystem?.RemoveDemolishDesignation(new GridPosition(evt.Selected.ResolvedTarget.X, evt.Selected.ResolvedTarget.Y, evt.Selected.ResolvedTarget.Z));
                    break;
            }
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
                if (keyEvent.Keycode == Key.Escape)
                {
                    if (_cursorMode.Kind != MapCursorModeKind.None)
                    {
                        ClearCursorMode();
                    }
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
                    if (_stockpile != null)
                    {
                        foreach (StockpileEntryData entry in _stockpile.GetDisplayEntries())
                        {
                            GD.Print($"[PlanningControlTestVillager][Stockpile] {entry.Label} -> {entry.Count}");
                        }
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

            if (_cursorMode.Kind != MapCursorModeKind.None)
            {
                if (mouseBtn.ButtonIndex == MouseButton.Left)
                {
                    ApplyCursorModeAt(screenMousePos, gridX, gridY, gridZ);
                }
                else if (mouseBtn.ButtonIndex == MouseButton.Right)
                {
                    ClearCursorMode();
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
            DrawDesignations();
            DrawPlacementPreview();
            DrawCommandPreview();
            DrawCommandMarker();
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
            if (string.IsNullOrEmpty(_cursorMode.ItemId))
            {
                return;
            }

            GridPosition anchor = new GridPosition(gridX, gridY, gridZ);
            uint actorId = GetPrimarySelectedVillager();
            if (_blueprintSystem.TryPlaceBlueprint(_cursorMode.ItemId, anchor, actorId == uint.MaxValue ? 0 : actorId, out int blueprintId, out string failureReason))
            {
                GD.Print($"[PlanningControlTestVillager] Placed blueprint {blueprintId} for {_cursorMode.ItemId} at {anchor}");
                RequestOverlayRedraw();
            }
            else
            {
                GD.Print($"[PlanningControlTestVillager] Blueprint placement blocked: {failureReason}");
            }
        }

        private void ApplyCursorModeAt(Vector2 screenMousePos, int gridX, int gridY, int gridZ)
        {
            switch (_cursorMode.Kind)
            {
                case MapCursorModeKind.BuildBlueprint:
                    TryPlaceBlueprint(gridX, gridY, gridZ);
                    break;
                case MapCursorModeKind.DigDesignation:
                case MapCursorModeKind.DemolishDesignation:
                    TryPlaceDesignation(gridX, gridY, gridZ);
                    break;
                case MapCursorModeKind.CancelDesignation:
                    TryCancelDesignationAt(screenMousePos, gridX, gridY, gridZ);
                    break;
            }
        }

        private void TryPlaceDesignation(int gridX, int gridY, int gridZ)
        {
            if (_designationSystem == null)
            {
                return;
            }

            GridPosition target = new GridPosition(gridX, gridY, gridZ);
            switch (_cursorMode.Kind)
            {
                case MapCursorModeKind.DigDesignation:
                    if (_designationSystem.TryPlaceDigDesignation(target, out string digFailure))
                    {
                        GD.Print($"[PlanningControlTestVillager] Toggled dig designation from {target}");
                    }
                    else
                    {
                        GD.Print($"[PlanningControlTestVillager] Dig designation blocked: {digFailure}");
                    }
                    break;

                case MapCursorModeKind.DemolishDesignation:
                    if (_designationSystem.TryPlaceDemolishDesignation(target, out string demolishFailure))
                    {
                        GD.Print($"[PlanningControlTestVillager] Toggled demolish designation at {target}");
                    }
                    else
                    {
                        GD.Print($"[PlanningControlTestVillager] Demolish designation blocked: {demolishFailure}");
                    }
                    break;
            }

            RequestOverlayRedraw();
        }

        private void HandleCommandAt(Vector2 screenMousePos, int gridX, int gridY, int gridZ)
        {
            GridPosition gp = new GridPosition(gridX, gridY, gridZ);
            uint actorId = GetPrimarySelectedVillager();
            List<ContextActionOption> options = BuildContextOptions(actorId, gp);
            if (options.Count == 0)
            {
                if (actorId == uint.MaxValue)
                {
                    GD.Print("[PlanningControlTestVillager] No selected villager. Right click ignored.");
                }
                return;
            }

            if (options.Count == 1)
            {
                PublishContextSelection(actorId, options[0]);
                return;
            }

            var request = new ContextActionMenuRequestEvent
            {
                ActorEntityId = actorId,
                ScreenPosition = screenMousePos,
                Options = options.ToArray()
            };
            _eventBus.Publish(ref request);
        }

        private List<ContextActionOption> BuildContextOptions(uint actorId, GridPosition clickedCell)
        {
            List<ContextActionOption> options = new List<ContextActionOption>();

            if (actorId != uint.MaxValue && _blueprintSystem.TryGetBlueprintAt(clickedCell, out ConstructionBlueprintSystemNode.BlueprintRecord blueprint))
            {
                if (ItemConfigManager.TryGetItem(blueprint.ItemId, out ItemDefinition def))
                {
                    options.Add(new ContextActionOption
                    {
                        Type = ContextActionType.BuildBlueprint,
                        Label = $"Build {def.ResolvePlannerLabel()}",
                        ItemId = blueprint.ItemId,
                        Target = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z),
                        ResolvedTarget = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z)
                    });
                }
            }

            if (actorId != uint.MaxValue
                && _designationSystem != null
                && _designationSystem.TryGetDigDesignationAtDisplayCell(clickedCell, out _, out DigTargetResolution digResolution))
            {
                options.Add(new ContextActionOption
                {
                    Type = ContextActionType.DigDesignationWork,
                    Label = digResolution.Kind == DigTargetKind.Floor ? "Dig Floor" : "Dig Wall",
                    ItemId = string.Empty,
                    Target = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z),
                    ResolvedTarget = new Vector3I(digResolution.ResolvedTarget.X, digResolution.ResolvedTarget.Y, digResolution.ResolvedTarget.Z),
                    DigTargetKind = digResolution.Kind
                });
            }

            if (actorId != uint.MaxValue
                && _designationSystem != null
                && _designationSystem.TryGetDemolishDesignation(clickedCell, out CommandDesignationNode.DemolishDesignation demolishDesignation))
            {
                options.Add(new ContextActionOption
                {
                    Type = ContextActionType.DemolishDesignationWork,
                    Label = "Demolish",
                    ItemId = demolishDesignation.ItemId,
                    PayloadId = demolishDesignation.ItemId,
                    Target = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z),
                    ResolvedTarget = new Vector3I(demolishDesignation.Anchor.X, demolishDesignation.Anchor.Y, demolishDesignation.Anchor.Z)
                });
            }

            if (actorId != uint.MaxValue && _itemSystem != null && _itemSystem.HasInteractableAt(clickedCell))
            {
                options.Add(new ContextActionOption
                {
                    Type = ContextActionType.Use,
                    Label = "Use Item Here",
                    ItemId = string.Empty,
                    Target = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z),
                    ResolvedTarget = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z)
                });
            }

            if (actorId != uint.MaxValue)
            {
                options.Add(new ContextActionOption
                {
                    Type = ContextActionType.Move,
                    Label = $"Move to ({clickedCell.X},{clickedCell.Y},{clickedCell.Z})",
                    ItemId = string.Empty,
                    Target = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z),
                    ResolvedTarget = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z)
                });
            }

            return options;
        }

        private void TryCancelDesignationAt(Vector2 screenMousePos, int gridX, int gridY, int gridZ)
        {
            GridPosition clickedCell = new GridPosition(gridX, gridY, gridZ);
            List<ContextActionOption> options = BuildCancelDesignationOptions(clickedCell);
            if (options.Count == 0)
            {
                GD.Print("[PlanningControlTestVillager] No designation to cancel at this tile.");
                return;
            }

            if (options.Count == 1)
            {
                PublishContextSelection(0, options[0]);
                return;
            }

            var request = new ContextActionMenuRequestEvent
            {
                ActorEntityId = 0,
                ScreenPosition = screenMousePos,
                Options = options.ToArray()
            };
            _eventBus.Publish(ref request);
        }

        private List<ContextActionOption> BuildCancelDesignationOptions(GridPosition clickedCell)
        {
            List<ContextActionOption> options = new List<ContextActionOption>();

            if (_designationSystem != null && _designationSystem.TryGetDigDesignationAtDisplayCell(clickedCell, out _, out DigTargetResolution digResolution))
            {
                options.Add(new ContextActionOption
                {
                    Type = ContextActionType.CancelDigDesignation,
                    Label = digResolution.Kind == DigTargetKind.Floor ? "Cancel Dig Floor" : "Cancel Dig Wall",
                    Target = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z),
                    ResolvedTarget = new Vector3I(digResolution.ResolvedTarget.X, digResolution.ResolvedTarget.Y, digResolution.ResolvedTarget.Z),
                    DigTargetKind = digResolution.Kind
                });
            }

            if (_designationSystem != null && _designationSystem.TryGetDemolishDesignation(clickedCell, out CommandDesignationNode.DemolishDesignation demolishDesignation))
            {
                options.Add(new ContextActionOption
                {
                    Type = ContextActionType.CancelDemolishDesignation,
                    Label = "Cancel Demolish",
                    Target = new Vector3I(clickedCell.X, clickedCell.Y, clickedCell.Z),
                    ResolvedTarget = new Vector3I(demolishDesignation.Anchor.X, demolishDesignation.Anchor.Y, demolishDesignation.Anchor.Z),
                    PayloadId = demolishDesignation.ItemId
                });
            }

            return options;
        }

        private void PublishContextSelection(uint actorId, ContextActionOption option)
        {
            if (option.Type == ContextActionType.Move)
            {
                CommandSelectedVillagersTo(option.ResolvedTarget.X, option.ResolvedTarget.Y, option.ResolvedTarget.Z);
                return;
            }

            var selected = new ContextActionSelectedEvent
            {
                ActorEntityId = actorId,
                Selected = option
            };
            _eventBus.Publish(ref selected);
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
            if (_cursorMode.Kind != MapCursorModeKind.BuildBlueprint || string.IsNullOrEmpty(_cursorMode.ItemId) || !_hasHoverGrid || _itemSystem == null || _blueprintSystem == null)
            {
                return;
            }

            List<GridPosition> previewCells = _itemSystem.GetOccupiedCellsForItem(_cursorMode.ItemId, _hoverGrid);
            bool canPlace = _blueprintSystem.CanPlaceBlueprint(_cursorMode.ItemId, _hoverGrid, out _);
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

        private void DrawDesignations()
        {
            if (_designationSystem == null || CanvasRenderer == null)
            {
                return;
            }

            foreach (CommandDesignationNode.DigDesignation designation in _designationSystem.EnumerateDigDesignations())
            {
                if (designation.PreviewCell.Z != CanvasRenderer.CurrentZLevel)
                {
                    continue;
                }

                DrawGridCellOverlay(designation.PreviewCell, new Color(1f, 0.75f, 0.15f, 0.24f), new Color(1f, 0.85f, 0.2f, 0.95f));
            }

            foreach (CommandDesignationNode.DemolishDesignation designation in _designationSystem.EnumerateDemolishDesignations())
            {
                if (!_itemSystem.TryGetPlacedItemRecord(designation.Anchor, out ItemSystemNode.PlacedItemRecord record))
                {
                    continue;
                }

                List<GridPosition> occupiedCells = _itemSystem.GetOccupiedCellsForItem(record.ItemId, designation.Anchor);
                for (int i = 0; i < occupiedCells.Count; i++)
                {
                    if (occupiedCells[i].Z != CanvasRenderer.CurrentZLevel)
                    {
                        continue;
                    }

                    DrawGridCellOverlay(occupiedCells[i], new Color(1f, 0.2f, 0.2f, 0.18f), new Color(1f, 0.3f, 0.3f, 0.95f));
                }
            }
        }

        private void DrawCommandPreview()
        {
            if (!_hasHoverGrid || _designationSystem == null || CanvasRenderer == null)
            {
                return;
            }

            switch (_cursorMode.Kind)
            {
                case MapCursorModeKind.DigDesignation:
                    if (_designationSystem.TryResolveDigTarget(_hoverGrid, out DigTargetResolution digResolution)
                        && _mapManager.IsWithinBounds(digResolution.ResolvedTarget)
                        && digResolution.PreviewCell.Z == CanvasRenderer.CurrentZLevel)
                    {
                        DrawGridCellOverlay(digResolution.PreviewCell, new Color(1f, 0.95f, 0.35f, 0.12f), new Color(1f, 0.95f, 0.35f, 0.9f));
                    }
                    break;

                case MapCursorModeKind.DemolishDesignation:
                    if (_itemSystem.TryGetPlacedItemAnchor(_hoverGrid, out GridPosition anchor) && _itemSystem.TryGetPlacedItemRecord(anchor, out ItemSystemNode.PlacedItemRecord record))
                    {
                        List<GridPosition> occupiedCells = _itemSystem.GetOccupiedCellsForItem(record.ItemId, anchor);
                        for (int i = 0; i < occupiedCells.Count; i++)
                        {
                            if (occupiedCells[i].Z != CanvasRenderer.CurrentZLevel)
                            {
                                continue;
                            }

                            DrawGridCellOverlay(occupiedCells[i], new Color(1f, 0.45f, 0.45f, 0.14f), new Color(1f, 0.45f, 0.45f, 0.92f));
                        }
                    }
                    break;

                case MapCursorModeKind.CancelDesignation:
                    List<ContextActionOption> cancelOptions = BuildCancelDesignationOptions(_hoverGrid);
                    for (int i = 0; i < cancelOptions.Count; i++)
                    {
                        Vector3I target = cancelOptions[i].Target;
                        DrawGridCellOverlay(new GridPosition(target.X, target.Y, target.Z), new Color(0.9f, 0.25f, 0.9f, 0.14f), new Color(0.95f, 0.35f, 0.95f, 0.92f));
                    }
                    break;
            }
        }

        private void DrawCommandMarker()
        {
            if (_designationSystem == null || _cursorMode.Kind == MapCursorModeKind.None || string.IsNullOrWhiteSpace(_cursorMode.MarkerKey))
            {
                return;
            }

            Font font = ThemeDB.FallbackFont;
            if (font == null || Visualizer == null)
            {
                return;
            }

            Vector2 drawPos = ToLocal(Visualizer.GetGlobalMousePosition()) + new Vector2(10f, -10f);
            DrawString(font, drawPos, _cursorMode.MarkerKey);
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
            if (!TryClampMoveTarget(targetX, targetY, targetZ, out GridPosition clampedTarget))
            {
                GD.Print("[PlanningControlTestVillager] Move command ignored because target is outside the map.");
                return;
            }

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
                    state.TargetX = clampedTarget.X;
                    state.TargetY = clampedTarget.Y;
                    state.TargetZ = clampedTarget.Z;

                    var moveCmd = new MoveCommandEvent { EntityId = id, Target = clampedTarget };
                    _eventBus.Publish(ref moveCmd);
                }
            }

            GD.Print($"[PlanningControlTestVillager] Commanded {selectedCount} units to {clampedTarget.X},{clampedTarget.Y},{clampedTarget.Z}");
        }

        private void RequestOverlayRedraw()
        {
            _redrawRequested = true;
        }

        private bool TryClampMoveTarget(int targetX, int targetY, int targetZ, out GridPosition target)
        {
            target = default;
            if (_mapManager == null || _mapManager.Width <= 0 || _mapManager.Height <= 0 || _mapManager.Depth <= 0)
            {
                return false;
            }

            int clampedX = Mathf.Clamp(targetX, 0, _mapManager.Width - 1);
            int clampedY = Mathf.Clamp(targetY, 0, _mapManager.Height - 1);
            int clampedZ = Mathf.Clamp(targetZ, 0, _mapManager.Depth - 1);
            target = new GridPosition(clampedX, clampedY, clampedZ);
            return _mapManager.IsWithinBounds(target);
        }
    }
}
