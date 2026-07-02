using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the tactic controls on the existing screen at runtime. Every button writes
/// to TeamTacticManager, so visual selection and live AI state cannot drift apart.
/// </summary>
[DisallowMultipleComponent]
public sealed class TeamTacticUI : MonoBehaviour
{
    private static readonly Color InactiveColor = new Color(0.10f, 0.12f, 0.15f, 0.96f);
    private static readonly Color ActiveColor = new Color(0.08f, 0.48f, 0.60f, 0.98f);
    private static readonly Color AccentColor = new Color(0.20f, 0.86f, 0.96f, 1f);
    private static readonly Color TextColor = new Color(0.94f, 0.97f, 1f, 1f);

    private TeamTacticManager tacticManager;
    private RoundManager roundManager;
    private Font font;
    private GameObject initialOverlay;
    private GameObject currentTacticPanel;
    private GameObject midRoundPanel;
    private Text currentTacticText;
    private readonly Dictionary<MidRoundTactic, Button> midRoundButtons =
        new Dictionary<MidRoundTactic, Button>();
    private readonly Dictionary<MidRoundTactic, Text> midRoundLabels =
        new Dictionary<MidRoundTactic, Text>();

    private void Awake()
    {
        tacticManager = GetComponent<TeamTacticManager>();
    }

    private void Start()
    {
        roundManager = RoundManager.Instance != null
            ? RoundManager.Instance
            : FindAnyObjectByType<RoundManager>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();

        tacticManager.InitialTacticSelected += OnInitialTacticSelected;
        tacticManager.TacticsChanged += Refresh;
        tacticManager.RoundTacticsReset += Refresh;
        if (roundManager != null)
        {
            roundManager.StateChanged += OnRoundStateChanged;
        }

        Refresh();
    }

    private void OnDestroy()
    {
        if (tacticManager != null)
        {
            tacticManager.InitialTacticSelected -= OnInitialTacticSelected;
            tacticManager.TacticsChanged -= Refresh;
            tacticManager.RoundTacticsReset -= Refresh;
        }

        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject(
            "TeamTacticCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        BuildInitialSelection(canvasObject.transform);
        BuildCurrentTacticPanel(canvasObject.transform);
        BuildMidRoundPanel(canvasObject.transform);
    }

    private void BuildInitialSelection(Transform parent)
    {
        initialOverlay = CreatePanel("InitialTacticOverlay", parent,
            new Color(0.015f, 0.025f, 0.04f, 0.82f));
        RectTransform overlayRect = initialOverlay.GetComponent<RectTransform>();
        StretchToParent(overlayRect);

        GameObject window = CreatePanel("SelectionWindow", initialOverlay.transform,
            new Color(0.055f, 0.07f, 0.095f, 0.99f));
        SetRect(window.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1080f, 590f),
            new Vector2(0.5f, 0.5f));
        AddOutline(window, AccentColor, new Vector2(2f, -2f));

        Text title = CreateText("Title", window.transform,
            "CHOOSE INITIAL TEAM TACTIC", 30, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -48f), new Vector2(960f, 55f), new Vector2(0.5f, 0.5f));
        title.color = AccentColor;

