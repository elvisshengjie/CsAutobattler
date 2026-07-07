using UnityEngine;

/// <summary>
/// Compact objective countdown beside the bottom HUD. Created automatically by ObjectiveManager.
/// </summary>
public class ObjectiveTimerUI : MonoBehaviour
{
    [Header("Layout")]
    public float width = 300f;
    public float height = 40f;
    public float rightMargin = 16f;
    public float bottomMargin = 82f;
    public int fontSize = 14;

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
    private float cachedStyleScale = -1f;

    private void OnGUI()
    {
        RoundManager round = RoundManager.Instance;
        ObjectiveManager objective = ObjectiveManager.Instance;
        if (round == null || objective == null)
        {
            return;
        }

        float uiScale = GetUiScale();
        EnsureStyles(uiScale);
        DrawRoundTimer(round);

        string message = GetMessage(round, objective);
        if (!string.IsNullOrEmpty(message))
        {
            float contentWidth =
                textStyle.CalcSize(new GUIContent(message)).x + 34f * uiScale;
            float fittedWidth = Mathf.Clamp(
                contentWidth,
                220f * uiScale,
                Mathf.Min(width * uiScale, Screen.width - rightMargin * uiScale * 2f));
            Rect panel = new Rect(
                Screen.width - fittedWidth - rightMargin * uiScale,
                Screen.height - height * uiScale - bottomMargin * uiScale,
                fittedWidth,
                height * uiScale);
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.Box(new Rect(panel.x + 3f * uiScale, panel.y + 3f * uiScale,
                    panel.width - 6f * uiScale, panel.height - 6f * uiScale),
                GUIContent.none,
                backgroundStyle);
            GUI.Label(panel, message, textStyle);
        }

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
        float uiScale = GetUiScale();
        float timerWidth = 310f * uiScale;
        float timerHeight = 66f * uiScale;
        Rect panel = new Rect((Screen.width - timerWidth) * 0.5f, 16f,
            timerWidth, timerHeight);
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Box(new Rect(panel.x + 3f * uiScale, panel.y + 3f * uiScale,
            panel.width - 6f * uiScale, panel.height - 6f * uiScale),
            GUIContent.none, backgroundStyle);
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
        float uiScale = GetUiScale();
        float alertWidth = Mathf.Min(620f * uiScale, Screen.width - 40f * uiScale);
        float alertHeight = 132f * uiScale;
        Rect alert = new Rect((Screen.width - alertWidth) * 0.5f, 86f * uiScale,
            alertWidth, alertHeight);
        GUI.Box(alert, GUIContent.none, panelStyle);
        GUI.Box(new Rect(alert.x + 3f * uiScale, alert.y + 3f * uiScale,
            alert.width - 6f * uiScale, alert.height - 6f * uiScale),
            GUIContent.none, backgroundStyle);

        float remaining = Mathf.Max(0f, round.defuseDuration - objective.DefuseProgress);
        bool tooLate = remaining > round.BombTimeRemaining + 0.01f;
        string defuserName = objective.ActiveDefuser != null
            ? objective.ActiveDefuser.name
            : "Defender";
        GUI.Label(new Rect(alert.x + 14f * uiScale, alert.y + 10f * uiScale,
                alert.width - 28f * uiScale, 38f * uiScale),
            tooLate ? "ENEMY DEFUSING - TOO LATE" : "ENEMY IS DEFUSING!",
            defuseTitleStyle);
        GUI.Label(new Rect(alert.x + 14f * uiScale, alert.y + 48f * uiScale,
                alert.width - 28f * uiScale, 28f * uiScale),
            $"{defuserName}  |  {remaining:0.0}s remaining  |  Bomb {round.BombTimeRemaining:0.0}s",
            defuseDetailStyle);

        Rect bar = new Rect(alert.x + 24f * uiScale, alert.y + 88f * uiScale,
            alert.width - 48f * uiScale, 24f * uiScale);
        GUI.DrawTexture(bar, progressBackgroundTexture);
        float progress = round.defuseDuration <= 0f ? 1f :
            Mathf.Clamp01(objective.DefuseProgress / round.defuseDuration);
        Rect fill = new Rect(bar.x + 2f * uiScale, bar.y + 2f * uiScale,
            (bar.width - 4f * uiScale) * progress, bar.height - 4f * uiScale);
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

        return string.Empty;
    }

    private static int DisplaySeconds(float seconds)
    {
        return Mathf.CeilToInt(seconds);
    }

    private static string TeamName(TeamType team)
    {
        return team == TeamType.Blue ? "DEFENDERS" : "STRIKERS";
    }

    private void EnsureStyles(float uiScale)
    {
        if (panelStyle != null && Mathf.Approximately(cachedStyleScale, uiScale))
        {
            return;
        }

        cachedStyleScale = uiScale;

        panelTexture = new Texture2D(1, 1);
        panelTexture.name = "ObjectiveTimerPanelBackground";
        panelTexture.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.05f, 0.92f));
        panelTexture.Apply();

        panelStyle = new GUIStyle(GUI.skin.box);
        backgroundStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = panelTexture },
            border = new RectOffset(0, 0, 0, 0)
        };

        textStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = ScaleFont(fontSize, uiScale),
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            normal = { textColor = Color.white }
        };

        defuseTitleStyle = new GUIStyle(textStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = ScaleFont(26, uiScale),
            normal = { textColor = new Color(1f, 0.32f, 0.22f) }
        };
        defuseDetailStyle = new GUIStyle(textStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = ScaleFont(18, uiScale)
        };
        roundTimerStyle = new GUIStyle(textStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = ScaleFont(22, uiScale),
            normal = { textColor = new Color(0.2f, 0.86f, 0.96f, 1f) }
        };
        progressBackgroundTexture = CreateTexture("DefuseProgressBackground",
            new Color(0.12f, 0.13f, 0.15f, 1f));
        defuseFillTexture = CreateTexture("DefuseProgressFill",
            new Color(0.12f, 0.78f, 0.92f, 1f));
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

    private static float GetUiScale()
    {
        float widthScale = Screen.width / HudCanvasScaleUtility.ReferenceResolution.x;
        float heightScale = Screen.height / HudCanvasScaleUtility.ReferenceResolution.y;
        return Mathf.Clamp(Mathf.Min(widthScale, heightScale), 0.85f, 1.35f);
    }

    private static int ScaleFont(int baseSize, float uiScale)
    {
        return Mathf.RoundToInt(baseSize * uiScale);
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
