using System;
using System.Collections.Generic;
using System.Linq;
using MetaFort.Core.Heat;

namespace MetaFort.Core.Items
{
    public enum ItemMaterialType
    {
        Wood,
        Metal,
        Cloth,
        Stone,
        Organic,
        Electronic,
        Composite
    }

    public enum ItemDecayMode
    {
        Linear,
        WetFirstThenRot,
        HeatFatigue,
        RustLike,
        ElectricalFragile
    }

    [System.Flags]
    public enum PlacementRuleFlags
    {
        None = 0,
        RequireWalkableGround = 1 << 0,
        SupportCrossZ = 1 << 1,
        NeedsAdjacentSolid = 1 << 2,
        AllowOnAir = 1 << 3
    }

    public class ItemMaterialRequirement
    {
        public string itemId { get; set; }
        public int count { get; set; }
    }

    public class OccupiedCellOffset
    {
        public int x { get; set; }
        public int y { get; set; }
        public int z { get; set; }
    }

    public class ItemDefinition
    {
        public sealed class EnvironmentalDefaults
        {
            public float BaseDecayRate { get; init; }
            public float WeatherSensitivity { get; init; }
            public float MoistureSensitivity { get; init; }
            public float TemperatureSensitivity { get; init; }
            public float ThermalShockSensitivity { get; init; }
            public float LightningSensitivity { get; init; }
        }

        public string id { get; set; }
        public string displayName { get; set; }
        public int maxDurability { get; set; }
        public int hardness { get; set; }
        public bool blocksVision { get; set; }
        public bool blocksMovement { get; set; }
        public int footprintX { get; set; } = 1;
        public int footprintY { get; set; } = 1;
        public int placementFlags { get; set; }
        public List<OccupiedCellOffset> occupiedOffsets { get; set; } = new List<OccupiedCellOffset>();
        public List<ItemMaterialRequirement> requiredMaterials { get; set; } = new List<ItemMaterialRequirement>();
        public string texturePath { get; set; }
        public string interactionScript { get; set; }
        public bool isBuildable { get; set; }
        public string buildCategory { get; set; } = "Misc";
        public string plannerLabel { get; set; }
        public bool showInStockpile { get; set; }
        public int stockpileOrder { get; set; }
        public string stockpileLabel { get; set; }
        public string materialType { get; set; } = ItemMaterialType.Composite.ToString();
        public string decayMode { get; set; } = ItemDecayMode.Linear.ToString();
        public float? baseDecayRate { get; set; }
        public float? weatherSensitivity { get; set; }
        public float? moistureSensitivity { get; set; }
        public float? temperatureSensitivity { get; set; }
        public float? thermalShockSensitivity { get; set; }
        public float? lightningSensitivity { get; set; }
        public float? maxCondition { get; set; }
        public float? failureThreshold { get; set; }
        public string thermalProfileId { get; set; } = string.Empty;
        public float? baseHeatOutput { get; set; }
        public float? baseExhaustOutput { get; set; }
        public int? heatEmissionRadiusXY { get; set; }
        public int? heatEmissionRiseZ { get; set; }
        public int? heatEmissionDownZ { get; set; }
        public float? heatEmissionFalloff { get; set; }
        public bool? emitsWhenBroken { get; set; }
        public List<string> heatTags { get; set; } = new List<string>();

        public PlacementRuleFlags GetPlacementFlags() => (PlacementRuleFlags)placementFlags;

        public ItemMaterialType GetMaterialType()
        {
            return Enum.TryParse(materialType, true, out ItemMaterialType parsed)
                ? parsed
                : ItemMaterialType.Composite;
        }

        public ItemDecayMode GetDecayMode()
        {
            return Enum.TryParse(decayMode, true, out ItemDecayMode parsed)
                ? parsed
                : ItemDecayMode.Linear;
        }

