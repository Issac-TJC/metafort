using System;
using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Systems
{
    /// <summary>
    /// 生理状态流失系统
    /// 遵循 ECS 纯数据遍历和延迟事件机制，避免结构性变化（如直接抛出致死事件导致数组被破坏）
    /// </summary>
    public class BiologicalNeedsSystem : ISystem
    {
        private IEntityManager _entityManager;
        private IEventBus _eventBus;
        
        // 可复用的列表，避免在遍历致密数组（Span）时直接抛出事件导致的结构性改变 (Structural Change)
        private readonly List<uint> _starvingEntities = new List<uint>();

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
            _eventBus = eventBus;
        }

        public void Update(double deltaTime)
        {
            _starvingEntities.Clear();
            float dt = (float)deltaTime;

            // 获取极致性能的致密数组
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<BiologicalComponent>();

            // 紧凑数组遍历，0 GC，缓存友好
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint entityId = entityIds[i];
                ref var bio = ref _entityManager.GetComponent<BiologicalComponent>(entityId);

                // 随着时间流逝的生理需求变化
                bio.Hunger += 5f * dt;
                bio.Stamina -= 2f * dt;
                bio.Sanity -= 1f * dt;
                bio.Libido += 2f * dt;

                // 钳制数值范围 0-100
                bio.Hunger = Math.Clamp(bio.Hunger, 0f, 100f);
                bio.Stamina = Math.Clamp(bio.Stamina, 0f, 100f);
                bio.Sanity = Math.Clamp(bio.Sanity, 0f, 100f);
                bio.Libido = Math.Clamp(bio.Libido, 0f, 100f);

                // 如果饿到了极点，标记本帧需要扣血
                if (bio.Hunger >= 100f)
                {
                    _starvingEntities.Add(entityId);
                }
            }

            // 修正 1：延迟事件广播 (Deferred Event Broadcasting)
            // 循环彻底结束后，集中发送事件，保障 ECS 内存安全
            foreach (var entity in _starvingEntities)
            {
                var damageEvent = new DamageEvent
                {
                    TargetEntity = entity,
                    DamageAmount = 10f // 饿死扣血伤害
                };
                _eventBus.Publish(ref damageEvent);
            }
        }
    }
}
