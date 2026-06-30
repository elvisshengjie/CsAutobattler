using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public sealed class RedTeamStatsSlotUI : MonoBehaviour
{
    private static readonly Color NeutralPortraitColor = new Color(0.65f, 0.65f, 0.65f, 1f);

    [Header("Wired by RedTeamStatsPanelBuilder")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Text hpText;
    [SerializeField] private Text tacticText;
    [SerializeField] private Image backgroundImage;

    private AgentStats agent;
    private HealthSystem healthSystem;
    private CanvasGroup canvasGroup;
    private bool hadAssignedAgent;

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
        ApplyPortrait();
        Refresh();
    }

    public void SetAgent(AgentStats assignedAgent)
    {
        agent = assignedAgent;
        hadAssignedAgent = assignedAgent != null;
        healthSystem = assignedAgent != null
            ? assignedAgent.GetComponent<HealthSystem>()
            : null;

        ApplyPortrait();
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
    }

    private void SetDimmed(bool dimmed)
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (canvasGroup == null)
        {
            return;
        }

        float targetAlpha = dimmed ? 0.4f : 1f;
        if (!Mathf.Approximately(canvasGroup.alpha, targetAlpha))
        {
            canvasGroup.alpha = targetAlpha;
        }
    }

    private void ApplyPortrait()
    {
        if (portraitImage == null)
        {
            return;
        }

        Sprite portraitSprite = null;
        Color portraitColor = NeutralPortraitColor;

        if (agent != null)
        {
            AgentPortraitData portraitData = agent.GetComponent<AgentPortraitData>();
            if (portraitData != null)
            {
                portraitSprite = portraitData.GetPortraitSprite();
                portraitColor = portraitSprite != null
                    ? Color.white
                    : portraitData.GetPortraitColor();
            }
            else
            {
                Renderer renderer = agent.GetComponent<Renderer>();
                if (renderer == null)
                {
                    renderer = agent.GetComponentInChildren<Renderer>(true);
                }

                if (AgentPortraitData.TryGetRendererColor(renderer, out Color rendererColor))
                {
                    portraitColor = rendererColor;
                }
            }
        }

        portraitImage.sprite = portraitSprite;
        portraitImage.color = portraitColor;
        portraitImage.preserveAspect = portraitSprite != null;
    }

    private static string FormatHealth(float value)
    {
        return value.ToString("0.#");
    }
}
