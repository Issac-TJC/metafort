using Godot;
using MetaFort.Core.Spatial;

namespace MetaFort.Test_Control
{
    public partial class ControlTestSandbox : Node
    {
        [Export] 
        public MetaFort.Visual.TerrainVisualizer2D Visualizer;

        private IMapManager _mapManager;

        public override void _Ready()
        {
            if (GameEntry.Instance != null)
            {
                _mapManager = GameEntry.Instance.MapManager;
            }
            else
            {
                GD.PrintErr("[ControlTestSandbox] GameEntry not found!");
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (_mapManager == null || Visualizer == null || Visualizer.TargetTileMap == null) return;

            if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
            {
                // Only process Left and Right clicks
                if (mouseBtn.ButtonIndex != MouseButton.Left && mouseBtn.ButtonIndex != MouseButton.Right)
                    return;

                Vector2 globalMousePos = Visualizer.GetGlobalMousePosition();
                Vector2I mapPos = Visualizer.TargetTileMap.LocalToMap(Visualizer.TargetTileMap.ToLocal(globalMousePos));

                TerrainType targetType = TerrainType.Air;
                bool actionValid = false;
                bool modifyZMinus1 = Input.IsKeyPressed(Key.Shift); // 按住Shift挖脚下 

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
                    // Map the visualizer's internal private state using Reflection or Getter if needed, 
                    // or since we are loosely coupled, we just use string property lookup
                    int currentZ = (int)Visualizer.Get("_currentZLevel");
                    int targetZ = modifyZMinus1 ? currentZ - 1 : currentZ;

                    // 沙盒物理界限特判：禁止任何操作试图击穿最底层的物理宇宙基岩！
                    if (targetZ == 0 && targetType == TerrainType.Air)
                    {
                        return;
                    }

                    if (targetZ >= 0 && targetZ < _mapManager.Depth)
                    {
                        _mapManager.ReplaceTile(mapPos.X, mapPos.Y, targetZ, targetType);
                    }
                }
            }
        }
    }
}
