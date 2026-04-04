using Godot;
using MetaFort.Core.ECS;
using MetaFort.Core.Systems;

namespace MetaFort.Core.Bootstrappers
{
    public class EnvironmentBootstrapper : IBootstrapper
    {
        public void Initialize(GameContext context)
        {
            // Fluid System
            var fluidSystem = new FluidSimulationSystem();
            fluidSystem.Initialize(context.EntityManager, context.EventBus);
            context.RootNode.AddChild(fluidSystem);

            // Weather 系统不在这里自动挂载，改为独立节点供场景手动组合
            // Auto Save System 被彻底剥离出此环境基石，交由用户自由挂载为 Node 决定生死
        }
    }
}
