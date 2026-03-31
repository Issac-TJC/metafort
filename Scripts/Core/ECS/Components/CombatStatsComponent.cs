using MetaFort.Core.ECS;

namespace MetaFort.Core.ECS
{
    /// <summary>
    /// 战斗属性组件，纯数据结构
    /// </summary>
    public struct CombatStatsComponent : IComponent
    {
        public float HP;
        public float MaxHP;
        public float MeleeAttack;
        public float RangedAttack;
        public float RangedAccuracy;
    }
}
