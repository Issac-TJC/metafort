using System;
using System.Collections.Generic;
using Godot;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Heat
{
    public sealed class ThermalProfileDefinition
    {
        public string id { get; set; } = string.Empty;
        public string displayName { get; set; } = string.Empty;
        public float baseHeatOutput { get; set; }
        public float baseExhaustOutput { get; set; }
        public int heatEmissionRadiusXY { get; set; } = 1;
        public int heatEmissionRiseZ { get; set; } = 1;
        public int heatEmissionDownZ { get; set; } = 0;
        public float heatEmissionFalloff { get; set; } = 1.0f;
        public float upwardBias { get; set; } = 1.25f;
        public float downwardMultiplier { get; set; } = 0.35f;
        public bool emitsWhenBroken { get; set; }
        public List<string> heatTags { get; set; } = new();
    }

    public sealed class ThermalConfigRoot
    {
        public List<ThermalProfileDefinition> thermalProfiles { get; set; } = new();
    }

    public readonly struct HeatFieldCell
    {
        public HeatFieldCell(float heat, float exhaust)
        {
            Heat = heat;
            Exhaust = exhaust;
        }

        public float Heat { get; }
        public float Exhaust { get; }
    }

    public readonly struct HeatFieldBounds
    {
        public HeatFieldBounds(GridPosition min, GridPosition max)
        {
            Min = min;
            Max = max;
        }

        public GridPosition Min { get; }
        public GridPosition Max { get; }
    }

    public sealed class HeatFieldSnapshot
    {
        public HeatFieldSnapshot(float[] heat, float[] exhaust, int width, int height, int depth, float industrialSignature)
        {
            Heat = heat ?? Array.Empty<float>();
            Exhaust = exhaust ?? Array.Empty<float>();
            Width = width;
            Height = height;
            Depth = depth;
            IndustrialSignature = industrialSignature;
        }

        public float[] Heat { get; }
        public float[] Exhaust { get; }
        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }
        public float IndustrialSignature { get; }

        public bool IsEmpty => Heat.Length == 0 || Exhaust.Length == 0 || Width <= 0 || Height <= 0 || Depth <= 0;
    }

    public readonly struct EnemyScentProfile
    {
        public EnemyScentProfile(float heatWeight, float exhaustWeight)
        {
            HeatWeight = heatWeight;
            ExhaustWeight = exhaustWeight;
        }

        public float HeatWeight { get; }
        public float ExhaustWeight { get; }
    }

    public interface IHeatFieldQuery
    {
        float BaseIndustrialSignature { get; }
        HeatFieldSnapshot Snapshot { get; }
        float GetHeat(GridPosition position);
        float GetExhaust(GridPosition position);
        float GetAttractionScore(GridPosition position, EnemyScentProfile profile);
        bool TryGetGradient(GridPosition position, out Vector3I direction);
    }
}
