using System;
using UnityEngine;

/// <summary>
/// Ends a prototype round when every living agent on one team has been eliminated.
/// It creates itself automatically, so existing battle scenes need no manual setup.
/// </summary>
public class EliminationRoundManager : MonoBehaviour
{
    public static event Action<TeamType> RoundWon;

    public float checkInterval = 0.25f;

    private float nextCheckTime;
    private bool sawBothTeamsAlive;
    private bool roundEnded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<EliminationRoundManager>() == null)
        {
            new GameObject("EliminationRoundManager").AddComponent<EliminationRoundManager>();
        }
    }

    private void Update()
    {
        // Bomb rounds own their own elimination rules, especially after planting.
        if (RoundManager.Instance != null && RoundManager.Instance.enabled)
        {
            return;
        }

        if (roundEnded || Time.time < nextCheckTime)
        {
            return;
        }

        nextCheckTime = Time.time + checkInterval;

        bool blueAlive = false;
        bool redAlive = false;

        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);

        foreach (AgentStats agent in agents)
        {
            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (health == null || health.IsDead)
            {
                continue;
            }

            if (agent.team == TeamType.Blue)
            {
                blueAlive = true;
            }
            else if (agent.team == TeamType.Red)
            {
                redAlive = true;
            }
        }

        if (blueAlive && redAlive)
        {
            sawBothTeamsAlive = true;
            return;
        }

        if (!sawBothTeamsAlive || blueAlive == redAlive)
        {
            return;
        }

        EndRound(blueAlive ? TeamType.Blue : TeamType.Red);
    }

    private void EndRound(TeamType winningTeam)
    {
        roundEnded = true;
        Debug.Log($"Round over. {winningTeam} team wins by elimination.");

        AgentController3D[] controllers =
            FindObjectsByType<AgentController3D>(FindObjectsInactive.Exclude);
        foreach (AgentController3D controller in controllers)
        {
            controller.enabled = false;
        }

        WeaponSystem[] weapons = FindObjectsByType<WeaponSystem>(FindObjectsInactive.Exclude);
        foreach (WeaponSystem weapon in weapons)
        {
            weapon.enabled = false;
        }

        RoundWon?.Invoke(winningTeam);
    }
}
