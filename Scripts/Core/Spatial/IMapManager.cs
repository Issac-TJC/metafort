using System;

namespace MetaFort.Core.Spatial
{
    public interface IMapManager
    {
        int Width { get; }
        int Height { get; }
        int Depth { get; }

        void InitializeGrid(int width, int height, int depth);

        bool IsWithinBounds(GridPosition position);
        bool IsWithinBounds(int x, int y, int z);

        int GetFlatIndex(GridPosition position);
        int GetFlatIndex(int x, int y, int z);

        GridPosition GetGridPosition(int flatIndex);

        // ==========================================
        // 沙盒玩法级数据与替换接口（对外公开保证多模块单向依赖）
        // ==========================================
        TileData GetTile(int x, int y, int z);
        bool ReplaceTile(int x, int y, int z, TerrainType newType);
    }
}
