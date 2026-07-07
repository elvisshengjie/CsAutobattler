using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RedTeamStatsPanelUI : MonoBehaviour
{
    private const int RequiredSlotCount = 8;
    private const string RedAgentNamePrefix = "RedAgent3D_";

    [SerializeField] private RedTeamStatsSlotUI[] slots = new RedTeamStatsSlotUI[RequiredSlotCount];
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    private float nextRefreshTime;
    private float lastCanvasWidth = -1f;
    private CanvasGroup panelCanvasGroup;

    private void Start()
    {
        panelCanvasGroup = GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null) panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        EnsureRequiredSlots();
        ApplyCompactLayout();
        UpdatePanelVisibility();
        AssignRedTeamAgents();
        RefreshSlots();
        nextRefreshTime = Time.unscaledTime + refreshInterval;
    }

    private void EnsureRequiredSlots()
    {
        RedTeamStatsSlotUI[] expandedSlots = new RedTeamStatsSlotUI[RequiredSlotCount];
        int existingCount = slots == null ? 0 : Mathf.Min(slots.Length, RequiredSlotCount);
        if (existingCount > 0)
        {
            Array.Copy(slots, expandedSlots, existingCount);
        }

        RedTeamStatsSlotUI template = null;
        for (int i = existingCount - 1; i >= 0; i--)
        {
            if (expandedSlots[i] != null)
            {
                template = expandedSlots[i];
                break;
            }
        }

        if (template != null)
        {
            for (int i = existingCount; i < RequiredSlotCount; i++)
            {
                GameObject slotObject = Instantiate(template.gameObject, transform);
                slotObject.name = "RedTeamSlot_" + (i + 1);
                expandedSlots[i] = slotObject.GetComponent<RedTeamStatsSlotUI>();
                expandedSlots[i].SetAgent(null);
            }
        }

        slots = expandedSlots;
    }

    private void ApplyCompactLayout()
    {
        const float spacing = 5f;
        RectTransform panelRect = transform as RectTransform;
        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(
                (RequiredSlotCount * RedTeamStatsSlotUI.DisplayWidth) +
                ((RequiredSlotCount - 1) * spacing),
                RedTeamStatsSlotUI.DisplayHeight);
            UpdateResponsivePlacement(panelRect);
        }

        HorizontalLayoutGroup layout = GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.spacing = spacing;
        }
    }

    private void Update()
    {
        UpdatePanelVisibility();
        RectTransform panelRect = transform as RectTransform;
        if (panelRect != null) UpdateResponsivePlacement(panelRect);
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshSlots();
    }

    private void UpdateResponsivePlacement(RectTransform panelRect)
    {
        RectTransform canvasRect = panelRect.parent as RectTransform;
        float canvasWidth = canvasRect != null && canvasRect.rect.width > 0f
            ? canvasRect.rect.width
            : Screen.width;
        if (Mathf.Approximately(canvasWidth, lastCanvasWidth)) return;

        const float miniMapSafeEdge = 230f;
        const float rightPadding = 20f;
        float naturalWidth = (RequiredSlotCount * RedTeamStatsSlotUI.DisplayWidth) +
                             ((RequiredSlotCount - 1) * 5f);
        float availableWidth = Mathf.Max(1f, canvasWidth - miniMapSafeEdge - rightPadding);
        float scale = Mathf.Min(1f, availableWidth / naturalWidth);

        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(
            miniMapSafeEdge + availableWidth * 0.5f,
            14f);
        panelRect.localScale = new Vector3(scale, scale, 1f);
        lastCanvasWidth = canvasWidth;
    }

    private void UpdatePanelVisibility()
    {
        if (panelCanvasGroup == null) return;

        bool hiddenForSelection = TeamTacticManager.Instance != null &&
                                  TeamTacticManager.Instance.IsInitialSelectionBlockingInput;
        panelCanvasGroup.alpha = hiddenForSelection ? 0f : 1f;
        panelCanvasGroup.interactable = false;
        panelCanvasGroup.blocksRaycasts = false;
    }

    public void SetSlots(RedTeamStatsSlotUI[] assignedSlots)
    {
        slots = new RedTeamStatsSlotUI[RequiredSlotCount];

        if (assignedSlots == null)
        {
            return;
        }

        int count = Mathf.Min(RequiredSlotCount, assignedSlots.Length);
        Array.Copy(assignedSlots, slots, count);
    }

    private void AssignRedTeamAgents()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Include);

        List<AgentStats> redAgents = new List<AgentStats>();
        foreach (AgentStats candidate in allAgents)
        {
            if (candidate != null &&
                candidate.gameObject.scene == activeScene &&
                candidate.team == TeamType.Red)
            {
                redAgents.Add(candidate);
            }
        }

        redAgents.Sort((left, right) =>
            string.Compare(left.gameObject.name, right.gameObject.name, StringComparison.Ordinal));

        for (int slotIndex = 0; slotIndex < RequiredSlotCount; slotIndex++)
        {
            if (slots == null || slotIndex >= slots.Length || slots[slotIndex] == null)
            {
                continue;
            }

            string expectedAgentName = RedAgentNamePrefix + (slotIndex + 1);
            AgentStats matchingAgent = null;

            foreach (AgentStats candidate in redAgents)
            {
                if (string.Equals(candidate.gameObject.name, expectedAgentName, StringComparison.Ordinal))
                {
                    matchingAgent = candidate;
                    break;
                }
            }

            slots[slotIndex].SetAgent(matchingAgent);
        }
    }

    private void RefreshSlots()
    {
        if (slots == null)
        {
            return;
        }

        foreach (RedTeamStatsSlotUI slot in slots)
        {
            if (slot != null)
            {
                slot.Refresh();
            }
        }
    }
}
