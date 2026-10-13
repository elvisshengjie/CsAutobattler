using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders UI art from the real game assets: a menu key-art backdrop and
/// transparent operator portraits (one per weapon), written to
/// Assets/Resources/UI so the campaign screens can load them at runtime.
/// Menu: CsAutobattler/Visual Overhaul/Render UI Art
/// </summary>
public static class UiArtRenderer
{
    private const string OutputFolder = "Assets/Resources/UI";
    private const int PortraitLayer = 31;

    [MenuItem("CsAutobattler/Visual Overhaul/Render UI Art")]
    public static void RenderAll()
    {
        bool previousAsync = EditorSettings.asyncShaderCompilation;
        EditorSettings.asyncShaderCompilation = false;
        try
        {
            Directory.CreateDirectory(OutputFolder);
            EditorSceneManager.OpenScene("Assets/Scenes/Map1.unity");
            RenderKeyArt();
            foreach (WeaponType weapon in System.Enum.GetValues(typeof(WeaponType)))
            {
                RenderPortrait("Characters/Character_Soldier", weapon, $"Portrait_Soldier_{weapon}");
                RenderPortrait("Characters/Character_Enemy", weapon, $"Portrait_Enemy_{weapon}");
            }
        }
        finally
        {
            EditorSettings.asyncShaderCompilation = previousAsync;
        }
        AssetDatabase.Refresh();
        foreach (string file in Directory.GetFiles(OutputFolder, "*.png"))
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        Debug.Log("[UiArt] Rendered UI art.");
    }

    private static void RenderKeyArt()
    {
        // A squad advancing toward the camera across the sunlit compound; the
        // late-afternoon sun travels toward +Z, so a squad facing -Z is front-lit.
        Vector3 origin = FindSpawn("TSpawn");
        foreach (AgentStats agent in Object.FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            agent.gameObject.SetActive(false); // hide placeholder capsules
        }
        GameObject root = new GameObject("KeyArt");
        WeaponType[] weapons = { WeaponType.Shotgun, WeaponType.Rifle, WeaponType.Sniper, WeaponType.SMG };
        float[] offsets = { -2.4f, -0.6f, 1.2f, 3.0f };
        float[] depths = { 0.8f, -0.4f, 0.6f, 1.6f };
        for (int i = 0; i < weapons.Length; i++)
        {
            // Built through the runtime character pipeline (playables pose), which
            // renders multi-material skinned meshes correctly in edit mode.
            GameObject soldier = new GameObject("KeyArtSoldier" + i);
            soldier.AddComponent<CapsuleCollider>().center = Vector3.up;
            soldier.transform.SetParent(root.transform);
            Vector3 spot = origin + new Vector3(offsets[i], 0f, 14f + depths[i]);
            if (Physics.Raycast(spot + Vector3.up * 20f, Vector3.down, out RaycastHit ground, 40f, 1 << 7))
            {
                spot.y = ground.point.y; // stand on the terrain
            }
            soldier.transform.position = spot;
            soldier.transform.rotation = Quaternion.Euler(0f, 180f + (i - 1.5f) * 6f, 0f);
            AgentCharacterModel.TryApply(soldier, weapons[i]);
            Transform ring = soldier.transform.Find("CharacterModel/TeamRing");
            if (ring != null) Object.DestroyImmediate(ring.gameObject);
            soldier.GetComponent<AgentCharacterModel>().SetPreviewPose("Run_Gun", 0.15f + i * 0.17f);
            SetLayer(soldier, PortraitLayer);
        }

        // Two passes, composited: the map through the configured gameplay camera,
        // then the squad isolated on its own layer and light (the same path as
        // the portraits), which renders the multi-material characters reliably.
        Camera camera = Camera.main;
        camera.fieldOfView = 34f;
        camera.transform.position = origin + new Vector3(2.2f, 1.3f, 5.5f);
        camera.transform.LookAt(origin + new Vector3(0.6f, 1.6f, 16f));
        int previousMask = camera.cullingMask;
        camera.cullingMask = previousMask & ~(1 << PortraitLayer);
        Texture2D background = RenderToTexture(camera, 1920, 1080, false);
        camera.cullingMask = previousMask;

        // Front-left warm key (travels toward +Z, lighting the squad's faces).
        GameObject keyLight = CreatePortraitLight(Quaternion.Euler(32f, 20f, 0f), 1.6f);
        GameObject foregroundObject = new GameObject("KeyArtForeground");
        Camera foreground = foregroundObject.AddComponent<Camera>();
        foreground.CopyFrom(camera);
        foreground.cullingMask = 1 << PortraitLayer;
        foreground.clearFlags = CameraClearFlags.SolidColor;
        foreground.backgroundColor = new Color(0f, 0f, 0f, 0f);
        foreground.GetUniversalAdditionalCameraData().renderPostProcessing = false;

        // In batchmode the first draw of a freshly built skinned character uses
        // only its first material; one throwaway warm-up render avoids that.
        // Soldiers are then rendered one at a time, far to near, and composited.
        Color[] back = background.GetPixels();
        Transform[] soldiers = new Transform[root.transform.childCount];
        for (int s = 0; s < soldiers.Length; s++) soldiers[s] = root.transform.GetChild(s);
        System.Array.Sort(soldiers, (a, b) =>
            Vector3.Distance(b.position, camera.transform.position)
                .CompareTo(Vector3.Distance(a.position, camera.transform.position)));
        Object.DestroyImmediate(RenderToTexture(foreground, 64, 64, true));
        foreach (Transform soldier in soldiers)
        {
            foreach (Transform other in soldiers) other.gameObject.SetActive(other == soldier);
            Object.DestroyImmediate(RenderToTexture(foreground, 64, 64, true));
            Texture2D layer = RenderToTexture(foreground, 1920, 1080, true);
            Color[] front = layer.GetPixels();
            for (int p = 0; p < back.Length; p++)
            {
                back[p] = Color.Lerp(back[p], new Color(front[p].r, front[p].g, front[p].b, 1f), front[p].a);
            }
            Object.DestroyImmediate(layer);
        }
        background.SetPixels(back);
        background.Apply();
        File.WriteAllBytes(Path.Combine(OutputFolder, "MenuKeyArt.png"), background.EncodeToPNG());

        Object.DestroyImmediate(background);
        Object.DestroyImmediate(foregroundObject);
        Object.DestroyImmediate(keyLight);
        Object.DestroyImmediate(root);
    }

