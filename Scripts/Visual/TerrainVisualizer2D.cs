using Godot;
using System;
using System.Collections.Generic;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Spatial;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort.Visual
{
    public partial class TerrainVisualizer2D : Node2D
    {
        [Export]
        public TileMapLayer TargetTileMap;
        
        [Export]
        public bool IgnoreVision = false;

        [Export]
        public int MaxCachedLayers = 6;

        private class LayerCacheItem
        {
            public int ZLevel = -1;
            public TileMapLayer GroundLayer;
            public TileMapLayer ObstacleLayer;
            public TileMapLayer FogLayer;
            public long LastAccessedTicks;
        }

        private List<LayerCacheItem> _layerCache = new List<LayerCacheItem>();

        private int _currentZLevel = 0;
        private IMapManager _mapManager;
        private IEventBus _eventBus;
        private IVisionDataSystem _visionData;

        private Camera2D _camera;
        private float _cameraSpeed = 1200f;

        private readonly Dictionary<TerrainType, int> _terrainCoords = new Dictionary<TerrainType, int>();
        private readonly Dictionary<TerrainType, Vector2I> _defaultFloorAtlas = new Dictionary<TerrainType, Vector2I>();
        private GameEventHandler<TerrainModifiedEvent> _onTerrainModHandler;
        private GameEventHandler<VisionUpdatedEvent> _onVisionUpdatedHandler;

        private bool _isInitialized = false;

        public override void _Ready()
        {
            if (GameEntry.Instance != null)
            {
                Initialize(GameEntry.Instance.MapManager, GameEntry.Instance.EventBus, GameEntry.Instance.VisionData);
            }
        }

        public void Initialize(IMapManager mapManager, IEventBus eventBus, IVisionDataSystem visionData)
        {
            if (_isInitialized) return;

            _mapManager = mapManager;
            _eventBus = eventBus;
            _visionData = visionData;

            if (_mapManager == null || _eventBus == null || _visionData == null) return;

            if (TargetTileMap == null)
            {
                GD.PrintErr("[TerrainVisualizer2D] 致命错误：未指定 TargetTileMap！请在编辑器中拖入或新建一个带有设定集的 TileMapLayer 节点到此坑位。");
                return;
            }

            LoadRenderConfig();

            TargetTileMap.Visible = false; // 隐藏作为预制件的根节点

            // 1. 初始化 LRU 缓存池
            for (int i = 0; i < MaxCachedLayers; i++)
            {
                var gLayer = (TileMapLayer)TargetTileMap.Duplicate();
                var oLayer = new TileMapLayer();
                oLayer.TileSet = TargetTileMap.TileSet;
                oLayer.ZIndex = 1; // 确保墙壁挡这层上面

                var fLayer = new TileMapLayer();
                fLayer.TileSet = TargetTileMap.TileSet;
                fLayer.ZIndex = 2; // 迷雾盖在最上方
                fLayer.Modulate = new Color(0, 0, 0, 0.65f); // 65% 纯黑迷雾滤镜

                AddChild(gLayer);
                AddChild(oLayer);
                AddChild(fLayer);

                gLayer.Visible = false;
                oLayer.Visible = false;
                fLayer.Visible = false;

                _layerCache.Add(new LayerCacheItem
                {
                    GroundLayer = gLayer,
                    ObstacleLayer = oLayer,
                    FogLayer = fLayer
                });
            }

            _camera = new Camera2D();
            _camera.Zoom = new Vector2(0.8f, 0.8f);
            // 瓦片大小 32f
            _camera.Position = new Vector2((_mapManager.Width / 2f) * 32f, (_mapManager.Height / 2f) * 32f);
            AddChild(_camera);
            _camera.MakeCurrent();

            _currentZLevel = 15;

            _onTerrainModHandler = OnTerrainModified;
            _eventBus.Subscribe(_onTerrainModHandler);

            _onVisionUpdatedHandler = OnVisionUpdated;
            _eventBus.Subscribe(_onVisionUpdatedHandler);

            // 初始化基础地板 Atlas 的映射（针对增量极速 SetCell 绘制）
            InitDefaultFloorAtlas();

            // 首层触发加载
            ChangeZLevel(_currentZLevel);

            _isInitialized = true;
        }

        private void InitDefaultFloorAtlas()
        {
            // 根据你的 Godot 瓦片图集 (来源ID为 Source: 1) 提供如下精准映射：
            _defaultFloorAtlas[TerrainType.Bedrock] = new Vector2I(2, 0); // 黑色
            _defaultFloorAtlas[TerrainType.Stone] = new Vector2I(1, 0); // 深灰色
            _defaultFloorAtlas[TerrainType.Dirt] = new Vector2I(0, 0); // 棕色
            _defaultFloorAtlas[TerrainType.Grass] = new Vector2I(1, 1); // 浅绿色
            _defaultFloorAtlas[TerrainType.Water] = new Vector2I(0, 2); // 蓝色
            _defaultFloorAtlas[TerrainType.Iron] = new Vector2I(3, 0); // 
            _defaultFloorAtlas[TerrainType.Sand] = new Vector2I(0, 1); // 浅米色/沙色
            _defaultFloorAtlas[TerrainType.Coal] = new Vector2I(1, 2); // 
        }

        private LayerCacheItem TryGetCachedLayer(int zLevel)
        {
            foreach (var item in _layerCache)
            {
                if (item.ZLevel == zLevel) return item;
            }
            return null;
        }

        private LayerCacheItem GetOrLoadLayer(int zLevel)
        {
            LayerCacheItem target = TryGetCachedLayer(zLevel);

            if (target != null)
            {
                target.LastAccessedTicks = DateTime.UtcNow.Ticks;
                return target;
            }

            // LRU 淘汰：寻找最久没有被访问过的层
            target = _layerCache[0];
            foreach (var item in _layerCache)
            {
                if (item.LastAccessedTicks < target.LastAccessedTicks)
                {
                    target = item;
                }
            }

            // 剔除旧数据并重新装载新楼层！
            target.ZLevel = zLevel;
            target.LastAccessedTicks = DateTime.UtcNow.Ticks;
            DrawFloor(zLevel, target.GroundLayer, target.ObstacleLayer, target.FogLayer);

            return target;
        }

        /// <summary>
        /// 接收到后端视野数据更新事件时触发 (按需增量渲染)
        /// </summary>
        private void OnVisionUpdated(ref VisionUpdatedEvent e)
        {
            if (IgnoreVision) return;

            var cachedLayer = TryGetCachedLayer(e.ZLevel);
            if (cachedLayer == null) return;

            int floorZ = e.ZLevel - 1;
            var obstacleCoordsByTerrain = new Dictionary<int, Godot.Collections.Array<Vector2I>>();

            var coordsToDraw = new HashSet<Vector2I>(e.NewlyVisibleCoords);
            coordsToDraw.UnionWith(e.NewlyExploredCoords);

            foreach (var pos in coordsToDraw)
            {
                // 1. 在 groundLayer 印上地砖 (使用极速 SetCell)
                if (floorZ >= 0 && _mapManager.IsWithinBounds(pos.X, pos.Y, floorZ))
                {
                    TileData floorData = _mapManager.GetTile(pos.X, pos.Y, floorZ);
                    if (floorData.Type != TerrainType.Air && _defaultFloorAtlas.TryGetValue(floorData.Type, out Vector2I atlasCoord))
                    {
                        cachedLayer.GroundLayer.SetCell(pos, 0, atlasCoord);
                    }
                }

                // 2. 收集 obstacleLayer 当层的墙壁
                if (_mapManager.IsWithinBounds(pos.X, pos.Y, e.ZLevel))
                {
                    TileData obstacleData = _mapManager.GetTile(pos.X, pos.Y, e.ZLevel);
                    if (obstacleData.Type != TerrainType.Air && _terrainCoords.TryGetValue(obstacleData.Type, out int tId))
                    {
                        if (!obstacleCoordsByTerrain.ContainsKey(tId))
                            obstacleCoordsByTerrain[tId] = new Godot.Collections.Array<Vector2I>();
                        obstacleCoordsByTerrain[tId].Add(pos);
                    }
                }
            }

            foreach (var kvp in obstacleCoordsByTerrain)
            {
                cachedLayer.ObstacleLayer.SetCellsTerrainConnect(kvp.Value, 0, kvp.Key, true);
            }

            // 处理迷雾的动态遮罩
            Vector2I fogAtlasCoord = new Vector2I(2, 0); // 使用黑色作为迷雾替代贴图
            foreach(var pos in e.NewlyVisibleCoords)
            {
                cachedLayer.FogLayer.EraseCell(pos);
            }
            foreach(var pos in e.NewlyHiddenCoords)
            {
                cachedLayer.FogLayer.SetCell(pos, 0, fogAtlasCoord);
            }
        }

        /// <summary>
        /// 当 LRU 淘汰重新分配新楼层时的全屏重绘（完全兼容 IgnoreVision 全视模式）
        /// </summary>
        private void DrawFloor(int zLevel, TileMapLayer groundLayer, TileMapLayer obstacleLayer, TileMapLayer fogLayer)
        {
            groundLayer.Clear();
            obstacleLayer.Clear();
            fogLayer.Clear();

            IEnumerable<Vector2I> tilesToRender;

            if (IgnoreVision)
            {
                // 调试沙盒模式：画出当前整层的所有格子
                var fullMap = new List<Vector2I>();
                for (int x = 0; x < _mapManager.Width; x++)
                    for (int y = 0; y < _mapManager.Height; y++)
                        fullMap.Add(new Vector2I(x, y));
                tilesToRender = fullMap;
            }
            else
            {
                // 游戏模式：仅画出被探索过的格子
                tilesToRender = _visionData.GetExploredTiles(zLevel);
            }

            int floorZ = zLevel - 1;
            var obstacleCoordsByTerrain = new Dictionary<int, Godot.Collections.Array<Vector2I>>();

            foreach (var pos in tilesToRender)
            {
                // 画 Z-1 的极速地板层
                if (floorZ >= 0 && _mapManager.IsWithinBounds(pos.X, pos.Y, floorZ))
                {
                    TileData floorData = _mapManager.GetTile(pos.X, pos.Y, floorZ);
                    if (floorData.Type != TerrainType.Air && _defaultFloorAtlas.TryGetValue(floorData.Type, out Vector2I atlasCoord))
                    {
                        groundLayer.SetCell(pos, 0, atlasCoord);
                    }
                }

                // 收集 Z 的墙壁
                if (_mapManager.IsWithinBounds(pos.X, pos.Y, zLevel))
                {
                    TileData obstacleData = _mapManager.GetTile(pos.X, pos.Y, zLevel);
                    if (obstacleData.Type != TerrainType.Air && _terrainCoords.TryGetValue(obstacleData.Type, out int tId))
                    {
                        if (!obstacleCoordsByTerrain.ContainsKey(tId))
                            obstacleCoordsByTerrain[tId] = new Godot.Collections.Array<Vector2I>();
                        obstacleCoordsByTerrain[tId].Add(pos);
                    }
                }
            }

            // 统一渲染 Z 层的固体墙壁，跑一次 TerrainConnect 优化性能
            foreach (var kvp in obstacleCoordsByTerrain)
            {
                obstacleLayer.SetCellsTerrainConnect(kvp.Value, 0, kvp.Key, true);
            }

            // 全局盖上一层迷雾
            if (!IgnoreVision)
            {
                Vector2I fogAtlasCoord = new Vector2I(2, 0); 
                foreach (var pos in tilesToRender)
                {
                    if (!_visionData.IsCurrentlyVisible(pos.X, pos.Y, zLevel))
                    {
                        fogLayer.SetCell(pos, 0, fogAtlasCoord);
                    }
                }
            }
        }

        public override void _Process(double delta)
        {
            if (_camera == null || !Visible) return;

            Vector2 moveDir = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) moveDir.Y -= 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) moveDir.Y += 1;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) moveDir.X -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) moveDir.X += 1;

            if (moveDir != Vector2.Zero)
            {
                _camera.Position += moveDir.Normalized() * _cameraSpeed * (float)delta;
            }
        }

        private void OnTerrainModified(ref TerrainModifiedEvent e)
        {
            if (e.OldType == e.NewType) return; // 拦截同质化物理重写（如水体更新流速引起的冗余渲染调用），极大提升帧率

            // 物理防抖判断在方法头部已执行。
            int modZ = e.Position.Z;
            Vector2I pos = new Vector2I(e.Position.X, e.Position.Y);

            // 获取被影响的缓存画板：
            // 若某画板代表 modZ 层，则其被修改的是“墙体障碍物”
            var layerAsObstacle = TryGetCachedLayer(modZ);
            
            // 若某画板代表 modZ+1 层，则其被修改的是脚下的“地面”（Z-1就是修改层）
            var layerAsGround = TryGetCachedLayer(modZ + 1);

            // 【情况 B】：挖当层面前的墙 (Z)
            if (layerAsObstacle != null)
            {
                // 判断视野是否被允许更新：开启超级沙盒，或是这层在这格被探索过
                if (IgnoreVision || _visionData.IsExplored(pos.X, pos.Y, layerAsObstacle.ZLevel))
                {
                    if (e.NewType == TerrainType.Air)
                    {
                        var singleCoordArr = new Godot.Collections.Array<Vector2I> { pos };
                        layerAsObstacle.ObstacleLayer.SetCellsTerrainConnect(singleCoordArr, 0, -1, true);
                    }
                    else if (_terrainCoords.TryGetValue(e.NewType, out int tId))
                    {
                        var singleCoordArr = new Godot.Collections.Array<Vector2I> { pos };
                        layerAsObstacle.ObstacleLayer.SetCellsTerrainConnect(singleCoordArr, 0, tId, true);
                    }
                }
            }

            // 【情况 A】：挖当前层脚下的地 (Z - 1)
            if (layerAsGround != null)
            {
                // 视野逻辑判定同理
                if (IgnoreVision || _visionData.IsExplored(pos.X, pos.Y, layerAsGround.ZLevel))
                {
                    if (e.NewType == TerrainType.Air)
                    {
                        layerAsGround.GroundLayer.EraseCell(pos);
                    }
                    else if (_defaultFloorAtlas.TryGetValue(e.NewType, out Vector2I atlasCoord))
                    {
                        layerAsGround.GroundLayer.SetCell(pos, 0, atlasCoord);
                    }
                }
            }
        }

        private void ChangeZLevel(int newZ)
        {
            newZ = Mathf.Clamp(newZ, 0, _mapManager.Depth - 1);
            if (newZ != _currentZLevel || _currentZLevel == -1)
            {
                // 先安全隐藏当前旧楼层（如果有常驻则不影响，仅仅设不可见）
                var oldLayer = TryGetCachedLayer(_currentZLevel);
                if (oldLayer != null)
                {
                    oldLayer.GroundLayer.Visible = false;
                    oldLayer.ObstacleLayer.Visible = false;
                }

                _currentZLevel = newZ;
                
                // 从 LRU 获取或瞬间冷加载并显示目标层！
                var currentItem = GetOrLoadLayer(_currentZLevel);
                currentItem.GroundLayer.Visible = true;
                currentItem.ObstacleLayer.Visible = true;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!_isInitialized) return;

            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.Pageup) ChangeZLevel(_currentZLevel + 1);
                else if (keyEvent.Keycode == Key.Pagedown) ChangeZLevel(_currentZLevel - 1);
            }

            if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
            {
                if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
                {
                    if (_camera != null) _camera.Zoom *= 1.1f;
                    return;
                }
                else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
                {
                    if (_camera != null) _camera.Zoom *= 0.9f;
                    return;
                }
            }
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                if (_onTerrainModHandler != null) _eventBus.Unsubscribe(_onTerrainModHandler);
                if (_onVisionUpdatedHandler != null) _eventBus.Unsubscribe(_onVisionUpdatedHandler);
            }
        }

        private void LoadRenderConfig()
        {
            string configPath = "res://assets/config/terrain_config.json";
            if (!Godot.FileAccess.FileExists(configPath)) return;

            try
            {
                string jsonText = Godot.FileAccess.GetFileAsString(configPath);
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonText))
                {
                    var types = doc.RootElement.GetProperty("terrain").GetProperty("types");
                    foreach (var element in types.EnumerateArray())
                    {
                        int id = element.GetProperty("id").GetInt32();
                        if (element.TryGetProperty("terrain", out System.Text.Json.JsonElement terrainProp))
                        {
                            _terrainCoords[(TerrainType)id] = terrainProp.GetInt32();
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[Visualizer] Parse failed: {ex.Message}");
            }
        }
    }
}
