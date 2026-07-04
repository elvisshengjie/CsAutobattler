using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoritative player tactic state. UI writes here; the executor and agents read here.
/// </summary>
[DisallowMultipleComponent]
public sealed class TeamTacticManager : MonoBehaviour
{
    public static TeamTacticManager Instance { get; private set; }

    [Header("Controlled Team")]
    [SerializeField] private TeamType controlledTeam = TeamType.Red;

    [Header("Runtime (Read Only)")]
    [SerializeField] private bool hasSelectedInitialTactic;
    [SerializeField] private InitialTeamTactic selectedInitialTactic;
    [SerializeField] private bool rolesConfirmed;
    [SerializeField] private List<MidRoundTactic> activeTactics = new List<MidRoundTactic>();

    private readonly HashSet<MidRoundTactic> activeTacticSet =
        new HashSet<MidRoundTactic>();
    private RoundManager roundManager;

    public bool HasSelectedInitialTactic => hasSelectedInitialTactic;
    public bool RolesConfirmed => rolesConfirmed;
    public TeamType ControlledTeam => controlledTeam;
    public int PlanRevision { get; private set; }
    public bool IsInitialSelectionBlockingInput =>
        roundManager != null && roundManager.CurrentState == RoundState.Preparation &&
        roundManager.attackingTeam == controlledTeam &&
        (!hasSelectedInitialTactic || !rolesConfirmed);

    public event Action<InitialTeamTactic> InitialTacticSelected;
    public event Action TacticsChanged;
    public event Action RoundTacticsReset;
    public event Action RolesChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstanceForBombRound()
    {
        if (FindAnyObjectByType<RoundManager>() == null ||
            FindAnyObjectByType<TeamTacticManager>() != null)
        {
            return;
        }

        new GameObject("Team Tactic System").AddComponent<TeamTacticManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        activeTacticSet.Clear();
        foreach (MidRoundTactic tactic in activeTactics)
        {
            activeTacticSet.Add(tactic);
        }

        if (GetComponent<TeamTacticExecutor>() == null)
        {
            gameObject.AddComponent<TeamTacticExecutor>();
        }

        if (GetComponent<TeamTacticUI>() == null)
        {
            gameObject.AddComponent<TeamTacticUI>();
        }
    }

    private void Start()
    {
        roundManager = RoundManager.Instance != null
            ? RoundManager.Instance
            : FindAnyObjectByType<RoundManager>();
        if (roundManager == null)
        {
            enabled = false;
            return;
        }

        roundManager.StateChanged += OnRoundStateChanged;
        if (roundManager.CurrentState == RoundState.Preparation)
        {
            ResetForNewRound();
        }
    }

    private void OnDestroy()
    {
        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SelectInitialTactic(InitialTeamTactic tactic)
    {
        if (hasSelectedInitialTactic ||
            (roundManager != null && roundManager.CurrentState == RoundState.RoundEnd))
        {
            return;
        }

        selectedInitialTactic = tactic;
        hasSelectedInitialTactic = true;
        AssignBalancedRoles();
        PlanRevision++;
        InitialTacticSelected?.Invoke(tactic);
        TacticsChanged?.Invoke();
        Debug.Log("Initial team tactic selected: " + TeamTacticDefinitions.GetName(tactic));
    }

    public void ToggleMidRoundTactic(MidRoundTactic tactic)
    {
        if (!hasSelectedInitialTactic ||
            (roundManager != null && roundManager.CurrentState == RoundState.RoundEnd))
        {
            return;
        }

        if (!activeTacticSet.Add(tactic))
        {
            activeTacticSet.Remove(tactic);
        }

        SyncSerializedTactics();
        PlanRevision++;
        TacticsChanged?.Invoke();
        Debug.Log(
            $"Mid-round tactic {TeamTacticDefinitions.GetName(tactic)}: " +
            (activeTacticSet.Contains(tactic) ? "active" : "inactive"));
    }

    public bool IsMidRoundTacticActive(MidRoundTactic tactic)
    {
        return activeTacticSet.Contains(tactic);
    }

    public InitialTeamTactic GetSelectedInitialTactic()
    {
        return selectedInitialTactic;
    }

    public IReadOnlyCollection<MidRoundTactic> GetActiveMidRoundTactics()
    {
        return activeTacticSet;
    }

    public bool RequiresInitialSelection(RoundManager candidateRoundManager)
    {
        return candidateRoundManager != null &&
               candidateRoundManager.CurrentState == RoundState.Preparation &&
               candidateRoundManager.attackingTeam == controlledTeam &&
               (!hasSelectedInitialTactic || !rolesConfirmed);
    }

    public bool ControlsAttackingTeam(RoundManager candidateRoundManager)
    {
        return candidateRoundManager != null &&
               candidateRoundManager.attackingTeam == controlledTeam;
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Preparation)
        {
            ResetForNewRound();
        }
    }

    private void ResetForNewRound()
    {
        hasSelectedInitialTactic = false;
        rolesConfirmed = false;
        activeTacticSet.Clear();
        activeTactics.Clear();
        PlanRevision++;
        RoundTacticsReset?.Invoke();
        TacticsChanged?.Invoke();
        RolesChanged?.Invoke();
    }

    public List<AgentRole> GetControlledRoles()
    {
        List<AgentRole> result = new List<AgentRole>();
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Include))
        {
            if (agent.team != controlledTeam) continue;
            AgentRole role = agent.GetComponent<AgentRole>();
            if (role == null) role = agent.gameObject.AddComponent<AgentRole>();
            result.Add(role);
        }
        result.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        return result;
    }

    public void AssignBalancedRoles()
    {
        List<AgentRole> agents = GetControlledRoles();
        AgentRoleType[] four = { AgentRoleType.Support, AgentRoleType.Flanker,
            AgentRoleType.Assaulter, AgentRoleType.Defender };
        AgentRoleType[] five = { AgentRoleType.Support, AgentRoleType.Flanker,
            AgentRoleType.Assaulter, AgentRoleType.Assaulter, AgentRoleType.Defender };
        for (int i = 0; i < agents.Count; i++)
        {
            AgentRoleType role = agents.Count == 5 ? five[i] :
                agents.Count == 4 ? four[i] : four[i % four.Length];
            agents[i].SetRole(role);
        }
        rolesConfirmed = false;
        RolesChanged?.Invoke();
    }

    public void SetAgentRole(AgentRole agent, AgentRoleType role)
    {
        if (agent == null || !GetControlledRoles().Contains(agent)) return;
        agent.SetRole(role);
        rolesConfirmed = false;
        RolesChanged?.Invoke();
    }

    public void ConfirmRoles()
    {
        if (!hasSelectedInitialTactic || GetControlledRoles().Count == 0) return;
        rolesConfirmed = true;
        PlanRevision++;
        RolesChanged?.Invoke();
        TacticsChanged?.Invoke();
    }

    private void SyncSerializedTactics()
    {
        activeTactics.Clear();
        activeTactics.AddRange(activeTacticSet);
        activeTactics.Sort();
    }
}
