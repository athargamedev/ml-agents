using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor
{
    /// <summary>
    /// Prevents Inspector null-target errors after runtime objects are destroyed while selected.
    /// </summary>
    [InitializeOnLoad]
    public static class InspectorSelectionGuard
    {
        static InspectorSelectionGuard()
        {
            Selection.selectionChanged += QueueSanitizeSelection;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += SanitizeSelection;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (
                state == PlayModeStateChange.EnteredPlayMode
                || state == PlayModeStateChange.ExitingPlayMode
                || state == PlayModeStateChange.EnteredEditMode
            )
            {
                QueueSanitizeSelection();
            }
        }

        private static void QueueSanitizeSelection()
        {
            EditorApplication.delayCall -= SanitizeSelection;
            EditorApplication.delayCall += SanitizeSelection;
        }

        private static void SanitizeSelection()
        {
            UnityEngine.Object[] currentSelection = Selection.objects;
            if (currentSelection == null || currentSelection.Length == 0)
            {
                return;
            }

            UnityEngine.Object[] validSelection = currentSelection
                .Where(obj => obj != null)
                .ToArray();
            if (validSelection.Length == currentSelection.Length)
            {
                return;
            }

            if (validSelection.Length == 0)
            {
                Selection.activeObject = null;
            }
            else
            {
                Selection.objects = validSelection;
            }

            Debug.Log(
                $"[InspectorSelectionGuard] Removed {currentSelection.Length - validSelection.Length} stale selection target(s)."
            );
        }
    }
}
