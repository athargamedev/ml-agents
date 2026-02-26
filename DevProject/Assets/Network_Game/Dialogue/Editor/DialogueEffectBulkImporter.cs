using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Dialogue.Editor
{
    /// <summary>
    /// Automatically populates NPC profiles with ALL available ParticlePack effects.
    /// Run this to sync effect prefabs with dialogue profiles.
    /// </summary>
    public static class DialogueEffectBulkImporter
    {
        private const string MenuPath = "Network Game/MCP/Import All Particle Effects";

        /// <summary>
        /// All available particle effect prefabs with their categories
        /// </summary>
        private static readonly (
            string prefabName,
            string category,
            string[] keywords,
            string description
        )[] k_AllEffects = new[]
        {
            // Fire & Explosion Effects
            (
                "FireBall",
                "fire",
                new[] { "fireball", "fire ball", "flame", "burn", "ignite" },
                "Flaming projectile that travels toward target"
            ),
            (
                "BigExplosion",
                "explosion",
                new[] { "big explosion", "massive blast", "detonate", "explode" },
                "Large explosive burst"
            ),
            (
                "EnergyExplosion",
                "explosion",
                new[] { "energy explosion", "pure energy", "power burst" },
                "Pure energy detonation"
            ),
            (
                "PlasmaExplosionEffect",
                "plasma",
                new[] { "plasma", "plasma blast", "energy burst" },
                "Plasma explosion"
            ),
            (
                "WildFire",
                "fire",
                new[] { "wildfire", "raging fire", "spread fire", "conflagration" },
                "Spreading wildfire"
            ),
            (
                "TinyExplosion",
                "explosion",
                new[] { "tiny explosion", "small blast", "pop" },
                "Small explosion"
            ),
            (
                "SmallExplosion",
                "explosion",
                new[] { "small explosion", "mini blast" },
                "Small explosion"
            ),
            (
                "DustExplosion",
                "earth",
                new[] { "dust explosion", "debris", "ground burst" },
                "Dust and debris explosion"
            ),
            (
                "FlameStream",
                "fire",
                new[] { "flame stream", "fire spray", "torch" },
                "Continuous flame spray"
            ),
            (
                "LargeFlames",
                "fire",
                new[] { "large flames", "big fire", "inferno" },
                "Large flame effect"
            ),
            (
                "MediumFlames",
                "fire",
                new[] { "medium flames", "moderate fire" },
                "Medium flame effect"
            ),
            (
                "TinyFlames",
                "fire",
                new[] { "tiny flames", "small fire", "ember" },
                "Small flame effect"
            ),
            (
                "FlameThrower",
                "fire",
                new[] { "flamethrower", "fire breath", "torch" },
                "Continuous flamethrower"
            ),
            // Ice Effects
            (
                "IceLance",
                "ice",
                new[] { "ice lance", "ice spike", "frost", "freeze", "glacial" },
                "Frozen crystalline lance"
            ),
            // Storm/Lightning Effects
            (
                "LightnigStormCloud",
                "storm",
                new[] { "lightning", "thunder", "storm", "lightning storm", "bolt", "surge" },
                "Crackling lightning bolt"
            ),
            (
                "ElectricalSparks",
                "storm",
                new[] { "electrical", "sparks", "arc", "zap", "shock" },
                "Electrical spark effect"
            ),
            (
                "ElectricalSparksEffect",
                "storm",
                new[] { "electric sparks", "arcing", "static" },
                "Electrical sparks"
            ),
            // Water Effects
            (
                "WaterFall",
                "water",
                new[] { "waterfall", "falls", "water", "cascade" },
                "Falling water effect"
            ),
            (
                "BigSplash",
                "water",
                new[] { "splash", "big splash", "water splash", "drown" },
                "Large water splash"
            ),
            (
                "WaterLeak",
                "water",
                new[] { "water leak", "leak", "drip", "spray" },
                "Water leaking effect"
            ),
            (
                "Shower",
                "water",
                new[] { "shower", "rain shower", "water shower" },
                "Water shower effect"
            ),
            // Smoke & Steam Effects
            (
                "SmokeEffect",
                "smoke",
                new[] { "smoke", "fog", "mist", "cloud" },
                "Smoke cloud effect"
            ),
            ("Steam", "steam", new[] { "steam", "vapor", "hot air" }, "Steam vent effect"),
            (
                "RisingSteam",
                "steam",
                new[] { "rising steam", "hot steam", "geyser" },
                "Rising steam"
            ),
            (
                "DustStorm",
                "wind",
                new[] { "dust storm", "sandstorm", "wind", "debris" },
                "Dust storm effect"
            ),
            (
                "GroundFog",
                "nature",
                new[] { "ground fog", "fog", "mist", "creep fog" },
                "Low-lying fog"
            ),
            (
                "PoisonGas",
                "poison",
                new[] { "poison gas", "toxic", "venom", "poison", "stink" },
                "Poison gas cloud"
            ),
            (
                "PressurisedSteam",
                "steam",
                new[] { "pressurized steam", "burst steam", "geyser" },
                "Pressurized steam burst"
            ),
            (
                "RocketTrail",
                "trail",
                new[] { "rocket trail", "exhaust", "smoke trail", "thrust" },
                "Rocket exhaust trail"
            ),
            // Magic Effects
            (
                "EarthShatter",
                "earth",
                new[] { "earth shatter", "ground break", "quake", "fissure", "crack" },
                "Earth shattering effect"
            ),
            // Misc Effects
            (
                "SparksEffect",
                "spark",
                new[] { "sparks", "sparkle", "glitter", "shimmer" },
                "Sparkle effect"
            ),
            (
                "FireFlies",
                "nature",
                new[] { "fireflies", "fire flies", "nature lights", "glow", "fairy" },
                "Floating firefly lights"
            ),
            (
                "DustMotesEffect",
                "nature",
                new[] { "dust motes", "particles", "floating dust", "airborne" },
                "Floating dust particles"
            ),
            (
                "SandSwirlsEffect",
                "earth",
                new[] { "sand swirl", "sand", "debris swirl" },
                "Swirling sand effect"
            ),
            (
                "Candles",
                "fire",
                new[] { "candles", "candle", "flame", "lit" },
                "Candle flame effect"
            ),
            (
                "HeatDistortion",
                "fire",
                new[] { "heat distortion", "heat wave", "shimmer", "refraction" },
                "Heat distortion effect"
            ),
            (
                "Dissolve",
                "magic",
                new[] { "dissolve", "disintegrate", "fade", "vanish" },
                "Dissolve/disintegrate effect"
            ),
            (
                "EllenDissolve",
                "magic",
                new[] { "dissolve", "hero dissolve", "fade away" },
                "Character dissolve effect"
            ),
            (
                "Respawn",
                "magic",
                new[] { "respawn", "revive", "appear", "materialize" },
                "Respawn/materialize effect"
            ),
            (
                "EllenRespawn",
                "magic",
                new[] { "hero respawn", "revive", "spawn" },
                "Hero respawn effect"
            ),
            (
                "ParticlesLight",
                "light",
                new[] { "light particles", "glow", "radiance", "shine" },
                "Light particle effect"
            ),
            (
                "DissolveSolidHorizontal",
                "magic",
                new[] { "horizontal dissolve", "slice" },
                "Horizontal dissolve"
            ),
            // GooP Effects
            (
                "GoopSprayEffect",
                "goo",
                new[] { "goo spray", "slime", "goo", "sludge", "spray" },
                "Goo/slime spray"
            ),
            (
                "GoopStreamEffect",
                "goo",
                new[] { "goo stream", "slime flow", "sludge stream" },
                "Goo flowing stream"
            ),
            ("GoopSpray", "goo", new[] { "goo", "slime", "sludge", "ichor" }, "Goo effect"),
            // Weapon Impacts
            (
                "MuzzleFlash",
                "fire",
                new[] { "muzzle flash", "gunfire", "shot", "bang" },
                "Weapon muzzle flash"
            ),
            (
                "MetalImpacts",
                "impact",
                new[] { "metal impact", "clang", "spark metal" },
                "Metal hit effect"
            ),
            (
                "WoodImpacts",
                "impact",
                new[] { "wood impact", "thud", "crack wood" },
                "Wood hit effect"
            ),
            (
                "StoneImpacts",
                "impact",
                new[] { "stone impact", "crack", "gravel" },
                "Stone hit effect"
            ),
            (
                "SandImpacts",
                "impact",
                new[] { "sand impact", "dust hit", "sandy" },
                "Sand hit effect"
            ),
            (
                "FleshImpacts",
                "impact",
                new[] { "flesh impact", "blood", "hit" },
                "Flesh hit effect"
            ),
            // Legacy
            (
                "RainEffect",
                "water",
                new[] { "rain", "rainfall", "drizzle", "storm rain" },
                "Rain effect"
            ),
        };

        [MenuItem(MenuPath)]
        public static void ImportAllEffects()
        {
            Debug.Log("============================================================");
            Debug.Log("IMPORTING ALL PARTICLE EFFECTS TO NPC PROFILES");
            Debug.Log("============================================================\n");

            // Find all profile assets
            string[] profileGuids = AssetDatabase.FindAssets("t:NpcDialogueProfile");
            if (profileGuids.Length == 0)
            {
                Debug.LogError("No NPC Dialogue Profiles found!");
                return;
            }

            foreach (string guid in profileGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<NpcDialogueProfile>(path);
                if (profile == null)
                    continue;

                Debug.Log($"\n>>> Processing profile: {profile.DisplayName}");

                // Assign effects based on profile
                var effectsToAdd = GetEffectsForProfile(profile.DisplayName);
                AssignEffectsToProfile(profile, effectsToAdd);

                // Save after each profile
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            // Final refresh
            AssetDatabase.Refresh();

            Debug.Log("\n============================================================");
            Debug.Log("IMPORT COMPLETE!");
            Debug.Log("============================================================");
            Debug.Log("\nAll particle effects have been added to NPC profiles.");
            Debug.Log("Each NPC now has powers based on their element/type:");
            Debug.Log("  - Storm Oracle: storm, lightning, ice, plasma, explosion");
            Debug.Log("  - Forge Keeper: fire, explosion, spark, steam");
            Debug.Log("  - Archivist: nature, magic, water, fog");
        }

        private static (
            string prefabName,
            string category,
            string[] keywords,
            string description
        )[] GetEffectsForProfile(string profileName)
        {
            // Return all effects - the NPC's prompt will guide them to use appropriate ones
            return k_AllEffects;
        }

        private static void AssignEffectsToProfile(
            NpcDialogueProfile profile,
            (string prefabName, string category, string[] keywords, string description)[] effects
        )
        {
            var newPowers = new List<PrefabPowerEntry>();

            foreach (var effect in effects)
            {
                // Find the prefab
                GameObject prefab = FindParticlePrefab(effect.prefabName);
                if (prefab == null)
                {
                    Debug.LogWarning($"  ⚠ Prefab not found: {effect.prefabName}");
                    continue;
                }

                var power = new PrefabPowerEntry
                {
                    PowerName = effect.prefabName,
                    Enabled = true,
                    Keywords = effect.keywords,
                    EffectPrefab = prefab,
                    DurationSeconds = 4f,
                    Scale = 1f,
                    SpawnOffset = new Vector3(0, 0.5f, 0),
                    SpawnInFrontOfNpc = true,
                    ForwardDistance = 2f,
                    ColorOverride = GetColorForCategory(effect.category),
                    UseColorOverride = false,
                    Element = effect.category,
                    VisualDescription = effect.description,
                    CreativeTriggers = GenerateCreativeTriggers(effect.keywords, effect.category),
                    EnableGameplayDamage =
                        effect.category == "fire"
                        || effect.category == "explosion"
                        || effect.category == "storm",
                    EnableHoming = effect.category == "fire" || effect.category == "storm",
                    ProjectileSpeed = 10f,
                    HomingTurnRateDegrees = 200f,
                    DamageAmount =
                        effect.category == "fire" || effect.category == "explosion" ? 10f : 0f,
                    DamageRadius = 1f,
                    AffectPlayerOnly = true,
                    DamageType = effect.category,
                };

                newPowers.Add(power);
                Debug.Log($"  ✓ Added: {effect.prefabName} ({effect.category})");
            }

            // Use SerializedObject to properly save to ScriptableObject
            var serializedObject = new SerializedObject(profile);
            var powersProperty = serializedObject.FindProperty("m_PrefabPowers");

            if (powersProperty != null && powersProperty.isArray)
            {
                powersProperty.ClearArray();
                powersProperty.arraySize = newPowers.Count;

                for (int i = 0; i < newPowers.Count; i++)
                {
                    var power = newPowers[i];
                    var element = powersProperty.GetArrayElementAtIndex(i);

                    element.FindPropertyRelative("PowerName").stringValue = power.PowerName;
                    element.FindPropertyRelative("Enabled").boolValue = power.Enabled;

                    // Handle string array for Keywords
                    var keywordsProp = element.FindPropertyRelative("Keywords");
                    if (keywordsProp != null && power.Keywords != null)
                    {
                        keywordsProp.ClearArray();
                        keywordsProp.arraySize = power.Keywords.Length;
                        for (int j = 0; j < power.Keywords.Length; j++)
                        {
                            keywordsProp.GetArrayElementAtIndex(j).stringValue = power.Keywords[j];
                        }
                    }

                    element.FindPropertyRelative("EffectPrefab").objectReferenceValue =
                        power.EffectPrefab;
                    element.FindPropertyRelative("DurationSeconds").floatValue =
                        power.DurationSeconds;
                    element.FindPropertyRelative("Scale").floatValue = power.Scale;
                    element.FindPropertyRelative("SpawnOffset").vector3Value = power.SpawnOffset;
                    element.FindPropertyRelative("SpawnInFrontOfNpc").boolValue =
                        power.SpawnInFrontOfNpc;
                    element.FindPropertyRelative("ForwardDistance").floatValue =
                        power.ForwardDistance;
                    element.FindPropertyRelative("ColorOverride").colorValue = power.ColorOverride;
                    element.FindPropertyRelative("UseColorOverride").boolValue =
                        power.UseColorOverride;
                    element.FindPropertyRelative("Element").stringValue = power.Element ?? "";
                    element.FindPropertyRelative("VisualDescription").stringValue =
                        power.VisualDescription ?? "";

                    // Handle string array for CreativeTriggers
                    var triggersProp = element.FindPropertyRelative("CreativeTriggers");
                    if (triggersProp != null && power.CreativeTriggers != null)
                    {
                        triggersProp.ClearArray();
                        triggersProp.arraySize = power.CreativeTriggers.Length;
                        for (int j = 0; j < power.CreativeTriggers.Length; j++)
                        {
                            triggersProp.GetArrayElementAtIndex(j).stringValue =
                                power.CreativeTriggers[j];
                        }
                    }

                    element.FindPropertyRelative("EnableGameplayDamage").boolValue =
                        power.EnableGameplayDamage;
                    element.FindPropertyRelative("EnableHoming").boolValue = power.EnableHoming;
                    element.FindPropertyRelative("ProjectileSpeed").floatValue =
                        power.ProjectileSpeed;
                    element.FindPropertyRelative("HomingTurnRateDegrees").floatValue =
                        power.HomingTurnRateDegrees;
                    element.FindPropertyRelative("DamageAmount").floatValue = power.DamageAmount;
                    element.FindPropertyRelative("DamageRadius").floatValue = power.DamageRadius;
                    element.FindPropertyRelative("AffectPlayerOnly").boolValue =
                        power.AffectPlayerOnly;
                    element.FindPropertyRelative("DamageType").stringValue =
                        power.DamageType ?? "effect";
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(profile);
            }
            else
            {
                Debug.LogWarning("  ⚠ Could not find m_PrefabPowers property");
            }

            Debug.Log($"  → Total powers added: {newPowers.Count}");
        }

        private static GameObject FindParticlePrefab(string name)
        {
            // Try different paths
            string[] searchPaths = new[]
            {
                $"Assets/Network_Game/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/{name}.prefab",
                $"Assets/Network_Game/ParticlePack/EffectExamples/Misc Effects/Prefabs/{name}.prefab",
                $"Assets/Network_Game/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/{name}.prefab",
                $"Assets/Network_Game/ParticlePack/EffectExamples/Water Effects/Prefabs/{name}.prefab",
                $"Assets/Network_Game/ParticlePack/EffectExamples/Magic Effects/Prefabs/{name}.prefab",
                $"Assets/Network_Game/ParticlePack/EffectExamples/Legacy Particles/Prefabs/{name}.prefab",
                $"Assets/Network_Game/ParticlePack/EffectExamples/Weapon Effects/Prefabs/{name}.prefab",
                $"Assets/Network_Game/ParticlePack/EffectExamples/Goop Effects/Prefabs/{name}.prefab",
            };

            foreach (string path in searchPaths)
            {
                if (System.IO.File.Exists(path))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
            }

            // Try finding by name
            string[] guids = AssetDatabase.FindAssets(name + " t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("ParticlePack") && path.Contains("EffectExamples"))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
            }

            return null;
        }

        private static Color GetColorForCategory(string category)
        {
            return category switch
            {
                "fire" => new Color(1f, 0.5f, 0f),
                "ice" => new Color(0.7f, 0.9f, 1f),
                "storm" => new Color(0.6f, 0.8f, 1f),
                "water" => new Color(0.3f, 0.6f, 1f),
                "explosion" => new Color(1f, 0.8f, 0.3f),
                "plasma" => new Color(0.6f, 0.4f, 1f),
                "nature" => new Color(0.4f, 1f, 0.5f),
                "smoke" => new Color(0.5f, 0.5f, 0.5f),
                "steam" => new Color(0.9f, 0.9f, 1f),
                "earth" => new Color(0.6f, 0.5f, 0.3f),
                "magic" => new Color(0.8f, 0.4f, 1f),
                "poison" => new Color(0.4f, 0.8f, 0.3f),
                "goo" => new Color(0.3f, 0.8f, 0.2f),
                "light" => new Color(1f, 1f, 0.8f),
                "wind" => new Color(0.8f, 0.9f, 1f),
                "spark" => new Color(1f, 0.9f, 0.4f),
                "impact" => new Color(0.7f, 0.6f, 0.5f),
                _ => Color.white,
            };
        }

        private static string[] GenerateCreativeTriggers(string[] keywords, string category)
        {
            var triggers = new List<string>();

            // Add variations
            foreach (var kw in keywords)
            {
                triggers.Add($"cast {kw}");
                triggers.Add($"use {kw}");
                triggers.Add($"activate {kw}");
                triggers.Add($"trigger {kw}");
            }

            // Add category-specific creative triggers
            switch (category)
            {
                case "fire":
                    triggers.AddRange(
                        new[] { "ignite", "set ablaze", "incinerate", "engulf in flames" }
                    );
                    break;
                case "ice":
                    triggers.AddRange(
                        new[] { "shatter", "frost over", "freeze solid", "chill to the bone" }
                    );
                    break;
                case "storm":
                    triggers.AddRange(
                        new[] { "strike down", "electrify", "bring the thunder", "shock" }
                    );
                    break;
                case "water":
                    triggers.AddRange(new[] { "drown", "flood", "submerge", "drench" });
                    break;
                case "explosion":
                    triggers.AddRange(new[] { "detonate", "blow up", "annihilate", "obliterate" });
                    break;
                case "nature":
                    triggers.AddRange(new[] { "grow", "blossom", "entangle", "overgrow" });
                    break;
                case "magic":
                    triggers.AddRange(new[] { "cast spell", "weave magic", "enchant", "conjure" });
                    break;
            }

            return triggers.ToArray();
        }

        /// <summary>
        /// Print a summary of all available effects
        /// </summary>
        [MenuItem("Network Game/MCP/Legacy/Print All Available Effects")]
        public static void PrintAllEffects()
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n============================================================");
            sb.AppendLine("ALL AVAILABLE PARTICLE EFFECTS");
            sb.AppendLine("============================================================\n");

            var grouped = k_AllEffects.GroupBy(e => e.category).OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                sb.AppendLine($"[{group.Key.ToUpper()}]");
                foreach (var effect in group)
                {
                    sb.AppendLine($"  • {effect.prefabName}: {effect.description}");
                    sb.AppendLine($"    Keywords: {string.Join(", ", effect.keywords)}");
                }
                sb.AppendLine();
            }

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Verify all effects are properly linked
        /// </summary>
        [MenuItem("Network Game/MCP/Legacy/Verify Effect Links")]
        public static void VerifyEffectLinks()
        {
            Debug.Log("\n============================================================");
            Debug.Log("VERIFYING EFFECT PREFAB LINKS");
            Debug.Log("============================================================\n");

            int total = 0;
            int found = 0;
            int missing = 0;

            foreach (var effect in k_AllEffects)
            {
                total++;
                var prefab = FindParticlePrefab(effect.prefabName);
                if (prefab != null)
                {
                    found++;
                    Debug.Log($"  ✓ {effect.prefabName} ({effect.category})");
                }
                else
                {
                    missing++;
                    Debug.LogError($"  ✗ {effect.prefabName} NOT FOUND");
                }
            }

            Debug.Log($"\n============================================================");
            Debug.Log($"RESULTS: {found}/{total} found, {missing} missing");
            Debug.Log("============================================================");
        }
    }
}
