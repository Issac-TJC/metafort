using System.Collections.Generic;
using Godot;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;

namespace MetaFort.Core.Spatial
{
    public interface IVisionDataSystem
    {
        bool IsExplored(int x, int y, int z);
        bool IsExplored(Vector3I pos);
        void SetVisibilitiesAndDiff(int zLevel, HashSet<Vector2I> visibleSet, out List<Vector2I> newlyVisible, out List<Vector2I> newlyHidden, out List<Vector2I> newlyExplored);
        IEnumerable<Vector2I> GetExploredTiles(int zLevel);
        IEnumerable<Vector2I> GetVisibleTiles(int zLevel);
        bool IsCurrentlyVisible(int x, int y, int z);
    }

    /// <summary>
    /// 全局视野数据中心：纯数据驱动，仅负责维护 ExploredTiles 的缓存和脏扩散，并抛出事件供渲染层更新。
    /// 可以由 GameEntry 或 MapManager 实例化。
    /// </summary>
    public class VisionDataSystem : IVisionDataSystem
    {
        // 存储方式：按 Z 维度切片存储
        private Dictionary<int, HashSet<Vector2I>> _exploredTilesByZLevel = new Dictionary<int, HashSet<Vector2I>>();
        private Dictionary<int, HashSet<Vector2I>> _visibleTilesByZLevel = new Dictionary<int, HashSet<Vector2I>>();
        
        private IEventBus _eventBus;
        private IMapManager _mapManager;

        public VisionDataSystem(IEventBus eventBus, IMapManager mapManager)
        {
            _eventBus = eventBus;
            _mapManager = mapManager;
        }

        public bool IsExplored(int x, int y, int z)
        {
            if (_exploredTilesByZLevel.TryGetValue(z, out var exploredSet))
            {
                return exploredSet.Contains(new Vector2I(x, y));
            }
            return false;
        }

        public bool IsCurrentlyVisible(int x, int y, int z)
        {
            if (_visibleTilesByZLevel.TryGetValue(z, out var visibleSet))
            {
                return visibleSet.Contains(new Vector2I(x, y));
            }
            return false;
        }

        public bool IsExplored(Vector3I pos) => IsExplored(pos.X, pos.Y, pos.Z);

        public IEnumerable<Vector2I> GetExploredTiles(int zLevel)
        {
            if (_exploredTilesByZLevel.TryGetValue(zLevel, out var set))
                return set;
            return System.Array.Empty<Vector2I>();
        }

        public IEnumerable<Vector2I> GetVisibleTiles(int zLevel)
        {
            if (_visibleTilesByZLevel.TryGetValue(zLevel, out var set))
                return set;
            return System.Array.Empty<Vector2I>();
        }

        /// <summary>
        /// 后端核心 ECS 根据小人 FOV 计算后统一覆盖注入。
        /// </summary>
        public void SetVisibilitiesAndDiff(int zLevel, HashSet<Vector2I> newlyCalculatedVisibleSet, 
            out List<Vector2I> newlyVisible, out List<Vector2I> newlyHidden, out List<Vector2I> newlyExplored)
        {
            newlyVisible = new List<Vector2I>();
            newlyHidden = new List<Vector2I>();
            newlyExplored = new List<Vector2I>();

            if (!_exploredTilesByZLevel.ContainsKey(zLevel)) _exploredTilesByZLevel[zLevel] = new HashSet<Vector2I>();
            if (!_visibleTilesByZLevel.ContainsKey(zLevel)) _visibleTilesByZLevel[zLevel] = new HashSet<Vector2I>();

            var currentExplored = _exploredTilesByZLevel[zLevel];
            var currentVisible = _visibleTilesByZLevel[zLevel];

            // 1. 找出变为不看见的（以前在 currentlyVisible 里面，现在不在了的）
            foreach (var oldVis in currentVisible)
            {
                if (!newlyCalculatedVisibleSet.Contains(oldVis))
                {
                    // 掉入阴影：它依然是 Explored 过的，所以对于小人前端来说只是渲染一层阴影遮罩。
                    newlyHidden.Add(oldVis);
                }
            }

            // 2. 找出初次可见，或者从阴影重见天日的格子
            foreach (var newVis in newlyCalculatedVisibleSet)
            {
                if (!currentVisible.Contains(newVis))
                {
                    newlyVisible.Add(newVis);
                    if (currentExplored.Add(newVis))
                    {
                        newlyExplored.Add(newVis); // Historically totally new
                        newlyVisible.Remove(newVis); // If it's totally new explored, it goes into newlyVisible anyways, but we can keep it in newlyVisible OR newlyExplored to let frontend handle it. Let frontend handle newlyVisible as bright.
                        // Actually let's just use newlyExplored for statistics, visually it's Visible.
                        newlyVisible.Add(newVis); // Add it back
                    }
                }
            }

            // 更新状态机
            _visibleTilesByZLevel[zLevel] = newlyCalculatedVisibleSet;

            // 派发前端增量事件
            if ((newlyVisible.Count > 0 || newlyHidden.Count > 0) && _eventBus != null)
            {
                var ev = new VisionUpdatedEvent
                {
                    ZLevel = zLevel,
                    NewlyVisibleCoords = newlyVisible,
                    NewlyExploredCoords = newlyExplored,
                    NewlyHiddenCoords = newlyHidden
                };
                _eventBus.Publish(ref ev);
            }
        }
    }
}
