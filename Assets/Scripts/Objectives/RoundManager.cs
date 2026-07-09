using System;
using System.Collections.Generic;
using UnityEngine;

public enum RoundState
{
    Preparation,
    Active,
    Planting,
    BombPlanted,
    Defused,
    Exploded,
    RoundEnd
}

public enum RoundEndReason
{
    None,
    TimeExpired,
    StrikersEliminated,
    BombDefused,
    BombExploded,
    DefendersEliminated
}

/// <summary>
/// Authoritative owner of bomb-round state, timers, teams, and victory.
/// </summary>
public class RoundManager : MonoBehaviour
{
    public static RoundManager Instance { get; private set; }

    [Header("Teams")]
    public TeamType attackingTeam = TeamType.Red;
    public TeamType defendingTeam = TeamType.Blue;
    [Tooltip("When disabled, Red always attacks as the player-controlled terrorist side.")]
    public bool randomizeTeamRoles;
    [Tooltip("Blue and Red exchange their starting sides whenever Blue is selected as attacker.")]
    public bool swapStartingSidesByRole = true;

    [Header("Timers")]
    public float preparationDuration = 5f;
    public float roundDuration = 180f;
    public float plantDuration = 8f;
    public float defuseDuration = 5f;
    public float bombDuration = 60f;

    [Header("Runtime (Read Only)")]
    [SerializeField] private RoundState currentState = RoundState.Preparation;
    [SerializeField] private float preparationTimeRemaining;
    [SerializeField] private float roundTimeRemaining;
    [SerializeField] private float bombTimeRemaining;
    [SerializeField] private bool hasWinner;
    [SerializeField] private TeamType winner;
    [SerializeField] private RoundEndReason winnerReason;

    public RoundState CurrentState => currentState;
    public float PreparationTimeRemaining => preparationTimeRemaining;
    public float RoundTimeRemaining => roundTimeRemaining;
    public float BombTimeRemaining => bombTimeRemaining;
    public bool HasWinner => hasWinner;
    public TeamType Winner => winner;
    public RoundEndReason WinnerReason => winnerReason;
    public bool AreAttackersAlive => IsTeamAlive(attackingTeam);
    public bool AreDefendersAlive => IsTeamAlive(defendingTeam);
    public bool IsRoundInProgress => currentState != RoundState.RoundEnd;

    public event Action<RoundState> StateChanged;
    public event Action<TeamType> RoundEnded;
    public event Action<TeamType, RoundEndReason> RoundResultDeclared;

