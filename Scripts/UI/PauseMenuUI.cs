using Godot;
using MetaFort.Core.EventBus;

namespace MetaFort.UI
{
    public struct TogglePauseMenuEvent : IGameEvent { }
    
    public partial class PauseMenuUI : Node
    {
        private Control _pauseMenu;
        private IEventBus _eventBus;
        private System.Action<int> _onSaveRequested;
        private System.Action _onExitRequested;

        public void Initialize(IEventBus eventBus, System.Action<int> onSaveRequested, System.Action onExitRequested)
        {
            _eventBus = eventBus;
            _onSaveRequested = onSaveRequested;
            _onExitRequested = onExitRequested;
            
            GameEventHandler<TogglePauseMenuEvent> onToggle = (ref TogglePauseMenuEvent e) => 
            {
                if (_pauseMenu != null)
                {
                    _pauseMenu.Visible = !_pauseMenu.Visible;
                }
            };
            _eventBus.Subscribe(onToggle);
            
            SetupUI();
        }

        private void SetupUI()
        {
            CanvasLayer layer = new CanvasLayer { Layer = 100 };
            _pauseMenu = new ColorRect { Color = new Color(0, 0, 0, 0.85f) };
            _pauseMenu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _pauseMenu.Visible = false;
            layer.AddChild(_pauseMenu);

            // 引入 CenterContainer 来绝对接管内部的 VBox 排版，免除右偏移的错位问题
            CenterContainer centerWrap = new CenterContainer();
            centerWrap.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _pauseMenu.AddChild(centerWrap);

            VBoxContainer vbox = new VBoxContainer { CustomMinimumSize = new Vector2(550, 600) };
            centerWrap.AddChild(vbox);

            Label title = new Label { Text = "=== 局内跨时间段保存控制器 ===", HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeFontSizeOverride("font_size", 28);
            vbox.AddChild(title);
            vbox.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 20) });

            for(int i = 1; i <= 9; i++)
            {
                int sub = i; 
                Button btn = new Button { 
                    Text = $"记忆节点固化 -> 写入手动追溯锚点 [ {sub} ]",
                    CustomMinimumSize = new Vector2(0, 45)
                };
                btn.Pressed += () => { 
                    _onSaveRequested?.Invoke(sub);
                    _pauseMenu.Visible = false; 
                    GD.Print($"[PauseMenuUI] 手动安全备份已提交，落地槽位 Sub-Slot: {sub}!");
                };
                vbox.AddChild(btn);
            }

            vbox.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 30) });
            
            Button exitBtn = new Button { 
                Text = "跳跃回主轴宇宙 (退出至根层级主界面)",
                CustomMinimumSize = new Vector2(0, 60)
            };
            exitBtn.Pressed += () => _onExitRequested?.Invoke();
            vbox.AddChild(exitBtn);

            AddChild(layer);
        }
    }
}
