using System.Collections.Generic;

namespace MetaFort.Core.Enemy
{
    public enum EnemyRoleType : byte
    {
        Grunt = 0,
        Bomber = 1,
        Hauler = 2
    }

    public enum EnemyStateType : byte
    {
        Dormant = 0,
        SeekHeat = 1,
        Investigate = 2,
        ApproachTarget = 3,
        AttackTarget = 4,
        StealResource = 5,
        Escape = 6,
        SelfDestructWindup = 7,
        Dead = 8
    }

    public sealed class EnemyStateProfileDefinition
    {
        public string id { get; set; } = string.Empty;
        public float investigateDuration { get; set; } = 2.0f;
        public float attackCooldown { get; set; } = 1.5f;
        public float selfDestructWindup { get; set; } = 1.0f;
        public float escapeDuration { get; set; } = 3.0f;
    }

    public sealed class EnemyScentProfileDefinition
    {
        public string id { get; set; } = string.Empty;
        public float heatWeight { get; set; } = 1.0f;
        public float exhaustWeight { get; set; } = 0.65f;
        public float aggroThreshold { get; set; } = 6.0f;
        public float disengageThreshold { get; set; } = 2.0f;
    }

    public sealed class EnemyActionProfileDefinition
    {
        public string id { get; set; } = string.Empty;
        public float attackRange { get; set; } = 1.0f;
        public float attackDamage { get; set; } = 4.0f;
        public float selfDestructRadius { get; set; } = 2.0f;
        public float selfDestructDamage { get; set; } = 12.0f;
        public int lootCapacity { get; set; } = 0;
        public int stealPerTrip { get; set; } = 0;
    }

    public sealed class EnemyArchetypeDefinition
    {
        public string id { get; set; } = string.Empty;
        public string displayName { get; set; } = string.Empty;
        public string roleType { get; set; } = EnemyRoleType.Grunt.ToString();
        public float maxHp { get; set; } = 10f;
        public float moveSpeed { get; set; } = 1.0f;
        public float attackDamage { get; set; } = 4f;
        public float attackRange { get; set; } = 1f;
        public float scentSensitivityHeat { get; set; } = 1f;
        public float scentSensitivityExhaust { get; set; } = 0.65f;
        public float visionRange { get; set; } = 8f;
        public float hearingRange { get; set; } = 4f;
        public float aggroThreshold { get; set; } = 6f;
        public float disengageThreshold { get; set; } = 2f;
        public int lootCapacity { get; set; }
        public int stealPerTrip { get; set; }
        public float selfDestructRadius { get; set; }
        public float selfDestructDamage { get; set; }
        public List<string> preferredTargets { get; set; } = new();
        public string stateProfileId { get; set; } = string.Empty;
        public string scentProfileId { get; set; } = string.Empty;
        public string actionProfileId { get; set; } = string.Empty;

        public EnemyRoleType ResolveRoleType()
        {
            return System.Enum.TryParse(roleType, true, out EnemyRoleType parsed)
                ? parsed
                : EnemyRoleType.Grunt;
        }
    }

    public sealed class EnemyConfigRoot
    {
        public List<EnemyArchetypeDefinition> enemyArchetypes { get; set; } = new();
        public List<EnemyStateProfileDefinition> enemyStateProfiles { get; set; } = new();
        public List<EnemyScentProfileDefinition> enemyScentProfiles { get; set; } = new();
        public List<EnemyActionProfileDefinition> enemyActionProfiles { get; set; } = new();
    }
}
