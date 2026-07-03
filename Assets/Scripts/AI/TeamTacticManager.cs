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
    [SerializeField] private List<MidRoundTactic> activeTactics = new List<MidRoundTactic>();

    private readonly HashSet<MidRoundTactic> activeTacticSet =
        new HashSet<MidRoundTactic>();
    private RoundManager roundManager;

    public bool HasSelectedInitialTactic => hasSelectedInitialTactic;
    public TeamType ControlledTeam => controlledTeam;
    public int PlanRevision { get; private set; }
    public bool IsInitialSelectionBlockingInput =>
        roundManager != null && roundManager.CurrentState == RoundState.Preparation &&
        roundManager.attackingTeam == controlledTeam && !hasSelectedInitialTactic;

    public event Action<InitialTeamTactic> InitialTacticSelected;
    public event Action TacticsChanged;
    public event Action RoundTacticsReset;

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
               !hasSelectedInitialTactic;
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
        activeTacticSet.Clear();
        activeTactics.Clear();
        PlanRevision++;
        RoundTacticsReset?.Invoke();
        TacticsChanged?.Invoke();
    }

    private void SyncSerializedTactics()
    {
        activeTactics.Clear();
        activeTactics.AddRange(activeTacticSet);
        activeTactics.Sort();
    }
}
