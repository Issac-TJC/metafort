using System;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.ECS
{
    // ====== 示例组件 ======
    public struct PositionComponent : IComponent
    {
        public float X, Y, Z;
    }

    public struct VelocityComponent : IComponent
    {
        public float Vx, Vy, Vz;
    }

    /// <summary>
    /// 全零 GC，基于最少组件集合（Driver）的双数组迭代 Join，
    /// 这是现代商业级 ECS（如 Unity Entities、EnTT）中最核心的高频率更新优化范例。
    /// </summary>
    public class MovementSystem : ISystem
    {
        private IEntityManager _entityManager;

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
        }

        public void Update(double deltaTime)
        {
            float dt = (float)deltaTime;

            // 1. 获取并比对两个组件的实体数量，以此决定谁是最小范围的驱动主轴（Driver）
            int posCount = _entityManager.GetComponentCount<PositionComponent>();
            int velCount = _entityManager.GetComponentCount<VelocityComponent>();

            // 如果任意一个组件池为空，没有任何实体能够同时满足双组件条件，直接短路返回
            if (posCount == 0 || velCount == 0) return;

            // 2. 选择包含实体最少的数组开始循环，能极大地削减 O(N) 遍历的总次数
            if (velCount <= posCount)
            {
                // 以 Velocity (数量相对较少) 作为主轴
                // GetDenseEntityIds 返回的是无拷贝的连续内存映射
                ReadOnlySpan<uint> driverIds = _entityManager.GetDenseEntityIds<VelocityComponent>();

                for (int i = 0; i < driverIds.Length; i++)
                {
                    uint entityId = driverIds[i];

                    // 利用底层的 _entityToIndex 实现极速 0 分配查询验证
                    if (_entityManager.HasComponent<PositionComponent>(entityId))
                    {
                        // 3. 同时得到 ref，实现无对象分配和原内存块原地写回覆盖 (In-place Set)
                        ref VelocityComponent vel = ref _entityManager.GetComponent<VelocityComponent>(entityId);
                        ref PositionComponent pos = ref _entityManager.GetComponent<PositionComponent>(entityId);

                        pos.X += vel.Vx * dt;
                        pos.Y += vel.Vy * dt;
                        pos.Z += vel.Vz * dt;
                    }
                }
            }
            else
            {
                // 以 Position 作为主轴的反向遍历策略
                ReadOnlySpan<uint> driverIds = _entityManager.GetDenseEntityIds<PositionComponent>();

                for (int i = 0; i < driverIds.Length; i++)
                {
                    uint entityId = driverIds[i];

                    if (_entityManager.HasComponent<VelocityComponent>(entityId))
                    {
                        ref PositionComponent pos = ref _entityManager.GetComponent<PositionComponent>(entityId);
                        ref VelocityComponent vel = ref _entityManager.GetComponent<VelocityComponent>(entityId);

                        pos.X += vel.Vx * dt;
                        pos.Y += vel.Vy * dt;
                        pos.Z += vel.Vz * dt;
                    }
                }
            }
        }
    }
}
