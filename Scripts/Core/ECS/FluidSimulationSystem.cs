using Godot;
using System.Collections.Generic;
using MetaFort.Core.EventBus;
using MetaFort.Core.Spatial;
using MetaFort.Core.Systems;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort.Core.ECS
{
    public partial class FluidSimulationSystem : Node, ISystem
    {
        [Export]
        public NodePath TimeSourcePath { get; set; } = "../SimulationTimeNode";

        private SimulationTimeNode _timeSource;

        public override void _Process(double delta)
        {
            if (_timeSource == null && TimeSourcePath != null && !TimeSourcePath.IsEmpty)
            {
                _timeSource = GetNodeOrNull<SimulationTimeNode>(TimeSourcePath);
            }

            double scaledDelta = _timeSource != null ? _timeSource.ScaledDeltaTime : delta;
            Update(scaledDelta);
        }

        private MapManager _map;
        private double _tickTimer = 0;
        private const double TICK_RATE = 0.2; 

        private Queue<int> _sleepQueue = new Queue<int>();
        private HashSet<int> _nextActive = new HashSet<int>();

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _map = GameEntry.Instance.MapManager as MapManager;
            GameEventHandler<TerrainModifiedEvent> handler = OnTerrainModified;
            eventBus.Subscribe(handler);
        }

        private void OnTerrainModified(ref TerrainModifiedEvent e)
        {
            if (_map == null) return;
            if (e.NewType == TerrainType.Air || e.NewType == TerrainType.Water)
            {
                int currIndex = _map.GetFlatIndex(e.Position.X, e.Position.Y, e.Position.Z);
                
                if (e.NewType == TerrainType.Water) 
                    _map.ActiveFluidTiles.Add(currIndex);

                TryWakeUpFluid(currIndex + 1, e.Position.X < _map.Width - 1);
                TryWakeUpFluid(currIndex - 1, e.Position.X > 0);
                TryWakeUpFluid(currIndex + _map.Width, e.Position.Y < _map.Height - 1);
                TryWakeUpFluid(currIndex - _map.Width, e.Position.Y > 0);
                
                int xyPlane = _map.Width * _map.Height;
                TryWakeUpFluid(currIndex + xyPlane, e.Position.Z < _map.Depth - 1);
                TryWakeUpFluid(currIndex - xyPlane, e.Position.Z > 0);
            }
        }

        private void TryWakeUpFluid(int flatIndex, bool withinBounds)
        {
            if (withinBounds)
            {
                var t = _map.GetTileRaw(flatIndex);
                if (t.Type == TerrainType.Water)
                {
                    _map.ActiveFluidTiles.Add(flatIndex);
                }
            }
        }

        public void Update(double deltaTime)
        {
            if (_map == null) return;

            _tickTimer += deltaTime;
            if (_tickTimer >= TICK_RATE)
            {
                _tickTimer -= TICK_RATE;
                SimulateFluids();
            }
        }

        private void SimulateFluids()
        {
            if (_map.ActiveFluidTiles.Count == 0) return;

            int xyPlane = _map.Width * _map.Height;

            var activeSetArray = new int[_map.ActiveFluidTiles.Count];
            _map.ActiveFluidTiles.CopyTo(activeSetArray);
            _nextActive.Clear();

            foreach (int index in activeSetArray)
            {
                TileData currTile = _map.GetTileRaw(index);
                if (currTile.Type != TerrainType.Water)
                {
                    _sleepQueue.Enqueue(index);
                    continue; 
                }

                bool stayedActive = false;
                
                int currZ = index / xyPlane;
                int rem = index % xyPlane;
                int currY = rem / _map.Width;
                int currX = rem % _map.Width;

                // 1. 向下自由落体
                if (currZ > 0)
                {
                    int downIndex = index - xyPlane;
                    TileData downTile = _map.GetTileRaw(downIndex);
                    
                    if (downTile.Type == TerrainType.Air || (downTile.Type == TerrainType.Water && downTile.FluidLevel < 7))
                    {
                        _map.ReplaceFluidRaw(downIndex, currX, currY, currZ - 1, 7);
                        _nextActive.Add(downIndex);
                        stayedActive = true;
                    }
                    
                    // 水往低处流：一旦落体生效，本水体不再侧漏！并且维持自身活跃以持续下落。
                    if (stayedActive) 
                    {
                        _nextActive.Add(index);
                        continue; 
                    }
                }

                // 2. 水平张力扩散 (Cellular Automata)
                if (currTile.FluidLevel > 1)
                {
                    byte nextLevel = (byte)(currTile.FluidLevel - 1);
                    int[] horizOffsets = new int[] { 1, -1, _map.Width, -_map.Width };
                    
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = currX;
                        int ny = currY;
                        if (i == 0) nx++; else if (i == 1) nx--; else if (i == 2) ny++; else if (i == 3) ny--;

                        if (nx >= 0 && nx < _map.Width && ny >= 0 && ny < _map.Height)
                        {
                            int nIndex = index + horizOffsets[i];
                            TileData neighborTile = _map.GetTileRaw(nIndex);

                            if (neighborTile.Type == TerrainType.Air || (neighborTile.Type == TerrainType.Water && neighborTile.FluidLevel < nextLevel))
                            {
                                _map.ReplaceFluidRaw(nIndex, nx, ny, currZ, nextLevel);
                                _nextActive.Add(nIndex);
                                stayedActive = true;
                            }
                        }
                    }
                }

                if (!stayedActive)
                {
                    _sleepQueue.Enqueue(index);
                }
                else 
                {
                    _nextActive.Add(index);
                }
            }

            foreach (int newActive in _nextActive)
            {
                _map.ActiveFluidTiles.Add(newActive);
            }

            while (_sleepQueue.Count > 0)
            {
                _map.ActiveFluidTiles.Remove(_sleepQueue.Dequeue());
            }
        }


    }
}
