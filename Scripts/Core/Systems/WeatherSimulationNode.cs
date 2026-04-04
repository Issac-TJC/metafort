using Godot;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class WeatherSimulationNode : Node
    {
        [Export]
        public SimulationTimeNode TimeSource;

        private WeatherSimulationSystem _weatherSystem;
        private int _lastHour = -1;
        private int _lastDay = -1;

        public override void _Ready()
        {
            if (GameEntry.Instance == null || GameEntry.Instance.EntityManager == null || GameEntry.Instance.EventBus == null)
            {
                GD.PrintErr("[WeatherSimulationNode] GameEntry core dependencies not found! Disabling weather simulation.");
                SetProcess(false);
                return;
            }

            if (TimeSource == null)
            {
                GD.PushWarning("[WeatherSimulationNode] TimeSource is not assigned in Inspector. Trying auto-find.");
                TimeSource = GetNodeOrNull<SimulationTimeNode>("../SimulationTimeNode");
            }

            if (TimeSource == null)
            {
                GD.PrintErr("[WeatherSimulationNode] TimeSource not found. Please link SimulationTimeNode in Inspector.");
                SetProcess(false);
                return;
            }

            _weatherSystem = new WeatherSimulationSystem();
            _weatherSystem.Initialize(GameEntry.Instance.EntityManager, GameEntry.Instance.EventBus);

            _lastDay = TimeSource.Day;
            _lastHour = TimeSource.Hour;
        }

        public override void _Process(double delta)
        {
            if (_weatherSystem == null || TimeSource == null) return;

            if (TimeSource.Day != _lastDay || TimeSource.Hour != _lastHour)
            {
                _lastDay = TimeSource.Day;
                _lastHour = TimeSource.Hour;
                _weatherSystem.AdvanceHour(_lastDay, _lastHour);
            }
        }
    }
}
