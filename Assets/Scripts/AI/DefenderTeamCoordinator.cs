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
    Retake,
    SearchBombSite,
    Defuse,
    CoverDefuser
}

public struct DefenderOrder
{
    public DefenderOrderType type;
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
    [SerializeField] private float bombTimerRiskThreshold = 7f;
    [SerializeField] private bool drawDebugGizmos = true;

    [Header("Bomb Search")]
    [SerializeField] private float siteCheckDistance = 1.6f;
    [SerializeField] private float bombVisualConfirmationRange = 24f;
    [SerializeField] private float searchAssignmentSpreadPenalty = 5f;

    [Header("Influence Map")]
    [SerializeField] private float postPlantSweepInterval = 2.25f;
    [SerializeField] private float postPlantMinimumHoldRadius = 2.5f;
    [SerializeField] private float postPlantMaximumHoldRadius = 7f;

    [Header("Pursuit Limits")]
    [SerializeField] private float pursuitMaxDistanceFromSite = 12f;
    [SerializeField] private float pursuitLocalThreatRadius = 9f;

    private RoundManager roundManager;
    private ObjectiveManager objectiveManager;
    private readonly SiteAlert alertA = new SiteAlert();
    private readonly SiteAlert alertB = new SiteAlert();
    private readonly Dictionary<GameObject, DefenderRole> roles =
        new Dictionary<GameObject, DefenderRole>();
    private readonly Dictionary<GameObject, BombSite> homeSites =
        new Dictionary<GameObject, BombSite>();
    private readonly HashSet<GameObject> rotationLogged = new HashSet<GameObject>();
    private readonly HashSet<BombSiteId> checkedBombSites = new HashSet<BombSiteId>();
    private readonly Dictionary<GameObject, BombSite> bombSearchAssignments =
        new Dictionary<GameObject, BombSite>();
    private readonly List<Transform> coverPoints = new List<Transform>();
    private readonly List<VisionDebugLine> visionDebugLines =
        new List<VisionDebugLine>();
    private readonly DefenderInfluenceMap influenceMap = new DefenderInfluenceMap();
    private readonly Dictionary<GameObject, Vector3> postPlantSweepPositions =
        new Dictionary<GameObject, Vector3>();
    private readonly Dictionary<GameObject, float> nextPostPlantSweepTimes =
        new Dictionary<GameObject, float>();
    private AgentStats designatedDefuser;
    private GameObject defusePositionOwner;
    private Vector3 designatedDefusePosition;
    private float confirmedAlertTime = Mathf.NegativeInfinity;
    private float nextTeamRefreshTime;
    private bool retakeLogged;
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
    public BombKnowledgeState BombKnowledge => bombKnowledgeState;
    public bool PlantedSiteKnown => bombKnowledgeState == BombKnowledgeState.PlantedKnownSite &&
                                    knownPlantedSite != null;
    public BombSite KnownPlantedSite => PlantedSiteKnown ? knownPlantedSite : null;
    public bool BombPositionKnown => PlantedSiteKnown && bombPositionKnown;
    public GameObject DesignatedDefuser => designatedDefuser != null &&
                                           IsLivingDefender(designatedDefuser.gameObject)
        ? designatedDefuser.gameObject
        : null;

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
            if (FlatDistance(defender.transform.position, bombPosition) > 7f)
            {
                order.type = DefenderOrderType.Retake;
                order.site = plantedSite;
                order.destination = plantedSite.GetNearestPlantPosition(
                    defender.transform.position);
                order.watchPosition = bombPosition;
                order.speedMultiplier = 1.45f;
                return true;
            }

