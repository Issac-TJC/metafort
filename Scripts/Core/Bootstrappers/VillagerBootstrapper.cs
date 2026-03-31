using Godot;
using System.Collections.Generic;
using MetaFort.Core.Systems;
using MetaFort.Core.ECS;

namespace MetaFort.Core.Bootstrappers
{
    public partial class VillagerSystemRunner : Node
    {
        private readonly List<ISystem> _systems = new List<ISystem>();

        public void AddSystem(ISystem system)
        {
            _systems.Add(system);
        }

        public override void _Process(double delta)
        {
            foreach (var system in _systems)
            {
                system.Update(delta);
            }
        }
    }

    public class VillagerBootstrapper : IBootstrapper
    {
        public void Initialize(GameContext context)
        {
            // 初始化系统
            var pathfinding = new PathfindingSystem();
            pathfinding.Initialize(context.EntityManager, context.EventBus);
            pathfinding.InjectMapManager(context.MapManager);

            var visibility = new VisibilityCalculationSystem();
            visibility.Initialize(context.EntityManager, context.EventBus);
            visibility.InjectDependencies(context.MapManager, context.VisionData);

            // 包装为 Node 纳入主循环更新
            var runner = new VillagerSystemRunner();
            runner.Name = "VillagerSystemRunner";
            runner.AddSystem(pathfinding);
            runner.AddSystem(visibility);
            
            context.RootNode.AddChild(runner);
        }
    }
}
