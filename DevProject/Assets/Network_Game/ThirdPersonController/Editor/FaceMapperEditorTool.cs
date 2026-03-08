#if UNITY_EDITOR
using System;
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
    private const string ProcessedFolder = "Assets/FacePhotos/Processed";
    private const string BakedFolder = "Assets/FacePhotos/Baked";
    private const string CharactersFolder = "Assets/Network_Game/ThirdPersonController/Prefabs";
    private const string BodyMatPath =
        "Assets/Network_Game/ThirdPersonController/Character/Materials/Ch33_body.mat";
    private const string BodyMeshPath =
        "Assets/Network_Game/ThirdPersonController/Character/Models/Ch33_nonPBR.fbx";
    private const string AlbedoProp = "_BaseMap";
    private const float MaskFeather = 0.25f;
    private const float ColorMatchStrength = 0.70f;
    private const float ShadingTransferStrength = 0.35f;
    private const float BlendStrength = 0.98f;
    private const float MinBlendAlpha = 0.01f;
    private const float MinFaceBoneAverage = 0.16f;
    private const float MinFaceBoneMax = 0.35f;
    private const float MinFaceNormalFacingDot = 0.35f;
    private const float FaceGeometryCentroidZMin = 9.5f;
    private const float FaceGeometryCentroidAbsXMax = 1.5f;
    private const float FaceGeometryNormalYMax = -0.30f;
    private const float LandmarkCropWidthPadding = 1.08f;
    private const float LandmarkCropHeightPadding = 1.03f;
    private const float LandmarkCropVerticalOffset = -0.01f;

    // Source photo crop in normalized top-left UV.
    // Preprocessor now outputs landmark-aligned canonical portraits, so do not recrop here.
    private static readonly Rect SourcePhotoRectTopLeft = new Rect(0.00f, 0.00f, 1.00f, 1.00f);

    // Normalized UV rect in the Ch33_1001_Diffuse atlas where the frontal face island sits.
    // Kept tight intentionally; FBX triangle selection + UV raster mask provides the final shape.
    private static readonly Rect FaceUVRect = new Rect(0.7160f, 0.7440f, 0.2850f, 0.2640f);

    // Landmark template inside the full head UV bounds (top-left normalized).
    // These anchors were tuned against the Ch33 face atlas so eyes/nose/mouth land
    // on the actual model features instead of being stretched across the whole head.
    private static readonly int[] FaceTemplateLandmarkIds =
    {
        10,  // forehead top
        33,  // left eye outer
        133, // left eye inner
        362, // right eye inner
        263, // right eye outer
        1,   // nose tip
        61,  // mouth left
        291, // mouth right
        152, // chin
    };

    private static readonly Vector2[] FaceTemplatePointsTopLeft =
    {
        new(0.50f, 0.17f),
        new(0.35f, 0.38f),
        new(0.45f, 0.39f),
        new(0.55f, 0.39f),
        new(0.65f, 0.38f),
        new(0.50f, 0.53f),
        new(0.46f, 0.66f),
        new(0.54f, 0.66f),
        new(0.50f, 0.82f),
        new(0.27f, 0.30f),
        new(0.73f, 0.30f),
        new(0.31f, 0.58f),
        new(0.69f, 0.58f),
        new(0.40f, 0.77f),
        new(0.60f, 0.77f),
    };

    private static readonly int[] FaceTemplateTriangles =
    {
        0, 9, 1,
        0, 1, 2,
        0, 2, 3,
        0, 3, 4,
        0, 4, 10,
        9, 1, 11,
        1, 2, 11,
        2, 5, 11,
        2, 3, 5,
        3, 5, 12,
        3, 4, 12,
        4, 10, 12,
        11, 5, 6,
        5, 6, 7,
        5, 7, 12,
        11, 6, 13,
        6, 13, 8,
        6, 8, 7,
        7, 8, 14,
        7, 14, 12,
        13, 11, 8,
        14, 12, 8,
    };

    [Serializable]
    private sealed class FaceLandmarkPoint
    {
        public int id;
        public float x;
        public float y;
    }

    [Serializable]
    private sealed class FaceLandmarkFile
    {
        public int width;
        public int height;
        public FaceLandmarkPoint[] points;
        public string detector;
    }

    private sealed class FaceUvSelection
    {
        public Vector2[] Uv;
        public int[] Triangles;
        public Rect UvBounds;
    }

    private static Mesh s_CachedBodyMesh;
    private static bool s_BodyMeshLookupDone;
    private static FaceUvSelection s_CachedFaceUvSelection;
    private static bool s_FaceUvSelectionBuilt;

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

        bool usingProcessedPhotos;
        string[] photoGuids = GetPhotoTextureGuids(out usingProcessedPhotos);
        if (photoGuids.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Face Mapper",
                "No photo textures found. Add images into Assets/FacePhotos or Assets/FacePhotos/Processed.",
                "OK"
            );
            return;
        }

        if (!AssetDatabase.IsValidFolder(BakedFolder))
            AssetDatabase.CreateFolder(FacePhotosFolder, "Baked");

        int baked = 0;

        try
        {
            for (int i = 0; i < photoGuids.Length; i++)
            {
                string photoPath = AssetDatabase.GUIDToAssetPath(photoGuids[i]);
                if (photoPath.StartsWith(BakedFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (
                    !usingProcessedPhotos
                    && photoPath.StartsWith(ProcessedFolder, StringComparison.OrdinalIgnoreCase)
                )
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
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "Face Mapper",
            $"Baked {baked} material(s) into:\n{BakedFolder}",
            "OK"
        );
    }

    /// <summary>
    /// Imports Blender-baked atlas PNGs (*_face_atlas.png) from FacePhotos/Baked/ and
    /// creates FaceMat_* materials from them.  Run this instead of Step 1 when the
    /// UV back-projection bake has been done externally in Blender.
    /// After this completes, run Step 2 (Auto-Setup Characters) as normal.
    /// </summary>
    [MenuItem("Tools/Face Mapper/1b. Import Blender-Baked Atlases")]
    public static void ImportBlenderBakedAtlases()
    {
        Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(BodyMatPath);
        if (bodyMat == null)
        {
            EditorUtility.DisplayDialog("Face Mapper",
                $"Body material not found:\n{BodyMatPath}", "OK");
            return;
        }

        string bakedAbsDir = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", BakedFolder));

        string[] pngPaths = Directory.Exists(bakedAbsDir)
            ? Directory.GetFiles(bakedAbsDir, "*_face_atlas.png", SearchOption.TopDirectoryOnly)
            : new string[0];

        if (pngPaths.Length == 0)
        {
            EditorUtility.DisplayDialog("Face Mapper",
                "No *_face_atlas.png files found in:\n" + bakedAbsDir +
                "\nRun the Blender bake script first.", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(BakedFolder))
            AssetDatabase.CreateFolder(FacePhotosFolder, "Baked");

        System.Array.Sort(pngPaths, StringComparer.OrdinalIgnoreCase);

        // Discover new PNG files before touching importers.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // Phase 1: configure importer settings for all PNGs in one batch.
        // StartAssetEditing prevents intermediate reimports between SaveAndReimport calls.
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string p in pngPaths)
            {
                string stem = Path.GetFileNameWithoutExtension(p);
                string tex  = $"{BakedFolder}/{stem}.png";
                var imp = AssetImporter.GetAtPath(tex) as TextureImporter;
                if (imp == null) continue;
                imp.sRGBTexture        = true;
                imp.isReadable         = false;
                imp.alphaSource        = TextureImporterAlphaSource.FromInput;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.wrapMode           = TextureWrapMode.Clamp;
                imp.filterMode         = FilterMode.Bilinear;
                imp.maxTextureSize     = 2048;
                imp.SaveAndReimport();
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing(); // single batch reimport here
        }

        // Phase 2: create materials (textures now loaded from the batch import above).
        int imported = 0;
        for (int i = 0; i < pngPaths.Length; i++)
        {
            string stem        = Path.GetFileNameWithoutExtension(pngPaths[i]);
            string texAssetPath = $"{BakedFolder}/{stem}.png";
            Texture2D atlasTex  = AssetDatabase.LoadAssetAtPath<Texture2D>(texAssetPath);
            if (atlasTex == null)
            {
                Debug.LogWarning($"[Face Mapper] Texture not found after import: {texAssetPath}");
                continue;
            }

            string matAssetPath = $"{BakedFolder}/FaceMat_{i:D2}_{stem}.mat";
            string matName      = Path.GetFileNameWithoutExtension(matAssetPath);
            Material mat        = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);
            if (mat == null)
            {
                mat = new Material(bodyMat);
                ConfigureFaceMaterial(mat, bodyMat, atlasTex, matName);
                AssetDatabase.CreateAsset(mat, matAssetPath);
            }
            else
            {
                ConfigureFaceMaterial(mat, bodyMat, atlasTex, matName);
                EditorUtility.SetDirty(mat);
            }

            imported++;
            Debug.Log($"[Face Mapper] Imported Blender bake {i}: {stem}");
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Face Mapper",
            $"Imported {imported} Blender-baked material(s) into:\n{BakedFolder}\n\n" +
            "Now run Step 2 — Auto-Setup Characters.", "OK");
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

        Material baseBodyMat = AssetDatabase.LoadAssetAtPath<Material>(BodyMatPath);
        if (baseBodyMat == null)
            Debug.LogWarning($"[Face Mapper] Base body material missing at: {BodyMatPath}");

        var bakedMats = new System.Collections.Generic.List<Material>();
        foreach (string g in matGuids)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null && m.name.StartsWith("FaceMat_", StringComparison.OrdinalIgnoreCase))
                bakedMats.Add(m);
        }

        if (bakedMats.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Face Mapper",
                "No FaceMat_* assets found in Baked folder. Run Step 1 first.",
                "OK"
            );
            return;
        }

        // Prefer processed bakes when both raw and processed variants exist.
        var processedMats = new System.Collections.Generic.List<Material>();
        foreach (Material mat in bakedMats)
        {
            if (
                mat != null
                && mat.name.IndexOf("_processed", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                processedMats.Add(mat);
            }
        }

        if (processedMats.Count > 0)
        {
            bakedMats = processedMats;
            Debug.Log(
                $"[Face Mapper] Using processed-only material set ({bakedMats.Count} entries)."
            );
        }

        bakedMats.Sort(
            (a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase)
        );

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { CharactersFolder });
        int configured = 0;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                GameObject root = scope.prefabContentsRoot;
                SkinnedMeshRenderer bodyRenderer = FindBodyRenderer(root);
                if (bodyRenderer == null)
                {
                    Debug.LogWarning(
                        $"[Face Mapper] {prefab.name}: no body renderer found, skipped."
                    );
                    continue;
                }

                int faceSlot = FindFaceMaterialSlot(bodyRenderer);
                Material slotBaseMat = baseBodyMat;

                var shared = bodyRenderer.sharedMaterials;
                if (faceSlot >= 0 && faceSlot < shared.Length)
                {
                    if (slotBaseMat == null)
                        slotBaseMat = shared[faceSlot];

                    if (slotBaseMat != null && shared[faceSlot] != slotBaseMat)
                    {
                        shared[faceSlot] = slotBaseMat;
                        bodyRenderer.sharedMaterials = shared;
                        EditorUtility.SetDirty(bodyRenderer);
                    }
                }

                PlayerFaceMapper mapper = root.GetComponent<PlayerFaceMapper>();
                if (mapper == null)
                    mapper = root.AddComponent<PlayerFaceMapper>();

                var so = new SerializedObject(mapper);
                so.FindProperty("m_Renderer").objectReferenceValue = bodyRenderer;
                so.FindProperty("m_FaceMaterialIndex").intValue = faceSlot;
                so.FindProperty("m_BaseBodyMaterial").objectReferenceValue = slotBaseMat;

                SerializedProperty arr = so.FindProperty("m_FaceMaterials");
                arr.arraySize = bakedMats.Count;
                for (int i = 0; i < bakedMats.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = bakedMats[i];

                so.ApplyModifiedProperties();

                configured++;
                Debug.Log(
                    $"[Face Mapper] {prefab.name}: slot {faceSlot}, {bakedMats.Count} faces assigned."
                );
            }
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

        Color[] basePixels = bodyTex.GetPixels();
        Color[] pixels = (Color[])basePixels.Clone();

        // Use the full head bounds as the warp canvas, then align source landmarks to a
        // fixed destination template so the facial features land on the model correctly.
        FaceUvSelection selection = GetFaceUvSelection();
        Rect targetUvRect = ResolveHeadUvRect(selection);

        // Face region in pixel space
        int px = Mathf.Clamp(Mathf.FloorToInt(targetUvRect.x * atlasW), 0, atlasW - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(targetUvRect.y * atlasH), 0, atlasH - 1);
        int pw = Mathf.Clamp(Mathf.FloorToInt(targetUvRect.width * atlasW), 1, atlasW - px);
        int ph = Mathf.Clamp(Mathf.FloorToInt(targetUvRect.height * atlasH), 1, atlasH - py);

        Rect sourceCropRect = ResolveSourcePhotoCrop(facePhoto);
        Color[] facePixels;
        string placementMode;
        if (!TryBuildWarpedFacePixels(facePhoto, sourceCropRect, pw, ph, out facePixels))
        {
            Texture2D resized = GpuResizeAndCenterCrop(facePhoto, pw, ph, sourceCropRect);
            facePixels = resized.GetPixels();
            UnityEngine.Object.DestroyImmediate(resized);
            placementMode = "stretch-fallback";
        }
        else
        {
            placementMode = "landmark-warp";
        }

        float[] mask = BuildFeatheredFaceMask(pw, ph, MaskFeather);
        Debug.Log(
            $"[Face Mapper] Head UV {targetUvRect} | source crop {sourceCropRect} | placement={placementMode}"
        );

        Color baseAverage;
        Color sourceAverage;
        ComputeWeightedAverages(
            basePixels,
            atlasW,
            px,
            py,
            pw,
            ph,
            facePixels,
            mask,
            out baseAverage,
            out sourceAverage
        );

        Vector3 channelGain = new Vector3(
            SafeChannelGain(baseAverage.r, sourceAverage.r),
            SafeChannelGain(baseAverage.g, sourceAverage.g),
            SafeChannelGain(baseAverage.b, sourceAverage.b)
        );

        // Blend face into atlas with color + shading harmonization.
        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                int local = y * pw + x;
                int atlas = (py + y) * atlasW + (px + x);

                Color baseColor = pixels[atlas];
                Color faceColor = facePixels[local];

                float alpha = mask[local] * Mathf.Clamp01(faceColor.a) * BlendStrength;
                if (alpha <= MinBlendAlpha)
                    continue;

                Color corrected = ApplyChannelGain(faceColor, channelGain, ColorMatchStrength);
                corrected = TransferBaseShading(corrected, baseColor, ShadingTransferStrength);
                pixels[atlas] = Color.Lerp(baseColor, corrected, alpha);
            }

        Texture2D composite = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
        composite.SetPixels(pixels);
        composite.Apply();

        // Save composited texture as PNG
        string texPath = $"{BakedFolder}/FaceAtlas_{index:D2}_{facePhoto.name}.png";
        File.WriteAllBytes(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", texPath)),
            composite.EncodeToPNG()
        );
        UnityEngine.Object.DestroyImmediate(composite);
        AssetDatabase.ImportAsset(texPath);

        var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (importer != null)
        {
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = Mathf.Max(atlasW, atlasH);
            importer.SaveAndReimport();
        }

        // Create material variant
        string matPath = $"{BakedFolder}/FaceMat_{index:D2}_{facePhoto.name}.mat";
        string matName = Path.GetFileNameWithoutExtension(matPath);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        Texture2D bakedAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (mat == null)
        {
            mat = new Material(srcMat);
            ConfigureFaceMaterial(mat, srcMat, bakedAtlas, matName);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            ConfigureFaceMaterial(mat, srcMat, bakedAtlas, matName);
            EditorUtility.SetDirty(mat);
        }
        Debug.Log($"[Face Mapper] Baked face {index}: {facePhoto.name}");
    }

    private static void ConfigureFaceMaterial(
        Material target,
        Material source,
        Texture2D bakedAtlas,
        string materialName
    )
    {
        target.CopyPropertiesFromMaterial(source);
        target.name = materialName;

        if (target.HasProperty(AlbedoProp))
            target.SetTexture(AlbedoProp, bakedAtlas);
        if (target.HasProperty("_MainTex"))
            target.SetTexture("_MainTex", bakedAtlas);
    }

    private static string[] GetPhotoTextureGuids(out bool usingProcessedPhotos)
    {
        string[] processed = AssetDatabase.FindAssets("t:Texture2D", new[] { ProcessedFolder });
        if (processed.Length > 0)
        {
            usingProcessedPhotos = true;
            return processed;
        }

        usingProcessedPhotos = false;
        return AssetDatabase.FindAssets("t:Texture2D", new[] { FacePhotosFolder });
    }

    private static Texture2D GpuResizeAndCenterCrop(Texture2D src, int w, int h, Rect cropRectTopLeft)
    {
        Texture2D croppedSource = CropSourcePhotoForHeadUV(src, cropRectTopLeft);

        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);

        // Stretch-to-fill: the face UV island is wider than a portrait photo (~1.7:1 vs 1:1).
        // Preserving aspect ratio here would crop forehead/chin. Instead we stretch the photo
        // to fill the UV region — the UV mapping on the 3D mesh undoes this distortion so the
        // face looks natural on the character. (BuildFaceIslandMaskFromMeshUv clips non-face pixels.)
        Graphics.Blit(croppedSource, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        if (!ReferenceEquals(croppedSource, src))
            UnityEngine.Object.DestroyImmediate(croppedSource);

        return result;
    }

    private static Texture2D CropSourcePhotoForHeadUV(Texture2D src, Rect cropRectTopLeft)
    {
        Rect r = cropRectTopLeft;
        r.x = Mathf.Clamp01(r.x);
        r.y = Mathf.Clamp01(r.y);
        r.width = Mathf.Clamp(r.width, 0.01f, 1f - r.x);
        r.height = Mathf.Clamp(r.height, 0.01f, 1f - r.y);

        int sx = Mathf.Clamp(Mathf.FloorToInt(r.x * src.width), 0, src.width - 1);
        int syTop = Mathf.Clamp(Mathf.FloorToInt(r.y * src.height), 0, src.height - 1);
        int sw = Mathf.Clamp(Mathf.FloorToInt(r.width * src.width), 1, src.width - sx);
        int sh = Mathf.Clamp(Mathf.FloorToInt(r.height * src.height), 1, src.height - syTop);
        // Convert top-left source rect into Unity texture pixel space (bottom-left origin).
        int sy = Mathf.Clamp(src.height - syTop - sh, 0, src.height - sh);

        if (sx == 0 && sy == 0 && sw == src.width && sh == src.height)
            return src;

        Texture2D cropped = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
        cropped.SetPixels(src.GetPixels(sx, sy, sw, sh));
        cropped.Apply();
        return cropped;
    }

    private static bool TryBuildWarpedFacePixels(
        Texture2D facePhoto,
        Rect sourceCropRectTopLeft,
        int width,
        int height,
        out Color[] warpedPixels
    )
    {
        warpedPixels = null;

        FaceLandmarkFile landmarks;
        if (!TryLoadLandmarks(facePhoto, out landmarks))
            return false;

        Texture2D croppedSource = CropSourcePhotoForHeadUV(facePhoto, sourceCropRectTopLeft);
        bool destroyCroppedSource = !ReferenceEquals(croppedSource, facePhoto);

        try
        {
            Vector2[] sourcePoints;
            if (
                !TryBuildSourceTemplatePoints(
                    landmarks,
                    sourceCropRectTopLeft,
                    croppedSource,
                    out sourcePoints
                )
            )
                return false;

            Vector2[] destinationPoints = BuildDestinationTemplatePoints(width, height);
            warpedPixels = WarpTriangles(
                croppedSource,
                sourcePoints,
                destinationPoints,
                width,
                height
            );
            return warpedPixels != null;
        }
        finally
        {
            if (destroyCroppedSource)
                UnityEngine.Object.DestroyImmediate(croppedSource);
        }
    }

    private static bool TryBuildSourceTemplatePoints(
        FaceLandmarkFile landmarks,
        Rect sourceCropRectTopLeft,
        Texture2D croppedSource,
        out Vector2[] points
    )
    {
        points = null;
        if (landmarks?.points == null || croppedSource == null)
            return false;

        points = new Vector2[FaceTemplatePointsTopLeft.Length];
        for (int i = 0; i < FaceTemplateLandmarkIds.Length; i++)
        {
            FaceLandmarkPoint point = FindLandmarkPoint(landmarks.points, FaceTemplateLandmarkIds[i]);
            if (point == null)
                return false;

            float u = (point.x - sourceCropRectTopLeft.xMin) / Mathf.Max(0.0001f, sourceCropRectTopLeft.width);
            float vTop = (point.y - sourceCropRectTopLeft.yMin) / Mathf.Max(
                0.0001f,
                sourceCropRectTopLeft.height
            );
            if (u < -0.05f || u > 1.05f || vTop < -0.05f || vTop > 1.05f)
                return false;

            u = Mathf.Clamp01(u);
            float v = 1f - Mathf.Clamp01(vTop);
            points[i] = new Vector2(u * (croppedSource.width - 1), v * (croppedSource.height - 1));
        }

        float cropWidth = croppedSource.width;
        float cropHeight = croppedSource.height;
        points[9] = ClampPointToTexture(
            points[1] + new Vector2(-0.18f * cropWidth, 0.10f * cropHeight),
            croppedSource
        );
        points[10] = ClampPointToTexture(
            points[4] + new Vector2(0.18f * cropWidth, 0.10f * cropHeight),
            croppedSource
        );
        points[11] = ClampPointToTexture(
            points[6] + new Vector2(-0.22f * cropWidth, 0.01f * cropHeight),
            croppedSource
        );
        points[12] = ClampPointToTexture(
            points[7] + new Vector2(0.22f * cropWidth, 0.01f * cropHeight),
            croppedSource
        );
        points[13] = ClampPointToTexture(
            points[8] + new Vector2(-0.16f * cropWidth, 0.10f * cropHeight),
            croppedSource
        );
        points[14] = ClampPointToTexture(
            points[8] + new Vector2(0.16f * cropWidth, 0.10f * cropHeight),
            croppedSource
        );

        return true;
    }

    private static Vector2 ClampPointToTexture(Vector2 point, Texture2D texture)
    {
        return new Vector2(
            Mathf.Clamp(point.x, 0f, Mathf.Max(0f, texture.width - 1)),
            Mathf.Clamp(point.y, 0f, Mathf.Max(0f, texture.height - 1))
        );
    }

    private static FaceLandmarkPoint FindLandmarkPoint(FaceLandmarkPoint[] points, int id)
    {
        if (points == null)
            return null;

        for (int i = 0; i < points.Length; i++)
            if (points[i] != null && points[i].id == id)
                return points[i];

        return null;
    }

    private static Vector2[] BuildDestinationTemplatePoints(int width, int height)
    {
        var points = new Vector2[FaceTemplatePointsTopLeft.Length];
        for (int i = 0; i < FaceTemplatePointsTopLeft.Length; i++)
        {
            Vector2 p = FaceTemplatePointsTopLeft[i];
            points[i] = new Vector2(
                Mathf.Clamp01(p.x) * (width - 1),
                (1f - Mathf.Clamp01(p.y)) * (height - 1)
            );
        }

        return points;
    }

    private static Color[] WarpTriangles(
        Texture2D source,
        Vector2[] sourcePoints,
        Vector2[] destinationPoints,
        int width,
        int height
    )
    {
        if (
            source == null
            || sourcePoints == null
            || destinationPoints == null
            || sourcePoints.Length != destinationPoints.Length
            || sourcePoints.Length != FaceTemplatePointsTopLeft.Length
        )
            return null;

        Color[] pixels = new Color[width * height];
        for (int i = 0; i < FaceTemplateTriangles.Length; i += 3)
        {
            int i0 = FaceTemplateTriangles[i];
            int i1 = FaceTemplateTriangles[i + 1];
            int i2 = FaceTemplateTriangles[i + 2];

            RasterizeWarpTriangle(
                source,
                sourcePoints[i0],
                sourcePoints[i1],
                sourcePoints[i2],
                destinationPoints[i0],
                destinationPoints[i1],
                destinationPoints[i2],
                width,
                height,
                pixels
            );
        }

        return pixels;
    }

    private static void RasterizeWarpTriangle(
        Texture2D source,
        Vector2 source0,
        Vector2 source1,
        Vector2 source2,
        Vector2 dest0,
        Vector2 dest1,
        Vector2 dest2,
        int width,
        int height,
        Color[] output
    )
    {
        float minX = Mathf.Min(dest0.x, Mathf.Min(dest1.x, dest2.x));
        float maxX = Mathf.Max(dest0.x, Mathf.Max(dest1.x, dest2.x));
        float minY = Mathf.Min(dest0.y, Mathf.Min(dest1.y, dest2.y));
        float maxY = Mathf.Max(dest0.y, Mathf.Max(dest1.y, dest2.y));

        int x0 = Mathf.Clamp(Mathf.FloorToInt(minX), 0, width - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX), 0, width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(minY), 0, height - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, height - 1);

        float area = Sign(dest0, dest1, dest2);
        if (Mathf.Abs(area) <= 0.0001f)
            return;

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (!PointInTriangle(p, dest0, dest1, dest2))
                    continue;

                Vector3 bary = ComputeBarycentricCoordinates(p, dest0, dest1, dest2);
                Vector2 samplePoint =
                    (source0 * bary.x) + (source1 * bary.y) + (source2 * bary.z);

                float u = Mathf.Clamp01(samplePoint.x / Mathf.Max(1f, source.width - 1));
                float v = Mathf.Clamp01(samplePoint.y / Mathf.Max(1f, source.height - 1));
                Color sample = source.GetPixelBilinear(u, v);
                sample.a *= ComputeSourcePortraitAlpha(u, v);
                sample.a *= ComputeDestinationTemplateAlpha(x, y, width, height);
                output[(y * width) + x] = sample;
            }
    }

    private static Vector3 ComputeBarycentricCoordinates(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Sign(a, b, c);
        if (Mathf.Abs(area) <= 0.0001f)
            return new Vector3(1f, 0f, 0f);

        float w0 = Sign(p, b, c) / area;
        float w1 = Sign(p, c, a) / area;
        float w2 = 1f - w0 - w1;
        return new Vector3(w0, w1, w2);
    }

    private static float ComputeSourcePortraitAlpha(float u, float v)
    {
        float nx = (u - 0.5f) / 0.42f;
        float ny = (v - 0.53f) / 0.50f;
        float radius = Mathf.Sqrt((nx * nx) + (ny * ny));
        float alpha = Mathf.InverseLerp(1.06f, 0.78f, radius);
        return Mathf.SmoothStep(0f, 1f, alpha);
    }

    private static float ComputeDestinationTemplateAlpha(int x, int y, int width, int height)
    {
        float u = x / Mathf.Max(1f, width - 1);
        float vTop = 1f - (y / Mathf.Max(1f, height - 1));

        float nx = (u - 0.5f) / 0.30f;
        float ny = (vTop - 0.52f) / 0.42f;
        float radius = Mathf.Sqrt((nx * nx) + (ny * ny));
        float oval = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.08f, 0.76f, radius));

        // Fade in below the hairline so the photo never paints over the scalp/hair breakup.
        float hairlineFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.18f, vTop));

        // Preserve the model's built-in eye sockets/eyelids. A photographic eye texture
        // does not line up with this mesh and causes the "broken face" look from the screenshot.
        float leftEyeKeep = 1f - ComputeSoftEllipseMask(
            u,
            vTop,
            new Vector2(0.40f, 0.39f),
            new Vector2(0.10f, 0.07f)
        );
        float rightEyeKeep = 1f - ComputeSoftEllipseMask(
            u,
            vTop,
            new Vector2(0.60f, 0.39f),
            new Vector2(0.10f, 0.07f)
        );

        // Preserve the nostril and mouth opening from the base atlas as well.
        float nostrilKeep = 1f - ComputeSoftEllipseMask(
            u,
            vTop,
            new Vector2(0.50f, 0.53f),
            new Vector2(0.05f, 0.035f)
        );
        float mouthKeep = 1f - ComputeSoftEllipseMask(
            u,
            vTop,
            new Vector2(0.50f, 0.67f),
            new Vector2(0.12f, 0.08f)
        );

        float lowerFaceFade = Mathf.Lerp(1f, 0.25f, Mathf.SmoothStep(0.60f, 0.84f, vTop));
        return oval * hairlineFade * leftEyeKeep * rightEyeKeep * nostrilKeep * mouthKeep * lowerFaceFade;
    }

    private static float ComputeSoftEllipseMask(
        float u,
        float vTop,
        Vector2 center,
        Vector2 radius
    )
    {
        float nx = (u - center.x) / Mathf.Max(0.0001f, radius.x);
        float ny = (vTop - center.y) / Mathf.Max(0.0001f, radius.y);
        float d = Mathf.Sqrt((nx * nx) + (ny * ny));
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.15f, 0.72f, d));
    }

    private static Rect ResolveHeadUvRect(FaceUvSelection selection)
    {
        if (selection == null)
            return FaceUVRect;

        Rect bounds = selection.UvBounds;
        if (bounds.width <= 0.001f || bounds.height <= 0.001f)
            return FaceUVRect;

        return bounds;
    }

    private static Rect ResolveSourcePhotoCrop(Texture2D facePhoto)
    {
        Rect crop;
        if (TryGetLandmarkDrivenCrop(facePhoto, out crop))
            return crop;

        return SourcePhotoRectTopLeft;
    }

    private static bool TryLoadLandmarks(Texture2D facePhoto, out FaceLandmarkFile landmarks)
    {
        landmarks = null;
        if (facePhoto == null)
            return false;

        string texturePath = AssetDatabase.GetAssetPath(facePhoto);
        if (string.IsNullOrWhiteSpace(texturePath))
            return false;

        string folder = Path.GetDirectoryName(texturePath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(folder))
            return false;

        string jsonPath = $"{folder}/{Path.GetFileNameWithoutExtension(texturePath)}_landmarks.json";
        string absoluteJsonPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", jsonPath));
        if (!File.Exists(absoluteJsonPath))
            return false;

        try
        {
            landmarks = JsonUtility.FromJson<FaceLandmarkFile>(File.ReadAllText(absoluteJsonPath));
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[Face Mapper] Failed to read landmarks for '{facePhoto.name}': {ex.Message}"
            );
            return false;
        }

        return landmarks?.points != null && landmarks.points.Length > 0;
    }

    private static bool TryGetLandmarkDrivenCrop(Texture2D facePhoto, out Rect cropRectTopLeft)
    {
        cropRectTopLeft = SourcePhotoRectTopLeft;

        FaceLandmarkFile landmarks;
        if (!TryLoadLandmarks(facePhoto, out landmarks))
            return false;

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        foreach (FaceLandmarkPoint point in landmarks.points)
        {
            minX = Mathf.Min(minX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxX = Mathf.Max(maxX, point.x);
            maxY = Mathf.Max(maxY, point.y);
        }

        if (
            float.IsInfinity(minX)
            || float.IsInfinity(minY)
            || float.IsInfinity(maxX)
            || float.IsInfinity(maxY)
        )
            return false;

        float width = maxX - minX;
        float height = maxY - minY;
        if (width <= 0.05f || height <= 0.05f)
            return false;

        // The processed portraits are landmark-aligned but still padded. Crop to the face
        // bounds first so the subsequent UV stretch places the facial features at the right size.
        float paddedWidth = width * LandmarkCropWidthPadding;
        float paddedHeight = height * LandmarkCropHeightPadding;

        Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        center.y += height * LandmarkCropVerticalOffset;
        cropRectTopLeft = CreateCenteredCropRect(center, paddedWidth, paddedHeight);
        return true;
    }

    private static Rect CreateCenteredCropRect(Vector2 center, float width, float height)
    {
        width = Mathf.Clamp(width, 0.05f, 1f);
        height = Mathf.Clamp(height, 0.05f, 1f);

        float x = Mathf.Clamp(center.x - (width * 0.5f), 0f, 1f - width);
        float y = Mathf.Clamp(center.y - (height * 0.5f), 0f, 1f - height);
        return new Rect(x, y, width, height);
    }

    private static float[] BuildFeatheredFaceMask(int width, int height, float feather)
    {
        float[] mask = new float[width * height];
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float safeFeather = Mathf.Max(0.0001f, feather);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float nx = ((x + 0.5f) - halfW) / halfW;
                float ny = ((y + 0.5f) - halfH) / halfH;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float edge = Mathf.Clamp01((1f - radius) / safeFeather);
                mask[y * width + x] = Mathf.SmoothStep(0f, 1f, edge);
            }

        return mask;
    }

    private static float[] BuildFaceIslandMaskFromSelection(
        FaceUvSelection selection,
        Rect targetUvRect,
        int width,
        int height
    )
    {
        var mask = new float[width * height];
        if (
            selection == null
            || selection.Uv == null
            || selection.Triangles == null
            || selection.Triangles.Length < 3
        )
        {
            FillMask(mask, 1f);
            return mask;
        }

        bool wroteAny = false;
        for (int i = 0; i < selection.Triangles.Length; i += 3)
        {
            int i0 = selection.Triangles[i];
            int i1 = selection.Triangles[i + 1];
            int i2 = selection.Triangles[i + 2];
            if (
                i0 < 0
                || i1 < 0
                || i2 < 0
                || i0 >= selection.Uv.Length
                || i1 >= selection.Uv.Length
                || i2 >= selection.Uv.Length
            )
                continue;

            Vector2 u0 = selection.Uv[i0];
            Vector2 u1 = selection.Uv[i1];
            Vector2 u2 = selection.Uv[i2];
            if (!TriangleIntersectsRect(u0, u1, u2, targetUvRect))
                continue;

            RasterizeUvTriangleIntoMask(u0, u1, u2, targetUvRect, width, height, mask);
            wroteAny = true;
        }

        if (!wroteAny)
            FillMask(mask, 1f);

        return mask;
    }

    private static float[] BuildFaceIslandMaskFromMeshUvLegacy(Rect targetUvRect, int width, int height)
    {
        var mask = new float[width * height];
        Mesh mesh = GetBodyMeshForUvMask();
        if (mesh == null)
        {
            FillMask(mask, 1f);
            return mask;
        }

        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        if (uv == null || uv.Length == 0 || triangles == null || triangles.Length < 3)
        {
            FillMask(mask, 1f);
            return mask;
        }

        bool wroteAny = false;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= uv.Length || i1 >= uv.Length || i2 >= uv.Length)
                continue;

            Vector2 u0 = uv[i0];
            Vector2 u1 = uv[i1];
            Vector2 u2 = uv[i2];
            if (!TriangleIntersectsRect(u0, u1, u2, targetUvRect))
                continue;

            RasterizeUvTriangleIntoMask(u0, u1, u2, targetUvRect, width, height, mask);
            wroteAny = true;
        }

        if (!wroteAny)
            FillMask(mask, 1f);

        return mask;
    }

    private static FaceUvSelection GetFaceUvSelection()
    {
        if (s_FaceUvSelectionBuilt)
            return s_CachedFaceUvSelection;

        s_FaceUvSelectionBuilt = true;

        Mesh mesh = GetBodyMeshForUvMask();
        if (mesh == null)
            return null;

        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        if (uv == null || uv.Length == 0 || triangles == null || triangles.Length < 3)
            return null;

        System.Collections.Generic.List<int> selected = null;
        bool usedBoneWeights = false;
        int boneCandidateTriangles = 0;

        // 1) Bone-weight-first: extract head triangles, then keep the most frontal subset.
        // This is robust against coordinate-space differences between DCC tools and Unity import.
        float[] vertexFaceScore = BuildVertexFaceInfluenceScores(mesh);
        if (vertexFaceScore != null && vertexFaceScore.Length == uv.Length)
        {
            var boneCandidates = new System.Collections.Generic.List<int>(triangles.Length / 6);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];
                if (
                    i0 < 0
                    || i1 < 0
                    || i2 < 0
                    || i0 >= vertexFaceScore.Length
                    || i1 >= vertexFaceScore.Length
                    || i2 >= vertexFaceScore.Length
                )
                    continue;

                float s0 = vertexFaceScore[i0];
                float s1 = vertexFaceScore[i1];
                float s2 = vertexFaceScore[i2];
                float avg = (s0 + s1 + s2) / 3f;
                float max = Mathf.Max(s0, Mathf.Max(s1, s2));
                if (avg < MinFaceBoneAverage || max < MinFaceBoneMax)
                    continue;

                boneCandidates.Add(i0);
                boneCandidates.Add(i1);
                boneCandidates.Add(i2);
            }

            boneCandidateTriangles = boneCandidates.Count / 3;
            if (boneCandidates.Count >= 3)
            {
                selected = FilterFaceTrianglesByBestNormalDirection(uv, mesh.normals, boneCandidates);
                if (selected == null || selected.Count < 3)
                    selected = boneCandidates;
                usedBoneWeights = selected != null && selected.Count >= 3;
            }
        }

        // 2) Fallback: geometry heuristics (last resort).
        if (selected == null || selected.Count < 3)
            selected = BuildFaceTrianglesFromGeometry(mesh);

        if (selected == null || selected.Count < 3)
            return null;

        Rect bounds = ComputeUvBounds(uv, selected);
        if (bounds.width <= 0.0001f || bounds.height <= 0.0001f)
            return null;

        s_CachedFaceUvSelection = new FaceUvSelection
        {
            Uv = uv,
            Triangles = selected.ToArray(),
            UvBounds = bounds,
        };

        Debug.Log(
            $"[Face Mapper] Face UV selection source={(usedBoneWeights ? "bone-weights" : "geometry")} boneCandidates={boneCandidateTriangles} selected={(selected.Count / 3)} bounds={bounds}"
        );

        return s_CachedFaceUvSelection;
    }

    private static System.Collections.Generic.List<int> BuildFaceTrianglesFromGeometry(Mesh mesh)
    {
        if (mesh == null)
            return null;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        if (
            vertices == null
            || normals == null
            || triangles == null
            || vertices.Length == 0
            || normals.Length != vertices.Length
            || triangles.Length < 3
        )
            return null;

        var selected = new System.Collections.Generic.List<int>(triangles.Length / 8);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if (
                i0 < 0
                || i1 < 0
                || i2 < 0
                || i0 >= vertices.Length
                || i1 >= vertices.Length
                || i2 >= vertices.Length
            )
                continue;

            Vector3 c = (vertices[i0] + vertices[i1] + vertices[i2]) / 3f;
            Vector3 n = (normals[i0] + normals[i1] + normals[i2]) / 3f;
            if (n.sqrMagnitude < 1e-8f)
                continue;
            n.Normalize();

            if (c.z <= FaceGeometryCentroidZMin)
                continue;
            if (Mathf.Abs(c.x) >= FaceGeometryCentroidAbsXMax)
                continue;
            if (n.y >= FaceGeometryNormalYMax)
                continue;

            selected.Add(i0);
            selected.Add(i1);
            selected.Add(i2);
        }

        return selected.Count >= 3 ? selected : null;
    }

    private static Rect ComputeUvBounds(
        Vector2[] uv,
        System.Collections.Generic.List<int> triangleVertexIndices
    )
    {
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < triangleVertexIndices.Count; i++)
        {
            int vi = triangleVertexIndices[i];
            if (vi < 0 || vi >= uv.Length)
                continue;
            Vector2 u = uv[vi];
            minX = Mathf.Min(minX, u.x);
            minY = Mathf.Min(minY, u.y);
            maxX = Mathf.Max(maxX, u.x);
            maxY = Mathf.Max(maxY, u.y);
        }

        if (
            float.IsNaN(minX)
            || float.IsNaN(minY)
            || float.IsNaN(maxX)
            || float.IsNaN(maxY)
            || float.IsInfinity(minX)
            || float.IsInfinity(minY)
            || float.IsInfinity(maxX)
            || float.IsInfinity(maxY)
        )
            return FaceUVRect;

        float padX = Mathf.Max(0.003f, (maxX - minX) * 0.015f);
        float padY = Mathf.Max(0.003f, (maxY - minY) * 0.015f);
        minX = Mathf.Clamp01(minX - padX);
        minY = Mathf.Clamp01(minY - padY);
        maxX = Mathf.Clamp01(maxX + padX);
        maxY = Mathf.Clamp01(maxY + padY);

        float w = Mathf.Max(0.001f, maxX - minX);
        float h = Mathf.Max(0.001f, maxY - minY);
        return new Rect(minX, minY, w, h);
    }

    private static System.Collections.Generic.List<int> FilterFaceTrianglesByBestNormalDirection(
        Vector2[] uv,
        Vector3[] normals,
        System.Collections.Generic.List<int> candidates
    )
    {
        if (normals == null || uv == null || normals.Length != uv.Length || candidates == null)
            return null;

        Vector3[] dirs =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back,
        };

        System.Collections.Generic.List<int> best = null;
        float bestScore = float.NegativeInfinity;

        foreach (Vector3 dir in dirs)
        {
            var subset = new System.Collections.Generic.List<int>(candidates.Count / 2);
            for (int i = 0; i < candidates.Count; i += 3)
            {
                int i0 = candidates[i];
                int i1 = candidates[i + 1];
                int i2 = candidates[i + 2];
                if (
                    i0 < 0
                    || i1 < 0
                    || i2 < 0
                    || i0 >= normals.Length
                    || i1 >= normals.Length
                    || i2 >= normals.Length
                )
                    continue;

                Vector3 triN = (normals[i0] + normals[i1] + normals[i2]) / 3f;
                if (triN.sqrMagnitude < 1e-8f)
                    continue;

                float facing = Vector3.Dot(triN.normalized, dir);
                if (facing < MinFaceNormalFacingDot)
                    continue;

                subset.Add(i0);
                subset.Add(i1);
                subset.Add(i2);
            }

            if (subset.Count < 3)
                continue;

            Rect bounds = ComputeUvBounds(uv, subset);
            float area = Mathf.Max(1e-6f, bounds.width * bounds.height);
            float overlap = ComputeRectOverlapArea(bounds, FaceUVRect);
            if (overlap <= 0f)
                continue;

            float targetArea = Mathf.Max(1e-6f, FaceUVRect.width * FaceUVRect.height);
            float overlapRatio = overlap / targetArea;
            float areaRatio = area / targetArea;
            Vector2 centerDelta = bounds.center - FaceUVRect.center;
            float centerPenalty =
                (Mathf.Abs(centerDelta.x) / Mathf.Max(0.0001f, FaceUVRect.width))
                + (Mathf.Abs(centerDelta.y) / Mathf.Max(0.0001f, FaceUVRect.height));
            float score = (overlapRatio * 8f) - areaRatio - (centerPenalty * 0.35f);
            if (score > bestScore)
            {
                bestScore = score;
                best = subset;
            }
        }

        return best;
    }

    private static float ComputeRectOverlapArea(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        if (xMax <= xMin || yMax <= yMin)
            return 0f;
        return (xMax - xMin) * (yMax - yMin);
    }

    private static float[] BuildVertexFaceInfluenceScores(Mesh mesh)
    {
        if (mesh == null)
            return null;

        BoneWeight[] weights = mesh.boneWeights;
        if (weights == null || weights.Length != mesh.vertexCount)
            return null;

        SkinnedMeshRenderer bodyRenderer = GetBodyRendererForBoneMask();
        bool[] faceBoneMask = ResolveFaceBoneMask(bodyRenderer);
        if (faceBoneMask == null || faceBoneMask.Length == 0)
            return null;

        float[] scores = new float[weights.Length];
        for (int i = 0; i < weights.Length; i++)
            scores[i] = ScoreBoneWeight(weights[i], faceBoneMask);
        return scores;
    }

    private static float ScoreBoneWeight(BoneWeight bw, bool[] faceBoneMask)
    {
        float score = 0f;
        if (IsFaceBoneIndex(bw.boneIndex0, faceBoneMask))
            score += bw.weight0;
        if (IsFaceBoneIndex(bw.boneIndex1, faceBoneMask))
            score += bw.weight1;
        if (IsFaceBoneIndex(bw.boneIndex2, faceBoneMask))
            score += bw.weight2;
        if (IsFaceBoneIndex(bw.boneIndex3, faceBoneMask))
            score += bw.weight3;
        return Mathf.Clamp01(score);
    }

    private static bool IsFaceBoneIndex(int boneIndex, bool[] faceBoneMask)
    {
        return boneIndex >= 0 && boneIndex < faceBoneMask.Length && faceBoneMask[boneIndex];
    }

    private static bool[] ResolveFaceBoneMask(SkinnedMeshRenderer renderer)
    {
        if (renderer == null || renderer.bones == null || renderer.bones.Length == 0)
            return null;

        bool[] mask = new bool[renderer.bones.Length];
        int count = 0;
        for (int i = 0; i < renderer.bones.Length; i++)
        {
            string boneName = renderer.bones[i] != null ? renderer.bones[i].name : string.Empty;
            if (IsFaceBoneName(boneName))
            {
                mask[i] = true;
                count++;
            }
        }

        return count > 0 ? mask : null;
    }

    private static bool IsFaceBoneName(string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
            return false;

        string n = boneName.ToLowerInvariant();
        return
            n.Contains("head")
            || n.Contains("neck")
            || n.Contains("jaw")
            || n.Contains("face")
            || n.Contains("eye");
    }

    private static SkinnedMeshRenderer GetBodyRendererForBoneMask()
    {
        GameObject modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BodyMeshPath);
        if (modelRoot == null)
            return null;
        return FindBodyRenderer(modelRoot);
    }

    private static Mesh GetBodyMeshForUvMask()
    {
        if (s_BodyMeshLookupDone)
            return s_CachedBodyMesh;

        s_BodyMeshLookupDone = true;
        SkinnedMeshRenderer bodyRenderer = GetBodyRendererForBoneMask();
        if (bodyRenderer != null && bodyRenderer.sharedMesh != null)
        {
            s_CachedBodyMesh = bodyRenderer.sharedMesh;
            return s_CachedBodyMesh;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(BodyMeshPath);
        Mesh fallback = null;
        foreach (UnityEngine.Object asset in assets)
        {
            Mesh mesh = asset as Mesh;
            if (mesh == null)
                continue;

            if (fallback == null)
                fallback = mesh;

            if (mesh.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                s_CachedBodyMesh = mesh;
                break;
            }
        }

        if (s_CachedBodyMesh == null)
            s_CachedBodyMesh = fallback;

        if (s_CachedBodyMesh == null)
        {
            Debug.LogWarning(
                $"[Face Mapper] Could not load body mesh at '{BodyMeshPath}'. Falling back to rectangle blend mask."
            );
        }

        return s_CachedBodyMesh;
    }

    private static bool TriangleIntersectsRect(Vector2 a, Vector2 b, Vector2 c, Rect rect)
    {
        float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
        return !(maxX < rect.xMin || minX > rect.xMax || maxY < rect.yMin || minY > rect.yMax);
    }

    private static void RasterizeUvTriangleIntoMask(
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Rect targetUvRect,
        int width,
        int height,
        float[] mask
    )
    {
        Vector2 p0 = ToLocalMaskPoint(uv0, targetUvRect, width, height);
        Vector2 p1 = ToLocalMaskPoint(uv1, targetUvRect, width, height);
        Vector2 p2 = ToLocalMaskPoint(uv2, targetUvRect, width, height);

        float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x));
        float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x));
        float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y));
        float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));

        int x0 = Mathf.Clamp(Mathf.FloorToInt(minX), 0, width - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX), 0, width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(minY), 0, height - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, height - 1);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (PointInTriangle(p, p0, p1, p2))
                    mask[y * width + x] = 1f;
            }
    }

    private static Vector2 ToLocalMaskPoint(Vector2 uv, Rect targetUvRect, int width, int height)
    {
        float u = (uv.x - targetUvRect.xMin) / targetUvRect.width;
        float v = (uv.y - targetUvRect.yMin) / targetUvRect.height;
        return new Vector2(u * width, v * height);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
        bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static void FillMask(float[] mask, float value)
    {
        for (int i = 0; i < mask.Length; i++)
            mask[i] = value;
    }

    private static float ComputeMaskCoverage(float[] mask)
    {
        if (mask == null || mask.Length == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < mask.Length; i++)
            sum += Mathf.Clamp01(mask[i]);
        return sum / mask.Length;
    }

    private static float ComputeMaskCoverageInRect(float[] mask, int width, int height, Rect normalizedRect)
    {
        if (mask == null || mask.Length == 0 || width <= 0 || height <= 0)
            return 0f;

        float xMinN = Mathf.Clamp01(normalizedRect.xMin);
        float xMaxN = Mathf.Clamp01(normalizedRect.xMax);
        float yMinN = Mathf.Clamp01(normalizedRect.yMin);
        float yMaxN = Mathf.Clamp01(normalizedRect.yMax);

        int x0 = Mathf.Clamp(Mathf.FloorToInt(xMinN * width), 0, width - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(xMaxN * width), 0, width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(yMinN * height), 0, height - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(yMaxN * height), 0, height - 1);
        if (x1 < x0 || y1 < y0)
            return 0f;

        float sum = 0f;
        int count = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                sum += Mathf.Clamp01(mask[y * width + x]);
                count++;
            }

        return count > 0 ? (sum / count) : 0f;
    }

    private static void ComputeWeightedAverages(
        Color[] basePixels,
        int atlasWidth,
        int px,
        int py,
        int pw,
        int ph,
        Color[] sourcePixels,
        float[] weights,
        out Color baseAverage,
        out Color sourceAverage
    )
    {
        Vector3 baseSum = Vector3.zero;
        Vector3 sourceSum = Vector3.zero;
        float weightSum = 0f;

        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                int local = y * pw + x;
                float weight = weights[local];
                if (weight <= 0.2f)
                    continue;

                Color src = sourcePixels[local];
                float alpha = weight * Mathf.Clamp01(src.a);
                if (alpha <= 0f)
                    continue;

                Color dst = basePixels[(py + y) * atlasWidth + (px + x)];
                baseSum += new Vector3(dst.r, dst.g, dst.b) * alpha;
                sourceSum += new Vector3(src.r, src.g, src.b) * alpha;
                weightSum += alpha;
            }

        if (weightSum <= 0.0001f)
        {
            baseAverage = Color.white;
            sourceAverage = Color.white;
            return;
        }

        Vector3 baseAvg = baseSum / weightSum;
        Vector3 sourceAvg = sourceSum / weightSum;
        baseAverage = new Color(baseAvg.x, baseAvg.y, baseAvg.z, 1f);
        sourceAverage = new Color(sourceAvg.x, sourceAvg.y, sourceAvg.z, 1f);
    }

    private static float SafeChannelGain(float target, float source)
    {
        if (source <= 0.0001f)
            return 1f;
        return Mathf.Clamp(target / source, 0.65f, 1.45f);
    }

    private static Color ApplyChannelGain(Color c, Vector3 gain, float strength)
    {
        Color mapped = new Color(c.r * gain.x, c.g * gain.y, c.b * gain.z, c.a);
        return Color.Lerp(c, mapped, Mathf.Clamp01(strength));
    }

    private static Color TransferBaseShading(Color faceColor, Color baseColor, float strength)
    {
        float blend = Mathf.Clamp01(strength);
        if (blend <= 0f)
            return faceColor;

        float faceLum = Luminance(faceColor);
        if (faceLum <= 0.0001f)
            return faceColor;

        float baseLum = Luminance(baseColor);
        float ratio = Mathf.Clamp(baseLum / faceLum, 0.7f, 1.3f);
        float lumScale = Mathf.Pow(ratio, blend);

        return new Color(
            Mathf.Clamp01(faceColor.r * lumScale),
            Mathf.Clamp01(faceColor.g * lumScale),
            Mathf.Clamp01(faceColor.b * lumScale),
            faceColor.a
        );
    }

    private static float Luminance(Color c)
    {
        return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
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
            if (
                IsActiveInAssetHierarchy(smr.gameObject)
                && smr.gameObject.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0
            )
                return smr;
        }

        foreach (var smr in allSmr)
        {
            if (smr.gameObject.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return smr;
        }

        foreach (var smr in allSmr)
        {
            if (IsActiveInAssetHierarchy(smr.gameObject))
                return smr;
        }

        return allSmr.Length > 0 ? allSmr[0] : null;
    }

    private static bool IsActiveInAssetHierarchy(GameObject go)
    {
        for (Transform current = go != null ? go.transform : null; current != null; current = current.parent)
        {
            if (!current.gameObject.activeSelf)
                return false;
        }

        return go != null;
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
