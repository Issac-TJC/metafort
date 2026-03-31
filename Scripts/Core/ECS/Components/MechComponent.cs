using MetaFort.Core.ECS;

namespace MetaFort.Core.ECS
{
    /// <summary>
    /// 机甲组件，拥有此组件代表单位可以驾驶/搭载机甲武器
    /// </summary>
    public struct MechComponent : IComponent
    {
        public int AmmoCount;
        public int MaxAmmo;
        public bool IsMounted;
    }
}
