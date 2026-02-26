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
        "ng_pipeline_status",
        Description = "Get complete dialogue pipeline status in one call: LLM state, queue, stats, catalog health, active NPCs, and errors. Use this instead of multiple separate queries."
    )]
    public static class PipelineStatusTool
    {
        public static object HandleCommand(JObject @params)
        {
            var snapshot = DialoguePipelineInspector.GetPipelineSnapshot();
            return new SuccessResponse("Pipeline status retrieved.", snapshot);
        }
    }

    [McpForUnityTool(
        "ng_get_full_diagnostics",
        Description = "Complete dialogue system diagnostic dump: pipeline stats, LLM health, catalog validation, NPC profiles, conversation keys, effect types — all in one call."
    )]
    public static class FullDiagnosticsTool
    {
        public static object HandleCommand(JObject @params)
        {
            var result = new Dictionary<string, object>();

            // Pipeline snapshot
            result["pipeline"] = DialoguePipelineInspector.GetPipelineSnapshot();

            // Full validation
            var validationReport = EffectValidationService.ValidateAll();
            result["validation"] = EffectValidationService.ReportToDict(validationReport);

            // All profiles summary
            var profiles = DialogueMCPBridge.GetProfiles();
            result["profiles"] = profiles ?? new List<Dictionary<string, object>>();

            // Effect types
            result["effect_types"] = DialogueSceneEffectsController.GetAvailableEffects();

            // Conversation keys (Play Mode only)
            if (EditorApplication.isPlaying)
            {
                var service = NetworkDialogueService.Instance;
                if (service != null)
                {
                    result["conversation_keys"] = service.GetConversationKeys();
                }
                else
                {
                    result["conversation_keys"] = Array.Empty<string>();
                }
            }
            else
            {
                result["conversation_keys"] = Array.Empty<string>();
            }

            // Catalog prompt string
            var catalog = EffectCatalog.Load();
            result["catalog_prompt_string"] = catalog != null
                ? catalog.GetPromptCatalog()
                : "No catalog loaded.";

            return new SuccessResponse("Full diagnostics retrieved.", result);
        }
    }
}
