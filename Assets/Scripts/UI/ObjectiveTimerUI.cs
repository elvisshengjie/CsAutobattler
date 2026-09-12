using UnityEngine;

/// <summary>
/// Compact objective countdown beside the bottom HUD. Created automatically by ObjectiveManager.
/// </summary>
public class ObjectiveTimerUI : MonoBehaviour
{
    [Header("Layout")]
    public float width = 420f;
    public float height = 82f;
    public float rightMargin = 16f;
    public float bottomMargin = 44f;
    public int fontSize = 19;

    private GUIStyle panelStyle;
    private GUIStyle backgroundStyle;
    private GUIStyle textStyle;
    private GUIStyle defuseTitleStyle;
    private GUIStyle defuseDetailStyle;
    private GUIStyle roundTimerStyle;
    private Texture2D panelTexture;
    private Texture2D defuseFillTexture;
    private Texture2D tooLateFillTexture;
    private Texture2D progressBackgroundTexture;

    private void OnGUI()
    {
        RoundManager round = RoundManager.Instance;
        ObjectiveManager objective = ObjectiveManager.Instance;
        if (round == null || objective == null)
        {
            return;
        }

        EnsureStyles();
        DrawRoundTimer(round);

        string message = GetMessage(round, objective);
        if (HudLayoutUtility.TryGetScaledTextFontSize(
            "ObjectiveProgressPreview",
            "Label",
            out int previewFontSize))
        {
            textStyle.fontSize = previewFontSize;
        }
        else
        {
            textStyle.fontSize = fontSize;
        }

        Rect panel;
        if (!HudLayoutUtility.TryGetGuiRect("ObjectiveProgressPreview", out panel))
        {
            float contentWidth = textStyle.CalcSize(new GUIContent(message)).x + 30f;
            float fittedWidth = Mathf.Clamp(
                contentWidth,
                260f,
                Mathf.Min(width, Screen.width - rightMargin * 2f));
            panel = new Rect(
                Screen.width - fittedWidth - rightMargin,
                Screen.height - height - bottomMargin,
                fittedWidth,
                height);
        }
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Box(new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f, panel.height - 6f),
            GUIContent.none,
            backgroundStyle);
        GUI.Label(panel, message, textStyle);

