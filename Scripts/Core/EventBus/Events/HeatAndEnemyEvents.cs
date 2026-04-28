using Godot;
using MetaFort.Core.Enemy;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.EventBus.Events
{
    public struct HeatFieldChangedEvent : IGameEvent
    {
        public bool FullRebuild;
        public GridPosition Min;
        public GridPosition Max;
        public float BaseIndustrialSignature;
    }

    public struct IndustrialSignatureChangedEvent : IGameEvent
    {
        public float PreviousSignature;
        public float CurrentSignature;
    }

    public struct EnemySpawnRequestedEvent : IGameEvent
    {
        public string EnemyArchetypeId;
        public EnemyRoleType RoleType;
        public int Count;
        public float ThreatBudget;
        public float IndustrialSignature;
    }

    public struct EnemySpawnedEvent : IGameEvent
    {
        public uint EnemyEntityId;
        public string EnemyArchetypeId;
        public EnemyRoleType RoleType;
        public GridPosition SpawnPosition;
    }

    public struct EnemyTargetAcquiredEvent : IGameEvent
    {
        public uint EnemyEntityId;
        public EnemyStateType State;
        public string TargetKind;
        public uint TargetEntityId;
        public GridPosition TargetPosition;
    }

    public struct EnemyPerformedAttackEvent : IGameEvent
    {
        public uint EnemyEntityId;
        public uint TargetEntityId;
        public float Damage;
        public GridPosition Position;
    }

    public struct EnemyAttackResolvedEvent : IGameEvent
    {
        public uint EnemyEntityId;
        public uint TargetEntityId;
        public float Damage;
        public bool DidApplyDamage;
        public GridPosition Position;
    }

    public struct EnemyStoleStockpileEvent : IGameEvent
    {
        public uint EnemyEntityId;
        public string ItemId;
        public int RequestedAmount;
        public int ExtractedAmount;
        public GridPosition Position;
    }

    public struct EnemySelfDestructEvent : IGameEvent
    {
        public uint EnemyEntityId;
        public float Radius;
        public float Damage;
        public GridPosition Position;
    }

    public struct EnemyStateChangedEvent : IGameEvent
    {
        public uint EnemyEntityId;
        public EnemyStateType PreviousState;
        public EnemyStateType CurrentState;
        public GridPosition Position;
    }
}
