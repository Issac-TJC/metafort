using Godot;
using MetaFort.Core.Items;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class WeatherEffectOnItemNode : Node
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath ItemSystemPath { get; set; }

        private WeatherEffectOnItemSystem _system;

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry == null || gameEntry.EventBus == null)
            {
                GD.PrintErr("[WeatherEffectOnItemNode] Missing GameEntry/EventBus. Node disabled.");
                SetProcess(false);
                return;
            }

            ItemSystemNode itemSystem = null;
            if (ItemSystemPath != null && !ItemSystemPath.IsEmpty)
            {
                itemSystem = GetNodeOrNull<ItemSystemNode>(ItemSystemPath);
            }
            else
            {
                itemSystem = GetNodeOrNull<ItemSystemNode>("../ItemSystemNode");
            }

            if (itemSystem == null)
            {
                GD.PrintErr("[WeatherEffectOnItemNode] ItemSystemNode not found. Node disabled.");
                SetProcess(false);
                return;
            }

            _system = new WeatherEffectOnItemSystem(itemSystem, gameEntry.EventBus);
            _system.Initialize();
        }

        public override void _ExitTree()
        {
            _system?.Shutdown();
            _system = null;
        }

        private MetaFort.GameEntry ResolveGameEntry()
        {
            if (CoreSourcePath != null && !CoreSourcePath.IsEmpty)
            {
                return GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            }

            return GetNodeOrNull<MetaFort.GameEntry>("../GameEntry") ?? MetaFort.GameEntry.Instance;
        }
    }
}
