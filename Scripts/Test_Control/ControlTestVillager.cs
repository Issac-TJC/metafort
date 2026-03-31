using Godot;
using System;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;
using MetaFort.Core.EventBus;
using MetaFort.Core.Systems;

namespace MetaFort.Test_Control
{
    public partial class ControlTestVillager : Node
    {
        [Export] 
        public MetaFort.Visual.TerrainVisualizer2D Visualizer;

        [Export]
        public MetaFort.Visual.VillagerCanvasRenderer CanvasRenderer;

        private IEntityManager _entityManager;
        private IMapManager _mapManager;
        private IEventBus _eventBus;

        public override void _Ready()
        {
            if (GameEntry.Instance != null)
            {
                _entityManager = GameEntry.Instance.EntityManager;
                _mapManager = GameEntry.Instance.MapManager;
                _eventBus = GameEntry.Instance.EventBus;
            }
            else
            {
                GD.PrintErr("[ControlTestVillager] GameEntry not found! Sandboxed operation aborted.");
                return;
            }

            if (CanvasRenderer != null)
            {
                CanvasRenderer.InjectDependencies(_entityManager);
            }

            // Deferred generation to allow terrain load
            SpawnTestVillagers(3);
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
                    // 放弃粗暴除以32，改用神级原生 TileMapLayer.LocalToMap 取绝对网格，避免任何物理变形导致的网格错位
                    Vector2I mapGridPos = Visualizer.TargetTileMap.LocalToMap(Visualizer.TargetTileMap.ToLocal(globalMousePos));
                    int gridX = mapGridPos.X;
                    int gridY = mapGridPos.Y;
                    int gridZ = CanvasRenderer.CurrentZLevel;

                    if (mouseBtn.ButtonIndex == MouseButton.Left)
                    {
                        // 左键选中脚底所在格子的小人
                        SelectVillagerAt(gridX, gridY, gridZ);
                    }
                    else if (mouseBtn.ButtonIndex == MouseButton.Right)
                    {
                        // 右键指派移动
                        CommandSelectedVillagersTo(gridX, gridY, gridZ);
                    }
                }
            }
            
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                // 按下 T 键，在鼠标位置放一个临时梯子
                if (keyEvent.Keycode == Key.T)
                {
                    Vector2 mousePos = Visualizer.GetGlobalMousePosition();
                    int gridX = Mathf.FloorToInt(mousePos.X / 32f);
                    int gridY = Mathf.FloorToInt(mousePos.Y / 32f);
                    SpawnTempStair(gridX, gridY, CanvasRenderer.CurrentZLevel);
                    GD.Print($"[VillagerControl] Placed Temporary Stair at {gridX},{gridY},{CanvasRenderer.CurrentZLevel}");
                }
            }
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

                // 自顶向下射线检测寻找真正的地表层
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

                    // 赋予随机颜色
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
            // 清空现有选择
            ReadOnlySpan<uint> selectedIds = _entityManager.GetDenseEntityIds<PlayerSelectedComponent>();
            for (int i = selectedIds.Length - 1; i >= 0; i--)
            {
                _entityManager.RemoveComponent<PlayerSelectedComponent>(selectedIds[i]);
            }

            bool found = false;

            // 查找这格的小人并选中
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<MetaFort.Core.ECS.PositionComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];
                if (_entityManager.HasComponent<VillagerVisualComponent>(id))
                {
                    ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                    if ((int)pos.Z == z)
                    {
                        // 判定鼠标点击的逻辑格子是否与小人脚部所在的绝对系统坐标系格子一致
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
                GD.Print($"          >>> [Debug] 本层的活体小人 ECS 逻辑真实坐标分别位于：");

                for (int i = 0; i < entityIds.Length; i++)
                {
                    uint id = entityIds[i];
                    if (_entityManager.HasComponent<VillagerVisualComponent>(id))
                    {
                        ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                        if ((int)pos.Z == z)
                        {
                            GD.Print($"             - 小人实体 [{id}] 真实站在了格子: X:{Mathf.RoundToInt(pos.X)}, Y:{Mathf.RoundToInt(pos.Y)}");
                        }
                    }
                }
            }
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

                    // 指派寻路系统开始动作 (通过发布事件代替直接调用引用)
                    var moveCmd = new MoveCommandEvent { EntityId = id, Target = new GridPosition(targetX, targetY, targetZ) };
                    _eventBus.Publish(ref moveCmd);
                }
            }

            GD.Print($"[VillagerControl] Commanded {selectedCount} units to {targetX},{targetY},{targetZ}");
        }
    }
}
