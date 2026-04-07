using MetaFort.Core.EventBus;
using MetaFort.Core.Items;

namespace MetaFort.Core.Systems
{
    public class WeatherEffectOnItemSystem
    {
        private readonly ItemSystemNode _itemSystem;
        private readonly IEventBus _eventBus;
        private WeatherState? _lastWeather;
        private int _lastDay = 1;
        private int _lastHour;

        public WeatherEffectOnItemSystem(ItemSystemNode itemSystem, IEventBus eventBus)
        {
            _itemSystem = itemSystem;
            _eventBus = eventBus;
        }

        public void Initialize()
        {
            _eventBus.Subscribe<WeatherTickEvent>(OnWeatherTick);
            _eventBus.Subscribe<LightningStrikeEvent>(OnLightningStrike);
        }

        public void Shutdown()
        {
            _eventBus.Unsubscribe<WeatherTickEvent>(OnWeatherTick);
            _eventBus.Unsubscribe<LightningStrikeEvent>(OnLightningStrike);
        }

        private void OnWeatherTick(ref WeatherTickEvent e)
        {
            _itemSystem.ApplyWeatherTick(e.Current, _lastWeather, e.Day, e.Hour);
            _lastWeather = e.Current;
            _lastDay = e.Day;
            _lastHour = e.Hour;
        }

        private void OnLightningStrike(ref LightningStrikeEvent e)
        {
            _itemSystem.ApplyLightningStrike(e, _lastDay, _lastHour);
        }
    }
}
