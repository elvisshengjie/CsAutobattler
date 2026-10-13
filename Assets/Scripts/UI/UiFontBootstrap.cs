using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Applies the game's UI font everywhere: the IMGUI skin (campaign screens,
/// HUD overlays, combat banners) and any uGUI Text still on Unity's default
/// font, including HUD panels baked into scenes by editor builders.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class UiFontBootstrap : MonoBehaviour
{
    private static UiFontBootstrap instance;
    private int sweepFrames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance == null)
        {
            GameObject host = new GameObject("UI Font");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<UiFontBootstrap>();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        sweepFrames = 3;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // HUD panels are also created in Start, so sweep for a few frames.
        sweepFrames = 3;
    }

    private void LateUpdate()
    {
        if (sweepFrames <= 0)
        {
            return;
        }
        sweepFrames--;
        Font font = HudLayoutUtility.UiFont;
        foreach (Text text in FindObjectsByType<Text>(FindObjectsInactive.Include))
        {
            if (text.font == null || text.font.name == "LegacyRuntime" || text.font.name == "Arial")
            {
                text.font = font;
            }
        }
    }

    private void OnGUI()
    {
        if (GUI.skin.font != HudLayoutUtility.UiFont)
        {
            GUI.skin.font = HudLayoutUtility.UiFont;
        }
    }
}
