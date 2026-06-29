using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates bomb ownership, sites, planting, recovery, and defusing.
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Header("References")]
    public RoundManager roundManager;
    public BombController bombPrefab;
    public BombSite siteA;
    public BombSite siteB;

    [Header("Plant Rules")]
    public float allowedMovementWhilePlanting = 0.15f;
    [Tooltip("Maximum flat distance from the bomb for defusing. One map tile is 1 unit.")]
    public float defuseInteractionRange = 1.25f;

    [Header("Squad Tactics")]
    [Tooltip("Spacing between squad members assigned around a site or planted bomb.")]
    public float tacticalSpacing = 2.5f;
    [Tooltip("Adds a new formation rotation each round so agents do not reuse identical positions.")]
    public bool randomizeTacticalPositions = true;

    [Header("Runtime (Read Only)")]
    [SerializeField] private BombController activeBomb;
    [SerializeField] private BombCarrier activePlantCarrier;
    [SerializeField] private BombSite activePlantSite;
    [SerializeField] private float plantProgress;
    [SerializeField] private GameObject activeDefuser;
    [SerializeField] private float defuseProgress;
    [SerializeField] private BombSite selectedAttackSite;

    private Vector3 plantStartPosition;
    private Vector3 defuseStartPosition;
    private AgentBrain suspendedPlantBrain;
    private AgentMotor suspendedPlantMotor;
    private AgentBrain suspendedDefuseBrain;
    private AgentMotor suspendedDefuseMotor;
    private GameObject bombRecoveryAgent;
    private AgentBrain bombRecoveryBrain;
    private AgentMotor bombRecoveryMotor;
    private GameObject defuseApproachAgent;
    private AgentBrain defuseApproachBrain;
    private AgentMotor defuseApproachMotor;
    private float tacticalRotationDegrees;

    public BombController ActiveBomb => activeBomb;
    public float PlantProgress => plantProgress;
    public float DefuseProgress => defuseProgress;
    public bool IsPlanting => activePlantCarrier != null;
    public bool IsDefusing => activeDefuser != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        ResolveReferences();
        EnsureObjectiveTimerUI();
        SelectRoundTactics();
        if (roundManager != null)
        {
            roundManager.StateChanged += OnRoundStateChanged;
        }
        CreateBombIfNeeded();
        AssignStartingCarrier();
    }

    private void OnDestroy()
    {
        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }
    }

    /// <summary>
    /// Supplies centralized squad intent while each AgentBrain remains responsible
    /// for immediate combat decisions.
    /// </summary>
    public bool TryExecuteTacticalObjective(GameObject agent, AgentMotor motor)
    {
        if (agent == null || motor == null || roundManager == null || activeBomb == null ||
            roundManager.CurrentState == RoundState.Preparation ||
            roundManager.CurrentState == RoundState.RoundEnd)
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        HealthSystem health = agent.GetComponent<HealthSystem>();
        if (stats == null || health == null || health.IsDead)
        {
            return false;
        }

        if (roundManager.CurrentState == RoundState.BombPlanted)
        {
            float radius = stats.team == roundManager.attackingTeam
                ? tacticalSpacing
                : tacticalSpacing * 0.65f;
            motor.MoveTo(GetClaimedPosition(
                agent,
                stats.team,
                activeBomb.transform.position,
                radius));
            return true;
        }

        if (stats.team == roundManager.attackingTeam)
        {
            BombCarrier carrier = agent.GetComponent<BombCarrier>();
            if (carrier != null && carrier.HasBomb)
            {
                if (selectedAttackSite != null && selectedAttackSite.Contains(agent) &&
                    roundManager.CurrentState == RoundState.Active)
                {
                    BeginPlant(carrier, selectedAttackSite);
                    return true;
                }

                if (selectedAttackSite != null)
                {
                    motor.MoveTo(selectedAttackSite.PlantPosition);
                    return true;
                }
            }

            Vector3 assaultCenter = activeBomb.CurrentState == BombState.Dropped
                ? activeBomb.transform.position
                : selectedAttackSite != null
                    ? selectedAttackSite.PlantPosition
                    : activeBomb.transform.position;
            motor.MoveTo(GetClaimedPosition(
                agent,
                stats.team,
                assaultCenter,
                tacticalSpacing));
            return true;
        }

        if (stats.team == roundManager.defendingTeam)
        {
            BombSite defendedSite = GetDefendedSite(agent);
            if (defendedSite == null)
            {
                return false;
            }

            motor.MoveTo(GetClaimedPosition(
                agent,
                stats.team,
                defendedSite.PlantPosition,
                tacticalSpacing));
            return true;
        }

        return false;
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Preparation)
        {
            SelectRoundTactics();
        }
    }

    private void SelectRoundTactics()
    {
        if (siteA == null && siteB == null)
        {
            selectedAttackSite = null;
            return;
        }

        selectedAttackSite = siteA == null
            ? siteB
            : siteB == null
                ? siteA
                : Random.value < 0.5f ? siteA : siteB;
        tacticalRotationDegrees = randomizeTacticalPositions
            ? Random.Range(0f, 360f)
            : 0f;
        Debug.Log(
            $"Attacking squad selected site {selectedAttackSite.siteId}; " +
            $"formation rotation {tacticalRotationDegrees:0} degrees.");
    }

    private BombSite GetDefendedSite(GameObject agent)
    {
        int index = GetTeamSlotIndex(agent, roundManager.defendingTeam, out _);
        if (siteA == null)
        {
            return siteB;
        }

        if (siteB == null)
        {
            return siteA;
        }

        // Alternating slots guarantees both sites receive defenders.
        return index % 2 == 0 ? siteA : siteB;
    }

    private Vector3 GetClaimedPosition(
        GameObject agent,
        TeamType team,
        Vector3 center,
        float radius)
    {
        int index = GetTeamSlotIndex(agent, team, out int teamCount);
        float angleStep = 360f / Mathf.Max(1, teamCount);
        float angle = tacticalRotationDegrees + index * angleStep;
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
        return center + offset;
    }

    private static int GetTeamSlotIndex(
        GameObject agent,
        TeamType team,
        out int teamCount)
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        List<AgentStats> teammates = new List<AgentStats>();
        foreach (AgentStats candidate in allAgents)
        {
            HealthSystem health = candidate.GetComponent<HealthSystem>();
            if (candidate.team == team && candidate.GetComponent<AgentController3D>() != null &&
                health != null && !health.IsDead)
            {
                teammates.Add(candidate);
            }
        }

        teammates.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        teamCount = teammates.Count;
        for (int i = 0; i < teammates.Count; i++)
        {
            if (teammates[i].gameObject == agent)
            {
                return i;
            }
        }

        return 0;
    }

    private void Update()
    {
        UpdateBombRecovery();
        UpdateDefenderObjective();
        UpdatePlanting();
        UpdateDefusing();
    }

    public bool CanPlant(GameObject agent, BombSite site)
    {
        if (agent == null || site == null || roundManager == null || activeBomb == null)
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        HealthSystem health = agent.GetComponent<HealthSystem>();
        BombCarrier carrier = agent.GetComponent<BombCarrier>();

        return roundManager.CurrentState == RoundState.Active &&
               stats != null && stats.team == roundManager.attackingTeam &&
               health != null && !health.IsDead &&
               carrier != null && carrier.HasBomb &&
               activeBomb.CurrentCarrier == carrier &&
               site.Contains(agent);
    }

    public void BeginPlant(BombCarrier carrier, BombSite site)
    {
        if (carrier == null || !CanPlant(carrier.gameObject, site) ||
            !roundManager.BeginPlanting())
        {
            return;
        }

        activePlantCarrier = carrier;
        activePlantSite = site;
        plantProgress = 0f;
        plantStartPosition = carrier.transform.position;
        carrier.SetPlanting(true);
        activeBomb.BeginPlanting();
        SuspendAgentForAction(
            carrier.gameObject,
            out suspendedPlantBrain,
            out suspendedPlantMotor);
        SetActionStatus(carrier.gameObject, "PLANTING", roundManager.plantDuration);
        Debug.Log($"{carrier.name} started planting at site {site.siteId}.");
    }

    public void CancelPlant(BombCarrier carrier)
    {
        if (activePlantCarrier == null ||
            (carrier != null && carrier != activePlantCarrier))
        {
            return;
        }

        ClearActionStatus(activePlantCarrier.gameObject);
        activePlantCarrier.SetPlanting(false);
        activeBomb?.CancelPlanting();
        RestoreAgentAfterAction(suspendedPlantBrain, suspendedPlantMotor);
        activePlantCarrier = null;
        activePlantSite = null;
        suspendedPlantBrain = null;
        suspendedPlantMotor = null;
        plantProgress = 0f;
        roundManager?.CancelPlanting();
        Debug.Log("Planting cancelled.");
    }

    public bool CanDefuse(GameObject agent)
    {
        if (agent == null || roundManager == null || activeBomb == null ||
            activeBomb.CurrentState != BombState.Planted ||
            roundManager.CurrentState != RoundState.BombPlanted)
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        HealthSystem health = agent.GetComponent<HealthSystem>();
        return stats != null && stats.team == roundManager.defendingTeam &&
               health != null && !health.IsDead &&
               FlatDistance(agent.transform.position, activeBomb.transform.position) <=
               defuseInteractionRange;
    }

    public void BeginDefuse(GameObject defender)
    {
        if (activeDefuser != null || !CanDefuse(defender))
        {
            return;
        }

        activeDefuser = defender;
        defuseProgress = 0f;
        defuseStartPosition = defender.transform.position;
        SuspendAgentForAction(
            defender,
            out suspendedDefuseBrain,
            out suspendedDefuseMotor);
        SetActionStatus(defender, "DEFUSING", roundManager.defuseDuration);
        Debug.Log(defender.name + " started defusing.");
    }

    public void CancelDefuse(GameObject defender)
    {
        if (activeDefuser == null || (defender != null && defender != activeDefuser))
        {
            return;
        }

        ClearActionStatus(activeDefuser);
        RestoreAgentAfterAction(suspendedDefuseBrain, suspendedDefuseMotor);
        activeDefuser = null;
        suspendedDefuseBrain = null;
        suspendedDefuseMotor = null;
        defuseProgress = 0f;
        Debug.Log("Defusing cancelled.");
    }

    public bool TryPickupBomb(GameObject agent)
    {
        if (agent == null || activeBomb == null ||
            activeBomb.CurrentState != BombState.Dropped || roundManager == null)
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        HealthSystem health = agent.GetComponent<HealthSystem>();
        if (stats == null || stats.team != roundManager.attackingTeam ||
            health == null || health.IsDead)
        {
            return false;
        }

        BombCarrier carrier = agent.GetComponent<BombCarrier>();
        if (carrier == null)
        {
            carrier = agent.AddComponent<BombCarrier>();
        }

        activeBomb.AssignToCarrier(carrier);
        ReleaseBombRecoveryAgent();
        Debug.Log(agent.name + " picked up the bomb.");
        return true;
    }

    public BombSite FindSiteContaining(GameObject agent)
    {
        if (siteA != null && siteA.Contains(agent))
        {
            return siteA;
        }

        if (siteB != null && siteB.Contains(agent))
        {
            return siteB;
        }

        return null;
    }

    public void OnBombTimerExpired()
    {
        activeBomb?.Explode();
    }

    private void UpdatePlanting()
    {
        if (activePlantCarrier == null)
        {
            return;
        }

        bool invalid = roundManager == null ||
                       roundManager.CurrentState != RoundState.Planting ||
                       !activePlantCarrier.IsAlive ||
                       activePlantSite == null ||
                       !activePlantSite.Contains(activePlantCarrier.gameObject) ||
                       Vector3.Distance(
                           plantStartPosition,
                           activePlantCarrier.transform.position) > allowedMovementWhilePlanting;

        if (invalid)
        {
            CancelPlant(activePlantCarrier);
            return;
        }

        plantProgress += Time.deltaTime;
        SetActionStatus(
            activePlantCarrier.gameObject,
            "PLANTING",
            roundManager.plantDuration - plantProgress);
        if (plantProgress < roundManager.plantDuration)
        {
            return;
        }

        BombCarrier completedCarrier = activePlantCarrier;
        BombSite completedSite = activePlantSite;
        ClearActionStatus(completedCarrier.gameObject);
        completedCarrier.SetPlanting(false);
        activeBomb.CompletePlanting(completedSite);
        RestoreAgentAfterAction(suspendedPlantBrain, suspendedPlantMotor);
        activePlantCarrier = null;
        activePlantSite = null;
        suspendedPlantBrain = null;
        suspendedPlantMotor = null;
        plantProgress = 0f;
        roundManager.NotifyBombPlanted();
        Debug.Log("Bomb planted at site " + completedSite.siteId + ".");
    }

    private void UpdateDefusing()
    {
        if (activeDefuser == null)
        {
            return;
        }

        HealthSystem health = activeDefuser.GetComponent<HealthSystem>();
        bool invalid = !CanDefuse(activeDefuser) ||
                       health == null || health.IsDead ||
                       Vector3.Distance(defuseStartPosition, activeDefuser.transform.position) >
                       allowedMovementWhilePlanting;
        if (invalid)
        {
            CancelDefuse(activeDefuser);
            return;
        }

        defuseProgress += Time.deltaTime;
        SetActionStatus(
            activeDefuser,
            "DEFUSING",
            roundManager.defuseDuration - defuseProgress);
        if (defuseProgress < roundManager.defuseDuration)
        {
            return;
        }

        GameObject completedDefuser = activeDefuser;
        ClearActionStatus(completedDefuser);
        RestoreAgentAfterAction(suspendedDefuseBrain, suspendedDefuseMotor);
        activeDefuser = null;
        suspendedDefuseBrain = null;
        suspendedDefuseMotor = null;
        defuseProgress = 0f;
        activeBomb.Defuse();
        Debug.Log(completedDefuser.name + " defused the bomb.");
    }

    private void UpdateBombRecovery()
    {
        if (activeBomb == null || roundManager == null ||
            activeBomb.CurrentState != BombState.Dropped ||
            roundManager.CurrentState == RoundState.RoundEnd)
        {
            ReleaseBombRecoveryAgent();
            return;
        }

        if (!IsLivingTeamAgent(bombRecoveryAgent, roundManager.attackingTeam))
        {
            ReleaseBombRecoveryAgent();
            bombRecoveryAgent = FindNearestLivingAgent(
                roundManager.attackingTeam,
                activeBomb.transform.position);
            if (bombRecoveryAgent == null)
            {
                return;
            }

            TakeMovementControl(
                bombRecoveryAgent,
                out bombRecoveryBrain,
                out bombRecoveryMotor);
        }

        bombRecoveryMotor?.MoveTo(activeBomb.transform.position);
    }

    private void UpdateDefenderObjective()
    {
        bool shouldApproach = activeBomb != null && roundManager != null &&
                              activeBomb.CurrentState == BombState.Planted &&
                              roundManager.CurrentState == RoundState.BombPlanted &&
                              activeDefuser == null;
        if (!shouldApproach)
        {
            ReleaseDefuseApproachAgent();
            return;
        }

        if (!IsLivingTeamAgent(defuseApproachAgent, roundManager.defendingTeam))
        {
            ReleaseDefuseApproachAgent();
            defuseApproachAgent = FindNearestLivingAgent(
                roundManager.defendingTeam,
                activeBomb.transform.position);
            if (defuseApproachAgent == null)
            {
                return;
            }

            TakeMovementControl(
                defuseApproachAgent,
                out defuseApproachBrain,
                out defuseApproachMotor);
        }

        if (FlatDistance(
                defuseApproachAgent.transform.position,
                activeBomb.transform.position) <= defuseInteractionRange)
        {
            GameObject defender = defuseApproachAgent;
            ReleaseDefuseApproachAgent();
            BeginDefuse(defender);
            return;
        }

        defuseApproachMotor?.MoveTo(activeBomb.transform.position);
    }

    private void ResolveReferences()
    {
        if (roundManager == null)
        {
            roundManager = FindAnyObjectByType<RoundManager>();
        }

        BombSite[] sites = FindObjectsByType<BombSite>(FindObjectsInactive.Include);
        foreach (BombSite site in sites)
        {
            if (site.siteId == BombSiteId.A)
            {
                siteA = site;
            }
            else
            {
                siteB = site;
            }
        }
    }

    private void EnsureObjectiveTimerUI()
    {
        if (FindAnyObjectByType<ObjectiveTimerUI>() == null)
        {
            new GameObject("ObjectiveTimerUI").AddComponent<ObjectiveTimerUI>();
        }
    }

    private void CreateBombIfNeeded()
    {
        if (activeBomb != null)
        {
            return;
        }

        if (bombPrefab != null)
        {
            activeBomb = Instantiate(bombPrefab);
            activeBomb.name = "Bomb";
            return;
        }

        GameObject bombObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bombObject.name = "Bomb";
        bombObject.transform.localScale = Vector3.one * 0.3f;
        activeBomb = bombObject.AddComponent<BombController>();
    }

    private void AssignStartingCarrier()
    {
        if (activeBomb == null || roundManager == null)
        {
            return;
        }

        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        List<AgentStats> attackers = new List<AgentStats>();

        foreach (AgentStats agent in agents)
        {
            if (agent.team == roundManager.attackingTeam &&
                agent.GetComponent<AgentController3D>() != null)
            {
                HealthSystem health = agent.GetComponent<HealthSystem>();
                if (health != null && !health.IsDead)
                {
                    attackers.Add(agent);
                }
            }
        }

        if (attackers.Count == 0)
        {
            Debug.LogWarning("No living 3D attackers were found for bomb assignment.");
            return;
        }

        AgentStats selected = attackers[Random.Range(0, attackers.Count)];
        BombCarrier carrier = selected.GetComponent<BombCarrier>();
        if (carrier == null)
        {
            carrier = selected.gameObject.AddComponent<BombCarrier>();
        }

        activeBomb.AssignToCarrier(carrier);
        Debug.Log(selected.name + " starts with the bomb.");
    }

    private GameObject FindNearestLivingAgent(TeamType team, Vector3 position)
    {
        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        GameObject nearest = null;
        float nearestDistanceSquared = Mathf.Infinity;

        foreach (AgentStats agent in agents)
        {
            if (agent.team != team || agent.GetComponent<AgentController3D>() == null)
            {
                continue;
            }

            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (health == null || health.IsDead)
            {
                continue;
            }

            Vector3 difference = agent.transform.position - position;
            difference.y = 0f;
            float distanceSquared = difference.sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = agent.gameObject;
            }
        }

        return nearest;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static bool IsLivingTeamAgent(GameObject agent, TeamType team)
    {
        if (agent == null)
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        HealthSystem health = agent.GetComponent<HealthSystem>();
        return stats != null && stats.team == team &&
               health != null && !health.IsDead;
    }

    private static void TakeMovementControl(
        GameObject agent,
        out AgentBrain brain,
        out AgentMotor motor)
    {
        brain = agent.GetComponent<AgentBrain>();
        motor = agent.GetComponent<AgentMotor>();
        if (brain != null)
        {
            brain.enabled = false;
        }

        if (motor != null)
        {
            motor.enabled = true;
            motor.Stop();
        }
    }

    private void ReleaseBombRecoveryAgent()
    {
        RestoreMovementControl(bombRecoveryAgent, bombRecoveryBrain, bombRecoveryMotor);
        bombRecoveryAgent = null;
        bombRecoveryBrain = null;
        bombRecoveryMotor = null;
    }

    private void ReleaseDefuseApproachAgent()
    {
        RestoreMovementControl(defuseApproachAgent, defuseApproachBrain, defuseApproachMotor);
        defuseApproachAgent = null;
        defuseApproachBrain = null;
        defuseApproachMotor = null;
    }

    private void RestoreMovementControl(
        GameObject agent,
        AgentBrain brain,
        AgentMotor motor)
    {
        if (motor != null)
        {
            motor.Stop();
        }

        bool canResume = agent != null && roundManager != null &&
                         roundManager.CurrentState != RoundState.RoundEnd;
        HealthSystem health = agent != null ? agent.GetComponent<HealthSystem>() : null;
        canResume &= health != null && !health.IsDead;

        if (motor != null)
        {
            motor.enabled = canResume;
        }

        if (brain != null)
        {
            brain.enabled = canResume;
        }
    }

    private static void SetActionStatus(
        GameObject agent,
        string actionName,
        float timeRemaining)
    {
        AgentHealthBar3D healthBar = agent != null
            ? agent.GetComponent<AgentHealthBar3D>()
            : null;
        healthBar?.SetActionStatus(actionName, timeRemaining);
    }

    private static void ClearActionStatus(GameObject agent)
    {
        AgentHealthBar3D healthBar = agent != null
            ? agent.GetComponent<AgentHealthBar3D>()
            : null;
        healthBar?.ClearActionStatus();
    }

    private static void SuspendAgentForAction(
        GameObject agent,
        out AgentBrain brain,
        out AgentMotor motor)
    {
        brain = agent.GetComponent<AgentBrain>();
        motor = agent.GetComponent<AgentMotor>();
        if (brain != null)
        {
            brain.enabled = false;
        }

        if (motor != null)
        {
            motor.Stop();
            motor.enabled = false;
        }
    }

    private void RestoreAgentAfterAction(AgentBrain brain, AgentMotor motor)
    {
        GameObject agent = brain != null
            ? brain.gameObject
            : motor != null
                ? motor.gameObject
                : null;
        HealthSystem health = agent != null ? agent.GetComponent<HealthSystem>() : null;
        bool canResume = agent != null && health != null && !health.IsDead &&
                         roundManager != null &&
                         roundManager.CurrentState != RoundState.RoundEnd;

        if (brain != null)
        {
            brain.enabled = canResume;
        }

        if (motor != null)
        {
            motor.enabled = canResume;
        }
    }
}
