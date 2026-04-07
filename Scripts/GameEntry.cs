using Godot;
using System;
using MetaFort.Core.EventBus;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort
{
    // ==========================================
    // 娴嬭瘯鐢ㄧ粍浠朵笌浜嬩欢瀹氫箟
    // ==========================================
    public struct StatusChangedEvent : IGameEvent { public string Status; }
    public struct TestMessageEvent : IGameEvent { public string Message; }
    
    public struct HealthComponent : IComponent { public int Health; }
    public struct PositionComponent : IComponent 
    { 
        public GridPosition Position; 
        public PositionComponent(int x, int y, int z) { Position = new GridPosition(x, y, z); }
    }

    // ==========================================
    // 娓告垙涓诲叆鍙?(鍗曚緥妯″紡锛屽彲鎸傝浇浜嶨odot鐨凴oot Node鎴朅utoload)
    // ==========================================
    public partial class GameEntry : Node
    {
        // 鎻愪緵鍗曚緥璁块棶鐐癸紝鏂逛究鍏朵粬鑴氭湰蹇€熻幏鍙栨牳蹇冨瓙绯荤粺
        public static GameEntry Instance { get; private set; }

        [Export]
        public int DefaultMapWidth { get; set; } = 275;

        [Export]
        public int DefaultMapHeight { get; set; } = 275;

        [Export]
        public int DefaultMapDepth { get; set; } = 30;

        public IEventBus EventBus { get; private set; }
        public IEntityManager EntityManager { get; private set; }
        public IMapManager MapManager { get; private set; }
        public IVisionDataSystem VisionData { get; private set; }
        private bool _initializationFailed;

        public override void _EnterTree()
        {
            if (Instance != null && Instance != this && GodotObject.IsInstanceValid(Instance))
            {
                QueueFree(); // 纭繚鍦烘櫙涓彧鏈変竴涓湁鏁?GameEntry
                return;
            }

            Instance = this;
            InitializeCoreSystems();
        }

        public override void _ExitTree()
        {
            // 鍒囨崲鍥炰富鑿滃崟閿€姣佽鍦烘櫙鏃讹紝褰诲簳閲婃斁鍗曚緥閿侊紝浠ヤ究涓嬩竴娆￠噸鏂版父鐜╂椂鑳芥甯搁噸鏂板垵濮嬪寲
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void _Ready()
        {
            if (Instance != this) return; // 鐭矾鎷︽埅锛氬凡缁忚鏍囧織涓哄垹闄ょ殑鍐椾綑鑺傜偣绂佹鎵ц娴嬭瘯

            if (_initializationFailed)
            {
                GD.PrintErr("[GameEntry] Startup aborted because one or more required configs failed validation.");
                return;
            }

            GD.Print(">>> [GameEntry] MetaFort High Performance Subsystems Booting <<<\n");
            
            // 杩愯璇婃柇绾ф祴璇?            RunDiagnostics();
            
            GD.Print("\n>>> [GameEntry] All Subsystem Checks Passed Successfully! <<<");
        }

        /// <summary>
        /// 鍒濆鍖栨墍鏈夌殑鏍稿績搴曞眰绯荤粺
        /// </summary>
        private void InitializeCoreSystems()
        {
            if (!MetaFort.Core.Data.ConfigManager.LoadAllConfigs())
            {
                _initializationFailed = true;
                GD.PrintErr("[GameEntry] Core config validation failed. Scene bootstrap has been stopped.");
                return;
            }

            _initializationFailed = false;
            EventBus = new EventBus();
            EntityManager = new EntityManager(10000); 

            var mapManager = new MapManager();
            mapManager.InjectDependencies(EventBus);
            
            // 璇诲彇鍏ㄥ眬 Session 浠ュ喅瀹氭槸璇诲彇鏃ф。杩樻槸鍒涘缓鐪熼殢鏈虹瀛愭。
            int slot = MetaFort.UI.GameSession.CurrentSlot == 0 ? 1 : MetaFort.UI.GameSession.CurrentSlot;
            int subSlot = MetaFort.UI.GameSession.CurrentSubSlot;
            
            if (MetaFort.UI.GameSession.IsNewGame)
            {
                int randomSeed = MetaFort.UI.GameSession.Seed != 0 ? MetaFort.UI.GameSession.Seed : new System.Random().Next();
                GD.Print($"[SaveManager] Creating New Flat Map with Random Seed: {randomSeed}");
                
                int mapW = MetaFort.UI.GameSession.MapWidth > 0 ? MetaFort.UI.GameSession.MapWidth : DefaultMapWidth;
                int mapH = MetaFort.UI.GameSession.MapHeight > 0 ? MetaFort.UI.GameSession.MapHeight : DefaultMapHeight;
                int mapD = MetaFort.UI.GameSession.MapDepth > 0 ? MetaFort.UI.GameSession.MapDepth : DefaultMapDepth; // 绐佺牬闄愬埗锛氭瀬澶ф嫇瀹界珛浣撶旱娣憋紝璧愪簣娲炵┐灞備笌楂樺北缇ょ郴鏂藉睍鎷宠剼鐨勭墿鐞嗙淮搴︼紒
                
                mapManager.InitializeGrid(mapW, mapH, mapD); 
                mapManager.InitMap(randomSeed);
                
                // 鍒濆鍖栧湴鍧楀悗绔嬪埢鍥哄寲杩涘垵鐗堝熀纭€娓哥帺瀛愭。妗堜腑锛屽缓绔嬮涓钩琛屾椂闂寸偣閿氱偣
                MetaFort.Core.Data.SaveManager.SaveGame(slot, subSlot, randomSeed, mapW, mapH, mapD, mapManager.SerializeMap());
            }
            else
            {
                GD.Print($"[SaveManager] Reading unified bytes natively from Slot {slot} SubSlot {subSlot}...");
                MetaFort.Core.Data.SaveManager.LoadGame(slot, subSlot, out int seed, out int w, out int h, out int d, out byte[] mapData);
                MetaFort.UI.GameSession.Seed = seed; 
                
                mapManager.InitializeGrid(w, h, d);
                mapManager.DeserializeMap(mapData);
            }
            
            MapManager = mapManager;
            
            var visionData = new VisionDataSystem(EventBus, MapManager);
            VisionData = visionData;
            
            // -- 妯″潡鍖栨灦鏋勶細閫氳繃 Bootstrappers 鍚姩鍚勫ぇ鐜╂硶瀛愮郴缁?--
            var context = new MetaFort.Core.Bootstrappers.GameContext(this, EntityManager, MapManager, EventBus, VisionData);
            
            new MetaFort.Core.Bootstrappers.EnvironmentBootstrapper().Initialize(context);
            new MetaFort.Core.Bootstrappers.VillagerBootstrapper().Initialize(context);


            // 娴嬭瘯鍦烘櫙灞忚斀鍔熻兘鐗瑰垽
            bool isTestScene = GetTree().CurrentScene.Name.ToString().Contains("test", StringComparison.OrdinalIgnoreCase);

            if (!isTestScene)
            {
                var inputSystem = new MetaFort.Core.Systems.InputSystem();
                inputSystem.Initialize(EventBus);
                AddChild(inputSystem);

                var pauseMenuUI = new MetaFort.UI.PauseMenuUI();
                pauseMenuUI.Initialize(EventBus, SaveCurrentState, () => GetTree().ChangeSceneToFile("res://MainMenu.tscn"));
                AddChild(pauseMenuUI);
            }
            else
            {
                GD.Print("[GameEntry] Detected Test Scene. PauseMenu and Save Systems are disabled.");
            }

            // Save request bridge
            GameEventHandler<MetaFort.Core.Systems.SaveRequestedEvent> onSaveReq = (ref MetaFort.Core.Systems.SaveRequestedEvent e) =>
            {
                SaveCurrentState(e.SubSlot);
            };
            EventBus.Subscribe(onSaveReq);
        }

        // UI鍜岃嚜鍔ㄤ繚瀛樼郴缁熺殑纭紪鐮侀€昏緫宸插畬鍏ㄧЩ闄よ嚦鐙珛绯荤粺涓€?
        private void SaveCurrentState(int subSlot)
        {
            if (MapManager is MapManager mm)
            {
                int slot = MetaFort.UI.GameSession.CurrentSlot == 0 ? 1 : MetaFort.UI.GameSession.CurrentSlot;
                MetaFort.Core.Data.SaveManager.SaveGame(slot, subSlot, MetaFort.UI.GameSession.Seed, mm.Width, mm.Height, mm.Depth, mm.SerializeMap());
            }
        }

        /// <summary>
        /// 灏嗗師 Test.cs 涓?GameEntry 瀵规ā鍧楃殑鐙珛娴嬭瘯鍚堝苟鍦ㄤ竴璧?        /// 鏂逛究蹇€熼獙璇佹灦鏋勭殑褰撳墠鐘跺喌
        /// </summary>
        private void RunDiagnostics()
        {
            // ==========================================
            // 1. EventBus 娴嬭瘯
            // ==========================================
            GD.Print("=== Testing 1: EventBus ===");
            
            GameEventHandler<StatusChangedEvent> onStatusChanged = (ref StatusChangedEvent e) => 
                GD.Print($"[EventBus] StatusChangedEvent received. Status: '{e.Status}'");
                
            GameEventHandler<TestMessageEvent> onTestMessage = (ref TestMessageEvent e) => 
                GD.Print($"[EventBus] TestMessageEvent received. Message: '{e.Message}'");
            
            // 璁㈤槄浜嬩欢骞跺彂甯?            EventBus.Subscribe(onStatusChanged);
            EventBus.Subscribe(onTestMessage);
            
            var statusEvent = new StatusChangedEvent { Status = "All Systems Nominal" };
            EventBus.Publish(ref statusEvent);
            
            var msgEvent = new TestMessageEvent { Message = "EventBus is fully operational!" };
            EventBus.Publish(ref msgEvent);
            
            EventBus.Unsubscribe(onStatusChanged);
            EventBus.Unsubscribe(onTestMessage);

            // ==========================================
            // 2. ECS 娴嬭瘯
            // ==========================================
            GD.Print("\n=== Testing 2: EntityManager with Struct Of Arrays (SOA) ===");
            
            uint entityA = EntityManager.CreateEntity();
            uint genA = entityA >> 24;
            uint idxA = entityA & 0x00FFFFFF;
            GD.Print($"[ECS] Created EntityA ID: 0x{entityA:X8} (Generation: {genA}, Index: {idxA})");
            
            // 鍘熶綅娣诲姞涓斿師浣嶄慨鏀圭粍浠?            EntityManager.AddComponent(entityA, new HealthComponent { Health = 100 });
            ref HealthComponent healthRef = ref EntityManager.GetComponent<HealthComponent>(entityA);
            healthRef.Health -= 25; 
            GD.Print($"[ECS] EntityA Health modified in-place using ref. Verified Health: {EntityManager.GetComponent<HealthComponent>(entityA).Health}");
            
            // 娣诲姞鏉ヨ嚜鍘?Test.cs 鐨勫熀浜?GridPosition 鐨勬祴璇曠粍浠?            EntityManager.AddComponent(entityA, new PositionComponent(5, 5, 2));
            var pos = EntityManager.GetComponent<PositionComponent>(entityA).Position;
            GD.Print($"[ECS] EntityA Position added at (X:{pos.X}, Y:{pos.Y}, Z:{pos.Z})");

            // 閿€姣佸苟楠岃瘉IsAlive
            EntityManager.DestroyEntity(entityA);
            GD.Print($"[ECS] Destroyed EntityA. IsAlive: {EntityManager.IsAlive(entityA)}"); 
            
            // ==========================================
            // 3. MapManager 娴嬭瘯: 鍦板舰鐢熸垚涓庢矙鐩掍氦浜?            // ==========================================
            GD.Print("\n=== Testing 3: Spatial MapManager Sandbox APIs ===");
            
            int x = 5, y = 5, z = 2;
            int flatIndex = MapManager.GetFlatIndex(x, y, z);
            GD.Print($"[Spatial] W={MapManager.Width}, H={MapManager.Height}, D={MapManager.Depth} Map. Flat index for ({x},{y},{z}) -> {flatIndex}");
            
            if (MapManager is MapManager actualMapManager)
            {
                // Read generated tile
                TileData genTile = actualMapManager.GetTile(x, y, z);

                // 鐩戝惉鍦板浘鍙樻洿浜嬩欢
                GameEventHandler<TerrainModifiedEvent> onTerrainMod = (ref TerrainModifiedEvent e) => 
                {
                    GD.Print($"[TerrainModifiedEvent] Position: {e.Position}, Old: {e.OldType}, New: {e.NewType}");
                };
                EventBus.Subscribe(onTerrainMod);

                // 鐜╁灏濊瘯鎸栨帢骞舵浛鎹负绌烘皵
                GD.Print("[Spatial] Player executing ReplaceTile...");
                bool replaced = actualMapManager.ReplaceTile(x, y, z, TerrainType.Air);
                GD.Print($"[Spatial] Tile Replaced Result: {replaced}");
                
                // 鍘婚櫎鐩戝惉
                EventBus.Unsubscribe(onTerrainMod);
            }
        }
    }
}

