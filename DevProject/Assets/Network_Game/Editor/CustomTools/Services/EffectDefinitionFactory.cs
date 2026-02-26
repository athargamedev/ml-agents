using System.Collections.Generic;
using System.IO;
using System.Linq;
using Network_Game.Dialogue.Effects;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools.Services
{
    /// <summary>
    /// Factory for creating and registering EffectDefinition ScriptableObjects.
    /// </summary>
    public static class EffectDefinitionFactory
    {
        private static readonly string k_DefaultSavePath = "Assets/Network_Game/Dialogue/Effects";
#pragma warning disable CS0414
        private static readonly string k_CatalogResourcePath = "Assets/Resources/Dialogue/EffectCatalog.asset";
#pragma warning restore CS0414

        /// <summary>
        /// Create an EffectDefinition asset, resolve its prefab, and optionally register in catalog.
        /// </summary>
        public static (EffectDefinition definition, string assetPath, List<string> warnings) Create(
            string effectTag,
            string description,
            string prefabName,
            EffectPlacementMode placementMode = EffectPlacementMode.Auto,
            EffectTargetType targetType = EffectTargetType.Auto,
            float defaultScale = 1f,
            float defaultDuration = 4f,
            Color? defaultColor = null,
            string[] alternativeTags = null,
            bool enableDamage = false,
            float damageAmount = 10f,
            bool registerInCatalog = true)
        {
            var warnings = new List<string>();

            // Create the SO
            var def = ScriptableObject.CreateInstance<EffectDefinition>();
            def.effectTag = effectTag;
            def.description = description ?? "";
            def.placementMode = placementMode;
            def.targetType = targetType;
            def.defaultScale = defaultScale;
            def.defaultDuration = defaultDuration;
            def.defaultColor = defaultColor ?? Color.white;
            def.alternativeTags = alternativeTags ?? new string[0];
            def.enableGameplayDamage = enableDamage;
            def.damageAmount = damageAmount;

            // Resolve prefab
            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                GameObject prefab = ResolvePrefabByName(prefabName);
                if (prefab != null)
                {
                    def.effectPrefab = prefab;
                }
                else
                {
                    warnings.Add($"Prefab '{prefabName}' not found in ParticlePack or project.");
                }
            }
            else
            {
                warnings.Add("No prefab name specified.");
            }

            // Save asset
            string directory = k_DefaultSavePath;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            string safeName = effectTag.Replace(" ", "_").Replace("/", "_");
            string assetPath = $"{directory}/{safeName}.asset";

            // Avoid overwrite
            if (File.Exists(assetPath))
            {
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                warnings.Add($"Asset path already exists, saving as: {assetPath}");
            }

            AssetDatabase.CreateAsset(def, assetPath);

            // Register in catalog
            if (registerInCatalog)
            {
                string catalogWarning = RegisterInCatalog(def);
                if (catalogWarning != null)
                {
                    warnings.Add(catalogWarning);
                }
            }

            AssetDatabase.SaveAssets();

            return (def, assetPath, warnings);
        }

        /// <summary>
        /// Resolve a prefab by name, searching ParticlePack and project-wide.
        /// </summary>
        public static GameObject ResolvePrefabByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // Search ParticlePack paths first
            string[] searchRoots = new[]
            {
                "Assets/Network_Game/ParticlePack/EffectExamples",
                "Assets/Network_Game/Dialogue/Addressables/DialoguePowers",
                "Assets/Network_Game/Dialogue/Resources/DialoguePowers",
            };

            foreach (string root in searchRoots)
            {
                string[] guids = AssetDatabase.FindAssets($"{name} t:Prefab", new[] { root });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null && go.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return go;
                    }
                }
            }

            // Fallback: project-wide search
            string[] allGuids = AssetDatabase.FindAssets($"{name} t:Prefab");
            foreach (string guid in allGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return go;
                }
            }

            return null;
        }

        /// <summary>
        /// Register an EffectDefinition in the EffectCatalog's allEffects list.
        /// </summary>
        public static string RegisterInCatalog(EffectDefinition definition)
        {
            // Find the catalog asset
            EffectCatalog catalog = null;

            string[] guids = AssetDatabase.FindAssets("t:EffectCatalog");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                catalog = AssetDatabase.LoadAssetAtPath<EffectCatalog>(path);
            }

            if (catalog == null)
            {
                return "EffectCatalog not found. Effect was created but not registered.";
            }

            // Check for duplicate
            if (catalog.allEffects != null)
            {
                for (int i = 0; i < catalog.allEffects.Count; i++)
                {
                    if (catalog.allEffects[i] != null &&
                        string.Equals(catalog.allEffects[i].effectTag, definition.effectTag, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Tag '{definition.effectTag}' already exists in catalog at index {i}. Skipping registration.";
                    }
                }
            }

            // Add to catalog
            var serialized = new SerializedObject(catalog);
            var effectsProperty = serialized.FindProperty("allEffects");
            int newIndex = effectsProperty.arraySize;
            effectsProperty.InsertArrayElementAtIndex(newIndex);
            effectsProperty.GetArrayElementAtIndex(newIndex).objectReferenceValue = definition;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);

            return null; // success
        }
    }
}
