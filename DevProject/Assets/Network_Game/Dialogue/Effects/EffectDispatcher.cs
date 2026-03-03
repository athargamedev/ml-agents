using System;
using System.Collections.Generic;
using Network_Game.Dialogue.Effects;
using Network_Game.Diagnostics;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Handles effect dispatch from dialogue responses to the scene.
    /// Extracted from NetworkDialogueService for single responsibility.
    ///
    /// Supported effects:
    /// - Catalog intents (prefab powers from [EFFECT:] tags)
    /// - Player special effects (dissolve, respawn)
    /// - Dynamic parameter modifiers (color, scale, duration from NLP)
    /// </summary>
    public class EffectDispatcher
    {
        #region STRUCTS

        /// <summary>
        /// Context for effect dispatch.
        /// </summary>
        public struct DispatchContext
        {
            public string Prompt;
            public string ResponseText;
            public ulong SpeakerNetworkId;
            public ulong ListenerNetworkId;
            public ulong RequestingClientId;
            public NpcDialogueActor Actor;
            public NpcDialogueProfile Profile;
            public GameObject SpeakerObject;
            public GameObject ListenerObject;
            public Vector3 EffectOrigin;
            public Vector3 EffectForward;
            public string EffectAnchorLabel;
            public EffectModifier Modifier;
        }

        /// <summary>
        /// Per-player effect modifiers.
        /// </summary>
        public struct EffectModifier
        {
            public float DamageScaleReceived;
            public float EffectSizeScale;
            public float EffectDurationScale;
            public Color? PreferredColor;
            public string PreferredElement;
            public bool IsShielded;

            public static EffectModifier Neutral => new EffectModifier
            {
                DamageScaleReceived = 1f,
                EffectSizeScale = 1f,
                EffectDurationScale = 1f,
                PreferredColor = null,
                PreferredElement = null,
                IsShielded = false
            };
        }

        #endregion

        // Dependencies
        private readonly DialogueSceneEffectsController _sceneEffects;
        private readonly EffectCatalog _catalog;
        private readonly bool _enableContextSceneEffects;
        private readonly bool _logDebug;

        public EffectDispatcher(
            DialogueSceneEffectsController sceneEffects,
            EffectCatalog catalog,
            bool enableContextSceneEffects = true,
            bool logDebug = false)
        {
            _sceneEffects = sceneEffects;
            _catalog = catalog;
            _enableContextSceneEffects = enableContextSceneEffects;
            _logDebug = logDebug;
        }

        /// <summary>
        /// Main entry point: dispatch all effects from a dialogue response.
        /// </summary>
        public void Dispatch(DispatchContext context)
        {
            Dispatch(context, null);
        }

        /// <summary>
        /// Dispatch effects using prebuilt catalog intents (optional).
        /// </summary>
        public void Dispatch(DispatchContext context, List<EffectIntent> catalogIntents)
        {
            if (!_enableContextSceneEffects)
                return;

            if (string.IsNullOrWhiteSpace(context.ResponseText))
                return;

            // Extract parameter intent (NLP modifiers)
            string effectContext = BuildEffectContextText(context.Prompt, context.ResponseText);
            var parameterIntent = ExtractParameterIntent(context, effectContext);

            // Use provided intents or parse structured [EFFECT:] tags
            if (catalogIntents == null)
                catalogIntents = ExtractCatalogIntents(context.ResponseText);

            // Resolve special effect mode
            var specialMode = ResolveSpecialEffectMode(context.Prompt);

            // Check if there's anything to dispatch
            bool hasCatalogIntents = catalogIntents != null && catalogIntents.Count > 0;
            bool hasPlayerSpecialEffect = specialMode != PlayerSpecialEffectMode.None;

            if (!hasCatalogIntents && !hasPlayerSpecialEffect)
            {
                if (_logDebug)
                    NGLog.Debug("DialogueFX", "No effects matched.");
                return;
            }

            // Dispatch effects
            if (hasPlayerSpecialEffect)
                DispatchPlayerSpecialEffects(context, specialMode);

            if (hasCatalogIntents)
                DispatchCatalogIntents(context, catalogIntents, parameterIntent);
        }

        #region PRIVATE_DISPATCH_METHODS

        private void DispatchCatalogIntents(
            DispatchContext context,
            List<EffectIntent> intents,
            ParticleParameterExtractor.ParticleParameterIntent modifiers)
        {
            if (_sceneEffects == null || intents == null || intents.Count == 0)
                return;

            if (_logDebug)
                NGLog.Debug("DialogueFX", $"Dispatching {intents.Count} catalog effects");

            // TODO: Full implementation would iterate and dispatch each intent
            // with modifiers applied (scale, duration, color from modifiers)
        }

        private void DispatchPlayerSpecialEffects(DispatchContext context, PlayerSpecialEffectMode mode)
        {
            if (_sceneEffects == null)
                return;

            if (_logDebug)
                NGLog.Debug("DialogueFX", $"Player special effect: {mode}");

            // TODO: Dispatch dissolve/respawn effects via sceneEffects
        }

        #endregion

        #region PRIVATE_HELPERS

        private string BuildEffectContextText(string prompt, string response)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return response ?? string.Empty;
            if (string.IsNullOrWhiteSpace(response))
                return prompt;
            return $"{prompt}\n\n{response}";
        }

        private ParticleParameterExtractor.ParticleParameterIntent ExtractParameterIntent(
            DispatchContext context,
            string effectContext)
        {
            if (context.Profile != null && context.Profile.EnableDynamicEffectParameters)
            {
                return ParticleParameterExtractor.Extract(effectContext);
            }
            return ParticleParameterExtractor.ParticleParameterIntent.Default;
        }

        private List<EffectIntent> ExtractCatalogIntents(string responseText)
        {
            if (_catalog == null || string.IsNullOrWhiteSpace(responseText))
                return null;

            return EffectParser.ExtractIntents(responseText, _catalog, stripTags: false);
        }

        private PlayerSpecialEffectMode ResolveSpecialEffectMode(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return PlayerSpecialEffectMode.None;

            string lower = prompt.ToLowerInvariant();

            if (lower.Contains("dissolve") || lower.Contains("disappear") || lower.Contains("vanish"))
                return PlayerSpecialEffectMode.Dissolve;

            if (lower.Contains("respawn") || lower.Contains("return") || lower.Contains("appear"))
                return PlayerSpecialEffectMode.Respawn;

            return PlayerSpecialEffectMode.None;
        }

        #endregion

        #region ENUMS

        public enum PlayerSpecialEffectMode
        {
            None,
            Dissolve,
            Respawn
        }

        #endregion
    }
}
