using System;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Systems
{
    /// <summary>
    /// 将天气事件作用到沙盒地形/流体系统。
    /// </summary>
    public class WeatherEffectOnSandboxSystem : ISystem
    {
        private MapManager _map;
        private Random _rng;

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _map = GameEntry.Instance?.MapManager as MapManager;

            int seed = MetaFort.UI.GameSession.Seed;
            if (seed == 0) seed = Environment.TickCount;
            _rng = new Random(seed ^ 0x77BC01);

            GameEventHandler<WeatherTickEvent> tickHandler = OnWeatherTick;
            GameEventHandler<LightningStrikeEvent> strikeHandler = OnLightningStrike;
            eventBus.Subscribe(tickHandler);
            eventBus.Subscribe(strikeHandler);
        }

        public void Update(double deltaTime)
        {
            // 事件驱动：由天气tick与雷击事件触发。
        }

        private void OnWeatherTick(ref WeatherTickEvent e)
        {
            if (_map == null) return;

            if (e.Current.Type == WeatherType.HeavyRain || e.Current.Type == WeatherType.Thunderstorm)
            {
                InjectRainWater(e.Current);
            }
        }

        private void InjectRainWater(in WeatherState state)
        {
            int totalColumns = _map.Width * _map.Height;
            float rainFactor = state.Type == WeatherType.HeavyRain ? 1.0f : 0.6f;
            int sampleCount = (int)Math.Clamp(32 + totalColumns * 0.005f * state.Intensity * rainFactor, 32, 512);

            byte fluidLevel = (byte)Math.Clamp(2 + state.Intensity * 5f, 1, 7);

            for (int i = 0; i < sampleCount; i++)
            {
                int x = _rng.Next(0, _map.Width);
                int y = _rng.Next(0, _map.Height);

                int surfaceZ = FindTopSolidZ(x, y);
                if (surfaceZ < 0 || surfaceZ >= _map.Depth - 1) continue;

                int rainZ = surfaceZ + 1;
                TileData rainTile = _map.GetTile(x, y, rainZ);
                if (rainTile.Type == TerrainType.Air || rainTile.Type == TerrainType.Water)
                {
                    int idx = _map.GetFlatIndex(x, y, rainZ);
                    _map.ReplaceFluidRaw(idx, x, y, rainZ, fluidLevel);
                    _map.ActiveFluidTiles.Add(idx);
                }
            }
        }

        private int FindTopSolidZ(int x, int y)
        {
            for (int z = _map.Depth - 1; z >= 0; z--)
            {
                TerrainType t = _map.GetTile(x, y, z).Type;
                if (t != TerrainType.Air && t != TerrainType.Water)
                    return z;
            }
            return -1;
        }

        private void OnLightningStrike(ref LightningStrikeEvent e)
        {
            if (_map == null) return;

            int x = Math.Clamp(e.Position.X, 0, _map.Width - 1);
            int y = Math.Clamp(e.Position.Y, 0, _map.Height - 1);
            int z = FindTopSolidZ(x, y);
            if (z < 0) return;

            TileData tile = _map.GetTile(x, y, z);
            TerrainType newType = tile.Type;

            if (tile.Type == TerrainType.Grass) newType = TerrainType.Dirt;
            else if (tile.Type == TerrainType.Dirt) newType = TerrainType.Sand;

            if (newType != tile.Type)
            {
                _map.ReplaceTile(x, y, z, newType);
            }

            if (z < _map.Depth - 1)
            {
                int airZ = z + 1;
                int idx = _map.GetFlatIndex(x, y, airZ);
                _map.ReplaceFluidRaw(idx, x, y, airZ, (byte)Math.Clamp(1 + e.Power * 3f, 1, 7));
                _map.ActiveFluidTiles.Add(idx);
            }
        }
    }
}
