using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Network_Game.EditorTools
{
    /// <summary>
    /// Safe Mixamo ingest automation:
    /// 1. Download FBX from Mixamo manually.
    /// 2. Drop it into the Incoming folder.
    /// 3. Unity auto-applies import settings, then you can finalize/move in one click.
    /// We intentionally do not scrape Mixamo directly because there is no stable supported API.
    /// </summary>
    public sealed class MixamoImportAutomation : AssetPostprocessor
    {
        private const string MixamoRoot =
            "Assets/Network_Game/ThirdPersonController/Character/Animations/Mixamo";
        private const string IncomingFolder = MixamoRoot + "/Incoming";

        [MenuItem("Network Game/Animations/Open Mixamo Incoming Folder")]
        private static void OpenIncomingFolder()
        {
            EnsureFolderExists();
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", IncomingFolder));
            EditorUtility.RevealInFinder(absolute);
        }

        [MenuItem("Network Game/Animations/Finalize Mixamo Incoming Imports")]
        private static void FinalizeIncomingImports()
        {
            EnsureFolderExists();

            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { IncomingFolder });
            int movedCount = 0;
            List<string> failures = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string destinationPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{MixamoRoot}/{Path.GetFileName(assetPath)}"
                );
                string error = AssetDatabase.MoveAsset(assetPath, destinationPath);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    failures.Add($"{assetPath} -> {error}");
                    continue;
                }

                movedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failures.Count == 0)
            {
                Debug.Log(
                    $"[MixamoImportAutomation] Finalized {movedCount} Mixamo import(s)."
                );
                return;
            }

            Debug.LogWarning(
                $"[MixamoImportAutomation] Finalized {movedCount} import(s) with {failures.Count} failure(s):\n"
                + string.Join("\n", failures)
            );
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!assetPath.StartsWith(MixamoRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ModelImporter importer = assetImporter as ModelImporter;
            if (importer == null)
            {
                return;
            }

            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        private static void EnsureFolderExists()
        {
            if (!AssetDatabase.IsValidFolder(MixamoRoot))
            {
                return;
            }

            if (AssetDatabase.IsValidFolder(IncomingFolder))
            {
                return;
            }

            AssetDatabase.CreateFolder(MixamoRoot, "Incoming");
            AssetDatabase.Refresh();
        }
    }
}
