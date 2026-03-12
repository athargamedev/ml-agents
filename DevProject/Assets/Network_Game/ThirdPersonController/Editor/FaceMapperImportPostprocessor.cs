#if UNITY_EDITOR
using System;
using UnityEditor;

public sealed class FaceMapperImportPostprocessor : AssetPostprocessor
{
    private const string PendingSetupKey = "Network_Game.FaceMapper.PendingSetup";
    private const string ProcessingKey = "Network_Game.FaceMapper.IsProcessing";
    private const string BakedFolder = "Assets/FacePhotos/Baked/";

    private static readonly string[] WatchedModelAssets =
    {
        "Assets/Network_Game/ThirdPersonController/Character/Models/modelAndre/modelAndre.fbx",
        "Assets/Network_Game/ThirdPersonController/Character/Models/Ch33_nonPBR.fbx",
    };

    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths,
        bool didDomainReload
    )
    {
        if (SessionState.GetBool(ProcessingKey, false))
            return;

        if (!TouchesFaceMapperAssets(importedAssets) && !TouchesFaceMapperAssets(movedAssets))
            return;

        SessionState.SetBool(PendingSetupKey, true);
        EditorApplication.delayCall -= RunPendingSetup;
        EditorApplication.delayCall += RunPendingSetup;
    }

    [InitializeOnLoadMethod]
    private static void SchedulePendingSetupOnLoad()
    {
        if (!SessionState.GetBool(PendingSetupKey, false))
            return;

        EditorApplication.delayCall -= RunPendingSetup;
        EditorApplication.delayCall += RunPendingSetup;
    }

    private static bool TouchesFaceMapperAssets(string[] assetPaths)
    {
        if (assetPaths == null)
            return false;

        foreach (string assetPath in assetPaths)
            if (IsWatchedAsset(assetPath))
                return true;

        return false;
    }

    private static bool IsWatchedAsset(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        foreach (string modelAsset in WatchedModelAssets)
            if (assetPath.Equals(modelAsset, StringComparison.OrdinalIgnoreCase))
                return true;

        return assetPath.StartsWith(BakedFolder, StringComparison.OrdinalIgnoreCase)
            && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunPendingSetup()
    {
        if (!SessionState.GetBool(PendingSetupKey, false))
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall -= RunPendingSetup;
            EditorApplication.delayCall += RunPendingSetup;
            return;
        }

        SessionState.SetBool(PendingSetupKey, false);
        SessionState.SetBool(ProcessingKey, true);
        EditorApplication.delayCall -= RunPendingSetup;

        try
        {
            FaceMapperEditorTool.SetupAllCharacters();
        }
        finally
        {
            SessionState.SetBool(ProcessingKey, false);
        }
    }
}
#endif
