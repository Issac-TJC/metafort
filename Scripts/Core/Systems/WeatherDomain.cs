using MetaFort.Core.EventBus;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Systems
{
    public enum WeatherType : byte
    {
        Clear = 0,
        Thunderstorm = 1,
        Heatwave = 2,
        ColdWave = 3,
        HeavyRain = 4
    }

    public struct WeatherState
    {
        public WeatherType Type;
        public float Intensity;
        public int StartDay;
        public int StartHour;
        public int ExpectedDurationHours;
        public float TemperatureDelta;
        public float HumidityDelta;
        public float LightningProbabilityPerHour;
    }

    public struct WeatherChangedEvent : IGameEvent
    {
        public WeatherState Previous;
        public WeatherState Current;
    }

    public struct WeatherTickEvent : IGameEvent
    {
        public WeatherState Current;
        public int Day;
        public int Hour;
    }

    public struct LightningStrikeEvent : IGameEvent
    {
        public GridPosition Position;
        public float Power;
    }
}
