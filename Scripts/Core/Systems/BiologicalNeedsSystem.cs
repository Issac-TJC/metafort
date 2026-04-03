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

        public void Update(double hoursPassedDelta)
        {
            _starvingEntities.Clear();
            
            float hoursPassed = (float)hoursPassedDelta;
            if (hoursPassed <= 0) return; // 暂停状态无需空转

            // 获取极致性能的致密数组
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<BiologicalComponent>();

            // 紧凑数组遍历，0 GC，缓存友好
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint entityId = entityIds[i];
                ref var bio = ref _entityManager.GetComponent<BiologicalComponent>(entityId);
                
                // 默认乘数
                float actionMultiplier = 1.0f;
                bool isWorking = false;

                // 尝试获取当前行为状态，如果存在则进行乘区结算
                if (_entityManager.HasComponent<VillagerStateComponent>(entityId))
                {
                    ref var state = ref _entityManager.GetComponent<VillagerStateComponent>(entityId);
                    if (state.CurrentAction == VillagerAction.Moving)
                        actionMultiplier = 1.5f;
                    else if (state.CurrentAction == VillagerAction.Digging || state.CurrentAction == VillagerAction.Building)
                    {
                        actionMultiplier = 3.0f;
                        isWorking = true;
                    }
                }

                // === 1. Hunger 饥饿代谢逻辑 ===
                float fatiguePenalty = bio.Stamina > 80f ? 1.2f : 1.0f;
                float hungerDelta = 2.5f * actionMultiplier * fatiguePenalty * hoursPassed;
                bio.Hunger += hungerDelta;

                // === 2. Stamina 肌肉疲劳逻辑 ===
                float staminaBaseRate = 4.0f;
                if (isWorking) staminaBaseRate += 2.0f; // 重体力劳动加速基础流失
                
                float starvationMultiplier = 1.0f;
                if (bio.Hunger > 50f)
                {
                    float hungerExcess = bio.Hunger - 50f;
                    starvationMultiplier += (hungerExcess * hungerExcess) * 0.001f;
                }
                bio.Stamina += staminaBaseRate * starvationMultiplier * hoursPassed;

                // === 3. Sanity 弹性情绪标的逻辑 ===
                float targetSanity = 100f; // 100 为理智充沛
                if (bio.Hunger > 60f) targetSanity -= (bio.Hunger - 60f) * 1.5f;
                if (bio.Stamina > 75f) targetSanity -= 30f;
                if (bio.Libido > 80f) targetSanity -= 15f;
                
                // 阻尼回准 (拉赫平滑过度)
                bio.Sanity += (targetSanity - bio.Sanity) * 0.1f * hoursPassed;

                // === 4. Libido 马斯洛阶层需求逻辑 ===
                if (bio.Hunger > 70f || bio.Sanity < 30f)
                {
                    // 生存受到威胁，强行熔断社交与繁衍需求
                    bio.Libido -= 3.0f * hoursPassed;
                }
                else
                {
                    // 饱暖思淫欲，每天累计约10点
                    bio.Libido += 0.41f * hoursPassed;
                }

                // === 5. 数值越界钳制 0-100 ===
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
