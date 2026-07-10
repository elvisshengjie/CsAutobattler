using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class HudEditModePreview : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0.035f, 0.045f, 0.06f, 0.90f);
    private static readonly Color TextColor = new Color(0.94f, 0.97f, 1f, 1f);
    private static readonly Color AccentColor = new Color(0.20f, 0.86f, 0.96f, 1f);
    private static readonly Color WarningColor = new Color(1f, 0.35f, 0.25f, 1f);
    private static readonly Color RedPortraitColor = new Color(0.72f, 0f, 0f, 1f);
    private static readonly Color BluePortraitColor = new Color(0.10f, 0.22f, 1f, 1f);

    private const int ManualAbilityUses = 3;
    private Font font;

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            gameObject.SetActive(false);
            return;
        }

        EnsurePreviewHierarchy();
    }

    private void EnsurePreviewHierarchy()
    {
        if (transform.Find("EditableHudCanvas") != null)
        {
            return;
        }

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject(
            "EditableHudCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        BuildManualAbilityPreview(canvasObject.transform);
        BuildRoundTimerPreview(canvasObject.transform);
        BuildInitialTacticPreview(canvasObject.transform);
        BuildEnemyFlashcardPreview(canvasObject.transform);
        BuildDebugTogglePreview(canvasObject.transform);
        BuildObjectiveProgressPreview(canvasObject.transform);
    }

    private void BuildManualAbilityPreview(Transform parent)
    {
        GameObject panel = CreatePanel("ManualAbilityPreview", parent);
        SetRect(panel.GetComponent<RectTransform>(),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -18f), new Vector2(390f, 110f), new Vector2(0f, 1f));

        CreateText("Title", panel.transform,
            "MANUAL ABILITIES",
            22, FontStyle.Bold, WarningColor, TextAnchor.MiddleLeft,
            new Vector2(12f, -8f), new Vector2(-76f, -36f));
        CreateText("Count", panel.transform,
            $"{ManualAbilityUses}/{ManualAbilityUses}",
            23, FontStyle.Bold, WarningColor, TextAnchor.MiddleRight,
            new Vector2(-70f, -8f), new Vector2(-12f, -36f));
        CreateText("Hint", panel.transform,
            "Left-click a player to command their role ability",
            17, FontStyle.Bold, TextColor, TextAnchor.UpperLeft,
            new Vector2(12f, -40f), new Vector2(-12f, -8f));
    }

    private void BuildRoundTimerPreview(Transform parent)
    {
        GameObject panel = CreatePanel("RoundTimerPreview", parent);
        SetRect(panel.GetComponent<RectTransform>(),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(330f, 72f), new Vector2(0.5f, 1f));

        RoundManager round = FindAnyObjectByType<RoundManager>();
        float seconds = round != null ? round.preparationDuration : 5f;
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        string time = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        CreateText("Label", panel.transform,
            $"ROUND STARTS IN   {time}",
            24, FontStyle.Bold, AccentColor, TextAnchor.MiddleCenter,
            new Vector2(8f, -8f), new Vector2(-8f, 8f));
    }

    private void BuildInitialTacticPreview(Transform parent)
    {
        GameObject panel = CreatePanel("InitialTacticPreview", parent);
        SetRect(panel.GetComponent<RectTransform>(),
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-14f, -14f), new Vector2(220f, 74f), new Vector2(1f, 1f));
        AddOutline(panel);

        CreateText("Title", panel.transform,
            "INITIAL TACTIC:", 16, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft,
            new Vector2(14f, -8f), new Vector2(-14f, -38f));
        CreateText("Tactic", panel.transform,
            "Fast Execute", 16, FontStyle.Normal, AccentColor, TextAnchor.MiddleLeft,
            new Vector2(14f, -36f), new Vector2(-14f, -8f));
    }

    private void BuildEnemyFlashcardPreview(Transform parent)
    {
        GameObject group = new GameObject("EnemyFlashcardsPreview", typeof(RectTransform));
        group.transform.SetParent(parent, false);
        SetRect(group.GetComponent<RectTransform>(),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(18f, 110f), new Vector2(225f, 612f), new Vector2(0f, 0.5f));

        for (int i = 0; i < 5; i++)
        {
            GameObject card = CreatePanel("EnemyCard_" + (i + 1), group.transform);
            SetRect(card.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -i * 124f), new Vector2(225f, 116f), new Vector2(0f, 1f));

            GameObject portrait = CreatePanel("Portrait", card.transform, BluePortraitColor);
            SetRect(portrait.GetComponent<RectTransform>(),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, 0f), new Vector2(70f, 70f), new Vector2(0f, 0.5f));

            CreateText("HP", card.transform,
                "HP: 100/100", 15, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft,
                new Vector2(88f, -18f), new Vector2(-8f, -44f));
            CreateText("Details", card.transform,
                "Role: Unassigned\nWeapon: Rifle", 15, FontStyle.Bold, TextColor, TextAnchor.UpperLeft,
                new Vector2(88f, -66f), new Vector2(-8f, -8f));
        }
    }

    private void BuildDebugTogglePreview(Transform parent)
    {
        GameObject panel = CreatePanel("DebugTogglePreview", parent);
        SetRect(panel.GetComponent<RectTransform>(),
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-245f, -8f), new Vector2(150f, 104f), new Vector2(1f, 1f));

        string[] labels = { "AI States", "Path Lines", "Target Markers", "Heatmap" };
        for (int i = 0; i < labels.Length; i++)
        {
            GameObject box = CreatePanel("ToggleBox_" + labels[i], panel.transform,
                new Color(0.16f, 0.16f, 0.18f, 1f));
            SetRect(box.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(8f, -8f - i * 24f), new Vector2(16f, 16f), new Vector2(0f, 1f));

            CreateText("Label_" + labels[i], panel.transform,
                labels[i], 13, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft,
                new Vector2(32f, -5f - i * 24f), new Vector2(-8f, -26f - i * 24f));
        }
    }

    private void BuildObjectiveProgressPreview(Transform parent)
    {
        GameObject panel = CreatePanel("ObjectiveProgressPreview", parent);
        SetRect(panel.GetComponent<RectTransform>(),
            new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-16f, 44f), new Vector2(420f, 82f), new Vector2(1f, 0f));

        CreateText("Label", panel.transform,
            "STRIKERS PLANTING  |  8s",
            19, FontStyle.Bold, TextColor, TextAnchor.MiddleCenter,
            new Vector2(12f, -10f), new Vector2(-12f, 10f));
    }

    private GameObject CreatePanel(string objectName, Transform parent)
    {
        return CreatePanel(objectName, parent, PanelColor);
    }

    private static GameObject CreatePanel(string objectName, Transform parent, Color color)
    {
        GameObject panel = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        panel.transform.SetParent(parent, false);
        Image image = panel.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return panel;
    }

    private void CreateText(
        string objectName,
        Transform parent,
        string content,
        int fontSize,
        FontStyle style,
        Color color,
        TextAnchor alignment,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(offsetMin.x, offsetMax.y);
        rect.offsetMax = new Vector2(offsetMax.x, offsetMin.y);
    }

    private static void AddOutline(GameObject target)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = AccentColor;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 pivot)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.pivot = pivot;
    }
}
