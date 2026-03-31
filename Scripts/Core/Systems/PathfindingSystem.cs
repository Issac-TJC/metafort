using System;
using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Systems
{
    public struct MoveCommandEvent : MetaFort.Core.EventBus.IGameEvent 
    { 
        public uint EntityId; 
        public GridPosition Target; 
    }

    /// <summary>
    /// 支持 Z 轴梯子分层寻路的系统
    /// 包含了一套简易的平面 A* 和自动规划逻辑
    /// </summary>
    public class PathfindingSystem : ISystem
    {
        private IEntityManager _entityManager;
        private IMapManager _mapManager;

        // 在系统内部管理路径列表，因为 ECS 本地 struct 不能放引用类型 (List/Queue) 以保证 0 Alloc
        private Dictionary<uint, Queue<GridPosition>> _entityPaths = new Dictionary<uint, Queue<GridPosition>>();

        private MetaFort.Core.EventBus.GameEventHandler<MoveCommandEvent> _onMoveCmdHandler;

        public void Initialize(IEntityManager entityManager, MetaFort.Core.EventBus.IEventBus eventBus)
        {
            _entityManager = entityManager;
            _mapManager = GameEntry.Instance?.MapManager;

            if (eventBus != null)
            {
                _onMoveCmdHandler = OnMoveCommand;
                eventBus.Subscribe(_onMoveCmdHandler);
            }
        }

        private void OnMoveCommand(ref MoveCommandEvent e)
        {
            AssignNewTarget(e.EntityId, e.Target);
        }

        public void InjectMapManager(IMapManager mapManager)
        {
            _mapManager = mapManager;
        }

        public void AssignNewTarget(uint entityId, GridPosition finalTarget)
        {
            if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(entityId)) return;
            
            ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(entityId);
            GridPosition currentPos = new GridPosition((int)Math.Round(pos.X), (int)Math.Round(pos.Y), (int)pos.Z);

            var path = CalculateLayeredPath(currentPos, finalTarget);
            _entityPaths[entityId] = path;
        }

        public void Update(double deltaTime)
        {
            if (_entityManager == null) return;
            float dt = (float)deltaTime;
            float speed = 4.0f; // 4格每秒

            // 遍历所有带有小人状态的实体
            int count = _entityManager.GetComponentCount<VillagerStateComponent>();
            if (count == 0) return;

            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<VillagerStateComponent>();

            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];

                if (_entityPaths.TryGetValue(id, out Queue<GridPosition> path) && path.Count > 0)
                {
                    if (_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(id))
                    {
                        ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                        
                        GridPosition nextStep = path.Peek();
                        float dx = nextStep.X - pos.X;
                        float dy = nextStep.Y - pos.Y;
                        float dz = nextStep.Z - pos.Z;

                        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        
                        if (dist < 0.1f)
                        {
                            // 已抵达当前节点
                            pos.X = nextStep.X;
                            pos.Y = nextStep.Y;
                            pos.Z = nextStep.Z;
                            path.Dequeue();
                            
                            // 抵达终点后，自动设置Idle
                            if (path.Count == 0)
                            {
                                ref VillagerStateComponent state = ref _entityManager.GetComponent<VillagerStateComponent>(id);
                                state.CurrentAction = VillagerAction.Idle;
                            }
                        }
                        else
                        {
                            // 走向目标 (如果存在跨Z，通常瞬间抵达或沿斜边)
                            if (dz != 0)
                            {
                                // 垂直攀爬梯子瞬间完成或者也用速度平滑
                                pos.Z += (dz / dist) * speed * dt;
                                // 为防止越界跳动，爬楼瞬间完成或增加特判
                                if (Math.Abs(pos.Z - nextStep.Z) < 0.2f) pos.Z = nextStep.Z;
                            }
                            else
                            {
                                pos.X += (dx / dist) * speed * dt;
                                pos.Y += (dy / dist) * speed * dt;
                            }
                        }
                    }
                }
            }
        }

        private Queue<GridPosition> CalculateLayeredPath(GridPosition start, GridPosition target)
        {
            Queue<GridPosition> fullPath = new Queue<GridPosition>();

            // 同层，直接 A*
            if (start.Z == target.Z)
            {
                return SimpleAStar(start, target);
            }

            // 跨层，寻找本层最近梯子
            GridPosition? closestStair = FindClosestStair(start.X, start.Y, start.Z);
            if (closestStair.HasValue)
            {
                var partialPath = SimpleAStar(start, closestStair.Value);
                foreach(var node in partialPath) fullPath.Enqueue(node); // 走到梯子

                // 跨层步伐，设定为走向目标所在 Z 的下一个梯子（简化：直接瞬间跨越）
                int nextZ = start.Z < target.Z ? start.Z + 1 : start.Z - 1;
                GridPosition climbingPos = new GridPosition(closestStair.Value.X, closestStair.Value.Y, nextZ);
                fullPath.Enqueue(climbingPos);

                // 递归继续求路
                var remainingPath = CalculateLayeredPath(climbingPos, target);
                foreach(var node in remainingPath) fullPath.Enqueue(node);
            }
            else
            {
                // 无路可走，原地发呆
                // System.Console.WriteLine("No stairs found on this Z level!");
            }

            return fullPath;
        }

        private GridPosition? FindClosestStair(int sx, int sy, int sz)
        {
            // 遍历由 EntityManager 管理的 TempStairComponent
            int count = _entityManager.GetComponentCount<TempStairComponent>();
            if (count == 0) return null;

            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<TempStairComponent>();
            GridPosition? closest = null;
            float minD = float.MaxValue;

            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];
                if (_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(id))
                {
                    ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                    if ((int)pos.Z == sz)
                    {
                        float d = (pos.X - sx) * (pos.X - sx) + (pos.Y - sy) * (pos.Y - sy);
                        if (d < minD)
                        {
                            minD = d;
                            closest = new GridPosition((int)pos.X, (int)pos.Y, (int)pos.Z);
                        }
                    }
                }
            }

            return closest;
        }

        private Queue<GridPosition> SimpleAStar(GridPosition start, GridPosition target)
        {
            // 这里为了掩饰，做一个极其简化的直线追踪 A* 或曼哈顿路径
            // 真实的 A* 需要读取 _mapManager.GetTile 检查空气墙
            Queue<GridPosition> path = new Queue<GridPosition>();

            int cx = start.X;
            int cy = start.Y;

            int iterations = 0;
            while ((cx != target.X || cy != target.Y) && iterations < 1000)
            {
                iterations++;
                int dx = Math.Sign(target.X - cx);
                int dy = Math.Sign(target.Y - cy);

                // 优先走X，再走Y (最简单的 L 型或对角式路线)
                if (cx != target.X) cx += dx;
                else if (cy != target.Y) cy += dy;

                path.Enqueue(new GridPosition(cx, cy, start.Z));
            }

            return path;
        }
    }
}
