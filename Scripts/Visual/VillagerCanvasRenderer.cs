using Godot;
using System;
using MetaFort.Core.ECS;

namespace MetaFort.Visual
{
    public partial class VillagerCanvasRenderer : Node2D
    {
        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public Godot.Collections.Array<Texture2D> TorsoTextures;

        [Export]
        public Godot.Collections.Array<Texture2D> HeadTextures;

        [Export]
        public Godot.Collections.Array<Texture2D> ClothesTextures;

        [Export]
        public Godot.Collections.Array<Texture2D> HairTextures;

        [Export]
        public Texture2D SelectionRingTexture;

        [Export(PropertyHint.Range, "0.1,5.0,0.1")]
        public float CharacterScale = 1.0f;

        [Export]
        public Vector2 GlobalRenderOffset = Vector2.Zero;

        [Export]
        public Vector2 TorsoOffset = Vector2.Zero;

        [Export]
        public Vector2 HeadOffset = Vector2.Zero;

        [Export]
        public Vector2 ClothesOffset = Vector2.Zero;

        [Export]
        public Vector2 HairOffset = Vector2.Zero;

        public int CurrentZLevel { get; set; } = -1;

        private IEntityManager _entityManager;
        private int _lastRenderSignature;
        private bool _redrawQueued = true;

        private const float TileSize = 32f;

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry != null)
            {
                _entityManager = gameEntry.EntityManager;
            }
        }

        public void InjectDependencies(IEntityManager entityManager)
        {
            _entityManager = entityManager;
            _redrawQueued = true;
        }

        public override void _Process(double delta)
        {
            if (_entityManager == null)
            {
                return;
            }

            int currentSignature = BuildRenderSignature();
            if (_redrawQueued || currentSignature != _lastRenderSignature)
            {
                _lastRenderSignature = currentSignature;
                _redrawQueued = false;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (_entityManager == null)
            {
                return;
            }

            if (_entityManager.GetComponentCount<VillagerVisualComponent>() == 0)
            {
                return;
            }

            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<VillagerVisualComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(id))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                if ((int)pos.Z != CurrentZLevel)
                {
                    continue;
                }

                Vector2 screenPos = new Vector2(pos.X * TileSize, pos.Y * TileSize);
                Vector2 tileCenter = screenPos + new Vector2(16, 16);
                Vector2 tileBottomCenter = screenPos + new Vector2(16, 32);
                ref VillagerVisualComponent vis = ref _entityManager.GetComponent<VillagerVisualComponent>(id);

                if (_entityManager.HasComponent<PlayerSelectedComponent>(id))
                {
                    if (SelectionRingTexture != null)
                    {
                        Vector2 ringSize = SelectionRingTexture.GetSize();
                        DrawTexture(SelectionRingTexture, tileCenter - ringSize / 2f);
                    }
                    else
                    {
                        DrawArc(tileCenter, 14f, 0, Mathf.Pi * 2, 16, Colors.Yellow, 2f);
                    }
                }

                bool hasDrawnAnything = false;
                Action<Godot.Collections.Array<Texture2D>, int, Vector2> drawPart = (arr, partId, partOffset) =>
                {
                    if (!TryGetAnyTexture(arr, partId, out Texture2D tex))
                    {
                        return;
                    }

                    Vector2 size = tex.GetSize() * CharacterScale;
                    Vector2 drawOrigin = tileBottomCenter - new Vector2(size.X / 2f, size.Y);
                    Vector2 finalPos = drawOrigin + GlobalRenderOffset + partOffset;
                    DrawTextureRect(tex, new Rect2(finalPos, size), false);
                    hasDrawnAnything = true;
                };

                drawPart(TorsoTextures, vis.TorsoId, TorsoOffset);
                drawPart(HeadTextures, vis.HeadId, HeadOffset);
                drawPart(ClothesTextures, vis.ClothesId, ClothesOffset);
                drawPart(HairTextures, vis.HairId, HairOffset);

                if (!hasDrawnAnything)
                {
                    Color skinColor = new Color(vis.SkinColorHex);
                    if (skinColor.A == 0)
                    {
                        skinColor = Colors.NavajoWhite;
                    }

                    Vector2 fallbackSize = new Vector2(24, 24) * CharacterScale;
                    Vector2 fallbackOrigin = tileBottomCenter - new Vector2(fallbackSize.X / 2f, fallbackSize.Y);
                    Rect2 rect = new Rect2(fallbackOrigin + GlobalRenderOffset, fallbackSize);
                    DrawRect(rect, skinColor);
                }
            }
        }

        private bool TryGetAnyTexture(Godot.Collections.Array<Texture2D> arr, int id, out Texture2D tex)
        {
            tex = null;
            if (arr == null || arr.Count == 0)
            {
                return false;
            }

            int index = id % arr.Count;
            tex = arr[index];
            return tex != null;
        }

        private int BuildRenderSignature()
        {
            HashCode hash = new HashCode();
            hash.Add(CurrentZLevel);

            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<VillagerVisualComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint id = entityIds[i];
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(id))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent pos = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(id);
                if ((int)pos.Z != CurrentZLevel)
                {
                    continue;
                }

                ref VillagerVisualComponent vis = ref _entityManager.GetComponent<VillagerVisualComponent>(id);
                hash.Add(id);
                hash.Add(Mathf.RoundToInt(pos.X * 100f));
                hash.Add(Mathf.RoundToInt(pos.Y * 100f));
                hash.Add(Mathf.RoundToInt(pos.Z * 100f));
                hash.Add(vis.HeadId);
                hash.Add(vis.TorsoId);
                hash.Add(vis.ClothesId);
                hash.Add(vis.HairId);
                hash.Add(vis.SkinColorHex);
                hash.Add(_entityManager.HasComponent<PlayerSelectedComponent>(id));
            }

            return hash.ToHashCode();
        }

        private MetaFort.GameEntry ResolveGameEntry()
        {
            if (CoreSourcePath != null && !CoreSourcePath.IsEmpty)
            {
                return GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            }

            return GetNodeOrNull<MetaFort.GameEntry>("../GameEntry") ?? MetaFort.GameEntry.Instance;
        }
    }
}