        if (ShouldShowEnemyDefuse(round, objective))
        {
            DrawEnemyDefuseAlert(round, objective);
        }
    }

    private void DrawRoundTimer(RoundManager round)
    {
        string label;
        float seconds;
        if (round.CurrentState == RoundState.Preparation)
        {
            label = "ROUND STARTS IN";
            seconds = round.PreparationTimeRemaining;
        }
        else if (round.CurrentState == RoundState.Active ||
                 round.CurrentState == RoundState.Planting)
        {
            label = "ROUND";
            seconds = round.RoundTimeRemaining;
        }
        else
        {
            return;
        }

        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        string time = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        Rect panel;
        if (!HudLayoutUtility.TryGetGuiRect("RoundTimerPreview", out panel))
        {
            const float timerWidth = 330f;
            const float timerHeight = 72f;
            panel = new Rect((Screen.width - timerWidth) * 0.5f, 16f,
                timerWidth, timerHeight);
        }
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Box(new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f,
            panel.height - 6f), GUIContent.none, backgroundStyle);
        GUI.Label(panel, $"{label}   {time}", roundTimerStyle);
    }

    private bool ShouldShowEnemyDefuse(RoundManager round, ObjectiveManager objective)
    {
        if (!objective.IsDefusing)
        {
            return false;
        }

        TeamType playerTeam = TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.ControlledTeam
            : round.attackingTeam;
        return round.defendingTeam != playerTeam;
    }

    private void DrawEnemyDefuseAlert(RoundManager round, ObjectiveManager objective)
    {
        float alertWidth = Mathf.Min(520f, Screen.width - 40f);
        const float alertHeight = 112f;
        Rect alert = new Rect((Screen.width - alertWidth) * 0.5f, 82f,
            alertWidth, alertHeight);
        GUI.Box(alert, GUIContent.none, panelStyle);
        GUI.Box(new Rect(alert.x + 3f, alert.y + 3f, alert.width - 6f,
            alert.height - 6f), GUIContent.none, backgroundStyle);

        float remaining = Mathf.Max(0f, round.defuseDuration - objective.DefuseProgress);
        bool tooLate = remaining > round.BombTimeRemaining + 0.01f;
        string defuserName = objective.ActiveDefuser != null
            ? objective.ActiveDefuser.name
            : "Defender";
        GUI.Label(new Rect(alert.x + 12f, alert.y + 9f, alert.width - 24f, 32f),
            tooLate ? "ENEMY DEFUSING - TOO LATE" : "ENEMY IS DEFUSING!",
            defuseTitleStyle);
        GUI.Label(new Rect(alert.x + 12f, alert.y + 39f, alert.width - 24f, 24f),
            $"{defuserName}  |  {remaining:0.0}s remaining  |  Bomb {round.BombTimeRemaining:0.0}s",
            defuseDetailStyle);

        Rect bar = new Rect(alert.x + 22f, alert.y + 75f, alert.width - 44f, 20f);
        GUI.DrawTexture(bar, progressBackgroundTexture);
        float progress = round.defuseDuration <= 0f ? 1f :
            Mathf.Clamp01(objective.DefuseProgress / round.defuseDuration);
        Rect fill = new Rect(bar.x + 2f, bar.y + 2f,
            (bar.width - 4f) * progress, bar.height - 4f);
        GUI.DrawTexture(fill, tooLate ? tooLateFillTexture : defuseFillTexture);
    }

    private static string GetMessage(RoundManager round, ObjectiveManager objective)
    {
        if (objective.IsDefusing)
        {
            float remaining = Mathf.Max(0f, round.defuseDuration - objective.DefuseProgress);
            return $"{TeamName(round.defendingTeam)} DEFUSING  |  {DisplaySeconds(remaining)}s";
        }

        if (objective.IsPlanting)
        {
            float remaining = Mathf.Max(0f, round.plantDuration - objective.PlantProgress);
            return $"{TeamName(round.attackingTeam)} PLANTING  |  {DisplaySeconds(remaining)}s";
        }

        if (round.CurrentState == RoundState.BombPlanted)
        {
            return $"BOMB EXPLODES  |  {DisplaySeconds(round.BombTimeRemaining)}s";
        }

        return "OBJECTIVE READY";
    }

    private static int DisplaySeconds(float seconds)
    {
        return Mathf.CeilToInt(seconds);
    }

    private static string TeamName(TeamType team)
    {
        return team == TeamType.Blue ? "DEFENDERS" : "STRIKERS";
    }

    private void EnsureStyles()
    {
        if (panelStyle != null)
        {
            return;
        }

        panelTexture = new Texture2D(1, 1);
        panelTexture.name = "ObjectiveTimerPanelBackground";
        panelTexture.SetPixel(0, 0, HudLayoutUtility.TacticalPanel);
        panelTexture.Apply();

        panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTexture }, border = new RectOffset(0, 0, 0, 0) };
        backgroundStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = panelTexture },
            border = new RectOffset(0, 0, 0, 0)
        };

        textStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            normal = { textColor = Color.white }
        };

        defuseTitleStyle = new GUIStyle(textStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            normal = { textColor = new Color(1f, 0.32f, 0.22f) }
        };
        defuseDetailStyle = new GUIStyle(textStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15
        };
        roundTimerStyle = new GUIStyle(textStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24,
            normal = { textColor = HudLayoutUtility.TacticalAccent }
        };
        progressBackgroundTexture = CreateTexture("DefuseProgressBackground",
            new Color(0.12f, 0.13f, 0.15f, 1f));
        defuseFillTexture = CreateTexture("DefuseProgressFill",
            HudLayoutUtility.TacticalAccent);
        tooLateFillTexture = CreateTexture("DefuseTooLateFill",
            new Color(0.95f, 0.18f, 0.12f, 1f));
    }

    private static Texture2D CreateTexture(string textureName, Color color)
    {
        Texture2D texture = new Texture2D(1, 1) { name = textureName };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void OnDestroy()
    {
        if (panelTexture != null)
        {
            Destroy(panelTexture);
        }
        if (defuseFillTexture != null) Destroy(defuseFillTexture);
        if (tooLateFillTexture != null) Destroy(tooLateFillTexture);
        if (progressBackgroundTexture != null) Destroy(progressBackgroundTexture);
    }
}
