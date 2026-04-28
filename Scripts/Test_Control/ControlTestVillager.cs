using Godot;
using System;
using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;
using MetaFort.Core.EventBus;
using MetaFort.Core.Systems;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Items;

namespace MetaFort.Test_Control
{
    public partial class ControlTestVillager : Node
    {
        [Export]
        public MetaFort.Visual.TerrainVisualizer2D Visualizer;

        [Export]
        public MetaFort.Visual.VillagerCanvasRenderer CanvasRenderer;

        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath ItemSystemPath { get; set; }

        private IEntityManager _entityManager;
        private IMapManager _mapManager;
        private IEventBus _eventBus;
        private ItemSystemNode _itemSystem;

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
                GD.PrintErr($"[ControlTestVillager] CoreSourcePath '{CoreSourcePath}' must point to a GameEntry node.");
                return;
            }

            _itemSystem = GetNodeOrNull<ItemSystemNode>(ItemSystemPath);
            if (_itemSystem == null)
            {
                GD.PrintErr($"[ControlTestVillager] ItemSystemPath '{ItemSystemPath}' must point to an ItemSystemNode.");
                return;
            }

            if (_entityManager == null || _mapManager == null || _eventBus == null)
            {
                GD.PrintErr("[ControlTestVillager] GameEntry core systems are not ready. Sandboxed operation aborted.");
                return;
            }

            if (CanvasRenderer != null)
            {
                CanvasRenderer.InjectDependencies(_entityManager);
            }

            // Deferred generation to allow terrain load
            SpawnTestVillagers(3);

