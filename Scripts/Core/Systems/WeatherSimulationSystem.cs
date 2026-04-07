using System;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Systems
{
    public class WeatherSimulationSystem : ISystem
    {
        private IEventBus _eventBus;
        private IMapManager _mapManager;
        private Random _rng;

        private WeatherState _current;
        private int _remainingHours;
        private int _currentDay;
        private int _currentHour;

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            Initialize(entityManager, eventBus, null, 0);
        }

        public void Initialize(IEntityManager entityManager, IEventBus eventBus, IMapManager mapManager, int seed)
        {
            _eventBus = eventBus;
            _mapManager = mapManager;

            if (seed == 0)
            {
                seed = Environment.TickCount;
            }

            _rng = new Random(seed ^ 0x2A4F91);
            _currentDay = 1;
            _currentHour = 0;
            _current = BuildState(WeatherType.Clear, 0.1f, 6, _currentDay, _currentHour);
            _remainingHours = _current.ExpectedDurationHours;

            var changed = new WeatherChangedEvent
            {
                Previous = _current,
                Current = _current
            };
            _eventBus.Publish(ref changed);
        }

        public void Update(double deltaTime)
        {
        }

        public void AdvanceHour(int day, int hour)
        {
            _currentDay = day;
            _currentHour = hour;

            _remainingHours--;
            if (_remainingHours <= 0)
            {
                RotateWeather();
            }

            var tick = new WeatherTickEvent
            {
                Current = _current,
                Day = _currentDay,
                Hour = _currentHour
            };
            _eventBus.Publish(ref tick);

            TryEmitLightning();
        }

        private void RotateWeather()
        {
            WeatherState previous = _current;
            WeatherType nextType = RollNextType();
            float intensity = RollIntensity(nextType);
            int duration = RollDurationHours(nextType);

            _current = BuildState(nextType, intensity, duration, _currentDay, _currentHour);
            _remainingHours = duration;

            var changed = new WeatherChangedEvent
            {
                Previous = previous,
                Current = _current
            };
            _eventBus.Publish(ref changed);
        }

        private WeatherType RollNextType()
        {
            double roll = _rng.NextDouble();
            if (roll < 0.45) return WeatherType.Clear;
            if (roll < 0.65) return WeatherType.HeavyRain;
            if (roll < 0.80) return WeatherType.Heatwave;
            if (roll < 0.95) return WeatherType.ColdWave;
            return WeatherType.Thunderstorm;
        }

        private float RollIntensity(WeatherType type)
        {
            return type switch
            {
                WeatherType.Clear => 0.05f + (float)_rng.NextDouble() * 0.2f,
                WeatherType.HeavyRain => 0.55f + (float)_rng.NextDouble() * 0.45f,
                WeatherType.Thunderstorm => 0.65f + (float)_rng.NextDouble() * 0.35f,
                WeatherType.Heatwave => 0.50f + (float)_rng.NextDouble() * 0.50f,
                WeatherType.ColdWave => 0.50f + (float)_rng.NextDouble() * 0.50f,
                _ => 0.3f
            };
        }

        private int RollDurationHours(WeatherType type)
        {
            return type switch
            {
                WeatherType.Clear => _rng.Next(4, 13),
                WeatherType.HeavyRain => _rng.Next(2, 8),
                WeatherType.Thunderstorm => _rng.Next(1, 5),
                WeatherType.Heatwave => _rng.Next(6, 25),
                WeatherType.ColdWave => _rng.Next(6, 25),
                _ => _rng.Next(3, 8)
            };
        }

        private WeatherState BuildState(WeatherType type, float intensity, int durationHours, int day, int hour)
        {
            float tempDelta = 0f;
            float humidityDelta = 0f;
            float lightning = 0f;

            switch (type)
            {
                case WeatherType.HeavyRain:
                    tempDelta = -4f * intensity;
                    humidityDelta = 0.6f * intensity;
                    break;
                case WeatherType.Thunderstorm:
                    tempDelta = -6f * intensity;
                    humidityDelta = 0.8f * intensity;
                    lightning = 0.20f + 0.60f * intensity;
                    break;
                case WeatherType.Heatwave:
                    tempDelta = 8f * intensity;
                    humidityDelta = -0.4f * intensity;
                    break;
                case WeatherType.ColdWave:
                    tempDelta = -10f * intensity;
                    humidityDelta = 0.2f * intensity;
                    break;
            }

            return new WeatherState
            {
                Type = type,
                Intensity = intensity,
                StartDay = day,
                StartHour = hour,
                ExpectedDurationHours = durationHours,
                TemperatureDelta = tempDelta,
                HumidityDelta = humidityDelta,
                LightningProbabilityPerHour = lightning
            };
        }

        private void TryEmitLightning()
        {
            if (_current.Type != WeatherType.Thunderstorm || _mapManager == null)
            {
                return;
            }

            if (_rng.NextDouble() > _current.LightningProbabilityPerHour)
            {
                return;
            }

            int x = _rng.Next(0, _mapManager.Width);
            int y = _rng.Next(0, _mapManager.Height);
            int z = _mapManager.Depth - 1;

            var strike = new LightningStrikeEvent
            {
                Position = new GridPosition(x, y, z),
                Power = 0.5f + 0.5f * _current.Intensity
            };
            _eventBus.Publish(ref strike);
        }
    }
}
