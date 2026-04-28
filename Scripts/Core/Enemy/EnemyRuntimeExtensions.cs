using System;
using System.Collections.Generic;
using Godot;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Spatial;
using MetaFort.Core.Systems;

namespace MetaFort.Core.Enemy
{
    public sealed class EnemyNavigationIntentSystem : ISystem
    {
        private IEntityManager _entityManager;

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
        }

        public void Update(double deltaTime)
        {
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyNavigationComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                if (!_entityManager.HasComponent<EnemyStateComponent>(enemyId))
                {
                    continue;
                }

                ref EnemyNavigationComponent navigation = ref _entityManager.GetComponent<EnemyNavigationComponent>(enemyId);
                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                navigation.DesiredX = state.TargetX;
                navigation.DesiredY = state.TargetY;
                navigation.DesiredZ = state.TargetZ;
            }
        }
    }

    public sealed class EnemyNavigationSystem : ISystem
    {
        private readonly IPathQueryService _pathService;
        private readonly IMapManager _mapManager;
        private readonly Dictionary<uint, Queue<GridPosition>> _paths = new();
        private IEntityManager _entityManager;

        public EnemyNavigationSystem(IPathQueryService pathService, IMapManager mapManager)
        {
            _pathService = pathService;
            _mapManager = mapManager;
        }

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
        }

        public void Update(double deltaTime)
        {
            float dt = (float)deltaTime;
            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyNavigationComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(enemyId) || !_entityManager.HasComponent<EnemyStateComponent>(enemyId))
                {
                    continue;
                }

                ref EnemyNavigationComponent navigation = ref _entityManager.GetComponent<EnemyNavigationComponent>(enemyId);
                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                ref MetaFort.Core.ECS.PositionComponent position = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(enemyId);

                if (state.CurrentState != EnemyStateType.SeekHeat && state.CurrentState != EnemyStateType.ApproachTarget && state.CurrentState != EnemyStateType.Escape)
                {
                    continue;
                }

                GridPosition desired = new(navigation.DesiredX, navigation.DesiredY, navigation.DesiredZ);
                bool needsRepath = !_paths.TryGetValue(enemyId, out Queue<GridPosition> path)
                    || path.Count == 0
                    || navigation.LastPlannedX != desired.X
                    || navigation.LastPlannedY != desired.Y
                    || navigation.LastPlannedZ != desired.Z;

                if (needsRepath)
                {
                    GridPosition current = new(Mathf.RoundToInt(position.X), Mathf.RoundToInt(position.Y), Mathf.RoundToInt(position.Z));
                    path = _pathService.CalculateLayeredPath(_entityManager, _mapManager, current, desired);
                    _paths[enemyId] = path;
                    navigation.LastPlannedX = desired.X;
                    navigation.LastPlannedY = desired.Y;
                    navigation.LastPlannedZ = desired.Z;
                }

                if (path == null || path.Count == 0)
                {
                    continue;
                }

                GridPosition nextStep = path.Peek();
                float dx = nextStep.X - position.X;
                float dy = nextStep.Y - position.Y;
                float dz = nextStep.Z - position.Z;
                float distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                float speed = navigation.MoveSpeed > 0f ? navigation.MoveSpeed : 3.5f;

                if (distance < 0.1f)
                {
                    position.X = nextStep.X;
                    position.Y = nextStep.Y;
                    position.Z = nextStep.Z;
                    path.Dequeue();
                }
                else if (dz != 0f)
                {
                    position.Z += (dz / distance) * speed * dt;
                    if (Mathf.Abs(position.Z - nextStep.Z) < 0.2f)
                    {
                        position.Z = nextStep.Z;
                    }
                }
                else
                {
                    position.X += (dx / distance) * speed * dt;
                    position.Y += (dy / distance) * speed * dt;
                }
            }
        }
    }

    public sealed class EnemyStateObserverSystem : ISystem
    {
        private IEntityManager _entityManager;
        private IEventBus _eventBus;

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
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(enemyId))
                {
                    continue;
                }

                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                if (state.CurrentState == state.LastPublishedState)
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent position = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(enemyId);
                var evt = new EnemyStateChangedEvent
                {
                    EnemyEntityId = enemyId,
                    PreviousState = state.LastPublishedState,
                    CurrentState = state.CurrentState,
                    Position = new GridPosition(Mathf.RoundToInt(position.X), Mathf.RoundToInt(position.Y), Mathf.RoundToInt(position.Z))
                };
                _eventBus.Publish(ref evt);
                state.LastPublishedState = state.CurrentState;
            }
        }
    }

    public sealed class EnemyCombatResolutionSystem
    {
        private readonly IEntityManager _entityManager;
        private readonly IEventBus _eventBus;

        public EnemyCombatResolutionSystem(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
            _eventBus = eventBus;
        }

        public void Initialize()
        {
            _eventBus.Subscribe<EnemyPerformedAttackEvent>(OnEnemyAttackPerformed);
            _eventBus.Subscribe<EnemySelfDestructEvent>(OnEnemySelfDestruct);
        }

        public void Shutdown()
        {
            _eventBus.Unsubscribe<EnemyPerformedAttackEvent>(OnEnemyAttackPerformed);
            _eventBus.Unsubscribe<EnemySelfDestructEvent>(OnEnemySelfDestruct);
        }

        private void OnEnemyAttackPerformed(ref EnemyPerformedAttackEvent evt)
        {
            bool didApplyDamage = false;
            if (evt.TargetEntityId != 0 && _entityManager.IsAlive(evt.TargetEntityId))
            {
                var damage = new DamageEvent
                {
                    TargetEntity = evt.TargetEntityId,
                    DamageAmount = evt.Damage
                };
                _eventBus.Publish(ref damage);
                didApplyDamage = true;
            }

            var resolved = new EnemyAttackResolvedEvent
            {
                EnemyEntityId = evt.EnemyEntityId,
                TargetEntityId = evt.TargetEntityId,
                Damage = evt.Damage,
                DidApplyDamage = didApplyDamage,
                Position = evt.Position
            };
            _eventBus.Publish(ref resolved);
        }

        private void OnEnemySelfDestruct(ref EnemySelfDestructEvent evt)
        {
            ReadOnlySpan<uint> villagerIds = _entityManager.GetDenseEntityIds<VillagerVisualComponent>();
            for (int i = 0; i < villagerIds.Length; i++)
            {
                uint villagerId = villagerIds[i];
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(villagerId))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent position = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(villagerId);
                float dx = position.X - evt.Position.X;
                float dy = position.Y - evt.Position.Y;
                float dz = position.Z - evt.Position.Z;
                if (Mathf.Sqrt(dx * dx + dy * dy + dz * dz) > evt.Radius)
                {
                    continue;
                }

                var damage = new DamageEvent
                {
                    TargetEntity = villagerId,
                    DamageAmount = evt.Damage
                };
                _eventBus.Publish(ref damage);
            }
        }
    }
}
