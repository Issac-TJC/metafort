using Godot;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Systems
{
    public struct SaveRequestedEvent : IGameEvent
    {
        public int SubSlot;
    }

    public partial class AutoSaveSystem : Node
    {
        private IEventBus _eventBus;
        private Timer _autoSaveTimer;

        public void Initialize(IEventBus eventBus)
        {
            _eventBus = eventBus;
            SetupTimer();
        }

        private void SetupTimer()
        {
            _autoSaveTimer = new Timer();
            _autoSaveTimer.WaitTime = 300; // 每 5 分钟 (300秒) 切割下发保存请求
            _autoSaveTimer.Autostart = true;
            _autoSaveTimer.Timeout += () => 
            {
                var saveEvent = new SaveRequestedEvent { SubSlot = 0 };
                _eventBus.Publish(ref saveEvent);
                GD.Print("[AutoSaveSys] Routine Backup Flushed... -> 自动灾备文件已默默安全回写落盘于 Sub-Slot 0。");
            };
            AddChild(_autoSaveTimer);
        }
    }
}
