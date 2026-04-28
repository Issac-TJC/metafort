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
        public static int MapWidth = 0;
        public static int MapHeight = 0;
        public static int MapDepth = 0;
    }

    public partial class MainMenu : Control
    {
        private VBoxContainer _rootLayout;

        public override void _Ready()
        {
            ColorRect bg = new ColorRect { Color = new Color(0.12f, 0.12f, 0.16f) };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            // 浣跨敤 CenterContainer 瀹屽叏鎺ョ瀛愬厓绱犵殑灞呬腑鎺掔増锛屽交搴曡В鍐崇‖浠ｇ爜閿氱偣(Anchor)璁＄畻婊炲悗瀵艰嚧鐨勫悜鍙充笅鍋忕Щ
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
                Text = "MetaFort 鏋侀€?ECS 娌欑洅\n澶氶噸瀛愬瓨妗ｇ郴缁熶富鑿滃崟 (骞宠瀹囧畽涓庢椂闂撮敋鐐?", 
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
                    Text = exists ? $"灞曞紑涓诲畤瀹欒褰?{slot} 鐨勬墍鏈夋椂闂村垎鏀偣" : $"馃實 鍒涚珛鏂板畤瀹欙細鎻愬彇鐪熼殢鏈虹瀛愮敓鎴?(绌哄畤瀹?{slot})", 
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(0, 60)
                };
                playBtn.Pressed += () => {
                    if (exists) RenderSubSlots(slot);
                    else CreateNewGame(slot);
                };

                Button delBtn = new Button { 
                    Text = "鎶归櫎鏁翠釜瀹囧畽鏋舵瀯", 
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
                Text = $"鐩墠姝ｆ帰鏌ヤ富瀹囧畽妗ｆ锛?{slot}", 
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            title.AddThemeFontSizeOverride("font_size", 24);
            _rootLayout.AddChild(title);
            _rootLayout.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 20) });

            // 鍒嗗眰妫€鏌ワ細0 鍙峰畾鏍间负鐏鹃毦鎭㈠绾ц嚜鍔ㄥ瓨妗ｏ紝 1-9 涓烘繁灞備富鍔ㄦ搷浣滅殑澶囦唤閿氱偣
            for (int sub = 0; sub <= 9; sub++)
            {
                if (SaveManager.SaveExists(slot, sub))
                {
                    int localSub = sub; // Capture lambda variable locally!
                    
                    string prefix = sub == 0 ? "[0] 馃挕 鑷姩闃茬伨瀛樻。鐐?(AutoSave)" : $"[{sub}] 鈴?鎵嬪姩鐗╃悊杩芥函閿氱偣 (Manual Save {sub})";
                    Button subBtn = new Button { 
                        Text = $"杩涘叆璁板繂 => {prefix}",
                        CustomMinimumSize = new Vector2(0, 50)
                    };
                    subBtn.Pressed += () => LoadGame(slot, localSub);
                    
                    _rootLayout.AddChild(subBtn);
                    _rootLayout.AddChild(new MarginContainer { CustomMinimumSize = new Vector2(0, 5) });
                }
            }

            _rootLayout.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 20) });
            
            Button backBtn = new Button { Text = "返回主宇宙列表", CustomMinimumSize = new Vector2(0, 50) };
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
            GameSession.CurrentSubSlot = 1; // 鏂版父鎴忚捣濮嬩富鏃堕棿閿氬繀瀹氳瀹氫负鍒嗘敮 1
            GameSession.IsNewGame = true;
            GameSession.Seed = new Random().Next();
            GameSession.MapWidth = 0;
            GameSession.MapHeight = 0;
            GameSession.MapDepth = 0;
            GetTree().ChangeSceneToFile("res://scenes/main/MainGame.tscn");
        }

        private void LoadGame(int slot, int subSlot)
        {
            GameSession.CurrentSlot = slot;
            GameSession.CurrentSubSlot = subSlot;
            GameSession.IsNewGame = false;
            GetTree().ChangeSceneToFile("res://scenes/main/MainGame.tscn");
        }
    }
}
