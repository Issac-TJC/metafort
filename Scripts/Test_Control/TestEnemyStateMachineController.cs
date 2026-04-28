using Godot;
using System;
using MetaFort.Core.ECS;
using MetaFort.Core.Enemy;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Spatial;
using MetaFort.Visual;

namespace MetaFort.Test_Control
{
    public partial class TestEnemyStateMachineController : Node2D
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath TerrainVisualizerPath { get; set; }

        [Export]
        public NodePath VillagerRendererPath { get; set; }

        [Export]
        public NodePath EnemyRendererPath { get; set; }

        [Export]
        public NodePath EnemySystemPath { get; set; }

        [Export]
        public NodePath EditorMapSourcePath { get; set; }

        [Export]
        public NodePath VillagerSpawnMarkerPath { get; set; }

        [Export]
        public NodePath EnemySpawnMarkerPath { get; set; }

        [Export]
        public string SpawnEnemyArchetypeId { get; set; } = "bug_grunt";

        [Export]
        public bool AutoSpawnOnReady { get; set; } = true;

        [Export]
        public bool AutoLinkEnemyTargetToNearestVillager { get; set; } = true;

        [Export]
        public bool EnableEnemyRendering { get; set; } = true;

        [Export]
        public bool EnableFogVisibilityGate { get; set; } = true;

        [Export]
        public Vector3I InitialVillagerPos { get; set; } = new Vector3I(2, 2, 2);

        [Export]
        public Vector3I InitialEnemyPos { get; set; } = new Vector3I(7, 7, 2);

        private MetaFort.GameEntry _gameEntry;
        private IEntityManager _entityManager;
        private IMapManager _mapManager;
        private IEventBus _eventBus;
        private EnemySystemNode _enemySystemNode;
        private TerrainVisualizer2D _terrainVisualizer;
        private VillagerCanvasRenderer _villagerRenderer;
        private EnemyCanvasRenderer _enemyRenderer;
        private TileMapLayer _editorTileMap;
        private Marker2D _villagerSpawnMarker;
        private Marker2D _enemySpawnMarker;
        private Label _debugLabel;
        private uint _villagerId;
        private uint _enemyId;
        private int _attackCount;

