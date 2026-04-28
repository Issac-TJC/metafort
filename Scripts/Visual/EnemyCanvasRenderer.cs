using Godot;
using System;
using MetaFort.Core.ECS;
using MetaFort.Core.Enemy;
using MetaFort.Core.Spatial;

namespace MetaFort.Visual
{
    public partial class EnemyCanvasRenderer : Node2D
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

        [Export]
        public bool EnableFogVisibilityGate { get; set; } = true;

        public int CurrentZLevel { get; set; } = -1;

        private IEntityManager _entityManager;
        private IVisionDataSystem _visionDataSystem;
        private int _lastRenderSignature;
        private bool _redrawQueued = true;
        private const float TileSize = 32f;

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry != null)
            {
                _entityManager = gameEntry.EntityManager;
                _visionDataSystem = gameEntry.VisionData;
            }
        }

        public void InjectDependencies(IEntityManager entityManager, IVisionDataSystem visionDataSystem)
        {
            _entityManager = entityManager;
            _visionDataSystem = visionDataSystem;
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
            if (_entityManager == null || _entityManager.GetComponentCount<EnemyVisualComponent>() == 0)
            {
                return;
            }

            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyVisualComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(enemyId) || !_entityManager.HasComponent<EnemyStateComponent>(enemyId))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent position = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(enemyId);
                if ((int)position.Z != CurrentZLevel)
                {
                    continue;
                }

                int x = Mathf.RoundToInt(position.X);
                int y = Mathf.RoundToInt(position.Y);
                int z = Mathf.RoundToInt(position.Z);
                if (EnableFogVisibilityGate && (_visionDataSystem == null || !_visionDataSystem.IsCurrentlyVisible(x, y, z)))
                {
                    continue;
                }

                ref EnemyVisualComponent visual = ref _entityManager.GetComponent<EnemyVisualComponent>(enemyId);
                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                Vector2 screenPos = new Vector2(position.X * TileSize, position.Y * TileSize);
                Vector2 tileBottomCenter = screenPos + new Vector2(16f, 32f);
                bool hasDrawnAnything = false;

                Action<Godot.Collections.Array<Texture2D>, int, Vector2> drawPart = (textures, id, offset) =>
                {
                    if (!TryGetAnyTexture(textures, id, out Texture2D texture))
                    {
                        return;
                    }

                    Vector2 size = texture.GetSize() * CharacterScale;
                    Vector2 drawOrigin = tileBottomCenter - new Vector2(size.X / 2f, size.Y);
                    DrawTextureRect(texture, new Rect2(drawOrigin + GlobalRenderOffset + offset, size), false);
                    hasDrawnAnything = true;
                };

                drawPart(TorsoTextures, visual.TorsoId + visual.VariantId, TorsoOffset);
                drawPart(HeadTextures, visual.HeadId + visual.VariantId, HeadOffset);
                drawPart(ClothesTextures, visual.ClothesId + visual.VariantId, ClothesOffset);
                drawPart(HairTextures, visual.HairId + visual.VariantId, HairOffset);

                if (!hasDrawnAnything)
                {
                    Color tint = new Color(visual.SkinColorHex);
                    if (tint.A == 0)
                    {
                        tint = state.CurrentState == EnemyStateType.AttackTarget || state.CurrentState == EnemyStateType.SelfDestructWindup
                            ? Colors.OrangeRed
                            : Colors.IndianRed;
                    }

                    Vector2 fallbackSize = new Vector2(24f, 24f) * CharacterScale;
                    Vector2 fallbackOrigin = tileBottomCenter - new Vector2(fallbackSize.X / 2f, fallbackSize.Y);
                    DrawRect(new Rect2(fallbackOrigin + GlobalRenderOffset, fallbackSize), tint);
                }
            }
        }

        private int BuildRenderSignature()
        {
            HashCode hash = new HashCode();
            hash.Add(CurrentZLevel);
            hash.Add(EnableFogVisibilityGate);

            if (_entityManager == null || _entityManager.GetComponentCount<EnemyVisualComponent>() == 0)
            {
                return hash.ToHashCode();
            }

            ReadOnlySpan<uint> entityIds = _entityManager.GetDenseEntityIds<EnemyVisualComponent>();
            for (int i = 0; i < entityIds.Length; i++)
            {
                uint enemyId = entityIds[i];
                if (!_entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(enemyId) || !_entityManager.HasComponent<EnemyStateComponent>(enemyId))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent position = ref _entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(enemyId);
                if ((int)position.Z != CurrentZLevel)
                {
                    continue;
                }

                int x = Mathf.RoundToInt(position.X);
                int y = Mathf.RoundToInt(position.Y);
                int z = Mathf.RoundToInt(position.Z);
                bool isVisible = !EnableFogVisibilityGate || (_visionDataSystem != null && _visionDataSystem.IsCurrentlyVisible(x, y, z));

                ref EnemyVisualComponent visual = ref _entityManager.GetComponent<EnemyVisualComponent>(enemyId);
                ref EnemyStateComponent state = ref _entityManager.GetComponent<EnemyStateComponent>(enemyId);
                hash.Add(enemyId);
                hash.Add(x);
                hash.Add(y);
                hash.Add(z);
                hash.Add(isVisible);
                hash.Add((int)state.CurrentState);
                hash.Add(visual.HeadId);
                hash.Add(visual.TorsoId);
                hash.Add(visual.ClothesId);
                hash.Add(visual.HairId);
                hash.Add(visual.VariantId);
                hash.Add(visual.SkinColorHex);
            }

            return hash.ToHashCode();
        }

        private bool TryGetAnyTexture(Godot.Collections.Array<Texture2D> textures, int id, out Texture2D texture)
        {
            texture = null;
            if (textures == null || textures.Count == 0)
            {
                return false;
            }

            int index = Mathf.Abs(id) % textures.Count;
            texture = textures[index];
            return texture != null;
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
