using Godot;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class WeatherEffectOnSandboxNode : Node
    {
        [Export]
        public NodePath WeatherSimulationNodePath;

        private WeatherEffectOnSandboxSystem _system;

        public override void _Ready()
        {
            if (GameEntry.Instance == null || GameEntry.Instance.EntityManager == null || GameEntry.Instance.EventBus == null)
            {
                GD.PrintErr("[WeatherEffectOnSandboxNode] Missing GameEntry dependencies. Node disabled.");
                SetProcess(false);
                return;
            }

            if (!WeatherSimulationNodePath.IsEmpty && GetNodeOrNull<Node>(WeatherSimulationNodePath) == null)
            {
                GD.PushWarning("[WeatherEffectOnSandboxNode] Linked WeatherSimulationNodePath not found. Check Inspector link.");
            }

            _system = new WeatherEffectOnSandboxSystem();
            _system.Initialize(GameEntry.Instance.EntityManager, GameEntry.Instance.EventBus);
        }
    }
}
