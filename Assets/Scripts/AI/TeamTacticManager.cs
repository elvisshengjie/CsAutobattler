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
    [SerializeField] private bool loadoutsConfirmed;
    [SerializeField] private List<MidRoundTactic> activeTactics = new List<MidRoundTactic>();
    [SerializeField] private PlantSitePreference selectedPlantSitePreference;
    [SerializeField] private PlantSitePreference queuedPlantSitePreference;
    [SerializeField] private string plantDecisionSummary;

    private readonly HashSet<MidRoundTactic> activeTacticSet =
        new HashSet<MidRoundTactic>();
    private RoundManager roundManager;

    public bool HasSelectedInitialTactic => hasSelectedInitialTactic;
    public bool RolesConfirmed => rolesConfirmed;
    public bool LoadoutsConfirmed => loadoutsConfirmed;
    public TeamType ControlledTeam => controlledTeam;
    public int PlanRevision { get; private set; }
    public int QueuedMidRoundTacticCount => activeTactics.Count;
    public PlantSitePreference SelectedPlantSitePreference => selectedPlantSitePreference;
    public PlantSitePreference QueuedPlantSitePreference => queuedPlantSitePreference;
    public string PlantDecisionSummary => plantDecisionSummary;
    public bool IsInitialSelectionBlockingInput =>
        roundManager != null && roundManager.CurrentState == RoundState.Preparation &&
        roundManager.attackingTeam == controlledTeam &&
        (!hasSelectedInitialTactic || !rolesConfirmed || !loadoutsConfirmed);

    public event Action<InitialTeamTactic> InitialTacticSelected;
    public event Action TacticsChanged;
    public event Action RoundTacticsReset;
    public event Action RolesChanged;
    public event Action LoadoutsChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstanceForBombRound()
    {
        if (FindAnyObjectByType<RoundManager>() == null ||
            FindAnyObjectByType<TeamTacticManager>() != null)
        {
            return;
        }

        // Campaign preparation must own the scene before the legacy role/loadout
        // flow is allowed to create itself.
        if (CampaignManager.EnsureForCurrentScene())
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
        activeTactics.RemoveAll(tactic => !activeTacticSet.Add(tactic));

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
        if (CampaignManager.Instance != null &&
            CampaignManager.Instance.UsesLockedCharacters)
        {
            // Campaign characters own their role and weapon. Every new scene still
            // asks for an initial tactic, but skips the legacy reassignment pages.
            rolesConfirmed = true;
            loadoutsConfirmed = true;
        }
        else
        {
            AssignBalancedRoles();
        }
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

        if (activeTacticSet.Remove(tactic))
        {
            activeTactics.Remove(tactic);
            if (tactic == MidRoundTactic.Plant)
            {
                queuedPlantSitePreference = PlantSitePreference.Auto;
                plantDecisionSummary = string.Empty;
            }
        }
        else
        {
            activeTacticSet.Add(tactic);
            activeTactics.Add(tactic);
            if (tactic == MidRoundTactic.Plant)
            {
                queuedPlantSitePreference = selectedPlantSitePreference;
                plantDecisionSummary = string.Empty;
            }
        }

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
        return activeTactics;
    }

    public void SetPlantSitePreference(PlantSitePreference preference)
    {
        if (selectedPlantSitePreference == preference &&
            (!activeTacticSet.Contains(MidRoundTactic.Plant) ||
             queuedPlantSitePreference == preference))
        {
            return;
        }

        selectedPlantSitePreference = preference;
        if (activeTacticSet.Contains(MidRoundTactic.Plant))
            queuedPlantSitePreference = preference;
        plantDecisionSummary = string.Empty;
        PlanRevision++;
        TacticsChanged?.Invoke();
    }

    public void SetPlantDecisionSummary(string summary)
    {
        summary ??= string.Empty;
        if (plantDecisionSummary == summary) return;
        plantDecisionSummary = summary;
        TacticsChanged?.Invoke();
    }

    public bool TryGetCurrentMidRoundTactic(out MidRoundTactic tactic)
    {
        if (activeTactics.Count > 0)
        {
            tactic = activeTactics[0];
            return true;
        }

        tactic = default;
        return false;
    }

    public int GetMidRoundTacticOrder(MidRoundTactic tactic)
    {
        return activeTactics.IndexOf(tactic);
    }

    public void CompleteCurrentMidRoundTactic(MidRoundTactic tactic)
    {
        if (activeTactics.Count == 0 || activeTactics[0] != tactic)
        {
            return;
        }

        activeTactics.RemoveAt(0);
        activeTacticSet.Remove(tactic);
        if (tactic == MidRoundTactic.Plant)
            queuedPlantSitePreference = PlantSitePreference.Auto;
        PlanRevision++;
        TacticsChanged?.Invoke();
        Debug.Log("Mid-round tactic completed: " +
                  TeamTacticDefinitions.GetName(tactic));
    }

    public bool RequiresInitialSelection(RoundManager candidateRoundManager)
    {
        return candidateRoundManager != null &&
               candidateRoundManager.CurrentState == RoundState.Preparation &&
               candidateRoundManager.attackingTeam == controlledTeam &&
               (!hasSelectedInitialTactic || !rolesConfirmed || !loadoutsConfirmed);
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
        loadoutsConfirmed = false;
        activeTacticSet.Clear();
        activeTactics.Clear();
        selectedPlantSitePreference = PlantSitePreference.Auto;
        queuedPlantSitePreference = PlantSitePreference.Auto;
        plantDecisionSummary = string.Empty;
        PlanRevision++;
        RoundTacticsReset?.Invoke();
        TacticsChanged?.Invoke();
        RolesChanged?.Invoke();
        LoadoutsChanged?.Invoke();
    }

    public List<AgentRole> GetControlledRoles()
    {
        List<AgentRole> result = new List<AgentRole>();
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
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
        loadoutsConfirmed = false;
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
        AssignRecommendedLoadouts();
        PlanRevision++;
        RolesChanged?.Invoke();
        TacticsChanged?.Invoke();
    }

    public List<WeaponLoadout> GetControlledLoadouts()
    {
        List<WeaponLoadout> result = new List<WeaponLoadout>();
        foreach (AgentRole role in GetControlledRoles())
            result.Add(WeaponLoadout.Get(role.gameObject));
        return result;
    }

    public void AssignRecommendedLoadouts()
    {
        foreach (AgentRole role in GetControlledRoles())
        {
            WeaponType type = role.SelectedRole switch
            {
                AgentRoleType.Support => WeaponType.Rifle,
                AgentRoleType.Flanker => WeaponType.SMG,
                AgentRoleType.Assaulter => WeaponType.Shotgun,
                AgentRoleType.Defender => WeaponType.Sniper,
                _ => WeaponType.Rifle
            };
            WeaponLoadout.Get(role.gameObject).SelectWeapon(type);
        }
        loadoutsConfirmed = false;
        LoadoutsChanged?.Invoke();
    }

    public void SetAgentWeapon(WeaponLoadout loadout, WeaponType type)
    {
        if (loadout == null || !GetControlledLoadouts().Contains(loadout)) return;
        loadout.SelectWeapon(type);
        loadoutsConfirmed = false;
        LoadoutsChanged?.Invoke();
    }

    public void ConfirmLoadouts()
    {
        if (!rolesConfirmed || GetControlledLoadouts().Count == 0) return;
        loadoutsConfirmed = true;
        PlanRevision++;
        LoadoutsChanged?.Invoke();
        TacticsChanged?.Invoke();
    }

}
