using System;
using System.Collections.Generic;
using System.IO;
using Network_Game.Dialogue;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools.Services
{
    /// <summary>
    /// Service for creating and modifying NpcDialogueProfile ScriptableObjects via automation.
    /// </summary>
    public static class ProfileAutomationService
    {
        private static readonly string k_DefaultSavePath = "Assets/Network_Game/Dialogue/Profiles";

        /// <summary>
        /// Create a complete NPC dialogue profile.
        /// </summary>
        public static (NpcDialogueProfile profile, string assetPath, List<string> warnings) CreateProfile(
            string profileId,
            string displayName,
            string systemPrompt,
            string lore = "",
            string[] boredKeywords = null,
            bool enableBoredLight = true,
            bool enableDynamicParams = true)
        {
            var warnings = new List<string>();

            var profile = ScriptableObject.CreateInstance<NpcDialogueProfile>();

            // Use SerializedObject for private field access
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_ProfileId").stringValue = profileId ?? "npc.new";
            serialized.FindProperty("m_DisplayName").stringValue = displayName ?? "NPC";
            serialized.FindProperty("m_SystemPrompt").stringValue = systemPrompt ?? "You are {npc_name}.";
            serialized.FindProperty("m_Lore").stringValue = lore ?? "";
            serialized.FindProperty("m_EnableBoredLightEffect").boolValue = enableBoredLight;
            serialized.FindProperty("m_EnableDynamicEffectParameters").boolValue = enableDynamicParams;

            if (boredKeywords != null && boredKeywords.Length > 0)
            {
                var boredProp = serialized.FindProperty("m_BoredKeywords");
                boredProp.ClearArray();
                boredProp.arraySize = boredKeywords.Length;
                for (int i = 0; i < boredKeywords.Length; i++)
                {
                    boredProp.GetArrayElementAtIndex(i).stringValue = boredKeywords[i];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Save asset
            if (!Directory.Exists(k_DefaultSavePath))
            {
                Directory.CreateDirectory(k_DefaultSavePath);
                AssetDatabase.Refresh();
            }

            string safeName = (profileId ?? "npc_new").Replace(".", "_").Replace(" ", "_");
            string assetPath = $"{k_DefaultSavePath}/{safeName}.asset";
            if (File.Exists(assetPath))
            {
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                warnings.Add($"Profile path already exists, saving as: {assetPath}");
            }

            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssets();

            return (profile, assetPath, warnings);
        }

        /// <summary>
        /// Patch an existing NPC profile by profile ID.
        /// </summary>
        public static (bool success, List<string> warnings) PatchProfile(
            string profileId,
            Dictionary<string, object> fieldPatches)
        {
            var warnings = new List<string>();

            NpcDialogueProfile profile = NpcDialogueProfile.GetProfile(profileId);
            if (profile == null)
            {
                // Also try finding by asset search
                string[] guids = AssetDatabase.FindAssets("t:NpcDialogueProfile");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var candidate = AssetDatabase.LoadAssetAtPath<NpcDialogueProfile>(path);
                    if (candidate != null && candidate.ProfileId == profileId)
                    {
                        profile = candidate;
                        break;
                    }
                }
            }

            if (profile == null)
            {
                warnings.Add($"Profile '{profileId}' not found.");
                return (false, warnings);
            }

            var serialized = new SerializedObject(profile);
            int appliedCount = 0;

            foreach (var kvp in fieldPatches)
            {
                string fieldName = MapFieldName(kvp.Key);
                var prop = serialized.FindProperty(fieldName);
                if (prop == null)
                {
                    warnings.Add($"Unknown field: '{kvp.Key}' (mapped to '{fieldName}')");
                    continue;
                }

                try
                {
                    ApplyValue(prop, kvp.Value);
                    appliedCount++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to set '{kvp.Key}': {ex.Message}");
                }
            }

            if (appliedCount > 0)
            {
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            return (appliedCount > 0, warnings);
        }

        /// <summary>
        /// Add PrefabPowerEntry entries to an existing profile.
        /// </summary>
        public static List<string> AddPowersToProfile(
            string profileId,
            List<(string powerName, string[] keywords, string prefabName, string element)> powers)
        {
            var warnings = new List<string>();

            NpcDialogueProfile profile = NpcDialogueProfile.GetProfile(profileId);
            if (profile == null)
            {
                warnings.Add($"Profile '{profileId}' not found.");
                return warnings;
            }

            var serialized = new SerializedObject(profile);
            var powersProp = serialized.FindProperty("m_PrefabPowers");
            if (powersProp == null)
            {
                warnings.Add("Could not find m_PrefabPowers property.");
                return warnings;
            }

            int baseIndex = powersProp.arraySize;

            foreach (var (powerName, keywords, prefabName, element) in powers)
            {
                int idx = powersProp.arraySize;
                powersProp.InsertArrayElementAtIndex(idx);
                var entry = powersProp.GetArrayElementAtIndex(idx);

                entry.FindPropertyRelative("PowerName").stringValue = powerName ?? "";
                entry.FindPropertyRelative("Enabled").boolValue = true;
                entry.FindPropertyRelative("DurationSeconds").floatValue = 4f;
                entry.FindPropertyRelative("Scale").floatValue = 1f;
                entry.FindPropertyRelative("SpawnOffset").vector3Value = new Vector3(0f, 0.5f, 0f);
                entry.FindPropertyRelative("SpawnInFrontOfNpc").boolValue = true;
                entry.FindPropertyRelative("ForwardDistance").floatValue = 2f;
                entry.FindPropertyRelative("Element").stringValue = element ?? "";

                // Set keywords
                if (keywords != null && keywords.Length > 0)
                {
                    var kwProp = entry.FindPropertyRelative("Keywords");
                    kwProp.ClearArray();
                    kwProp.arraySize = keywords.Length;
                    for (int j = 0; j < keywords.Length; j++)
                    {
                        kwProp.GetArrayElementAtIndex(j).stringValue = keywords[j];
                    }
                }

                // Resolve prefab
                if (!string.IsNullOrWhiteSpace(prefabName))
                {
                    GameObject prefab = EffectDefinitionFactory.ResolvePrefabByName(prefabName);
                    if (prefab != null)
                    {
                        entry.FindPropertyRelative("EffectPrefab").objectReferenceValue = prefab;
                    }
                    else
                    {
                        warnings.Add($"Prefab '{prefabName}' not found for power '{powerName}'.");
                    }
                }
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            return warnings;
        }

        /// <summary>
        /// Get a summary of a profile suitable for tool responses.
        /// </summary>
        public static Dictionary<string, object> GetProfileSummary(NpcDialogueProfile profile)
        {
            if (profile == null)
            {
                return null;
            }

            return new Dictionary<string, object>
            {
                ["profile_id"] = profile.ProfileId,
                ["display_name"] = profile.DisplayName,
                ["system_prompt_length"] = profile.SystemPrompt?.Length ?? 0,
                ["lore_length"] = profile.Lore?.Length ?? 0,
                ["bored_light_enabled"] = profile.EnableBoredLightEffect,
                ["bored_keyword_count"] = profile.BoredKeywords?.Length ?? 0,
                ["power_count"] = profile.PrefabPowers?.Length ?? 0,
                ["dynamic_params_enabled"] = profile.EnableDynamicEffectParameters,
            };
        }

        private static string MapFieldName(string key)
        {
            return key.ToLowerInvariant() switch
            {
                "system_prompt" or "systemprompt" or "prompt" => "m_SystemPrompt",
                "display_name" or "displayname" or "name" => "m_DisplayName",
                "profile_id" or "profileid" or "id" => "m_ProfileId",
                "lore" => "m_Lore",
                "bored_keywords" or "boredkeywords" => "m_BoredKeywords",
                "enable_bored_light" or "enableboredlight" => "m_EnableBoredLightEffect",
                "bored_light_color" or "boredlightcolor" => "m_BoredLightColor",
                "bored_light_intensity" or "boredlightintensity" => "m_BoredLightIntensity",
                "light_transition" or "lighttransition" => "m_LightTransitionSeconds",
                "enable_dynamic_params" or "enabledynamicparams" => "m_EnableDynamicEffectParameters",
                "dynamic_min" or "dynamicmin" => "m_DynamicEffectMinMultiplier",
                "dynamic_max" or "dynamicmax" => "m_DynamicEffectMaxMultiplier",
                _ => $"m_{key}",
            };
        }

        private static void ApplyValue(SerializedProperty prop, object value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:
                    prop.stringValue = value?.ToString() ?? "";
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Integer:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Color:
                    if (value is string colorStr && ColorUtility.TryParseHtmlString(colorStr, out Color c))
                    {
                        prop.colorValue = c;
                    }
                    break;
                default:
                    throw new NotSupportedException($"Property type {prop.propertyType} not supported for patching.");
            }
        }
    }
}