        public EnvironmentalDefaults GetEnvironmentalDefaults()
        {
            return GetMaterialType() switch
            {
                ItemMaterialType.Wood => new EnvironmentalDefaults
                {
                    BaseDecayRate = 0.08f,
                    WeatherSensitivity = 1.15f,
                    MoistureSensitivity = 1.65f,
                    TemperatureSensitivity = 0.65f,
                    ThermalShockSensitivity = 1.05f,
                    LightningSensitivity = 0.75f
                },
                ItemMaterialType.Metal => new EnvironmentalDefaults
                {
                    BaseDecayRate = 0.03f,
                    WeatherSensitivity = 0.85f,
                    MoistureSensitivity = 0.75f,
                    TemperatureSensitivity = 0.85f,
                    ThermalShockSensitivity = 0.65f,
                    LightningSensitivity = 1.45f
                },
                ItemMaterialType.Cloth => new EnvironmentalDefaults
                {
                    BaseDecayRate = 0.06f,
                    WeatherSensitivity = 1.20f,
                    MoistureSensitivity = 1.75f,
                    TemperatureSensitivity = 0.60f,
                    ThermalShockSensitivity = 0.70f,
                    LightningSensitivity = 0.90f
                },
                ItemMaterialType.Stone => new EnvironmentalDefaults
                {
                    BaseDecayRate = 0.01f,
                    WeatherSensitivity = 0.45f,
                    MoistureSensitivity = 0.20f,
                    TemperatureSensitivity = 0.30f,
                    ThermalShockSensitivity = 0.50f,
                    LightningSensitivity = 0.40f
                },
                ItemMaterialType.Organic => new EnvironmentalDefaults
                {
                    BaseDecayRate = 0.12f,
                    WeatherSensitivity = 1.30f,
                    MoistureSensitivity = 1.85f,
                    TemperatureSensitivity = 0.80f,
                    ThermalShockSensitivity = 0.90f,
                    LightningSensitivity = 0.85f
                },
                ItemMaterialType.Electronic => new EnvironmentalDefaults
                {
                    BaseDecayRate = 0.05f,
                    WeatherSensitivity = 1.45f,
                    MoistureSensitivity = 1.15f,
                    TemperatureSensitivity = 1.45f,
                    ThermalShockSensitivity = 1.50f,
                    LightningSensitivity = 2.10f
                },
                _ => new EnvironmentalDefaults
                {
                    BaseDecayRate = 0.04f,
                    WeatherSensitivity = 1.00f,
                    MoistureSensitivity = 1.00f,
                    TemperatureSensitivity = 1.00f,
                    ThermalShockSensitivity = 1.00f,
                    LightningSensitivity = 1.00f
                }
            };
        }

        public float ResolveBaseDecayRate() => baseDecayRate ?? GetEnvironmentalDefaults().BaseDecayRate;
        public float ResolveWeatherSensitivity() => weatherSensitivity ?? GetEnvironmentalDefaults().WeatherSensitivity;
        public float ResolveMoistureSensitivity() => moistureSensitivity ?? GetEnvironmentalDefaults().MoistureSensitivity;
        public float ResolveTemperatureSensitivity() => temperatureSensitivity ?? GetEnvironmentalDefaults().TemperatureSensitivity;
        public float ResolveThermalShockSensitivity() => thermalShockSensitivity ?? GetEnvironmentalDefaults().ThermalShockSensitivity;
        public float ResolveLightningSensitivity() => lightningSensitivity ?? GetEnvironmentalDefaults().LightningSensitivity;
        public float ResolveMaxCondition() => maxCondition is > 0f ? maxCondition.Value : Math.Max(1, maxDurability);
        public float ResolveFailureThreshold() => failureThreshold is >= 0f
            ? failureThreshold.Value
            : ResolveMaxCondition() * 0.20f;
        public string ResolvePlannerLabel() => string.IsNullOrWhiteSpace(plannerLabel) ? displayName : plannerLabel;
        public string ResolveBuildCategory() => string.IsNullOrWhiteSpace(buildCategory) ? "Misc" : buildCategory;
        public string ResolveStockpileLabel() => string.IsNullOrWhiteSpace(stockpileLabel) ? displayName : stockpileLabel;
        public ThermalProfileDefinition ResolveThermalProfile()
        {
            return ThermalConfigManager.TryGetProfile(thermalProfileId, out ThermalProfileDefinition profile)
                ? profile
                : null;
        }

        public float ResolveBaseHeatOutput() => baseHeatOutput ?? ResolveThermalProfile()?.baseHeatOutput ?? 0f;
        public float ResolveBaseExhaustOutput() => baseExhaustOutput ?? ResolveThermalProfile()?.baseExhaustOutput ?? 0f;
        public int ResolveHeatEmissionRadiusXY() => Math.Max(0, heatEmissionRadiusXY ?? ResolveThermalProfile()?.heatEmissionRadiusXY ?? 0);
        public int ResolveHeatEmissionRiseZ() => Math.Max(0, heatEmissionRiseZ ?? ResolveThermalProfile()?.heatEmissionRiseZ ?? 0);
        public int ResolveHeatEmissionDownZ() => Math.Max(0, heatEmissionDownZ ?? ResolveThermalProfile()?.heatEmissionDownZ ?? 0);
        public float ResolveHeatEmissionFalloff() => Math.Max(0.01f, heatEmissionFalloff ?? ResolveThermalProfile()?.heatEmissionFalloff ?? 1f);
        public float ResolveUpwardBias() => Math.Max(0.01f, ResolveThermalProfile()?.upwardBias ?? 1.25f);
        public float ResolveDownwardMultiplier() => Math.Clamp(ResolveThermalProfile()?.downwardMultiplier ?? 0.35f, 0f, 4f);
        public bool ResolveEmitsWhenBroken() => emitsWhenBroken ?? ResolveThermalProfile()?.emitsWhenBroken ?? false;
        public IReadOnlyList<string> ResolveHeatTags()
        {
            List<string> resolved = heatTags != null && heatTags.Count > 0
                ? heatTags
                : ResolveThermalProfile()?.heatTags;
            return resolved?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();
        }
        public bool EmitsIndustrialSignature() => ResolveBaseHeatOutput() > 0f || ResolveBaseExhaustOutput() > 0f;
    }

    public class ItemConfigRoot
    {
        public List<ItemDefinition> items { get; set; } = new List<ItemDefinition>();
    }
}
