using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace MetaFort.Core.Heat
{
    public static class ThermalConfigManager
    {
        public static Dictionary<string, ThermalProfileDefinition> ThermalProfiles { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public static string LastLoadError { get; private set; } = string.Empty;
        public static bool IsLoadedSuccessfully { get; private set; }

        public static bool LoadThermalConfig()
        {
            LastLoadError = string.Empty;
            IsLoadedSuccessfully = false;

            string path = "res://assets/config/thermal_profiles.json";
            string globalPath = ProjectSettings.GlobalizePath(path);

            if (!File.Exists(globalPath))
            {
                LastLoadError = $"Missing thermal profile config file: {globalPath}";
                GD.PrintErr($"[ThermalConfigManager] {LastLoadError}");
                ThermalProfiles.Clear();
                return false;
            }

            try
            {
                string json = File.ReadAllText(globalPath);
                ThermalConfigRoot root = JsonSerializer.Deserialize<ThermalConfigRoot>(json);

                ThermalProfiles.Clear();
                if (root?.thermalProfiles == null)
                {
                    LastLoadError = "Thermal config is missing the 'thermalProfiles' array.";
                    GD.PrintErr($"[ThermalConfigManager] {LastLoadError}");
                    return false;
                }

                foreach (ThermalProfileDefinition profile in root.thermalProfiles)
                {
                    if (!TryValidateProfile(profile, out string error))
                    {
                        LastLoadError = error;
                        GD.PrintErr($"[ThermalConfigManager] {LastLoadError}");
                        ThermalProfiles.Clear();
                        return false;
                    }

                    ThermalProfiles[profile.id] = profile;
                }

                IsLoadedSuccessfully = true;
                GD.Print($"[ThermalConfigManager] Loaded {ThermalProfiles.Count} thermal profiles.");
                return true;
            }
            catch (JsonException ex)
            {
                LastLoadError = $"Invalid thermal config JSON: {ex.Message}";
                GD.PrintErr($"[ThermalConfigManager] {LastLoadError}");
            }
            catch (Exception ex)
            {
                LastLoadError = $"Failed to load thermal config: {ex.Message}";
                GD.PrintErr($"[ThermalConfigManager] {LastLoadError}");
            }

            ThermalProfiles.Clear();
            return false;
        }

        public static bool TryGetProfile(string profileId, out ThermalProfileDefinition profile)
        {
            profile = null;
            return !string.IsNullOrWhiteSpace(profileId) && ThermalProfiles.TryGetValue(profileId, out profile);
        }

        private static bool TryValidateProfile(ThermalProfileDefinition profile, out string error)
        {
            error = string.Empty;
            if (profile == null)
            {
                error = "Encountered a null thermal profile definition.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(profile.id))
            {
                error = "Thermal profile definition is missing 'id'.";
                return false;
            }

            if (ThermalProfiles.ContainsKey(profile.id))
            {
                error = $"Duplicate thermal profile id '{profile.id}'.";
                return false;
            }

            if (profile.heatEmissionRadiusXY < 0)
            {
                error = $"Thermal profile '{profile.id}' has invalid heatEmissionRadiusXY.";
                return false;
            }

            if (profile.heatEmissionRiseZ < 0 || profile.heatEmissionDownZ < 0)
            {
                error = $"Thermal profile '{profile.id}' has invalid vertical emission ranges.";
                return false;
            }

            if (profile.heatEmissionFalloff <= 0f)
            {
                error = $"Thermal profile '{profile.id}' must have a positive heatEmissionFalloff.";
                return false;
            }

            profile.heatTags ??= new List<string>();
            return true;
        }
    }
}