    private static void RenderPortrait(string modelPath, WeaponType weapon, string fileName)
    {
        GameObject model = Spawn(modelPath, weapon, "Idle_Shoot", 0.12f);
        if (modelPath.Contains("Enemy"))
        {
            Recolor(model);
        }
        Vector3 position = new Vector3(500f, 0f, 500f); // away from the map
        model.transform.position = position;
        model.transform.rotation = Quaternion.Euler(0f, 152f, 0f);
        SetLayer(model, PortraitLayer);

        GameObject lightObject = CreatePortraitLight(Quaternion.Euler(28f, 210f, 0f), 1.6f);

        GameObject cameraObject = new GameObject("PortraitCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.cullingMask = 1 << PortraitLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.fieldOfView = 26f;
        camera.transform.position = position + new Vector3(0f, 1.7f, -6.4f);
        camera.transform.LookAt(position + Vector3.up * 1.05f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        Save(camera, 384, 384, fileName, true);

        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(lightObject);
        Object.DestroyImmediate(model);
    }

    private static GameObject Spawn(string path, WeaponType weapon, string clipName, float time)
    {
        GameObject prefab = Resources.Load<GameObject>(path);
        GameObject instance = Object.Instantiate(prefab);
        AgentCharacterModel.ApplyMatteMaterials(instance.GetComponentsInChildren<Renderer>(true));
        string gunName = weapon switch
        {
            WeaponType.SMG => "SMG",
            WeaponType.Sniper => "Sniper",
            WeaponType.Shotgun => "Shotgun",
            _ => "AK"
        };
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (child.GetComponent<Renderer>() != null && child.parent != null &&
                child.parent.name.StartsWith("Index1.R"))
            {
                child.gameObject.SetActive(child.name == gunName);
            }
        }
        foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(path))
        {
            if (clip.name == clipName || clip.name.EndsWith("|" + clipName))
            {
                clip.SampleAnimation(instance, time * clip.length);
                break;
            }
        }
        return instance;
    }

    private static void Recolor(GameObject enemy)
    {
        foreach (Renderer renderer in enemy.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name.StartsWith("Enemy_Red"))
                {
                    materials[i] = new Material(materials[i]);
                    materials[i].SetColor("_BaseColor", new Color(0.16f, 0.34f, 0.72f));
                }
            }
            renderer.sharedMaterials = materials;
        }
    }

    private static Vector3 FindSpawn(string name)
    {
        GameObject spawn = GameObject.Find(name);
        return spawn != null ? new Vector3(spawn.transform.position.x, 0f, spawn.transform.position.z) : Vector3.zero;
    }

    private static void SetLayer(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }

    private static GameObject CreatePortraitLight(Quaternion rotation, float intensity)
    {
        GameObject lightObject = new GameObject("PortraitKey");
        Light key = lightObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1f, 0.93f, 0.82f);
        key.intensity = intensity;
        key.cullingMask = 1 << PortraitLayer;
        lightObject.transform.rotation = rotation;
        return lightObject;
    }

    private static Texture2D RenderToTexture(Camera camera, int width, int height, bool alpha)
    {
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        RenderTexture previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render(); // warm-up: the first batchmode draw can mis-assign materials
        camera.Render();
        RenderTexture.active = rt;
        Texture2D texture = new Texture2D(width, height, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        camera.targetTexture = previous;
        RenderTexture.active = null;
        rt.Release();
        return texture;
    }

    private static void Save(Camera camera, int width, int height, string fileName, bool alpha)
    {
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        Texture2D texture = new Texture2D(width, height, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(OutputFolder, fileName + ".png"), texture.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(texture);
        rt.Release();
    }
}
