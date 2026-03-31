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

        public override void _Ready()
        {
            // 作为可拆卸的组件节点，在进入场景时主动寻访主程序的 EventBus 进行自我启动
            if (GameEntry.Instance != null && GameEntry.Instance.EventBus != null)
            {
                Initialize(GameEntry.Instance.EventBus);
            }
        }

        public void Initialize(IEventBus eventBus)
        {
            if (_autoSaveTimer != null) return; // 防重复启动
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
