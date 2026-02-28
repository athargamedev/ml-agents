using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Dialogue.Editor
{
    /// <summary>
    /// Small editor launcher for the repo-level NPC dialogue training batch file.
    /// Opens the batch in a separate terminal window so training can continue
    /// after the Unity editor call returns.
    /// </summary>
    public sealed class NpcDialogueTrainingLauncherWindow : EditorWindow
    {
        private const string MenuPath = "Network Game/MCP/Npc Dialogue Training Launcher";
        private const string WindowTitle = "NPC Training";

        private string m_RunId = string.Empty;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<NpcDialogueTrainingLauncherWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(420f, 180f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("NPC Dialogue Training", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Launches train_npc_dialogue.bat in a separate terminal window. "
                    + "Leave Run ID empty to use the batch file's auto-generated ID.",
                MessageType.Info
            );

            using (new EditorGUILayout.VerticalScope("box"))
            {
                m_RunId = EditorGUILayout.TextField("Optional Run ID", m_RunId ?? string.Empty);
            }

            EditorGUILayout.Space(8f);

            if (GUILayout.Button("Start Training", GUILayout.Height(34f)))
            {
                StartTraining();
            }

            if (GUILayout.Button("Reveal Launcher Script", GUILayout.Height(24f)))
            {
                RevealLauncherScript();
            }
        }

        private void StartTraining()
        {
            if (!TryGetLauncherPaths(out string repoRoot, out string batchPath, out string error))
            {
                EditorUtility.DisplayDialog("NPC Training", error, "OK");
                return;
            }

            string runId = SanitizeRunId(m_RunId);
            if (!string.IsNullOrEmpty(m_RunId) && string.IsNullOrEmpty(runId))
            {
                EditorUtility.DisplayDialog(
                    "NPC Training",
                    "Run ID contains unsupported characters. Use only letters, numbers, dash, underscore, or period.",
                    "OK"
                );
                return;
            }

            string arguments = string.IsNullOrEmpty(runId)
                ? $"/c start \"NpcDialogueTraining\" \"{batchPath}\""
                : $"/c start \"NpcDialogueTraining\" \"{batchPath}\" \"{runId}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                Process.Start(startInfo);
                UnityEngine.Debug.Log(
                    $"[NpcDialogueTrainingLauncher] Started training launcher: {Path.GetFileName(batchPath)}"
                        + (string.IsNullOrEmpty(runId) ? " (auto run ID)." : $" (runId={runId}).")
                );
            }
            catch (System.SystemException ex)
            {
                EditorUtility.DisplayDialog(
                    "NPC Training",
                    $"Failed to start training launcher.\n\n{ex.Message}",
                    "OK"
                );
            }
        }

        private void RevealLauncherScript()
        {
            if (!TryGetLauncherPaths(out _, out string batchPath, out string error))
            {
                EditorUtility.DisplayDialog("NPC Training", error, "OK");
                return;
            }

            EditorUtility.RevealInFinder(batchPath);
        }

        private static bool TryGetLauncherPaths(
            out string repoRoot,
            out string batchPath,
            out string error
        )
        {
            repoRoot = string.Empty;
            batchPath = string.Empty;
            error = string.Empty;

            DirectoryInfo assetsDir = new DirectoryInfo(Application.dataPath);
            DirectoryInfo projectRoot = assetsDir.Parent;
            DirectoryInfo repoRootDir = projectRoot != null ? projectRoot.Parent : null;
            if (repoRootDir == null)
            {
                error = "Could not resolve the repository root from Application.dataPath.";
                return false;
            }

            repoRoot = repoRootDir.FullName;
            batchPath = Path.Combine(repoRoot, "train_npc_dialogue.bat");
            if (!File.Exists(batchPath))
            {
                error = $"Batch file not found:\n{batchPath}";
                return false;
            }

            return true;
        }

        private static string SanitizeRunId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool allowed =
                    char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
                if (!allowed)
                {
                    return string.Empty;
                }
            }

            return trimmed;
        }
    }
}
