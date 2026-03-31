using Godot;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.EventBus
{
    public enum CommandType
    {
        Move,
        Attack,
        Eat,
        Sleep,
        Mate,
        MountMech,
        DismountMech,
        FireCannon,
        Reload
    }

    /// <summary>
    /// 单位行为指令事件，负责向核心循环传递外界（玩家/AI）的意图
    /// </summary>
    public struct UnitCommandEvent : IGameEvent
    {
        public uint TargetUnit;
        public CommandType Type;
        public Vector3I TargetPosition;
        public uint TargetEntity;
    }
}
