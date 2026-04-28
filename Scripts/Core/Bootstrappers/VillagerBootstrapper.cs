using Godot;
using System.Collections.Generic;
using MetaFort.Core.Systems;
using MetaFort.Core.ECS;

namespace MetaFort.Core.Bootstrappers
{
    public partial class VillagerSystemRunner : Node
    {
        [Export]
        public NodePath TimeSourcePath { get; set; } = "../SimulationTimeNode";

        private readonly List<ISystem> _systems = new List<ISystem>();
        private SimulationTimeNode _timeSource;

        public void AddSystem(ISystem system)
        {
            _systems.Add(system);
        }

        public override void _Ready()
        {
            if (TimeSourcePath != null && !TimeSourcePath.IsEmpty)
            {
                _timeSource = GetNodeOrNull<SimulationTimeNode>(TimeSourcePath);
            }

            _timeSource ??= GetNodeOrNull<SimulationTimeNode>("../TestVillagerRuntime/SimulationTimeNode");
            _timeSource ??= GetNodeOrNull<SimulationTimeNode>("../SimulationTimeNode");
            _timeSource ??= FindChild("SimulationTimeNode", true, false) as SimulationTimeNode;
        }

        public override void _Process(double delta)
        {
            double effectiveDelta = _timeSource != null ? _timeSource.ScaledDeltaTime : delta;
            foreach (var system in _systems)
            {
                system.Update(effectiveDelta);
            }
        }
    }

    public class VillagerBootstrapper : IBootstrapper
    {
        public void Initialize(GameContext context)
        {
            // 初始化系统
            var pathfinding = new PathfindingSystem();
            pathfinding.Initialize(context.EntityManager, context.EventBus, context.MapManager);

            var visibility = new VisibilityCalculationSystem();
            visibility.Initialize(context.EntityManager, context.EventBus, context.MapManager, context.VisionData);

            // 包装为 Node 纳入主循环更新
            var runner = new VillagerSystemRunner();
            runner.Name = "VillagerSystemRunner";
            runner.AddSystem(pathfinding);
            runner.AddSystem(visibility);
            
            context.RootNode.AddChild(runner);
        }
    }
}
