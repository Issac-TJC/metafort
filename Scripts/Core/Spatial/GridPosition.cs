namespace MetaFort.Core.Spatial
{
    /// <summary>
    /// 表示基于网格的坐标（包含多楼层 Z 轴）的结构体定义
    /// </summary>
    public struct GridPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public GridPosition(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return $"(X:{X}, Y:{Y}, Z:{Z})";
        }
    }
}
