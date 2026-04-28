using Godot;
using MetaFort.Core.EventBus;
using MetaFort.Core.Items;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Heat
{
    [GlobalClass]
    public partial class HeatFieldNode : Node
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath ItemSystemPath { get; set; }

        public IHeatFieldQuery HeatQuery => _system;

        private HeatFieldSystem _system;

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry == null || gameEntry.EventBus == null || gameEntry.MapManager == null)
            {
                GD.PrintErr("[HeatFieldNode] Missing GameEntry dependencies. Node disabled.");
                SetProcess(false);
                return;
            }

            ItemSystemNode itemSystem = ResolveItemSystem();
            if (itemSystem == null)
            {
                GD.PrintErr("[HeatFieldNode] ItemSystemNode not found. Node disabled.");
                SetProcess(false);
                return;
            }

            Initialize(gameEntry.EventBus, gameEntry.MapManager, itemSystem);
        }

        public override void _ExitTree()
        {
            _system?.Shutdown();
            _system = null;
        }

        public void Initialize(IEventBus eventBus, IMapManager mapManager, ItemSystemNode itemSystem)
        {
            _system?.Shutdown();
            _system = new HeatFieldSystem(mapManager, itemSystem, eventBus);
            _system.Initialize();
        }

        private ItemSystemNode ResolveItemSystem()
        {
            if (ItemSystemPath != null && !ItemSystemPath.IsEmpty)
            {
                return GetNodeOrNull<ItemSystemNode>(ItemSystemPath);
            }

            return GetNodeOrNull<ItemSystemNode>("../ItemSystemNode");
        }

        private MetaFort.GameEntry ResolveGameEntry()
        {
            if (CoreSourcePath != null && !CoreSourcePath.IsEmpty)
            {
                return GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            }

            return GetNodeOrNull<MetaFort.GameEntry>("..") ?? MetaFort.GameEntry.Instance;
        }
    }
}
