using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gives the player a short real-time command window while the simulation
/// continues at a reduced speed. Focus drains and recovers in unscaled time.
/// </summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class TacticalSlowMotionController : MonoBehaviour
{
    public static TacticalSlowMotionController Instance { get; private set; }

    [Header("Time Dilation")]
    [SerializeField, Range(0.05f, 0.95f)] private float tacticalTimeScale = 0.2f;
    [SerializeField, Min(0f)] private float transitionDuration = 0.15f;

    [Header("Tactical Focus")]
    [SerializeField, Min(1f)] private float maximumFocus = 100f;
    [SerializeField, Min(0.1f)] private float focusDrainPerRealSecond = 25f;
    [SerializeField, Min(0f)] private float focusRecoveryPerRealSecond = 10f;
    [SerializeField, Min(0f)] private float minimumFocusToEnter = 15f;
    [SerializeField, Min(0.1f)] private float maximumContinuousUse = 3f;

    [Header("HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private Color tacticalTint = new Color(0.04f, 0.16f, 0.24f, 0.16f);
    [SerializeField] private Color focusColor = new Color(0.12f, 0.84f, 0.96f, 1f);
    [SerializeField] private Color lowFocusColor = new Color(1f, 0.42f, 0.18f, 1f);

    private RoundManager roundManager;
    private RedTeamStatsPanelUI redTeamStatsPanel;
    private readonly Vector3[] redTeamStatsCorners = new Vector3[4];
    private float focus;
    private float activeRealTime;
    private float normalTimeScale = 1f;
    private float fixedDeltaAtNormalSpeed = 0.02f;
    private float currentTimeScale = 1f;
    private bool isActive;
    private bool mustReleaseBeforeReenter;
    private GUIStyle titleStyle;
    private GUIStyle detailStyle;
    private GUIStyle focusStyle;
    private Texture2D panelTexture;
    private Texture2D barBackgroundTexture;
    private Texture2D focusTexture;
    private Texture2D lowFocusTexture;

    public bool IsActive => isActive;
    public float Focus => focus;
    public float MaximumFocus => maximumFocus;
    public float NormalizedFocus => maximumFocus <= 0f
        ? 0f
        : Mathf.Clamp01(focus / maximumFocus);
    public float TacticalTimeScale => tacticalTimeScale;
    public float MaximumContinuousUse => maximumContinuousUse;
    public bool CanEnter => IsCommandableNow() && !mustReleaseBeforeReenter &&
                            focus >= Mathf.Min(minimumFocusToEnter, maximumFocus);

    public event Action TacticalModeEntered;
    public event Action TacticalModeExited;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<RoundManager>() != null &&
            FindAnyObjectByType<TacticalSlowMotionController>() == null)
        {
            new GameObject("Tactical Slow Motion Controller")
                .AddComponent<TacticalSlowMotionController>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        normalTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        fixedDeltaAtNormalSpeed = Time.fixedDeltaTime /
                                  Mathf.Max(0.01f, normalTimeScale);
        currentTimeScale = normalTimeScale;
        focus = maximumFocus;
        BindRoundManager();
    }

    private void Start()
    {
        BindRoundManager();
        focus = maximumFocus;
    }

    private void Update()
    {
        BindRoundManager();

        bool spaceHeld = Keyboard.current != null &&
                         Keyboard.current.spaceKey.isPressed;
        if (!spaceHeld)
        {
            mustReleaseBeforeReenter = false;
        }

        bool wantsTacticalMode = spaceHeld &&
                                  (isActive || CanEnter) &&
                                  focus > 0f;
        if (wantsTacticalMode)
        {
            activeRealTime += Time.unscaledDeltaTime;
            focus = TacticalFocusMath.Drain(
                focus,
                focusDrainPerRealSecond,
                Time.unscaledDeltaTime);

            if (focus <= 0f || activeRealTime >= maximumContinuousUse)
            {
                wantsTacticalMode = false;
                mustReleaseBeforeReenter = true;
            }
        }
        else if (!spaceHeld)
        {
            focus = TacticalFocusMath.Recover(
                focus,
                maximumFocus,
                focusRecoveryPerRealSecond,
                Time.unscaledDeltaTime);
        }

        SetActive(wantsTacticalMode);
        UpdateTimeScale();
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
            roundManager.StateChanged -= OnRoundStateChanged;
        }

        roundManager = candidate;
        if (roundManager != null)
        {
            roundManager.StateChanged += OnRoundStateChanged;
        }
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Preparation)
        {
            focus = maximumFocus;
            mustReleaseBeforeReenter = true;
        }

        if (!IsCommandableRoundState(state))
        {
            SetActive(false);
        }
    }

    private bool IsCommandableNow()
    {
        if (TeamTacticManager.Instance != null &&
            TeamTacticManager.Instance.IsInitialSelectionBlockingInput)
        {
            return false;
        }

        return roundManager == null ||
               IsCommandableRoundState(roundManager.CurrentState);
    }

    public static bool IsCommandableRoundState(RoundState state)
    {
        return state == RoundState.Active ||
               state == RoundState.Planting ||
               state == RoundState.BombPlanted;
    }

    public bool ShouldPauseControlledAutoAbilities(TeamType team)
    {
        if (!isActive)
        {
            return false;
        }

        TeamType controlledTeam = TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.ControlledTeam
            : TeamType.Red;
        return team == controlledTeam;
    }

    private void SetActive(bool active)
    {
        if (isActive == active)
        {
            return;
        }

        isActive = active;
        activeRealTime = 0f;
        if (active)
        {
            TacticalModeEntered?.Invoke();
        }
        else
        {
            TacticalModeExited?.Invoke();
        }
    }

    private void UpdateTimeScale()
    {
        float targetScale = isActive ? tacticalTimeScale : normalTimeScale;
        if (transitionDuration <= 0f)
        {
            currentTimeScale = targetScale;
        }
        else
        {
            float speed = Mathf.Abs(normalTimeScale - tacticalTimeScale) /
                          transitionDuration;
            currentTimeScale = Mathf.MoveTowards(
                currentTimeScale,
                targetScale,
                speed * Time.unscaledDeltaTime);
        }

        ApplyTimeScale(currentTimeScale);
    }

    private void ApplyTimeScale(float scale)
    {
        Time.timeScale = Mathf.Max(0.01f, scale);
        Time.fixedDeltaTime = fixedDeltaAtNormalSpeed * Time.timeScale;
    }

    private void OnGUI()
    {
        if (!showHud || !IsCommandableNow())
        {
            return;
        }

        EnsureHudStyles();
        int previousDepth = GUI.depth;
        GUI.depth = -1000;

        if (isActive)
        {
            Color previousColor = GUI.color;
            GUI.color = tacticalTint;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        float width = Mathf.Min(520f, Screen.width - 36f);
        const float height = 94f;
        Rect panel;
        if (TryGetRedTeamStatsGuiRect(out Rect redTeamStatsRect))
        {
            panel = TacticalHudLayout.PlaceAbove(
                redTeamStatsRect,
                width,
                height,
                12f,
                Screen.width,
                18f);
        }
        else
        {
            panel = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - height - 24f,
                width,
                height);
        }
        GUI.DrawTexture(panel, panelTexture);

        string title;
        string detail;
        if (isActive)
        {
            title = $"TACTICAL COMMAND  {tacticalTimeScale * 100f:0}% SPEED";
            detail = "Select a player and issue a role ability command";
        }
        else if (mustReleaseBeforeReenter)
        {
            title = "TACTICAL FOCUS PAUSED";
            detail = "Release SPACE before entering again";
        }
        else if (focus < minimumFocusToEnter)
        {
            title = "TACTICAL FOCUS RECHARGING";
            detail = "Slow motion unlocks when the focus bar reaches the marker";
        }
        else
        {
            title = "HOLD SPACE  -  TACTICAL SLOW MOTION";
            detail = "The battle continues while you inspect and command your team";
        }

        GUI.Label(
            new Rect(panel.x + 14f, panel.y + 8f, panel.width - 28f, 27f),
            title,
            titleStyle);
        GUI.Label(
            new Rect(panel.x + 14f, panel.y + 34f, panel.width - 28f, 22f),
            detail,
            detailStyle);

        Rect bar = new Rect(panel.x + 18f, panel.y + 66f, panel.width - 96f, 14f);
        GUI.DrawTexture(bar, barBackgroundTexture);
        Rect fill = new Rect(
            bar.x + 2f,
            bar.y + 2f,
            (bar.width - 4f) * NormalizedFocus,
            bar.height - 4f);
        GUI.DrawTexture(
            fill,
            focus < minimumFocusToEnter ? lowFocusTexture : focusTexture);
        GUI.Label(
            new Rect(panel.x + panel.width - 72f, panel.y + 58f, 58f, 30f),
            $"{Mathf.CeilToInt(focus)}",
            focusStyle);

        GUI.depth = previousDepth;
    }

    private bool TryGetRedTeamStatsGuiRect(out Rect guiRect)
    {
        if (redTeamStatsPanel == null)
        {
            redTeamStatsPanel = FindAnyObjectByType<RedTeamStatsPanelUI>();
        }

        RectTransform panelRect = redTeamStatsPanel != null
            ? redTeamStatsPanel.transform as RectTransform
            : null;
        if (panelRect == null || !panelRect.gameObject.activeInHierarchy)
        {
            guiRect = default;
            return false;
        }

        Canvas canvas = panelRect.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            guiRect = default;
            return false;
        }

        Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera != null
                ? canvas.worldCamera
                : Camera.main;

        panelRect.GetWorldCorners(redTeamStatsCorners);
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int index = 0; index < redTeamStatsCorners.Length; index++)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
                canvasCamera,
                redTeamStatsCorners[index]);
            minimum = Vector2.Min(minimum, screenPoint);
            maximum = Vector2.Max(maximum, screenPoint);
        }

        float width = maximum.x - minimum.x;
        float height = maximum.y - minimum.y;
        if (width <= 1f || height <= 1f)
        {
            guiRect = default;
            return false;
        }

        guiRect = new Rect(
            minimum.x,
            Screen.height - maximum.y,
            width,
            height);
        return true;
    }

    private void EnsureHudStyles()
    {
        if (panelTexture != null)
        {
            return;
        }

        panelTexture = CreateTexture(
            "TacticalSlowMotionPanel",
            new Color(0.025f, 0.045f, 0.065f, 0.94f));
        barBackgroundTexture = CreateTexture(
            "TacticalFocusBackground",
            new Color(0.12f, 0.15f, 0.18f, 1f));
        focusTexture = CreateTexture("TacticalFocus", focusColor);
        lowFocusTexture = CreateTexture("TacticalFocusLow", lowFocusColor);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 0.9f, 1f, 1f) }
        };
        detailStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14,
            normal = { textColor = Color.white }
        };
        focusStyle = new GUIStyle(titleStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 19
        };
    }

    private static Texture2D CreateTexture(string textureName, Color color)
    {
        Texture2D texture = new Texture2D(1, 1)
        {
            name = textureName,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        return texture;
    }

    private void RestoreNormalTime()
    {
        isActive = false;
        currentTimeScale = normalTimeScale;
        Time.timeScale = normalTimeScale;
        Time.fixedDeltaTime = fixedDeltaAtNormalSpeed * normalTimeScale;
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            RestoreNormalTime();
        }
    }

    private void OnDestroy()
    {
        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }

        if (Instance == this)
        {
            RestoreNormalTime();
            Instance = null;
        }

        Destroy(panelTexture);
        Destroy(barBackgroundTexture);
        Destroy(focusTexture);
        Destroy(lowFocusTexture);
    }
}

public static class TacticalFocusMath
{
    public static float Drain(float current, float perSecond, float realDeltaTime)
    {
        return Mathf.Max(
            0f,
            current - Mathf.Max(0f, perSecond) * Mathf.Max(0f, realDeltaTime));
    }

    public static float Recover(
        float current,
        float maximum,
        float perSecond,
        float realDeltaTime)
    {
        return Mathf.Clamp(
            current + Mathf.Max(0f, perSecond) * Mathf.Max(0f, realDeltaTime),
            0f,
            Mathf.Max(0f, maximum));
    }
}

public static class TacticalHudLayout
{
    public static Rect PlaceAbove(
        Rect anchor,
        float width,
        float height,
        float gap,
        float screenWidth,
        float topPadding)
    {
        float safeWidth = Mathf.Min(Mathf.Max(0f, width), Mathf.Max(0f, screenWidth));
        float x = Mathf.Clamp(
            anchor.center.x - safeWidth * 0.5f,
            0f,
            Mathf.Max(0f, screenWidth - safeWidth));
        float y = Mathf.Max(Mathf.Max(0f, topPadding), anchor.y - height - gap);
        return new Rect(x, y, safeWidth, Mathf.Max(0f, height));
    }
}
