using Godot;
using System;
using MetaFort.Core.Data;

namespace MetaFort.UI
{
    public static class GameSession
    {
        public static int CurrentSlot = 1;
        public static int CurrentSubSlot = 1;
        public static bool IsNewGame = true;
        public static int Seed = 0;
    }

    public partial class MainMenu : Control
    {
        private VBoxContainer _rootLayout;

        public override void _Ready()
        {
            ColorRect bg = new ColorRect { Color = new Color(0.12f, 0.12f, 0.16f) };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            // 使用 CenterContainer 完全接管子元素的居中排版，彻底解决硬代码锚点(Anchor)计算滞后导致的向右下偏移
            CenterContainer centerWrap = new CenterContainer();
            centerWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(centerWrap);

            _rootLayout = new VBoxContainer { CustomMinimumSize = new Vector2(750, 400) };
            centerWrap.AddChild(_rootLayout);

            RenderMainSlots();
        }

        private void RenderMainSlots()
        {
            ClearUI();

            Label title = new Label 
            { 
                Text = "MetaFort 极速 ECS 沙盒\n多重子存档系统主菜单 (平行宇宙与时间锚点)", 
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            title.AddThemeFontSizeOverride("font_size", 28);
            title.Modulate = new Color(0.3f, 0.8f, 1.0f);
            _rootLayout.AddChild(title);
            _rootLayout.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 30) });

            for (int i = 1; i <= 3; i++)
            {
                int slot = i;
                bool exists = SaveManager.HasAnySubSave(slot);

                HBoxContainer row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                
                Button playBtn = new Button { 
                    Text = exists ? $"展开主宇宙记录 {slot} 的所有时间分支点" : $"🌍 创立新宇宙：提取真随机种子生成 (空宇宙 {slot})", 
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(0, 60)
                };
                playBtn.Pressed += () => {
                    if (exists) RenderSubSlots(slot);
                    else CreateNewGame(slot);
                };

                Button delBtn = new Button { 
                    Text = "抹除整个宇宙架构", 
                    Disabled = !exists,
                    CustomMinimumSize = new Vector2(160, 60)
                };
                delBtn.Pressed += () => { SaveManager.DeleteSave(slot); RenderMainSlots(); };

                row.AddChild(playBtn);
                row.AddChild(delBtn);
                _rootLayout.AddChild(row);
                _rootLayout.AddChild(new MarginContainer { CustomMinimumSize = new Vector2(0, 15) });
            }
        }

        private void RenderSubSlots(int slot)
        {
            ClearUI();

            Label title = new Label 
            { 
                Text = $"目前正探查主宇宙档案： {slot}", 
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            title.AddThemeFontSizeOverride("font_size", 24);
            _rootLayout.AddChild(title);
            _rootLayout.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 20) });

            // 分层检查：0 号定格为灾难恢复级自动存档， 1-9 为深层主动操作的备份锚点
            for (int sub = 0; sub <= 9; sub++)
            {
                if (SaveManager.SaveExists(slot, sub))
                {
                    int localSub = sub; // Capture lambda variable locally!
                    
                    string prefix = sub == 0 ? "[0] 💡 自动防灾存档点 (AutoSave)" : $"[{sub}] ⏳ 手动物理追溯锚点 (Manual Save {sub})";
                    Button subBtn = new Button { 
                        Text = $"进入记忆 => {prefix}",
                        CustomMinimumSize = new Vector2(0, 50)
                    };
                    subBtn.Pressed += () => LoadGame(slot, localSub);
                    
                    _rootLayout.AddChild(subBtn);
                    _rootLayout.AddChild(new MarginContainer { CustomMinimumSize = new Vector2(0, 5) });
                }
            }

            _rootLayout.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 20) });
            
            Button backBtn = new Button { Text = "返回脱离本宇宙层级", CustomMinimumSize = new Vector2(0, 50) };
            backBtn.Pressed += () => RenderMainSlots();
            _rootLayout.AddChild(backBtn);
        }

        private void ClearUI()
        {
            foreach (Node child in _rootLayout.GetChildren()) child.QueueFree();
        }

        private void CreateNewGame(int slot)
        {
            GameSession.CurrentSlot = slot;
            GameSession.CurrentSubSlot = 1; // 新游戏起始主时间锚必定设定为分支 1
            GameSession.IsNewGame = true;
            GameSession.Seed = new Random().Next();
            GetTree().ChangeSceneToFile("res://test.tscn");
        }

        private void LoadGame(int slot, int subSlot)
        {
            GameSession.CurrentSlot = slot;
            GameSession.CurrentSubSlot = subSlot;
            GameSession.IsNewGame = false;
            GetTree().ChangeSceneToFile("res://test.tscn");
        }
    }
}
