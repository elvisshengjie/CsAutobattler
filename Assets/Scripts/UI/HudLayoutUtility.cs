using UnityEngine;
using UnityEngine.UI;

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

        float scale = GetCanvasScale(source);
        Vector2 size = source.rect.size * scale;
        Vector2 position = GetGuiPosition(source, size, scale);
        rect = new Rect(position.x, position.y, size.x, size.y);
        return true;
    }

    public static bool TryGetScaledTextFontSize(
        string parentName,
        string textName,
        out int fontSize)
    {
        fontSize = 0;

        RectTransform parent = FindPreviewRect(parentName);
        if (parent == null)
        {
            return false;
        }

        Transform textTransform = FindChildRecursive(parent, textName);
        Text text = textTransform != null
            ? textTransform.GetComponent<Text>()
            : null;
        if (text == null)
        {
            return false;
        }

        float scale = GetCanvasScale(parent);
        fontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * scale));
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

    private static float GetCanvasScale(RectTransform rectTransform)
    {
        CanvasScaler scaler = rectTransform.GetComponentInParent<CanvasScaler>(true);
        if (scaler == null ||
            scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            return 1f;
        }

        Vector2 reference = scaler.referenceResolution;
        if (reference.x <= 0f || reference.y <= 0f)
        {
            return 1f;
        }

        float widthRatio = Screen.width / reference.x;
        float heightRatio = Screen.height / reference.y;
        float logWidth = Mathf.Log(widthRatio, 2f);
        float logHeight = Mathf.Log(heightRatio, 2f);
        return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
    }

    private static Vector2 GetGuiPosition(
        RectTransform rectTransform,
        Vector2 size,
        float scale)
    {
        Vector2 anchor = rectTransform.anchorMin;
        Vector2 anchored = rectTransform.anchoredPosition * scale;
        Vector2 pivot = rectTransform.pivot;

        float x = Screen.width * anchor.x + anchored.x - size.x * pivot.x;
        float y = Screen.height * (1f - anchor.y) - anchored.y - size.y * (1f - pivot.y);
        return new Vector2(x, y);
    }
}
