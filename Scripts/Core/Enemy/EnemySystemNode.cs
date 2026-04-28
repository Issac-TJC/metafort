using System.Collections.Generic;
using System;
using Godot;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Heat;
using MetaFort.Core.Spatial;
using MetaFort.Core.Systems;

namespace MetaFort.Core.Enemy
{
    [GlobalClass]
    public partial class EnemySystemNode : Node
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath HeatFieldPath { get; set; }

        [Export]
        public NodePath StockpilePath { get; set; }

        [Export]
        public int StockpileTargetX { get; set; } = 0;

        [Export]
        public int StockpileTargetY { get; set; } = 0;

        [Export]
        public int StockpileTargetZ { get; set; } = 0;

        private readonly List<ISystem> _systems = new();
        private EnemySpawnDirectorSystem _spawnDirector;
        private EnemyCombatResolutionSystem _combatResolutionSystem;
        private SimulationTimeNode _timeSource;
        private IEventBus _eventBus;
        private IEntityManager _entityManager;
        private IMapManager _mapManager;

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            HeatFieldNode heatNode = ResolveHeatFieldNode();
            PlayerStockpileNode stockpileNode = ResolveStockpileNode();

            if (gameEntry == null || gameEntry.EntityManager == null || gameEntry.EventBus == null || heatNode?.HeatQuery == null)
            {
                GD.PrintErr("[EnemySystemNode] Missing dependencies. Node disabled.");
                SetProcess(false);
                return;
            }

