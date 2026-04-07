using Godot;
using System;
using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;
using MetaFort.Core.Data;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Systems
{
    public class VisibilityCalculationSystem : ISystem
    {
        private IEntityManager _entityManager;
        private IEventBus _eventBus;
        private IMapManager _mapManager;
        private IVisionDataSystem _visionDataSystem;

        private float _timeSinceLastUpdate = 0f;
        private float _updateInterval = 0.1f; // 10Hz
        private bool _forceUpdateNextTick = true;
        private int _lastVisibilitySourceSignature;
        private GameEventHandler<MetaFort.Core.Spatial.TerrainModifiedEvent> _terrainModifiedHandler;

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            Initialize(entityManager, eventBus, null, null);
        }

        public void Initialize(IEntityManager entityManager, IEventBus eventBus, IMapManager mapManager, IVisionDataSystem visionDataSystem)
        {
            _entityManager = entityManager;
            _eventBus = eventBus;
            _mapManager = mapManager;
            _visionDataSystem = visionDataSystem;

            if (_eventBus != null)
            {
                _terrainModifiedHandler = (ref MetaFort.Core.Spatial.TerrainModifiedEvent e) => {
                    _forceUpdateNextTick = true;
                };
                _eventBus.Subscribe(_terrainModifiedHandler);
            }
        }

        public void InjectDependencies(IMapManager mapManager, IVisionDataSystem visionDataSystem)
        {
            _mapManager = mapManager;
            _visionDataSystem = visionDataSystem;
        }

        public void Update(double deltaTime)
        {
            if (_mapManager == null || _visionDataSystem == null) return;

            _timeSinceLastUpdate += (float)deltaTime;
            
            // 只要超过 0.1秒 或是被触发砖块修改事件，立即全量计算
            if (_timeSinceLastUpdate < _updateInterval && !_forceUpdateNextTick)
            {
                return;
            }

            _timeSinceLastUpdate = 0f;
            int currentSignature = BuildVisibilitySourceSignature();
            if (!_forceUpdateNextTick && currentSignature == _lastVisibilitySourceSignature)
            {
                return;
            }

            _lastVisibilitySourceSignature = currentSignature;
            _forceUpdateNextTick = false;
            CalculateGlobalVisibility();
        }

        public void Shutdown()
        {
            if (_eventBus != null && _terrainModifiedHandler != null)
            {
                _eventBus.Unsubscribe(_terrainModifiedHandler);
            }
        }

        private void CalculateGlobalVisibility()
        {
            var villagersByZ = new Dictionary<int, List<Vector2I>>();
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<MetaFort.Core.ECS.PositionComponent>();
            
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];
                if (_entityManager.HasComponent<VillagerVisualComponent>(id))
                {
                    ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                    int z = (int)Math.Round(pos.Z);
                    if (!villagersByZ.ContainsKey(z)) villagersByZ[z] = new List<Vector2I>();
                    villagersByZ[z].Add(new Vector2I((int)Math.Round(pos.X), (int)Math.Round(pos.Y)));
                }
            }

            for (int z = 0; z < _mapManager.Depth; z++)
            {
                if (villagersByZ.TryGetValue(z, out var villagerPosList))
                {
                    HashSet<Vector2I> visibleSet = new HashSet<Vector2I>();
                    foreach (var vPos in villagerPosList)
                    {
                        ComputeFOVForVillager(vPos, z, visibleSet);
                    }
                    _visionDataSystem.SetVisibilitiesAndDiff(z, visibleSet, out _, out _, out _);
                }
                else
                {
                    // 若该层没有任何小人，推送一个空集合，使得该层陷入全暗（Fog of War）
                    _visionDataSystem.SetVisibilitiesAndDiff(z, new HashSet<Vector2I>(), out _, out _, out _);
                }
            }
        }

        private int BuildVisibilitySourceSignature()
        {
            HashCode hash = new HashCode();
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<MetaFort.Core.ECS.PositionComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];
                if (!_entityManager.HasComponent<VillagerVisualComponent>(id))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                hash.Add(id);
                hash.Add(Mathf.RoundToInt(pos.X));
                hash.Add(Mathf.RoundToInt(pos.Y));
                hash.Add(Mathf.RoundToInt(pos.Z));
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// 基于地图最大物理边缘的射线探寻 (Bresenham) 算法
        /// 彻底取代固定圆半径的逻辑，达成“无限光视距直到触碰墙壁”的设计
        /// </summary>
        private void ComputeFOVForVillager(Vector2I origin, int z, HashSet<Vector2I> outSet)
        {
            int w = _mapManager.Width;
            int h = _mapManager.Height;
            
            // 补丁：无条件点亮小人周边的 3x3 九宫格，解决 Bresenham 对角线漏缝导致的“身前一格全黑”现象
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = origin.X + dx;
                    int ny = origin.Y + dy;
                    if (_mapManager.IsWithinBounds(nx, ny, z))
                    {
                        var tile = _mapManager.GetTile(nx, ny, z);
                        if (!ConfigManager.BlocksVision((ushort)tile.Type) || (dx == 0 && dy == 0))
                        {
                            outSet.Add(new Vector2I(nx, ny));
                        }
                    }
                }
            }

            // 向四周边框打出密集的侦测射线
            for (int x = 0; x < w; x++)
            {
                CastRay(origin, new Vector2I(x, 0), z, outSet);
                CastRay(origin, new Vector2I(x, h - 1), z, outSet);
            }
            for (int y = 0; y < h; y++)
            {
                CastRay(origin, new Vector2I(0, y), z, outSet);
                CastRay(origin, new Vector2I(w - 1, y), z, outSet);
            }
        }

        private void CastRay(Vector2I p0, Vector2I p1, int z, HashSet<Vector2I> outSet)
        {
            int dx = Math.Abs(p1.X - p0.X);
            int dy = Math.Abs(p1.Y - p0.Y);
            int x = p0.X;
            int y = p0.Y;
            int n = 1 + dx + dy;
            int x_inc = (p1.X > p0.X) ? 1 : -1;
            int y_inc = (p1.Y > p0.Y) ? 1 : -1;
            int error = dx - dy;
            dx *= 2;
            dy *= 2;

            for (; n > 0; --n)
            {
                if (!_mapManager.IsWithinBounds(x, y, z)) break;
                
                outSet.Add(new Vector2I(x, y));
                MetaFort.Core.Spatial.TileData tile = _mapManager.GetTile(x, y, z);
                
                // ECS 数据驱动控制：不写死代码定性判墙，转由配表统御
                if (ConfigManager.BlocksVision((ushort)tile.Type))
                {
                    // 光源被墙体吃掉，中止衍生，保留墙体本身的可见性
                    break;
                }

                if (error > 0)
                {
                    x += x_inc;
                    error -= dy;
                }
                else
                {
                    y += y_inc;
                    error += dx;
                }
            }
        }
    }
}
