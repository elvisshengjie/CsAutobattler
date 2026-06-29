using UnityEngine;
using UnityEngine.UI;

public sealed class RedTeamStatsSlotUI : MonoBehaviour
{
    [Header("Wired by RedTeamStatsPanelBuilder")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Text hpText;
    [SerializeField] private Text tacticText;
    [SerializeField] private Image backgroundImage;

    private AgentStats agent;
    private HealthSystem healthSystem;
    private CanvasGroup canvasGroup;
    private bool hadAssignedAgent;
    private Color normalBackgroundColor;
    private bool hasBackgroundColor;

    private void Awake()
    {
        CacheVisualState();
    }

    public void Configure(Image portrait, Text hp, Text tactic, Image background = null)
    {
        portraitImage = portrait;
        hpText = hp;
        tacticText = tactic;
        backgroundImage = background;
        CacheVisualState();
        Refresh();
    }

    public void SetAgent(AgentStats assignedAgent)
    {
        agent = assignedAgent;
        hadAssignedAgent = assignedAgent != null;
        healthSystem = assignedAgent != null
            ? assignedAgent.GetComponent<HealthSystem>()
            : null;

        Refresh();
    }

    public void Refresh()
    {
        if (tacticText != null)
        {
            tacticText.text = "Tactic:";
        }

        if (agent == null)
        {
            if (hpText != null)
            {
                hpText.text = "HP: --";
            }

            // If a previously assigned agent has been destroyed after dying, keep the slot dimmed.
            SetDimmed(hadAssignedAgent);
            return;
        }

        if (healthSystem == null)
        {
            healthSystem = agent.GetComponent<HealthSystem>();
        }

        if (hpText != null)
        {
            if (healthSystem == null)
            {
                hpText.text = "HP: --";
            }
            else
            {
                float currentHealth = Mathf.Max(0f, healthSystem.CurrentHealth);
                float maxHealth = Mathf.Max(0f, agent.maxHealth);
                hpText.text = "HP: " + FormatHealth(currentHealth) + "/" + FormatHealth(maxHealth);
            }
        }

        SetDimmed(healthSystem != null && healthSystem.IsDead);
    }

    private void CacheVisualState()
    {
        canvasGroup = GetComponent<CanvasGroup>();

        if (backgroundImage != null)
        {
            normalBackgroundColor = backgroundImage.color;
            hasBackgroundColor = true;
        }
    }

    private void SetDimmed(bool dimmed)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = dimmed ? 0.4f : 1f;
            return;
        }

        if (backgroundImage != null && hasBackgroundColor)
        {
            Color color = normalBackgroundColor;
            color.a *= dimmed ? 0.4f : 1f;
            backgroundImage.color = color;
        }

        if (portraitImage != null)
        {
            Color color = portraitImage.color;
            color.a = dimmed ? 0.4f : 1f;
            portraitImage.color = color;
        }
    }

    private static string FormatHealth(float value)
    {
        return value.ToString("0.#");
    }
}
