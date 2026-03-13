using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Network_Game.Dialogue;
using Network_Game.Dialogue.Effects;
using Network_Game.Dialogue.MCP;
using Network_Game.Editor.CustomTools.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools
{
    [McpForUnityTool(
        "ng_create_effect_definition",
        Description = "Create a new EffectDefinition asset, link its prefab from ParticlePack, register it in the EffectCatalog, and save — all in one call. Params: effect_tag (required), description, prefab_name, placement_mode (Auto/AttachMesh/GroundAoe/SkyVolume/Projectile), target_type (Auto/Player/Floor/Npc/WorldPoint), default_scale, default_duration, alternative_tags (comma-separated), enable_damage, damage_amount."
    )]
    public static class CreateEffectDefinitionTool
    {
        public static object HandleCommand(JObject @params)
        {
            string effectTag = @params?.Value<string>("effect_tag");
            if (string.IsNullOrWhiteSpace(effectTag))
            {
                return new ErrorResponse("effect_tag is required.");
            }

            string description = @params.Value<string>("description") ?? "";
            string prefabName = @params.Value<string>("prefab_name") ?? "";

            // Parse placement mode
            EffectPlacementMode placementMode = EffectPlacementMode.Auto;
            string placementStr = @params.Value<string>("placement_mode");
            if (!string.IsNullOrWhiteSpace(placementStr))
            {
                if (Enum.TryParse(placementStr, true, out EffectPlacementMode parsed))
                {
                    placementMode = parsed;
                }
            }

            // Parse target type
            EffectTargetType targetType = EffectTargetType.Auto;
            string targetStr = @params.Value<string>("target_type");
            if (!string.IsNullOrWhiteSpace(targetStr))
            {
                if (Enum.TryParse(targetStr, true, out EffectTargetType parsed))
                {
                    targetType = parsed;
                }
            }

            float defaultScale = @params.Value<float?>("default_scale") ?? 1f;
            float defaultDuration = @params.Value<float?>("default_duration") ?? 4f;
            bool enableDamage = @params.Value<bool?>("enable_damage") ?? false;
            float damageAmount = @params.Value<float?>("damage_amount") ?? 10f;

            // Parse alternative tags
            string[] altTags = null;
            string altTagsStr = @params.Value<string>("alternative_tags");
            if (!string.IsNullOrWhiteSpace(altTagsStr))
            {
                altTags = altTagsStr.Split(',')
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .ToArray();
            }

            var (definition, assetPath, warnings) = EffectDefinitionFactory.Create(
                effectTag, description, prefabName,
                placementMode, targetType,
                defaultScale, defaultDuration,
                null, altTags,
                enableDamage, damageAmount,
                registerInCatalog: true);

            return new SuccessResponse(
                $"EffectDefinition '{effectTag}' created at {assetPath}.",
                new
                {
                    effect_tag = effectTag,
                    asset_path = assetPath,
                    has_prefab = definition.effectPrefab != null,
                    prefab_name = definition.effectPrefab != null ? definition.effectPrefab.name : null,
                    placement_mode = placementMode.ToString(),
                    target_type = targetType.ToString(),
                    warnings = warnings,
                });
        }
    }

    [McpForUnityTool(
        "ng_test_effect_tag",
        Description = "Validate an effect tag against the catalog and spawn it in-scene. PlayMode: networked dispatch. EditMode: sandbox preview. Params: effect_tag (required), spawn_preview (bool, default true)."
    )]
    public static class TestEffectTagTool
    {
        public static object HandleCommand(JObject @params)
        {
            string effectTag = @params?.Value<string>("effect_tag");
            if (string.IsNullOrWhiteSpace(effectTag))
            {
                return new ErrorResponse("effect_tag is required.");
            }

            bool spawnPreview = @params?.Value<bool?>("spawn_preview") ?? true;

            // Validate against catalog
            var (valid, resolvedTag, issues) = EffectValidationService.ValidateTag(effectTag);

            var result = new Dictionary<string, object>
            {
                ["input_tag"] = effectTag,
                ["valid"] = valid,
                ["resolved_tag"] = resolvedTag,
                ["validation_issues"] = issues,
                ["preview_spawned"] = false,
            };

            if (valid && spawnPreview)
            {
                string fullTag = $"[EFFECT: {resolvedTag}]";
                GameObject preview = EffectSandboxRunner.PreviewEffect(fullTag);
                if (preview != null)
                {
                    result["preview_spawned"] = true;
                    result["preview_position"] = new float[]
                    {
                        preview.transform.position.x,
                        preview.transform.position.y,
                        preview.transform.position.z,
                    };
                    result["preview_name"] = preview.name;

                    // Auto-destroy after 8 seconds
                    UnityEngine.Object.Destroy(preview, 8f);
                }
            }

            string status = valid ? "Effect tag is valid." : "Effect tag validation failed.";
            if (valid && (bool)result["preview_spawned"])
            {
                status += " Preview spawned in scene.";
            }

            return new SuccessResponse(status, result);
        }
    }

    [McpForUnityTool(
        "ng_catalog_summary",
        Description = "Get the full EffectCatalog as structured data: all tags, descriptions, prefab status, placement modes, and the LLM-ready prompt string."
    )]
    public static class CatalogSummaryTool
    {
        public static object HandleCommand(JObject @params)
        {
            var catalog = EffectCatalog.Load();
            if (catalog == null)
            {
                return new ErrorResponse("EffectCatalog not found. Create one first.");
            }

            var effects = new List<Dictionary<string, object>>();
            if (catalog.allEffects != null)
            {
                for (int i = 0; i < catalog.allEffects.Count; i++)
                {
                    var effect = catalog.allEffects[i];
                    if (effect == null)
                    {
                        continue;
                    }

                    effects.Add(new Dictionary<string, object>
                    {
                        ["tag"] = effect.effectTag ?? "",
                        ["description"] = effect.description ?? "",
                        ["has_prefab"] = effect.effectPrefab != null,
                        ["prefab_name"] = effect.effectPrefab != null ? effect.effectPrefab.name : null,
                        ["placement_mode"] = effect.placementMode.ToString(),
                        ["target_type"] = effect.targetType.ToString(),
                        ["default_scale"] = effect.defaultScale,
                        ["default_duration"] = effect.defaultDuration,
                        ["allow_custom_scale"] = effect.allowCustomScale,
                        ["allow_custom_duration"] = effect.allowCustomDuration,
                        ["allow_custom_color"] = effect.allowCustomColor,
                        ["enable_damage"] = effect.enableGameplayDamage,
                        ["alternative_tags"] = effect.alternativeTags ?? new string[0],
                    });
                }
            }

            return new SuccessResponse(
                $"Catalog loaded with {effects.Count} effects.",
                new
                {
                    total_effects = effects.Count,
                    effects = effects,
                    has_fallback = catalog.fallbackEffectPrefab != null,
                    allow_unknown_tags = catalog.allowUnknownTags,
                    llm_prompt_catalog = catalog.GetPromptCatalog(),
                });
        }
    }

    [McpForUnityTool(
        "ng_simulate_llm_response",
        Description = "Parse a simulated LLM response through the full effect pipeline: extract [EFFECT:] tags, validate against catalog, report intents and parameters. Params: response_text (required), spawn_effects (bool, default false)."
    )]
    public static class SimulateLlmResponseTool
    {
        public static object HandleCommand(JObject @params)
        {
            string responseText = @params?.Value<string>("response_text");
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return new ErrorResponse("response_text is required.");
            }

            bool spawnEffects = @params?.Value<bool?>("spawn_effects") ?? false;

            var catalog = EffectCatalog.Load();
            var intents = EffectParser.ExtractIntents(responseText, catalog, false);
            string strippedText = EffectParser.StripTags(responseText);

            var intentData = new List<Dictionary<string, object>>();
            int validCount = 0;
            int invalidCount = 0;

            foreach (var intent in intents)
            {
                var intentDict = new Dictionary<string, object>
                {
                    ["raw_tag"] = intent.rawTagName ?? "",
                    ["valid"] = intent.isValid,
                    ["resolved_tag"] = intent.definition?.effectTag,
                    ["scale"] = intent.GetEffectiveScale(),
                    ["duration"] = intent.GetEffectiveDuration(),
                    ["target"] = intent.target,
                    ["anchor"] = intent.anchor,
                    ["placement_type"] = intent.placementType,
                    ["intensity"] = intent.intensity,
                    ["has_prefab"] = intent.definition?.effectPrefab != null,
                };

                if (intent.color != Color.white && intent.color != default)
                {
                    intentDict["color"] = "#" + ColorUtility.ToHtmlStringRGB(intent.color);
                }

                if (intent.isValid)
                {
                    validCount++;
                }
                else
                {
                    invalidCount++;
                }

                intentData.Add(intentDict);
            }

            int spawnedCount = 0;
            if (spawnEffects && validCount > 0)
            {
                Vector3 spawnPos = Vector3.up * 2f;
                var sceneCam = SceneView.lastActiveSceneView?.camera;
                if (sceneCam != null)
                {
                    spawnPos = sceneCam.transform.position + sceneCam.transform.forward * 3f;
                }

                spawnedCount = EffectSandboxRunner.RunLocalTest(responseText, spawnPos);
            }

            return new SuccessResponse(
                $"Parsed {intents.Count} effect tags ({validCount} valid, {invalidCount} invalid).",
                new
                {
                    total_tags_found = intents.Count,
                    valid_count = validCount,
                    invalid_count = invalidCount,
                    stripped_text = strippedText,
                    intents = intentData,
                    effects_spawned = spawnedCount,
                });
        }
    }

    [McpForUnityTool(
        "ng_bulk_validate_effects",
        Description = "Validate all EffectDefinitions in the catalog and all NPC profile powers: missing prefabs, duplicate tags, unregistered effects. Full health report in one call."
    )]
    public static class BulkValidateEffectsTool
    {
        public static object HandleCommand(JObject @params)
        {
            var report = EffectValidationService.ValidateAll();
            var result = EffectValidationService.ReportToDict(report);

            string health = report.Issues.Count == 0
                ? "All effects are healthy."
                : $"Found {report.Issues.Count} issue(s) across {report.TotalEffects} effects and {report.TotalProfiles} profiles.";

            return new SuccessResponse(health, result);
        }
    }
}