            _eventBus = gameEntry.EventBus;
            _entityManager = gameEntry.EntityManager;
            _mapManager = gameEntry.MapManager;
            _timeSource = GetNodeOrNull<SimulationTimeNode>("../SimulationTimeNode");
            Initialize(gameEntry.EntityManager, gameEntry.EventBus, gameEntry.MapManager, heatNode.HeatQuery, stockpileNode);
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<IndustrialSignatureChangedEvent>(OnIndustrialSignatureChanged);
                _eventBus.Unsubscribe<EnemySpawnRequestedEvent>(OnEnemySpawnRequested);
            }

            _combatResolutionSystem?.Shutdown();
            _systems.Clear();
            _spawnDirector = null;
            _combatResolutionSystem = null;
        }

        public override void _Process(double delta)
        {
            double effectiveDelta = _timeSource != null ? _timeSource.ScaledDeltaTime : delta;
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].Update(effectiveDelta);
            }
        }

        public void Initialize(IEntityManager entityManager, IEventBus eventBus, IMapManager mapManager, IHeatFieldQuery heatFieldQuery, PlayerStockpileNode stockpileNode)
        {
            _systems.Clear();
            _entityManager = entityManager;
            _mapManager = mapManager;
            GridPosition stockpilePosition = new(StockpileTargetX, StockpileTargetY, StockpileTargetZ);
            IResourceRaidTarget raidTarget = stockpileNode;

            EnemyPerceptionSystem perception = new(heatFieldQuery);
            EnemyTargetSelectionSystem targetSelection = new(new BaseTargetProvider(heatFieldQuery), new StockpileTargetProvider(raidTarget, stockpilePosition));
            EnemyNavigationIntentSystem navigationIntent = new();
            EnemyNavigationSystem navigation = new(new GridPathService(), mapManager);
            EnemyStateMachineSystem stateMachine = new();
            EnemyActionSystem action = new(raidTarget);
            EnemyStateObserverSystem observer = new();

            perception.Initialize(entityManager, eventBus);
            targetSelection.Initialize(entityManager, eventBus);
            navigationIntent.Initialize(entityManager, eventBus);
            navigation.Initialize(entityManager, eventBus);
            action.Initialize(entityManager, eventBus);
            stateMachine.Initialize(entityManager, eventBus);
            observer.Initialize(entityManager, eventBus);

            _systems.Add(perception);
            _systems.Add(targetSelection);
            _systems.Add(navigationIntent);
            _systems.Add(navigation);
            _systems.Add(action);
            _systems.Add(stateMachine);
            _systems.Add(observer);

            _spawnDirector = new EnemySpawnDirectorSystem(eventBus);
            _combatResolutionSystem = new EnemyCombatResolutionSystem(entityManager, eventBus);
            _combatResolutionSystem.Initialize();
            eventBus.Subscribe<IndustrialSignatureChangedEvent>(OnIndustrialSignatureChanged);
            eventBus.Subscribe<EnemySpawnRequestedEvent>(OnEnemySpawnRequested);
        }

        private void OnIndustrialSignatureChanged(ref IndustrialSignatureChangedEvent evt)
        {
            _spawnDirector?.Evaluate(evt.CurrentSignature);
        }

        private void OnEnemySpawnRequested(ref EnemySpawnRequestedEvent evt)
        {
            if (_entityManager == null || string.IsNullOrWhiteSpace(evt.EnemyArchetypeId))
            {
                return;
            }

            int count = Mathf.Clamp(evt.Count, 1, 8);
            for (int i = 0; i < count; i++)
            {
                SpawnEnemy(evt.EnemyArchetypeId, new GridPosition(0, i, 0));
            }
        }

        public uint SpawnEnemy(string archetypeId, GridPosition spawnPosition)
        {
            if (_entityManager == null || !EnemyConfigManager.TryGetEnemyArchetype(archetypeId, out EnemyArchetypeDefinition archetype))
            {
                return 0;
            }

            EnemyConfigManager.TryGetStateProfile(archetype.stateProfileId, out EnemyStateProfileDefinition stateProfile);
            EnemyConfigManager.TryGetScentProfile(archetype.scentProfileId, out EnemyScentProfileDefinition scentProfile);
            EnemyConfigManager.TryGetActionProfile(archetype.actionProfileId, out EnemyActionProfileDefinition actionProfile);

            uint entityId = _entityManager.CreateEntity();
            _entityManager.AddComponent(entityId, new MetaFort.Core.ECS.PositionComponent
            {
                X = spawnPosition.X,
                Y = spawnPosition.Y,
                Z = spawnPosition.Z
            });
            _entityManager.AddComponent(entityId, new EnemyTagComponent());
            _entityManager.AddComponent(entityId, new EnemyArchetypeComponent
            {
                ArchetypeId = archetype.id,
                RoleType = archetype.ResolveRoleType()
            });
            _entityManager.AddComponent(entityId, new EnemyStateComponent
            {
                CurrentState = EnemyStateType.Dormant,
                LastPublishedState = EnemyStateType.Dormant,
                TargetX = spawnPosition.X,
                TargetY = spawnPosition.Y,
                TargetZ = spawnPosition.Z,
                FallbackX = spawnPosition.X,
                FallbackY = spawnPosition.Y,
                FallbackZ = spawnPosition.Z
            });
            _entityManager.AddComponent(entityId, new EnemyPerceptionComponent());
            _entityManager.AddComponent(entityId, new EnemyVisualComponent
            {
                HeadId = 0,
                TorsoId = 0,
                HairId = 0,
                ClothesId = 0,
                VariantId = archetype.ResolveRoleType() == EnemyRoleType.Bomber ? 1 : archetype.ResolveRoleType() == EnemyRoleType.Hauler ? 2 : 0,
                SkinColorHex = archetype.ResolveRoleType() == EnemyRoleType.Bomber ? Colors.OrangeRed.ToArgb32() : Colors.IndianRed.ToArgb32()
            });
            _entityManager.AddComponent(entityId, new EnemyCombatComponent
            {
                AttackDamage = actionProfile?.attackDamage > 0f ? actionProfile.attackDamage : archetype.attackDamage,
                AttackRange = actionProfile?.attackRange > 0f ? actionProfile.attackRange : archetype.attackRange,
                Cooldown = stateProfile?.attackCooldown ?? 1.0f,
                CooldownRemaining = 0f,
                SelfDestructRadius = actionProfile?.selfDestructRadius > 0f ? actionProfile.selfDestructRadius : archetype.selfDestructRadius,
                SelfDestructDamage = actionProfile?.selfDestructDamage > 0f ? actionProfile.selfDestructDamage : archetype.selfDestructDamage
            });
            _entityManager.AddComponent(entityId, new EnemyCarryComponent
            {
                CarryingItemId = string.Empty,
                CarryingAmount = 0,
                Capacity = actionProfile?.lootCapacity > 0 ? actionProfile.lootCapacity : archetype.lootCapacity,
                StealPerTrip = actionProfile?.stealPerTrip > 0 ? actionProfile.stealPerTrip : archetype.stealPerTrip
            });
            _entityManager.AddComponent(entityId, new EnemyNavigationComponent
            {
                DesiredX = spawnPosition.X,
                DesiredY = spawnPosition.Y,
                DesiredZ = spawnPosition.Z,
                LastPlannedX = int.MinValue,
                LastPlannedY = int.MinValue,
                LastPlannedZ = int.MinValue,
                MoveSpeed = archetype.moveSpeed > 0f ? archetype.moveSpeed : 3.5f
            });
            _entityManager.AddComponent(entityId, new EnemyThreatPreferenceComponent
            {
                HeatWeight = scentProfile?.heatWeight > 0f ? scentProfile.heatWeight : archetype.scentSensitivityHeat,
                ExhaustWeight = scentProfile?.exhaustWeight > 0f ? scentProfile.exhaustWeight : archetype.scentSensitivityExhaust,
                BuildingWeight = 1.0f,
                StockpileWeight = archetype.ResolveRoleType() == EnemyRoleType.Hauler ? 1.4f : 0.6f,
                VillagerWeight = archetype.ResolveRoleType() == EnemyRoleType.Grunt ? 1.2f : 0.5f,
                AggroThreshold = scentProfile?.aggroThreshold > 0f ? scentProfile.aggroThreshold : archetype.aggroThreshold,
                DisengageThreshold = scentProfile?.disengageThreshold > 0f ? scentProfile.disengageThreshold : archetype.disengageThreshold
            });

            var evt = new EnemySpawnedEvent
            {
                EnemyEntityId = entityId,
                EnemyArchetypeId = archetype.id,
                RoleType = archetype.ResolveRoleType(),
                SpawnPosition = spawnPosition
            };
            _eventBus?.Publish(ref evt);

            return entityId;
        }

        private MetaFort.GameEntry ResolveGameEntry()
        {
            if (CoreSourcePath != null && !CoreSourcePath.IsEmpty)
            {
                return GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            }

            return GetNodeOrNull<MetaFort.GameEntry>("..") ?? MetaFort.GameEntry.Instance;
        }

        private HeatFieldNode ResolveHeatFieldNode()
        {
            if (HeatFieldPath != null && !HeatFieldPath.IsEmpty)
            {
                return GetNodeOrNull<HeatFieldNode>(HeatFieldPath);
            }

            return GetNodeOrNull<HeatFieldNode>("../HeatFieldNode");
        }

        private PlayerStockpileNode ResolveStockpileNode()
        {
            if (StockpilePath != null && !StockpilePath.IsEmpty)
            {
                return GetNodeOrNull<PlayerStockpileNode>(StockpilePath);
            }

            return GetNodeOrNull<PlayerStockpileNode>("../PlayerStockpileNode");
        }
    }
}
