using System;
using System.Collections.Generic;
using Godot;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Spatial
{
    public enum TerrainType : ushort
    {
        Air = 0,
        Bedrock = 1,
        Stone = 2,
        Dirt = 3,
        Grass = 4,
        Sand = 5,
        Water = 6,
        Coal = 7,
        Iron = 8
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct TileData
    {
        public TerrainType Type;
        public byte FluidLevel; 
        public byte Health;
    }

    public struct TerrainModifiedEvent : IGameEvent
    {
        public GridPosition Position;
        public TerrainType OldType;
        public TerrainType NewType;
    }

    public class MapManager : IMapManager
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; } 

        private TileData[] _tiles;
        private IEventBus _eventBus;
        
        // 用于极速液体元胞自动机的存活集合缓存
        public HashSet<int> ActiveFluidTiles { get; private set; } = new HashSet<int>();

        public void InjectDependencies(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void InitializeGrid(int width, int height, int depth)
        {
            Width = width;
            Height = height;
            Depth = depth;
            
            _tiles = new TileData[width * height * depth];
            ActiveFluidTiles.Clear();
        }

        public void InitMap(int seed = 42)
        {
            FastNoiseLite noiseElevation = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.015f };
            FastNoiseLite noiseTemperature = new FastNoiseLite { Seed = seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.005f };
            FastNoiseLite noiseMoisture = new FastNoiseLite { Seed = seed + 2, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.005f };
            FastNoiseLite noiseCaves = new FastNoiseLite { Seed = seed + 3, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.05f };
            
            System.Random randomOres = new System.Random(seed);

            int baseHeight = Depth / 3; 
            int amplitude = Depth / 4;
            int SeaLevel = 15;

            // ==========================================
            // Phase 1 & 2: 2D 宏观群系计算与 Z 轴垂直柱填充 
            // ==========================================
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    float elevationVal = noiseElevation.GetNoise2D(x, y);
                    float tempVal = noiseTemperature.GetNoise2D(x, y);
                    float moistVal = noiseMoisture.GetNoise2D(x, y);

                    int surfaceZ = baseHeight + Mathf.RoundToInt(elevationVal * amplitude);
                    surfaceZ = Mathf.Clamp(surfaceZ, 1, Depth - 1); 

                    bool isDesert = tempVal > 0.3f && moistVal < -0.2f;
                    bool isOceanFloor = surfaceZ < SeaLevel;

                    for (int z = 0; z < Depth; z++)
                    {
                        int index = GetFlatIndex(x, y, z);
                        
                        _tiles[index].FluidLevel = 0;

                        if (z == 0)
                        {
                            _tiles[index].Type = TerrainType.Bedrock;
                        }
                        else if (z > surfaceZ)
                        {
                            // 跃离地表：全部默认为空气 (水体将留到 Pass 4 交由 BFS 闭包灌注)
                            _tiles[index].Type = TerrainType.Air;
                        }
                        else if (z >= surfaceZ - 3) 
                        {
                            if (isDesert)
                            {
                                _tiles[index].Type = TerrainType.Sand;
                            }
                            else if (isOceanFloor)
                            {
                                _tiles[index].Type = TerrainType.Sand; 
                            }
                            else
                            {
                                if (z == surfaceZ && z >= SeaLevel) _tiles[index].Type = TerrainType.Grass;
                                else _tiles[index].Type = TerrainType.Dirt;
                            }
                        }
                        else 
                        {
                            _tiles[index].Type = TerrainType.Stone;
                        }
                        
                        _tiles[index].Health = MetaFort.Core.Data.ConfigManager.GetDefaultHealth((ushort)_tiles[index].Type);
                    }
                }
            }

            // ==========================================
            // Phase 3: 3D 洞穴雕刻 (Caves) 与随机点阵矿脉 (Ores)
            // ==========================================
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int z = 1; z < Depth; z++) // z=0 已固化为 Bedrock，受绝对保护
                    {
                        int index = GetFlatIndex(x, y, z);
                        TerrainType currentType = _tiles[index].Type;

                        if (currentType == TerrainType.Stone || currentType == TerrainType.Dirt)
                        {
                            float caveVal = noiseCaves.GetNoise3D(x, y, z);
                            if (caveVal > 0.6f)
                            {
                                _tiles[index].Type = TerrainType.Air;
                                _tiles[index].Health = 0;
                                continue; 
                            }
                            
                            if (currentType == TerrainType.Stone)
                            {
                                double rand = randomOres.NextDouble();
                                if (rand < 0.005)
                                {
                                    _tiles[index].Type = TerrainType.Iron;
                                    _tiles[index].Health = MetaFort.Core.Data.ConfigManager.GetDefaultHealth((ushort)TerrainType.Iron);
                                }
                                else if (rand < 0.015)
                                {
                                    _tiles[index].Type = TerrainType.Coal;
                                    _tiles[index].Health = MetaFort.Core.Data.ConfigManager.GetDefaultHealth((ushort)TerrainType.Coal);
                                }
                            }
                        }
                    }
                }
            }

            // ==========================================
            // Phase 4: 基于元胞流动引擎的高速地貌前演化 (Minecraft 机制)
            // ==========================================
            var activeFluids = new HashSet<int>();
            int xyPlane = Width * Height;

            // 1. 取最高处指定海平面的表面气泡位为活水生成源头
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    int index = GetFlatIndex(x, y, SeaLevel);
                    // 如果该坐标目前是空气（证明它未被陆地高山填实）
                    if (_tiles[index].Type == TerrainType.Air)
                    {
                        _tiles[index].Type = TerrainType.Water;
                        _tiles[index].FluidLevel = 7;
                        activeFluids.Add(index);
                    }
                }
            }

            // 2. 无副作用内部流体加速演化（直到海平面下沉静水均衡）
            int[] horizOffsets = new int[] { 1, -1, Width, -Width };
            int maxIterations = 200; // 防止深渊水流无限演化卡死
            
            while (activeFluids.Count > 0 && maxIterations > 0)
            {
                maxIterations--;
                var activeArr = new int[activeFluids.Count];
                activeFluids.CopyTo(activeArr);
                activeFluids.Clear();

                foreach (int index in activeArr)
                {
                    if (_tiles[index].Type != TerrainType.Water) continue; 

                    int currZ = index / xyPlane;
                    int rem = index % xyPlane;
                    int currY = rem / Width;
                    int currX = rem % Width;

                    bool stayedActive = false;

                    // A: 落水重力机制 -> 水柱垂直下打
                    if (currZ > 0)
                    {
                        int downIndex = index - xyPlane;
                        TileData downTile = _tiles[downIndex];
                        if (downTile.Type == TerrainType.Air || (downTile.Type == TerrainType.Water && downTile.FluidLevel < 7))
                        {
                            _tiles[downIndex].Type = TerrainType.Water;
                            _tiles[downIndex].FluidLevel = 7;
                            activeFluids.Add(downIndex);
                            stayedActive = true;
                        }
                        
                        if (stayedActive) 
                        {
                            activeFluids.Add(index); // 源头保持活跃持续宣泄
                            continue; // 不产生侧方蔓延
                        }
                    }

                    // B: 侧向张力蔓延 
                    if (_tiles[index].FluidLevel > 1)
                    {
                        byte nextLevel = (byte)(_tiles[index].FluidLevel - 1);
                        for (int i = 0; i < 4; i++)
                        {
                            int nx = currX; int ny = currY;
                            if (i == 0) nx++; else if (i == 1) nx--; else if (i == 2) ny++; else if (i == 3) ny--;

                            if (nx >= 0 && nx < Width && ny >= 0 && ny < Height)
                            {
                                int nIndex = index + horizOffsets[i];
                                TileData nTile = _tiles[nIndex];
                                
                                if (nTile.Type == TerrainType.Air || (nTile.Type == TerrainType.Water && nTile.FluidLevel < nextLevel))
                                {
                                    _tiles[nIndex].Type = TerrainType.Water;
                                    _tiles[nIndex].FluidLevel = nextLevel;
                                    activeFluids.Add(nIndex);
                                    stayedActive = true;
                                }
                            }
                        }
                    }
                    
                    // 若无任何状态转移，这滴水正式冷却成为休眠静态水池
                    if (stayedActive) activeFluids.Add(index);
                }
            }
        }

        // ==========================================
        // 沙盒挖掘系统对外安全无状态接口
        // ==========================================

        public TileData GetTile(int x, int y, int z)
        {
            if (!IsWithinBounds(x, y, z)) 
            {
                return new TileData { Type = TerrainType.Air, Health = 0, FluidLevel = 0 };
            }
            return _tiles[GetFlatIndex(x, y, z)];
        }

        // 高频无检读取通道：为纯净 ECS/流体演化系统所提供的原生接口
        public TileData GetTileRaw(int flatIndex)
        {
            return _tiles[flatIndex];
        }

        public bool ReplaceTile(int x, int y, int z, TerrainType newType)
        {
            if (!IsWithinBounds(x, y, z)) return false;
            
            int index = GetFlatIndex(x, y, z);
            TerrainType oldType = _tiles[index].Type;
            
            if (oldType == newType) return false;

            _tiles[index].Type = newType;
            _tiles[index].Health = GetDefaultHealth(newType);
            _tiles[index].FluidLevel = 0;

            if (_eventBus != null)
            {
                var modEvent = new TerrainModifiedEvent
                {
                    Position = new GridPosition(x, y, z),
                    OldType = oldType,
                    NewType = newType
                };
                _eventBus.Publish(ref modEvent);
            }

            return true;
        }

        // 极致性能：流体子系统专用原位改写通道
        public void ReplaceFluidRaw(int flatIndex, int x, int y, int z, byte newLevel)
        {
            TerrainType oldType = _tiles[flatIndex].Type;
            _tiles[flatIndex].Type = TerrainType.Water;
            _tiles[flatIndex].FluidLevel = newLevel;

            if (_eventBus != null)
            {
                var modEvent = new TerrainModifiedEvent
                {
                    Position = new GridPosition(x, y, z),
                    OldType = oldType,
                    NewType = TerrainType.Water
                };
                _eventBus.Publish(ref modEvent);
            }
        }

        private byte GetDefaultHealth(TerrainType type)
        {
            return MetaFort.Core.Data.ConfigManager.GetDefaultHealth((ushort)type);
        }

        // ==========================================
        // $O(1)$ 几何寻址核心
        // ==========================================

        public bool IsWithinBounds(GridPosition position) => IsWithinBounds(position.X, position.Y, position.Z);

        public bool IsWithinBounds(int x, int y, int z)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;
        }

        public int GetFlatIndex(GridPosition position) => GetFlatIndex(position.X, position.Y, position.Z);

        public int GetFlatIndex(int x, int y, int z)
        {
            return x + y * Width + z * Width * Height;
        }

        public GridPosition GetGridPosition(int flatIndex)
        {
            int xyPlane = Width * Height;
            int z = flatIndex / xyPlane;
            int rem = flatIndex % xyPlane;
            int y = rem / Width;
            int x = rem % Width;

            return new GridPosition(x, y, z);
        }

        public byte[] SerializeMap()
        {
            return System.Runtime.InteropServices.MemoryMarshal.Cast<TileData, byte>(_tiles.AsSpan()).ToArray();
        }

        public void DeserializeMap(byte[] data)
        {
            if (data.Length == _tiles.Length * 4) 
            {
                var sourceSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, TileData>(data.AsSpan());
                sourceSpan.CopyTo(_tiles.AsSpan());
            }
            else
            {
                Godot.GD.PrintErr($"[MapManager] Deserialization failed: Data length {data.Length} does not match Map Size {_tiles.Length * 4}");
            }
        }
    }
}
