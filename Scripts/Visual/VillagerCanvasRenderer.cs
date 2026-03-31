using Godot;
using System;
using MetaFort.Core.ECS;

namespace MetaFort.Visual
{
    public partial class VillagerCanvasRenderer : Node2D
    {
        [Export]
        public Godot.Collections.Array<Texture2D> TorsoTextures;
        [Export]
        public Godot.Collections.Array<Texture2D> HeadTextures;
        [Export] public Godot.Collections.Array<Texture2D> ClothesTextures;
        [Export] public Godot.Collections.Array<Texture2D> HairTextures;
        
        [Export] public Texture2D SelectionRingTexture;

        // 【新增】美术对齐专用属性，在 Inspector 中动态调整，再也不怕乱轴！
        [Export(PropertyHint.Range, "0.1,5.0,0.1")] public float CharacterScale = 1.0f;
        [Export] public Vector2 GlobalRenderOffset = Vector2.Zero; // 底部中心对齐法本身即是完美的，自带 0,0 即可脚踩黄圈中心
        [Export] public Vector2 TorsoOffset = Vector2.Zero;
        [Export] public Vector2 HeadOffset = Vector2.Zero;
        [Export] public Vector2 ClothesOffset = Vector2.Zero;
        [Export] public Vector2 HairOffset = Vector2.Zero;

        public int CurrentZLevel { get; set; } = -1;
        private IEntityManager _entityManager;
        
        private const float TileSize = 32f;
        
        public override void _Ready()
        {
            if (GameEntry.Instance != null)
            {
                _entityManager = GameEntry.Instance.EntityManager;
            }
        }

        public void InjectDependencies(IEntityManager entityManager)
        {
            _entityManager = entityManager;
        }

        public override void _Process(double delta)
        {
            // 对于 Immediate Mode，每帧直接请求重绘制 (对Godot而言极低开销)
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_entityManager == null) return;
            
            var placeholderSize = new Vector2(24, 24);
            var offset = new Vector2(-12, -24); // 脚底为原点

            int visCount = _entityManager.GetComponentCount<VillagerVisualComponent>();
            if (visCount == 0) return;

            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<VillagerVisualComponent>();

            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];

                if (_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(id))
                {
                    ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                    
                    // Z轴剔除：不属于当前观察楼层的实体，直接通过 `continue` 省去后续所有渲染工作！
                    if ((int)pos.Z != CurrentZLevel) continue;
                    
                    Vector2 screenPos = new Vector2(pos.X * TileSize, pos.Y * TileSize); // 格子的左上角
                    Vector2 tileCenterStr = screenPos + new Vector2(16, 16);            // 格子的纯物理中心
                    Vector2 tileBottomCenter = screenPos + new Vector2(16, 32);         // 格子的正下方踩踏接引点
                    
                    ref VillagerVisualComponent vis = ref _entityManager.GetComponent<VillagerVisualComponent>(id);

                    // 1. 高亮玩家选中的单元
                    if (_entityManager.HasComponent<PlayerSelectedComponent>(id))
                    {
                        if (SelectionRingTexture != null)
                        {
                            Vector2 ringSize = SelectionRingTexture.GetSize();
                            DrawTexture(SelectionRingTexture, tileCenterStr - ringSize / 2f);
                        }
                        else 
                        {
                            DrawArc(tileCenterStr, 14f, 0, Mathf.Pi * 2, 16, Colors.Yellow, 2f);
                        }
                    }

                    // 2. 绘制四大外观部件结构 (新增自定义位移与缩放锚点逻辑)
                    bool hasDrawnAnything = false;

                    // 为了追求极致性能，这里按顺序叠加，避免Node树结构组装
                    Action<Godot.Collections.Array<Texture2D>, int, Vector2> drawPart = (arr, partId, partOffset) =>
                    {
                        if (TryGetAnyTexture(arr, partId, out var tex))
                        {
                            Vector2 size = tex.GetSize() * CharacterScale;
                            // 真正以脚底盘作为锚点计算，这才是真正的对齐大法！无论素材多庞大，双脚永远紧贴当前格子的正下方
                            Vector2 drawOrigin = tileBottomCenter - new Vector2(size.X / 2f, size.Y);
                            var finalPos = drawOrigin + GlobalRenderOffset + partOffset;
                            DrawTextureRect(tex, new Rect2(finalPos, size), false);
                            hasDrawnAnything = true;
                        }
                    };

                    drawPart(TorsoTextures, vis.TorsoId, TorsoOffset);
                    drawPart(HeadTextures, vis.HeadId, HeadOffset);
                    drawPart(ClothesTextures, vis.ClothesId, ClothesOffset);
                    drawPart(HairTextures, vis.HairId, HairOffset);

                    // 3. 开发者无贴图状态时的 Fallback 色块测试替代
                    if (!hasDrawnAnything)
                    {
                        Color skinColor = new Color(vis.SkinColorHex);
                        if (skinColor.A == 0) skinColor = Colors.NavajoWhite; 
                        
                        Vector2 fbSize = new Vector2(24, 24) * CharacterScale;
                        Vector2 fbDrawOrigin = tileBottomCenter - new Vector2(fbSize.X / 2f, fbSize.Y);
                        Rect2 rect = new Rect2(fbDrawOrigin + GlobalRenderOffset, fbSize);
                        DrawRect(rect, skinColor);
                    }
                }
            }
        }

        private bool TryGetAnyTexture(Godot.Collections.Array<Texture2D> arr, int id, out Texture2D tex)
        {
            tex = null;
            if (arr == null || arr.Count == 0) return false;
            
            // 安全取余，确保只要有图就一定能随到一张，告别越界和无效字典键
            int index = id % arr.Count;
            tex = arr[index];
            return tex != null;
        }
    }
}
