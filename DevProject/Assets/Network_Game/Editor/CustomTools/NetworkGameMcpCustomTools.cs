using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Network_Game.Dialogue;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools
{
    [McpForUnityTool(
        "ng_effects_test_mode",
        Description = "Composite workflow tool for multiplayer effects testing: disables feedback prompt, starts/stops direct effect suite, and reports session status."
    )]
    public static class EffectsTestModeTool
    {
        public class Parameters
        {
            [ToolParameter("Action to run: start, stop, status, spawn_once", Required = false)]
            public string action { get; set; }

            [ToolParameter("Disable feedback prompt while testing (default true for start/spawn_once)", Required = false)]
            public bool? disable_feedback_prompt { get; set; }

            [ToolParameter("Start direct all-effects suite on start (default true)", Required = false)]
            public bool? start_suite { get; set; }

            [ToolParameter("Stop direct all-effects suite on stop (default true)", Required = false)]
            public bool? stop_suite { get; set; }

            [ToolParameter("Restore prompt state captured at start when stopping (default true)", Required = false)]
            public bool? restore_prompt_on_stop { get; set; }
        }

        private const string StartSuiteMenuPath = "Network Game/MCP/Auto Test All Effects Direct (No LLM)";
        private const string StopSuiteMenuPath = "Network Game/MCP/Stop Direct Auto Test All Effects";
        private const string SpawnSingleMenuPath = "Network Game/MCP/Spawn Direct NPC Power (No LLM)";

        public static object HandleCommand(JObject @params)
        {
            Parameters p = @params?.ToObject<Parameters>() ?? new Parameters();
            string action = string.IsNullOrWhiteSpace(p.action) ? "status" : p.action.Trim().ToLowerInvariant();

            switch (action)
            {
                case "start":
                    return Start(p);
                case "stop":
                    return Stop(p);
                case "spawn_once":
                    return SpawnOnce(p);
                case "status":
                    return new SuccessResponse("Effects test mode status.", EffectsTestModeSession.BuildStatusData());
                default:
                    return new ErrorResponse(
                        $"Unsupported action '{action}'. Expected: start, stop, status, spawn_once."
                    );
            }
        }

        private static object Start(Parameters p)
        {
            if (!EditorApplication.isPlaying)
            {
                return new ErrorResponse(
                    "Unity must be in Play Mode to start ng_effects_test_mode."
                );
            }

            bool disablePrompt = p.disable_feedback_prompt ?? true;
            bool startSuite = p.start_suite ?? true;

            EffectsTestModeSession.State state = EffectsTestModeSession.Load();
            bool wasActive = state.IsActive;
            if (string.IsNullOrWhiteSpace(state.CorrelationId))
            {
                state.CorrelationId = Guid.NewGuid().ToString("N");
            }

            state.IsActive = true;
            state.LastAction = "start";
            state.LastStartedUtc = DateTime.UtcNow.ToString("o");
            state.RunCount = Mathf.Max(0, state.RunCount) + 1;

            object promptResult = null;
            if (disablePrompt)
            {
                PromptToolCommon.CapturePromptStateIfAvailable(ref state);
                promptResult = PromptToolCommon.TrySetPromptEnabledForWorkflow(false);
            }

            object suiteResult = null;
            if (startSuite)
            {
                suiteResult = PromptToolCommon.ExecuteMenu(StartSuiteMenuPath);
            }

            EffectsTestModeSession.Save(state);

            return new SuccessResponse(
                wasActive
                    ? "Effects test mode refreshed."
                    : "Effects test mode started.",
                new
                {
                    result = wasActive ? "updated" : "started",
                    session = EffectsTestModeSession.BuildStatusData(),
                    prompt = PromptToolCommon.CoerceResponse(promptResult),
                    suite = PromptToolCommon.CoerceResponse(suiteResult),
                }
            );
        }

        private static object Stop(Parameters p)
        {
            bool stopSuite = p.stop_suite ?? true;
            bool restorePrompt = p.restore_prompt_on_stop ?? true;

            EffectsTestModeSession.State state = EffectsTestModeSession.Load();
            bool wasActive = state.IsActive;

            object suiteResult = null;
            if (stopSuite)
            {
                suiteResult = PromptToolCommon.ExecuteMenu(StopSuiteMenuPath);
            }

            object promptResult = null;
            if (restorePrompt)
            {
                promptResult = PromptToolCommon.RestoreCapturedPromptState(ref state);
            }

            state.IsActive = false;
            state.LastAction = "stop";
            EffectsTestModeSession.Save(state);

            return new SuccessResponse(
                wasActive
                    ? "Effects test mode stopped."
                    : "Effects test mode was not active (applied stop actions anyway).",
                new
                {
                    result = wasActive ? "stopped" : "no_op",
                    session = EffectsTestModeSession.BuildStatusData(),
                    prompt = PromptToolCommon.CoerceResponse(promptResult),
                    suite = PromptToolCommon.CoerceResponse(suiteResult),
                }
            );
        }

        private static object SpawnOnce(Parameters p)
        {
            if (!EditorApplication.isPlaying)
            {
                return new ErrorResponse(
                    "Unity must be in Play Mode to spawn effects with ng_effects_test_mode."
                );
            }

            bool disablePrompt = p.disable_feedback_prompt ?? true;
            EffectsTestModeSession.State state = EffectsTestModeSession.Load();
            object promptResult = null;
            if (disablePrompt)
            {
                PromptToolCommon.CapturePromptStateIfAvailable(ref state);
                promptResult = PromptToolCommon.TrySetPromptEnabledForWorkflow(false);
                EffectsTestModeSession.Save(state);
            }

            object spawnResult = PromptToolCommon.ExecuteMenu(SpawnSingleMenuPath);
            return new SuccessResponse(
                "Effects test mode spawn_once executed.",
                new
                {
                    session = EffectsTestModeSession.BuildStatusData(),
                    prompt = PromptToolCommon.CoerceResponse(promptResult),
                    spawn = PromptToolCommon.CoerceResponse(spawnResult),
                }
            );
        }
    }

    [McpForUnityTool(
        "ng_disable_effect_feedback_prompt",
        Description = "Disable the runtime Dialogue Effect Feedback prompt overlay in the current Play Mode session so gameplay effects remain visible."
    )]
    public static class DisableEffectFeedbackPromptTool
    {
        public static object HandleCommand(JObject @params)
        {
            return PromptToolCommon.SetPromptEnabled(false);
        }
    }

    [McpForUnityTool(
        "ng_enable_effect_feedback_prompt",
        Description = "Enable the runtime Dialogue Effect Feedback prompt overlay in the current Play Mode session."
    )]
    public static class EnableEffectFeedbackPromptTool
    {
        public static object HandleCommand(JObject @params)
        {
            return PromptToolCommon.SetPromptEnabled(true);
        }
    }

    [McpForUnityTool(
        "ng_toggle_effect_feedback_prompt",
        Description = "Toggle the runtime Dialogue Effect Feedback prompt overlay in the current Play Mode session."
    )]
    public static class ToggleEffectFeedbackPromptTool
    {
        public static object HandleCommand(JObject @params)
        {
            DialogueEffectFeedbackPrompt prompt = PromptToolCommon.FindPromptInstance();
            if (prompt == null)
            {
                return PromptToolCommon.MissingPrompt();
            }

            return PromptToolCommon.SetPromptEnabled(!prompt.enabled);
        }
    }

    [McpForUnityTool(
        "ng_get_effect_feedback_prompt_status",
        Description = "Get current runtime status of the Dialogue Effect Feedback prompt instance in Play Mode."
    )]
    public static class GetEffectFeedbackPromptStatusTool
    {
        public static object HandleCommand(JObject @params)
        {
            DialogueEffectFeedbackPrompt prompt = PromptToolCommon.FindPromptInstance();
            if (prompt == null)
            {
                return new SuccessResponse(
                    "Effect feedback prompt instance not found.",
                    new
                    {
                        found = false,
                        isPlaying = EditorApplication.isPlaying,
                    }
                );
            }

            return new SuccessResponse(
                "Effect feedback prompt status retrieved.",
                new
                {
                    found = true,
                    enabled = prompt.enabled,
                    activeInHierarchy = prompt.gameObject.activeInHierarchy,
                    gameObjectName = prompt.gameObject.name,
                    scene = prompt.gameObject.scene.name,
                    isPlaying = EditorApplication.isPlaying,
                }
            );
        }
    }

    [McpForUnityTool(
        "ng_run_all_effects_direct",
        Description = "Start direct all-effects automation (no LLM) using the Network Game MCP test menu."
    )]
    public static class RunAllEffectsDirectTool
    {
        private const string MenuPath = "Network Game/MCP/Auto Test All Effects Direct (No LLM)";

        public static object HandleCommand(JObject @params)
        {
            return PromptToolCommon.ExecuteMenu(MenuPath);
        }
    }

    [McpForUnityTool(
        "ng_stop_all_effects_direct",
        Description = "Stop the direct all-effects automation (no LLM) using the Network Game MCP test menu."
    )]
    public static class StopAllEffectsDirectTool
    {
        private const string MenuPath = "Network Game/MCP/Stop Direct Auto Test All Effects";

        public static object HandleCommand(JObject @params)
        {
            return PromptToolCommon.ExecuteMenu(MenuPath);
        }
    }

    [McpForUnityTool(
        "ng_spawn_direct_npc_power",
        Description = "Spawn one direct NPC prefab power effect (no LLM) using the Network Game MCP test menu."
    )]
    public static class SpawnDirectNpcPowerTool
    {
        private const string MenuPath = "Network Game/MCP/Spawn Direct NPC Power (No LLM)";

        public static object HandleCommand(JObject @params)
        {
            return PromptToolCommon.ExecuteMenu(MenuPath);
        }
    }

    internal static class PromptToolCommon
    {
        public static object SetPromptEnabled(bool enabled)
        {
            if (!EditorApplication.isPlaying)
            {
                return new ErrorResponse(
                    "Unity must be in Play Mode to control DialogueEffectFeedbackPrompt."
                );
            }

            DialogueEffectFeedbackPrompt prompt = FindPromptInstance();
            if (prompt == null)
            {
                return MissingPrompt();
            }

            if (prompt.enabled == enabled)
            {
                return new SuccessResponse(
                    $"Effect feedback prompt already {(enabled ? "enabled" : "disabled")}.",
                    new { enabled, gameObjectName = prompt.gameObject.name }
                );
            }

            Undo.RecordObject(prompt, "Toggle Dialogue Effect Feedback Prompt");
            prompt.enabled = enabled;
            EditorUtility.SetDirty(prompt);

            return new SuccessResponse(
                $"Effect feedback prompt {(enabled ? "enabled" : "disabled")}.",
                new
                {
                    enabled,
                    gameObjectName = prompt.gameObject.name,
                    scene = prompt.gameObject.scene.name,
                }
            );
        }

        public static object TrySetPromptEnabledForWorkflow(bool enabled)
        {
            if (!EditorApplication.isPlaying)
            {
                return new
                {
                    success = false,
                    code = "not_playing",
                    message = "Unity is not in Play Mode.",
                };
            }

            DialogueEffectFeedbackPrompt prompt = FindPromptInstance();
            if (prompt == null)
            {
                return new
                {
                    success = false,
                    code = "prompt_not_found",
                    message = "DialogueEffectFeedbackPrompt instance not found.",
                };
            }

            if (prompt.enabled == enabled)
            {
                return new
                {
                    success = true,
                    result = "no_op",
                    enabled,
                    gameObjectName = prompt.gameObject.name,
                };
            }

            Undo.RecordObject(prompt, "Toggle Dialogue Effect Feedback Prompt");
            prompt.enabled = enabled;
            EditorUtility.SetDirty(prompt);

            return new
            {
                success = true,
                result = "updated",
                enabled,
                gameObjectName = prompt.gameObject.name,
                scene = prompt.gameObject.scene.name,
            };
        }

        public static DialogueEffectFeedbackPrompt FindPromptInstance()
        {
            DialogueEffectFeedbackPrompt[] prompts =
                Resources.FindObjectsOfTypeAll<DialogueEffectFeedbackPrompt>();
            if (prompts == null || prompts.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < prompts.Length; i++)
            {
                DialogueEffectFeedbackPrompt prompt = prompts[i];
                if (prompt == null)
                {
                    continue;
                }

                if (!prompt.gameObject.scene.IsValid())
                {
                    continue;
                }

                return prompt;
            }

            return null;
        }

        public static ErrorResponse MissingPrompt()
        {
            return new ErrorResponse(
                "DialogueEffectFeedbackPrompt instance not found. Start Play Mode and trigger an effect first."
            );
        }

        public static void CapturePromptStateIfAvailable(ref EffectsTestModeSession.State state)
        {
            DialogueEffectFeedbackPrompt prompt = FindPromptInstance();
            if (prompt == null)
            {
                return;
            }

            state.HasPromptStateSnapshot = true;
            state.PromptEnabledBeforeStart = prompt.enabled;
            state.PromptObjectName = prompt.gameObject != null ? prompt.gameObject.name : string.Empty;
        }

        public static object RestoreCapturedPromptState(ref EffectsTestModeSession.State state)
        {
            if (!state.HasPromptStateSnapshot)
            {
                return new
                {
                    success = true,
                    result = "no_op",
                    message = "No captured prompt state to restore.",
                };
            }

            object result = TrySetPromptEnabledForWorkflow(state.PromptEnabledBeforeStart);
            state.HasPromptStateSnapshot = false;
            state.PromptObjectName = string.Empty;
            return result;
        }

        public static object CoerceResponse(object response)
        {
            if (response == null)
            {
                return null;
            }

            if (response is JObject jo)
            {
                return jo;
            }

            return JObject.FromObject(response);
        }

        public static object ExecuteMenu(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath))
            {
                return new ErrorResponse("Menu path is required.");
            }

            try
            {
                bool ok = EditorApplication.ExecuteMenuItem(menuPath);
                if (!ok)
                {
                    return new ErrorResponse(
                        $"Failed to execute menu item '{menuPath}'. It may be unavailable or disabled."
                    );
                }

                return new SuccessResponse(
                    $"Executed menu item '{menuPath}'.",
                    new { menuPath }
                );
            }
            catch (Exception ex)
            {
                return new ErrorResponse(
                    $"Error executing menu item '{menuPath}': {ex.Message}"
                );
            }
        }
    }

    internal static class EffectsTestModeSession
    {
        private const string KeyPrefix = "Network_Game.MCP.EffectsTestMode.";
        private const string KeyIsActive = KeyPrefix + "IsActive";
        private const string KeyCorrelationId = KeyPrefix + "CorrelationId";
        private const string KeyLastAction = KeyPrefix + "LastAction";
        private const string KeyLastStartedUtc = KeyPrefix + "LastStartedUtc";
        private const string KeyRunCount = KeyPrefix + "RunCount";
        private const string KeyHasPromptStateSnapshot = KeyPrefix + "HasPromptStateSnapshot";
        private const string KeyPromptEnabledBeforeStart = KeyPrefix + "PromptEnabledBeforeStart";
        private const string KeyPromptObjectName = KeyPrefix + "PromptObjectName";

        internal struct State
        {
            public bool IsActive;
            public string CorrelationId;
            public string LastAction;
            public string LastStartedUtc;
            public int RunCount;
            public bool HasPromptStateSnapshot;
            public bool PromptEnabledBeforeStart;
            public string PromptObjectName;
        }

        internal static State Load()
        {
            return new State
            {
                IsActive = SessionState.GetBool(KeyIsActive, false),
                CorrelationId = SessionState.GetString(KeyCorrelationId, string.Empty),
                LastAction = SessionState.GetString(KeyLastAction, string.Empty),
                LastStartedUtc = SessionState.GetString(KeyLastStartedUtc, string.Empty),
                RunCount = SessionState.GetInt(KeyRunCount, 0),
                HasPromptStateSnapshot = SessionState.GetBool(KeyHasPromptStateSnapshot, false),
                PromptEnabledBeforeStart = SessionState.GetBool(KeyPromptEnabledBeforeStart, true),
                PromptObjectName = SessionState.GetString(KeyPromptObjectName, string.Empty),
            };
        }

        internal static void Save(State state)
        {
            SessionState.SetBool(KeyIsActive, state.IsActive);
            SessionState.SetString(KeyCorrelationId, state.CorrelationId ?? string.Empty);
            SessionState.SetString(KeyLastAction, state.LastAction ?? string.Empty);
            SessionState.SetString(KeyLastStartedUtc, state.LastStartedUtc ?? string.Empty);
            SessionState.SetInt(KeyRunCount, Mathf.Max(0, state.RunCount));
            SessionState.SetBool(KeyHasPromptStateSnapshot, state.HasPromptStateSnapshot);
            SessionState.SetBool(KeyPromptEnabledBeforeStart, state.PromptEnabledBeforeStart);
            SessionState.SetString(KeyPromptObjectName, state.PromptObjectName ?? string.Empty);
        }

        internal static object BuildStatusData()
        {
            State state = Load();
            DialogueEffectFeedbackPrompt prompt = PromptToolCommon.FindPromptInstance();

            return new
            {
                active = state.IsActive,
                correlationId = string.IsNullOrWhiteSpace(state.CorrelationId)
                    ? null
                    : state.CorrelationId,
                lastAction = string.IsNullOrWhiteSpace(state.LastAction) ? null : state.LastAction,
                lastStartedUtc = string.IsNullOrWhiteSpace(state.LastStartedUtc)
                    ? null
                    : state.LastStartedUtc,
                runCount = Mathf.Max(0, state.RunCount),
                playMode = EditorApplication.isPlaying,
                capturedPromptState = new
                {
                    hasSnapshot = state.HasPromptStateSnapshot,
                    promptEnabledBeforeStart = state.PromptEnabledBeforeStart,
                    promptObjectName = string.IsNullOrWhiteSpace(state.PromptObjectName)
                        ? null
                        : state.PromptObjectName,
                },
                prompt = prompt == null
                    ? null
                    : new
                    {
                        found = true,
                        enabled = prompt.enabled,
                        activeInHierarchy = prompt.gameObject.activeInHierarchy,
                        gameObjectName = prompt.gameObject.name,
                        scene = prompt.gameObject.scene.name,
                    }
            };
        }
    }
}
