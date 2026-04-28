using MetaFort.Core.ECS;

namespace MetaFort.Core.Enemy
{
    public struct EnemyTagComponent : IComponent
    {
    }

    public struct EnemyVisualComponent : IComponent
    {
        public int HeadId;
        public int TorsoId;
        public int HairId;
        public int ClothesId;
        public int VariantId;
        public uint SkinColorHex;
    }

    public struct EnemyArchetypeComponent : IComponent
    {
        public string ArchetypeId;
        public EnemyRoleType RoleType;
    }

    public struct EnemyStateComponent : IComponent
    {
        public EnemyStateType CurrentState;
        public float StateTimer;
        public uint TargetEntityId;
        public int TargetX;
        public int TargetY;
        public int TargetZ;
        public int FallbackX;
        public int FallbackY;
        public int FallbackZ;
        public EnemyStateType LastPublishedState;
    }

    public struct EnemyPerceptionComponent : IComponent
    {
        public float CurrentHeat;
        public float CurrentExhaust;
        public int GradientX;
        public int GradientY;
        public int GradientZ;
        public float CurrentAttractionScore;
        public float LastSenseTime;
    }

    public struct EnemyCombatComponent : IComponent
    {
        public float AttackDamage;
        public float AttackRange;
        public float Cooldown;
        public float CooldownRemaining;
        public float SelfDestructRadius;
        public float SelfDestructDamage;
    }

    public struct EnemyCarryComponent : IComponent
    {
        public string CarryingItemId;
        public int CarryingAmount;
        public int Capacity;
        public int StealPerTrip;
    }

    public struct EnemyThreatPreferenceComponent : IComponent
    {
        public float HeatWeight;
        public float ExhaustWeight;
        public float BuildingWeight;
        public float StockpileWeight;
        public float VillagerWeight;
        public float AggroThreshold;
        public float DisengageThreshold;
    }

    public struct EnemyNavigationComponent : IComponent
    {
        public int DesiredX;
        public int DesiredY;
        public int DesiredZ;
        public int LastPlannedX;
        public int LastPlannedY;
        public int LastPlannedZ;
        public float MoveSpeed;
    }

    public struct EnemyNavigationAnchorComponent : IComponent
    {
    }
}
