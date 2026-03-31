using MetaFort.Core.EventBus;

namespace MetaFort.Core.EventBus
{
    /// <summary>
    /// 伤害事件，用于在不直接耦合系统的情况下扣除实体生命值
    /// </summary>
    public struct DamageEvent : IGameEvent
    {
        public uint TargetEntity;
        public float DamageAmount;
    }
}
