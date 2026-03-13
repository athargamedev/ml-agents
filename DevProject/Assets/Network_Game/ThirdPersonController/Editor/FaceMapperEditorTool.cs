#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Network_Game.ThirdPersonController;

/// <summary>
/// Thin editor-side bridge for externally generated face assets.
///
/// The old Unity-only face baking path was removed because it relied on
/// brittle mesh- and UV-specific heuristics. The supported workflow is now:
/// 1. Prepare the character in Blender / Faceit and bake likeness atlases externally.
/// 2. Drop the generated atlas textures into Assets/FacePhotos/Baked.
/// 3. Run the sync/setup steps below to create per-material libraries and wire the prefabs.
/// </summary>
public static class FaceMapperEditorTool
{
    private const string FacePhotosFolder = "Assets/FacePhotos";
    private const string BakedFolder = "Assets/FacePhotos/Baked";
    private const string GeneratedMaterialsFolder = "Assets/FacePhotos/Baked/Materials";
    private const string LibrariesFolder = "Assets/FacePhotos/Baked/Libraries";
    private const string CharactersFolder = "Assets/Network_Game/ThirdPersonController/Prefabs";
    private const string AlbedoProp = "_BaseMap";
    private const string GeneratedMaterialPrefix = "FaceMat_";
    private static readonly string[] FaceRendererTokens = { "head", "face" };
    private static readonly string[] FaceSlotTokens = { "head", "face" };
    private static readonly string[] BodySlotTokens = { "body" };

    [MenuItem("Tools/Face Mapper/1. Sync Generated Face Materials")]
    public static void BakeFaceMaterials()
    {
        SyncGeneratedFaceMaterials();
    }

    [MenuItem("Tools/Face Mapper/1b. Import Blender-Baked Atlases")]
    public static void ImportBlenderBakedAtlases()
    {
        SyncGeneratedFaceMaterials();
    }

    [MenuItem("Tools/Face Mapper/2. Auto-Setup Characters")]
    public static void SetupAllCharacters()
    {
        SyncGeneratedFaceMaterials();

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { CharactersFolder });
        int configured = 0;

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                RefreshNestedModelInstances(root);

                PlayerFaceMapper mapper = root.GetComponent<PlayerFaceMapper>();
                if (mapper == null)
                    mapper = root.AddComponent<PlayerFaceMapper>();

                SkinnedMeshRenderer renderer = FindFaceRenderer(root);
                int faceMaterialIndex = FindFaceMaterialSlot(renderer, null);
                Material baseMaterial = ResolveBaseMaterial(renderer, faceMaterialIndex);
                PlayerFaceMaterialLibrary compatibleLibrary = GetLibraryForBaseMaterial(baseMaterial);

