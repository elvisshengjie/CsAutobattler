using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class RedTeamStatsPanelUI : MonoBehaviour
{
    private const int RequiredSlotCount = 5;

    [SerializeField] private RedTeamStatsSlotUI[] slots = new RedTeamStatsSlotUI[RequiredSlotCount];
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    private float nextRefreshTime;

    private void Start()
    {
        AssignRedTeamAgents();
        RefreshSlots();
        nextRefreshTime = Time.unscaledTime + refreshInterval;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshSlots();
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
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

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

        for (int i = 0; i < RequiredSlotCount; i++)
        {
            if (slots == null || i >= slots.Length || slots[i] == null)
            {
                continue;
            }

            slots[i].SetAgent(i < redAgents.Count ? redAgents[i] : null);
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
