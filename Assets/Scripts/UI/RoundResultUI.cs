using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Full-screen round result overlay. Reloading the battle scene gives the next
/// round fresh agents, health, objective state, positions, and AI state.
/// </summary>
[DisallowMultipleComponent]
public sealed class RoundResultUI : MonoBehaviour
{
    private RoundManager roundManager;
    private GameObject overlay;
    private Text resultText;
    private Font font;

    private void Awake()
    {
        roundManager = GetComponent<RoundManager>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildInterface();
    }

    private void OnEnable()
    {
        if (roundManager != null)
        {
            roundManager.RoundEnded += ShowResult;
        }
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.RoundEnded -= ShowResult;
        }
    }

    private void ShowResult(TeamType winningTeam)
    {
        if (overlay == null || resultText == null || roundManager == null)
        {
            return;
        }

        bool attackersWon = winningTeam == roundManager.attackingTeam;
        resultText.text = attackersWon ? "ATTACKER WIN" : "DEFENDER WIN";
        resultText.color = attackersWon
            ? new Color(1f, 0.3f, 0.24f)
            : new Color(0.2f, 0.75f, 1f);
        overlay.SetActive(true);
    }

    private void StartNextRound()
    {
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.name);
    }

    private void BuildInterface()
    {
        EnsureEventSystem();

        GameObject canvasObject = new GameObject(
            "RoundResultCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        overlay = new GameObject("RoundResultOverlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvasObject.transform, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.035f, 0.9f);

        GameObject panel = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(overlay.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 330f);
        panelRect.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.055f, 0.07f, 0.11f, 0.98f);

        resultText = CreateText(
            "ResultText",
            panel.transform,
            "DEFENDER WIN",
            52,
            FontStyle.Bold);
        RectTransform resultRect = resultText.rectTransform;
        resultRect.anchorMin = new Vector2(0f, 0.55f);
        resultRect.anchorMax = new Vector2(1f, 0.95f);
        resultRect.offsetMin = new Vector2(30f, 0f);
        resultRect.offsetMax = new Vector2(-30f, 0f);

        Text subtitle = CreateText(
            "Subtitle",
            panel.transform,
            "ROUND COMPLETE",
            20,
            FontStyle.Normal);
        subtitle.color = new Color(0.75f, 0.8f, 0.88f);
        RectTransform subtitleRect = subtitle.rectTransform;
        subtitleRect.anchorMin = new Vector2(0f, 0.43f);
        subtitleRect.anchorMax = new Vector2(1f, 0.58f);
        subtitleRect.offsetMin = new Vector2(30f, 0f);
        subtitleRect.offsetMax = new Vector2(-30f, 0f);

        GameObject buttonObject = new GameObject(
            "NextRoundButton",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(panel.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.22f);
        buttonRect.sizeDelta = new Vector2(280f, 72f);
        buttonRect.anchoredPosition = Vector2.zero;

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.1f, 0.55f, 0.78f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.18f, 0.7f, 0.95f, 1f);
        colors.pressedColor = new Color(0.06f, 0.4f, 0.62f, 1f);
        button.colors = colors;
        button.onClick.AddListener(StartNextRound);

        Text buttonText = CreateText(
            "Label",
            buttonObject.transform,
            "NEXT ROUND",
            24,
            FontStyle.Bold);
        buttonText.color = Color.white;
        RectTransform buttonTextRect = buttonText.rectTransform;
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        overlay.SetActive(false);
    }

    private Text CreateText(
        string objectName,
        Transform parent,
        string value,
        int size,
        FontStyle style)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
        eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
    }
}
