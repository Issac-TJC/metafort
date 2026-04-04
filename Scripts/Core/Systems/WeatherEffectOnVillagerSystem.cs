using System;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Systems
{
    /// <summary>
    /// 将天气信号转换为小人生理属性增减。
    /// </summary>
    public class WeatherEffectOnVillagerSystem : ISystem
    {
        private IEntityManager _entityManager;

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;

            GameEventHandler<WeatherTickEvent> weatherTickHandler = OnWeatherTick;
            eventBus.Subscribe(weatherTickHandler);
        }

        public void Update(double deltaTime)
        {
            // 事件驱动系统，逐小时由 WeatherTickEvent 触发
        }

        private void OnWeatherTick(ref WeatherTickEvent e)
        {
            ReadOnlySpan<uint> ids = _entityManager.GetDenseEntityIds<BiologicalComponent>();
            float intensity = e.Current.Intensity;

            for (int i = 0; i < ids.Length; i++)
            {
                uint id = ids[i];
                ref var bio = ref _entityManager.GetComponent<BiologicalComponent>(id);

                switch (e.Current.Type)
                {
                    case WeatherType.Heatwave:
                        bio.Stamina += 4.0f * intensity;
                        bio.Hunger += 2.5f * intensity;
                        bio.Sanity -= 1.8f * intensity;
                        break;

                    case WeatherType.ColdWave:
                        bio.Stamina += 3.2f * intensity;
                        bio.Hunger += 1.8f * intensity;
                        bio.Sanity -= 1.2f * intensity;
                        break;

                    case WeatherType.Thunderstorm:
                        bio.Sanity -= 2.8f * intensity;
                        bio.Stamina += 1.2f * intensity;
                        break;

                    case WeatherType.HeavyRain:
                        bio.Sanity -= 0.8f * intensity;
                        bio.Stamina += 0.8f * intensity;
                        break;

                    case WeatherType.Clear:
                        // 轻微恢复
                        bio.Sanity += 0.4f;
                        break;
                }

                bio.Hunger = Math.Clamp(bio.Hunger, 0f, 100f);
                bio.Stamina = Math.Clamp(bio.Stamina, 0f, 100f);
                bio.Sanity = Math.Clamp(bio.Sanity, 0f, 100f);
                bio.Libido = Math.Clamp(bio.Libido, 0f, 100f);
            }
        }
    }
}
