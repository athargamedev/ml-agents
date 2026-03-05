#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Network_Game.ThirdPersonController;

/// <summary>
/// Automates the face-photo-to-character pipeline via Tools > Face Mapper:
///   Step 1 — Bake Face Materials: composites each photo into a copy of the
///             body UV atlas; saves .png + .mat pairs under FacePhotos/Baked/.
///   Step 2 — Auto-Setup Characters: adds PlayerFaceMapper to every character
///             prefab and wires the baked material array.
/// </summary>
public static class FaceMapperEditorTool
{
    private const string FacePhotosFolder = "Assets/FacePhotos";
    private const string BakedFolder = "Assets/FacePhotos/Baked";
    private const string CharactersFolder = "Assets/Network_Game/ThirdPersonController/Prefabs";
    private const string BodyMatPath =
        "Assets/Network_Game/ThirdPersonController/Character/Materials/Ch33_body.mat";
    private const string AlbedoProp = "_BaseMap";

    // Normalized UV rect in the Ch33_1001_Diffuse atlas where the face UV island sits.
    // Adjust X/Y/W/H if the face lands wrong. Use Blender UV editor to inspect Ch33_nonPBR.fbx.
    private static readonly Rect FaceUVRect = new Rect(0.52f, 0.58f, 0.45f, 0.40f);

    [MenuItem("Tools/Face Mapper/1. Bake Face Materials")]
    public static void BakeFaceMaterials()
    {
        Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(BodyMatPath);
        if (bodyMat == null)
        {
            EditorUtility.DisplayDialog(
                "Face Mapper",
                $"Body material not found:\n{BodyMatPath}",
                "OK"
            );
            return;
        }

        Texture2D bodyTex = bodyMat.GetTexture(AlbedoProp) as Texture2D;
        if (bodyTex == null)
        {
            EditorUtility.DisplayDialog(
                "Face Mapper",
                $"No texture on '{AlbedoProp}' of {bodyMat.name}.",
                "OK"
            );
            return;
        }

        EnsureReadWrite(bodyTex);

        string[] photoGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { FacePhotosFolder });
        if (!AssetDatabase.IsValidFolder(BakedFolder))
            AssetDatabase.CreateFolder(FacePhotosFolder, "Baked");