                ConfigureMapper(mapper, renderer, faceMaterialIndex, baseMaterial, compatibleLibrary);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                configured++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[Face Mapper] Configured {configured} prefab(s) with material-matched face libraries."
        );
    }

    private static void RefreshNestedModelInstances(GameObject root)
    {
        if (root == null)
            return;

        List<Transform> directChildren = new List<Transform>();
        foreach (Transform child in root.transform)
            directChildren.Add(child);

        foreach (Transform child in directChildren)
        {
            string assetPath = GetModelAssetPath(child.gameObject);
            if (string.IsNullOrEmpty(assetPath))
                continue;

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (modelAsset == null)
                continue;

            Vector3 localPosition = child.localPosition;
            Quaternion localRotation = child.localRotation;
            Vector3 localScale = child.localScale;
            int siblingIndex = child.GetSiblingIndex();
            bool isActive = child.gameObject.activeSelf;
            string instanceName = child.gameObject.name;

            UnityEngine.Object.DestroyImmediate(child.gameObject);

            GameObject replacement = UnityEngine.Object.Instantiate(modelAsset);

            replacement.name = instanceName;
            replacement.transform.SetParent(root.transform, false);
            replacement.transform.SetSiblingIndex(siblingIndex);
            replacement.transform.localPosition = localPosition;
            replacement.transform.localRotation = localRotation;
            replacement.transform.localScale = localScale;
            replacement.SetActive(isActive);
        }
    }

    [MenuItem("Tools/Face Mapper/3. Clear Generated Face Assets")]
    public static void ClearBaked()
    {
        if (
            !EditorUtility.DisplayDialog(
                "Face Mapper",
                "Delete generated face materials, imported atlases, and the face library asset?",
                "Delete",
                "Cancel"
            )
        )
            return;

        if (AssetDatabase.IsValidFolder(BakedFolder))
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { BakedFolder });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileName(assetPath);
                if (
                    fileName.StartsWith(GeneratedMaterialPrefix, StringComparison.OrdinalIgnoreCase)
                    || fileName.StartsWith("FaceAtlas_", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("_face_atlas.png", StringComparison.OrdinalIgnoreCase)
                    || fileName.StartsWith("PlayerFaceMaterialLibrary", StringComparison.OrdinalIgnoreCase)
                )
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Face Mapper/4. Preview First Face In Open Scene")]
    public static void PreviewFirstFaceInOpenScene()
    {
        ApplyScenePreviewMaterial(restoreBase: false);
    }

    [MenuItem("Tools/Face Mapper/5. Restore Base Heads In Open Scene")]
    public static void RestoreBaseHeadsInOpenScene()
    {
        ApplyScenePreviewMaterial(restoreBase: true);
    }

    [MenuItem("Tools/Face Mapper/Deprecated/Legacy Unity Bake")]
    public static void ShowLegacyBakeRemoved()
    {
        EditorUtility.DisplayDialog(
            "Face Mapper",
            "The old Unity-side face bake was removed. Use the Blender/Faceit prep pipeline and then run 'Sync Generated Face Materials'.",
            "OK"
        );
    }

    private static void SyncGeneratedFaceMaterials()
    {
        EnsureFolderExists(FacePhotosFolder);
        EnsureFolderExists(BakedFolder);
        EnsureFolderExists(GeneratedMaterialsFolder);
        EnsureFolderExists(LibrariesFolder);

        AssetDatabase.Refresh();

        List<Texture2D> textures = GetGeneratedAtlasTextures();
        if (textures.Count == 0)
        {
            Debug.LogWarning(
                $"[Face Mapper] No generated face atlas textures were found in: {BakedFolder}"
            );
            return;
        }

        List<Material> baseMaterials = CollectDistinctFaceBaseMaterials();
        if (baseMaterials.Count == 0)
        {
            Debug.LogWarning(
                "[Face Mapper] No compatible head/base materials were found on the character prefabs."
            );
            return;
        }

        int syncedLibraries = 0;
        int syncedMaterials = 0;
        foreach (Material baseMaterial in baseMaterials)
        {
            string materialFolder = GetGeneratedMaterialFolder(baseMaterial);
            EnsureFolderExists(materialFolder);

            List<Material> materials = new List<Material>(textures.Count);
            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D texture = textures[i];
                materials.Add(CreateOrUpdateGeneratedMaterial(baseMaterial, texture, i, materialFolder));
            }

            RemoveStaleGeneratedMaterials(materialFolder, materials);
            CreateOrUpdateFaceLibrary(baseMaterial, materials);
            syncedLibraries++;
            syncedMaterials += materials.Count;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[Face Mapper] Synchronized {syncedMaterials} generated face material(s) across {syncedLibraries} base material family(s)."
        );
    }

    private static string GetModelAssetPath(GameObject instanceRoot)
    {
        if (instanceRoot == null)
            return null;

        UnityEngine.Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(
            instanceRoot
        );
        if (sourceObject != null)
        {
            string sourceAssetPath = AssetDatabase.GetAssetPath(sourceObject);
            if (sourceAssetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return sourceAssetPath;
        }

        foreach (
            SkinnedMeshRenderer renderer in instanceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
        )
        {
            if (renderer == null || renderer.sharedMesh == null)
                continue;

            string meshAssetPath = AssetDatabase.GetAssetPath(renderer.sharedMesh);
            if (meshAssetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return meshAssetPath;
        }

        return null;
    }

    private static void ApplyScenePreviewMaterial(bool restoreBase)
    {
        PlayerFaceMapper[] mappers = UnityEngine.Object.FindObjectsByType<PlayerFaceMapper>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        int updated = 0;

        foreach (PlayerFaceMapper mapper in mappers)
        {
            SerializedObject serializedMapper = new(mapper);
            SkinnedMeshRenderer renderer =
                serializedMapper.FindProperty("m_Renderer").objectReferenceValue as SkinnedMeshRenderer;
            renderer = renderer != null ? renderer : FindFaceRenderer(mapper.gameObject);
            if (renderer == null)
                continue;

            PlayerFaceMaterialLibrary library =
                serializedMapper.FindProperty("m_FaceLibrary").objectReferenceValue as PlayerFaceMaterialLibrary;
            Material baseMaterial =
                serializedMapper.FindProperty("m_BaseBodyMaterial").objectReferenceValue as Material;
            int faceMaterialIndex = serializedMapper.FindProperty("m_FaceMaterialIndex").intValue;
            if (faceMaterialIndex < 0)
                faceMaterialIndex = FindFaceMaterialSlot(renderer, baseMaterial);

            Material previewMaterial = restoreBase
                ? baseMaterial != null
                    ? baseMaterial
                    : library != null
                        ? library.BaseBodyMaterial
                        : null
                : library != null && library.FaceCount > 0
                    ? library.FaceMaterials[0]
                    : null;
            if (previewMaterial == null)
                continue;

            Material layoutMaterial =
                baseMaterial != null
                    ? baseMaterial
                    : library != null
                        ? library.BaseBodyMaterial
                        : previewMaterial;
            EnsureRendererMaterialLayout(renderer, faceMaterialIndex, layoutMaterial);
            Material[] sharedMaterials = renderer.sharedMaterials;
            if (faceMaterialIndex < 0 || faceMaterialIndex >= sharedMaterials.Length)
                continue;

            sharedMaterials[faceMaterialIndex] = previewMaterial;
            renderer.sharedMaterials = sharedMaterials;
            EditorUtility.SetDirty(renderer);
            updated++;
        }

        if (updated > 0)
            EditorSceneManager.MarkAllScenesDirty();

        Debug.Log(
            restoreBase
                ? $"[Face Mapper] Restored base head materials on {updated} scene character(s)."
                : $"[Face Mapper] Updated head preview material on {updated} scene character(s)."
        );
    }

    private static void EnsureFolderExists(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
            return;

        string parent = Path.GetDirectoryName(assetFolderPath)?.Replace("\\", "/");
        string folderName = Path.GetFileName(assetFolderPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            return;

        EnsureFolderExists(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static List<Texture2D> GetGeneratedAtlasTextures()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { BakedFolder });
        Array.Sort(textureGuids, StringComparer.Ordinal);

        List<Texture2D> textures = new List<Texture2D>(textureGuids.Length);
        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
                textures.Add(texture);
        }

        textures.Sort((left, right) =>
            string.CompareOrdinal(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right))
        );
        return textures;
    }

    private static Material CreateOrUpdateGeneratedMaterial(
        Material baseBodyMaterial,
        Texture2D atlasTexture,
        int index,
        string materialFolder
    )
    {
        string stem = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(atlasTexture));
        string materialPath = $"{materialFolder}/{GeneratedMaterialPrefix}{index:D2}_{stem}.mat";
        string materialName = Path.GetFileNameWithoutExtension(materialPath);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(baseBodyMaterial);
            ConfigureGeneratedMaterial(material, baseBodyMaterial, atlasTexture, materialName);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            ConfigureGeneratedMaterial(material, baseBodyMaterial, atlasTexture, materialName);
            EditorUtility.SetDirty(material);
        }

        return material;
    }

    private static void ConfigureGeneratedMaterial(
        Material material,
        Material baseBodyMaterial,
        Texture2D atlasTexture,
        string materialName
    )
    {
        if (material == null || baseBodyMaterial == null)
            return;

        material.CopyPropertiesFromMaterial(baseBodyMaterial);
        material.shader = baseBodyMaterial.shader;
        material.name = materialName;

        if (material.HasProperty(AlbedoProp))
            material.SetTexture(AlbedoProp, atlasTexture);
    }

    private static void RemoveStaleGeneratedMaterials(
        string materialFolder,
        IReadOnlyCollection<Material> keepMaterials
    )
    {
        HashSet<string> keepPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Material material in keepMaterials)
        {
            string materialPath = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(materialPath))
                keepPaths.Add(materialPath);
        }

        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { materialFolder });
        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileName(path);
            if (
                !fileName.StartsWith(GeneratedMaterialPrefix, StringComparison.OrdinalIgnoreCase)
                || keepPaths.Contains(path)
            )
                continue;

            AssetDatabase.DeleteAsset(path);
        }
    }

    private static void CreateOrUpdateFaceLibrary(
        Material baseBodyMaterial,
        IReadOnlyList<Material> materials
    )
    {
        string libraryAssetPath = GetLibraryAssetPath(baseBodyMaterial);
        PlayerFaceMaterialLibrary library = AssetDatabase.LoadAssetAtPath<PlayerFaceMaterialLibrary>(
            libraryAssetPath
        );
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<PlayerFaceMaterialLibrary>();
            AssetDatabase.CreateAsset(library, libraryAssetPath);
        }

        library.SetContents(baseBodyMaterial, ToArray(materials), 0);
        EditorUtility.SetDirty(library);
    }

    private static void ConfigureMapper(
        PlayerFaceMapper mapper,
        SkinnedMeshRenderer renderer,
        int faceMaterialIndex,
        Material baseMaterial,
        PlayerFaceMaterialLibrary library
    )
    {
        EnsureRendererMaterialLayout(renderer, faceMaterialIndex, baseMaterial);

        SerializedObject serializedMapper = new(mapper);
        serializedMapper.FindProperty("m_Renderer").objectReferenceValue = renderer;
        serializedMapper.FindProperty("m_FaceMaterialIndex").intValue = Mathf.Max(faceMaterialIndex, 0);
        serializedMapper.FindProperty("m_BaseBodyMaterial").objectReferenceValue = baseMaterial;
        serializedMapper.FindProperty("m_FaceMaterials").arraySize = 0;
        serializedMapper.FindProperty("m_FaceLibrary").objectReferenceValue = library;
        serializedMapper.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mapper);
    }

    private static SkinnedMeshRenderer FindFaceRenderer(GameObject root)
    {
        if (root == null)
            return null;

        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer renderer in renderers)
            if (RendererNameContainsToken(renderer, FaceRendererTokens))
                return renderer;

        foreach (SkinnedMeshRenderer renderer in renderers)
            if (renderer.gameObject.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer;

        return renderers.Length > 0 ? renderers[0] : null;
    }

    private static int FindFaceMaterialSlot(
        SkinnedMeshRenderer renderer,
        Material baseBodyMaterial
    )
    {
        if (renderer == null)
            return 0;

        Material[] sharedMaterials = renderer.sharedMaterials;
        for (int i = 0; i < sharedMaterials.Length; i++)
            if (MaterialNameContainsToken(sharedMaterials[i], FaceSlotTokens))
                return i;

        if (RendererNameContainsToken(renderer, FaceRendererTokens) && sharedMaterials.Length <= 1)
            return 0;

        int subMeshCount = renderer.sharedMesh != null ? renderer.sharedMesh.subMeshCount : 0;
        if (sharedMaterials.Length > 1 || subMeshCount > 1)
            return Mathf.Max(Mathf.Max(sharedMaterials.Length, subMeshCount) - 1, 0);

        for (int i = 0; i < sharedMaterials.Length; i++)
            if (sharedMaterials[i] == baseBodyMaterial)
                return i;

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            Material shared = sharedMaterials[i];
            if (MaterialNameContainsToken(shared, BodySlotTokens))
                return i;
        }

        return sharedMaterials.Length > 0 ? 0 : -1;
    }

    private static Material ResolveBaseMaterial(
        SkinnedMeshRenderer renderer,
        int faceMaterialIndex
    )
    {
        if (renderer == null)
            return null;

        Material[] sharedMaterials = renderer.sharedMaterials;
        if (faceMaterialIndex >= 0 && faceMaterialIndex < sharedMaterials.Length && sharedMaterials[faceMaterialIndex] != null)
            return sharedMaterials[faceMaterialIndex];

        foreach (Material material in sharedMaterials)
        {
            if (material != null)
                return material;
        }

        return null;
    }

    private static void EnsureRendererMaterialLayout(
        SkinnedMeshRenderer renderer,
        int faceMaterialIndex,
        Material baseBodyMaterial
    )
    {
        if (renderer == null || baseBodyMaterial == null)
            return;

        int subMeshCount = renderer.sharedMesh != null ? renderer.sharedMesh.subMeshCount : 0;
        int targetCount = Mathf.Max(subMeshCount, renderer.sharedMaterials.Length);
        if (targetCount <= 0)
            return;

        Material[] materials = renderer.sharedMaterials;
        if (materials.Length != targetCount)
            Array.Resize(ref materials, targetCount);

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null)
                materials[i] = baseBodyMaterial;
        }

        if (faceMaterialIndex >= 0 && faceMaterialIndex < materials.Length)
            materials[faceMaterialIndex] = baseBodyMaterial;

        renderer.sharedMaterials = materials;
        EditorUtility.SetDirty(renderer);
    }

    private static PlayerFaceMaterialLibrary GetLibraryForBaseMaterial(Material baseMaterial)
    {
        if (baseMaterial == null)
            return null;

        return AssetDatabase.LoadAssetAtPath<PlayerFaceMaterialLibrary>(GetLibraryAssetPath(baseMaterial));
    }

    private static List<Material> CollectDistinctFaceBaseMaterials()
    {
        List<Material> result = new List<Material>();
        HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { CharactersFolder });

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                SkinnedMeshRenderer renderer = FindFaceRenderer(root);
                int faceMaterialIndex = FindFaceMaterialSlot(renderer, null);
                Material baseMaterial = ResolveBaseMaterial(renderer, faceMaterialIndex);
                if (baseMaterial == null)
                    continue;

                string key = GetBaseMaterialKey(baseMaterial);
                if (seenKeys.Add(key))
                    result.Add(baseMaterial);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return result;
    }

    private static string GetGeneratedMaterialFolder(Material baseMaterial)
    {
        return $"{GeneratedMaterialsFolder}/{GetBaseMaterialKey(baseMaterial)}";
    }

    private static string GetLibraryAssetPath(Material baseMaterial)
    {
        return $"{LibrariesFolder}/PlayerFaceMaterialLibrary_{GetBaseMaterialKey(baseMaterial)}.asset";
    }

    private static string GetBaseMaterialKey(Material baseMaterial)
    {
        if (baseMaterial == null)
            return "unknown";

        string assetPath = AssetDatabase.GetAssetPath(baseMaterial);
        string assetStem = string.IsNullOrEmpty(assetPath)
            ? "embedded"
            : Path.GetFileNameWithoutExtension(assetPath);
        return SanitizeFileSegment($"{assetStem}_{baseMaterial.name}");
    }

    private static string SanitizeFileSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unnamed";

        char[] buffer = value.ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
            if (!(char.IsLetterOrDigit(buffer[i]) || buffer[i] == '_' || buffer[i] == '-'))
                buffer[i] = '_';

        return new string(buffer);
    }

    private static bool RendererNameContainsToken(
        SkinnedMeshRenderer renderer,
        IReadOnlyList<string> tokens
    )
    {
        if (renderer == null || renderer.gameObject == null)
            return false;

        foreach (string token in tokens)
        {
            if (renderer.gameObject.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool MaterialNameContainsToken(Material material, IReadOnlyList<string> tokens)
    {
        if (material == null)
            return false;

        foreach (string token in tokens)
        {
            if (material.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static Material[] ToArray(IReadOnlyList<Material> materials)
    {
        Material[] result = new Material[materials.Count];
        for (int i = 0; i < materials.Count; i++)
            result[i] = materials[i];
        return result;
    }
}
#endif