            _eventBus.Subscribe<ItemCommandResultEvent>(OnItemCommandResult);
            _eventBus.Subscribe<ContextActionSelectedEvent>(OnContextActionSelected);
        }

        private void OnItemCommandResult(ref ItemCommandResultEvent evt)
        {
            GD.Print($"[ControlTestVillager] Item command result -> success={evt.Success}, msg={evt.Message}");
        }

        private void OnContextActionSelected(ref ContextActionSelectedEvent evt)
        {
            if (evt.Selected.Type != ContextActionType.Move) return;

            if (!_entityManager.IsAlive(evt.ActorEntityId))
            {
                GD.Print($"[ControlTestVillager] Move action ignored because actor {evt.ActorEntityId} is invalid.");
                return;
            }

            CommandSelectedVillagersTo(evt.Selected.Target.X, evt.Selected.Target.Y, evt.Selected.Target.Z);
        }

        public override void _Process(double delta)
        {
            if (Visualizer != null && CanvasRenderer != null)
            {
                // 让CanvasRenderer时刻与TerrainVisualizer所在层数同步
                int zLevelFromVis = (int)Visualizer.Get("_currentZLevel");
                CanvasRenderer.CurrentZLevel = zLevelFromVis;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (Visualizer == null || CanvasRenderer == null || _eventBus == null || _entityManager == null) return;

            if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
            {
                if (mouseBtn.ButtonIndex == MouseButton.Left || mouseBtn.ButtonIndex == MouseButton.Right)
                {
                    Vector2 globalMousePos = Visualizer.GetGlobalMousePosition();
                    Vector2 screenMousePos = mouseBtn.Position;
                    Vector2I mapGridPos = Visualizer.TargetTileMap.LocalToMap(Visualizer.TargetTileMap.ToLocal(globalMousePos));
                    int gridX = mapGridPos.X;
                    int gridY = mapGridPos.Y;
                    int gridZ = CanvasRenderer.CurrentZLevel;

                    if (mouseBtn.ButtonIndex == MouseButton.Left)
                    {
                        SelectVillagerAt(gridX, gridY, gridZ);
                    }
                    else if (mouseBtn.ButtonIndex == MouseButton.Right)
                    {
                        HandleContextActionAt(screenMousePos, gridX, gridY, gridZ);
                    }
                }
            }

            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.T)
                {
                    Vector2 mousePos = Visualizer.GetGlobalMousePosition();
                    int gridX = Mathf.FloorToInt(mousePos.X / 32f);
                    int gridY = Mathf.FloorToInt(mousePos.Y / 32f);
                    SpawnTempStair(gridX, gridY, CanvasRenderer.CurrentZLevel);
                    GD.Print($"[VillagerControl] Placed Temporary Stair at {gridX},{gridY},{CanvasRenderer.CurrentZLevel}");
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
        }

        private void HandleContextActionAt(Vector2 globalMousePos, int gridX, int gridY, int gridZ)
        {
            uint actorId = GetPrimarySelectedVillager();
            if (actorId == uint.MaxValue)
            {
                GD.Print("[VillagerControl] No selected villager. Right click ignored.");
                return;
            }

            GridPosition gp = new GridPosition(gridX, gridY, gridZ);
            var options = BuildContextOptions(actorId, gp);

            if (options.Count == 0)
            {
                GD.Print("[VillagerControl] No available actions at this location.");
                return;
            }

            if (options.Count == 1)
            {
                ExecuteContextOption(actorId, options[0]);
                return;
            }

            var req = new ContextActionMenuRequestEvent
            {
                ActorEntityId = actorId,
                ScreenPosition = globalMousePos,
                Options = options.ToArray()
            };
            _eventBus.Publish(ref req);
        }

        private List<ContextActionOption> BuildContextOptions(uint actorId, GridPosition gp)
        {
            var options = new List<ContextActionOption>();

            // Default movement option
            options.Add(new ContextActionOption
            {
                Type = ContextActionType.Move,
                Label = $"Move to ({gp.X},{gp.Y},{gp.Z})",
                ItemId = string.Empty,
                Target = new Vector3I(gp.X, gp.Y, gp.Z)
            });

            if (_itemSystem != null)
            {
                // Craft options
                if (_itemSystem.CanCraft(actorId, "build_ladder_wood"))
                {
                    options.Add(new ContextActionOption
                    {
                        Type = ContextActionType.Craft,
                        Label = "Craft Wood Ladder",
                        ItemId = "build_ladder_wood",
                        Target = new Vector3I(gp.X, gp.Y, gp.Z)
                    });
                }

                if (_itemSystem.CanCraft(actorId, "debug_bell"))
                {
                    options.Add(new ContextActionOption
                    {
                        Type = ContextActionType.Craft,
                        Label = "Craft Debug Bell",
                        ItemId = "debug_bell",
                        Target = new Vector3I(gp.X, gp.Y, gp.Z)
                    });
                }

                // Place options
                if (_itemSystem.CanPlaceItem(actorId, "build_ladder_wood", gp))
                {
                    options.Add(new ContextActionOption
                    {
                        Type = ContextActionType.Place,
                        Label = "Place Wood Ladder",
                        ItemId = "build_ladder_wood",
                        Target = new Vector3I(gp.X, gp.Y, gp.Z)
                    });
                }

                if (_itemSystem.CanPlaceItem(actorId, "debug_bell", gp))
                {
                    options.Add(new ContextActionOption
                    {
                        Type = ContextActionType.Place,
                        Label = "Place Debug Bell",
                        ItemId = "debug_bell",
                        Target = new Vector3I(gp.X, gp.Y, gp.Z)
                    });
                }

                if (_itemSystem.HasInteractableAt(gp))
                {
                    options.Add(new ContextActionOption
                    {
                        Type = ContextActionType.Use,
                        Label = "Use Item Here",
                        ItemId = string.Empty,
                        Target = new Vector3I(gp.X, gp.Y, gp.Z)
                    });
                }
            }

            return options;
        }

        private void ExecuteContextOption(uint actorId, ContextActionOption option)
        {
            if (option.Type == ContextActionType.Move)
            {
                CommandSelectedVillagersTo(option.Target.X, option.Target.Y, option.Target.Z);
                return;
            }

            var selectedEvt = new ContextActionSelectedEvent
            {
                ActorEntityId = actorId,
                Selected = option
            };
            _eventBus.Publish(ref selectedEvt);
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
                        Gender = (Godot.GD.Randf() > 0.5f) ? Gender.Male : Gender.Female,
                        Hunger = 0f,
                        Stamina = 0f,
                        Sanity = 100f,
                        Libido = 0f
                    });

                    spawned++;
                }
            }
            GD.Print($"[VillagerControl] Spawned {spawned} Test Villagers at Layer 15 (Attempts: {attempts}).");
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
                    if ((int)pos.Z == z)
                    {
                        if (Mathf.RoundToInt(pos.X) == x && Mathf.RoundToInt(pos.Y) == y)
                        {
                            _entityManager.AddComponent(id, new PlayerSelectedComponent());
                            GD.Print($"[VillagerControl] (Left Click) Selected Villager ID: {id} at Grid {x},{y},{z}");
                            found = true;
                        }
                    }
                }
            }

            if (!found)
            {
                GD.Print($"[VillagerControl] (Left Click) Clicked terrain grid ({x},{y}). Selection cleared.");
            }
        }

        private void CommandSelectedVillagersTo(int targetX, int targetY, int targetZ)
        {
            if (!TryClampMoveTarget(targetX, targetY, targetZ, out GridPosition clampedTarget))
            {
                GD.Print("[VillagerControl] Move command ignored because target is outside the map.");
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

            GD.Print($"[VillagerControl] Commanded {selectedCount} units to {clampedTarget.X},{clampedTarget.Y},{clampedTarget.Z}");
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
