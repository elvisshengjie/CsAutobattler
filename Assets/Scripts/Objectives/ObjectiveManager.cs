using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

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
    public float immediatePlantThreatDistance = 3f;
    public float plantThreatRadius = 7f;
    public float plantSupportRadius = 8f;
    public float heavyPlantDamageThreshold = 25f;
    public float minimumPlantHealth = 20f;
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
    [SerializeField] private bool bombPlanted;
    [SerializeField] private BombSite plantedSite;
    [SerializeField] private Vector3 plantedBombPosition;

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
    private BombSite lastLoggedCarrierPlantZone;

    public event Action<BombSite, Vector3> OnBombPlanted;

    public BombController ActiveBomb => activeBomb;
    public float PlantProgress => plantProgress;
    public float DefuseProgress => defuseProgress;
    public bool IsPlanting => activePlantCarrier != null;
    public bool IsDefusing => activeDefuser != null;
    public GameObject ActiveDefuser => activeDefuser;
    public BombSite SelectedAttackSite => selectedAttackSite;
    public bool IsBombPlanted => bombPlanted;
    public BombSite PlantedSite => plantedSite;
    public Vector3 PlantedBombPosition => plantedBombPosition;

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

        if (activePlantCarrier != null)
        {
            HealthSystem carrierHealth = activePlantCarrier.GetComponent<HealthSystem>();
            if (carrierHealth != null)
            {
                carrierHealth.Damaged -= OnActivePlantCarrierDamaged;
            }
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

        TeamTacticExecutor tacticExecutor = TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.GetComponent<TeamTacticExecutor>()
            : null;
        if (stats.team == roundManager.attackingTeam && tacticExecutor != null &&
            tacticExecutor.TryExecuteTacticalObjective(agent, motor))
        {
            return true;
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
                    TryStartPriorityPlant(agent, null);
                    return true;
                }

                if (selectedAttackSite != null)
                {
                    motor.MoveTo(FindBestPlantPosition(
                        selectedAttackSite,
                        agent.transform.position));
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
            bombPlanted = false;
            plantedSite = null;
            plantedBombPosition = default;
            lastLoggedCarrierPlantZone = null;
            SelectRoundTactics();
        }
        else if (state == RoundState.RoundEnd)
        {
            if (activePlantCarrier != null)
            {
                CancelPlant(activePlantCarrier);
            }

            if (activeDefuser != null)
            {
                CancelDefuse(activeDefuser);
            }

            ReleaseBombRecoveryAgent();
            ReleaseDefuseApproachAgent();
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

    public void SetSelectedAttackSite(BombSite site)
    {
        if (site == null || (site != siteA && site != siteB))
        {
            return;
        }

        selectedAttackSite = site;
        Debug.Log("Player tactic selected bomb site " + site.siteId + ".");
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

    public bool TryStartPriorityPlant(
        GameObject agent,
        GameObject immediateThreat,
        bool carefulPlant = false)
    {
        if (agent == null || roundManager == null ||
            roundManager.CurrentState != RoundState.Active)
        {
            return false;
        }

        BombCarrier carrier = agent.GetComponent<BombCarrier>();
        BombSite site = FindSiteContaining(agent);
        if (carrier == null || !carrier.HasBomb || site == null)
        {
            lastLoggedCarrierPlantZone = null;
            return false;
        }

        if (lastLoggedCarrierPlantZone != site)
        {
            lastLoggedCarrierPlantZone = site;
            Debug.Log("Bomb carrier entered plant zone");
            Debug.Log("Bomb carrier inside plant zone");
        }

        carefulPlant |= TeamTacticManager.Instance != null &&
                        TeamTacticManager.Instance.IsMidRoundTacticActive(
                            MidRoundTactic.ProbeAndPlant);
        if (!IsPlantWindowSafeEnough(agent, immediateThreat, carefulPlant))
        {
            return false;
        }

        Debug.Log("Plant window safe, starting plant");
        Debug.Log($"Starting bomb plant at Site {site.siteId}");
        BeginPlant(carrier, site);
        return IsPlanting;
    }

    public bool IsPlantWindowSafeEnough(
        GameObject agent,
        GameObject immediateThreat,
        bool carefulPlant)
    {
        if (agent == null)
        {
            return false;
        }

        HealthSystem health = agent.GetComponent<HealthSystem>();
        AgentSensors sensors = agent.GetComponent<AgentSensors>();
        AttackerCombatAI combatAI = agent.GetComponent<AttackerCombatAI>();
        if (health == null || health.IsDead || health.CurrentHealth <= minimumPlantHealth ||
            (combatAI != null && combatAI.IsForcedToRetreat))
        {
            return false;
        }

        if (IsImmediateVisiblePlantThreat(agent, immediateThreat, sensors))
        {
            return false;
        }

        int nearbySupport = 0;
        int detectedThreats = 0;
        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        foreach (AgentStats candidate in agents)
        {
            HealthSystem candidateHealth = candidate.GetComponent<HealthSystem>();
            if (candidateHealth == null || candidateHealth.IsDead ||
                candidate.gameObject == agent)
            {
                continue;
            }

            float distance = FlatDistance(agent.transform.position, candidate.transform.position);
            if (candidate.team == roundManager.attackingTeam)
            {
                if (distance <= plantSupportRadius)
                {
                    nearbySupport++;
                }
            }
            else if (distance <= plantThreatRadius && sensors != null &&
                     sensors.CanDetect(candidate.gameObject))
            {
                detectedThreats++;
            }
        }

        if (nearbySupport == 0 && detectedThreats > 0)
        {
            return false;
        }

        return carefulPlant
            ? detectedThreats <= nearbySupport
            : detectedThreats <= nearbySupport + 1;
    }

    public bool CanContinuePlanting(BombCarrier carrier)
    {
        if (carrier == null || !carrier.IsAlive)
        {
            return false;
        }

        GameObject agent = carrier.gameObject;
        AgentSensors sensors = agent.GetComponent<AgentSensors>();
        HealthSystem health = agent.GetComponent<HealthSystem>();
        if (health == null || health.CurrentHealth <= minimumPlantHealth)
        {
            return false;
        }

        if (IsImmediateVisiblePlantThreat(agent, null, sensors))
        {
            return false;
        }

        int support = 0;
        int threats = 0;
        foreach (AgentStats candidate in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (candidate.gameObject == agent)
            {
                continue;
            }

            HealthSystem candidateHealth = candidate.GetComponent<HealthSystem>();
            if (candidateHealth == null || candidateHealth.IsDead)
            {
                continue;
            }

            float distance = FlatDistance(agent.transform.position, candidate.transform.position);
            if (candidate.team == roundManager.attackingTeam &&
                distance <= plantSupportRadius)
            {
                support++;
            }
            else if (candidate.team == roundManager.defendingTeam &&
                     distance <= plantThreatRadius && sensors != null &&
                     sensors.CanDetect(candidate.gameObject))
            {
                threats++;
            }
        }

        return threats <= support + 1;
    }

    private bool IsImmediateVisiblePlantThreat(
        GameObject agent,
        GameObject immediateThreat,
        AgentSensors sensors)
    {
        if (immediateThreat != null && sensors != null &&
            FlatDistance(agent.transform.position, immediateThreat.transform.position) <=
            immediatePlantThreatDistance && sensors.CanDetect(immediateThreat))
        {
            return true;
        }

        if (sensors == null)
        {
            return false;
        }

        foreach (AgentStats candidate in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            HealthSystem candidateHealth = candidate.GetComponent<HealthSystem>();
            if (candidate.team != roundManager.defendingTeam ||
                candidateHealth == null || candidateHealth.IsDead)
            {
                continue;
            }

            if (FlatDistance(agent.transform.position, candidate.transform.position) <=
                immediatePlantThreatDistance && sensors.CanDetect(candidate.gameObject))
            {
                return true;
            }
        }

        return false;
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
        HealthSystem carrierHealth = carrier.GetComponent<HealthSystem>();
        if (carrierHealth != null)
        {
            carrierHealth.Damaged -= OnActivePlantCarrierDamaged;
            carrierHealth.Damaged += OnActivePlantCarrierDamaged;
        }
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
        HealthSystem carrierHealth = activePlantCarrier.GetComponent<HealthSystem>();
        if (carrierHealth != null)
        {
            carrierHealth.Damaged -= OnActivePlantCarrierDamaged;
        }
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

        DefenderTeamCoordinator coordinator = DefenderTeamCoordinator.Instance;
        if (coordinator != null && !coordinator.IsDesignatedDefuser(agent))
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

    public Vector3 FindBestDefusePosition(
        Vector3 bombPosition,
        Vector3 defenderPosition,
        GameObject defender = null)
    {
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder == null)
        {
            return bombPosition;
        }

        AgentMotor defenderMotor = defender != null
            ? defender.GetComponent<AgentMotor>()
            : null;
        float clearance = defenderMotor != null
            ? defenderMotor.AgentRadius + defenderMotor.MinObstacleClearance
            : 0.65f;

        Vector3 best = default;
        float bestScore = Mathf.Infinity;
        bool found = false;
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = ring == 0
                ? 0f
                : Mathf.Min(defuseInteractionRange - 0.15f, ring * 0.5f);
            int samples = ring == 0 ? 1 : 12;
            for (int i = 0; i < samples; i++)
            {
                Vector3 direction = Quaternion.Euler(
                    0f,
                    i * 360f / samples,
                    0f) * Vector3.forward;
                Vector3 candidate = bombPosition + direction * radius;
                candidate.y = defenderPosition.y;
                if (!pathfinder.IsValidAgentPosition(
                        candidate,
                        clearance,
                        defender,
                        true))
                {
                    continue;
                }

                List<Vector3> path = pathfinder.FindPath(defenderPosition, candidate);
                if (path == null ||
                    FlatDistance(candidate, bombPosition) > defuseInteractionRange)
                {
                    continue;
                }

                float score = path.Count +
                              FlatDistance(defenderPosition, candidate) * 0.1f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    found = true;
                }
            }
        }

        if (found && FlatDistance(best, bombPosition) > 0.1f)
        {
            Debug.Log("Adjusted defuse position");
        }
        if (found)
        {
            return best;
        }

        if (pathfinder.TryGetNearestWalkablePosition(
                bombPosition,
                Mathf.Max(0.1f, defuseInteractionRange - 0.05f),
                clearance,
                out Vector3 fallback,
                defender) &&
            FlatDistance(fallback, bombPosition) <= defuseInteractionRange &&
            pathfinder.FindPath(defenderPosition, fallback) != null)
        {
            Debug.Log("Adjusted defuse position");
            return fallback;
        }

        Debug.LogWarning("Cannot defuse: invalid defuse position");
        return bombPosition;
    }

    public Vector3 FindBestPlantPosition(BombSite site, Vector3 agentPosition)
    {
        if (site == null || AStarPathfinder3D.Instance == null)
        {
            return site != null ? site.PlantPosition : agentPosition;
        }

        BoxCollider zone = site.GetComponent<BoxCollider>();
        if (zone != null &&
            AStarPathfinder3D.Instance.TryGetNearestWalkablePositionInBounds(
                agentPosition,
                zone.bounds,
                0.5f,
                out Vector3 validPosition) &&
            AStarPathfinder3D.Instance.FindPath(agentPosition, validPosition) != null)
        {
            validPosition.y = site.PlantPosition.y;
            return validPosition;
        }

        if (zone != null)
        {
            Vector3 best = default;
            float bestDistance = Mathf.Infinity;
            for (int x = -2; x <= 2; x++)
            {
                for (int z = -2; z <= 2; z++)
                {
                    Vector3 candidate = zone.bounds.center + new Vector3(
                        zone.bounds.extents.x * x * 0.4f,
                        0f,
                        zone.bounds.extents.z * z * 0.4f);
                    candidate.y = agentPosition.y;
                    if (!AStarPathfinder3D.Instance.IsValidAgentPosition(candidate, 0.5f) ||
                        AStarPathfinder3D.Instance.FindPath(agentPosition, candidate) == null)
                    {
                        continue;
                    }

                    float distance = FlatDistance(agentPosition, candidate);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            if (bestDistance < Mathf.Infinity)
            {
                best.y = site.PlantPosition.y;
                return best;
            }
        }

        return site.PlantPosition;
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
        Debug.Log("Defender started defusing");
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
            activeBomb.CurrentState != BombState.Dropped || roundManager == null ||
            roundManager.CurrentState == RoundState.RoundEnd)
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

        bool plantWindowBecameUnsafe = !CanContinuePlanting(activePlantCarrier);
        invalid |= plantWindowBecameUnsafe;

        if (invalid)
        {
            if (plantWindowBecameUnsafe)
            {
                Debug.Log("Plant cancelled because immediate threat");
            }
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
        HealthSystem completedHealth = completedCarrier.GetComponent<HealthSystem>();
        if (completedHealth != null)
        {
            completedHealth.Damaged -= OnActivePlantCarrierDamaged;
        }
        completedCarrier.SetPlanting(false);
        activeBomb.CompletePlanting(completedSite);
        RestoreAgentAfterAction(suspendedPlantBrain, suspendedPlantMotor);
        activePlantCarrier = null;
        activePlantSite = null;
        suspendedPlantBrain = null;
        suspendedPlantMotor = null;
        plantProgress = 0f;
        bombPlanted = true;
        plantedSite = completedSite;
        plantedBombPosition = activeBomb.transform.position;
        OnBombPlanted?.Invoke(completedSite, plantedBombPosition);
        roundManager.NotifyBombPlanted();
        Debug.Log("Bomb planted at Site " + completedSite.siteId);
    }

    private void OnActivePlantCarrierDamaged(
        HealthSystem carrierHealth,
        float amount,
        GameObject attacker)
    {
        if (activePlantCarrier == null || carrierHealth == null)
        {
            return;
        }

        AgentSensors sensors = activePlantCarrier.GetComponent<AgentSensors>();
        bool immediateThreat = attacker != null && sensors != null &&
                               FlatDistance(
                                   activePlantCarrier.transform.position,
                                   attacker.transform.position) <=
                               immediatePlantThreatDistance &&
                               sensors.CanDetect(attacker);
        if (amount < heavyPlantDamageThreshold &&
            carrierHealth.CurrentHealth > minimumPlantHealth && !immediateThreat)
        {
            return;
        }

        Debug.Log(immediateThreat
            ? "Plant cancelled because immediate threat"
            : "Plant cancelled because heavy damage");
        CancelPlant(activePlantCarrier);
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
        Debug.Log("Defuse complete");
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
        // The coordinated defender AI owns retake entry, cover, and safe defuse
        // decisions. Keep the legacy nearest-agent autopilot only as a fallback.
        if (DefenderTeamCoordinator.Instance != null)
        {
            ReleaseDefuseApproachAgent();
            return;
        }

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

        defuseApproachMotor?.MoveTo(FindBestDefusePosition(
            activeBomb.transform.position,
            defuseApproachAgent.transform.position,
            defuseApproachAgent));
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
