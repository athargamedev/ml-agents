using MCPForUnity.Editor.Helpers;
using Network_Game.Editor.CustomTools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor
{
    public sealed class NetworkGameMcpAutomationConsoleWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _disablePromptOnStart = true;
        private bool _startSuiteOnStart = true;
        private bool _stopSuiteOnStop = true;
        private bool _restorePromptOnStop = true;
        private string _lastResultJson = string.Empty;

        [MenuItem("Network Game/MCP/Multiplayer Automation Console")]
        public static void Open()
        {
            var window = GetWindow<NetworkGameMcpAutomationConsoleWindow>();
            window.titleContent = new GUIContent("NG MCP Automation");
            window.minSize = new Vector2(560f, 380f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshStatus();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Multiplayer Effects Test Mode", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Docs-style project custom tools for MPPM effect testing. Use Start to disable the feedback prompt and begin the direct all-effects suite.",
                MessageType.Info
            );

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Start/Stop Options", EditorStyles.boldLabel);
                _disablePromptOnStart = EditorGUILayout.ToggleLeft(
                    "Disable feedback prompt on start",
                    _disablePromptOnStart
                );
                _startSuiteOnStart = EditorGUILayout.ToggleLeft(
                    "Start direct all-effects suite",
                    _startSuiteOnStart
                );
                _stopSuiteOnStop = EditorGUILayout.ToggleLeft(
                    "Stop direct all-effects suite on stop",
                    _stopSuiteOnStop
                );
                _restorePromptOnStop = EditorGUILayout.ToggleLeft(
                    "Restore feedback prompt state on stop",
                    _restorePromptOnStop
                );
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Status", GUILayout.Height(28)))
                {
                    RefreshStatus();
                }

                if (GUILayout.Button("Start Test Mode", GUILayout.Height(28)))
                {
                    RunEffectsTestMode("start");
                }

                if (GUILayout.Button("Stop Test Mode", GUILayout.Height(28)))
                {
                    RunEffectsTestMode("stop");
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Spawn One NPC Power", GUILayout.Height(24)))
                {
                    RunEffectsTestMode("spawn_once");
                }

                if (GUILayout.Button("Disable Prompt", GUILayout.Height(24)))
                {
                    SetLastResult(DisableEffectFeedbackPromptTool.HandleCommand(new JObject()));
                }

                if (GUILayout.Button("Enable Prompt", GUILayout.Height(24)))
                {
                    SetLastResult(EnableEffectFeedbackPromptTool.HandleCommand(new JObject()));
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Last Tool Result", EditorStyles.boldLabel);

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                EditorGUILayout.TextArea(
                    string.IsNullOrWhiteSpace(_lastResultJson)
                    ? "(no result yet)"
                    : _lastResultJson,
                    GUILayout.ExpandHeight(true)
                );
            }
        }

        private void RefreshStatus()
        {
            SetLastResult(
                result: EffectsTestModeTool.HandleCommand(
                    new JObject { [propertyName: "action"] = "status" }
                )
            );
        }

        private void RunEffectsTestMode(string action)
        {
            var payload = new JObject { [propertyName: "action"] = action };

            if (action == "start")
            {
                payload[propertyName : "disable_feedback_prompt"] = _disablePromptOnStart;
                payload[propertyName : "start_suite"] = _startSuiteOnStart;
            }
            else if (action == "stop")
            {
                payload[propertyName : "stop_suite"] = _stopSuiteOnStop;
                payload[propertyName : "restore_prompt_on_stop"] = _restorePromptOnStop;
            }
            else if (action == "spawn_once")
            {
                payload[propertyName : "disable_feedback_prompt"] = _disablePromptOnStart;
            }

            SetLastResult(result: EffectsTestModeTool.HandleCommand(@params: payload));
        }

        private void SetLastResult(object result)
        {
            try
            {
                JObject jo =
                    result as JObject
                    ?? JObject.FromObject(
                        o: result ?? new ErrorResponse(messageOrCode: "null_result")
                    );
                _lastResultJson = jo.ToString();
            }
            catch (System.Exception ex)
            {
                _lastResultJson = $"Failed to render result: {ex.Message}";
            }

            Repaint();
        }
    }
}
