using Godot;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class WeatherEffectOnVillagerNode : Node
    {
        [Export]
        public NodePath WeatherSimulationNodePath;

        private WeatherEffectOnVillagerSystem _system;

        public override void _Ready()
        {
            if (GameEntry.Instance == null || GameEntry.Instance.EntityManager == null || GameEntry.Instance.EventBus == null)
            {
                GD.PrintErr("[WeatherEffectOnVillagerNode] Missing GameEntry dependencies. Node disabled.");
                SetProcess(false);
                return;
            }

            if (!WeatherSimulationNodePath.IsEmpty && GetNodeOrNull<Node>(WeatherSimulationNodePath) == null)
            {
                GD.PushWarning("[WeatherEffectOnVillagerNode] Linked WeatherSimulationNodePath not found. Check Inspector link.");
            }

            _system = new WeatherEffectOnVillagerSystem();
            _system.Initialize(GameEntry.Instance.EntityManager, GameEntry.Instance.EventBus);
        }
    }
}
