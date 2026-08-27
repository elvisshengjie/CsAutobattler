using System;
using System.Collections.Generic;
using UnityEngine;

public enum DefenderSiteAlertState
{
    None,
    Suspicious,
    UnderAttack,
    BombPlanted
}

public enum BombKnowledgeState
{
    NotPlanted,
    PlantedUnknownSite,
    PlantedKnownSite
}

public enum DefenderRole
{
    SiteAnchor,
    Rotator,
    Support,
    Retaker,
    Defuser
}

public enum DefenderOrderType
{
    HoldSite,
    Investigate,
    Rotate,
    Flank,
    Retake,
    SearchBombSite,
    Defuse,
    CoverDefuser
}

public enum DefenderEngagementRole
{
    Pressure,
    LeftFlank,
    RightFlank,
    RearCutoff
}

public enum DefenderFormationStyle
{
    Concentrated,
    SingleFlank,
    Pincer,
    FullEncirclement
}

public struct DefenderOrder
{
    public DefenderOrderType type;
    public DefenderEngagementRole engagementRole;
    public BombSite site;
    public Vector3 destination;
    public Vector3 watchPosition;
    public float speedMultiplier;
}

public struct DefenderCoverSolution
{
    public Transform cover;
    public Vector3 hiddenPosition;
    public Vector3 peekPosition;
    public float score;
}

