using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class RedTeamStatsPanelUI : MonoBehaviour
{
    private const int RequiredSlotCount = 5;
    [SerializeField] private RedTeamStatsSlotUI[] slots = new RedTeamStatsSlotUI[RequiredSlotCount];
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    private float nextRefreshTime;

    private void OnEnable()
    {
        InitializePanel();
    }

    private void ApplyExpandedLayout()
    {
        const float spacing = 12f;
        RectTransform panelRect = transform as RectTransform;
        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(
                (RequiredSlotCount * RedTeamStatsSlotUI.DisplayWidth) +
                ((RequiredSlotCount - 1) * spacing),
                RedTeamStatsSlotUI.DisplayHeight);
        }

        HorizontalLayoutGroup layout = GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.spacing = spacing;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        CacheSlotsIfNeeded();
        AssignRedTeamAgents();
        RefreshSlots();
        nextRefreshTime = Time.unscaledTime + refreshInterval;
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && isActiveAndEnabled)
        {
            InitializePanel();
        }
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
        CacheSlotsIfNeeded();

        Scene activeScene = SceneManager.GetActiveScene();
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);

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
        {
            CampaignUnitMarker leftMarker = left.GetComponent<CampaignUnitMarker>();
            CampaignUnitMarker rightMarker = right.GetComponent<CampaignUnitMarker>();
            if (leftMarker != null && rightMarker != null)
            {
                return leftMarker.CharacterId.CompareTo(rightMarker.CharacterId);
            }
            return string.Compare(
                left.gameObject.name,
                right.gameObject.name,
                StringComparison.Ordinal);
        });

        for (int slotIndex = 0; slotIndex < RequiredSlotCount; slotIndex++)
        {
            if (slots == null || slotIndex >= slots.Length || slots[slotIndex] == null)
            {
                continue;
            }

            slots[slotIndex].SetAgent(
                slotIndex < redAgents.Count ? redAgents[slotIndex] : null);
        }
    }

    private void InitializePanel()
    {
        ApplyExpandedLayout();
        CacheSlotsIfNeeded();
        AssignRedTeamAgents();
        RefreshSlots();
        nextRefreshTime = Time.unscaledTime + refreshInterval;
    }

    private void CacheSlotsIfNeeded()
    {
        bool missingSlot = slots == null || slots.Length != RequiredSlotCount;
        if (!missingSlot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    missingSlot = true;
                    break;
                }
            }
        }

        if (!missingSlot)
        {
            return;
        }

        RedTeamStatsSlotUI[] foundSlots = GetComponentsInChildren<RedTeamStatsSlotUI>(true);
        Array.Sort(foundSlots, (left, right) =>
            string.Compare(left.name, right.name, StringComparison.Ordinal));

        int count = Mathf.Min(RequiredSlotCount, foundSlots.Length);
        for (int i = 0; i < count; i++)
        {
            slots[i] = foundSlots[i];
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
