using UnityEngine;

/// <summary>
/// Persistent, resolution-independent winner banner. It follows the same
/// immediate-mode UI approach as ObjectiveTimerUI and is created by RoundManager.
/// </summary>
public sealed class RoundResultUI : MonoBehaviour
{
    public static RoundResultUI Instance { get; private set; }

    [Header("Layout")]
    [SerializeField] private float widthFraction = 0.62f;
    [SerializeField] private float height = 112f;
    [SerializeField] private float topMargin = 36f;
    [SerializeField] private int titleFontSize = 32;
    [SerializeField] private int reasonFontSize = 18;

    [Header("Colors")]
    [SerializeField] private Color defenderBlue = new Color(0.06f, 0.3f, 0.88f, 0.96f);
    [SerializeField] private Color strikerRed = new Color(0.82f, 0.08f, 0.08f, 0.96f);

    private RoundManager roundManager;
    private Texture2D backgroundTexture;
    private GUIStyle backgroundStyle;
    private GUIStyle titleStyle;
    private GUIStyle reasonStyle;
    private TeamType displayedWinner;
    private string displayedReason;
    private Color displayedColor;
    private bool visible;
    private bool styleDirty;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        BindRoundManager();
    }

    private void Update()
    {
        if (roundManager == null)
        {
            BindRoundManager();
        }

        // Handles scene/script execution order where the result was declared
        // before this overlay subscribed.
        if (!visible && roundManager != null && roundManager.HasWinner)
        {
            ShowWinner(roundManager.Winner, roundManager.WinnerReason);
        }
    }

    public void ShowWinner(TeamType winner, RoundEndReason reason)
    {
        displayedWinner = winner;
        displayedReason = RoundManager.GetReasonDisplayName(reason);
        displayedColor = winner == TeamType.Blue ? defenderBlue : strikerRed;
        visible = true;
        styleDirty = true;
    }

    public void Hide()
    {
        visible = false;
    }

    private void BindRoundManager()
    {
        RoundManager candidate = RoundManager.Instance != null
            ? RoundManager.Instance
            : FindAnyObjectByType<RoundManager>();
        if (candidate == roundManager)
        {
            return;
        }

        if (roundManager != null)
        {
            roundManager.RoundResultDeclared -= ShowWinner;
            roundManager.StateChanged -= OnRoundStateChanged;
        }

        roundManager = candidate;
        if (roundManager != null)
        {
            roundManager.RoundResultDeclared += ShowWinner;
            roundManager.StateChanged += OnRoundStateChanged;
        }
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Preparation)
        {
            Hide();
        }
    }

    private void OnGUI()
    {
        if (!visible)
        {
            return;
        }

        EnsureStyles();
        float width = Mathf.Clamp(Screen.width * widthFraction, 420f, 920f);
        Rect panel = new Rect(
            (Screen.width - width) * 0.5f,
            topMargin,
            width,
            height);
        GUI.Box(panel, GUIContent.none, backgroundStyle);
        GUI.Label(
            new Rect(panel.x + 12f, panel.y + 12f, panel.width - 24f, 48f),
            displayedWinner == TeamType.Blue ? "DEFENDERS WIN" : "STRIKERS WIN",
            titleStyle);
        GUI.Label(
            new Rect(panel.x + 12f, panel.y + 62f, panel.width - 24f, 34f),
            displayedReason,
            reasonStyle);
    }

    private void EnsureStyles()
    {
        if (backgroundStyle == null || styleDirty)
        {
            RebuildStyles(displayedColor);
            styleDirty = false;
        }
    }

    private void RebuildStyles(Color color)
    {
        if (backgroundTexture != null)
        {
            Destroy(backgroundTexture);
        }

        backgroundTexture = new Texture2D(1, 1);
        backgroundTexture.name = "RoundResultBackground";
        backgroundTexture.SetPixel(0, 0, color);
        backgroundTexture.Apply();

        backgroundStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = backgroundTexture },
            border = new RectOffset(0, 0, 0, 0)
        };
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = titleFontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        reasonStyle = new GUIStyle(titleStyle)
        {
            fontSize = reasonFontSize,
            fontStyle = FontStyle.Normal
        };
    }

    private void OnDestroy()
    {
        if (roundManager != null)
        {
            roundManager.RoundResultDeclared -= ShowWinner;
            roundManager.StateChanged -= OnRoundStateChanged;
        }

        if (backgroundTexture != null)
        {
            Destroy(backgroundTexture);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