        public override void _Ready()
        {
            _gameEntry = GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            _terrainVisualizer = GetNodeOrNull<TerrainVisualizer2D>(TerrainVisualizerPath);
            _villagerRenderer = GetNodeOrNull<VillagerCanvasRenderer>(VillagerRendererPath);
            _enemyRenderer = GetNodeOrNull<EnemyCanvasRenderer>(EnemyRendererPath);
            _enemySystemNode = GetNodeOrNull<EnemySystemNode>(EnemySystemPath);
            _editorTileMap = GetNodeOrNull<TileMapLayer>(EditorMapSourcePath);
            _villagerSpawnMarker = GetNodeOrNull<Marker2D>(VillagerSpawnMarkerPath);
            _enemySpawnMarker = GetNodeOrNull<Marker2D>(EnemySpawnMarkerPath);

            if (_gameEntry == null || _terrainVisualizer == null || _villagerRenderer == null || _enemyRenderer == null || _enemySystemNode == null || _editorTileMap == null)
            {
                GD.PrintErr("[TestEnemyStateMachineController] Missing required scene references.");
                return;
            }

            _entityManager = _gameEntry.EntityManager;
            _mapManager = _gameEntry.MapManager;
            _eventBus = _gameEntry.EventBus;
            _villagerRenderer.InjectDependencies(_entityManager);
            _enemyRenderer.InjectDependencies(_entityManager, _gameEntry.VisionData);
            _enemyRenderer.Visible = EnableEnemyRendering;
            _enemyRenderer.EnableFogVisibilityGate = EnableFogVisibilityGate;

            BuildDebugUi();
            ImportEditorMapToSimulation();

            if (AutoSpawnOnReady)
            {
                SpawnFromSceneMarkers();
            }

            if (_eventBus != null)
            {
                _eventBus.Subscribe<EnemyStateChangedEvent>(OnEnemyStateChanged);
                _eventBus.Subscribe<EnemyAttackResolvedEvent>(OnEnemyAttackResolved);
                _eventBus.Subscribe<EnemySpawnedEvent>(OnEnemySpawned);
            }
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<EnemyStateChangedEvent>(OnEnemyStateChanged);
                _eventBus.Unsubscribe<EnemyAttackResolvedEvent>(OnEnemyAttackResolved);
                _eventBus.Unsubscribe<EnemySpawnedEvent>(OnEnemySpawned);
            }
        }

        public override void _Process(double delta)
        {
            int currentZLevel = (int)_terrainVisualizer.Get("_currentZLevel");
            _villagerRenderer.CurrentZLevel = currentZLevel;
            _enemyRenderer.CurrentZLevel = currentZLevel;
            UpdateDebugUi();
        }

        private void ImportEditorMapToSimulation()
        {
            if (_mapManager == null || _editorTileMap == null)
            {
                return;
            }

            for (int x = 0; x < _mapManager.Width; x++)
            {
                for (int y = 0; y < _mapManager.Height; y++)
                {
                    for (int z = 0; z < _mapManager.Depth; z++)
                    {
                        _mapManager.ReplaceTile(x, y, z, z < InitialVillagerPos.Z ? TerrainType.Stone : TerrainType.Air);
                    }
                }
            }

            Rect2I usedRect = _editorTileMap.GetUsedRect();
            for (int x = usedRect.Position.X; x < usedRect.End.X; x++)
            {
                for (int y = usedRect.Position.Y; y < usedRect.End.Y; y++)
                {
                    Vector2I cell = new Vector2I(x, y);
                    if (_editorTileMap.GetCellSourceId(cell) < 0)
                    {
                        continue;
                    }

                    _mapManager.ReplaceTile(x, y, InitialVillagerPos.Z, ResolveTerrainTypeFromTile(cell));
                }
            }

            if (_editorTileMap.GetParent() is CanvasItem mapRoot)
            {
                mapRoot.Visible = false;
            }
        }

        private TerrainType ResolveTerrainTypeFromTile(Vector2I cell)
        {
            Vector2I atlas = _editorTileMap.GetCellAtlasCoords(cell);
            if (atlas == new Vector2I(0, 0))
            {
                return TerrainType.Dirt;
            }

            if (atlas == new Vector2I(1, 0) || atlas == new Vector2I(2, 0))
            {
                return TerrainType.Stone;
            }

            if (atlas == new Vector2I(1, 1))
            {
                return TerrainType.Grass;
            }

            if (atlas == new Vector2I(0, 2))
            {
                return TerrainType.Water;
            }

            if (atlas == new Vector2I(1, 2))
            {
                return TerrainType.Coal;
            }

            if (atlas == new Vector2I(3, 0))
            {
                return TerrainType.Iron;
            }

            return TerrainType.Air;
        }

        private void SpawnFromSceneMarkers()
        {
            if (_villagerSpawnMarker == null || _enemySpawnMarker == null)
            {
                GD.PrintErr("[TestEnemyStateMachineController] Missing VillagerSpawn or EnemySpawn marker. Auto spawn aborted.");
                return;
            }

            GridPosition villagerPos = ResolveGridPositionFromMarker(_villagerSpawnMarker, InitialVillagerPos);
            GridPosition enemyPos = ResolveGridPositionFromMarker(_enemySpawnMarker, InitialEnemyPos);
            _villagerId = SpawnVillager(villagerPos);
            _enemyId = _enemySystemNode.SpawnEnemy(SpawnEnemyArchetypeId, enemyPos);

            if (AutoLinkEnemyTargetToNearestVillager)
            {
                ConfigureEnemyTargeting(villagerPos);
            }
        }

        private GridPosition ResolveGridPositionFromMarker(Node2D marker, Vector3I fallback)
        {
            if (marker == null || _terrainVisualizer?.TargetTileMap == null)
            {
                return new GridPosition(fallback.X, fallback.Y, fallback.Z);
            }

            Vector2I cell = _terrainVisualizer.TargetTileMap.LocalToMap(_terrainVisualizer.TargetTileMap.ToLocal(marker.GlobalPosition));
            return new GridPosition(cell.X, cell.Y, fallback.Z);
        }

        private uint SpawnVillager(GridPosition position)
        {
            uint villagerId = _entityManager.CreateEntity();
            _entityManager.AddComponent(villagerId, new MetaFort.Core.ECS.PositionComponent
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z
            });
            _entityManager.AddComponent(villagerId, new VillagerVisualComponent
            {
                HeadId = 0,
                TorsoId = 0,
                HairId = 0,
                ClothesId = 0,
                SkinColorHex = new Color("f7e7a1").ToArgb32()
            });
            _entityManager.AddComponent(villagerId, new VillagerStateComponent
            {
                CurrentAction = VillagerAction.Idle,
                TargetX = position.X,
                TargetY = position.Y,
                TargetZ = position.Z
            });
            return villagerId;
        }

        private void ConfigureEnemyTargeting(GridPosition villagerPos)
        {
            if (_enemyId == 0 || !_entityManager.IsAlive(_enemyId))
            {
                return;
            }

            ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(_enemyId);
            state.TargetEntityId = _villagerId;
            state.TargetX = villagerPos.X;
            state.TargetY = villagerPos.Y;
            state.TargetZ = villagerPos.Z;
            state.CurrentState = EnemyStateType.SeekHeat;

            if (_entityManager.HasComponent<EnemyNavigationComponent>(_enemyId))
            {
                ref EnemyNavigationComponent navigation = ref _entityManager.GetComponent<EnemyNavigationComponent>(_enemyId);
                navigation.DesiredX = villagerPos.X;
                navigation.DesiredY = villagerPos.Y;
                navigation.DesiredZ = villagerPos.Z;
                navigation.LastPlannedX = int.MinValue;
                navigation.LastPlannedY = int.MinValue;
                navigation.LastPlannedZ = int.MinValue;
            }
        }

        private void BuildDebugUi()
        {
            CanvasLayer layer = new CanvasLayer { Layer = 160 };
            _debugLabel = new Label();
            _debugLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            _debugLabel.Position = new Vector2(18f, 18f);
            _debugLabel.Size = new Vector2(360f, 120f);
            layer.AddChild(_debugLabel);
            AddChild(layer);
        }

        private void UpdateDebugUi()
        {
            if (_debugLabel == null)
            {
                return;
            }

            string stateText = "n/a";
            string attractionText = "0.00";
            if (_enemyId != 0 && _entityManager != null && _entityManager.IsAlive(_enemyId) && _entityManager.HasComponent<EnemyStateComponent>(_enemyId))
            {
                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(_enemyId);
                stateText = state.CurrentState.ToString();
                if (_entityManager.HasComponent<EnemyPerceptionComponent>(_enemyId))
                {
                    ref EnemyPerceptionComponent perception = ref _entityManager.GetComponent<EnemyPerceptionComponent>(_enemyId);
                    attractionText = perception.CurrentAttractionScore.ToString("0.00");
                }
            }

            _debugLabel.Text = $"Enemy: {_enemyId}\nState: {stateText}\nAttraction: {attractionText}\nAttacks: {_attackCount}\nMap Source: Editor TileMap\nAuto Spawn: {AutoSpawnOnReady}";
        }

        private void OnEnemyStateChanged(ref EnemyStateChangedEvent evt)
        {
            if (evt.EnemyEntityId == _enemyId)
            {
                GD.Print($"[TestEnemyStateMachineController] State changed: {evt.PreviousState} -> {evt.CurrentState}");
            }
        }

        private void OnEnemyAttackResolved(ref EnemyAttackResolvedEvent evt)
        {
            if (evt.EnemyEntityId == _enemyId)
            {
                _attackCount++;
                GD.Print($"[TestEnemyStateMachineController] Attack resolved: damage={evt.Damage}, applied={evt.DidApplyDamage}");
            }
        }

        private void OnEnemySpawned(ref EnemySpawnedEvent evt)
        {
            if (evt.EnemyEntityId == _enemyId)
            {
                GD.Print($"[TestEnemyStateMachineController] Spawned {evt.EnemyArchetypeId} at {evt.SpawnPosition}");
            }
        }
    }
}
