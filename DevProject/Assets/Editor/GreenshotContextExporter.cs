using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class GreenshotContextExporter
{
    private static string ContextFilePath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "unity_state.json");

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        Selection.selectionChanged -= ExportContext;
        Selection.selectionChanged += ExportContext;
    }

    // Comply with UDR0001/Static field best practices for Unity 2019.3+ Domain Reloads
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        Selection.selectionChanged -= ExportContext;
    }

    [System.Serializable]
    private struct UnityContextData
    {
        public string timestamp;
        public string scene_name;
        public string selected_object;
        public string[] components;
    }

    [MenuItem("Tools/Greenshot/Force Export Context")]
    public static void ExportContext()
    {
        var selectedObject = Selection.activeGameObject;

        UnityContextData data = new UnityContextData
        {
            timestamp = System.DateTime.Now.ToString("o"),
            scene_name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
        };

        if (selectedObject != null)
        {
            data.selected_object = selectedObject.name;
            data.components = selectedObject
                .GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .ToArray();
        }
        else
        {
            data.selected_object = "None";
            data.components = new string[0];
        }

        string json = JsonUtility.ToJson(data, true);
        try
        {
            File.WriteAllText(ContextFilePath, json);
        }
        catch
        {
            // Fail silently if file is locked
        }
    }
}
