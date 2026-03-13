using Network_Game.Diagnostics;
using UnityEngine;

namespace Network_Game.Dialogue.Effects
{
    /// <summary>
    /// Runs the effect pipeline locally in the editor for fast iteration.
    /// Useful for preview buttons and unit tests.
    /// </summary>
    public static class EffectSandboxRunner
    {
        /// <summary>
        /// Preview an effect tag in the current scene (editor-only).
        /// </summary>
        /// <param name="effectTagString">The full [EFFECT: Tag | Param: Value] string</param>
        /// <param name="definition">Optional pre-resolved effect definition</param>
        /// <returns>The spawned preview GameObject (caller should destroy)</returns>
        public static GameObject PreviewEffect(
            string effectTagString,
            EffectDefinition definition = null
        )
        {
#if UNITY_EDITOR
            // Parse the tag
            var catalog = EffectCatalog.Load();
            var intents = EffectParser.ExtractIntents(effectTagString, catalog, false);

            if (intents.Count == 0)
            {
                Debug.LogWarning($"[EffectSandbox] No effect tags found in: {effectTagString}");
                return null;
            }

            var intent = intents[0];

            // Use provided definition or resolve from tag
            var effectDef = definition ?? intent.definition;
            if (effectDef == null || effectDef.effectPrefab == null)
            {
                Debug.LogWarning(
                    $"[EffectSandbox] No effect definition found for tag: {intent.rawTagName}"
                );
                return null;
            }

            // Determine spawn position (in front of scene camera)
            Vector3 spawnPos = GetSpawnPosition();
            GameObject preview = Object.Instantiate(
                effectDef.effectPrefab,
                spawnPos,
                Quaternion.identity
            );
            preview.name = $"Sandbox_{effectDef.effectTag}";

            // Apply parameters
            ApplyParameters(preview, intent);

            // Log for debugging
            NGLog.Info(
                "DialogueFX",
                $"[EffectSandbox] Preview spawned: {effectDef.effectTag} at {spawnPos}"
            );

            return preview;
#else
            Debug.LogWarning("[EffectSandbox] PreviewEffect is only available in Unity Editor");
            return null;
#endif
        }

        /// <summary>
        /// Run a full pipeline test locally (no network, no queue).
        /// </summary>
        /// <param name="llmResponse">Simulated LLM response with effect tags</param>
        /// <param name="playerPosition">Where to spawn effects</param>
        /// <returns>Number of effects applied</param>
        public static int RunLocalTest(string llmResponse, Vector3 playerPosition)
        {
            var catalog = EffectCatalog.Load();
            var intents = EffectParser.ExtractIntents(llmResponse, catalog, true);

            int applied = 0;
            foreach (var intent in intents)
            {
                if (!intent.isValid)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        $"[EffectSandbox] Skipping invalid effect: {intent.rawTagName}"
                    );
                    continue;
                }

                // In a real scenario, this would call DialogueSceneEffectsController.ApplyPrefabPower
                // For sandbox, we just spawn locally
                GameObject prefab = intent.definition.effectPrefab;
                if (prefab != null)
                {
                    GameObject instance = Object.Instantiate(
                        prefab,
                        playerPosition,
                        Quaternion.identity
                    );
                    ApplyParameters(instance, intent);

                    float lifetime = intent.GetEffectiveDuration() + 1f;
                    Object.Destroy(instance, lifetime);
                    applied++;
                }
            }

            NGLog.Info(
                "DialogueFX",
                $"[EffectSandbox] Applied {applied} effects from test response"
            );
            return applied;
        }

#if UNITY_EDITOR
        private static Vector3 GetSpawnPosition()
        {
            // Try scene view camera
            var sceneCam = UnityEditor.SceneView.lastActiveSceneView?.camera;
            if (sceneCam != null)
            {
                return sceneCam.transform.position + sceneCam.transform.forward * 3f;
            }

            // Fallback to origin
            return new Vector3(0, 2, 0);
        }
#endif

        private static void ApplyParameters(GameObject go, EffectIntent intent)
        {
            float scale = intent.GetEffectiveScale();
            float duration = intent.GetEffectiveDuration();
            Color color = intent.GetEffectiveColor();

            // Apply to particle systems
            var systems = go.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.startSize = main.startSize.constant * scale;
                main.duration = duration;
                main.startColor = color;
                main.loop = false;
            }

            // Apply scale to transform
            go.transform.localScale = go.transform.localScale * scale;
        }
    }
}
