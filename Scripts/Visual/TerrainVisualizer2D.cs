using Godot;
using System;
using System.Collections.Generic;
using MetaFort.Core.EventBus;
using MetaFort.Core.Spatial;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort.Visual
{
    public partial class TerrainVisualizer2D : Node2D
    {
        [Export]
        public TileMapLayer TargetTileMap; 

        private int _currentZLevel = 0;
        private IMapManager _mapManager;
        private IEventBus _eventBus;

        private Camera2D _camera;
        private float _cameraSpeed = 1200f; 

        private readonly Dictionary<TerrainType, int> _terrainCoords = new Dictionary<TerrainType, int>();
        private GameEventHandler<TerrainModifiedEvent> _onTerrainModHandler;

        private Vector2I[] _visualCache;
        private TileMapLayer _shadowLayer;
        private TileMapLayer _displayLayer;
        
        private Rect2I _lastVisibleRect;

        // [优化]: 脏图更新列队，批处理降低 C++ 层 API 调用延迟
        private Dictionary<int, HashSet<Vector2I>> _dirtyCellsByZ = new Dictionary<int, HashSet<Vector2I>>();

        public override void _Ready()
        {
            _mapManager = GameEntry.Instance.MapManager;
            _eventBus = GameEntry.Instance.EventBus;

            if (_mapManager == null || _eventBus == null || TargetTileMap == null) return;

            LoadRenderConfig();

            int capacity = _mapManager.Width * _mapManager.Height * _mapManager.Depth;
            _visualCache = new Vector2I[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _visualCache[i] = new Vector2I(-1, -1);
            }

            _displayLayer = TargetTileMap; 
            _shadowLayer = new TileMapLayer();
            _shadowLayer.TileSet = _displayLayer.TileSet;
            _shadowLayer.Visible = false; 

            BakeAllTerrain();

            _camera = new Camera2D();
            _camera.Zoom = new Vector2(0.8f, 0.8f);
            _camera.Position = new Vector2((_mapManager.Width / 2f) * 32f, (_mapManager.Height / 2f) * 32f); 
            AddChild(_camera);
            _camera.MakeCurrent(); 

            _currentZLevel = _mapManager.Depth - 1; 

            _onTerrainModHandler = OnTerrainModified;
            _eventBus.Subscribe(_onTerrainModHandler);

            ForceRenderViewport();
        }

        private void BakeAllTerrain()
        {
            GD.Print("[Visualizer] Starting Shadow Baking Process...");
            for (int z = 0; z < _mapManager.Depth; z++)
            {
                _shadowLayer.Clear();
                var cellsByTerrainId = new Dictionary<int, Godot.Collections.Array<Vector2I>>();

                for (int x = 0; x < _mapManager.Width; x++)
                {
                    for (int y = 0; y < _mapManager.Height; y++)
                    {
                        TileData data = _mapManager.GetTile(x, y, z);
                        if (data.Type == TerrainType.Air) continue;

                        if (_terrainCoords.TryGetValue(data.Type, out int terrainId))
                        {
                            if (!cellsByTerrainId.ContainsKey(terrainId))
                                cellsByTerrainId[terrainId] = new Godot.Collections.Array<Vector2I>();
                            cellsByTerrainId[terrainId].Add(new Vector2I(x, y));
                        }
                    }
                }

                foreach (var kvp in cellsByTerrainId)
                {
                    _shadowLayer.SetCellsTerrainConnect(kvp.Value, 0, kvp.Key, false);
                }

                for (int x = 0; x < _mapManager.Width; x++)
                {
                    for (int y = 0; y < _mapManager.Height; y++)
                    {
                        TileData data = _mapManager.GetTile(x, y, z);
                        int flatIndex = _mapManager.GetFlatIndex(x, y, z);
                        
                        if (data.Type == TerrainType.Air) _visualCache[flatIndex] = new Vector2I(-1, -1);
                        else _visualCache[flatIndex] = _shadowLayer.GetCellAtlasCoords(new Vector2I(x, y));
                    }
                }
            }
            _shadowLayer.Clear(); 
            GD.Print("[Visualizer] Shadow Baking Complete.");
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

            // [优化]: 聚合消费处理瀑布造成的批量水流脏图更新
            if (_dirtyCellsByZ.Count > 0)
            {
                ProcessDirtyCells();
            }

            Rect2I currentRect = GetVisibleGridRect();
            if (currentRect != _lastVisibleRect)
            {
                _lastVisibleRect = currentRect;
                RenderViewport(currentRect);
            }
        }

        private void OnTerrainModified(ref TerrainModifiedEvent e)
        {
            int z = e.Position.Z;
            if (!_dirtyCellsByZ.ContainsKey(z)) _dirtyCellsByZ[z] = new HashSet<Vector2I>();
            _dirtyCellsByZ[z].Add(new Vector2I(e.Position.X, e.Position.Y));
        }

        private void ProcessDirtyCells()
        {
            foreach (var kvp in _dirtyCellsByZ)
            {
                int zLevel = kvp.Key;
                HashSet<Vector2I> dirtySet = kvp.Value;
                if (dirtySet.Count == 0) continue;

                var contextSet = new Dictionary<int, HashSet<Vector2I>>();
                var coreCells = new HashSet<Vector2I>();

                foreach (var pos in dirtySet)
                {
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            coreCells.Add(new Vector2I(pos.X + dx, pos.Y + dy));

                    for (int dx = -2; dx <= 2; dx++)
                    {
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            int nx = pos.X + dx;
                            int ny = pos.Y + dy;
                            if (_mapManager.IsWithinBounds(nx, ny, zLevel))
                            {
                                TileData data = _mapManager.GetTile(nx, ny, zLevel);
                                if (data.Type != TerrainType.Air && _terrainCoords.TryGetValue(data.Type, out int tId))
                                {
                                    if (!contextSet.ContainsKey(tId)) contextSet[tId] = new HashSet<Vector2I>();
                                    contextSet[tId].Add(new Vector2I(nx, ny));
                                }
                            }
                        }
                    }
                }

                _shadowLayer.Clear();
                foreach (var cKvp in contextSet)
                {
                    var godotArr = new Godot.Collections.Array<Vector2I>(cKvp.Value);
                    _shadowLayer.SetCellsTerrainConnect(godotArr, 0, cKvp.Key, false);
                }

                foreach (var pos in coreCells)
                {
                    if (!_mapManager.IsWithinBounds(pos.X, pos.Y, zLevel)) continue;
                    
                    TileData data = _mapManager.GetTile(pos.X, pos.Y, zLevel);
                    int flatIndex = _mapManager.GetFlatIndex(pos.X, pos.Y, zLevel);

                    Vector2I newAtlas = new Vector2I(-1, -1);
                    if (data.Type != TerrainType.Air)
                    {
                        newAtlas = _shadowLayer.GetCellAtlasCoords(pos);
                    }
                    
                    _visualCache[flatIndex] = newAtlas;

                    if (zLevel == _currentZLevel && _lastVisibleRect.HasPoint(pos))
                    {
                        if (newAtlas.X == -1) _displayLayer.EraseCell(pos);
                        else _displayLayer.SetCell(pos, 0, newAtlas);
                    }
                }
            }
            _dirtyCellsByZ.Clear();
            _shadowLayer.Clear();
        }

        private Rect2I GetVisibleGridRect()
        {
            if (_camera == null || _displayLayer == null || _displayLayer.TileSet == null) return new Rect2I();

            Vector2 tileSize = (Vector2)_displayLayer.TileSet.TileSize;
            Vector2 viewportSize = GetViewportRect().Size;
            Vector2 visibleWorldSize = viewportSize / _camera.Zoom;

            Vector2 topLeftWorld = _camera.Position - (visibleWorldSize / 2f);
            Vector2 botRightWorld = _camera.Position + (visibleWorldSize / 2f);

            int minX = Mathf.FloorToInt(topLeftWorld.X / tileSize.X);
            int minY = Mathf.FloorToInt(topLeftWorld.Y / tileSize.Y);
            int maxX = Mathf.CeilToInt(botRightWorld.X / tileSize.X);
            int maxY = Mathf.CeilToInt(botRightWorld.Y / tileSize.Y);

            minX -= 2; minY -= 2;
            maxX += 2; maxY += 2;

            minX = Mathf.Clamp(minX, 0, _mapManager.Width - 1);
            minY = Mathf.Clamp(minY, 0, _mapManager.Height - 1);
            maxX = Mathf.Clamp(maxX, 0, _mapManager.Width - 1);
            maxY = Mathf.Clamp(maxY, 0, _mapManager.Height - 1);

            return new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private void ForceRenderViewport()
        {
            _lastVisibleRect = GetVisibleGridRect();
            RenderViewport(_lastVisibleRect);
        }

        private void RenderViewport(Rect2I rect)
        {
            _displayLayer.Clear();

            int endX = rect.Position.X + rect.Size.X;
            int endY = rect.Position.Y + rect.Size.Y;

            for (int x = rect.Position.X; x < endX; x++)
            {
                for (int y = rect.Position.Y; y < endY; y++)
                {
                    int flatIndex = _mapManager.GetFlatIndex(x, y, _currentZLevel);
                    Vector2I cachedAtlas = _visualCache[flatIndex];

                    if (cachedAtlas.X != -1)
                    {
                        _displayLayer.SetCell(new Vector2I(x, y), 0, cachedAtlas);
                    }
                }
            }
        }

        private void ChangeZLevel(int newZ)
        {
            newZ = Mathf.Clamp(newZ, 0, _mapManager.Depth - 1);
            if (newZ != _currentZLevel)
            {
                _currentZLevel = newZ;
                ForceRenderViewport(); 
            }
        }

        public override void _ExitTree()
        {
            if (_eventBus != null && _onTerrainModHandler != null)
                _eventBus.Unsubscribe(_onTerrainModHandler);
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

        public override void _UnhandledInput(InputEvent @event)
        {
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
                    ForceRenderViewport(); 
                    return; 
                }
                else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
                {
                    if (_camera != null) _camera.Zoom *= 0.9f;
                    ForceRenderViewport();
                    return; 
                }

                Vector2 globalMousePos = GetGlobalMousePosition();
                Vector2I mapPos = TargetTileMap.LocalToMap(TargetTileMap.ToLocal(globalMousePos));

                TerrainType targetType = TerrainType.Air;
                bool actionValid = false;

                if (mouseBtn.ButtonIndex == MouseButton.Left)
                {
                    targetType = TerrainType.Air;
                    actionValid = true;
                }
                else if (mouseBtn.ButtonIndex == MouseButton.Right)
                {
                    targetType = TerrainType.Stone;
                    actionValid = true;
                }

                if (actionValid)
                {
                    _mapManager.ReplaceTile(mapPos.X, mapPos.Y, _currentZLevel, targetType);
                }
            }
        }
    }
}
