using Godot;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Spatial;
using MetaFort.Visual;

namespace MetaFort.Test_Control
{
    public partial class TestEnemyAttackController : Node
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath TerrainVisualizerPath { get; set; }

        [Export]
        public NodePath EnemyRendererPath { get; set; }

        private TerrainVisualizer2D _terrainVisualizer;
        private EnemyCanvasRenderer _enemyRenderer;
        private IEventBus _eventBus;
        private IEntityManager _entityManager;
        private IVisionDataSystem _visionDataSystem;

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            _terrainVisualizer = GetNodeOrNull<TerrainVisualizer2D>(TerrainVisualizerPath);
            _enemyRenderer = GetNodeOrNull<EnemyCanvasRenderer>(EnemyRendererPath);

            if (gameEntry == null || _terrainVisualizer == null || _enemyRenderer == null)
            {
                GD.PrintErr("[TestEnemyAttackController] Missing GameEntry, TerrainVisualizer2D, or EnemyCanvasRenderer.");
                return;
            }

            _eventBus = gameEntry.EventBus;
            _entityManager = gameEntry.EntityManager;
            _visionDataSystem = gameEntry.VisionData;
            _enemyRenderer.InjectDependencies(_entityManager, _visionDataSystem);

            if (_eventBus != null)
            {
                _eventBus.Subscribe<IndustrialSignatureChangedEvent>(OnIndustrialSignatureChanged);
                _eventBus.Subscribe<EnemySpawnRequestedEvent>(OnEnemySpawnRequested);
                _eventBus.Subscribe<EnemySpawnedEvent>(OnEnemySpawned);
            }
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<IndustrialSignatureChangedEvent>(OnIndustrialSignatureChanged);
                _eventBus.Unsubscribe<EnemySpawnRequestedEvent>(OnEnemySpawnRequested);
                _eventBus.Unsubscribe<EnemySpawnedEvent>(OnEnemySpawned);
            }
        }

        public override void _Process(double delta)
        {
            if (_terrainVisualizer != null && _enemyRenderer != null)
            {
                _enemyRenderer.CurrentZLevel = (int)_terrainVisualizer.Get("_currentZLevel");
            }
        }

        private void OnIndustrialSignatureChanged(ref IndustrialSignatureChangedEvent evt)
        {
            GD.Print($"[TestEnemyAttackController] Industrial signature changed: {evt.PreviousSignature:0.00} -> {evt.CurrentSignature:0.00}");
        }

        private void OnEnemySpawnRequested(ref EnemySpawnRequestedEvent evt)
        {
            GD.Print($"[TestEnemyAttackController] Spawn request: archetype={evt.EnemyArchetypeId}, count={evt.Count}, signature={evt.IndustrialSignature:0.00}");
        }

        private void OnEnemySpawned(ref EnemySpawnedEvent evt)
        {
            GD.Print($"[TestEnemyAttackController] Enemy spawned: id={evt.EnemyEntityId}, archetype={evt.EnemyArchetypeId}, pos={evt.SpawnPosition}");
        }
    }
}