/// <summary>
/// Shares expiring sightings and site-level alerts without sharing live enemy
/// transforms. It owns defender roles, rotations, and planted-bomb retake orders.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenderTeamCoordinator : MonoBehaviour
{
    private sealed class SharedSighting
    {
        public Vector3 lastKnownPosition;
        public float spottedTime;
        public BombSiteId site;
    }

    private sealed class SiteAlert
    {
        public BombSite site;
        public DefenderSiteAlertState state;
        public float lastAttackTime = Mathf.NegativeInfinity;
        public float suspicionScore;
        public readonly Dictionary<GameObject, SharedSighting> sightings =
            new Dictionary<GameObject, SharedSighting>();
    }

    private struct VisionDebugLine
    {
        public Vector3 origin;
        public Vector3 target;
        public float expiresAt;
    }

    private sealed class EncirclementPlan
    {
        public GameObject target;
        public Vector3 lastKnownTargetPosition;
        public Vector3 frontDirection;
        public float assignmentValidUntil;
        public float nextDestinationRefreshTime;
        public int defenderCount;
        public DefenderFormationStyle formation;
        public readonly Dictionary<GameObject, DefenderEngagementRole> roles =
            new Dictionary<GameObject, DefenderEngagementRole>();
        public readonly Dictionary<GameObject, Vector3> destinations =
            new Dictionary<GameObject, Vector3>();
    }

    private sealed class PostPlantOrderMemory
    {
        public DefenderOrderType type;
        public BombSite site;
        public Vector3 destination;
        public Vector3 watchPosition;
        public float validUntil;
    }

    public static DefenderTeamCoordinator Instance { get; private set; }

    [Header("Shared Perception")]
    [SerializeField] private float siteDetectionRadius = 15f;
    [SerializeField] private float sharedMemoryDuration = 5f;
    [SerializeField] private float suspiciousDuration = 6f;
    [SerializeField] private float underAttackDuration = 10f;
    [SerializeField] private int siteUnderAttackThreshold = 2;

    [Header("Rotation")]
    [SerializeField] private float rotationDelay = 0.2f;
    [SerializeField] private float rotateSpeedMultiplier = 1.3f;
    [SerializeField] private bool requireConfirmedAttackBeforeRotate = true;
    [SerializeField] private float reinforcementContactMemory = 2.5f;

    [Header("Cover and Retake")]
    [SerializeField] private float coverSearchRadius = 14f;
    [SerializeField] private float defuseStartDistance = 1.2f;
    [SerializeField] private float defuseSafetyRadius = 7f;
    [SerializeField] private float defuseExclusiveRadius = 3.25f;
    [SerializeField] private float bombTimerRiskThreshold = 7f;
    [SerializeField] private float activeCombatDefuserPenalty = 10f;
    [SerializeField] private float postPlantOrderHoldTime = 1.75f;
    [SerializeField] private float postPlantOrderSwitchDistance = 1.25f;
    [SerializeField] private float postPlantOrderReachDistance = 0.85f;
    [SerializeField] private bool drawDebugGizmos = true;

    [Header("Bomb Search")]
    [SerializeField] private float siteCheckDistance = 1.6f;
    [SerializeField] private float bombVisualConfirmationRange = 24f;
    [SerializeField] private float searchAssignmentSpreadPenalty = 5f;

    [Header("Pursuit Limits")]
    [SerializeField] private float pursuitMaxDistanceFromSite = 12f;
    [SerializeField] private float pursuitLocalThreatRadius = 9f;

    [Header("Squad Encirclement")]
    [SerializeField] private bool enableEncirclement = true;
    [SerializeField] private int minimumEncirclementTeamSize = 2;
    [SerializeField] private float encirclementRadius = 5f;
    [SerializeField] private float encirclementAssignmentHoldTime = 6f;
    [SerializeField] private float encirclementDestinationRefreshTime = 0.9f;
    [SerializeField] private float encirclementTargetMoveThreshold = 1.25f;
    [SerializeField] private float encirclementPositionAdjustment = 2.5f;

    private RoundManager roundManager;
    private ObjectiveManager objectiveManager;
    private readonly SiteAlert alertA = new SiteAlert();
    private readonly SiteAlert alertB = new SiteAlert();
    private readonly SiteAlert alertC = new SiteAlert();
    private readonly Dictionary<GameObject, DefenderRole> roles =
        new Dictionary<GameObject, DefenderRole>();
    private readonly Dictionary<GameObject, BombSite> homeSites =
        new Dictionary<GameObject, BombSite>();
    private readonly HashSet<GameObject> rotationLogged = new HashSet<GameObject>();
    private readonly HashSet<GameObject> defuseMoveLogged = new HashSet<GameObject>();
    private readonly HashSet<GameObject> defuseRangeLogged = new HashSet<GameObject>();
    private readonly HashSet<BombSiteId> checkedBombSites = new HashSet<BombSiteId>();
    private readonly Dictionary<GameObject, BombSite> bombSearchAssignments =
        new Dictionary<GameObject, BombSite>();
    private readonly Dictionary<GameObject, Transform> holdCoverAssignments =
        new Dictionary<GameObject, Transform>();
    private readonly Dictionary<GameObject, PostPlantOrderMemory> postPlantOrders =
        new Dictionary<GameObject, PostPlantOrderMemory>();
    private readonly List<Transform> coverPoints = new List<Transform>();
    private readonly List<VisionDebugLine> visionDebugLines =
        new List<VisionDebugLine>();
    private readonly EncirclementPlan encirclement = new EncirclementPlan();
    private readonly HashSet<GameObject> recentEncirclementThreats =
        new HashSet<GameObject>();
    private readonly List<AgentStats> cachedEncirclementDefenders =
        new List<AgentStats>();
    private readonly List<AgentStats> eligibleEncirclementDefenders =
        new List<AgentStats>();
    private float nextEncirclementTeamCacheTime;
    private AgentStats designatedDefuser;
    private GameObject defusePositionOwner;
    private Vector3 designatedDefusePosition;
    private float confirmedAlertTime = Mathf.NegativeInfinity;
    private float nextTeamRefreshTime;
    private bool retakeLogged;
    private bool forcedDefuseLogged;
    private bool noLivingDefenderLogged;
    [SerializeField] private BombKnowledgeState bombKnowledgeState =
        BombKnowledgeState.NotPlanted;
    [SerializeField] private BombSite knownPlantedSite;
    private bool bombPositionKnown;
    [SerializeField] private Vector3 knownBombPosition;

    public float DefuseStartDistance => defuseStartDistance;
    public float DefuseSafetyRadius => defuseSafetyRadius;
    public bool BombTimerIsCritical => roundManager != null &&
                                       roundManager.BombTimeRemaining <=
                                       bombTimerRiskThreshold;
    public DefenderSiteAlertState AlertA => alertA.state;
    public DefenderSiteAlertState AlertB => alertB.state;
    public DefenderSiteAlertState AlertC => alertC.state;
    public BombKnowledgeState BombKnowledge => bombKnowledgeState;
    public bool PlantedSiteKnown => bombKnowledgeState == BombKnowledgeState.PlantedKnownSite &&
                                    knownPlantedSite != null;
    public BombSite KnownPlantedSite => PlantedSiteKnown ? knownPlantedSite : null;
    public bool BombPositionKnown => PlantedSiteKnown && bombPositionKnown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<RoundManager>() == null ||
            FindAnyObjectByType<DefenderTeamCoordinator>() != null)
        {
            return;
        }

        new GameObject("Defender AI Coordinator").AddComponent<DefenderTeamCoordinator>();
    }

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
        if (CampaignManager.Instance != null &&
            CampaignManager.Instance.IsCampaignScene)
        {
            ApplyCampaignTactic(CampaignManager.Instance.CurrentEnemyTactic);
        }
        CacheCoverPoints();
        RefreshDefenderTeam(true);
        if (roundManager != null)
        {
            roundManager.StateChanged += OnRoundStateChanged;
        }
        if (objectiveManager != null)
        {
            objectiveManager.OnBombPlanted += OnBombPlanted;
        }
    }

    public void ApplyCampaignTactic(CampaignEnemyTactic tactic)
    {
        switch (tactic)
        {
            case CampaignEnemyTactic.BasicHold:
                rotationDelay = 0.8f;
                rotateSpeedMultiplier = 1.1f;
                requireConfirmedAttackBeforeRotate = true;
                pursuitMaxDistanceFromSite = 9f;
                enableEncirclement = false;
                break;

            case CampaignEnemyTactic.SplitDefense:
                rotationDelay = 0.35f;
                rotateSpeedMultiplier = 1.25f;
                requireConfirmedAttackBeforeRotate = true;
                pursuitMaxDistanceFromSite = 12f;
                enableEncirclement = true;
                minimumEncirclementTeamSize = 2;
                break;

            case CampaignEnemyTactic.AggressiveRotation:
                rotationDelay = 0.05f;
                rotateSpeedMultiplier = 1.45f;
                requireConfirmedAttackBeforeRotate = false;
                pursuitMaxDistanceFromSite = 18f;
                enableEncirclement = true;
                minimumEncirclementTeamSize = 2;
                encirclementAssignmentHoldTime = 7.5f;
                break;
        }
    }

    private void OnDestroy()
    {
        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }
        if (objectiveManager != null)
        {
            objectiveManager.OnBombPlanted -= OnBombPlanted;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (Time.time >= nextTeamRefreshTime)
        {
            nextTeamRefreshTime = Time.time + 0.5f;
            RefreshDefenderTeam(false);
            ExpireSharedKnowledge();
        }

        if (roundManager != null &&
            roundManager.CurrentState == RoundState.BombPlanted)
        {
            UpdateBombKnowledge();
        }
    }

    public void ReportVisibleAttacker(GameObject reporter, GameObject attacker)
    {
        if (!IsLivingDefender(reporter) || attacker == null)
        {
            return;
        }

        holdCoverAssignments.Remove(reporter);

        AgentSensors sensors = reporter.GetComponent<AgentSensors>();
        AgentStats attackerStats = attacker.GetComponent<AgentStats>();
        if (sensors == null || attackerStats == null ||
            attackerStats.team != roundManager.attackingTeam ||
            !sensors.CanDetect(attacker))
        {
            return;
        }

        BombSite site = GetNearestSite(attacker.transform.position);
        if (site == null || FlatDistance(attacker.transform.position, site.PlantPosition) >
            siteDetectionRadius)
        {
            site = GetNearestSite(reporter.transform.position);
        }

        SiteAlert alert = GetAlert(site);
        Vector3 movement = Vector3.zero;
        if (alert.sightings.TryGetValue(attacker, out SharedSighting previous))
        {
            movement = attacker.transform.position - previous.lastKnownPosition;
            movement.y = 0f;
        }

        alert.sightings[attacker] = new SharedSighting
        {
            lastKnownPosition = attacker.transform.position,
            spottedTime = Time.time,
            site = site.siteId
        };
        alert.lastAttackTime = Time.time;

        int recentlySeen = CountRecentSightings(alert);
        bool bombCarrierSeen = attacker.GetComponent<BombCarrier>()?.HasBomb == true;
        bool attackerInsideSite = site.Contains(attacker);
        alert.suspicionScore = Mathf.Min(
            20f,
            alert.suspicionScore + 1f + (bombCarrierSeen ? 5f : 0f) +
            (attackerInsideSite ? 2f : 0f));
        IncreaseSuspicionFromMovement(attacker.transform.position, movement);
        SetAlertState(
            alert,
            recentlySeen >= siteUnderAttackThreshold || bombCarrierSeen || attackerInsideSite
                ? DefenderSiteAlertState.UnderAttack
                : DefenderSiteAlertState.Suspicious);

        if (roundManager.CurrentState == RoundState.BombPlanted &&
            bombKnowledgeState == BombKnowledgeState.PlantedUnknownSite &&
            attackerInsideSite && objectiveManager != null &&
            objectiveManager.ActiveBomb != null &&
            objectiveManager.ActiveBomb.PlantedSite == site)
        {
            ConfirmPlantedSite(
                site,
                $"visible attackers at Site {site.siteId}",
                false);
        }

        visionDebugLines.Add(new VisionDebugLine
        {
            origin = reporter.transform.position,
            target = attacker.transform.position,
            expiresAt = Time.time + 0.5f
        });
    }

    public void ReportCombat(GameObject defender, GameObject visibleAttacker)
    {
        if (!IsLivingDefender(defender))
        {
            return;
        }

        if (visibleAttacker != null)
        {
            ReportVisibleAttacker(defender, visibleAttacker);
        }

        SiteAlert alert = GetAlert(GetNearestSite(defender.transform.position));
        alert.lastAttackTime = Time.time;
        SetAlertState(alert, DefenderSiteAlertState.UnderAttack);
    }

    public void ReportDefenderDamaged(GameObject defender, GameObject attacker)
    {
        if (!IsLivingDefender(defender))
        {
            return;
        }

        BombSite site = GetNearestSite(defender.transform.position);
        SiteAlert alert = GetAlert(site);
        alert.lastAttackTime = Time.time;

        // A hit is direct evidence at this instant, so sharing this last-known
        // position is fair even when the shooter disappears behind a wall afterward.
        AgentStats attackerStats = attacker != null ? attacker.GetComponent<AgentStats>() : null;
        if (attackerStats != null && attackerStats.team == roundManager.attackingTeam)
        {
            alert.sightings[attacker] = new SharedSighting
            {
                lastKnownPosition = attacker.transform.position,
                spottedTime = Time.time,
                site = site.siteId
            };
        }

        SetAlertState(alert, DefenderSiteAlertState.UnderAttack);
    }

    /// <summary>
    /// Gives non-pressure defenders a stable side or rear route around the
    /// currently shared attacker position. The plan only uses reported
    /// sightings, so defenders cannot track an unseen attacker through walls.
    /// </summary>
    public bool TryGetEncirclementOrder(
        GameObject defender,
        GameObject observedAttacker,
        out DefenderOrder order)
    {
        order = default;
        if (!enableEncirclement || !IsLivingDefender(defender) ||
            roundManager == null)
        {
            return false;
        }

        bool postPlant = roundManager.CurrentState == RoundState.BombPlanted;
        if (postPlant && !BombPositionKnown)
        {
            return false;
        }

        bool hasSharedTarget = TryGetRecentSighting(
            observedAttacker,
            out GameObject sharedTarget,
            out Vector3 sharedPosition);
        if (!hasSharedTarget && observedAttacker != null)
        {
            hasSharedTarget = TryGetRecentSighting(
                null,
                out sharedTarget,
                out sharedPosition);
        }
        if (!hasSharedTarget)
        {
            return false;
        }

        List<AgentStats> defenders = GetEncirclementDefenders();
        if (postPlant)
        {
            AssignDefuserIfNeeded();
            if (designatedDefuser == null ||
                designatedDefuser.gameObject == defender)
            {
                return false;
            }

            eligibleEncirclementDefenders.Clear();
            foreach (AgentStats candidate in defenders)
            {
                if (candidate != designatedDefuser)
                {
                    eligibleEncirclementDefenders.Add(candidate);
                }
            }
            defenders = eligibleEncirclementDefenders;
        }

        if (defenders.Count < Mathf.Max(2, minimumEncirclementTeamSize) ||
            !EnsureEncirclementPlan(defenders, sharedTarget, sharedPosition) ||
            !encirclement.roles.TryGetValue(
                defender,
                out DefenderEngagementRole engagementRole) ||
            engagementRole == DefenderEngagementRole.Pressure)
        {
            return false;
        }

        HealthSystem health = defender.GetComponent<HealthSystem>();
        if (health != null && health.NormalizedHealth <= 0.3f)
        {
            return false;
        }

        Vector3 planTargetPosition = encirclement.lastKnownTargetPosition;
        if (!encirclement.destinations.TryGetValue(
                defender,
                out Vector3 destination))
        {
            return false;
        }

        order.type = DefenderOrderType.Flank;
        order.engagementRole = engagementRole;
        order.site = GetNearestSite(planTargetPosition);
        order.destination = destination;
        order.watchPosition = planTargetPosition;
        order.speedMultiplier = engagementRole == DefenderEngagementRole.RearCutoff
            ? 1.3f
            : 1.2f;
        return true;
    }

    public DefenderEngagementRole GetEngagementRole(GameObject defender)
    {
        return defender != null &&
               encirclement.roles.TryGetValue(
                   defender,
                   out DefenderEngagementRole role)
            ? role
            : DefenderEngagementRole.Pressure;
    }

    private bool EnsureEncirclementPlan(
        List<AgentStats> defenders,
        GameObject requestedTarget,
        Vector3 requestedTargetPosition)
    {
        Vector3 currentKnownPosition = default;
        bool currentTargetUsable = IsLivingAttacker(encirclement.target) &&
                                   TryGetRecentSighting(
                                       encirclement.target,
                                       out _,
                                       out currentKnownPosition);
        if (currentTargetUsable && encirclement.target != requestedTarget &&
            Time.time < encirclement.assignmentValidUntil)
        {
            requestedTarget = encirclement.target;
            requestedTargetPosition = currentKnownPosition;
        }

        DefenderFormationStyle recommendedFormation =
            EvaluateFormation(defenders, requestedTargetPosition);
        bool formationCanChange = Time.time >=
                                  encirclement.assignmentValidUntil;
        bool needsNewAssignments = !currentTargetUsable ||
                                   encirclement.target != requestedTarget ||
                                   encirclement.defenderCount != defenders.Count ||
                                   encirclement.roles.Count != defenders.Count ||
                                   (formationCanChange &&
                                    encirclement.formation != recommendedFormation);
        if (needsNewAssignments)
        {
            BuildEncirclementAssignments(
                defenders,
                requestedTarget,
                requestedTargetPosition,
                recommendedFormation);
        }

        RefreshEncirclementDestinationsIfNeeded(
            defenders,
            requestedTargetPosition);
        return encirclement.target != null && encirclement.roles.Count > 0;
    }

    private void BuildEncirclementAssignments(
        List<AgentStats> defenders,
        GameObject target,
        Vector3 targetPosition,
        DefenderFormationStyle formation)
    {
        ClearEncirclementPlan();
        encirclement.target = target;
        encirclement.lastKnownTargetPosition = targetPosition;
        encirclement.assignmentValidUntil =
            Time.time + encirclementAssignmentHoldTime;
        encirclement.defenderCount = defenders.Count;
        encirclement.formation = formation;

        Vector3 teamCenter = Vector3.zero;
        foreach (AgentStats defender in defenders)
        {
            teamCenter += defender.transform.position;
        }
        teamCenter /= Mathf.Max(1, defenders.Count);
        Vector3 front = teamCenter - targetPosition;
        front.y = 0f;
        encirclement.frontDirection = front.sqrMagnitude > 0.01f
            ? front.normalized
            : Vector3.forward;

        List<AgentStats> unassigned = new List<AgentStats>(defenders);
        List<DefenderEngagementRole> flankRoles = GetFlankRolesForFormation(
            formation,
            defenders,
            targetPosition);
        int pressureCount = Mathf.Max(0, defenders.Count - flankRoles.Count);
        unassigned.Sort((left, right) =>
        {
            float leftDistance = FlatDistance(
                left.transform.position,
                targetPosition);
            float rightDistance = FlatDistance(
                right.transform.position,
                targetPosition);
            return leftDistance.CompareTo(rightDistance);
        });
        for (int i = 0; i < pressureCount && unassigned.Count > 0; i++)
        {
            AgentStats pressure = unassigned[0];
            unassigned.RemoveAt(0);
            encirclement.roles[pressure.gameObject] =
                DefenderEngagementRole.Pressure;
        }

        foreach (DefenderEngagementRole flankRole in flankRoles)
        {
            AssignClosestDefenderToRole(
                unassigned,
                flankRole,
                targetPosition);
        }

        foreach (AgentStats remaining in unassigned)
        {
            encirclement.roles[remaining.gameObject] =
                DefenderEngagementRole.Pressure;
        }

        encirclement.nextDestinationRefreshTime = 0f;
        Debug.Log(
            $"Defender squad using {formation} against {target.name}: " +
            $"{pressureCount} pressure, " +
            $"{flankRoles.Count} flank/cutoff.");
    }

    private DefenderFormationStyle EvaluateFormation(
        List<AgentStats> defenders,
        Vector3 targetPosition)
    {
        float totalHealth = 0f;
        float closestDistance = Mathf.Infinity;
        foreach (AgentStats defender in defenders)
        {
            HealthSystem health = defender.GetComponent<HealthSystem>();
            totalHealth += health != null ? health.NormalizedHealth : 1f;
            closestDistance = Mathf.Min(
                closestDistance,
                FlatDistance(defender.transform.position, targetPosition));
        }

        int knownThreats = CountRecentEncirclementThreats(targetPosition);
        return SelectFormationForSituation(
            defenders.Count,
            Mathf.Max(1, knownThreats),
            totalHealth / Mathf.Max(1, defenders.Count),
            closestDistance);
    }

    public static DefenderFormationStyle SelectFormationForSituation(
        int defenderCount,
        int recentThreatCount,
        float averageHealth,
        float closestThreatDistance)
    {
        if (defenderCount >= 5)
        {
            // Five-player behavior intentionally remains the requested fixed
            // 2 pressure + left + right + rear formation.
            return DefenderFormationStyle.FullEncirclement;
        }

        bool criticalRisk = averageHealth < 0.4f ||
                            (recentThreatCount >= defenderCount &&
                             closestThreatDistance < 4f);
        bool highRisk = criticalRisk || averageHealth < 0.58f ||
                        recentThreatCount >= defenderCount ||
                        (recentThreatCount >= 2 &&
                         closestThreatDistance < 3.25f);

        return defenderCount switch
        {
            >= 4 when criticalRisk => DefenderFormationStyle.Concentrated,
            >= 4 when highRisk => DefenderFormationStyle.Pincer,
            >= 4 => DefenderFormationStyle.FullEncirclement,
            3 when criticalRisk => DefenderFormationStyle.Concentrated,
            3 when highRisk => DefenderFormationStyle.SingleFlank,
            3 => DefenderFormationStyle.Pincer,
            2 when highRisk => DefenderFormationStyle.Concentrated,
            2 => DefenderFormationStyle.SingleFlank,
            _ => DefenderFormationStyle.Concentrated
        };
    }

    private List<DefenderEngagementRole> GetFlankRolesForFormation(
        DefenderFormationStyle formation,
        List<AgentStats> defenders,
        Vector3 targetPosition)
    {
        List<DefenderEngagementRole> result =
            new List<DefenderEngagementRole>(3);
        switch (formation)
        {
            case DefenderFormationStyle.FullEncirclement:
                result.Add(DefenderEngagementRole.LeftFlank);
                result.Add(DefenderEngagementRole.RightFlank);
                result.Add(DefenderEngagementRole.RearCutoff);
                break;

            case DefenderFormationStyle.Pincer:
                result.Add(DefenderEngagementRole.LeftFlank);
                result.Add(DefenderEngagementRole.RightFlank);
                break;

            case DefenderFormationStyle.SingleFlank:
                result.Add(GetBestSingleFlankRole(defenders, targetPosition));
                break;
        }

        while (result.Count >= defenders.Count)
        {
            result.RemoveAt(result.Count - 1);
        }
        return result;
    }

    private DefenderEngagementRole GetBestSingleFlankRole(
        List<AgentStats> defenders,
        Vector3 targetPosition)
    {
        DefenderEngagementRole[] candidates =
        {
            DefenderEngagementRole.LeftFlank,
            DefenderEngagementRole.RightFlank,
            DefenderEngagementRole.RearCutoff
        };
        float averageHealth = 0f;
        float closestDistance = Mathf.Infinity;
        foreach (AgentStats defender in defenders)
        {
            HealthSystem health = defender.GetComponent<HealthSystem>();
            averageHealth += health != null ? health.NormalizedHealth : 1f;
            closestDistance = Mathf.Min(
                closestDistance,
                FlatDistance(defender.transform.position, targetPosition));
        }
        averageHealth /= Mathf.Max(1, defenders.Count);
        bool strongRearOpportunity =
            CountRecentEncirclementThreats(targetPosition) == 1 &&
            averageHealth >= 0.7f && closestDistance >= 4.5f;

        DefenderEngagementRole bestRole = DefenderEngagementRole.LeftFlank;
        float bestScore = Mathf.Infinity;
        foreach (DefenderEngagementRole candidateRole in candidates)
        {
            Vector3 desired = targetPosition + GetEncirclementOffset(
                candidateRole,
                encirclement.frontDirection,
                encirclementRadius);
            float shortestApproach = Mathf.Infinity;
            foreach (AgentStats defender in defenders)
            {
                shortestApproach = Mathf.Min(
                    shortestApproach,
                    FlatDistance(defender.transform.position, desired));
            }

            float score = shortestApproach;
            if (candidateRole == DefenderEngagementRole.RearCutoff)
            {
                score += strongRearOpportunity ? -2.5f : 1.5f;
            }
            if (score < bestScore)
            {
                bestScore = score;
                bestRole = candidateRole;
            }
        }

        return bestRole;
    }

    private int CountRecentEncirclementThreats(Vector3 center)
    {
        recentEncirclementThreats.Clear();
        CollectRecentEncirclementThreats(alertA, center);
        CollectRecentEncirclementThreats(alertB, center);
        CollectRecentEncirclementThreats(alertC, center);
        return recentEncirclementThreats.Count;
    }

    private void CollectRecentEncirclementThreats(
        SiteAlert alert,
        Vector3 center)
    {
        foreach (KeyValuePair<GameObject, SharedSighting> entry in alert.sightings)
        {
            if (IsLivingAttacker(entry.Key) &&
                Time.time <= entry.Value.spottedTime + sharedMemoryDuration &&
                FlatDistance(entry.Value.lastKnownPosition, center) <= 10f)
            {
                recentEncirclementThreats.Add(entry.Key);
            }
        }
    }

    private void AssignClosestDefenderToRole(
        List<AgentStats> unassigned,
        DefenderEngagementRole role,
        Vector3 targetPosition)
    {
        if (unassigned.Count == 0)
        {
            return;
        }

        Vector3 desired = targetPosition + GetEncirclementOffset(
            role,
            encirclement.frontDirection,
            encirclementRadius);
        int bestIndex = 0;
        float bestDistance = Mathf.Infinity;
        for (int i = 0; i < unassigned.Count; i++)
        {
            float distance = FlatDistance(
                unassigned[i].transform.position,
                desired);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        AgentStats selected = unassigned[bestIndex];
        unassigned.RemoveAt(bestIndex);
        encirclement.roles[selected.gameObject] = role;
    }

    private void RefreshEncirclementDestinationsIfNeeded(
        List<AgentStats> defenders,
        Vector3 latestTargetPosition)
    {
        bool targetMoved = FlatDistance(
            encirclement.lastKnownTargetPosition,
            latestTargetPosition) >= encirclementTargetMoveThreshold;
        if (!targetMoved && encirclement.destinations.Count > 0 &&
            Time.time < encirclement.nextDestinationRefreshTime)
        {
            return;
        }

        encirclement.lastKnownTargetPosition = latestTargetPosition;
        encirclement.nextDestinationRefreshTime =
            Time.time + encirclementDestinationRefreshTime;
        encirclement.destinations.Clear();
        foreach (AgentStats defender in defenders)
        {
            if (!encirclement.roles.TryGetValue(
                    defender.gameObject,
                    out DefenderEngagementRole role) ||
                role == DefenderEngagementRole.Pressure)
            {
                continue;
            }

            if (TryResolveEncirclementDestination(
                    defender.gameObject,
                    role,
                    latestTargetPosition,
                    out Vector3 destination))
            {
                encirclement.destinations[defender.gameObject] = destination;
            }
        }
    }

    private bool TryResolveEncirclementDestination(
        GameObject defender,
        DefenderEngagementRole role,
        Vector3 targetPosition,
        out Vector3 destination)
    {
        Vector3 baseOffset = GetEncirclementOffset(
            role,
            encirclement.frontDirection,
            encirclementRadius);
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        AgentMotor motor = defender.GetComponent<AgentMotor>();
        if (pathfinder == null || motor == null)
        {
            destination = targetPosition + baseOffset;
            return true;
        }

        float clearance = motor.AgentRadius + motor.MinObstacleClearance;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            float angle = attempt switch
            {
                1 => -20f,
                2 => 20f,
                3 => -38f,
                4 => 38f,
                _ => 0f
            };
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * baseOffset;
            Vector3 requested = targetPosition + offset;
            if (!pathfinder.TryGetNearestReachablePosition(
                    defender.transform.position,
                    requested,
                    encirclementPositionAdjustment,
                    clearance,
                    out Vector3 resolved,
                    out _,
                    defender,
                    false))
            {
                continue;
            }

            Vector3 actualOffset = resolved - targetPosition;
            actualOffset.y = 0f;
            if (actualOffset.sqrMagnitude > 0.01f &&
                Vector3.Dot(
                    actualOffset.normalized,
                    offset.normalized) >= 0.35f)
            {
                destination = resolved;
                return true;
            }
        }

        destination = default;
        return false;
    }

    public static Vector3 GetEncirclementOffset(
        DefenderEngagementRole role,
        Vector3 frontDirection,
        float radius)
    {
        frontDirection.y = 0f;
        if (frontDirection.sqrMagnitude <= 0.01f)
        {
            frontDirection = Vector3.forward;
        }
        frontDirection.Normalize();
        Vector3 left = Vector3.Cross(Vector3.up, frontDirection).normalized;
        float distance = Mathf.Max(1f, radius);
        return role switch
        {
            DefenderEngagementRole.LeftFlank =>
                (left + frontDirection * 0.15f).normalized * distance,
            DefenderEngagementRole.RightFlank =>
                (-left + frontDirection * 0.15f).normalized * distance,
            DefenderEngagementRole.RearCutoff => -frontDirection * distance,
            _ => frontDirection * distance
        };
    }

    private bool TryGetRecentSighting(
        GameObject preferredTarget,
        out GameObject target,
        out Vector3 position)
    {
        target = null;
        position = default;
        SharedSighting newest = null;
        FindNewestRecentSighting(
            alertA,
            preferredTarget,
            ref target,
            ref newest);
        FindNewestRecentSighting(
            alertB,
            preferredTarget,
            ref target,
            ref newest);
        FindNewestRecentSighting(
            alertC,
            preferredTarget,
            ref target,
            ref newest);

        if (newest == null)
        {
            return false;
        }

        position = newest.lastKnownPosition;
        return true;
    }

    private bool TryGetRecentSighting(
        SiteAlert alert,
        GameObject preferredTarget,
        out GameObject target,
        out Vector3 position)
    {
        target = null;
        position = default;
        SharedSighting newest = null;
        FindNewestRecentSighting(
            alert,
            preferredTarget,
            ref target,
            ref newest);
        if (newest == null)
        {
            return false;
        }

        position = newest.lastKnownPosition;
        return true;
    }

    private void FindNewestRecentSighting(
        SiteAlert alert,
        GameObject preferredTarget,
        ref GameObject target,
        ref SharedSighting newest)
    {
        foreach (KeyValuePair<GameObject, SharedSighting> entry in alert.sightings)
        {
            if (!IsLivingAttacker(entry.Key) ||
                Time.time > entry.Value.spottedTime + sharedMemoryDuration ||
                (preferredTarget != null && entry.Key != preferredTarget) ||
                (newest != null && newest.spottedTime >= entry.Value.spottedTime))
            {
                continue;
            }

            target = entry.Key;
            newest = entry.Value;
        }
    }

    private bool IsLivingAttacker(GameObject candidate)
    {
        if (candidate == null || roundManager == null)
        {
            return false;
        }

        AgentStats candidateStats = candidate.GetComponent<AgentStats>();
        HealthSystem candidateHealth = candidate.GetComponent<HealthSystem>();
        return candidateStats != null &&
               candidateStats.team == roundManager.attackingTeam &&
               candidateHealth != null && !candidateHealth.IsDead;
    }

    private void ClearEncirclementPlan()
    {
        encirclement.target = null;
        encirclement.lastKnownTargetPosition = default;
        encirclement.frontDirection = Vector3.zero;
        encirclement.assignmentValidUntil = 0f;
        encirclement.nextDestinationRefreshTime = 0f;
        encirclement.defenderCount = 0;
        encirclement.roles.Clear();
        encirclement.destinations.Clear();
    }

    private List<AgentStats> GetEncirclementDefenders()
    {
        if (Time.time < nextEncirclementTeamCacheTime &&
            cachedEncirclementDefenders.Count > 0)
        {
            return cachedEncirclementDefenders;
        }

        nextEncirclementTeamCacheTime = Time.time + 0.35f;
        cachedEncirclementDefenders.Clear();
        cachedEncirclementDefenders.AddRange(GetLivingDefenders());
        return cachedEncirclementDefenders;
    }

    public bool TryGetOrder(GameObject defender, out DefenderOrder order)
    {
        order = default;
        if (!IsLivingDefender(defender) || roundManager == null ||
            roundManager.CurrentState == RoundState.Preparation ||
            roundManager.CurrentState == RoundState.RoundEnd)
        {
            return false;
        }

        if (roundManager.CurrentState == RoundState.BombPlanted &&
            objectiveManager != null && objectiveManager.ActiveBomb != null)
        {
            UpdateBombKnowledge();
            if (!PlantedSiteKnown || !bombPositionKnown)
            {
                BombSite searchSite = PlantedSiteKnown
                    ? knownPlantedSite
                    : GetOrAssignBombSearchSite(defender);
                if (searchSite == null)
                {
                    return false;
                }

                Vector3 searchPoint = GetSiteSearchPoint(searchSite, defender.transform.position);
                order.type = DefenderOrderType.SearchBombSite;
                order.site = searchSite;
                order.destination = searchPoint;
                order.watchPosition = searchSite.PlantPosition;
                order.speedMultiplier = 1.4f;
                return true;
            }

            AssignDefuserIfNeeded();
            BombSite plantedSite = knownPlantedSite;
            Vector3 bombPosition = objectiveManager.ActiveBomb.transform.position;
            bool isDefuser = designatedDefuser != null &&
                             designatedDefuser.gameObject == defender;
            bool supporterMustYield = !isDefuser && ShouldYieldDefuseArea(
                false,
                FlatDistance(defender.transform.position, bombPosition),
                defuseExclusiveRadius);
            Vector3 defusePosition = bombPosition;
            if (isDefuser)
            {
                if (defusePositionOwner != defender)
                {
                    defusePositionOwner = defender;
                    designatedDefusePosition = objectiveManager.FindBestDefusePosition(
                        bombPosition,
                        defender.transform.position,
                        defender);
                }
                defusePosition = designatedDefusePosition;
            }

            bool attackersAlive = roundManager.AreAttackersAlive;
            bool timerCritical = roundManager.BombTimeRemaining <= bombTimerRiskThreshold;
            if (!attackersAlive)
            {
                if (!forcedDefuseLogged && isDefuser)
                {
                    forcedDefuseLogged = true;
                    Debug.Log("All strikers dead, defender forced to defuse");
                }

                order.type = isDefuser
                    ? DefenderOrderType.Defuse
                    : DefenderOrderType.CoverDefuser;
                order.site = plantedSite;
                order.destination = isDefuser
                    ? defusePosition
                    : GetPostPlantCoverPosition(
                        defender,
                        plantedSite,
                        bombPosition,
                        4f);
                order.watchPosition = bombPosition;
                order.speedMultiplier = isDefuser
                    ? 1.6f
                    : supporterMustYield ? 1.45f : 1.2f;
                if (isDefuser &&
                    FlatDistance(defender.transform.position, bombPosition) >
                    defuseStartDistance &&
                    defuseMoveLogged.Add(defender))
                {
                    Debug.Log("Defender moving to defuse");
                }
                return FinalizePostPlantOrder(
                    defender,
                    bombPosition,
                    true,
                    ref order);
            }

            if (FlatDistance(defender.transform.position, bombPosition) > 7f)
            {
                order.type = DefenderOrderType.Retake;
                order.site = plantedSite;
                order.destination = isDefuser
                    ? defusePosition
                    : GetPostPlantCoverPosition(
                        defender,
                        plantedSite,
                        bombPosition,
                        4.5f);
                order.watchPosition = bombPosition;
                order.speedMultiplier = 1.45f;
                return FinalizePostPlantOrder(
                    defender,
                    bombPosition,
                    false,
                    ref order);
            }
            bool hasSupport = HasRetakeSupport(defender, bombPosition) ||
                              HasActiveDefuseCover(
                                  defender,
                                  null,
                                  bombPosition);
            bool hasKnownThreat = TryGetLatestKnownPosition(
                plantedSite,
                out Vector3 known);
            bool knownThreatNearBomb = hasKnownThreat &&
                                       FlatDistance(known, bombPosition) <=
                                       defuseSafetyRadius;
            Vector3 retakeWatchPosition = hasKnownThreat
                ? known
                : GetLikelyRetakeEntranceWatch(defender, bombPosition);
            order.type = isDefuser
                ? DefenderOrderType.Defuse
                : DefenderOrderType.CoverDefuser;
            order.site = plantedSite;
            order.destination = isDefuser &&
                                (hasSupport || timerCritical || !knownThreatNearBomb)
                ? defusePosition
                : isDefuser
                    ? GetHoldPosition(
                        defender,
                        plantedSite,
                        retakeWatchPosition,
                        6f)
                    : GetPostPlantCoverPosition(
                        defender,
                        plantedSite,
                        retakeWatchPosition,
                        4f);
            order.watchPosition = retakeWatchPosition;
            order.speedMultiplier = isDefuser
                ? 1.3f
                : supporterMustYield ? 1.45f : 1.15f;
            if (isDefuser && order.destination == defusePosition &&
                FlatDistance(defender.transform.position, bombPosition) >
                defuseStartDistance &&
                defuseMoveLogged.Add(defender))
            {
                Debug.Log("Defender moving to defuse");
            }
            return FinalizePostPlantOrder(
                defender,
                bombPosition,
                timerCritical,
                ref order);
        }

        BombSite homeSite = GetHomeSite(defender);
        DefenderRole role = GetRole(defender);
        SiteAlert activeAlert = GetStrongestActiveAlert();
        bool multipleSitesUnderAttack = CountAlertsInState(
            DefenderSiteAlertState.UnderAttack) > 1;
        if (multipleSitesUnderAttack && role != DefenderRole.Rotator &&
            HasRecentContact(GetAlert(homeSite)))
        {
            activeAlert = GetAlert(homeSite);
        }

        if (activeAlert != null &&
            activeAlert.state == DefenderSiteAlertState.UnderAttack &&
            (homeSite == activeAlert.site ||
             !HasRecentContact(GetAlert(homeSite))) &&
            Time.time >= confirmedAlertTime + rotationDelay)
        {
            if (TryGetRecentSighting(
                    activeAlert,
                    null,
                    out GameObject sharedThreat,
                    out _) &&
                TryGetEncirclementOrder(
                    defender,
                    sharedThreat,
                    out DefenderOrder encirclementOrder))
            {
                order = encirclementOrder;
                return true;
            }

            order.type = homeSite == activeAlert.site
                ? DefenderOrderType.Investigate
                : DefenderOrderType.Rotate;
            order.site = activeAlert.site;
            Vector3 knownPosition = TryGetLatestKnownPosition(
                activeAlert.site,
                out Vector3 lastKnown)
                ? lastKnown
                : activeAlert.site.PlantPosition;
            order.destination = homeSite != activeAlert.site &&
                                FlatDistance(
                                    defender.transform.position,
                                    activeAlert.site.PlantPosition) > 6f
                ? activeAlert.site.GetNearestPlantPosition(defender.transform.position)
                : GetHoldPosition(
                    defender,
                    activeAlert.site,
                    knownPosition,
                    3f);
            order.watchPosition = knownPosition;
            order.speedMultiplier = rotateSpeedMultiplier;
            LogRotationOnce(defender, homeSite, activeAlert.site);
            return true;
        }

        bool investigateSuspicion = activeAlert != null &&
                                    activeAlert.state == DefenderSiteAlertState.Suspicious &&
                                    (role == DefenderRole.Rotator ||
                                     homeSite == activeAlert.site) &&
                                    !requireConfirmedAttackBeforeRotate;
        BombSite assignedSite = investigateSuspicion ? activeAlert.site : homeSite;
        if (assignedSite == null)
        {
            assignedSite = objectiveManager != null
                ? GetNearestSite(defender.transform.position)
                : null;
        }
        if (assignedSite == null)
        {
            return false;
        }

        Vector3 watch = activeAlert != null &&
                        TryGetLatestKnownPosition(activeAlert.site, out Vector3 suspiciousKnown)
            ? suspiciousKnown
            : assignedSite.PlantPosition;
        order.type = investigateSuspicion
            ? DefenderOrderType.Investigate
            : DefenderOrderType.HoldSite;
        order.site = assignedSite;
        order.destination = GetHoldPosition(defender, assignedSite, watch, 3.5f);
        order.watchPosition = watch;
        order.speedMultiplier = investigateSuspicion ? 1.1f : 1f;
        return true;
    }

    public bool ShouldImmediatelyReinforce(GameObject defender)
    {
        if (!IsLivingDefender(defender) || roundManager == null ||
            roundManager.CurrentState == RoundState.BombPlanted)
        {
            return false;
        }

        BombSite home = GetHomeSite(defender);
        SiteAlert homeAlert = GetAlert(home);
        SiteAlert attacked = GetStrongestActiveAlert(home);
        return attacked != null && attacked.site != null &&
               attacked.state == DefenderSiteAlertState.UnderAttack &&
               !HasRecentContact(homeAlert) &&
               Time.time >= confirmedAlertTime + rotationDelay;
    }

    public bool ShouldPrioritizeDefuse(
        GameObject defender,
        GameObject visibleAttacker)
    {
        if (!BombPositionKnown || defender == null || roundManager == null ||
            objectiveManager == null || objectiveManager.ActiveBomb == null)
        {
            return false;
        }

        AssignDefuserIfNeeded();
        bool isDesignated = designatedDefuser != null &&
                            designatedDefuser.gameObject == defender;
        bool attackersAlive = roundManager.AreAttackersAlive;
        bool timerCritical = BombTimerIsCritical;
        bool immediateThreat = IsImmediateThreatToDefender(
            defender,
            visibleAttacker);
        bool activeCover = HasActiveDefuseCover(
            defender,
            visibleAttacker,
            objectiveManager.ActiveBomb.transform.position);
        return ShouldCommitToDefuse(
            isDesignated,
            attackersAlive,
            timerCritical,
            immediateThreat,
               activeCover);
    }

    public bool IsDesignatedDefuser(GameObject defender)
    {
        if (!BombPositionKnown || defender == null)
        {
            return false;
        }

        AssignDefuserIfNeeded();
        return designatedDefuser != null &&
               designatedDefuser.gameObject == defender;
    }

    public bool MustClearDefuseArea(GameObject defender)
    {
        if (!BombPositionKnown || defender == null || objectiveManager == null ||
            objectiveManager.ActiveBomb == null)
        {
            return false;
        }

        bool designated = IsDesignatedDefuser(defender);
        float distance = FlatDistance(
            defender.transform.position,
            objectiveManager.ActiveBomb.transform.position);
        return ShouldYieldDefuseArea(
            designated,
            distance,
            defuseExclusiveRadius);
    }

    public static bool ShouldYieldDefuseArea(
        bool isDesignatedDefuser,
        float distanceToBomb,
        float exclusiveRadius)
    {
        return !isDesignatedDefuser &&
               distanceToBomb < Mathf.Max(1.5f, exclusiveRadius);
    }

    public static bool ShouldCommitToDefuse(
        bool isDesignatedDefuser,
        bool attackersAlive,
        bool timerCritical,
        bool immediateThreat,
        bool activeCover)
    {
        return isDesignatedDefuser &&
               (!attackersAlive || timerCritical || !immediateThreat || activeCover);
    }

    public bool CanStartDefuse(GameObject defender, GameObject visibleAttacker)
    {
        if (defender == null || objectiveManager == null ||
            objectiveManager.ActiveBomb == null || designatedDefuser == null ||
            designatedDefuser.gameObject != defender ||
            !BombPositionKnown)
        {
            return false;
        }

        Vector3 bombPosition = objectiveManager.ActiveBomb.transform.position;
        if (FlatDistance(defender.transform.position, bombPosition) > defuseStartDistance)
        {
            return false;
        }

        if (defuseRangeLogged.Add(defender))
        {
            Debug.Log("Defender reached defuse range");
        }

        if (!roundManager.AreAttackersAlive)
        {
            return true;
        }

        bool timerCritical = roundManager.BombTimeRemaining <= bombTimerRiskThreshold;
        if (timerCritical)
        {
            return true;
        }

        bool activeCover = HasActiveDefuseCover(
            defender,
            visibleAttacker,
            bombPosition);
        if (IsImmediateThreatToDefender(defender, visibleAttacker) &&
            !activeCover)
        {
            return false;
        }

        BombSite plantedSite = objectiveManager.ActiveBomb.PlantedSite;
        bool recentThreatNearBomb = TryGetLatestKnownPosition(
                                        plantedSite,
                                        out Vector3 known) &&
                                    FlatDistance(known, bombPosition) <=
                                    defuseSafetyRadius;
        return !recentThreatNearBomb ||
               HasRetakeSupport(defender, bombPosition) ||
               activeCover;
    }

    private bool HasActiveDefuseCover(
        GameObject defuser,
        GameObject visibleAttacker,
        Vector3 bombPosition)
    {
        foreach (AgentStats teammate in GetLivingDefenders())
        {
            if (teammate.gameObject == defuser ||
                (FlatDistance(teammate.transform.position, bombPosition) >
                 defuseSafetyRadius + 3f &&
                 FlatDistance(teammate.transform.position, defuser.transform.position) > 10f))
            {
                continue;
            }

            AgentBrain brain = teammate.GetComponent<AgentBrain>();
            GameObject teammateTarget = brain != null ? brain.CurrentTarget : null;
            AgentSensors sensors = teammate.GetComponent<AgentSensors>();
            if (teammateTarget != null &&
                (visibleAttacker == null || teammateTarget == visibleAttacker) &&
                sensors != null && sensors.CanDetect(teammateTarget))
            {
                return true;
            }

            if (visibleAttacker != null && sensors != null &&
                sensors.CanDetect(visibleAttacker))
            {
                return true;
            }

            DefenderAgentAI teammateAI = teammate.GetComponent<DefenderAgentAI>();
            if (teammateAI != null &&
                (teammateAI.CurrentState == DefenderCombatState.Shooting ||
                 teammateAI.CurrentState == DefenderCombatState.CoveringDefuser))
            {
                return true;
            }
        }

        return false;
    }

    public bool ShouldEngagePostPlantThreat(GameObject defender, GameObject attacker)
    {
        if (!BombPositionKnown || defender == null || attacker == null ||
            objectiveManager == null || objectiveManager.ActiveBomb == null)
        {
            return false;
        }

        HealthSystem defuserHealth = objectiveManager.ActiveDefuser != null
            ? objectiveManager.ActiveDefuser.GetComponent<HealthSystem>()
            : null;
        bool attackingDefuser = defuserHealth != null &&
                                defuserHealth.LastAttacker == attacker;
        HealthSystem defenderHealth = defender.GetComponent<HealthSystem>();
        bool directlyAttackingDefender = defenderHealth != null &&
                                         defenderHealth.LastAttacker == attacker;
        bool nearBomb = FlatDistance(
                            attacker.transform.position,
                            objectiveManager.ActiveBomb.transform.position) <=
                        defuseSafetyRadius;
        Vector3 toBomb = objectiveManager.ActiveBomb.transform.position -
                         defender.transform.position;
        Vector3 toAttacker = attacker.transform.position - defender.transform.position;
        toBomb.y = 0f;
        toAttacker.y = 0f;
        bool blockingRoute = toBomb.sqrMagnitude > 0.01f &&
                             toAttacker.sqrMagnitude > 0.01f &&
                             toAttacker.magnitude < toBomb.magnitude &&
                             Vector3.Dot(toBomb.normalized, toAttacker.normalized) > 0.45f;
        return (attackingDefuser || directlyAttackingDefender || nearBomb ||
                blockingRoute) &&
               IsImmediateThreatToDefender(defender, attacker);
    }

    private static bool IsImmediateThreatToDefender(
        GameObject defender,
        GameObject attacker)
    {
        if (defender == null || attacker == null)
        {
            return false;
        }

        AgentStats attackerStats = attacker.GetComponent<AgentStats>();
        AgentSensors defenderSensors = defender.GetComponent<AgentSensors>();
        float dangerRange = attackerStats != null
            ? Mathf.Max(4f,
                WeaponLoadout.Get(attackerStats.gameObject).MaximumRange * 1.25f)
            : 4f;
        return defenderSensors != null &&
               FlatDistance(defender.transform.position, attacker.transform.position) <=
               dangerRange && defenderSensors.HasLineOfSight(attacker);
    }

    public void ReportSiteChecked(GameObject defender, BombSite site)
    {
        if (!IsLivingDefender(defender) || site == null ||
            bombKnowledgeState == BombKnowledgeState.NotPlanted ||
            objectiveManager == null || objectiveManager.ActiveBomb == null)
        {
            return;
        }

        Vector3 checkPoint = GetSiteSearchPoint(site, defender.transform.position);
        if (!site.Contains(defender) &&
            FlatDistance(defender.transform.position, checkPoint) > siteCheckDistance)
        {
            return;
        }

        if (objectiveManager.ActiveBomb.PlantedSite == site)
        {
            if (!bombPositionKnown)
            {
                Debug.Log($"Site {site.siteId} checked: bomb found");
                ConfirmPlantedSite(site, "physical site check", true);
            }
            return;
        }

        if (bombKnowledgeState != BombKnowledgeState.PlantedUnknownSite)
        {
            return;
        }

        if (checkedBombSites.Add(site.siteId))
        {
            Debug.Log($"Site {site.siteId} checked: no bomb");
        }

        ClearAssignmentsForSite(site);
        BombSite remaining = GetUncheckedSite();
        if (remaining != null && OnlyOneSiteUnchecked())
        {
            // Eliminating one site is valid team knowledge; it does not reveal the
            // bomb's exact position, only which site must contain it.
            ConfirmPlantedSite(remaining, "other site eliminated", false);
        }
    }

    public bool CanPursueLastKnownEnemy(GameObject defender, Vector3 lastKnownPosition)
    {
        if (!IsLivingDefender(defender) ||
            roundManager.CurrentState == RoundState.BombPlanted)
        {
            return false;
        }

        BombSite home = GetHomeSite(defender);
        if (home != null &&
            FlatDistance(lastKnownPosition, home.PlantPosition) > pursuitMaxDistanceFromSite)
        {
            return false;
        }

        SiteAlert alert = GetAlert(GetNearestSite(lastKnownPosition));
        int knownEnemies = 0;
        foreach (SharedSighting sighting in alert.sightings.Values)
        {
            if (Time.time <= sighting.spottedTime + sharedMemoryDuration &&
                FlatDistance(sighting.lastKnownPosition, lastKnownPosition) <=
                pursuitLocalThreatRadius)
            {
                knownEnemies++;
            }
        }

        int nearbyDefenders = 0;
        foreach (AgentStats teammate in GetLivingDefenders())
        {
            if (FlatDistance(teammate.transform.position, defender.transform.position) <=
                pursuitLocalThreatRadius + 2f)
            {
                nearbyDefenders++;
            }
        }

        return knownEnemies <= nearbyDefenders + 1;
    }

    public bool ShouldRememberedThreatDelayBombOrder(Vector3 threatPosition)
    {
        if (!PlantedSiteKnown)
        {
            return false;
        }

        Vector3 protectedPosition = BombPositionKnown && objectiveManager != null &&
                                    objectiveManager.ActiveBomb != null
            ? objectiveManager.ActiveBomb.transform.position
            : knownPlantedSite.PlantPosition;
        return FlatDistance(threatPosition, protectedPosition) <= defuseSafetyRadius;
    }

    public Vector3 GetObjectivePositionFor(GameObject defender)
    {
        if (roundManager != null && roundManager.CurrentState == RoundState.BombPlanted)
        {
            if (BombPositionKnown && objectiveManager != null &&
                objectiveManager.ActiveBomb != null)
            {
                return objectiveManager.ActiveBomb.transform.position;
            }

            BombSite assigned = PlantedSiteKnown
                ? knownPlantedSite
                : GetOrAssignBombSearchSite(defender);
            if (assigned != null)
            {
                return GetSiteSearchPoint(
                    assigned,
                    defender != null ? defender.transform.position : assigned.PlantPosition);
            }
        }

        BombSite home = GetHomeSite(defender);
        return home != null
            ? home.PlantPosition
            : defender != null ? defender.transform.position : Vector3.zero;
    }

    private bool FinalizePostPlantOrder(
        GameObject defender,
        Vector3 bombPosition,
        bool urgent,
        ref DefenderOrder order)
    {
        StabilizePostPlantOrder(defender, bombPosition, urgent, ref order);
        return true;
    }

    private void StabilizePostPlantOrder(
        GameObject defender,
        Vector3 bombPosition,
        bool urgent,
        ref DefenderOrder order)
    {
        if (defender == null || !IsPostPlantMovementOrder(order.type))
        {
            return;
        }

        if (postPlantOrders.TryGetValue(defender, out PostPlantOrderMemory memory))
        {
            bool expired = Time.time > memory.validUntil;
            bool reachedPrevious =
                FlatDistance(defender.transform.position, memory.destination) <=
                postPlantOrderReachDistance;
            bool sameDestination =
                FlatDistance(memory.destination, order.destination) <=
                postPlantOrderSwitchDistance;
            bool differentSite = memory.site != null && order.site != null &&
                                 memory.site != order.site;

            if (!urgent && !expired && !reachedPrevious && !sameDestination &&
                !differentSite)
            {
                order.type = memory.type;
                order.site = memory.site;
                order.destination = memory.destination;
                order.watchPosition = memory.watchPosition;
                return;
            }
        }

        postPlantOrders[defender] = new PostPlantOrderMemory
        {
            type = order.type,
            site = order.site,
            destination = order.destination,
            watchPosition = order.watchPosition,
            validUntil = Time.time + postPlantOrderHoldTime
        };
    }

    private static bool IsPostPlantMovementOrder(DefenderOrderType type)
    {
        return type == DefenderOrderType.Retake ||
               type == DefenderOrderType.Defuse ||
               type == DefenderOrderType.CoverDefuser ||
               type == DefenderOrderType.SearchBombSite;
    }

    public DefenderRole GetRole(GameObject defender)
    {
        return defender != null && roles.TryGetValue(defender, out DefenderRole role)
            ? role
            : DefenderRole.Support;
    }

    public bool TryFindBestCover(
        GameObject defender,
        Vector3 threatPosition,
        Vector3 objectivePosition,
        float searchRadius,
        out DefenderCoverSolution solution,
        Transform excludedCover = null)
    {
        solution = default;
        if (defender == null)
        {
            return false;
        }

        float bestScore = Mathf.NegativeInfinity;
        foreach (Transform cover in coverPoints)
        {
            if (cover == null)
            {
                continue;
            }

            if (cover == excludedCover)
            {
                continue;
            }

            float distance = FlatDistance(defender.transform.position, cover.position);
            if (distance > Mathf.Min(searchRadius, coverSearchRadius))
            {
                continue;
            }

            Vector3 hiddenPosition = GetPositionBehindCover(cover, threatPosition);
            AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
            if (pathfinder != null &&
                (!pathfinder.IsValidAgentPosition(hiddenPosition, 0.5f) ||
                 pathfinder.FindPath(defender.transform.position, hiddenPosition) == null))
            {
                // Invalid candidates are expected near dense obstacle clusters.
                // Logging every rejected cover every AI tick causes severe editor
                // frame stalls that look like movement jitter for both teams.
                continue;
            }
            bool blocksThreat = IsLineBlocked(threatPosition, hiddenPosition);
            Vector3 peekPosition = GetPeekPosition(hiddenPosition, cover, threatPosition);
            bool canPeek = !IsLineBlocked(peekPosition, threatPosition);
            float objectiveDistance = FlatDistance(hiddenPosition, objectivePosition);
            float enemyDistance = FlatDistance(hiddenPosition, threatPosition);
            int occupantCount = CountCoverOccupants(cover, defender);

            float score = Mathf.Clamp01(1f - distance / searchRadius) * 4f +
                          (blocksThreat ? 6f : -8f) +
                          (canPeek ? 4f : 0f) +
                          Mathf.Clamp01(1f - objectiveDistance / 16f) * 3f +
                          (occupantCount == 0 ? 4f : -8f * occupantCount) -
                          Mathf.Clamp01(1f - enemyDistance / 5f) * 5f;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            solution = new DefenderCoverSolution
            {
                cover = cover,
                hiddenPosition = hiddenPosition,
                peekPosition = peekPosition,
                score = score
            };
        }

        return solution.cover != null;
    }

    public bool TryGetLatestKnownPosition(BombSite site, out Vector3 position)
    {
        position = default;
        SiteAlert alert = GetAlert(site);
        SharedSighting newest = null;
        foreach (SharedSighting sighting in alert.sightings.Values)
        {
            if (Time.time > sighting.spottedTime + sharedMemoryDuration ||
                (newest != null && newest.spottedTime >= sighting.spottedTime))
            {
                continue;
            }

            newest = sighting;
        }

        if (newest == null)
        {
            return false;
        }

        position = newest.lastKnownPosition;
        return true;
    }

    public static bool IsLineBlocked(Vector3 from, Vector3 to)
    {
        Vector3 origin = from + Vector3.up * 0.8f;
        Vector3 target = to + Vector3.up * 0.8f;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;
        if (distance <= 0.1f)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction / distance,
            distance - 0.1f,
            ~0,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                hit.collider.GetComponentInParent<AgentStats>() != null ||
                hit.collider.GetComponentInParent<BombSite>() != null)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void UpdateBombKnowledge()
    {
        if (roundManager == null || roundManager.CurrentState != RoundState.BombPlanted ||
            objectiveManager == null || objectiveManager.ActiveBomb == null)
        {
            return;
        }

        BombSite actualSite = objectiveManager.ActiveBomb.PlantedSite;
        if (actualSite == null)
        {
            return;
        }

        if (bombKnowledgeState != BombKnowledgeState.PlantedKnownSite ||
            knownPlantedSite != actualSite || !bombPositionKnown)
        {
            OnBombPlanted(actualSite, objectiveManager.ActiveBomb.transform.position);
            return;
        }

        foreach (AgentStats defender in GetLivingDefenders())
        {
            if (CanDefenderSeeBomb(defender.gameObject))
            {
                ConfirmPlantedSite(actualSite, "planted bomb seen", true);
                return;
            }
        }
    }

    private void OnBombPlanted(BombSite site, Vector3 bombPosition)
    {
        if (site == null)
        {
            return;
        }

        checkedBombSites.Clear();
        bombSearchAssignments.Clear();
        holdCoverAssignments.Clear();
        postPlantOrders.Clear();
        ClearEncirclementPlan();
        designatedDefuser = null;
        defusePositionOwner = null;
        knownBombPosition = bombPosition;
        retakeLogged = false;
        Debug.Log("Bomb planted event received by defender team");
        Debug.Log("Known planted site: " + site.siteId);
        ConfirmPlantedSite(site, "global planted event", true);

        foreach (AgentStats defender in GetLivingDefenders())
        {
            defender.GetComponent<AgentMotor>()?.Stop();
            Debug.Log("Defender switching objective to RetakeAndDefuse");
            BombSite home = GetHomeSite(defender.gameObject);
            if (home != null && home != site)
            {
                Debug.Log(
                    $"Defender leaving Site {home.siteId} to retake Site {site.siteId}");
            }
        }
    }

    private bool CanDefenderSeeBomb(GameObject defender)
    {
        if (!IsLivingDefender(defender) || objectiveManager == null ||
            objectiveManager.ActiveBomb == null)
        {
            return false;
        }

        AgentSensors sensors = defender.GetComponent<AgentSensors>();
        Vector3 bombPosition = objectiveManager.ActiveBomb.transform.position;
        return sensors != null &&
               FlatDistance(defender.transform.position, bombPosition) <=
               Mathf.Min(sensors.sightRange, bombVisualConfirmationRange) &&
               sensors.IsInsideFieldOfView(bombPosition) &&
               !IsLineBlocked(defender.transform.position, bombPosition);
    }

    private void ConfirmPlantedSite(
        BombSite site,
        string evidence,
        bool exactPositionKnown)
    {
        if (site == null)
        {
            return;
        }

        bool newlyKnown = bombKnowledgeState != BombKnowledgeState.PlantedKnownSite ||
                          knownPlantedSite != site;
        bool newlyFoundPosition = exactPositionKnown && !bombPositionKnown;
        bombKnowledgeState = BombKnowledgeState.PlantedKnownSite;
        knownPlantedSite = site;
        bombPositionKnown |= exactPositionKnown;
        bombSearchAssignments.Clear();
        SetAlertState(GetAlert(site), DefenderSiteAlertState.BombPlanted);

        if (newlyKnown)
        {
            Debug.Log($"Bomb planted: known site {site.siteId} ({evidence})");
        }

        if (!retakeLogged)
        {
            retakeLogged = true;
            Debug.Log("All defenders retaking planted site");
        }

        if (newlyFoundPosition)
        {
            Debug.Log("Bomb found, all defenders retaking");
        }
    }

    private BombSite GetOrAssignBombSearchSite(GameObject defender)
    {
        if (defender == null)
        {
            return null;
        }

        if (bombSearchAssignments.TryGetValue(defender, out BombSite assigned) &&
            assigned != null && !checkedBombSites.Contains(assigned.siteId))
        {
            return assigned;
        }

        BombSite best = null;
        float bestScore = Mathf.NegativeInfinity;
        SiteAlert[] candidates = GetAlerts();
        foreach (SiteAlert candidate in candidates)
        {
            if (candidate.site == null || checkedBombSites.Contains(candidate.site.siteId))
            {
                continue;
            }

            int assignedCount = 0;
            foreach (BombSite existing in bombSearchAssignments.Values)
            {
                if (existing == candidate.site)
                {
                    assignedCount++;
                }
            }

            float distance = FlatDistance(
                defender.transform.position,
                candidate.site.PlantPosition);
            float score = candidate.suspicionScore * 2f - distance -
                          assignedCount * searchAssignmentSpreadPenalty;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate.site;
            }
        }

        if (best == null)
        {
            return null;
        }

        bombSearchAssignments[defender] = best;
        Debug.Log($"Defender checking Site {best.siteId}");
        return best;
    }

    private Vector3 GetSiteSearchPoint(BombSite site, Vector3 fromPosition)
    {
        if (site == null)
        {
            return fromPosition;
        }

        Transform bestNamedPoint = null;
        float bestDistance = Mathf.Infinity;
        foreach (Transform child in site.GetComponentsInChildren<Transform>(true))
        {
            if (child == site.transform)
            {
                continue;
            }

            string pointName = child.name;
            bool isSearchPoint = pointName.IndexOf(
                                     "CheckPoint",
                                     StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 pointName.IndexOf(
                                     "SiteEntrance",
                                     StringComparison.OrdinalIgnoreCase) >= 0;
            float distance = FlatDistance(fromPosition, child.position);
            if (isSearchPoint && distance < bestDistance)
            {
                bestDistance = distance;
                bestNamedPoint = child;
            }
        }

        return bestNamedPoint != null
            ? bestNamedPoint.position
            : site.GetNearestPlantPosition(fromPosition);
    }

    private void ClearAssignmentsForSite(BombSite site)
    {
        List<GameObject> clear = new List<GameObject>();
        foreach (KeyValuePair<GameObject, BombSite> assignment in bombSearchAssignments)
        {
            if (assignment.Value == site)
            {
                clear.Add(assignment.Key);
            }
        }

        foreach (GameObject defender in clear)
        {
            bombSearchAssignments.Remove(defender);
        }
    }

    private BombSite GetUncheckedSite()
    {
        foreach (SiteAlert alert in GetAlerts())
        {
            if (alert.site != null && !checkedBombSites.Contains(alert.site.siteId))
                return alert.site;
        }
        return null;
    }

    private bool OnlyOneSiteUnchecked()
    {
        int count = 0;
        foreach (SiteAlert alert in GetAlerts())
            if (alert.site != null && !checkedBombSites.Contains(alert.site.siteId))
                count++;
        return count == 1;
    }

    private void IncreaseSuspicionFromMovement(Vector3 attackerPosition, Vector3 movement)
    {
        movement.y = 0f;
        if (movement.sqrMagnitude < 0.04f)
        {
            return;
        }

        movement.Normalize();
        SiteAlert[] candidates = GetAlerts();
        foreach (SiteAlert candidate in candidates)
        {
            if (candidate.site == null)
            {
                continue;
            }

            Vector3 toSite = candidate.site.PlantPosition - attackerPosition;
            toSite.y = 0f;
            if (toSite.sqrMagnitude > 0.01f)
            {
                candidate.suspicionScore = Mathf.Min(
                    20f,
                    candidate.suspicionScore +
                    Mathf.Max(0f, Vector3.Dot(movement, toSite.normalized)) * 2f);
            }
        }
    }

    private void SetAlertState(SiteAlert alert, DefenderSiteAlertState newState)
    {
        if (alert == null || newState <= alert.state)
        {
            return;
        }

        alert.state = newState;
        if (newState == DefenderSiteAlertState.UnderAttack)
        {
            confirmedAlertTime = Time.time;
            rotationLogged.Clear();
            Debug.Log(alert.site.siteId + " site under attack.");
            Debug.Log(
                $"Site {alert.site.siteId} under attack, other site defenders rotating");
        }
    }

    private void ExpireSharedKnowledge()
    {
        ExpireAlert(alertA);
        ExpireAlert(alertB);
        ExpireAlert(alertC);
        alertA.suspicionScore = Mathf.Max(0f, alertA.suspicionScore - 0.1f);
        alertB.suspicionScore = Mathf.Max(0f, alertB.suspicionScore - 0.1f);
        alertC.suspicionScore = Mathf.Max(0f, alertC.suspicionScore - 0.1f);
        for (int i = visionDebugLines.Count - 1; i >= 0; i--)
        {
            if (Time.time > visionDebugLines[i].expiresAt)
            {
                visionDebugLines.RemoveAt(i);
            }
        }
    }

    private void ExpireAlert(SiteAlert alert)
    {
        List<GameObject> expired = new List<GameObject>();
        foreach (KeyValuePair<GameObject, SharedSighting> entry in alert.sightings)
        {
            if (Time.time > entry.Value.spottedTime + sharedMemoryDuration)
            {
                expired.Add(entry.Key);
            }
        }

        foreach (GameObject enemy in expired)
        {
            alert.sightings.Remove(enemy);
        }

        if (alert.state == DefenderSiteAlertState.BombPlanted)
        {
            return;
        }

        float age = Time.time - alert.lastAttackTime;
        if (alert.state == DefenderSiteAlertState.UnderAttack &&
            age > underAttackDuration)
        {
            alert.state = DefenderSiteAlertState.Suspicious;
            alert.lastAttackTime = Time.time;
            return;
        }

        if (alert.state == DefenderSiteAlertState.Suspicious &&
            age > suspiciousDuration)
        {
            alert.state = DefenderSiteAlertState.None;
        }
    }

    private int CountRecentSightings(SiteAlert alert)
    {
        int count = 0;
        foreach (SharedSighting sighting in alert.sightings.Values)
        {
            if (Time.time <= sighting.spottedTime + sharedMemoryDuration)
            {
                count++;
            }
        }

        return count;
    }

    private bool HasRecentContact(SiteAlert alert)
    {
        if (alert == null)
        {
            return false;
        }

        if (Time.time <= alert.lastAttackTime + reinforcementContactMemory)
        {
            return true;
        }

        return CountRecentSightings(alert) > 0;
    }

    private void RefreshDefenderTeam(bool forceRoles)
    {
        if (roundManager == null)
        {
            ResolveReferences();
        }

        List<AgentStats> defenders = GetLivingDefenders();
        foreach (AgentStats defender in defenders)
        {
            if (defender.GetComponent<DefenderAgentAI>() == null)
            {
                defender.gameObject.AddComponent<DefenderAgentAI>();
            }
        }

        if (!forceRoles && roles.Count > 0)
        {
            return;
        }

        roles.Clear();
        homeSites.Clear();
        BombSite[] availableSites = objectiveManager != null
            ? objectiveManager.GetSites()
            : Array.Empty<BombSite>();
        if (availableSites.Length == 0)
        {
            return;
        }

        for (int i = 0; i < defenders.Count; i++)
        {
            AgentStats defender = defenders[i];
            if (i == defenders.Count - 1 && defenders.Count > availableSites.Length)
            {
                roles[defender.gameObject] = DefenderRole.Rotator;
                homeSites[defender.gameObject] = GetNearestSite(
                    defender.transform.position);
                continue;
            }

            homeSites[defender.gameObject] = availableSites[i % availableSites.Length];
            roles[defender.gameObject] = i < availableSites.Length
                ? DefenderRole.SiteAnchor
                : DefenderRole.Support;
        }
    }

    private void AssignDefuserIfNeeded()
    {
        GameObject activeDefuser = objectiveManager != null
            ? objectiveManager.ActiveDefuser
            : null;
        if (activeDefuser != null && IsLivingDefender(activeDefuser))
        {
            designatedDefuser = activeDefuser.GetComponent<AgentStats>();
            if (designatedDefuser != null)
            {
                roles[activeDefuser] = DefenderRole.Defuser;
            }
            return;
        }

        if (designatedDefuser != null && IsLivingDefender(designatedDefuser.gameObject))
        {
            return;
        }

        designatedDefuser = null;
        defusePositionOwner = null;
        if (objectiveManager == null || objectiveManager.ActiveBomb == null)
        {
            return;
        }

        float bestScore = Mathf.Infinity;
        foreach (AgentStats defender in GetLivingDefenders())
        {
            float distance = FlatDistance(
                defender.transform.position,
                objectiveManager.ActiveBomb.transform.position);
            HealthSystem health = defender.GetComponent<HealthSystem>();
            bool activelyEngaged = IsDefenderActivelyEngaged(defender.gameObject);
            float score = CalculateDefuserSelectionScore(
                distance,
                activelyEngaged,
                health != null ? health.NormalizedHealth : 1f,
                activeCombatDefuserPenalty);
            if (score < bestScore)
            {
                bestScore = score;
                designatedDefuser = defender;
            }
        }

        if (designatedDefuser != null)
        {
            noLivingDefenderLogged = false;
            roles[designatedDefuser.gameObject] = DefenderRole.Defuser;
            Debug.Log(
                $"Designated defuser: {designatedDefuser.name} " +
                "(available teammate preferred over active fighter)");
        }
        else if (!noLivingDefenderLogged)
        {
            noLivingDefenderLogged = true;
            Debug.LogWarning("No living defender available to defuse");
        }
    }

    public static float CalculateDefuserSelectionScore(
        float distanceToBomb,
        bool activelyEngaged,
        float normalizedHealth,
        float combatPenalty = 10f)
    {
        return Mathf.Max(0f, distanceToBomb) +
               (activelyEngaged ? Mathf.Max(0f, combatPenalty) : 0f) +
               (1f - Mathf.Clamp01(normalizedHealth)) * 2f;
    }

    private static bool IsDefenderActivelyEngaged(GameObject defender)
    {
        if (defender == null)
        {
            return false;
        }

        AgentBrain brain = defender.GetComponent<AgentBrain>();
        GameObject target = brain != null ? brain.CurrentTarget : null;
        AgentSensors sensors = defender.GetComponent<AgentSensors>();
        DefenderAgentAI defenderAI = defender.GetComponent<DefenderAgentAI>();
        return (target != null && sensors != null && sensors.CanDetect(target)) ||
               (defenderAI != null &&
                defenderAI.CurrentState == DefenderCombatState.Shooting);
    }

    private bool HasRetakeSupport(GameObject defuser, Vector3 bombPosition)
    {
        int livingDefenders = 0;
        bool nearbySupport = false;
        foreach (AgentStats defender in GetLivingDefenders())
        {
            livingDefenders++;
            if (defender.gameObject != defuser &&
                FlatDistance(defender.transform.position, bombPosition) <= 8f)
            {
                nearbySupport = true;
            }
        }

        return livingDefenders <= 1 || nearbySupport;
    }

    private static Vector3 GetLikelyRetakeEntranceWatch(
        GameObject defender,
        Vector3 bombPosition)
    {
        Vector3 outward = bombPosition - defender.transform.position;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.01f)
        {
            outward = defender.transform.forward;
            outward.y = 0f;
        }

        return bombPosition + outward.normalized * 8f;
    }

    private Vector3 GetPostPlantCoverPosition(
        GameObject defender,
        BombSite site,
        Vector3 watchPosition,
        float radius)
    {
        Vector3 bombPosition = objectiveManager != null &&
                               objectiveManager.ActiveBomb != null
            ? objectiveManager.ActiveBomb.transform.position
            : site != null ? site.PlantPosition : defender.transform.position;
        List<AgentStats> living = GetEncirclementDefenders();
        int supporterIndex = 0;
        int supporterCount = 0;
        foreach (AgentStats candidate in living)
        {
            if (designatedDefuser != null && candidate == designatedDefuser)
            {
                continue;
            }

            if (candidate.gameObject == defender)
            {
                supporterIndex = supporterCount;
            }
            supporterCount++;
        }

        Vector3 towardThreat = watchPosition - bombPosition;
        towardThreat.y = 0f;
        if (towardThreat.sqrMagnitude <= 0.01f)
        {
            towardThreat = bombPosition - defender.transform.position;
            towardThreat.y = 0f;
        }
        if (towardThreat.sqrMagnitude <= 0.01f)
        {
            towardThreat = Vector3.forward;
        }

        float angle = GetPostPlantCoverAngle(supporterIndex, supporterCount);
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        AgentMotor motor = defender.GetComponent<AgentMotor>();
        float clearance = motor != null
            ? motor.AgentRadius + motor.MinObstacleClearance
            : 0.65f;
        float coverDistance = Mathf.Max(
            radius,
            defuseExclusiveRadius + 0.75f);
        Vector3 preferredDirection = Quaternion.Euler(0f, angle, 0f) *
                                     towardThreat.normalized;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float angleAdjustment = attempt switch
            {
                1 => -25f,
                2 => 25f,
                3 => -50f,
                4 => 50f,
                5 => 180f,
                _ => 0f
            };
            Vector3 desiredDirection = Quaternion.Euler(
                0f,
                angleAdjustment,
                0f) * preferredDirection;
            Vector3 requested = bombPosition + desiredDirection * coverDistance;
            requested.y = defender.transform.position.y;
            if (pathfinder == null)
            {
                return requested;
            }

            if (!pathfinder.TryGetNearestReachablePosition(
                    defender.transform.position,
                    requested,
                    2f,
                    clearance,
                    out Vector3 resolved,
                    out _,
                    defender,
                    false))
            {
                continue;
            }

            Vector3 resolvedDirection = resolved - bombPosition;
            resolvedDirection.y = 0f;
            if (resolvedDirection.magnitude >= defuseExclusiveRadius + 0.35f &&
                Vector3.Dot(
                    resolvedDirection.normalized,
                    desiredDirection.normalized) >= 0.2f)
            {
                return resolved;
            }
        }

        Vector3 fallback = GetHoldPosition(
            defender,
            site,
            watchPosition,
            coverDistance);
        if (FlatDistance(fallback, bombPosition) >=
            defuseExclusiveRadius + 0.35f)
        {
            return fallback;
        }

        // Preserve the exclusive interaction area even when the map has no
        // ideal cover point. AgentMotor will validate this outward request.
        Vector3 emergencyOutside = bombPosition +
                                   preferredDirection * coverDistance;
        emergencyOutside.y = defender.transform.position.y;
        return emergencyOutside;
    }

    public static float GetPostPlantCoverAngle(int index, int count)
    {
        if (count <= 1)
        {
            return 180f;
        }
        if (count == 2)
        {
            return index == 0 ? -90f : 90f;
        }
        if (count == 3)
        {
            return index switch
            {
                0 => -100f,
                1 => 100f,
                _ => 180f
            };
        }
        if (count == 4)
        {
            return index switch
            {
                0 => -55f,
                1 => 55f,
                2 => -135f,
                _ => 135f
            };
        }

        return -150f + index * 300f / Mathf.Max(1, count - 1);
    }

    private Vector3 GetHoldPosition(
        GameObject defender,
        BombSite site,
        Vector3 watchPosition,
        float fallbackRadius)
    {
        if (holdCoverAssignments.TryGetValue(
                defender,
                out Transform assignedCover))
        {
            if (IsAssignedHoldCoverUsable(
                    defender,
                    assignedCover,
                    site,
                    watchPosition))
            {
                return GetPositionBehindCover(assignedCover, watchPosition);
            }

            holdCoverAssignments.Remove(defender);
        }

        if (site != null && TryFindBestCover(
                defender,
                watchPosition,
                site.PlantPosition,
                coverSearchRadius,
                out DefenderCoverSolution cover))
        {
            holdCoverAssignments[defender] = cover.cover;
            return cover.hiddenPosition;
        }

        List<AgentStats> defenders = GetLivingDefenders();
        int index = defenders.FindIndex(item => item.gameObject == defender);
        float angle = 35f + index * 360f / Mathf.Max(1, defenders.Count);
        return site.PlantPosition +
               Quaternion.Euler(0f, angle, 0f) * Vector3.forward * fallbackRadius;
    }

    private bool IsAssignedHoldCoverUsable(
        GameObject defender,
        Transform cover,
        BombSite site,
        Vector3 watchPosition)
    {
        if (defender == null || cover == null ||
            (site != null && FlatDistance(
                cover.position,
                site.PlantPosition) > coverSearchRadius + 3f))
        {
            return false;
        }

        Vector3 hiddenPosition = GetPositionBehindCover(cover, watchPosition);
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        return pathfinder == null ||
               pathfinder.IsValidAgentPosition(
                   hiddenPosition,
                   0.5f,
                   defender,
                   false);
    }

    private Vector3 GetPositionBehindCover(Transform cover, Vector3 threatPosition)
    {
        Collider collider = cover.GetComponent<Collider>();
        Vector3 center = collider != null ? collider.bounds.center : cover.position;
        Vector3 away = center - threatPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
        {
            away = Vector3.forward;
        }

        away.Normalize();
        float extent = 0.6f;
        if (collider != null)
        {
            extent = Mathf.Abs(away.x) * collider.bounds.extents.x +
                     Mathf.Abs(away.z) * collider.bounds.extents.z;
        }

        Vector3 result = center + away * (extent + 0.75f);
        result.y = defenderGroundHeight;
        return result;
    }

    private const float defenderGroundHeight = 0f;

    private static Vector3 GetPeekPosition(
        Vector3 hiddenPosition,
        Transform cover,
        Vector3 threatPosition)
    {
        Vector3 toThreat = threatPosition - hiddenPosition;
        toThreat.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toThreat.normalized);
        Vector3 first = hiddenPosition + side * 1.1f;
        Vector3 second = hiddenPosition - side * 1.1f;
        return !IsLineBlocked(first, threatPosition) ? first : second;
    }

    private int CountCoverOccupants(Transform cover, GameObject requester)
    {
        int count = 0;
        foreach (KeyValuePair<GameObject, Transform> assignment in
                 holdCoverAssignments)
        {
            if (assignment.Key != null && assignment.Key != requester &&
                assignment.Value == cover && IsLivingDefender(assignment.Key))
            {
                count++;
            }
        }

        DefenderAgentAI[] defenders =
            FindObjectsByType<DefenderAgentAI>(FindObjectsInactive.Exclude);
        foreach (DefenderAgentAI defender in defenders)
        {
            if (defender.gameObject != requester && defender.CurrentCover == cover)
            {
                count++;
            }
        }

        AttackerCombatAI[] attackers =
            FindObjectsByType<AttackerCombatAI>(FindObjectsInactive.Exclude);
        foreach (AttackerCombatAI attacker in attackers)
        {
            if (attacker.gameObject != requester && attacker.CurrentCover == cover)
            {
                count++;
            }
        }

        return count;
    }

    private void LogRotationOnce(GameObject defender, BombSite from, BombSite to)
    {
        if (defender == null || from == null || to == null || from == to ||
            !rotationLogged.Add(defender))
        {
            return;
        }

        Debug.Log(from.siteId + " defenders rotating to " + to.siteId + ": " + defender.name);
        Debug.Log("Defender leaving empty site to reinforce");
    }

    private SiteAlert GetStrongestActiveAlert()
    {
        return GetStrongestActiveAlert(null);
    }

    private SiteAlert GetStrongestActiveAlert(BombSite excludedSite)
    {
        SiteAlert strongest = null;
        foreach (SiteAlert candidate in GetAlerts())
        {
            if (candidate.site == null || candidate.site == excludedSite)
                continue;
            if (strongest == null || candidate.state > strongest.state ||
                candidate.state == strongest.state &&
                candidate.lastAttackTime > strongest.lastAttackTime)
                strongest = candidate;
        }
        return strongest;
    }

    private SiteAlert GetAlert(BombSite site)
    {
        if (site == null) return null;
        return site.siteId switch
        {
            BombSiteId.B => alertB,
            BombSiteId.C => alertC,
            _ => alertA
        };
    }

    private BombSite GetNearestSite(Vector3 position)
    {
        BombSite nearest = null;
        float nearestDistance = Mathf.Infinity;
        foreach (SiteAlert alert in GetAlerts())
        {
            if (alert.site == null) continue;
            float distance = FlatDistance(position, alert.site.PlantPosition);
            if (distance >= nearestDistance) continue;
            nearest = alert.site;
            nearestDistance = distance;
        }
        return nearest;
    }

    private SiteAlert[] GetAlerts()
    {
        return new[] { alertA, alertB, alertC };
    }

    private int CountAlertsInState(DefenderSiteAlertState state)
    {
        int count = 0;
        foreach (SiteAlert alert in GetAlerts())
            if (alert.site != null && alert.state == state)
                count++;
        return count;
    }

    private BombSite GetHomeSite(GameObject defender)
    {
        return defender != null && homeSites.TryGetValue(defender, out BombSite site)
            ? site
            : GetNearestSite(defender != null ? defender.transform.position : Vector3.zero);
    }

    private List<AgentStats> GetLivingDefenders()
    {
        List<AgentStats> defenders = new List<AgentStats>();
        if (roundManager == null)
        {
            return defenders;
        }

        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        foreach (AgentStats agent in agents)
        {
            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (agent.team == roundManager.defendingTeam &&
                health != null && !health.IsDead)
            {
                defenders.Add(agent);
            }
        }

        defenders.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        return defenders;
    }

    private bool IsLivingDefender(GameObject candidate)
    {
        if (candidate == null || roundManager == null)
        {
            return false;
        }

        AgentStats stats = candidate.GetComponent<AgentStats>();
        HealthSystem health = candidate.GetComponent<HealthSystem>();
        return stats != null && stats.team == roundManager.defendingTeam &&
               health != null && !health.IsDead;
    }

    private void CacheCoverPoints()
    {
        coverPoints.Clear();
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
        foreach (Transform candidate in transforms)
        {
            string name = candidate.name;
            if (name.IndexOf("Cover", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("DefenderHold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("RetakeCover", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                coverPoints.Add(candidate);
            }
        }
    }

    private void ResolveReferences()
    {
        roundManager = RoundManager.Instance != null
            ? RoundManager.Instance
            : FindAnyObjectByType<RoundManager>();
        objectiveManager = ObjectiveManager.Instance != null
            ? ObjectiveManager.Instance
            : FindAnyObjectByType<ObjectiveManager>();
        if (objectiveManager != null)
        {
            alertA.site = objectiveManager.siteA;
            alertB.site = objectiveManager.siteB;
            alertC.site = objectiveManager.siteC;
        }
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Preparation)
        {
            alertA.state = DefenderSiteAlertState.None;
            alertB.state = DefenderSiteAlertState.None;
            alertC.state = DefenderSiteAlertState.None;
            alertA.sightings.Clear();
            alertB.sightings.Clear();
            alertC.sightings.Clear();
            designatedDefuser = null;
            defusePositionOwner = null;
            retakeLogged = false;
            forcedDefuseLogged = false;
            noLivingDefenderLogged = false;
            defuseMoveLogged.Clear();
            defuseRangeLogged.Clear();
            bombKnowledgeState = BombKnowledgeState.NotPlanted;
            knownPlantedSite = null;
            bombPositionKnown = false;
            knownBombPosition = default;
            checkedBombSites.Clear();
            bombSearchAssignments.Clear();
            holdCoverAssignments.Clear();
            postPlantOrders.Clear();
            alertA.suspicionScore = 0f;
            alertB.suspicionScore = 0f;
            alertC.suspicionScore = 0f;
            rotationLogged.Clear();
            ClearEncirclementPlan();
            RefreshDefenderTeam(true);
        }
        else if (state == RoundState.BombPlanted)
        {
            UpdateBombKnowledge();
        }
    }

    private void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (!drawDebugGizmos)
        {
            return;
        }

        DrawAlertGizmo(alertA);
        DrawAlertGizmo(alertB);
        DrawAlertGizmo(alertC);
        DrawSuspicionGizmo(alertA);
        DrawSuspicionGizmo(alertB);
        DrawSuspicionGizmo(alertC);
        if (PlantedSiteKnown)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(
                knownPlantedSite.PlantPosition + Vector3.up * 0.5f,
                Vector3.one);
        }

        Gizmos.color = Color.cyan;
        foreach (KeyValuePair<GameObject, BombSite> assignment in bombSearchAssignments)
        {
            if (assignment.Key != null && assignment.Value != null)
            {
                Gizmos.DrawLine(
                    assignment.Key.transform.position + Vector3.up * 0.25f,
                    GetSiteSearchPoint(
                        assignment.Value,
                        assignment.Key.transform.position) + Vector3.up * 0.25f);
            }
        }

        Gizmos.color = Color.green;
        foreach (VisionDebugLine line in visionDebugLines)
        {
            Gizmos.DrawLine(
                line.origin + Vector3.up * 0.8f,
                line.target + Vector3.up * 0.8f);
        }

        foreach (KeyValuePair<GameObject, Vector3> flank in
                 encirclement.destinations)
        {
            if (flank.Key == null)
            {
                continue;
            }

            DefenderEngagementRole role = GetEngagementRole(flank.Key);
            Gizmos.color = role switch
            {
                DefenderEngagementRole.LeftFlank => Color.cyan,
                DefenderEngagementRole.RightFlank => Color.blue,
                DefenderEngagementRole.RearCutoff => Color.magenta,
                _ => Color.white
            };
            Gizmos.DrawLine(
                flank.Key.transform.position + Vector3.up * 0.3f,
                flank.Value + Vector3.up * 0.3f);
            Gizmos.DrawWireSphere(flank.Value + Vector3.up * 0.2f, 0.4f);
        }
    }

    private static void DrawAlertGizmo(SiteAlert alert)
    {
        if (alert == null || alert.site == null)
        {
            return;
        }

        Gizmos.color = alert.state switch
        {
            DefenderSiteAlertState.None => new Color(0.3f, 0.3f, 0.3f, 0.4f),
            DefenderSiteAlertState.Suspicious => Color.yellow,
            DefenderSiteAlertState.UnderAttack => Color.red,
            DefenderSiteAlertState.BombPlanted => new Color(1f, 0f, 1f, 1f),
            _ => Color.white
        };
        Gizmos.DrawWireSphere(alert.site.PlantPosition + Vector3.up * 0.2f, 2.5f);
    }

    private static void DrawSuspicionGizmo(SiteAlert alert)
    {
        if (alert == null || alert.site == null || alert.suspicionScore <= 0f)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.65f, 0f, 0.65f);
        Gizmos.DrawWireSphere(
            alert.site.PlantPosition + Vector3.up * 1.25f,
            0.25f + Mathf.Min(1.5f, alert.suspicionScore * 0.08f));
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
