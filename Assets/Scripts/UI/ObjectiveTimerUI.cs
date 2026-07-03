using UnityEngine;

/// <summary>
/// Compact objective countdown beside the bottom HUD. Created automatically by ObjectiveManager.
/// </summary>
public class ObjectiveTimerUI : MonoBehaviour
{
    [Header("Layout")]
    public float width = 270f;
    public float height = 48f;
    public float rightMargin = 24f;
    [Tooltip("Keeps the timer just above the 75-pixel-tall agent cards.")]
    public float bottomMargin = 110f;
    public int fontSize = 17;

    private GUIStyle panelStyle;
    private GUIStyle backgroundStyle;
    private GUIStyle textStyle;
    private Texture2D panelTexture;

    private void OnGUI()
    {
        RoundManager round = RoundManager.Instance;
        ObjectiveManager objective = ObjectiveManager.Instance;
        if (round == null || objective == null)
        {
            return;
        }

        string message = GetMessage(round, objective);
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        EnsureStyles();
        Rect panel = new Rect(
            Screen.width - width - rightMargin,
            Screen.height - height - bottomMargin,
            width,
            height);
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Box(new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f, panel.height - 6f),
            GUIContent.none,
            backgroundStyle);
        GUI.Label(panel, message, textStyle);
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
            return $"{TeamName(round.attackingTeam)} BOMB EXPLODES  |  " +
                   $"{DisplaySeconds(round.BombTimeRemaining)}s";
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

    private void EnsureStyles()
    {
        if (panelStyle != null)
        {
            return;
        }

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
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            normal = { textColor = Color.white }
        };
    }

    private void OnDestroy()
    {
        if (panelTexture != null)
        {
            Destroy(panelTexture);
        }
    }
}
