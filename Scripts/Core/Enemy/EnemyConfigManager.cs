using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace MetaFort.Core.Enemy
{
    public static class EnemyConfigManager
    {
        public static Dictionary<string, EnemyArchetypeDefinition> EnemyArchetypes { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, EnemyStateProfileDefinition> EnemyStateProfiles { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, EnemyScentProfileDefinition> EnemyScentProfiles { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, EnemyActionProfileDefinition> EnemyActionProfiles { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public static string LastLoadError { get; private set; } = string.Empty;
        public static bool IsLoadedSuccessfully { get; private set; }

        public static bool LoadEnemyConfig()
        {
            LastLoadError = string.Empty;
            IsLoadedSuccessfully = false;

            string path = "res://assets/config/enemy_config.json";
            string globalPath = ProjectSettings.GlobalizePath(path);

            if (!File.Exists(globalPath))
            {
                LastLoadError = $"Missing enemy config file: {globalPath}";
                GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
                Clear();
                return false;
            }

            try
            {
                string json = File.ReadAllText(globalPath);
                EnemyConfigRoot root = JsonSerializer.Deserialize<EnemyConfigRoot>(json);

                Clear();
                if (root == null)
                {
                    LastLoadError = "Enemy config root is missing.";
                    GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
                    return false;
                }

                foreach (EnemyStateProfileDefinition profile in root.enemyStateProfiles ?? new List<EnemyStateProfileDefinition>())
                {
                    if (!TryStore(profile, EnemyStateProfiles, "enemy state profile", out string error))
                    {
                        LastLoadError = error;
                        GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
                        Clear();
                        return false;
                    }
                }

                foreach (EnemyScentProfileDefinition profile in root.enemyScentProfiles ?? new List<EnemyScentProfileDefinition>())
                {
                    if (!TryStore(profile, EnemyScentProfiles, "enemy scent profile", out string error))
                    {
                        LastLoadError = error;
                        GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
                        Clear();
                        return false;
                    }
                }

                foreach (EnemyActionProfileDefinition profile in root.enemyActionProfiles ?? new List<EnemyActionProfileDefinition>())
                {
                    if (!TryStore(profile, EnemyActionProfiles, "enemy action profile", out string error))
                    {
                        LastLoadError = error;
                        GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
                        Clear();
                        return false;
                    }
                }

                foreach (EnemyArchetypeDefinition archetype in root.enemyArchetypes ?? new List<EnemyArchetypeDefinition>())
                {
                    if (!TryValidateArchetype(archetype, out string error))
                    {
                        LastLoadError = error;
                        GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
                        Clear();
                        return false;
                    }

                    EnemyArchetypes[archetype.id] = archetype;
                }

                IsLoadedSuccessfully = true;
                GD.Print($"[EnemyConfigManager] Loaded {EnemyArchetypes.Count} enemy archetypes.");
                return true;
            }
            catch (JsonException ex)
            {
                LastLoadError = $"Invalid enemy config JSON: {ex.Message}";
                GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
            }
            catch (Exception ex)
            {
                LastLoadError = $"Failed to load enemy config: {ex.Message}";
                GD.PrintErr($"[EnemyConfigManager] {LastLoadError}");
            }

            Clear();
            return false;
        }

        public static bool TryGetEnemyArchetype(string id, out EnemyArchetypeDefinition archetype) => EnemyArchetypes.TryGetValue(id, out archetype);
        public static bool TryGetStateProfile(string id, out EnemyStateProfileDefinition profile) => EnemyStateProfiles.TryGetValue(id, out profile);
        public static bool TryGetScentProfile(string id, out EnemyScentProfileDefinition profile) => EnemyScentProfiles.TryGetValue(id, out profile);
        public static bool TryGetActionProfile(string id, out EnemyActionProfileDefinition profile) => EnemyActionProfiles.TryGetValue(id, out profile);

        private static void Clear()
        {
            EnemyArchetypes.Clear();
            EnemyStateProfiles.Clear();
            EnemyScentProfiles.Clear();
            EnemyActionProfiles.Clear();
            IsLoadedSuccessfully = false;
        }

        private static bool TryStore<T>(T profile, Dictionary<string, T> store, string label, out string error) where T : class
        {
            error = string.Empty;
            if (profile == null)
            {
                error = $"Encountered a null {label}.";
                return false;
            }

            string id = (string)typeof(T).GetProperty("id")?.GetValue(profile);
            if (string.IsNullOrWhiteSpace(id))
            {
                error = $"A {label} is missing 'id'.";
                return false;
            }

            if (store.ContainsKey(id))
            {
                error = $"Duplicate {label} id '{id}'.";
                return false;
            }

            store[id] = profile;
            return true;
        }

        private static bool TryValidateArchetype(EnemyArchetypeDefinition archetype, out string error)
        {
            error = string.Empty;
            if (archetype == null)
            {
                error = "Encountered a null enemy archetype definition.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(archetype.id))
            {
                error = "Enemy archetype is missing 'id'.";
                return false;
            }

            if (EnemyArchetypes.ContainsKey(archetype.id))
            {
                error = $"Duplicate enemy archetype id '{archetype.id}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(archetype.displayName))
            {
                error = $"Enemy archetype '{archetype.id}' is missing 'displayName'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(archetype.stateProfileId) && !EnemyStateProfiles.ContainsKey(archetype.stateProfileId))
            {
                error = $"Enemy archetype '{archetype.id}' references missing stateProfileId '{archetype.stateProfileId}'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(archetype.scentProfileId) && !EnemyScentProfiles.ContainsKey(archetype.scentProfileId))
            {
                error = $"Enemy archetype '{archetype.id}' references missing scentProfileId '{archetype.scentProfileId}'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(archetype.actionProfileId) && !EnemyActionProfiles.ContainsKey(archetype.actionProfileId))
            {
                error = $"Enemy archetype '{archetype.id}' references missing actionProfileId '{archetype.actionProfileId}'.";
                return false;
            }

            archetype.preferredTargets ??= new List<string>();
            return true;
        }
    }
}