            bool isDefuser = designatedDefuser != null &&
                             designatedDefuser.gameObject == defender;
            Vector3 defusePosition = bombPosition;
            if (isDefuser)
            {
                if (defusePositionOwner != defender)
                {
                    defusePositionOwner = defender;
                    designatedDefusePosition = objectiveManager.FindBestDefusePosition(
                        bombPosition,
                        defender.transform.position);
                }
                defusePosition = designatedDefusePosition;
            }
            bool hasKnownThreat = TryGetLatestKnownPosition(
                plantedSite,
                out Vector3 known);
            Vector3 retakeWatchPosition = hasKnownThreat
                ? known
                : GetLikelyRetakeEntranceWatch(defender, bombPosition);
            order.type = isDefuser
                ? DefenderOrderType.Defuse
                : DefenderOrderType.CoverDefuser;
            order.site = plantedSite;
            order.destination = isDefuser
                ? defusePosition
                : GetPostPlantSweepPosition(
                    defender,
                    bombPosition,
                    retakeWatchPosition);
            order.watchPosition = retakeWatchPosition;
            order.speedMultiplier = isDefuser ? 1.3f : 1.15f;
            return true;
        }

        BombSite homeSite = GetHomeSite(defender);
        DefenderRole role = GetRole(defender);
        SiteAlert activeAlert = GetStrongestActiveAlert();
        bool bothSitesUnderAttack = alertA.state == DefenderSiteAlertState.UnderAttack &&
                                    alertB.state == DefenderSiteAlertState.UnderAttack;
        if (bothSitesUnderAttack && role != DefenderRole.Rotator &&
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
            assignedSite = objectiveManager != null ? objectiveManager.siteA : null;
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
        SiteAlert attacked = home == alertA.site ? alertB : alertA;
        return attacked.site != null &&
               attacked.state == DefenderSiteAlertState.UnderAttack &&
               !HasRecentContact(homeAlert) &&
               Time.time >= confirmedAlertTime + rotationDelay;
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

        bool timerCritical = roundManager.BombTimeRemaining <= bombTimerRiskThreshold;
        if (timerCritical)
        {
            return true;
        }

        if (IsImmediateThreatToDefender(defender, visibleAttacker))
        {
            return false;
        }

        BombSite plantedSite = objectiveManager.ActiveBomb.PlantedSite;
        // Last-known information guides escorts and cover selection, but cannot
        // deadlock the only defuser forever. A current visible threat above is the
        // only non-critical reason to delay once the defender reaches the bomb.
        return true;
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
            ? Mathf.Max(4f, attackerStats.attackRange * 1.25f)
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

        BuildInfluenceMap(defender, objectivePosition, threatPosition);
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
                Debug.Log("Cover point invalid, choosing another cover");
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
                          Mathf.Clamp01(1f - enemyDistance / 5f) * 5f +
                          influenceMap.Evaluate(hiddenPosition);
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
        postPlantSweepPositions.Clear();
        nextPostPlantSweepTimes.Clear();
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
        SiteAlert[] candidates = { alertA, alertB };
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
        if (alertA.site != null && !checkedBombSites.Contains(BombSiteId.A))
        {
            return alertA.site;
        }

        return alertB.site != null && !checkedBombSites.Contains(BombSiteId.B)
            ? alertB.site
            : null;
    }

    private bool OnlyOneSiteUnchecked()
    {
        int count = 0;
        if (alertA.site != null && !checkedBombSites.Contains(BombSiteId.A))
        {
            count++;
        }
        if (alertB.site != null && !checkedBombSites.Contains(BombSiteId.B))
        {
            count++;
        }
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
        SiteAlert[] candidates = { alertA, alertB };
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
            BombSiteId opposite = alert.site.siteId == BombSiteId.A
                ? BombSiteId.B
                : BombSiteId.A;
            Debug.Log(
                $"Site {alert.site.siteId} under attack, {opposite} defenders rotating");
        }
    }

    private void ExpireSharedKnowledge()
    {
        ExpireAlert(alertA);
        ExpireAlert(alertB);
        alertA.suspicionScore = Mathf.Max(0f, alertA.suspicionScore - 0.1f);
        alertB.suspicionScore = Mathf.Max(0f, alertB.suspicionScore - 0.1f);
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
        for (int i = 0; i < defenders.Count; i++)
        {
            AgentStats defender = defenders[i];
            if (i == 4)
            {
                roles[defender.gameObject] = DefenderRole.Rotator;
                homeSites[defender.gameObject] = FlatDistance(
                    defender.transform.position,
                    alertA.site.PlantPosition) <= FlatDistance(
                    defender.transform.position,
                    alertB.site.PlantPosition)
                    ? alertA.site
                    : alertB.site;
                continue;
            }

            bool siteAAgent = i % 4 < 2;
            homeSites[defender.gameObject] = siteAAgent ? alertA.site : alertB.site;
            roles[defender.gameObject] = i % 2 == 0
                ? DefenderRole.SiteAnchor
                : DefenderRole.Support;
        }
    }

    private void AssignDefuserIfNeeded()
    {
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

        float bestDistance = Mathf.Infinity;
        foreach (AgentStats defender in GetLivingDefenders())
        {
            float distance = FlatDistance(
                defender.transform.position,
                objectiveManager.ActiveBomb.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                designatedDefuser = defender;
            }
        }

        if (designatedDefuser != null)
        {
            roles[designatedDefuser.gameObject] = DefenderRole.Defuser;
        }
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

    private Vector3 GetHoldPosition(
        GameObject defender,
        BombSite site,
        Vector3 watchPosition,
        float fallbackRadius)
    {
        if (site != null && TryFindBestCover(
                defender,
                watchPosition,
                site.PlantPosition,
                coverSearchRadius,
                out DefenderCoverSolution cover))
        {
            return cover.hiddenPosition;
        }

        List<AgentStats> defenders = GetLivingDefenders();
        int index = defenders.FindIndex(item => item.gameObject == defender);
        float angle = 35f + index * 360f / Mathf.Max(1, defenders.Count);
        return site.PlantPosition +
               Quaternion.Euler(0f, angle, 0f) * Vector3.forward * fallbackRadius;
    }

    private Vector3 GetPostPlantSweepPosition(
        GameObject defender,
        Vector3 bombPosition,
        Vector3 threatPosition)
    {
        bool needsNewPosition = !postPlantSweepPositions.TryGetValue(
                                    defender,
                                    out Vector3 position) ||
                                !nextPostPlantSweepTimes.TryGetValue(
                                    defender,
                                    out float refreshTime) ||
                                Time.time >= refreshTime;
        if (!needsNewPosition)
        {
            return position;
        }

        position = GetInfluenceMapPosition(
            defender,
            bombPosition,
            threatPosition,
            postPlantMinimumHoldRadius,
            postPlantMaximumHoldRadius);
        postPlantSweepPositions[defender] = position;
        nextPostPlantSweepTimes[defender] = Time.time + postPlantSweepInterval;
        return position;
    }

    private Vector3 GetInfluenceMapPosition(
        GameObject defender,
        Vector3 objectivePosition,
        Vector3 threatPosition,
        float minimumRadius,
        float maximumRadius)
    {
        BuildInfluenceMap(defender, objectivePosition, threatPosition);
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        Vector3 best = defender.transform.position;
        float bestScore = Mathf.NegativeInfinity;
        int defenderIndex = GetLivingDefenders().FindIndex(
            candidate => candidate.gameObject == defender);

        const int directionCount = 16;
        const int ringCount = 3;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float radius = Mathf.Lerp(
                minimumRadius,
                maximumRadius,
                ring / (float)(ringCount - 1));
            for (int directionIndex = 0; directionIndex < directionCount; directionIndex++)
            {
                float angle = directionIndex * (360f / directionCount) + defenderIndex * 17f;
                Vector3 candidate = objectivePosition +
                    Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                List<Vector3> path = null;
                if (pathfinder != null &&
                    (!pathfinder.IsValidAgentPosition(candidate, 0.5f) ||
                     (path = pathfinder.FindPath(defender.transform.position, candidate)) == null))
                {
                    continue;
                }

                float pathCost = path != null ? path.Count * 0.08f :
                    FlatDistance(defender.transform.position, candidate) * 0.08f;
                float score = influenceMap.Evaluate(candidate) - pathCost;
                if (!IsLineBlocked(candidate, objectivePosition))
                {
                    score += 1.5f;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        return best;
    }

    private void BuildInfluenceMap(
        GameObject defender,
        Vector3 objectivePosition,
        Vector3 threatPosition)
    {
        influenceMap.Clear();
        influenceMap.Add(objectivePosition, 8f, 18f);
        if (FlatDistance(threatPosition, objectivePosition) > 0.1f)
        {
            influenceMap.Add(threatPosition, -10f, 9f);
        }

        foreach (AgentStats teammate in GetLivingDefenders())
        {
            if (teammate.gameObject != defender)
            {
                influenceMap.Add(teammate.transform.position, -4f, 3.25f);
            }
        }
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

    private static int CountCoverOccupants(Transform cover, GameObject requester)
    {
        int count = 0;
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
        if (alertA.state > alertB.state)
        {
            return alertA;
        }

        if (alertB.state > alertA.state)
        {
            return alertB;
        }

        return alertA.lastAttackTime >= alertB.lastAttackTime ? alertA : alertB;
    }

    private SiteAlert GetAlert(BombSite site)
    {
        return site != null && site.siteId == BombSiteId.B ? alertB : alertA;
    }

    private BombSite GetNearestSite(Vector3 position)
    {
        if (alertA.site == null)
        {
            return alertB.site;
        }

        if (alertB.site == null)
        {
            return alertA.site;
        }

        return FlatDistance(position, alertA.site.PlantPosition) <=
               FlatDistance(position, alertB.site.PlantPosition)
            ? alertA.site
            : alertB.site;
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
        }
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Preparation)
        {
            alertA.state = DefenderSiteAlertState.None;
            alertB.state = DefenderSiteAlertState.None;
            alertA.sightings.Clear();
            alertB.sightings.Clear();
            designatedDefuser = null;
            defusePositionOwner = null;
            retakeLogged = false;
            bombKnowledgeState = BombKnowledgeState.NotPlanted;
            knownPlantedSite = null;
            bombPositionKnown = false;
            knownBombPosition = default;
            checkedBombSites.Clear();
            bombSearchAssignments.Clear();
            postPlantSweepPositions.Clear();
            nextPostPlantSweepTimes.Clear();
            alertA.suspicionScore = 0f;
            alertB.suspicionScore = 0f;
            rotationLogged.Clear();
            RefreshDefenderTeam(true);
        }
        else if (state == RoundState.BombPlanted)
        {
            UpdateBombKnowledge();
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        DrawAlertGizmo(alertA);
        DrawAlertGizmo(alertB);
        DrawSuspicionGizmo(alertA);
        DrawSuspicionGizmo(alertB);
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
