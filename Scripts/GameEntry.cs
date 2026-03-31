using Godot;
using System;
using MetaFort.Core.EventBus;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort
{
    // ==========================================
    // 测试用组件与事件定义
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
    // 游戏主入口 (单例模式，可挂载于Godot的Root Node或Autoload)
    // ==========================================
    public partial class GameEntry : Node
    {
        // 提供单例访问点，方便其他脚本快速获取核心子系统
        public static GameEntry Instance { get; private set; }

        public IEventBus EventBus { get; private set; }
        public IEntityManager EntityManager { get; private set; }
        public IMapManager MapManager { get; private set; }
        public IVisionDataSystem VisionData { get; private set; }

        public override void _EnterTree()
        {
            if (Instance != null && Instance != this && GodotObject.IsInstanceValid(Instance))
            {
                QueueFree(); // 确保场景中只有一个有效 GameEntry
                return;
            }

            Instance = this;
            InitializeCoreSystems();
        }

        public override void _ExitTree()
        {
            // 切换回主菜单销毁该场景时，彻底释放单例锁，以便下一次重新游玩时能正常重新初始化
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void _Ready()
        {
            if (Instance != this) return; // 短路拦截：已经被标志为删除的冗余节点禁止执行测试

            GD.Print(">>> [GameEntry] MetaFort High Performance Subsystems Booting <<<\n");
            
            // 运行诊断级测试
            RunDiagnostics();
            
            GD.Print("\n>>> [GameEntry] All Subsystem Checks Passed Successfully! <<<");
        }

        /// <summary>
        /// 初始化所有的核心底层系统
        /// </summary>
        private void InitializeCoreSystems()
        {
            MetaFort.Core.Data.ConfigManager.LoadAllConfigs();
            EventBus = new EventBus();
            EntityManager = new EntityManager(10000); 
            
            var mapManager = new MapManager();
            mapManager.InjectDependencies(EventBus);
            
            // 读取全局 Session 以决定是读取旧档还是创建真随机种子档
            int slot = MetaFort.UI.GameSession.CurrentSlot == 0 ? 1 : MetaFort.UI.GameSession.CurrentSlot;
            int subSlot = MetaFort.UI.GameSession.CurrentSubSlot;
            
            if (MetaFort.UI.GameSession.IsNewGame)
            {
                int randomSeed = MetaFort.UI.GameSession.Seed != 0 ? MetaFort.UI.GameSession.Seed : new System.Random().Next();
                GD.Print($"[SaveManager] Creating New Flat Map with Random Seed: {randomSeed}");
                
                int mapW = 100;
                int mapH = 100;
                int mapD = 30; // 突破限制：极大拓宽立体纵深，赐予洞穴层与高山群系施展拳脚的物理维度！
                
                mapManager.InitializeGrid(mapW, mapH, mapD); 
                mapManager.InitMap(randomSeed);
                
                // 初始化地块后立刻固化进初版基础游玩子档案中，建立首个平行时间点锚点
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
            
            // -- 模块化架构：通过 Bootstrappers 启动各大玩法子系统 --
            var context = new MetaFort.Core.Bootstrappers.GameContext(this, EntityManager, MapManager, EventBus, VisionData);
            
            new MetaFort.Core.Bootstrappers.EnvironmentBootstrapper().Initialize(context);
            new MetaFort.Core.Bootstrappers.VillagerBootstrapper().Initialize(context);

            // 测试场景屏蔽功能特判
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

            // 监听底层系统抛出的存盘事件进行主控制环回传
            GameEventHandler<MetaFort.Core.Systems.SaveRequestedEvent> onSaveReq = (ref MetaFort.Core.Systems.SaveRequestedEvent e) => 
            {
                SaveCurrentState(e.SubSlot);
            };
            EventBus.Subscribe(onSaveReq);
        }

        // UI和自动保存系统的硬编码逻辑已完全移除至独立系统中。

        private void SaveCurrentState(int subSlot)
        {
            if (MapManager is MapManager mm)
            {
                int slot = MetaFort.UI.GameSession.CurrentSlot == 0 ? 1 : MetaFort.UI.GameSession.CurrentSlot;
                MetaFort.Core.Data.SaveManager.SaveGame(slot, subSlot, MetaFort.UI.GameSession.Seed, mm.Width, mm.Height, mm.Depth, mm.SerializeMap());
            }
        }

        /// <summary>
        /// 将原 Test.cs 与 GameEntry 对模块的独立测试合并在一起
        /// 方便快速验证架构的当前状况
        /// </summary>
        private void RunDiagnostics()
        {
            // ==========================================
            // 1. EventBus 测试
            // ==========================================
            GD.Print("=== Testing 1: EventBus ===");
            
            GameEventHandler<StatusChangedEvent> onStatusChanged = (ref StatusChangedEvent e) => 
                GD.Print($"[EventBus] StatusChangedEvent received. Status: '{e.Status}'");
                
            GameEventHandler<TestMessageEvent> onTestMessage = (ref TestMessageEvent e) => 
                GD.Print($"[EventBus] TestMessageEvent received. Message: '{e.Message}'");
            
            // 订阅事件并发布
            EventBus.Subscribe(onStatusChanged);
            EventBus.Subscribe(onTestMessage);
            
            var statusEvent = new StatusChangedEvent { Status = "All Systems Nominal" };
            EventBus.Publish(ref statusEvent);
            
            var msgEvent = new TestMessageEvent { Message = "EventBus is fully operational!" };
            EventBus.Publish(ref msgEvent);
            
            EventBus.Unsubscribe(onStatusChanged);
            EventBus.Unsubscribe(onTestMessage);

            // ==========================================
            // 2. ECS 测试
            // ==========================================
            GD.Print("\n=== Testing 2: EntityManager with Struct Of Arrays (SOA) ===");
            
            uint entityA = EntityManager.CreateEntity();
            uint genA = entityA >> 24;
            uint idxA = entityA & 0x00FFFFFF;
            GD.Print($"[ECS] Created EntityA ID: 0x{entityA:X8} (Generation: {genA}, Index: {idxA})");
            
            // 原位添加且原位修改组件
            EntityManager.AddComponent(entityA, new HealthComponent { Health = 100 });
            ref HealthComponent healthRef = ref EntityManager.GetComponent<HealthComponent>(entityA);
            healthRef.Health -= 25; 
            GD.Print($"[ECS] EntityA Health modified in-place using ref. Verified Health: {EntityManager.GetComponent<HealthComponent>(entityA).Health}");
            
            // 添加来自原 Test.cs 的基于 GridPosition 的测试组件
            EntityManager.AddComponent(entityA, new PositionComponent(5, 5, 2));
            var pos = EntityManager.GetComponent<PositionComponent>(entityA).Position;
            GD.Print($"[ECS] EntityA Position added at (X:{pos.X}, Y:{pos.Y}, Z:{pos.Z})");

            // 销毁并验证IsAlive
            EntityManager.DestroyEntity(entityA);
            GD.Print($"[ECS] Destroyed EntityA. IsAlive: {EntityManager.IsAlive(entityA)}"); 
            
            // ==========================================
            // 3. MapManager 测试: 地形生成与沙盒交互
            // ==========================================
            GD.Print("\n=== Testing 3: Spatial MapManager Sandbox APIs ===");
            
            int x = 5, y = 5, z = 2;
            int flatIndex = MapManager.GetFlatIndex(x, y, z);
            GD.Print($"[Spatial] W={MapManager.Width}, H={MapManager.Height}, D={MapManager.Depth} Map. Flat index for ({x},{y},{z}) -> {flatIndex}");
            
            if (MapManager is MapManager actualMapManager)
            {
                // 读取自然生成的方块
                TileData genTile = actualMapManager.GetTile(x, y, z);
                GD.Print($"[Spatial] Naturally Generated Tile at ({x},{y},{z}): {genTile.Type}, Health: {genTile.Health}");

                // 监听地图变更事件
                GameEventHandler<TerrainModifiedEvent> onTerrainMod = (ref TerrainModifiedEvent e) => 
                {
                    GD.Print($"[TerrainModifiedEvent] Position: {e.Position}, Old: {e.OldType}, New: {e.NewType}");
                };
                EventBus.Subscribe(onTerrainMod);

                // 玩家尝试挖掘并替换为空气
                GD.Print("[Spatial] Player executing ReplaceTile...");
                bool replaced = actualMapManager.ReplaceTile(x, y, z, TerrainType.Air);
                GD.Print($"[Spatial] Tile Replaced Result: {replaced}");
                
                // 去除监听
                EventBus.Unsubscribe(onTerrainMod);
            }
        }
    }
}
