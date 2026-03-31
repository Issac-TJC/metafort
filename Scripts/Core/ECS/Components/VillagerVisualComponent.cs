using MetaFort.Core.ECS;

namespace MetaFort.Core.ECS
{
    /// <summary>
    /// 小人视觉组件，存储各种外观部件ID与颜色参数
    /// </summary>
    public struct VillagerVisualComponent : IComponent
    {
        public int HeadId;
        public int TorsoId;
        public int HairId;
        public int ClothesId;
        
        // 可选：用于动态染色的Hex值（例如 "#FFDAB9"）或预设颜色ID
        public uint SkinColorHex;
    }
}
