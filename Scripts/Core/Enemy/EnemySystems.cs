using System;
using Godot;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Heat;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Enemy
{
    public sealed class BaseTargetProvider : IEnemyTargetProvider
    {
        private readonly IHeatFieldQuery _heatFieldQuery;

        public BaseTargetProvider(IHeatFieldQuery heatFieldQuery)
        {
            _heatFieldQuery = heatFieldQuery;
        }

        public bool TryGetTarget(uint enemyEntityId, out string targetKind, out uint targetEntityId, out GridPosition targetPosition)
        {
            targetKind = "heat";
            targetEntityId = 0;
            targetPosition = default;

            HeatFieldSnapshot snapshot = _heatFieldQuery?.Snapshot;
            if (snapshot == null || snapshot.IsEmpty)
            {
                return false;
            }

            int bestIndex = -1;
            float bestScore = 0f;
            for (int i = 0; i < snapshot.Heat.Length; i++)
            {
                float score = snapshot.Heat[i] + (snapshot.Exhaust[i] * 0.75f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0 || bestScore <= 0f)
            {
                return false;
            }

            int xy = snapshot.Width * snapshot.Height;
            int z = bestIndex / xy;
            int rem = bestIndex % xy;
            int y = rem / snapshot.Width;
            int x = rem % snapshot.Width;
            targetPosition = new GridPosition(x, y, z);
            return true;
        }
    }

    public sealed class StockpileTargetProvider : IEnemyTargetProvider
    {
        private readonly IResourceRaidTarget _raidTarget;
        private readonly GridPosition _stockpilePosition;

        public StockpileTargetProvider(IResourceRaidTarget raidTarget, GridPosition stockpilePosition)
        {
            _raidTarget = raidTarget;
            _stockpilePosition = stockpilePosition;
        }

        public bool TryGetTarget(uint enemyEntityId, out string targetKind, out uint targetEntityId, out GridPosition targetPosition)
        {
            targetKind = "stockpile";
            targetEntityId = 0;
            targetPosition = _stockpilePosition;
            return _raidTarget != null;
        }
    }

    public sealed class EnemyPerceptionSystem : ISystem
    {
        private IEntityManager _entityManager;
        private readonly IHeatFieldQuery _heatFieldQuery;
        private double _elapsedTime;

        public EnemyPerceptionSystem(IHeatFieldQuery heatFieldQuery)
        {
            _heatFieldQuery = heatFieldQuery;
        }

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
        }

        public void Update(double deltaTime)
        {
            _elapsedTime += deltaTime;
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyPerceptionComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(enemyId))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent position = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(enemyId);
                GridPosition grid = new(Mathf.RoundToInt(position.X), Mathf.RoundToInt(position.Y), Mathf.RoundToInt(position.Z));
                ref EnemyPerceptionComponent perception = ref _entityManager.GetComponent<EnemyPerceptionComponent>(enemyId);
                ref EnemyThreatPreferenceComponent preference = ref _entityManager.GetComponent<EnemyThreatPreferenceComponent>(enemyId);

                perception.CurrentHeat = _heatFieldQuery.GetHeat(grid);
                perception.CurrentExhaust = _heatFieldQuery.GetExhaust(grid);
                perception.CurrentAttractionScore = _heatFieldQuery.GetAttractionScore(grid, new EnemyScentProfile(preference.HeatWeight, preference.ExhaustWeight));
                perception.LastSenseTime = (float)_elapsedTime;

                if (_heatFieldQuery.TryGetGradient(grid, out Vector3I gradient))
                {
                    perception.GradientX = gradient.X;
                    perception.GradientY = gradient.Y;
                    perception.GradientZ = gradient.Z;
                }
                else
                {
                    perception.GradientX = 0;
                    perception.GradientY = 0;
                    perception.GradientZ = 0;
                }
            }
        }
    }

    public sealed class EnemyTargetSelectionSystem : ISystem
    {
        private IEntityManager _entityManager;
        private IEventBus _eventBus;
        private readonly IEnemyTargetProvider _baseTargetProvider;
        private readonly IEnemyTargetProvider _stockpileTargetProvider;

        public EnemyTargetSelectionSystem(IEnemyTargetProvider baseTargetProvider, IEnemyTargetProvider stockpileTargetProvider)
        {
            _baseTargetProvider = baseTargetProvider;
            _stockpileTargetProvider = stockpileTargetProvider;
        }

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
            _eventBus = eventBus;
        }

        public void Update(double deltaTime)
        {
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyStateComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                if (!_entityManager.HasComponent<EnemyThreatPreferenceComponent>(enemyId))
                {
                    continue;
                }

                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                ref EnemyThreatPreferenceComponent preference = ref _entityManager.GetComponent<EnemyThreatPreferenceComponent>(enemyId);
                ref EnemyArchetypeComponent archetype = ref _entityManager.GetComponent<EnemyArchetypeComponent>(enemyId);

                if (state.CurrentState == EnemyStateType.Dead || state.CurrentState == EnemyStateType.AttackTarget || state.CurrentState == EnemyStateType.StealResource)
                {
                    continue;
                }

                bool preferStockpile = archetype.RoleType == EnemyRoleType.Hauler && preference.StockpileWeight >= preference.BuildingWeight;
                IEnemyTargetProvider provider = preferStockpile ? _stockpileTargetProvider : _baseTargetProvider;
                if (provider == null || !provider.TryGetTarget(enemyId, out string targetKind, out uint targetEntityId, out GridPosition targetPosition))
                {
                    continue;
                }

                state.TargetEntityId = targetEntityId;
                state.TargetX = targetPosition.X;
                state.TargetY = targetPosition.Y;
                state.TargetZ = targetPosition.Z;

                var evt = new EnemyTargetAcquiredEvent
                {
                    EnemyEntityId = enemyId,
                    State = state.CurrentState,
                    TargetKind = targetKind,
                    TargetEntityId = targetEntityId,
                    TargetPosition = targetPosition
                };
                _eventBus.Publish(ref evt);
            }
        }
    }

    public sealed class EnemyStateMachineSystem : ISystem
    {
        private IEntityManager _entityManager;

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
        }

        public void Update(double deltaTime)
        {
            float dt = (float)deltaTime;
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyStateComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                ref EnemyPerceptionComponent perception = ref _entityManager.GetComponent<EnemyPerceptionComponent>(enemyId);
                ref EnemyThreatPreferenceComponent preference = ref _entityManager.GetComponent<EnemyThreatPreferenceComponent>(enemyId);
                ref EnemyArchetypeComponent archetype = ref _entityManager.GetComponent<EnemyArchetypeComponent>(enemyId);

                state.StateTimer += dt;
                switch (state.CurrentState)
                {
                    case EnemyStateType.Dormant:
                        if (perception.CurrentAttractionScore >= preference.AggroThreshold)
                        {
                            state.CurrentState = EnemyStateType.SeekHeat;
                            state.StateTimer = 0f;
                        }
                        break;
                    case EnemyStateType.SeekHeat:
                    case EnemyStateType.Investigate:
                        if (state.TargetX != 0 || state.TargetY != 0 || state.TargetZ != 0)
                        {
                            state.CurrentState = archetype.RoleType == EnemyRoleType.Hauler
                                ? EnemyStateType.StealResource
                                : EnemyStateType.ApproachTarget;
                            state.StateTimer = 0f;
                        }
                        else if (perception.CurrentAttractionScore < preference.DisengageThreshold)
                        {
                            state.CurrentState = EnemyStateType.Dormant;
                            state.StateTimer = 0f;
                        }
                        break;
                    case EnemyStateType.ApproachTarget:
                        if (archetype.RoleType == EnemyRoleType.Bomber)
                        {
                            state.CurrentState = EnemyStateType.SelfDestructWindup;
                        }
                        else
                        {
                            state.CurrentState = EnemyStateType.AttackTarget;
                        }
                        state.StateTimer = 0f;
                        break;
                    case EnemyStateType.StealResource:
                        if (state.StateTimer >= 0.2f)
                        {
                            state.CurrentState = EnemyStateType.Escape;
                            state.StateTimer = 0f;
                        }
                        break;
                    case EnemyStateType.SelfDestructWindup:
                        if (state.StateTimer >= 1.0f)
                        {
                            state.CurrentState = EnemyStateType.Dead;
                            state.StateTimer = 0f;
                        }
                        break;
                    case EnemyStateType.AttackTarget:
                    case EnemyStateType.Escape:
                    case EnemyStateType.Dead:
                    default:
                        break;
                }
            }
        }
    }

    public sealed class EnemyActionSystem : ISystem
    {
        private IEntityManager _entityManager;
        private IEventBus _eventBus;
        private readonly IResourceRaidTarget _raidTarget;

        public EnemyActionSystem(IResourceRaidTarget raidTarget)
        {
            _raidTarget = raidTarget;
        }

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
            _eventBus = eventBus;
        }

        public void Update(double deltaTime)
        {
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyStateComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                ref EnemyCombatComponent combat = ref _entityManager.GetComponent<EnemyCombatComponent>(enemyId);
                ref EnemyCarryComponent carry = ref _entityManager.GetComponent<EnemyCarryComponent>(enemyId);

                GridPosition position = new(state.TargetX, state.TargetY, state.TargetZ);

                if (state.CurrentState == EnemyStateType.AttackTarget && combat.CooldownRemaining <= 0f)
                {
                    var attackEvent = new EnemyPerformedAttackEvent
                    {
                        EnemyEntityId = enemyId,
                        TargetEntityId = state.TargetEntityId,
                        Damage = combat.AttackDamage,
                        Position = position
                    };
                    _eventBus.Publish(ref attackEvent);
                    combat.CooldownRemaining = Math.Max(0.1f, combat.Cooldown);
                }
                else
                {
                    combat.CooldownRemaining = Math.Max(0f, combat.CooldownRemaining - (float)deltaTime);
                }

                if (state.CurrentState == EnemyStateType.StealResource && _raidTarget != null && carry.Capacity > carry.CarryingAmount)
                {
                    int requestAmount = Math.Min(carry.StealPerTrip, carry.Capacity - carry.CarryingAmount);
                    if (_raidTarget.TryExtract("res_wood", requestAmount, out int extracted) && extracted > 0)
                    {
                        carry.CarryingItemId = "res_wood";
                        carry.CarryingAmount += extracted;
                        var stealEvent = new EnemyStoleStockpileEvent
                        {
                            EnemyEntityId = enemyId,
                            ItemId = carry.CarryingItemId,
                            RequestedAmount = requestAmount,
                            ExtractedAmount = extracted,
                            Position = position
                        };
                        _eventBus.Publish(ref stealEvent);
                    }
                }

                if (state.CurrentState == EnemyStateType.SelfDestructWindup && state.StateTimer >= 1.0f)
                {
                    var selfDestructEvent = new EnemySelfDestructEvent
                    {
                        EnemyEntityId = enemyId,
                        Radius = combat.SelfDestructRadius,
                        Damage = combat.SelfDestructDamage,
                        Position = position
                    };
                    _eventBus.Publish(ref selfDestructEvent);
                }
            }
        }
    }

    public sealed class EnemySpawnDirectorSystem
    {
        private readonly IEventBus _eventBus;

        public EnemySpawnDirectorSystem(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Evaluate(float industrialSignature)
        {
            if (industrialSignature <= 0f)
            {
                return;
            }

            string archetypeId = industrialSignature >= 30f
                ? "bug_bomber"
                : industrialSignature >= 18f
                    ? "bug_hauler"
                    : "bug_grunt";

            EnemyConfigManager.TryGetEnemyArchetype(archetypeId, out EnemyArchetypeDefinition archetype);
            var evt = new EnemySpawnRequestedEvent
            {
                EnemyArchetypeId = archetypeId,
                RoleType = archetype?.ResolveRoleType() ?? EnemyRoleType.Grunt,
                Count = Math.Max(1, Mathf.CeilToInt(industrialSignature / 12f)),
                ThreatBudget = industrialSignature,
                IndustrialSignature = industrialSignature
            };
            _eventBus.Publish(ref evt);
        }
    }
}
