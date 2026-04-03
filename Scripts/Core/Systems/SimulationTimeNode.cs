using Godot;
using System;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Systems
{
    public struct HourPassedEvent : IGameEvent 
    { 
        public int NewHour;
        public int NewDay;
    }

    [GlobalClass]
    public partial class SimulationTimeNode : Node
    {
        [ExportCategory("Time Settings")]
        [Export(PropertyHint.Enum, "Pause:0,Normal:1,Fast:2,Ultra:3")]
        public float TimeScale { get; set; } = 1.0f;

        // 1 real second = X game minutes (e.g. 1 means 1 hour takes 60 seconds)
        [Export] public float RealSecondsPerGameMinute { get; set; } = 1.0f;

        [ExportCategory("Current Time")]
        [Export] public int Day { get; private set; } = 1;
        [Export] public int Hour { get; private set; } = 8;
        [Export] public float Minute { get; private set; } = 0f;

        // This is the delta time scaled by TimeScale that systems should use
        public float ScaledDeltaTime { get; private set; }

        // This exposes the exact amount of in-game hours that elapsed this frame
        public float GameHoursPassedThisFrame { get; private set; }

        private IEventBus _eventBus;

        public override void _Ready()
        {
            // Auto-fetch EventBus if GameEntry is present, keeping this node independent
            if (GameEntry.Instance != null)
            {
                _eventBus = GameEntry.Instance.EventBus;
            }
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            ScaledDeltaTime = dt * TimeScale;
            GameHoursPassedThisFrame = 0f;

            if (TimeScale > 0)
            {
                // Advance in-game clock
                float minutesPassed = ScaledDeltaTime / RealSecondsPerGameMinute;
                GameHoursPassedThisFrame = minutesPassed / 60f;
                Minute += minutesPassed;

                while (Minute >= 60f)
                {
                    Minute -= 60f;
                    Hour++;
                    if (Hour >= 24)
                    {
                        Hour -= 24;
                        Day++;
                    }
                    
                    if (_eventBus != null)
                    {
                        var e = new HourPassedEvent { NewHour = Hour, NewDay = Day };
                        _eventBus.Publish(ref e);
                    }
                }
            }
        }
    }
}
