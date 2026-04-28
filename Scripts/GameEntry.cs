using Godot;
using System;
using MetaFort.Core.EventBus;
using MetaFort.Core.ECS;
using MetaFort.Core.Spatial;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort
{
    // ==========================================
    // 测试用事件与组件定义
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
    // 游戏主入口
    // 可作为场景根节点，也可作为 Autoload 使用
    // ==========================================
    public partial class GameEntry : Node
    {
        // 提供全局访问点，方便其他节点快速获取核心系统。
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
                QueueFree(); // 确保场景树里只有一个有效的 GameEntry。
                return;
            }

            Instance = this;
            InitializeCoreSystems();
        }

        public override void _ExitTree()
        {
            // 场景销毁时释放单例引用，避免下一次进入游戏时残留旧实例。
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void _Ready()
        {
            if (Instance != this) return; // 已标记销毁的冗余节点不再继续执行测试逻辑。

            if (_initializationFailed)
            {
                GD.PrintErr("[GameEntry] Startup aborted because one or more required configs failed validation.");
                return;
            }

            GD.Print(">>> [GameEntry] MetaFort High Performance Subsystems Booting <<<\n");

            // 运行一组启动诊断，快速确认基础子系统可用。
            RunDiagnostics();

            GD.Print("\n>>> [GameEntry] All Subsystem Checks Passed Successfully! <<<");
        }

        /// <summary>
        /// 初始化所有核心底层系统。
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

            // 读取全局 Session，决定是新建地图还是加载存档。
            int slot = MetaFort.UI.GameSession.CurrentSlot == 0 ? 1 : MetaFort.UI.GameSession.CurrentSlot;
            int subSlot = MetaFort.UI.GameSession.CurrentSubSlot;

            if (MetaFort.UI.GameSession.IsNewGame)
            {
                int randomSeed = MetaFort.UI.GameSession.Seed != 0 ? MetaFort.UI.GameSession.Seed : new Random().Next();
                GD.Print($"[SaveManager] Creating New Flat Map with Random Seed: {randomSeed}");

                (int mapW, int mapH, int mapD) = ResolveInitialMapSize();
                GD.Print($"[GameEntry] Using map size W={mapW}, H={mapH}, D={mapD} (Session override > 0, otherwise scene export).");

                mapManager.InitializeGrid(mapW, mapH, mapD);
                mapManager.InitMap(randomSeed);

                // 新地图生成后立即写入初始存档，作为后续分支与回档的基线。
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

            // 通过 Bootstrappers 装配各个玩法子系统。
            var context = new MetaFort.Core.Bootstrappers.GameContext(this, EntityManager, MapManager, EventBus, VisionData);

            new MetaFort.Core.Bootstrappers.EnvironmentBootstrapper().Initialize(context);
            new MetaFort.Core.Bootstrappers.VillagerBootstrapper().Initialize(context);

            // 测试场景不挂暂停菜单与保存入口，避免干扰实验。
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

            // 将存档请求事件桥接到当前场景状态保存。
            GameEventHandler<MetaFort.Core.Systems.SaveRequestedEvent> onSaveReq = (ref MetaFort.Core.Systems.SaveRequestedEvent e) =>
            {
                SaveCurrentState(e.SubSlot);
            };
            EventBus.Subscribe(onSaveReq);
        }

        private (int Width, int Height, int Depth) ResolveInitialMapSize()
        {
            int width = MetaFort.UI.GameSession.MapWidth > 0 ? MetaFort.UI.GameSession.MapWidth : DefaultMapWidth;
            int height = MetaFort.UI.GameSession.MapHeight > 0 ? MetaFort.UI.GameSession.MapHeight : DefaultMapHeight;
            int depth = MetaFort.UI.GameSession.MapDepth > 0 ? MetaFort.UI.GameSession.MapDepth : DefaultMapDepth;
            return (width, height, depth);
        }

        // UI 与自动存档的具体行为已经拆到独立系统，这里只保留场景级存档入口。
        private void SaveCurrentState(int subSlot)
        {
            if (MapManager is MapManager mm)
            {
                int slot = MetaFort.UI.GameSession.CurrentSlot == 0 ? 1 : MetaFort.UI.GameSession.CurrentSlot;
                MetaFort.Core.Data.SaveManager.SaveGame(slot, subSlot, MetaFort.UI.GameSession.Seed, mm.Width, mm.Height, mm.Depth, mm.SerializeMap());
            }
        }

        /// <summary>
        /// 将原先 Test.cs 中分散的核心诊断集中到这里，便于快速验证当前架构状态。
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

            // 订阅后立即发布事件，确认总线读写正常。
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

            // 验证组件可原位添加与原位修改。
            EntityManager.AddComponent(entityA, new HealthComponent { Health = 100 });
            ref HealthComponent healthRef = ref EntityManager.GetComponent<HealthComponent>(entityA);
            healthRef.Health -= 25;
            GD.Print($"[ECS] EntityA Health modified in-place using ref. Verified Health: {EntityManager.GetComponent<HealthComponent>(entityA).Health}");

            // 补一个基于 GridPosition 的位置组件测试。
            EntityManager.AddComponent(entityA, new PositionComponent(5, 5, 2));
            var pos = EntityManager.GetComponent<PositionComponent>(entityA).Position;
            GD.Print($"[ECS] EntityA Position added at (X:{pos.X}, Y:{pos.Y}, Z:{pos.Z})");

            // 销毁实体并确认生命周期状态正确。
            EntityManager.DestroyEntity(entityA);
            GD.Print($"[ECS] Destroyed EntityA. IsAlive: {EntityManager.IsAlive(entityA)}");

            // ==========================================
            // 3. MapManager 测试：地形生成与沙盒接口
            // ==========================================
            GD.Print("\n=== Testing 3: Spatial MapManager Sandbox APIs ===");

            int x = 5, y = 5, z = 2;
            int flatIndex = MapManager.GetFlatIndex(x, y, z);
            GD.Print($"[Spatial] W={MapManager.Width}, H={MapManager.Height}, D={MapManager.Depth} Map. Flat index for ({x},{y},{z}) -> {flatIndex}");

            if (MapManager is MapManager actualMapManager)
            {
                // 读取一格已生成地块，确保地图数据可访问。
                TileData genTile = actualMapManager.GetTile(x, y, z);
                _ = genTile;

                // 监听地形修改事件。
                GameEventHandler<TerrainModifiedEvent> onTerrainMod = (ref TerrainModifiedEvent e) =>
                {
                    GD.Print($"[TerrainModifiedEvent] Position: {e.Position}, Old: {e.OldType}, New: {e.NewType}");
                };
                EventBus.Subscribe(onTerrainMod);

                // 模拟玩家替换地块，验证地图与事件联动。
                GD.Print("[Spatial] Player executing ReplaceTile...");
                bool replaced = actualMapManager.ReplaceTile(x, y, z, TerrainType.Air);
                GD.Print($"[Spatial] Tile Replaced Result: {replaced}");

                // 移除监听，避免后续场景噪音。
                EventBus.Unsubscribe(onTerrainMod);
            }
        }
    }
}
