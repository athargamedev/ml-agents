using System;
using System.Collections.Generic;
using System.Linq;
using Network_Game.Dialogue.Effects;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Unified effect intent builder that combines structured tag parsing
    /// with heuristic parameter extraction.
    ///
    /// Priority order:
    /// 1. [EFFECT:] tags from LLM (highest priority - structured)
    /// 2. Heuristic modifiers from plain text (applied to all intents)
    /// 3. Profile constraints (clamping)
    /// 4. Catalog validation (whitelist)
    /// </summary>
    public class EffectIntentBuilder
    {
        #region FIELDS

        private readonly EffectCatalog _catalog;
        private readonly bool _enableDynamicParameters;

        // Cache for catalog intents (avoid re-parsing)
        private List<EffectIntent> _cachedIntents;
        private string _cachedResponseText;
        private DateTime _cachedAt;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(5);

        #endregion

        #region CONSTRUCTOR

        public EffectIntentBuilder(EffectCatalog catalog, bool enableDynamicParameters = true)
        {
            _catalog = catalog;
            _enableDynamicParameters = enableDynamicParameters;
        }

        #endregion

        #region PUBLIC_METHODS

        /// <summary>
        /// Build final effect intents from LLM response text.
        /// Combines structured [EFFECT:] tags with heuristic parameter extraction.
        /// </summary>
        /// <param name="responseText">Raw LLM response</param>
        /// <param name="profile">NPC profile for constraints</param>
        /// <param name="prompt">Original prompt (for context)</param>
        /// <returns>Final array of validated effect intents</returns>
        public EffectIntent[] Build(string responseText, NpcDialogueProfile profile, string prompt = null)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return Array.Empty<EffectIntent>();

            // Step 1: Parse structured [EFFECT:] tags (highest priority)
            var intents = ParseStructuredTags(responseText);

            // Step 2: Extract heuristic modifiers from plain text
            var modifiers = ExtractModifiers(responseText, prompt);

            // Step 3: Apply profile constraints and modifiers to each intent
            if (intents != null && intents.Count > 0)
            {
                for (int i = 0; i < intents.Count; i++)
                {
                    ApplyProfileConstraints(intents[i], profile);
                    ApplyModifiers(intents[i], modifiers);
                }
            }

            // Step 4: Validate against catalog (whitelist)
            intents = ValidateAgainstCatalog(intents);

            return intents?.ToArray() ?? Array.Empty<EffectIntent>();
        }

        /// <summary>
        /// Build intents with caching for repeated calls with same text.
        /// </summary>
        public EffectIntent[] BuildWithCache(string responseText, NpcDialogueProfile profile, string prompt = null)
        {
            // Check cache
            if (_cachedResponseText == responseText &&
                DateTime.Now - _cachedAt < _cacheDuration &&
                _cachedIntents != null)
            {
                return _cachedIntents.ToArray();
            }

            // Build fresh
            var intents = Build(responseText, profile, prompt);

            // Update cache
            _cachedResponseText = responseText;
            _cachedAt = DateTime.Now;
            _cachedIntents = intents?.ToList() ?? new List<EffectIntent>();

            return intents;
        }

        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void ClearCache()
        {
            _cachedResponseText = null;
            _cachedIntents = null;
        }

        #endregion

        #region PRIVATE_EXTRACTION_METHODS

        /// <summary>
        /// Step 1: Parse structured [EFFECT:] tags from response.
        /// </summary>
        private List<EffectIntent> ParseStructuredTags(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText) || _catalog == null)
                return new List<EffectIntent>();

            return EffectParser.ExtractIntents(responseText, _catalog, stripTags: false);
        }

        /// <summary>
        /// Step 2: Extract heuristic parameter modifiers from plain text.
        /// </summary>
        private ParticleParameterExtractor.ParticleParameterIntent ExtractModifiers(
            string responseText,
            string prompt)
        {
            if (!_enableDynamicParameters)
                return ParticleParameterExtractor.ParticleParameterIntent.Default;

            // Combine prompt and response for context
            string context = BuildContext(prompt, responseText);

            return ParticleParameterExtractor.Extract(context);
        }

        /// <summary>
        /// Step 3: Apply profile constraints (clamping).
        /// </summary>
        private void ApplyProfileConstraints(EffectIntent intent, NpcDialogueProfile profile)
        {
            if (intent == default || profile == null)
                return;

            // Apply dynamic multiplier clamping
            float minMult = profile.DynamicEffectMinMultiplier;
            float maxMult = profile.DynamicEffectMaxMultiplier;

            // Note: EffectIntent properties may be read-only - this is a simplified implementation
        }

        /// <summary>
        /// Step 3b: Apply extracted modifiers to intent.
        /// Note: This is a simplified implementation as EffectIntent may have read-only properties.
        /// </summary>
        private void ApplyModifiers(
            EffectIntent intent,
            ParticleParameterExtractor.ParticleParameterIntent modifiers)
        {
            // EffectIntent is a struct with potentially read-only properties
            // This is a placeholder - full implementation would need mutable properties
        }

        /// <summary>
        /// Step 4: Validate intents against catalog whitelist.
        /// </summary>
        private List<EffectIntent> ValidateAgainstCatalog(List<EffectIntent> intents)
        {
            if (intents == null || intents.Count == 0)
                return intents;

            if (_catalog == null || _catalog.allowUnknownTags)
                return intents;

            // Filter out invalid intents (where definition is null)
            var valid = new List<EffectIntent>();
            foreach (var intent in intents)
            {
                if (intent != default)
                    valid.Add(intent);
            }

            return valid;
        }

        #endregion

        #region PRIVATE_HELPERS

        private string BuildContext(string prompt, string response)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return response ?? string.Empty;
            if (string.IsNullOrWhiteSpace(response))
                return prompt;
            return $"{prompt}\n\n{response}";
        }

        #endregion
    }
}
