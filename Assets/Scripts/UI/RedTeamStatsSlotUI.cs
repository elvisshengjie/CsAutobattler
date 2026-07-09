using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public sealed class RedTeamStatsSlotUI : MonoBehaviour
{
    public const float DisplayWidth = 225f;
    public const float DisplayHeight = 116f;
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
        ApplyExpandedLayout();
    }

    public void Configure(Image portrait, Text hp, Text tactic, Image background = null)
    {
        portraitImage = portrait;
        hpText = hp;
        tacticText = tactic;
        backgroundImage = background;
        CacheVisualState();
        ApplyExpandedLayout();
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
            tacticText.text = "Role: --\nWeapon: --";
            tacticText.fontSize = 14;
            tacticText.color = Color.white;
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

        if (tacticText != null)
        {
            AgentRole role = agent.GetComponent<AgentRole>();
            WeaponLoadout loadout = agent.GetComponent<WeaponLoadout>();
            bool hasBomb = agent.GetComponent<BombCarrier>()?.HasBomb == true;
            string roleName = role != null ? role.SelectedRole.ToString() : "Unassigned";
            string weaponName = loadout != null ? loadout.SelectedWeapon.ToString() : "Rifle";
            tacticText.text = $"Role: {roleName}\nWeapon: {weaponName}" +
                              (hasBomb ? "\nPLANTER" : string.Empty);
            tacticText.fontSize = hasBomb ? 13 : 14;
            tacticText.color = hasBomb
                ? new Color(1f, 0.82f, 0.35f, 1f)
                : Color.white;
        }

        if (hpText != null)
        {
            if (healthSystem == null)
            {
                hpText.text = "HP: --";
            }
            else
            {
                float maxHealth = GetDisplayMaximumHealth(agent);
                float currentHealth = Application.isPlaying
                    ? Mathf.Max(0f, healthSystem.CurrentHealth)
                    : maxHealth;
                hpText.text = "HP: " + FormatHealth(currentHealth) + "/" + FormatHealth(maxHealth);
            }
        }

        SetDimmed(healthSystem != null && healthSystem.IsDead);
    }

    private void CacheVisualState()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void ApplyExpandedLayout()
    {
        RectTransform slotRect = transform as RectTransform;
        if (slotRect != null)
        {
            slotRect.sizeDelta = new Vector2(DisplayWidth, DisplayHeight);
        }

        LayoutElement layout = GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.minWidth = layout.preferredWidth = DisplayWidth;
            layout.minHeight = layout.preferredHeight = DisplayHeight;
        }

        if (portraitImage != null)
        {
            RectTransform portraitRect = portraitImage.rectTransform;
            portraitRect.sizeDelta = new Vector2(70f, 70f);
            portraitRect.anchoredPosition = new Vector2(8f, 0f);
        }

        if (hpText != null)
        {
            hpText.rectTransform.offsetMin = new Vector2(88f, 0f);
            hpText.rectTransform.offsetMax = new Vector2(-8f, -5f);
            hpText.fontSize = 14;
            hpText.resizeTextForBestFit = true;
            hpText.resizeTextMinSize = 11;
            hpText.resizeTextMaxSize = 14;
        }

        if (tacticText != null)
        {
            tacticText.rectTransform.offsetMin = new Vector2(88f, 5f);
            tacticText.rectTransform.offsetMax = new Vector2(-8f, 0f);
            tacticText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tacticText.verticalOverflow = VerticalWrapMode.Overflow;
            tacticText.resizeTextForBestFit = true;
            tacticText.resizeTextMinSize = 11;
            tacticText.resizeTextMaxSize = 14;
        }
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

    private static float GetDisplayMaximumHealth(AgentStats stats)
    {
        if (stats == null)
        {
            return 0f;
        }

        WeaponLoadout loadout = stats.GetComponent<WeaponLoadout>();
        return Mathf.Max(0f, loadout != null ? loadout.AgentHealth : stats.maxHealth);
    }
}
