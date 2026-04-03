using Godot;
using System;
using MetaFort.Core.ECS;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class BioSystemNode : Node
    {
        [Export]
        public SimulationTimeNode TimeSource;

        private BiologicalNeedsSystem _bioSystem;

        public override void _Ready()
        {
            if (GameEntry.Instance != null && GameEntry.Instance.EntityManager != null && GameEntry.Instance.EventBus != null)
            {
                _bioSystem = new BiologicalNeedsSystem();
                _bioSystem.Initialize(GameEntry.Instance.EntityManager, GameEntry.Instance.EventBus);
            }
            else
            {
                GD.PrintErr("[BioSystemNode] GameEntry core dependencies not found! Disabling BiologicalNeedsSystem.");
                SetProcess(false);
            }

            if (TimeSource == null)
            {
                GD.PushWarning("[BioSystemNode] TimeSource is not assigned in the Inspector. Will attempt to find one in the scene tree.");
                TimeSource = GetNodeOrNull<SimulationTimeNode>("/root/../SimulationTimeNode"); // Fallback lookup if possible, but manual assignment is preferred
            }
        }

        public override void _Process(double delta)
        {
            if (_bioSystem == null) return;

            // Use the globally controlled in-game hours passed, falling back to 0 if no time source exists
            double hoursPassed = (TimeSource != null) ? TimeSource.GameHoursPassedThisFrame : 0.0;
            
            if (hoursPassed > 0)
            {
                _bioSystem.Update(hoursPassed);
            }
        }
    }
}
