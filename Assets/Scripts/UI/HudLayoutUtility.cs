using UnityEngine;

public static class HudLayoutUtility
{
    private const string CanvasName = "EditableHudCanvas";

    public static bool TryCopyPreviewRect(string objectName, RectTransform target)
    {
        RectTransform source = FindPreviewRect(objectName);
        if (source == null || target == null)
        {
            return false;
        }

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        return true;
    }

    public static bool TryGetGuiRect(string objectName, out Rect rect)
    {
        RectTransform source = FindPreviewRect(objectName);
        if (source == null)
        {
            rect = default;
            return false;
        }

        Vector2 size = source.rect.size;
        Vector2 position = GetGuiPosition(source, size);
        rect = new Rect(position.x, position.y, size.x, size.y);
        return true;
    }

    private static RectTransform FindPreviewRect(string objectName)
    {
        HudEditModePreview preview = FindPreview();
        if (preview == null)
        {
            return null;
        }

        Transform canvas = preview.transform.Find(CanvasName);
        if (canvas == null)
        {
            return null;
        }

        Transform target = FindChildRecursive(canvas, objectName);
        return target != null ? target as RectTransform : null;
    }

    private static HudEditModePreview FindPreview()
    {
        foreach (HudEditModePreview preview in Resources.FindObjectsOfTypeAll<HudEditModePreview>())
        {
            if (preview != null && preview.gameObject.scene.IsValid())
            {
                return preview;
            }
        }

        return null;
    }

    private static Transform FindChildRecursive(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform match = FindChildRecursive(parent.GetChild(i), objectName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static Vector2 GetGuiPosition(RectTransform rectTransform, Vector2 size)
    {
        Vector2 anchor = rectTransform.anchorMin;
        Vector2 anchored = rectTransform.anchoredPosition;
        Vector2 pivot = rectTransform.pivot;

        float x = Screen.width * anchor.x + anchored.x - size.x * pivot.x;
        float y = Screen.height * (1f - anchor.y) - anchored.y - size.y * (1f - pivot.y);
        return new Vector2(x, y);
    }
}
