using Network_Game.Dialogue;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor
{
    internal static class DialogueEffectFeedbackPromptToggleMenu
    {
        private const string kDisableMenuPath = "Network Game/MCP/Disable Effect Feedback Prompt";
        private const string kEnableMenuPath = "Network Game/MCP/Enable Effect Feedback Prompt";
        private const string kToggleMenuPath = "Network Game/MCP/Toggle Effect Feedback Prompt";

        [MenuItem(kDisableMenuPath)]
        private static void DisablePrompt()
        {
            SetPromptEnabled(false);
        }

        [MenuItem(kEnableMenuPath)]
        private static void EnablePrompt()
        {
            SetPromptEnabled(true);
        }

        [MenuItem(kToggleMenuPath)]
        private static void TogglePrompt()
        {
            DialogueEffectFeedbackPrompt prompt = FindPromptInstance();
            if (prompt == null)
            {
                Debug.LogWarning(
                    "[DialogueFX] Effect feedback prompt instance not found. Start Play Mode and trigger an effect first."
                );
                return;
            }

            SetPromptEnabled(!prompt.enabled);
        }

        private static void SetPromptEnabled(bool enabled)
        {
            DialogueEffectFeedbackPrompt prompt = FindPromptInstance();
            if (prompt == null)
            {
                Debug.LogWarning(
                    "[DialogueFX] Effect feedback prompt instance not found. Start Play Mode and trigger an effect first."
                );
                return;
            }

            if (prompt.enabled == enabled)
            {
                Debug.Log(
                    $"[DialogueFX] Effect feedback prompt already {(enabled ? "enabled" : "disabled")}."
                );
                return;
            }

            Undo.RecordObject(prompt, "Toggle Effect Feedback Prompt");
            prompt.enabled = enabled;
            EditorUtility.SetDirty(prompt);

            Debug.Log(
                $"[DialogueFX] Effect feedback prompt {(enabled ? "enabled" : "disabled")} for current Play Mode session."
            );
        }

        private static DialogueEffectFeedbackPrompt FindPromptInstance()
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
    }
}