    private float nextEliminationCheckTime;
    private bool firstRoundStart = true;
    private AgentStats[] redAgents = Array.Empty<AgentStats>();
    private AgentStats[] blueAgents = Array.Empty<AgentStats>();
    private Pose[] redStartingPoses = Array.Empty<Pose>();
    private Pose[] blueStartingPoses = Array.Empty<Pose>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CacheTeamStartingPositions();
        ConfigureTeamRoles();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        // Re-scan after every agent has completed Awake. This avoids script execution
        // order causing an empty team cache in scenes upgraded from the old controller.
        CacheTeamStartingPositions();
        EnsureRoundResultUI();
        StartRound();
    }

    private void Update()
    {
        switch (currentState)
        {
            case RoundState.Preparation:
                if (TeamTacticManager.Instance != null &&
                    TeamTacticManager.Instance.RequiresInitialSelection(this))
                {
                    break;
                }

                preparationTimeRemaining = Mathf.Max(
                    0f,
                    preparationTimeRemaining - Time.deltaTime);
                if (preparationTimeRemaining <= 0f)
                {
                    SetState(RoundState.Active);
                    SetAgentCombatEnabled(true);
                }
                break;

            case RoundState.Active:
            case RoundState.Planting:
                roundTimeRemaining = Mathf.Max(0f, roundTimeRemaining - Time.deltaTime);
                if (roundTimeRemaining <= 0f)
                {
                    EndRound(defendingTeam, RoundEndReason.TimeExpired);
                }
                break;

            case RoundState.BombPlanted:
                bombTimeRemaining = Mathf.Max(0f, bombTimeRemaining - Time.deltaTime);
                if (bombTimeRemaining <= 0f)
                {
                    ObjectiveManager.Instance?.OnBombTimerExpired();
                }
                break;
        }

        if (Time.time >= nextEliminationCheckTime)
        {
            nextEliminationCheckTime = Time.time + 0.25f;
            CheckEliminationVictory();
        }
    }

    public void StartRound()
    {
        if (!firstRoundStart)
        {
            ConfigureTeamRoles();
        }

        firstRoundStart = false;
        ApplyRoleBasedStartingSides();
        hasWinner = false;
        winnerReason = RoundEndReason.None;
        preparationTimeRemaining = preparationDuration;
        roundTimeRemaining = roundDuration;
        bombTimeRemaining = bombDuration;
        SetState(RoundState.Preparation);
        SetAgentCombatEnabled(false);
        RoundResultUI.Instance?.Hide();
    }

    private void RandomizeTeamRoles()
    {
        bool redAttacks = UnityEngine.Random.value < 0.5f;
        attackingTeam = redAttacks ? TeamType.Red : TeamType.Blue;
        defendingTeam = redAttacks ? TeamType.Blue : TeamType.Red;
        Debug.Log($"Round roles: {attackingTeam} attacks, {defendingTeam} defends.");
    }

    private void ConfigureTeamRoles()
    {
        if (randomizeTeamRoles)
        {
            RandomizeTeamRoles();
            return;
        }

        attackingTeam = TeamType.Red;
        defendingTeam = TeamType.Blue;
        Debug.Log("Round roles: Red attacks, Blue defends (player-controlled sides).");
    }

    private void CacheTeamStartingPositions()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Include);
        List<AgentStats> reds = new List<AgentStats>();
        List<AgentStats> blues = new List<AgentStats>();

        foreach (AgentStats agent in allAgents)
        {
            if (agent.GetComponent<AgentController3D>() == null)
            {
                continue;
            }

            if (agent.team == TeamType.Red)
            {
                reds.Add(agent);
            }
            else if (agent.team == TeamType.Blue)
            {
                blues.Add(agent);
            }
        }

        reds.Sort(CompareAgentNames);
        blues.Sort(CompareAgentNames);
        redAgents = reds.ToArray();
        blueAgents = blues.ToArray();
        redStartingPoses = CapturePoses(redAgents);
        blueStartingPoses = CapturePoses(blueAgents);
    }

    private void ApplyRoleBasedStartingSides()
    {
        if (!swapStartingSidesByRole)
        {
            Debug.Log("Starting-side swap is disabled on RoundManager.");
            return;
        }

        ApplyPoses(redAgents, redStartingPoses);
        ApplyPoses(blueAgents, blueStartingPoses);

        bool blueAttacks = attackingTeam == TeamType.Blue;
        if (blueAttacks)
        {
            ApplyPoses(redAgents, blueStartingPoses);
            ApplyPoses(blueAgents, redStartingPoses);
        }

        Debug.Log(
            $"Starting sides applied: {attackingTeam} attacks. " +
            $"Red agents: {redAgents.Length}, Blue agents: {blueAgents.Length}, " +
            $"swapped: {blueAttacks}.");

        if (redAgents.Length != blueAgents.Length)
        {
            Debug.LogWarning(
                "Starting-side swap has unequal team sizes; unmatched agents keep their current position.");
        }
    }

    private static Pose[] CapturePoses(AgentStats[] agents)
    {
        Pose[] poses = new Pose[agents.Length];
        for (int i = 0; i < agents.Length; i++)
        {
            poses[i] = new Pose(agents[i].transform.position, agents[i].transform.rotation);
        }

        return poses;
    }

    private static void ApplyPoses(AgentStats[] agents, Pose[] poses)
    {
        int count = Mathf.Min(agents.Length, poses.Length);
        for (int i = 0; i < count; i++)
        {
            if (agents[i] == null)
            {
                continue;
            }

            AgentMotor motor = agents[i].GetComponent<AgentMotor>();
            motor?.Stop();

            Rigidbody body = agents[i].GetComponent<Rigidbody>();
            if (body != null)
            {
                // Teleport the physics body itself. Setting only Transform can be
                // overwritten by the Rigidbody on the next physics update.
                body.position = poses[i].position;
                body.rotation = poses[i].rotation;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            else
            {
                agents[i].transform.SetPositionAndRotation(
                    poses[i].position,
                    poses[i].rotation);
            }
        }
    }

    private static int CompareAgentNames(AgentStats left, AgentStats right)
    {
        return string.Compare(left.name, right.name, StringComparison.Ordinal);
    }

    public bool BeginPlanting()
    {
        if (currentState != RoundState.Active)
        {
            return false;
        }

        SetState(RoundState.Planting);
        return true;
    }

    public void CancelPlanting()
    {
        if (currentState == RoundState.Planting)
        {
            SetState(RoundState.Active);
        }
    }

    public void NotifyBombPlanted()
    {
        if (currentState != RoundState.Planting)
        {
            return;
        }

        bombTimeRemaining = bombDuration;
        SetState(RoundState.BombPlanted);
    }

    public void NotifyBombDefused()
    {
        if (currentState != RoundState.BombPlanted)
        {
            return;
        }

        SetState(RoundState.Defused);
        Debug.Log("Bomb defused, defenders win");
        EndRound(defendingTeam, RoundEndReason.BombDefused);
    }

    public void NotifyBombExploded()
    {
        if (currentState != RoundState.BombPlanted)
        {
            return;
        }

        SetState(RoundState.Exploded);
        EndRound(attackingTeam, RoundEndReason.BombExploded);
    }

    public void EndRound(TeamType winningTeam, RoundEndReason reason)
    {
        if (hasWinner || currentState == RoundState.RoundEnd)
        {
            return;
        }

        winner = winningTeam;
        winnerReason = reason;
        hasWinner = true;
        SetState(RoundState.RoundEnd);
        SetAgentCombatEnabled(false);
        Debug.Log(
            $"Round ended: {GetWinnerDisplayName(winningTeam)} win - " +
            GetReasonLogName(reason));
        RoundEnded?.Invoke(winningTeam);
        RoundResultDeclared?.Invoke(winningTeam, reason);
    }

    private void CheckEliminationVictory()
    {
        if (currentState == RoundState.Preparation ||
            currentState == RoundState.RoundEnd ||
            currentState == RoundState.Defused ||
            currentState == RoundState.Exploded)
        {
            return;
        }

        bool attackersAlive = IsTeamAlive(attackingTeam);
        bool defendersAlive = IsTeamAlive(defendingTeam);

        if (!defendersAlive)
        {
            EndRound(attackingTeam, RoundEndReason.DefendersEliminated);
            return;
        }

        // After planting, dead attackers do not end the round; defenders must defuse.
        if (!attackersAlive && currentState != RoundState.BombPlanted)
        {
            EndRound(defendingTeam, RoundEndReason.StrikersEliminated);
        }
    }

    public static bool IsTeamAlive(TeamType team)
    {
        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        foreach (AgentStats agent in agents)
        {
            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (agent.team == team && health != null && !health.IsDead)
            {
                return true;
            }
        }

        return false;
    }

    public static string GetWinnerDisplayName(TeamType team)
    {
        return team == TeamType.Blue ? "Defenders" : "Strikers";
    }

    public static string GetReasonDisplayName(RoundEndReason reason)
    {
        return reason switch
        {
            RoundEndReason.TimeExpired => "Time Expired",
            RoundEndReason.StrikersEliminated => "Strikers Eliminated",
            RoundEndReason.BombDefused => "Bomb Defused",
            RoundEndReason.BombExploded => "Bomb Exploded",
            RoundEndReason.DefendersEliminated => "Defenders Eliminated",
            _ => string.Empty
        };
    }

    private static string GetReasonLogName(RoundEndReason reason)
    {
        string displayName = GetReasonDisplayName(reason);
        return string.IsNullOrEmpty(displayName)
            ? displayName
            : char.ToUpperInvariant(displayName[0]) +
              displayName.Substring(1).ToLowerInvariant();
    }

    private static void EnsureRoundResultUI()
    {
        if (FindAnyObjectByType<RoundResultUI>() == null)
        {
            new GameObject("Round Result UI").AddComponent<RoundResultUI>();
        }
    }

    private void SetState(RoundState newState)
    {
        currentState = newState;
        StateChanged?.Invoke(newState);
        Debug.Log("Round state: " + newState);
    }

    private static void SetAgentCombatEnabled(bool enabled)
    {
        AgentBrain[] brains = FindObjectsByType<AgentBrain>(FindObjectsInactive.Exclude);
        foreach (AgentBrain brain in brains)
        {
            brain.enabled = enabled;
        }

        AgentMotor[] motors = FindObjectsByType<AgentMotor>(FindObjectsInactive.Exclude);
        foreach (AgentMotor motor in motors)
        {
            if (!enabled)
            {
                motor.Stop();
            }

            motor.enabled = enabled;
        }

        WeaponSystem[] weapons = FindObjectsByType<WeaponSystem>(FindObjectsInactive.Exclude);
        foreach (WeaponSystem weapon in weapons)
        {
            weapon.enabled = enabled;
        }
    }
}
