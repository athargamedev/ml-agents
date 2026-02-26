using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor
{
    /// <summary>
    /// Batch-assigns LODGroup components to selected GameObjects.
    /// Uses LOD culling (LOD0 = full mesh, then culled at threshold).
    /// </summary>
    public static class LODGroupAutoSetup
    {
        public enum LODPreset
        {
            Character,
            Environment,
            SmallProp,
            VFX,
        }

        [MenuItem("Tools/Performance/Setup LOD Groups - Character")]
        private static void SetupCharacter() => ApplyToSelection(LODPreset.Character);

        [MenuItem("Tools/Performance/Setup LOD Groups - Environment")]
        private static void SetupEnvironment() => ApplyToSelection(LODPreset.Environment);

        [MenuItem("Tools/Performance/Setup LOD Groups - SmallProp")]
        private static void SetupSmallProp() => ApplyToSelection(LODPreset.SmallProp);

        [MenuItem("Tools/Performance/Setup LOD Groups - VFX")]
        private static void SetupVFX() => ApplyToSelection(LODPreset.VFX);

        private static void ApplyToSelection(LODPreset preset)
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "LOD Setup",
                    "Select one or more GameObjects first.",
                    "OK"
                );
                return;
            }

            int count = 0;
            Undo.SetCurrentGroupName($"Setup LOD Groups ({preset})");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (GameObject go in selection)
            {
                if (ApplyLODGroup(go, preset))
                    count++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[LODGroupAutoSetup] Applied {preset} LOD preset to {count} objects.");
        }

        private static bool ApplyLODGroup(GameObject go, LODPreset preset)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return false;

            LODGroup lodGroup = go.GetComponent<LODGroup>();
            if (lodGroup == null)
            {
                Undo.AddComponent<LODGroup>(go);
                lodGroup = go.GetComponent<LODGroup>();
            }
            else
            {
                Undo.RecordObject(lodGroup, "Modify LODGroup");
            }

            GetThresholds(preset, out float lod0Screen, out float cullScreen);

            LOD[] lods = new LOD[] { new LOD(cullScreen, renderers) };

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();

            // Mark prefab dirty if applicable
            EditorUtility.SetDirty(go);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.RecordPrefabInstancePropertyModifications(go);

            return true;
        }

        private static void GetThresholds(
            LODPreset preset,
            out float lod0Screen,
            out float cullScreen
        )
        {
            switch (preset)
            {
                case LODPreset.Character:
                    lod0Screen = 0.50f;
                    cullScreen = 0.02f;
                    break;
                case LODPreset.Environment:
                    lod0Screen = 0.30f;
                    cullScreen = 0.01f;
                    break;
                case LODPreset.SmallProp:
                    lod0Screen = 0.20f;
                    cullScreen = 0.03f;
                    break;
                case LODPreset.VFX:
                    lod0Screen = 0.40f;
                    cullScreen = 0.05f;
                    break;
                default:
                    lod0Screen = 0.25f;
                    cullScreen = 0.02f;
                    break;
            }
        }
    }
}