        int baked = 0;
        for (int i = 0; i < photoGuids.Length; i++)
        {
            string photoPath = AssetDatabase.GUIDToAssetPath(photoGuids[i]);
            if (photoPath.StartsWith(BakedFolder))
                continue;

            Texture2D facePhoto = AssetDatabase.LoadAssetAtPath<Texture2D>(photoPath);
            if (facePhoto == null)
                continue;

            EnsureReadWrite(facePhoto);
            EditorUtility.DisplayProgressBar(
                "Face Mapper — Baking",
                $"Processing: {facePhoto.name}",
                (float)i / photoGuids.Length
            );

            BakeOneFace(bodyMat, bodyTex, facePhoto, baked);
            baked++;
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "Face Mapper",
            $"Baked {baked} material(s) into:\n{BakedFolder}",
            "OK"
        );
    }

    [MenuItem("Tools/Face Mapper/2. Auto-Setup Characters")]
    public static void SetupAllCharacters()
    {
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { BakedFolder });
        if (matGuids.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Face Mapper",
                "No baked materials found. Run Step 1 first.",
                "OK"
            );
            return;
        }

        var bakedMats = new System.Collections.Generic.List<Material>();
        foreach (string g in matGuids)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null)
                bakedMats.Add(m);
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { CharactersFolder });
        int configured = 0;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            SkinnedMeshRenderer smr = FindBodyRenderer(prefab);
            if (smr == null)
                continue;

            int faceSlot = FindFaceMaterialSlot(smr);

            using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                GameObject root = scope.prefabContentsRoot;
                PlayerFaceMapper mapper = root.GetComponent<PlayerFaceMapper>();
                if (mapper == null)
                    mapper = root.AddComponent<PlayerFaceMapper>();

                var so = new SerializedObject(mapper);
                so.FindProperty("m_Renderer").objectReferenceValue = FindBodyRenderer(root);
                so.FindProperty("m_FaceMaterialIndex").intValue = faceSlot;

                SerializedProperty arr = so.FindProperty("m_FaceMaterials");
                arr.arraySize = bakedMats.Count;
                for (int i = 0; i < bakedMats.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = bakedMats[i];

                so.ApplyModifiedProperties();
            }

            configured++;
            Debug.Log(
                $"[Face Mapper] {prefab.name}: slot {faceSlot}, {bakedMats.Count} faces assigned."
            );
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "Face Mapper",
            $"Configured {configured} prefab(s) with {bakedMats.Count} face(s).",
            "OK"
        );
    }

    [MenuItem("Tools/Face Mapper/3. Clear Baked Assets")]
    public static void ClearBaked()
    {
        if (
            !EditorUtility.DisplayDialog(
                "Clear Baked",
                $"Delete all baked assets in:\n{BakedFolder}?",
                "Delete",
                "Cancel"
            )
        )
            return;
        if (AssetDatabase.IsValidFolder(BakedFolder))
        {
            AssetDatabase.DeleteAsset(BakedFolder);
            AssetDatabase.Refresh();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static void BakeOneFace(
        Material srcMat,
        Texture2D bodyTex,
        Texture2D facePhoto,
        int index
    )
    {
        int atlasW = bodyTex.width;
        int atlasH = bodyTex.height;

        // Copy atlas pixels
        Color[] pixels = bodyTex.GetPixels();
        Texture2D composite = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
        composite.SetPixels(pixels);

        // Face region in pixel space
        int px = Mathf.FloorToInt(FaceUVRect.x * atlasW);
        int py = Mathf.FloorToInt(FaceUVRect.y * atlasH);
        int pw = Mathf.Clamp(Mathf.FloorToInt(FaceUVRect.width * atlasW), 1, atlasW - px);
        int ph = Mathf.Clamp(Mathf.FloorToInt(FaceUVRect.height * atlasH), 1, atlasH - py);

        // GPU-resize the face photo to the target pixel region
        Texture2D resized = GpuResize(facePhoto, pw, ph);
        Color[] facePixels = resized.GetPixels();
        Object.DestroyImmediate(resized);

        // Paste face into atlas copy
        for (int y = 0; y < ph; y++)
        for (int x = 0; x < pw; x++)
        {
            Color fp = facePixels[y * pw + x];
            pixels[(py + y) * atlasW + (px + x)] =
                fp.a > 0.01f ? fp : pixels[(py + y) * atlasW + (px + x)];
        }

        composite.SetPixels(pixels);
        composite.Apply();

        // Save composited texture as PNG
        string texPath = $"{BakedFolder}/FaceAtlas_{index:D2}_{facePhoto.name}.png";
        File.WriteAllBytes(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", texPath)),
            composite.EncodeToPNG()
        );
        Object.DestroyImmediate(composite);
        AssetDatabase.ImportAsset(texPath);

        var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (importer != null)
        {
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.maxTextureSize = Mathf.Max(atlasW, atlasH);
            importer.SaveAndReimport();
        }

        // Create material variant
        Material mat = new Material(srcMat) { name = $"Face_{index:D2}_{facePhoto.name}" };
        mat.SetTexture(AlbedoProp, AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        AssetDatabase.CreateAsset(mat, $"{BakedFolder}/FaceMat_{index:D2}_{facePhoto.name}.mat");
        Debug.Log($"[Face Mapper] Baked face {index}: {facePhoto.name}");
    }

    private static Texture2D GpuResize(Texture2D src, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    /// <summary>
    /// Among all SkinnedMeshRenderers in the hierarchy, return the one whose
    /// GameObject name contains "body" — that is the Ch33_Body mesh which
    /// holds the face+body UV atlas. Falls back to the first SMR found.
    /// </summary>
    private static SkinnedMeshRenderer FindBodyRenderer(GameObject root)
    {
        var allSmr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in allSmr)
        {
            if (smr.gameObject.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return smr;
        }
        return allSmr.Length > 0 ? allSmr[0] : null;
    }

    private static int FindFaceMaterialSlot(SkinnedMeshRenderer smr)
    {
        for (int i = 0; i < smr.sharedMaterials.Length; i++)
        {
            if (smr.sharedMaterials[i] == null)
                continue;
            string n = smr.sharedMaterials[i].name.ToLowerInvariant();
            if (
                n.Contains("body")
                || n.Contains("face")
                || n.Contains("head")
                || n.Contains("ch33")
            )
                return i;
        }
        return 0;
    }

    private static void EnsureReadWrite(Texture2D tex)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path))
            return;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null || imp.isReadable)
            return;
        imp.isReadable = true;
        imp.SaveAndReimport();
        Debug.Log($"[Face Mapper] Enabled Read/Write on: {tex.name}");
    }
}
#endif
