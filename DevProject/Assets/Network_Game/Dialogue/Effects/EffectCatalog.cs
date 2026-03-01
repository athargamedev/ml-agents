using System.Collections.Generic;
using UnityEngine;

namespace Network_Game.Dialogue.Effects
{
    /// <summary>
    /// Central registry for all effect definitions in the dialogue system.
    /// Single source of truth for what effects NPCs can trigger.
    /// </summary>
    [CreateAssetMenu(fileName = "EffectCatalog", menuName = "Dialogue/Effect Catalog")]
    public class EffectCatalog : ScriptableObject
    {
        public static EffectCatalog Instance { get; private set; }

        [Header("Effect Definitions")]
        [Tooltip("All available effects in the system. Populated automatically or manually.")]
        public List<EffectDefinition> allEffects = new List<EffectDefinition>();

        [Header("Fallback")]
        [Tooltip("Prefab to use when an unknown effect tag is received (safety fallback)")]
        public GameObject fallbackEffectPrefab;

        [Header("Settings")]
        [Tooltip("Log warnings when LLM uses unknown effect tags")]
        public bool logUnknownTags = true;

        [Tooltip("Allow unknown tags to silently pass (if false, unknown tags are dropped)")]
        public bool allowUnknownTags = false;

        // Runtime lookup cache
        private Dictionary<string, EffectDefinition> _byTag;
        private Dictionary<string, EffectDefinition> _byAlternativeTag;
        private bool _isInitialized;

        /// <summary>
        /// Initialize the catalog at runtime. Called automatically when accessed.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized && _byTag != null && _byAlternativeTag != null)
                return;

            _byTag = new Dictionary<string, EffectDefinition>(
                System.StringComparer.OrdinalIgnoreCase
            );
            _byAlternativeTag = new Dictionary<string, EffectDefinition>(
                System.StringComparer.OrdinalIgnoreCase
            );

            if (allEffects == null)
            {
                allEffects = new List<EffectDefinition>();
            }

            foreach (var effect in allEffects)
            {
                if (effect == null || string.IsNullOrWhiteSpace(effect.effectTag))
                    continue;

                // Primary tag lookup
                if (!_byTag.ContainsKey(effect.effectTag))
                {
                    _byTag[effect.effectTag] = effect;
                }

                // Alternative tags lookup
                if (effect.alternativeTags != null)
                {
                    foreach (var altTag in effect.alternativeTags)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(altTag)
                            && !_byAlternativeTag.ContainsKey(altTag)
                        )
                        {
                            _byAlternativeTag[altTag] = effect;
                        }
                    }
                }
            }

            _isInitialized = true;
        }

        /// <summary>
        /// Try to find an effect definition by tag (case-insensitive).
        /// </summary>
        public bool TryGet(string tag, out EffectDefinition effect)
        {
            if (!_isInitialized || _byTag == null || _byAlternativeTag == null)
                Initialize();

            if (string.IsNullOrWhiteSpace(tag))
            {
                effect = null;
                return false;
            }

            // Check primary tags
            if (_byTag != null && _byTag.TryGetValue(tag, out effect))
                return true;

            // Check alternative tags
            if (_byAlternativeTag != null && _byAlternativeTag.TryGetValue(tag, out effect))
                return true;

            effect = null;
            return false;
        }

        /// <summary>
        /// Registers a runtime-created EffectDefinition (e.g. from PrefabPowerEntry)
        /// without modifying the serialized allEffects list.
        /// </summary>
        public void RegisterRuntimeEffect(EffectDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.effectTag))
                return;

            if (!_isInitialized || _byTag == null || _byAlternativeTag == null)
                Initialize();

            if (_byTag == null || _byAlternativeTag == null)
            {
                Debug.LogWarning("[EffectCatalog] Runtime lookup tables unavailable; skipping runtime effect registration.");
                return;
            }

            if (!_byTag.ContainsKey(def.effectTag))
                _byTag[def.effectTag] = def;

            if (def.alternativeTags != null)
            {
                foreach (var altTag in def.alternativeTags)
                {
                    if (!string.IsNullOrWhiteSpace(altTag) && !_byAlternativeTag.ContainsKey(altTag))
                        _byAlternativeTag[altTag] = def;
                }
            }
        }

        /// <summary>
        /// Get all available effect tags as a formatted string for LLM prompts.
        /// </summary>
        public string GetPromptCatalog()
        {
            if (!_isInitialized)
                Initialize();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Available Effects:");

            foreach (var effect in allEffects)
            {
                if (effect == null)
                    continue;
                sb.AppendLine($"- {effect.effectTag}: {effect.description}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Load the catalog from Resources.
        /// </summary>
        public static EffectCatalog Load()
        {
            if (Instance != null)
                return Instance;

            Instance = Resources.Load<EffectCatalog>("Dialogue/EffectCatalog");
            if (Instance != null)
            {
                Instance.Initialize();
            }
            return Instance;
        }

        /// <summary>
        /// Force rebuild of lookup tables (call after modifying allEffects at runtime).
        /// </summary>
        public void RebuildLookup()
        {
            _isInitialized = false;
            Initialize();
        }

        /// <summary>
        /// All effects available at runtime: serialized list + any registered dynamically
        /// via RegisterRuntimeEffect (e.g. NPC profile PrefabPowers). Use this for LLM
        /// prompts so the full catalog is visible, not just the serialized asset entries.
        /// </summary>
        public IEnumerable<EffectDefinition> GetAllRegisteredEffects()
        {
            if (!_isInitialized || _byTag == null)
                Initialize();
            return _byTag?.Values ?? (System.Collections.Generic.IEnumerable<EffectDefinition>)allEffects;
        }

        private void OnEnable()
        {
            Instance = this;
            Initialize();
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
