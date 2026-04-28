using System;
using System.Collections.Generic;
using Godot;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Items;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Heat
{
    public sealed class HeatFieldSystem : IHeatFieldQuery
    {
        private readonly IMapManager _mapManager;
        private readonly ItemSystemNode _itemSystem;
        private readonly IEventBus _eventBus;

        private float[] _heat = Array.Empty<float>();
        private float[] _exhaust = Array.Empty<float>();
        private HeatFieldSnapshot _snapshot = new(Array.Empty<float>(), Array.Empty<float>(), 0, 0, 0, 0f);

        public HeatFieldSystem(IMapManager mapManager, ItemSystemNode itemSystem, IEventBus eventBus)
        {
            _mapManager = mapManager;
            _itemSystem = itemSystem;
            _eventBus = eventBus;
        }

        public float BaseIndustrialSignature { get; private set; }
        public HeatFieldSnapshot Snapshot => _snapshot;

        public void Initialize()
        {
            ResizeBuffersIfNeeded();
            _eventBus.Subscribe<PlacedItemAddedEvent>(OnPlacedItemChanged);
            _eventBus.Subscribe<PlacedItemRemovedEvent>(OnPlacedItemChanged);
            _eventBus.Subscribe<ItemBrokenEvent>(OnItemBroken);
            RebuildFromPlacedItems();
        }

        public void Shutdown()
        {
            _eventBus.Unsubscribe<PlacedItemAddedEvent>(OnPlacedItemChanged);
            _eventBus.Unsubscribe<PlacedItemRemovedEvent>(OnPlacedItemChanged);
            _eventBus.Unsubscribe<ItemBrokenEvent>(OnItemBroken);
        }

        public float GetHeat(GridPosition position)
        {
            return TryRead(_heat, position, out float value) ? value : 0f;
        }

        public float GetExhaust(GridPosition position)
        {
            return TryRead(_exhaust, position, out float value) ? value : 0f;
        }

        public float GetAttractionScore(GridPosition position, EnemyScentProfile profile)
        {
            return GetHeat(position) * profile.HeatWeight + GetExhaust(position) * profile.ExhaustWeight;
        }

        public bool TryGetGradient(GridPosition position, out Vector3I direction)
        {
            direction = Vector3I.Zero;
            if (!_mapManager.IsWithinBounds(position))
            {
                return false;
            }

            EnemyScentProfile profile = new(1f, 0.75f);
            float bestScore = GetAttractionScore(position, profile);
            Vector3I bestDirection = Vector3I.Zero;

            ReadOnlySpan<Vector3I> directions = stackalloc Vector3I[]
            {
                new Vector3I(1, 0, 0),
                new Vector3I(-1, 0, 0),
                new Vector3I(0, 1, 0),
                new Vector3I(0, -1, 0),
                new Vector3I(0, 0, 1),
                new Vector3I(0, 0, -1)
            };

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3I candidateDirection = directions[i];
                GridPosition candidate = new(position.X + candidateDirection.X, position.Y + candidateDirection.Y, position.Z + candidateDirection.Z);
                if (!_mapManager.IsWithinBounds(candidate))
                {
                    continue;
                }

                float candidateScore = GetAttractionScore(candidate, profile);
                if (candidateScore > bestScore + 0.001f)
                {
                    bestScore = candidateScore;
                    bestDirection = candidateDirection;
                }
            }

            direction = bestDirection;
            return direction != Vector3I.Zero;
        }

        public void RebuildFromPlacedItems()
        {
            ResizeBuffersIfNeeded();
            Array.Clear(_heat, 0, _heat.Length);
            Array.Clear(_exhaust, 0, _exhaust.Length);

            float previousSignature = BaseIndustrialSignature;
            float signature = 0f;

            foreach (KeyValuePair<GridPosition, ItemSystemNode.PlacedItemRecord> placedItem in _itemSystem.EnumeratePlacedItems())
            {
                if (!ItemConfigManager.TryGetItem(placedItem.Value.ItemId, out ItemDefinition definition))
                {
                    continue;
                }

                if (!definition.EmitsIndustrialSignature())
                {
                    continue;
                }

                if (placedItem.Value.IsBroken && !definition.ResolveEmitsWhenBroken())
                {
                    continue;
                }

                EmitFromItem(placedItem.Key, definition);
                signature += definition.ResolveBaseHeatOutput() + (definition.ResolveBaseExhaustOutput() * 0.75f);
            }

            BaseIndustrialSignature = signature;
            _snapshot = new HeatFieldSnapshot((float[])_heat.Clone(), (float[])_exhaust.Clone(), _mapManager.Width, _mapManager.Height, _mapManager.Depth, BaseIndustrialSignature);

            GridPosition max = new(Math.Max(0, _mapManager.Width - 1), Math.Max(0, _mapManager.Height - 1), Math.Max(0, _mapManager.Depth - 1));
            var changed = new HeatFieldChangedEvent
            {
                FullRebuild = true,
                Min = new GridPosition(0, 0, 0),
                Max = max,
                BaseIndustrialSignature = BaseIndustrialSignature
            };
            _eventBus.Publish(ref changed);

            if (Math.Abs(previousSignature - BaseIndustrialSignature) > 0.001f)
            {
                var signatureChanged = new IndustrialSignatureChangedEvent
                {
                    PreviousSignature = previousSignature,
                    CurrentSignature = BaseIndustrialSignature
                };
                _eventBus.Publish(ref signatureChanged);
            }
        }

        private void EmitFromItem(GridPosition anchor, ItemDefinition definition)
        {
            int radiusXY = definition.ResolveHeatEmissionRadiusXY();
            int riseZ = definition.ResolveHeatEmissionRiseZ();
            int downZ = definition.ResolveHeatEmissionDownZ();
            float heatOutput = definition.ResolveBaseHeatOutput();
            float exhaustOutput = definition.ResolveBaseExhaustOutput();
            float falloff = definition.ResolveHeatEmissionFalloff();
            float upwardBias = definition.ResolveUpwardBias();
            float downwardMultiplier = definition.ResolveDownwardMultiplier();

            List<GridPosition> sourceCells = _itemSystem.GetOccupiedCellsForItem(definition.id, anchor);
            if (sourceCells.Count == 0)
            {
                sourceCells.Add(anchor);
            }

            for (int sourceIndex = 0; sourceIndex < sourceCells.Count; sourceIndex++)
            {
                GridPosition source = sourceCells[sourceIndex];
                int minZ = Math.Max(0, source.Z - downZ);
                int maxZ = Math.Min(_mapManager.Depth - 1, source.Z + riseZ);

                for (int z = minZ; z <= maxZ; z++)
                {
                    int verticalDistance = Math.Abs(z - source.Z);
                    int verticalPenalty = z >= source.Z ? verticalDistance : verticalDistance + 1;
                    int minX = Math.Max(0, source.X - radiusXY);
                    int maxX = Math.Min(_mapManager.Width - 1, source.X + radiusXY);
                    int minY = Math.Max(0, source.Y - radiusXY);
                    int maxY = Math.Min(_mapManager.Height - 1, source.Y + radiusXY);

                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int y = minY; y <= maxY; y++)
                        {
                            int dx = Math.Abs(x - source.X);
                            int dy = Math.Abs(y - source.Y);
                            int distanceXY = dx + dy;
                            if (distanceXY > radiusXY)
                            {
                                continue;
                            }

                            GridPosition candidate = new(x, y, z);
                            float attenuation = 1f / (1f + ((distanceXY + verticalPenalty) * falloff));
                            if (z > source.Z)
                            {
                                attenuation *= upwardBias;
                            }
                            else if (z < source.Z)
                            {
                                attenuation *= downwardMultiplier;
                            }

                            int flatIndex = _mapManager.GetFlatIndex(candidate);
                            _heat[flatIndex] += heatOutput * attenuation;
                            _exhaust[flatIndex] += exhaustOutput * attenuation;
                        }
                    }
                }
            }
        }

        private void ResizeBuffersIfNeeded()
        {
            int requiredLength = Math.Max(0, _mapManager.Width * _mapManager.Height * _mapManager.Depth);
            if (_heat.Length == requiredLength && _exhaust.Length == requiredLength)
            {
                return;
            }

            _heat = requiredLength > 0 ? new float[requiredLength] : Array.Empty<float>();
            _exhaust = requiredLength > 0 ? new float[requiredLength] : Array.Empty<float>();
        }

        private bool TryRead(float[] source, GridPosition position, out float value)
        {
            value = 0f;
            if (source == null || source.Length == 0 || !_mapManager.IsWithinBounds(position))
            {
                return false;
            }

            value = source[_mapManager.GetFlatIndex(position)];
            return true;
        }

        private void OnPlacedItemChanged(ref PlacedItemAddedEvent evt)
        {
            RebuildFromPlacedItems();
        }

        private void OnPlacedItemChanged(ref PlacedItemRemovedEvent evt)
        {
            RebuildFromPlacedItems();
        }

        private void OnItemBroken(ref ItemBrokenEvent evt)
        {
            RebuildFromPlacedItems();
        }
    }
}
