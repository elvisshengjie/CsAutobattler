using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class HudEditModePreviewBootstrap
{
    private const string PreviewObjectName = "HUD Edit Mode Preview";

    static HudEditModePreviewBootstrap()
    {
        EditorApplication.update += EnsurePreview;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void EnsurePreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (Object.FindAnyObjectByType<RoundManager>() == null)
        {
            RemovePreview();
            return;
        }

        HudEditModePreview existing = FindPreview();
        if (existing != null)
        {
            return;
        }

        GameObject preview = new GameObject(PreviewObjectName)
        {
            hideFlags = HideFlags.None
        };
        preview.AddComponent<HudEditModePreview>();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EnsurePreview();
        }
    }

    private static void RemovePreview()
    {
        HudEditModePreview existing = FindPreview();
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }
    }

    private static HudEditModePreview FindPreview()
    {
        foreach (HudEditModePreview preview in Resources.FindObjectsOfTypeAll<HudEditModePreview>())
        {
            if (preview != null && preview.gameObject.name == PreviewObjectName)
            {
                return preview;
            }
        }

        return null;
    }
}
