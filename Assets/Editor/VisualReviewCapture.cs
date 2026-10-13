using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batchmode review helper: rebuilds visuals and renders review screenshots.
// ART_OUT=<dir> Unity -batchmode -executeMethod VisualReviewCapture.BuildAndCapture -quit
public static class VisualReviewCapture
{
    public static void BuildAndCapture()
    {
        VisualOverhaulBuilder.BuildAll();
        Capture();
    }

    public static void Capture()
    {
        string outDir = System.Environment.GetEnvironmentVariable("ART_OUT");
        string tag = System.Environment.GetEnvironmentVariable("ART_TAG") ?? "after";
        // Async compilation would capture placeholder/stale shader variants.
        bool previousAsync = EditorSettings.asyncShaderCompilation;
        EditorSettings.asyncShaderCompilation = false;
        try
        {
            CaptureScene(outDir, tag);
        }
        finally
        {
            EditorSettings.asyncShaderCompilation = previousAsync;
        }
    }

    private static void CaptureScene(string outDir, string tag)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map1.unity");

        foreach (AgentStats agent in Object.FindObjectsByType<AgentStats>(FindObjectsInactive.Include))
        {
            if (agent.GetComponent<AgentController3D>() == null) continue;
            agent.gameObject.SetActive(true);
            WeaponLoadout loadout = agent.GetComponent<WeaponLoadout>();
            if (AgentCharacterModel.TryApply(agent.gameObject, loadout != null ? loadout.SelectedWeapon : WeaponType.Rifle))
            {
                foreach (Renderer r in agent.GetComponents<Renderer>()) r.enabled = false;
                Transform generated = agent.transform.Find("GeneratedHumanVisual");
                if (generated != null) generated.gameObject.SetActive(false);
            }
        }

        Camera cam = Camera.main;
        Render(cam, Path.Combine(outDir, $"map1_{tag}.png"));

        // Close-up on the player spawn group.
        AgentStats first = null;
        foreach (AgentStats agent in Object.FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
            if (agent.team == TeamType.Red && agent.GetComponent<AgentController3D>() != null) { first = agent; break; }
        if (first != null)
        {
            Vector3 focus = first.transform.position;
            cam.transform.position = focus + new Vector3(3.5f, 4.5f, -6.5f);
            cam.transform.LookAt(focus + Vector3.up * 0.8f);
            cam.fieldOfView = 45f;
            Render(cam, Path.Combine(outDir, $"closeup_{tag}.png"));
        }
        foreach (AgentStats agent in Object.FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (agent.team != TeamType.Blue || agent.GetComponent<AgentController3D>() == null) continue;
            Vector3 focus = agent.transform.position;
            cam.transform.position = focus + new Vector3(-3f, 4f, 6f);
            cam.transform.LookAt(focus + Vector3.up * 0.8f);
            Render(cam, Path.Combine(outDir, $"enemy_{tag}.png"));
            break;
        }
    }

    private static void Render(Camera cam, string file)
    {
        const int w = 1600, h = 900;
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        RenderTexture previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = previous;
        RenderTexture.active = null;
    }
}