        Text subtitle = CreateText("Subtitle", window.transform,
            "The attacking team waits here until you choose its baseline plan.",
            17, FontStyle.Normal, TextAnchor.MiddleCenter);
        SetRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -88f), new Vector2(960f, 34f), new Vector2(0.5f, 0.5f));
        subtitle.color = new Color(0.72f, 0.78f, 0.84f, 1f);

        InitialTeamTactic[] tactics = (InitialTeamTactic[])Enum.GetValues(
            typeof(InitialTeamTactic));
        Vector2[] positions =
        {
            new Vector2(-255f, 105f),
            new Vector2(255f, 105f),
            new Vector2(-255f, -125f),
            new Vector2(255f, -125f)
        };

        for (int i = 0; i < tactics.Length; i++)
        {
            InitialTeamTactic captured = tactics[i];
            string label = $"<b>{TeamTacticDefinitions.GetName(captured)}</b>\n\n" +
                           TeamTacticDefinitions.GetShortDescription(captured);
            Button button = CreateTacticButton(
                TeamTacticDefinitions.GetName(captured),
                window.transform,
                label,
                18);
            SetRect(button.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                positions[i], new Vector2(470f, 195f), new Vector2(0.5f, 0.5f));
            button.onClick.AddListener(() => tacticManager.SelectInitialTactic(captured));
        }
    }

    private void BuildCurrentTacticPanel(Transform parent)
    {
        currentTacticPanel = CreatePanel("CurrentInitialTactic", parent,
            new Color(0.045f, 0.06f, 0.08f, 0.97f));
        SetRect(currentTacticPanel.GetComponent<RectTransform>(),
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-20f, -20f), new Vector2(340f, 92f), new Vector2(1f, 1f));
        AddOutline(currentTacticPanel, AccentColor, new Vector2(-2f, -2f));

        currentTacticText = CreateText("CurrentTacticText", currentTacticPanel.transform,
            string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft);
        StretchToParent(currentTacticText.rectTransform, 18f, 18f, 10f, 10f);
    }

    private void BuildMidRoundPanel(Transform parent)
    {
        midRoundPanel = CreatePanel("MidRoundTactics", parent,
            new Color(0.035f, 0.045f, 0.06f, 0.93f));
        SetRect(midRoundPanel.GetComponent<RectTransform>(),
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-20f, -126f), new Vector2(360f, 615f), new Vector2(1f, 1f));

        Text heading = CreateText("Heading", midRoundPanel.transform,
            "MID-ROUND TACTICS", 20, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(heading.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -30f), new Vector2(-28f, 42f), new Vector2(0.5f, 0.5f));
        heading.color = AccentColor;

        Text hint = CreateText("Hint", midRoundPanel.transform,
            "Toggle any combination • click again to cancel", 13,
            FontStyle.Normal, TextAnchor.MiddleLeft);
        SetRect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -61f), new Vector2(-28f, 28f), new Vector2(0.5f, 0.5f));
        hint.color = new Color(0.64f, 0.70f, 0.76f, 1f);

        MidRoundTactic[] tactics = (MidRoundTactic[])Enum.GetValues(typeof(MidRoundTactic));
        for (int i = 0; i < tactics.Length; i++)
        {
            MidRoundTactic captured = tactics[i];
            Button button = CreateTacticButton(
                TeamTacticDefinitions.GetName(captured),
                midRoundPanel.transform,
                GetMidRoundLabel(captured, false),
                14);
            SetRect(button.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -112f - i * 101f),
                new Vector2(-28f, 88f), new Vector2(0.5f, 0.5f));
            button.onClick.AddListener(() => tacticManager.ToggleMidRoundTactic(captured));
            midRoundButtons[captured] = button;
            midRoundLabels[captured] = button.GetComponentInChildren<Text>();
        }
    }

    private Button CreateTacticButton(
        string objectName,
        Transform parent,
        string label,
        int fontSize)
    {
        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(Outline));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = InactiveColor;
        image.raycastTarget = true;

        Outline outline = buttonObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.28f, 0.33f, 0.39f, 1f);
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.75f, 0.82f, 0.88f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Text text = CreateText("Label", buttonObject.transform, label, fontSize,
            FontStyle.Normal, TextAnchor.MiddleLeft);
        StretchToParent(text.rectTransform, 18f, 18f, 10f, 10f);
        text.raycastTarget = false;
        text.supportRichText = true;
        return button;
    }

    private void Refresh()
    {
        if (tacticManager == null || roundManager == null || initialOverlay == null)
        {
            return;
        }

        bool controlledRound = tacticManager.ControlsAttackingTeam(roundManager);
        bool selected = tacticManager.HasSelectedInitialTactic;
        bool roundEnded = roundManager.CurrentState == RoundState.RoundEnd;

        initialOverlay.SetActive(controlledRound &&
                                 roundManager.CurrentState == RoundState.Preparation &&
                                 !selected);
        currentTacticPanel.SetActive(controlledRound && selected && !roundEnded);
        midRoundPanel.SetActive(controlledRound && selected &&
                                roundManager.CurrentState != RoundState.Preparation &&
                                !roundEnded);

        if (selected)
        {
            currentTacticText.text = "<b>INITIAL TACTIC:</b>\n" +
                                     $"<color=#33DBF5>{TeamTacticDefinitions.GetName(tacticManager.GetSelectedInitialTactic())}</color>";
        }

        foreach (KeyValuePair<MidRoundTactic, Button> pair in midRoundButtons)
        {
            bool active = tacticManager.IsMidRoundTacticActive(pair.Key);
            Image image = pair.Value.GetComponent<Image>();
            Outline outline = pair.Value.GetComponent<Outline>();
            image.color = active ? ActiveColor : InactiveColor;
            outline.effectColor = active ? AccentColor : new Color(0.28f, 0.33f, 0.39f, 1f);
            outline.effectDistance = active ? new Vector2(2f, -2f) : new Vector2(1f, -1f);

            bool queued = active && pair.Key == MidRoundTactic.PostPlantLockdown &&
                          roundManager.CurrentState != RoundState.BombPlanted;
            midRoundLabels[pair.Key].text = GetMidRoundLabel(pair.Key, queued);
        }
    }

    private string GetMidRoundLabel(MidRoundTactic tactic, bool queued)
    {
        string status = queued
            ? "\n<color=#FFD166><b>QUEUED UNTIL PLANT</b></color>"
            : string.Empty;
        return $"<b>{TeamTacticDefinitions.GetName(tactic)}</b>\n" +
               TeamTacticDefinitions.GetShortDescription(tactic) + status;
    }

    private void OnInitialTacticSelected(InitialTeamTactic tactic)
    {
        Refresh();
    }

    private void OnRoundStateChanged(RoundState state)
    {
        Refresh();
    }

    private Text CreateText(
        string objectName,
        Transform parent,
        string content,
        int fontSize,
        FontStyle style,
        TextAnchor alignment)
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
        text.alignment = alignment;
        text.color = TextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = true;
        text.raycastTarget = false;
        return text;
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
        image.raycastTarget = objectName == "InitialTacticOverlay";
        return panel;
    }

    private static void AddOutline(GameObject target, Color color, Vector2 distance)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
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

    private static void StretchToParent(
        RectTransform rect,
        float left = 0f,
        float right = 0f,
        float top = 0f,
        float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
