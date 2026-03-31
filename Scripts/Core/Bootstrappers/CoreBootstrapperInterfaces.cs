using Godot;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Bootstrappers
{
    public struct GameContext
    {
        public Node RootNode { get; }
        public IEntityManager EntityManager { get; }
        public IMapManager MapManager { get; }
        public IEventBus EventBus { get; }
        public IVisionDataSystem VisionData { get; }

        public GameContext(Node rootNode, IEntityManager em, IMapManager map, IEventBus bus, IVisionDataSystem vision)
        {
            RootNode = rootNode;
            EntityManager = em;
            MapManager = map;
            EventBus = bus;
            VisionData = vision;
        }
    }

    public interface IBootstrapper
    {
        void Initialize(GameContext context);
    }
}
