using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Rebuilds the game's look in one reproducible step: switches URP from the 2D
/// renderer to a 3D forward renderer with SSAO, configures shadows, lighting and
/// a post-processing volume, and moves the map onto triplanar PBR materials.
/// Run from the menu or batchmode: -executeMethod VisualOverhaulBuilder.BuildAll
/// </summary>
public static class VisualOverhaulBuilder
{
    private const string RendererPath = "Assets/Settings/ForwardRenderer.asset";
    private const string PipelinePath = "Assets/Settings/UniversalRP.asset";
    private const string VolumeProfilePath = "Assets/Settings/BattleVolumeProfile.asset";
    private const string ScenePath = "Assets/Scenes/Map1.unity";
    private const string TextureFolder = "Assets/Art/PolyHaven/";

    [MenuItem("CsAutobattler/Visual Overhaul/Build All")]
    public static void BuildAll()
    {
        ConfigureTextures();
        UniversalRendererData renderer = CreateForwardRenderer();
        ConfigurePipeline(renderer);
        VolumeProfile profile = CreateVolumeProfile();
        ConfigureMaterials();
        ConfigureScene(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[VisualOverhaul] Build complete.");
    }

    private static void ConfigureTextures()
    {
        foreach (string path in Directory.GetFiles(TextureFolder, "*.jpg"))
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            bool normal = path.Contains("_nor_gl_");
            bool linearData = path.Contains("_arm_");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !linearData;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }

    private static UniversalRendererData CreateForwardRenderer()
    {
        UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
        }

        if (renderer.postProcessData == null)
        {
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
        }
        renderer.renderingMode = RenderingMode.Forward;

        if (!renderer.rendererFeatures.Exists(feature => feature is ScreenSpaceAmbientOcclusion))
        {
            ScreenSpaceAmbientOcclusion ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ssao, renderer);
            SerializedObject ssaoObject = new SerializedObject(ssao);
            SetFloat(ssaoObject, "m_Settings.Intensity", 1.6f);
            SetFloat(ssaoObject, "m_Settings.Radius", 0.6f);
            SetFloat(ssaoObject, "m_Settings.DirectLightingStrength", 0.35f);
            ssaoObject.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject rendererObject = new SerializedObject(renderer);
            SerializedProperty features = rendererObject.FindProperty("m_RendererFeatures");
            SerializedProperty map = rendererObject.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
            if (map != null)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId);
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }
            rendererObject.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(renderer);
        return renderer;
    }

    private static void ConfigurePipeline(UniversalRendererData renderer)
    {
        UniversalRenderPipelineAsset pipeline =
            AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        SerializedObject pipelineObject = new SerializedObject(pipeline);
        SerializedProperty renderers = pipelineObject.FindProperty("m_RendererDataList");
        renderers.arraySize = 1;
        renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        SetInt(pipelineObject, "m_DefaultRendererIndex", 0);
        SetBool(pipelineObject, "m_MainLightShadowsSupported", true);
        SetInt(pipelineObject, "m_MainLightShadowmapResolution", 2048);
        SetFloat(pipelineObject, "m_ShadowDistance", 90f);
        SetInt(pipelineObject, "m_ShadowCascadeCount", 2);
        SetBool(pipelineObject, "m_SoftShadowsSupported", true);
        SetInt(pipelineObject, "m_SoftShadowQuality", 2);
        SetBool(pipelineObject, "m_SupportsHDR", true);
        SetInt(pipelineObject, "m_MSAA", 4);
        SetInt(pipelineObject, "m_AdditionalLightsRenderingMode", 1);
        pipelineObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);
    }

    private static VolumeProfile CreateVolumeProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile != null)
        {
            AssetDatabase.DeleteAsset(VolumeProfilePath);
        }
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumeProfilePath);

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.value = TonemappingMode.ACES;

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = 1.05f;
        bloom.intensity.value = 0.55f;
        bloom.scatter.value = 0.65f;
        bloom.tint.value = new Color(1f, 0.92f, 0.82f);

        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.value = 0.35f;
        color.contrast.value = 18f;
        color.saturation.value = -4f;
        color.colorFilter.value = Color.white;

        WhiteBalance whiteBalance = profile.Add<WhiteBalance>(true);
        whiteBalance.temperature.value = 2f;
        whiteBalance.tint.value = 0f;

        ShadowsMidtonesHighlights smh = profile.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.value = new Vector4(0.92f, 0.98f, 1.1f, 0f);
        smh.highlights.value = new Vector4(1.06f, 1.0f, 0.92f, 0f);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.value = 0.28f;
        vignette.smoothness.value = 0.45f;
        vignette.color.value = new Color(0.05f, 0.03f, 0.02f);

        foreach (VolumeComponent component in profile.components)
        {
            component.name = component.GetType().Name;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void ConfigureMaterials()
    {
        Shader triplanar = Shader.Find("CsAutobattler/TriplanarLit");
        Texture2D floorAlbedo = Load("concrete_tiles_diff_1k");
        Texture2D floorNormal = Load("concrete_tiles_nor_gl_1k");
        Texture2D floorArm = Load("concrete_tiles_arm_1k");
        Texture2D wallAlbedo = Load("rock_embedded_concrete_wall_diff_1k");
        Texture2D wallNormal = Load("rock_embedded_concrete_wall_nor_gl_1k");
        Texture2D wallArm = Load("rock_embedded_concrete_wall_arm_1k");

        // Desert-compound palette: warm sand floor, sun-bleached plaster walls,
        // darker concrete cover, and colour-coded objective/spawn zones.
        Apply("Assets/Materials/Floor_Mat.mat", triplanar, floorAlbedo, floorNormal, floorArm,
            new Color(1.0f, 0.9f, 0.76f), 4f, 0.45f, Color.black);
        Apply("Assets/Materials/Wall_Mat.mat", triplanar, wallAlbedo, wallNormal, wallArm,
            new Color(1.18f, 1.1f, 0.98f), 1.6f, 0.35f, Color.black);
        Apply("Assets/Materials/DustLikeMap/Cover_Mat.mat", triplanar, wallAlbedo, wallNormal, wallArm,
            new Color(0.9f, 0.88f, 0.86f), 1.3f, 0.3f, Color.black);
        Apply("Assets/Materials/DustLikeMap/A_Site_Mat.mat", triplanar, floorAlbedo, floorNormal, floorArm,
            new Color(1.3f, 0.62f, 0.38f), 2.5f, 0.4f, new Color(0.55f, 0.12f, 0.02f));
        Apply("Assets/Materials/DustLikeMap/B_Site_Mat.mat", triplanar, floorAlbedo, floorNormal, floorArm,
            new Color(1.3f, 0.62f, 0.38f), 2.5f, 0.4f, new Color(0.55f, 0.12f, 0.02f));
        SetCap("Assets/Materials/Wall_Mat.mat", new Color(0.6f, 0.56f, 0.5f, 1f), 0.8f);
        SetCap("Assets/Materials/DustLikeMap/Cover_Mat.mat", new Color(0.46f, 0.45f, 0.44f, 1f), 0.5f);
        Apply("Assets/Materials/DustLikeMap/Spawn_Mat.mat", triplanar, floorAlbedo, floorNormal, floorArm,
            new Color(0.95f, 1.0f, 1.08f), 2.5f, 0.4f, Color.black);
    }

    private static void Apply(
        string path,
        Shader shader,
        Texture2D albedo,
        Texture2D normal,
        Texture2D arm,
        Color tint,
        float tileSize,
        float smoothness,
        Color emission)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Debug.LogWarning("[VisualOverhaul] Missing material " + path);
            return;
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetTexture("_ARMMap", arm);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_TileSize", tileSize);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_BumpScale", 1.1f);
        material.SetColor("_EmissionColor", emission);
        material.SetColor("_CapTint", new Color(1f, 1f, 1f, 0f)); // opt-in via SetCap
        EditorUtility.SetDirty(material);
    }

    /// <summary>Site/spawn decals must sit just above the floor or they z-fight/vanish.</summary>
    private static void LiftZonesAboveFloor()
    {
        float floorTop = float.MinValue;
        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.name == "Floor_Mat")
            {
                floorTop = Mathf.Max(floorTop, renderer.bounds.max.y);
            }
        }
        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            string material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : string.Empty;
            if (material != "A_Site_Mat" && material != "B_Site_Mat" && material != "Spawn_Mat")
            {
                continue;
            }
            float top = renderer.bounds.max.y;
            Debug.Log($"[VisualOverhaul] Zone {renderer.name} top={top:0.000} floorTop={floorTop:0.000}");
            if (top < floorTop + 0.015f)
            {
                renderer.transform.position += Vector3.up * (floorTop + 0.02f - top);
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private static void SetCap(string path, Color tint, float minHeight)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            return;
        }
        material.SetColor("_CapTint", tint);
        material.SetFloat("_CapHeight", minHeight);
        EditorUtility.SetDirty(material);
    }

    private static void ConfigureScene(VolumeProfile profile)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Late-afternoon sun: warm key light raking across the map for long,
        // readable shadows, with a cool sky fill.
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.type != LightType.Directional)
            {
                continue;
            }
            light.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 2.7f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.82f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.3f;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.58f, 0.53f, 0.48f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.25f, 0.21f);
        // The overview camera sits ~70m from the board; distance fog would wash
        // the whole map out, so keep it off.
        RenderSettings.fog = false;

        LiftZonesAboveFloor();

        GameObject volumeObject = GameObject.Find("Global Post Volume");
        if (volumeObject == null)
        {
            volumeObject = new GameObject("Global Post Volume");
        }
        Volume volume = volumeObject.GetComponent<Volume>() ?? volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (camera.targetTexture != null)
            {
                continue; // minimap render texture camera stays clean and cheap
            }
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.13f, 0.11f);
            if (camera.CompareTag("MainCamera") && !camera.orthographic)
            {
                FrameMap(camera);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// 92 degrees FOV distorted the board and exceeded the zoom clamp (max 70).
    /// Use a 60 degree lens at a 60 degree pitch for a readable 2.5D view and
    /// back off until the whole floor fits on screen.
    /// </summary>
    public static void FrameMainCamera()
    {
        Camera main = Camera.main;
        if (main != null && !main.orthographic)
        {
            FrameMap(main);
            EditorUtility.SetDirty(main.transform);
        }
    }

    private static void FrameMap(Camera camera)
    {
        // Floor3D is the playable ground (blockout floor or Open Front terrain).
        GameObject floorObject = GameObject.Find("Floor3D");
        Renderer floorRenderer = floorObject != null ? floorObject.GetComponent<Renderer>() : null;
        if (floorRenderer == null)
        {
            return;
        }
        Bounds floor = floorRenderer.bounds;

        camera.fieldOfView = 60f;
        camera.aspect = 16f / 9f;
        Quaternion rotation = Quaternion.Euler(60f, 0f, 0f);
        camera.transform.rotation = rotation;
        // Frame the ground plane (y=0), not the top of any raised terrain.
        Vector3 focus = new Vector3(floor.center.x, 0f, floor.center.z);
        Vector3[] corners =
        {
            new Vector3(floor.min.x, 0f, floor.min.z),
            new Vector3(floor.max.x, 0f, floor.min.z),
            new Vector3(floor.min.x, 0f, floor.max.z),
            new Vector3(floor.max.x, 0f, floor.max.z)
        };
        for (float distance = 10f; distance < 400f; distance += 1f)
        {
            camera.transform.position = focus - rotation * Vector3.forward * distance;
            bool fits = true;
            foreach (Vector3 corner in corners)
            {
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                fits &= viewport.z > 0f && viewport.x > 0.01f && viewport.x < 0.99f &&
                        viewport.y > 0.01f && viewport.y < 0.97f;
            }
            if (fits)
            {
                break;
            }
        }
        camera.ResetAspect();
    }

    private static Texture2D Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + name + ".jpg");
    }

    private static void SetFloat(SerializedObject target, string path, float value)
    {
        SerializedProperty property = target.FindProperty(path);
        if (property != null) property.floatValue = value;
        else Debug.LogWarning("[VisualOverhaul] Missing property " + path);
    }

    private static void SetInt(SerializedObject target, string path, int value)
    {
        SerializedProperty property = target.FindProperty(path);
        if (property != null) property.intValue = value;
        else Debug.LogWarning("[VisualOverhaul] Missing property " + path);
    }

    private static void SetBool(SerializedObject target, string path, bool value)
    {
        SerializedProperty property = target.FindProperty(path);
        if (property != null) property.boolValue = value;
        else Debug.LogWarning("[VisualOverhaul] Missing property " + path);
    }
}
