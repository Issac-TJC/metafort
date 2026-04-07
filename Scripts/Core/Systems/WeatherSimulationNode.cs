using Godot;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class WeatherSimulationNode : Node
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath TimeSourcePath { get; set; }

        private SimulationTimeNode _timeSource;
        private WeatherSimulationSystem _weatherSystem;
        private int _lastHour = -1;
        private int _lastDay = -1;

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry == null || gameEntry.EntityManager == null || gameEntry.EventBus == null || gameEntry.MapManager == null)
            {
                GD.PrintErr("[WeatherSimulationNode] GameEntry core dependencies not found. Node disabled.");
                SetProcess(false);
                return;
            }

            _timeSource = ResolveTimeSource();
            if (_timeSource == null)
            {
                GD.PrintErr("[WeatherSimulationNode] TimeSource not found. Please assign TimeSourcePath.");
                SetProcess(false);
                return;
            }

            _weatherSystem = new WeatherSimulationSystem();
            _weatherSystem.Initialize(gameEntry.EntityManager, gameEntry.EventBus, gameEntry.MapManager, MetaFort.UI.GameSession.Seed);

            _lastDay = _timeSource.Day;
            _lastHour = _timeSource.Hour;
        }

        public override void _Process(double delta)
        {
            if (_weatherSystem == null || _timeSource == null)
            {
                return;
            }

            if (_timeSource.Day != _lastDay || _timeSource.Hour != _lastHour)
            {
                _lastDay = _timeSource.Day;
                _lastHour = _timeSource.Hour;
                _weatherSystem.AdvanceHour(_lastDay, _lastHour);
            }
        }

        private SimulationTimeNode ResolveTimeSource()
        {
            if (TimeSourcePath != null && !TimeSourcePath.IsEmpty)
            {
                return GetNodeOrNull<SimulationTimeNode>(TimeSourcePath);
            }

            return GetNodeOrNull<SimulationTimeNode>("../SimulationTimeNode");
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
