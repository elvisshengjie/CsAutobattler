using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts the selected baseline tactic and ordered mid-round commands into live movement,
/// planting, cover, role, and combat decisions for the attacking red team.
/// </summary>
[DisallowMultipleComponent]
public sealed class TeamTacticExecutor : MonoBehaviour
{
    private enum FeintAndRotateState
    {
        Setup,
        FakeAttack,
        WaitForRotation,
        BExecute,
        FakeGroupRotate,
        PostPlant
    }

    private sealed class FeintStagingCandidate
    {
        public Vector3 position;
        public string sourceName;
        public bool authored;
        public bool requireHidden;
        public bool hasCover;
        public bool hidden;
        public bool hasSpace;
        public float score;
        public float groupSpacing = 1.6f;
        public List<Vector3> path;
        public readonly List<Vector3> exposedFrom = new List<Vector3>();
    }

    [Header("Tuning")]
    [SerializeField] private float worldRefreshInterval = 0.2f;
    [SerializeField] private float executeDistance = 7f;
    [SerializeField] private float regroupFormationRadius = 1.8f;
    [SerializeField] private float siteThreatRadius = 9f;
    [SerializeField] private float coverSearchRadius = 18f;
    [SerializeField] private float retreatFormationRadius = 4f;
    [SerializeField] private float siteInteriorMargin = 0.75f;
    [SerializeField] private float feintMinimumPressureDuration = 2f;
    [SerializeField] private float feintStagingMinimumSiteClearance = 2.5f;
    [SerializeField] private float feintStagingMaximumPlantDistance = 17f;
    [SerializeField] private float feintStagingReselectCooldown = 3f;
    [SerializeField] private bool drawFeintStagingGizmos = true;
    [SerializeField] private float splitRouteLateralDistance = 8f;
    [SerializeField] private float splitRouteWaypointTolerance = 1.1f;
    [SerializeField] private float splitGroupSyncDistance = 5.5f;
    [SerializeField] private float splitMaximumSyncWait = 2.5f;
    [SerializeField] private bool drawSplitRouteGizmos = true;

    private TeamTacticManager tacticManager;
    private RoundManager roundManager;
    private ObjectiveManager objectiveManager;
    private BombSite targetSite;
    private BombSite fakeSite;
    private int observedPlanRevision = -1;
    private float nextWorldRefreshTime;
    private bool silentAttackTriggered;
    private GameObject focusFireTarget;

    private FeintAndRotateState feintState = FeintAndRotateState.Setup;
    private readonly List<AgentStats> feintFakeGroup = new List<AgentStats>();
    private readonly List<AgentStats> feintRealGroup = new List<AgentStats>();
    private AgentStats feintBombCarrier;
    private int defendersNearBAtSetup;
    private int defendersNearAAtContact;
    private bool feintBaselineCaptured;
    private float fakeContactTime = -1f;
    private Vector3 feintAApproachDirection = Vector3.forward;
    private Vector3 feintBApproachDirection = Vector3.forward;
    private Vector3 feintBStagingPosition;
    private string feintBStagingName;
    private bool hasFeintBStagingPosition;
    private bool feintBStagingIsHidden;
    private float feintBStagingGroupSpacing = 1.6f;
    private float nextFeintStagingReselectTime;
    private int feintStagingReselectCount;
    private readonly List<Vector3> feintHiddenStagingCandidates = new List<Vector3>();
    private readonly List<Vector3> feintExposedStagingCandidates = new List<Vector3>();
    private readonly List<Vector3> feintSelectedStagingPath = new List<Vector3>();
    private readonly List<Vector3> feintSelectedExposureOrigins = new List<Vector3>();

    private bool hasSplitRoutePlan;
    private Vector3 splitPlannedSitePosition;
    private Vector3 splitRoutePointA;
    private Vector3 splitRoutePointB;
    private Vector3 splitEntryPointA;
    private Vector3 splitEntryPointB;
    private Vector3 splitPlantPosition;
    private float splitSyncReleaseTime;
    private readonly Dictionary<GameObject, bool> splitSecondGroup =
        new Dictionary<GameObject, bool>();
    private readonly HashSet<GameObject> splitRouteReached =
        new HashSet<GameObject>();
    private readonly HashSet<GameObject> splitEntryReached =
        new HashSet<GameObject>();
    private readonly List<Vector3> splitDebugPathA = new List<Vector3>();
    private readonly List<Vector3> splitDebugPathB = new List<Vector3>();

    private readonly List<AgentStats> livingAttackers = new List<AgentStats>();
    private readonly List<AgentStats> livingDefenders = new List<AgentStats>();
    private readonly List<Transform> coverPoints = new List<Transform>();
    private readonly HashSet<HealthSystem> observedAttackerHealth =
        new HashSet<HealthSystem>();
    private Transform attackerSafeZone;
    private BombSite scoredPlantSite;
    private float nextPlantSiteEvaluationTime;
    private int plantSiteEvaluationRevision = -1;

    public BombSite TargetSite => targetSite;
    public bool SilentAttackTriggered => silentAttackTriggered;
    public bool HasActiveMidRoundCommand =>
        tacticManager != null && tacticManager.TryGetCurrentMidRoundTactic(out _);
    public bool ShouldSuppressCombatTargets =>
        tacticManager != null &&
        tacticManager.TryGetCurrentMidRoundTactic(out MidRoundTactic tactic) &&
        tactic == MidRoundTactic.Retreat;

    private void Awake()
    {
        tacticManager = GetComponent<TeamTacticManager>();
    }

    private void Start()
    {
        ResolveReferences();
        CacheCoverPoints();
        if (tacticManager != null)
        {
            tacticManager.RoundTacticsReset += OnRoundTacticsReset;
        }
    }

    private void OnDestroy()
    {
        if (tacticManager != null)
        {
            tacticManager.RoundTacticsReset -= OnRoundTacticsReset;
        }

        foreach (HealthSystem health in observedAttackerHealth)
        {
            if (health != null)
            {
                health.Died -= OnAttackerDied;
            }
        }
    }

    /// <summary>
    /// Mid-round commands replace baseline movement and run in click order.
    /// Initial tactics run only while the command queue is empty.
    /// </summary>
    public bool TryExecuteTacticalObjective(GameObject agent, AgentMotor motor)
    {
        ResolveReferences();
        if (!CanControl(agent, motor))
        {
            return false;
        }

        RefreshPlanAndWorld();

        AgentStats stats = agent.GetComponent<AgentStats>();
        BombCarrier carrier = agent.GetComponent<BombCarrier>();
        TeamTacticRole role = GetRole(agent);

        motor.SpeedMultiplier = GetMovementSpeedMultiplier(agent);

        AdvanceCompletedMidRoundCommands();
        if (tacticManager.TryGetCurrentMidRoundTactic(out MidRoundTactic command))
        {
            return command switch
            {
                MidRoundTactic.Regroup => ExecuteRegroup(agent, motor),
                MidRoundTactic.Plant => ExecutePlant(agent, motor, carrier),
                MidRoundTactic.DefendBomb => ExecuteDefendBomb(agent, motor),
                MidRoundTactic.Retreat => ExecuteRetreat(agent, motor),
                _ => true
            };
        }

        if (targetSite == null) return false;

        if (roundManager.CurrentState == RoundState.BombPlanted)
            return ExecuteDefaultPostPlant(agent, motor);

        bool feintAndRotateSelected = tacticManager.GetSelectedInitialTactic() ==
                                      InitialTeamTactic.FeintAndRotate;
        if (feintAndRotateSelected)
        {
            UpdateFeintAndRotateState();
            return ExecuteFeint(agent, motor, carrier);
        }

        return tacticManager.GetSelectedInitialTactic() switch
        {
            InitialTeamTactic.FastExecute => ExecuteFast(agent, motor, carrier, role),
            InitialTeamTactic.FeintAndRotate => ExecuteFeint(agent, motor, carrier),
            InitialTeamTactic.SplitPush => ExecuteSplit(agent, motor, carrier),
            InitialTeamTactic.SilentInfiltration =>
                ExecuteSilent(agent, motor, carrier, role),
            _ => false
        };
    }

    /// <summary>
    /// Mid-round commands suppress initial-tactic combat restrictions. Retreat also
    /// suppresses target acquisition so every survivor keeps moving to safety.
    /// </summary>
    public GameObject SelectCombatTarget(
        GameObject agent,
        AgentSensors sensors,
        GameObject normallyDetectedTarget)
    {
        ResolveReferences();
        if (!CanControlCombat(agent, sensors) || !tacticManager.HasSelectedInitialTactic)
        {
            return normallyDetectedTarget;
        }

        RefreshPlanAndWorld();
        if (ShouldSuppressCombatTargets)
        {
            return null;
        }
        if (targetSite == null)
        {
            return normallyDetectedTarget;
        }

        GameObject defuser = GetActiveDefuseThreat();
        if (defuser != null)
        {
            // Starting a defuse is an objective event the post-plant team can react
            // to even when its current hold angles do not see the bomb. Shooting
            // still requires line of sight in AgentBrain/WeaponSystem.
            focusFireTarget = defuser;
            return defuser;
        }

        // Any mid-round command suppresses combat rules inherited from
        // the initial tactic (for example Feint target restrictions or Silent fire hold).
        if (HasActiveMidRoundCommand ||
            roundManager.CurrentState == RoundState.BombPlanted)
        {
            return normallyDetectedTarget;
        }

        if (tacticManager.GetSelectedInitialTactic() ==
            InitialTeamTactic.FeintAndRotate)
        {
            UpdateFeintAndRotateState();
            return SelectFeintAndRotateCombatTarget(
                agent,
                sensors);
        }

        if (tacticManager.GetSelectedInitialTactic() == InitialTeamTactic.SilentInfiltration &&
            !silentAttackTriggered)
        {
            if (normallyDetectedTarget == null &&
                FlatDistance(agent.transform.position, targetSite.PlantPosition) > executeDistance)
            {
                return null;
            }

            // Contact or reaching the execute point is the signal for the sudden attack.
            silentAttackTriggered = true;
        }

        return normallyDetectedTarget;
    }

    public TeamTacticRole GetRole(GameObject agent)
    {
        if (agent == null)
        {
            return TeamTacticRole.Support;
        }

        BombCarrier carrier = agent.GetComponent<BombCarrier>();
        if (carrier != null && carrier.HasBomb)
        {
            return TeamTacticRole.BombCarrier;
        }

        RefreshPlanAndWorld();
        int nonCarrierIndex = 0;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker.GetComponent<BombCarrier>()?.HasBomb == true)
            {
                continue;
            }

            if (attacker.gameObject == agent)
            {
                return nonCarrierIndex switch
                {
                    0 => TeamTacticRole.Entry,
                    1 => TeamTacticRole.Trader,
                    2 => TeamTacticRole.Support,
                    _ => TeamTacticRole.Lurker
                };
            }

            nonCarrierIndex++;
        }

        return TeamTacticRole.Support;
    }

    public bool CanContinuePlanting(BombCarrier carrier)
    {
        return objectiveManager == null ||
               objectiveManager.CanContinuePlanting(carrier);
    }

    private bool ExecuteFast(
        GameObject agent,
        AgentMotor motor,
        BombCarrier carrier,
        TeamTacticRole role)
    {
        if (TryStartPlant(carrier, false))
        {
            return true;
        }

        float back = role switch
        {
            TeamTacticRole.Entry => 0.5f,
            TeamTacticRole.Trader => 2f,
            TeamTacticRole.BombCarrier => IsPlantWindowSafe(false) ? 0f : 3.5f,
            _ => 3f
        };
        float side = GetFormationSideOffset(agent, 1.4f);
        return MoveOrHold(agent, motor, GetApproachPosition(targetSite, back, side),
            targetSite.PlantPosition);
    }

    private bool ExecuteFeint(
        GameObject agent,
        AgentMotor motor,
        BombCarrier carrier)
    {
        if (!EnsureFeintAssignments() || fakeSite == null || targetSite == null)
        {
            return false;
        }

        bool fakeMember = IsFakeGroupMember(agent);
        bool realMember = IsRealGroupMember(agent);

        if (feintState == FeintAndRotateState.FakeAttack ||
            feintState == FeintAndRotateState.WaitForRotation)
        {
            if (fakeMember)
            {
                // The fake must look real: all three players enter A aggressively and
                // keep taking fights there until defender movement weakens B.
                motor.SpeedMultiplier = Mathf.Max(motor.SpeedMultiplier, 1.2f);
                float lateral = GetFeintGroupSideOffset(
                    feintFakeGroup,
                    agent,
                    1.25f);
                Vector3 attackPosition = GetSiteApproachPosition(
                    fakeSite,
                    feintAApproachDirection,
                    0.35f,
                    lateral);
                return MoveOrHold(
                    agent,
                    motor,
                    attackPosition,
                    fakeSite.PlantPosition);
            }

            if (realMember)
            {
                // Carrier and escort wait outside B's trigger. Combat selection also
                // suppresses premature shots so this pair stays concealed.
                Vector3 waitingPosition = GetFeintBWaitingPosition(agent);
                return MoveOrHold(
                    agent,
                    motor,
                    waitingPosition,
                    fakeSite.PlantPosition);
            }
        }

        bool executeStarted = feintState == FeintAndRotateState.BExecute ||
                              feintState == FeintAndRotateState.FakeGroupRotate;
        if (executeStarted && realMember)
        {
            motor.SpeedMultiplier = Mathf.Max(motor.SpeedMultiplier, 1.25f);
            if (TryStartPlant(carrier, false))
            {
                return true;
            }

            float back = carrier != null && carrier.HasBomb ? 0f : 1.4f;
            float lateral = GetFeintGroupSideOffset(feintRealGroup, agent, 1.5f);
            Vector3 executePosition = GetSiteApproachPosition(
                targetSite,
                feintBApproachDirection,
                back,
                lateral);
            return MoveOrHold(
                agent,
                motor,
                executePosition,
                targetSite.PlantPosition);
        }

        if (executeStarted && fakeMember)
        {
            if (feintState == FeintAndRotateState.BExecute)
            {
                feintState = FeintAndRotateState.FakeGroupRotate;
                Debug.Log(
                    "Feint and Rotate: fake A group is disengaging and rotating back to B: " +
                    FormatAgentNames(feintFakeGroup));
            }

            motor.SpeedMultiplier = Mathf.Max(motor.SpeedMultiplier, 1.1f);
            float lateral = GetFeintGroupSideOffset(feintFakeGroup, agent, 1.35f);
            Vector3 rotationDestination = GetSiteApproachPosition(
                targetSite,
                feintBApproachDirection,
                2.25f,
                lateral);
            return MoveOrHold(
                agent,
                motor,
                rotationDestination,
                targetSite.PlantPosition);
        }

        // A reduced team can leave an unassigned survivor. Once the execute begins,
        // that survivor joins B instead of lingering between the sites.
        if (executeStarted)
        {
            return MoveOrHold(
                agent,
                motor,
                GetSiteApproachPosition(
                    targetSite,
                    feintBApproachDirection,
                    2f,
                    GetFormationSideOffset(agent, 1.2f)),
                targetSite.PlantPosition);
        }

        return false;
    }

    private bool ExecuteSplit(GameObject agent, AgentMotor motor, BombCarrier carrier)
    {
        if (TryStartPlant(carrier, false))
        {
            return true;
        }

        if (!EnsureSplitRoutePlan() ||
            !splitSecondGroup.TryGetValue(agent, out bool sideGroup))
        {
            return ExecuteSplitFallback(agent, motor, carrier);
        }

        Vector3 routePoint = sideGroup ? splitRoutePointB : splitRoutePointA;
        Vector3 entryPoint = sideGroup ? splitEntryPointB : splitEntryPointA;
        if (!splitRouteReached.Contains(agent))
        {
            bool reachedRoute =
                FlatDistance(agent.transform.position, routePoint) <=
                splitRouteWaypointTolerance ||
                motor.HasReachedRequestedDestination(
                    routePoint,
                    splitRouteWaypointTolerance);
            if (!reachedRoute)
            {
                return MoveOrHold(agent, motor, routePoint, entryPoint);
            }

            splitRouteReached.Add(agent);
            Debug.Log(
                $"Split Push: {agent.name} completed " +
                $"{(sideGroup ? "Route B" : "Route A")} waypoint.");
        }

        if (!splitEntryReached.Contains(agent))
        {
            bool reachedEntry =
                FlatDistance(agent.transform.position, entryPoint) <=
                splitRouteWaypointTolerance ||
                motor.HasReachedRequestedDestination(
                    entryPoint,
                    splitRouteWaypointTolerance);
            if (!reachedEntry)
            {
                return MoveOrHold(
                    agent,
                    motor,
                    entryPoint,
                    targetSite.PlantPosition);
            }

            splitEntryReached.Add(agent);
            if (splitSyncReleaseTime <= 0f)
            {
                splitSyncReleaseTime = Time.time + splitMaximumSyncWait;
            }
        }

        if (Time.time < splitSyncReleaseTime &&
            GetOtherSplitGroupEntryDistance(sideGroup) > splitGroupSyncDistance)
        {
            return MoveOrHold(
                agent,
                motor,
                entryPoint,
                targetSite.PlantPosition);
        }

        Vector3 approach = GetApproachDirection(targetSite);
        Vector3 perpendicular = Vector3.Cross(Vector3.up, approach).normalized;
        Vector3 destination;
        if (carrier != null && carrier.HasBomb)
        {
            destination = splitPlantPosition;
        }
        else
        {
            float groupSide = sideGroup ? 2.4f : -2.4f;
            float memberOffset = GetSplitGroupMemberOffset(
                agent,
                sideGroup,
                1.1f);
            destination = targetSite.PlantPosition - approach * 0.5f +
                          perpendicular * (groupSide + memberOffset);
        }

        return MoveOrHold(agent, motor, destination, targetSite.PlantPosition);
    }

    private bool ExecuteSplitFallback(
        GameObject agent,
        AgentMotor motor,
        BombCarrier carrier)
    {
        Vector3 destination = carrier != null && carrier.HasBomb
            ? targetSite.GetNearestPlantPosition(agent.transform.position)
            : GetApproachPosition(
                targetSite,
                1.5f,
                GetFormationSideOffset(agent, 1.4f));
        return MoveOrHold(agent, motor, destination, targetSite.PlantPosition);
    }

    private bool ExecuteSilent(
        GameObject agent,
        AgentMotor motor,
        BombCarrier carrier,
        TeamTacticRole role)
    {
        if (FlatDistance(agent.transform.position, targetSite.PlantPosition) <= executeDistance)
        {
            silentAttackTriggered = true;
        }

        if (silentAttackTriggered)
        {
            motor.SpeedMultiplier = 1.2f;
            return ExecuteFast(agent, motor, carrier, role);
        }

        Transform cover = FindAssignedCover(agent, targetSite.PlantPosition, 4f, coverSearchRadius);
        Vector3 destination = cover != null
            ? GetPositionBesideCover(cover, targetSite.PlantPosition)
            : GetApproachPosition(
                targetSite,
                role == TeamTacticRole.BombCarrier ? 6f : 4f,
                GetFormationSideOffset(agent, 2.2f));
        return MoveOrHold(agent, motor, destination, targetSite.PlantPosition);
    }

    private bool ExecutePlant(
        GameObject agent,
        AgentMotor motor,
        BombCarrier carrier)
    {
        AgentStats bombCarrier = GetBombCarrier();
        if (bombCarrier == null)
        {
            // Keep ownership of the squad while ObjectiveManager recovers a dropped bomb.
            motor.Stop();
            return true;
        }

        SelectBestPlantSite(bombCarrier);
        if (targetSite == null)
        {
            motor.Stop();
            return true;
        }

        if (bombCarrier.gameObject == agent)
        {
            motor.SpeedMultiplier = Mathf.Max(motor.SpeedMultiplier, 1.25f);
            if (TryStartPlant(carrier, false))
            {
                return true;
            }

            Vector3 plantPosition = targetSite.GetNearestPlantPosition(
                agent.transform.position);
            return MoveOrHold(
                agent,
                motor,
                plantPosition,
                GetDefenderCenter(),
                false);
        }

        Vector3 carrierPosition = bombCarrier.transform.position;
        Transform supportCover = FindAssignedCover(
            agent,
            carrierPosition,
            1.5f,
            objectiveManager != null ? objectiveManager.plantSupportRadius : 8f);
        Vector3 destination;
        if (supportCover != null)
        {
            destination = GetPositionBesideCover(supportCover, GetDefenderCenter());
        }
        else
        {
            int index = GetNonCarrierIndex(agent);
            int escortCount = Mathf.Max(1, livingAttackers.Count - 1);
            float angle = index * (360f / escortCount);
            destination = carrierPosition +
                          Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 3f;
        }

        return MoveOrHold(
            agent,
            motor,
            destination,
            GetDefenderCenter(),
            false);
    }

    private bool ExecuteRegroup(GameObject agent, AgentMotor motor)
    {
        AgentStats bombCarrier = GetBombCarrier();
        Vector3 regroupCenter;
        if (bombCarrier != null)
        {
            regroupCenter = bombCarrier.transform.position;
        }
        else if (objectiveManager != null && objectiveManager.ActiveBomb != null)
        {
            regroupCenter = objectiveManager.ActiveBomb.transform.position;
        }
        else
        {
            return false;
        }

        if (bombCarrier != null && bombCarrier.gameObject == agent)
        {
            motor.Stop();
            motor.FacePosition(GetDefenderCenter());
            return true;
        }

        int index = GetNonCarrierIndex(agent);
        int followerCount = Mathf.Max(1, livingAttackers.Count - (bombCarrier != null ? 1 : 0));
        float angle = index * (360f / followerCount);
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) *
                         Vector3.forward * regroupFormationRadius;
        return MoveOrHold(
            agent,
            motor,
            regroupCenter + offset,
            GetDefenderCenter(),
            false);
    }

    private void AdvanceCompletedMidRoundCommands()
    {
        while (tacticManager.TryGetCurrentMidRoundTactic(out MidRoundTactic command))
        {
            bool complete = command switch
            {
                MidRoundTactic.Regroup => IsRegroupComplete(),
                MidRoundTactic.Plant =>
                    roundManager.CurrentState == RoundState.BombPlanted,
                MidRoundTactic.DefendBomb =>
                    tacticManager.QueuedMidRoundTacticCount > 1 &&
                    roundManager.CurrentState == RoundState.BombPlanted &&
                    IsBombDefenseFormationComplete(),
                MidRoundTactic.Retreat =>
                    tacticManager.QueuedMidRoundTacticCount > 1 &&
                    IsRetreatComplete(),
                _ => false
            };
            if (!complete) return;
            tacticManager.CompleteCurrentMidRoundTactic(command);
        }
    }

    private void SelectBestPlantSite(AgentStats bombCarrier)
    {
        if (objectiveManager == null || bombCarrier == null) return;
        if (scoredPlantSite != null &&
            plantSiteEvaluationRevision == tacticManager.PlanRevision &&
            Time.time < nextPlantSiteEvaluationTime)
        {
            targetSite = scoredPlantSite;
            return;
        }

        PlantSitePreference preference = tacticManager.QueuedPlantSitePreference;
        BombSite bestSite = null;
        float bestScore = float.NegativeInfinity;
        BombSite[] sites = objectiveManager.GetSites();
        foreach (BombSite site in sites)
        {
            if (site == null) continue;
            float score = ScorePlantSite(site, bombCarrier, preference);
            if (site == scoredPlantSite) score += 1.5f;
            if (score <= bestScore) continue;
            bestSite = site;
            bestScore = score;
        }

        if (bestSite == null) return;
        scoredPlantSite = bestSite;
        plantSiteEvaluationRevision = tacticManager.PlanRevision;
        nextPlantSiteEvaluationTime = Time.time + 1.25f;
        targetSite = bestSite;
        fakeSite = GetAlternativeSite(bestSite);
        objectiveManager.SetSelectedAttackSite(targetSite);

        string summary = preference == PlantSitePreference.Auto
            ? $"AUTO -> AI chose {bestSite.siteId}"
            : preference.ToString() == bestSite.siteId.ToString()
                ? $"Suggested {preference} -> AI chose {bestSite.siteId}"
                : $"Suggested {preference} -> AI chose {bestSite.siteId}: safer route";
        tacticManager.SetPlantDecisionSummary(summary);
    }

    private float ScorePlantSite(
        BombSite site,
        AgentStats bombCarrier,
        PlantSitePreference preference)
    {
        Vector3 carrierPosition = bombCarrier.transform.position;
        float directDistance = FlatDistance(carrierPosition, site.PlantPosition);
        float score = -directDistance * 0.45f;

        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder != null)
        {
            List<Vector3> path = pathfinder.FindPath(carrierPosition, site.PlantPosition);
            if (path == null || path.Count == 0)
            {
                score -= 100f;
            }
            else
            {
                score -= GetPathLength(path) * 0.18f;
                score += CalculatePathSafety(path) * 10f;
            }
        }

        int defenders = CountLivingNear(
            livingDefenders,
            site.PlantPosition,
            siteThreatRadius * 1.2f);
        int support = CountLivingNear(
            livingAttackers,
            site.PlantPosition,
            siteThreatRadius * 1.35f);
        score -= defenders * 7f;
        score += support * 2.25f;

        int nearbyCover = 0;
        foreach (Transform cover in coverPoints)
        {
            if (cover != null &&
                FlatDistance(cover.position, site.PlantPosition) <= 9f)
                nearbyCover++;
        }
        score += Mathf.Min(4, nearbyCover) * 0.75f;

        if (roundManager != null && roundManager.RoundTimeRemaining <= 25f)
            score -= directDistance * 0.25f;

        return score + GetPlantPreferenceBonus(preference, site.siteId);
    }

    public static float GetPlantPreferenceBonus(
        PlantSitePreference preference,
        BombSiteId site,
        float bonus = 8f)
    {
        bool matches = preference == PlantSitePreference.A && site == BombSiteId.A ||
                       preference == PlantSitePreference.B && site == BombSiteId.B ||
                       preference == PlantSitePreference.C && site == BombSiteId.C;
        return matches ? Mathf.Max(0f, bonus) : 0f;
    }

    private static float GetPathLength(List<Vector3> path)
    {
        float length = 0f;
        for (int i = 1; i < path.Count; i++)
            length += FlatDistance(path[i - 1], path[i]);
        return length;
    }

    private bool IsRegroupComplete()
    {
        AgentStats carrier = GetBombCarrier();
        Vector3 center;
        if (carrier != null) center = carrier.transform.position;
        else if (objectiveManager != null && objectiveManager.ActiveBomb != null)
            center = objectiveManager.ActiveBomb.transform.position;
        else return false;

        float completionRadius = regroupFormationRadius + 1.25f;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker != null && attacker != carrier &&
                FlatDistance(attacker.transform.position, center) > completionRadius)
                return false;
        }
        return livingAttackers.Count > 0;
    }

    private bool ExecuteDefendBomb(GameObject agent, AgentMotor motor)
    {
        if (roundManager.CurrentState != RoundState.BombPlanted)
        {
            motor.Stop();
            motor.FacePosition(targetSite != null
                ? targetSite.PlantPosition
                : agent.transform.position + agent.transform.forward);
            return true;
        }

        GameObject defuser = GetActiveDefuseThreat();
        if (defuser != null)
        {
            // Abandon the spread hold immediately and collapse on the stationary
            // defuser. This lets hidden post-plant positions reacquire a firing
            // angle before the defuse completes.
            motor.SpeedMultiplier = 1.25f;
            return MoveOrHold(
                agent,
                motor,
                defuser.transform.position,
                defuser.transform.position,
                false);
        }

        Vector3 bombPosition = objectiveManager != null && objectiveManager.ActiveBomb != null
            ? objectiveManager.ActiveBomb.transform.position
            : targetSite.PlantPosition;
        Vector3 destination = GetSiteInteriorHoldPosition(agent, bombPosition);
        return MoveOrHold(agent, motor, destination, GetDefenderCenter(), false);
    }

    private GameObject GetActiveDefuseThreat()
    {
        if (roundManager == null || objectiveManager == null ||
            roundManager.CurrentState != RoundState.BombPlanted)
        {
            return null;
        }

        GameObject defuser = objectiveManager.ActiveDefuser;
        return IsLivingEnemy(defuser) ? defuser : null;
    }

    private bool ExecuteRetreat(GameObject agent, AgentMotor motor)
    {
        Vector3 safeCenter = GetAttackerSafeZone();
        Transform cover = FindAssignedCover(
            agent,
            safeCenter,
            0f,
            Mathf.Max(6f, coverSearchRadius * 0.75f));
        Vector3 destination = cover != null
            ? GetPositionBesideCover(cover, GetDefenderCenter())
            : safeCenter + GetSpreadOffset(agent, retreatFormationRadius);
        motor.SpeedMultiplier = Mathf.Max(motor.SpeedMultiplier, 1.25f);
        return MoveOrHold(agent, motor, destination, GetDefenderCenter(), false);
    }

    private bool IsBombDefenseFormationComplete()
    {
        Vector3 bombPosition = objectiveManager != null && objectiveManager.ActiveBomb != null
            ? objectiveManager.ActiveBomb.transform.position
            : targetSite.PlantPosition;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker == null) continue;
            Vector3 destination = GetSiteInteriorHoldPosition(
                attacker.gameObject,
                bombPosition);
            if (FlatDistance(attacker.transform.position, destination) > 2f)
                return false;
        }
        return livingAttackers.Count > 0;
    }

    private bool IsRetreatComplete()
    {
        Vector3 safeCenter = GetAttackerSafeZone();
        float safeRadius = retreatFormationRadius + 3f;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker != null &&
                FlatDistance(attacker.transform.position, safeCenter) > safeRadius)
                return false;
        }
        return livingAttackers.Count > 0;
    }

    private bool ExecuteDefaultPostPlant(GameObject agent, AgentMotor motor)
    {
        Vector3 bombPosition = objectiveManager != null && objectiveManager.ActiveBomb != null
            ? objectiveManager.ActiveBomb.transform.position
            : targetSite.PlantPosition;

        return MoveOrHold(
            agent,
            motor,
            bombPosition + GetSpreadOffset(agent, 3f),
            GetDefenderCenter(),
            false);
    }

    private bool TryStartPlant(BombCarrier carrier, bool carefulPlant)
    {
        if (carrier == null || !carrier.HasBomb || targetSite == null ||
            !targetSite.Contains(carrier.gameObject))
        {
            return false;
        }

        return objectiveManager.TryStartPriorityPlant(
            carrier.gameObject,
            null,
            carefulPlant);
    }

    private bool IsPlantWindowSafe(bool carefulPlant)
    {
        foreach (AgentStats attacker in livingAttackers)
        {
            BombCarrier carrier = attacker.GetComponent<BombCarrier>();
            if (carrier != null && carrier.HasBomb && objectiveManager != null)
            {
                return objectiveManager.IsPlantWindowSafeEnough(
                    attacker.gameObject,
                    null,
                    carefulPlant);
            }
        }

        int threats = CountLivingNear(livingDefenders, targetSite.PlantPosition, siteThreatRadius);
        int support = CountLivingNear(livingAttackers, targetSite.PlantPosition, siteThreatRadius);
        bool teammatesRemain = livingAttackers.Count > 1;
        bool hasCoveringTeammate = !teammatesRemain || support >= 2;

        if (!hasCoveringTeammate)
        {
            return false;
        }

        if (carefulPlant)
        {
            return threats == 0 || support >= threats + 2;
        }

        bool fast = tacticManager.GetSelectedInitialTactic() == InitialTeamTactic.FastExecute;
        return threats == 0 || (fast && threats <= 1 && support >= 2);
    }

    private AgentStats GetBombCarrier()
    {
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker != null &&
                attacker.GetComponent<BombCarrier>()?.HasBomb == true)
            {
                return attacker;
            }
        }

        return null;
    }

    private int GetNonCarrierIndex(GameObject agent)
    {
        int index = 0;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker.GetComponent<BombCarrier>()?.HasBomb == true)
            {
                continue;
            }

            if (attacker.gameObject == agent)
            {
                return index;
            }

            index++;
        }

        return 0;
    }

    private bool IsFakeGroupMember(GameObject agent)
    {
        return feintFakeGroup.Exists(member =>
            member != null && member.gameObject == agent);
    }

    private bool IsRealGroupMember(GameObject agent)
    {
        return feintRealGroup.Exists(member =>
            member != null && member.gameObject == agent);
    }

    private bool EnsureFeintAssignments()
    {
        if (tacticManager == null ||
            tacticManager.GetSelectedInitialTactic() !=
            InitialTeamTactic.FeintAndRotate ||
            fakeSite == null || targetSite == null)
        {
            return false;
        }

        AgentStats currentCarrier = null;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker != null && attacker.GetComponent<BombCarrier>()?.HasBomb == true)
            {
                currentCarrier = attacker;
                break;
            }
        }

        bool assignmentsStillValid = currentCarrier != null &&
                                     currentCarrier == feintBombCarrier &&
                                     feintFakeGroup.Count == Mathf.Min(
                                         3,
                                         Mathf.Max(0, livingAttackers.Count - 2)) &&
                                     feintRealGroup.Contains(currentCarrier);
        if (assignmentsStillValid)
        {
            return true;
        }

        if (currentCarrier == null)
        {
            // A dropped bomb is recovered by ObjectiveManager. Keep the established
            // split intact until a new carrier picks it up, then rebuild around them.
            return feintFakeGroup.Count > 0 && feintRealGroup.Count > 0;
        }

        feintFakeGroup.Clear();
        feintRealGroup.Clear();
        feintBombCarrier = currentCarrier;
        feintRealGroup.Add(currentCarrier);

        List<AgentStats> availableTeammates = new List<AgentStats>();
        foreach (AgentStats attacker in livingAttackers)
        {
            if (attacker != null && attacker != currentCarrier)
            {
                availableTeammates.Add(attacker);
            }
        }

        int fakeCount = Mathf.Min(3, Mathf.Max(0, livingAttackers.Count - 2));
        for (int i = 0; i < fakeCount && i < availableTeammates.Count; i++)
        {
            feintFakeGroup.Add(availableTeammates[i]);
        }

        foreach (AgentStats teammate in availableTeammates)
        {
            if (!feintFakeGroup.Contains(teammate))
            {
                feintRealGroup.Add(teammate);
                break;
            }
        }

        Vector3 attackerCenter = GetCenter(
            livingAttackers,
            (fakeSite.PlantPosition + targetSite.PlantPosition) * 0.5f);
        feintAApproachDirection = GetFlatDirection(
            attackerCenter,
            fakeSite.PlantPosition);
        feintBApproachDirection = GetFlatDirection(
            attackerCenter,
            targetSite.PlantPosition);

        if (!hasFeintBStagingPosition)
        {
            SelectFeintBStagingPosition(false);
        }

        if (!feintBaselineCaptured)
        {
            defendersNearBAtSetup = CountLivingNear(
                livingDefenders,
                targetSite.PlantPosition,
                siteThreatRadius * 1.4f);
            feintBaselineCaptured = true;
        }

        Debug.Log(
            "Feint and Rotate assignments: fake A group (3): " +
            FormatAgentNames(feintFakeGroup));
        Debug.Log(
            "Feint and Rotate assignments: real B group (carrier + escort): " +
            FormatAgentNames(feintRealGroup));

        if (feintFakeGroup.Count < 3 || feintRealGroup.Count < 2)
        {
            Debug.LogWarning(
                "Feint and Rotate started with fewer than five living attackers; " +
                $"assigned {feintFakeGroup.Count} to fake A and " +
                $"{feintRealGroup.Count} to real B.");
        }

        if (feintState == FeintAndRotateState.Setup)
        {
            feintState = FeintAndRotateState.FakeAttack;
            Debug.Log(
                "Feint and Rotate: fake attack starts at A; carrier and escort hold outside B. " +
                $"Initial defenders near B: {defendersNearBAtSetup}.");
        }

        return true;
    }

    private void UpdateFeintAndRotateState()
    {
        if (!EnsureFeintAssignments() ||
            feintState == FeintAndRotateState.PostPlant ||
            feintState == FeintAndRotateState.BExecute ||
            feintState == FeintAndRotateState.FakeGroupRotate)
        {
            return;
        }

        if ((feintState == FeintAndRotateState.FakeAttack ||
             feintState == FeintAndRotateState.WaitForRotation) &&
            Time.time >= nextFeintStagingReselectTime &&
            IsRealGroupDetectedNearB())
        {
            nextFeintStagingReselectTime = Time.time + feintStagingReselectCooldown;
            if (feintStagingReselectCount < 2)
            {
                feintStagingReselectCount++;
                Debug.LogWarning(
                    $"Feint and Rotate: real B group was detected early near " +
                    $"'{feintBStagingName}'; " +
                    "searching for a safer staging position.");
                SelectFeintBStagingPosition(true);
            }
        }

        if (feintState == FeintAndRotateState.FakeAttack && HasFakeGroupMadeContact())
        {
            defendersNearAAtContact = CountLivingNear(
                livingDefenders,
                fakeSite.PlantPosition,
                siteThreatRadius * 1.4f);
            fakeContactTime = Time.time;
            feintState = FeintAndRotateState.WaitForRotation;
            Debug.Log(
                "Feint and Rotate: A contact detected; fake group is actively fighting. " +
                $"Monitoring B from baseline {defendersNearBAtSetup} defenders.");
        }

        if (feintState != FeintAndRotateState.WaitForRotation)
        {
            return;
        }

        if (Time.time - fakeContactTime < feintMinimumPressureDuration)
        {
            return;
        }

        int defendersNearB = CountLivingNear(
            livingDefenders,
            targetSite.PlantPosition,
            siteThreatRadius * 1.4f);
        int defendersNearA = CountLivingNear(
            livingDefenders,
            fakeSite.PlantPosition,
            siteThreatRadius * 1.4f);

        bool bDefenderCountDropped = defendersNearB < defendersNearBAtSetup;
        bool bIsLow = defendersNearB <= 1;
        bool defendersReinforcedA = defendersNearA > defendersNearAAtContact;
        if (!bDefenderCountDropped && !bIsLow && !defendersReinforcedA)
        {
            return;
        }

        string reason = bDefenderCountDropped
            ? $"B defenders dropped from {defendersNearBAtSetup} to {defendersNearB}"
            : defendersReinforcedA
                ? $"A defenders increased from {defendersNearAAtContact} to {defendersNearA}"
                : $"only {defendersNearB} defender remains near B";
        Debug.Log("Feint and Rotate: B is considered weak: " + reason + ".");

        feintState = FeintAndRotateState.BExecute;
        Debug.Log(
            "Feint and Rotate: real B execute starts now. Bomb carrier enters B with escort: " +
            FormatAgentNames(feintRealGroup));
    }

    private bool HasFakeGroupMadeContact()
    {
        foreach (AgentStats attacker in feintFakeGroup)
        {
            if (attacker == null)
            {
                continue;
            }

            AgentBrain brain = attacker.GetComponent<AgentBrain>();
            GameObject target = brain != null ? brain.CurrentTarget : null;
            if (target == null)
            {
                continue;
            }

            AgentStats stats = attacker.GetComponent<AgentStats>();
            WeaponLoadout loadout = stats != null
                ? WeaponLoadout.Get(stats.gameObject) : null;
            float fightingRange = loadout != null ? loadout.MaximumRange + 0.75f : 3.75f;
            bool attackerNearA = FlatDistance(
                                     attacker.transform.position,
                                     fakeSite.PlantPosition) <= siteThreatRadius * 1.5f;
            bool targetNearA = FlatDistance(
                                   target.transform.position,
                                   fakeSite.PlantPosition) <= siteThreatRadius * 1.5f;
            bool closeEnoughToFight = FlatDistance(
                                          attacker.transform.position,
                                          target.transform.position) <= fightingRange;
            if (attackerNearA && targetNearA && closeEnoughToFight)
            {
                return true;
            }
        }

        return false;
    }

    private GameObject SelectFeintAndRotateCombatTarget(
        GameObject agent,
        AgentSensors sensors)
    {
        if (!EnsureFeintAssignments())
        {
            return null;
        }

        bool fakeMember = IsFakeGroupMember(agent);
        bool executeStarted = feintState == FeintAndRotateState.BExecute ||
                              feintState == FeintAndRotateState.FakeGroupRotate ||
                              feintState == FeintAndRotateState.PostPlant;

        if (!executeStarted)
        {
            if (!fakeMember)
            {
                // The hidden B pair does not reveal itself before A contact pulls
                // defenders away. It only fires in direct self-defence.
                HealthSystem health = agent.GetComponent<HealthSystem>();
                GameObject attacker = health != null ? health.LastAttacker : null;
                if (IsLivingEnemy(attacker) && sensors.CanDetect(attacker))
                {
                    return attacker;
                }

                return null;
            }

            // Fake players take only A-side fights, preventing unrelated enemies on
            // their route from turning the strong fake into a weak roadside skirmish.
            return FindClosestDetectedDefenderNear(
                agent,
                sensors,
                fakeSite.PlantPosition,
                siteThreatRadius * 1.6f);
        }

        if (fakeMember && feintState != FeintAndRotateState.PostPlant &&
            FlatDistance(agent.transform.position, targetSite.PlantPosition) >
            siteThreatRadius)
        {
            // Ignore A-side targets during the disengage so surviving fake players
            // actually rotate instead of being pinned in an endless local fight.
            return null;
        }

        // During the B execute, engage only defenders who can immediately contest B.
        return FindClosestDetectedDefenderNear(
            agent,
            sensors,
            targetSite.PlantPosition,
            siteThreatRadius * 1.5f);
    }

    private GameObject FindClosestDetectedDefenderNear(
        GameObject agent,
        AgentSensors sensors,
        Vector3 sitePosition,
        float siteRadius)
    {
        GameObject closest = null;
        float closestDistance = Mathf.Infinity;
        foreach (AgentStats defender in livingDefenders)
        {
            if (defender == null ||
                FlatDistance(defender.transform.position, sitePosition) > siteRadius ||
                !sensors.CanDetect(defender.gameObject))
            {
                continue;
            }

            float distance = FlatDistance(
                agent.transform.position,
                defender.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = defender.gameObject;
            }
        }

        return closest;
    }

    private float GetMovementSpeedMultiplier(GameObject agent)
    {
        if (tacticManager.TryGetCurrentMidRoundTactic(out MidRoundTactic command))
        {
            return command switch
            {
                MidRoundTactic.Regroup => 1.15f,
                MidRoundTactic.Plant when
                    agent != null && agent.GetComponent<BombCarrier>()?.HasBomb == true => 1.25f,
                MidRoundTactic.Plant => 1.05f,
                MidRoundTactic.Retreat => 1.25f,
                _ => 1f
            };
        }

        return tacticManager.GetSelectedInitialTactic() switch
        {
            InitialTeamTactic.FastExecute => 1.25f,
            InitialTeamTactic.SilentInfiltration when !silentAttackTriggered => 0.68f,
            _ => 1f
        };
    }

    private Vector3 GetFeintBWaitingPosition(GameObject agent)
    {
        if (hasFeintBStagingPosition)
        {
            Vector3 directionToSite = GetFlatDirection(
                feintBStagingPosition,
                targetSite.PlantPosition);
            Vector3 stagingPerpendicular = Vector3.Cross(
                Vector3.up,
                directionToSite).normalized;
            float groupOffset = GetFeintGroupSideOffset(
                feintRealGroup,
                agent,
                feintBStagingGroupSpacing);
            return feintBStagingPosition + stagingPerpendicular * groupOffset;
        }

        // Last-resort fallback for maps with no walkable hidden candidate. The scored
        // selector normally replaces this simple geometric point.
        Vector3 direction = feintBApproachDirection;
        Vector3 perpendicular = Vector3.Cross(Vector3.up, direction).normalized;
        float lateral = GetFeintGroupSideOffset(feintRealGroup, agent, 1.8f);

        BoxCollider siteTrigger = targetSite.GetComponent<BoxCollider>();
        if (siteTrigger == null)
        {
            return targetSite.PlantPosition - direction * 7f +
                   perpendicular * lateral;
        }

        Bounds bounds = siteTrigger.bounds;
        float projectedSiteExtent = Mathf.Abs(direction.x) * bounds.extents.x +
                                    Mathf.Abs(direction.z) * bounds.extents.z;
        Vector3 waitingPosition = bounds.center -
                                  direction * (projectedSiteExtent + 2.75f) +
                                  perpendicular * lateral;
        waitingPosition.y = targetSite.PlantPosition.y;
        return waitingPosition;
    }

    private bool SelectFeintBStagingPosition(bool excludeCurrentPosition)
    {
        if (targetSite == null)
        {
            return false;
        }

        List<FeintStagingCandidate> rawCandidates =
            new List<FeintStagingCandidate>();
        AddAuthoredFeintStagingCandidates(rawCandidates);
        AddCoverAndWallStagingCandidates(rawCandidates);
        AddRouteAndRadialStagingCandidates(rawCandidates);

        Vector3 groupStart = GetCenter(feintRealGroup, GetAttackerCenter());
        FeintStagingCandidate bestHidden = null;
        FeintStagingCandidate bestAvailable = null;
        feintHiddenStagingCandidates.Clear();
        feintExposedStagingCandidates.Clear();

        foreach (FeintStagingCandidate rawCandidate in rawCandidates)
        {
            FeintStagingCandidate candidate = EvaluateFeintStagingCandidate(
                rawCandidate,
                groupStart);
            if (candidate == null ||
                (excludeCurrentPosition && hasFeintBStagingPosition &&
                 FlatDistance(candidate.position, feintBStagingPosition) < 1.5f))
            {
                continue;
            }

            if (candidate.hidden)
            {
                feintHiddenStagingCandidates.Add(candidate.position);
                if (bestHidden == null || candidate.score > bestHidden.score)
                {
                    bestHidden = candidate;
                }
            }
            else
            {
                feintExposedStagingCandidates.Add(candidate.position);
            }

            if (bestAvailable == null || candidate.score > bestAvailable.score)
            {
                bestAvailable = candidate;
            }
        }

        FeintStagingCandidate selected = bestHidden ?? bestAvailable;
        if (selected == null)
        {
            if (excludeCurrentPosition && hasFeintBStagingPosition)
            {
                Debug.LogWarning(
                    "Feint and Rotate: no safer alternate B staging point was found; " +
                    "the real group will stay behind its current cover.");
                return false;
            }

            Debug.LogWarning(
                "Feint and Rotate: no valid B staging candidate was found; " +
                "using the outside-site geometric fallback.");
            hasFeintBStagingPosition = false;
            return false;
        }

        feintBStagingPosition = selected.position;
        feintBStagingName = selected.sourceName;
        feintBStagingIsHidden = selected.hidden;
        feintBStagingGroupSpacing = selected.groupSpacing;
        hasFeintBStagingPosition = true;
        feintSelectedStagingPath.Clear();
        if (selected.path != null)
        {
            feintSelectedStagingPath.AddRange(selected.path);
        }

        feintSelectedExposureOrigins.Clear();
        feintSelectedExposureOrigins.AddRange(selected.exposedFrom);

        string visibility = selected.hidden ? "hidden" : "EXPOSED fallback";
        Debug.Log(
            $"Feint and Rotate: selected B staging point '{selected.sourceName}' at " +
            $"{selected.position} ({visibility}, score {selected.score:0.0}).");
        if (!selected.hidden)
        {
            Debug.LogWarning(
                "Feint and Rotate: no fully hidden walkable B staging point exists; " +
                "using the least-exposed valid candidate.");
        }

        return true;
    }

    private void AddAuthoredFeintStagingCandidates(
        List<FeintStagingCandidate> candidates)
    {
        TacticStagingPoint[] stagingPoints =
            FindObjectsByType<TacticStagingPoint>(FindObjectsInactive.Exclude);
        foreach (TacticStagingPoint point in stagingPoints)
        {
            if (point == null || fakeSite == null || point.site != fakeSite.siteId ||
                point.purpose != TacticStagingPurpose.FeintRealGroup)
            {
                continue;
            }

            AddFeintStagingCandidate(
                candidates,
                point.transform.position,
                point.name,
                true,
                true,
                Mathf.Max(1.2f, point.spaceForAgents),
                point.mustBeHiddenFromDefenders);
        }
    }

    private void AddCoverAndWallStagingCandidates(
        List<FeintStagingCandidate> candidates)
    {
        Vector3 threatCenter = GetBThreatCenter();
        foreach (Transform cover in coverPoints)
        {
            if (cover == null || FlatDistance(
                    cover.position,
                    targetSite.PlantPosition) > feintStagingMaximumPlantDistance + 5f)
            {
                continue;
            }

            AddFeintStagingCandidate(
                candidates,
                GetPointBehindObstacle(cover, threatCenter),
                cover.name + " (defender side)",
                false,
                true);
            AddFeintStagingCandidate(
                candidates,
                GetPointBehindObstacle(cover, targetSite.PlantPosition),
                cover.name + " (site side)",
                false,
                true);
        }

        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
        foreach (Collider obstacle in colliders)
        {
            if (obstacle == null || obstacle.isTrigger ||
                obstacle.GetComponentInParent<AgentStats>() != null ||
                (!NameContains(obstacle.name, "Wall") &&
                 !NameContains(obstacle.name, "Obstacle")))
            {
                continue;
            }

            if (FlatDistance(obstacle.bounds.center, targetSite.PlantPosition) >
                feintStagingMaximumPlantDistance + 5f)
            {
                continue;
            }

            AddFeintStagingCandidate(
                candidates,
                GetPointBehindObstacle(obstacle.transform, threatCenter),
                obstacle.name + " (wall cover)",
                false,
                true);
            AddFeintStagingCandidate(
                candidates,
                GetPointBehindObstacle(obstacle.transform, targetSite.PlantPosition),
                obstacle.name + " (wall/site cover)",
                false,
                true);
        }
    }

    private void AddRouteAndRadialStagingCandidates(
        List<FeintStagingCandidate> candidates)
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
        foreach (Transform candidateTransform in transforms)
        {
            if (candidateTransform == null ||
                (!NameContains(candidateTransform.name, "RoutePoint") &&
                 !NameContains(candidateTransform.name, "ChokePoint")))
            {
                continue;
            }

            AddFeintStagingCandidate(
                candidates,
                candidateTransform.position,
                candidateTransform.name,
                false,
                false);
        }

        BoxCollider siteTrigger = targetSite.GetComponent<BoxCollider>();
        Bounds siteBounds = siteTrigger != null
            ? siteTrigger.bounds
            : new Bounds(targetSite.PlantPosition, Vector3.one * 4f);
        for (int ring = 0; ring < 2; ring++)
        {
            float extraClearance = feintStagingMinimumSiteClearance + 1.5f + ring * 3.5f;
            for (int index = 0; index < 16; index++)
            {
                float angle = index * 360f / 16f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float projectedExtent = Mathf.Abs(direction.x) * siteBounds.extents.x +
                                        Mathf.Abs(direction.z) * siteBounds.extents.z;
                Vector3 position = siteBounds.center +
                                   direction * (projectedExtent + extraClearance);
                position.y = targetSite.PlantPosition.y;
                AddFeintStagingCandidate(
                    candidates,
                    position,
                    $"Generated B staging {ring + 1}-{index + 1}",
                    false,
                    false);
            }
        }
    }

    private static void AddFeintStagingCandidate(
        List<FeintStagingCandidate> candidates,
        Vector3 position,
        string sourceName,
        bool authored,
        bool hasCover,
        float groupSpacing = 1.6f,
        bool requireHidden = false)
    {
        foreach (FeintStagingCandidate existing in candidates)
        {
            if (FlatDistance(existing.position, position) < 0.5f)
            {
                return;
            }
        }

        candidates.Add(new FeintStagingCandidate
        {
            position = position,
            sourceName = sourceName,
            authored = authored,
            requireHidden = requireHidden,
            hasCover = hasCover,
            groupSpacing = groupSpacing,
            score = 0f
        });
    }

    private FeintStagingCandidate EvaluateFeintStagingCandidate(
        FeintStagingCandidate candidate,
        Vector3 groupStart)
    {
        candidate.position.y = targetSite.PlantPosition.y;
        float siteClearance = GetDistanceFromSiteBoundsXZ(candidate.position);
        float distanceToPlant = FlatDistance(
            candidate.position,
            targetSite.PlantPosition);
        if (siteClearance < feintStagingMinimumSiteClearance ||
            distanceToPlant > feintStagingMaximumPlantDistance)
        {
            return null;
        }

        List<Vector3> path = AStarPathfinder3D.Instance != null
            ? AStarPathfinder3D.Instance.FindPath(groupStart, candidate.position)
            : null;
        if (AStarPathfinder3D.Instance != null && path == null)
        {
            return null;
        }

        if (path != null && path.Count > 0)
        {
            Vector3 walkableEndpoint = path[path.Count - 1];
            if (FlatDistance(walkableEndpoint, candidate.position) > 2.5f)
            {
                return null;
            }

            walkableEndpoint.y = targetSite.PlantPosition.y;
            candidate.position = walkableEndpoint;
            siteClearance = GetDistanceFromSiteBoundsXZ(candidate.position);
            distanceToPlant = FlatDistance(candidate.position, targetSite.PlantPosition);
            if (siteClearance < feintStagingMinimumSiteClearance ||
                distanceToPlant > feintStagingMaximumPlantDistance)
            {
                return null;
            }
        }

        Vector3 directionToSite = GetFlatDirection(
            candidate.position,
            targetSite.PlantPosition);
        Vector3 perpendicular = Vector3.Cross(Vector3.up, directionToSite).normalized;
        Vector3 groupPositionA = candidate.position -
                                 perpendicular * candidate.groupSpacing * 0.5f;
        Vector3 groupPositionB = candidate.position +
                                 perpendicular * candidate.groupSpacing * 0.5f;
        candidate.hasSpace = IsAgentPositionOpen(groupPositionA) &&
                             IsAgentPositionOpen(groupPositionB) &&
                             GetDistanceFromSiteBoundsXZ(groupPositionA) >=
                             feintStagingMinimumSiteClearance - 0.3f &&
                             GetDistanceFromSiteBoundsXZ(groupPositionB) >=
                             feintStagingMinimumSiteClearance - 0.3f;
        if (!candidate.hasSpace)
        {
            return null;
        }

        int defenderExposure = 0;
        candidate.exposedFrom.Clear();
        foreach (AgentStats defender in livingDefenders)
        {
            if (defender == null)
            {
                continue;
            }

            Vector3 origin = defender.transform.position;
            bool seesGroup = !IsLineBlocked(origin, groupPositionA) ||
                             !IsLineBlocked(origin, groupPositionB);
            if (seesGroup)
            {
                defenderExposure++;
                candidate.exposedFrom.Add(origin);
            }
        }

        int commonExposure = 0;
        foreach (Vector3 observationPoint in GetBObservationPoints())
        {
            bool seesGroup = !IsLineBlocked(observationPoint, groupPositionA) ||
                             !IsLineBlocked(observationPoint, groupPositionB);
            if (seesGroup)
            {
                commonExposure++;
                candidate.exposedFrom.Add(observationPoint);
            }
        }

        candidate.hidden = defenderExposure == 0 && commonExposure == 0;
        if (candidate.requireHidden && !candidate.hidden)
        {
            return null;
        }

        candidate.hasCover |= HasNearbyCover(candidate.position);
        candidate.path = path;

        float enemyThreat = CalculateEnemyThreat(candidate.position);
        float distanceScore = Mathf.Clamp01(
            1f - Mathf.Abs(distanceToPlant - 10f) / 10f);
        float safePathScore = CalculatePathSafety(path);
        float exposedRatio = Mathf.Clamp01(
            defenderExposure / (float)Mathf.Max(1, livingDefenders.Count) +
            commonExposure / 5f);

        candidate.score = (candidate.hasCover ? 1f : 0f) * 4f +
                          (candidate.hidden ? 1f : 0f) * 6f +
                          distanceScore * 3f +
                          safePathScore * 5f -
                          enemyThreat * 5f -
                          exposedRatio * 8f +
                          (candidate.authored ? 2f : 0f);
        return candidate;
    }

    private Vector3 GetPointBehindObstacle(Transform obstacle, Vector3 threatOrigin)
    {
        Collider obstacleCollider = obstacle.GetComponent<Collider>();
        Vector3 center = obstacleCollider != null
            ? obstacleCollider.bounds.center
            : obstacle.position;
        Vector3 awayFromThreat = GetFlatDirection(threatOrigin, center);
        float obstacleExtent = 0.75f;
        if (obstacleCollider != null)
        {
            Bounds bounds = obstacleCollider.bounds;
            obstacleExtent = Mathf.Abs(awayFromThreat.x) * bounds.extents.x +
                             Mathf.Abs(awayFromThreat.z) * bounds.extents.z;
        }

        Vector3 position = center + awayFromThreat * (obstacleExtent + 1.05f);
        position.y = targetSite.PlantPosition.y;
        return position;
    }

    private Vector3 GetBThreatCenter()
    {
        List<AgentStats> nearbyDefenders = new List<AgentStats>();
        foreach (AgentStats defender in livingDefenders)
        {
            if (defender != null && FlatDistance(
                    defender.transform.position,
                    targetSite.PlantPosition) <= siteThreatRadius * 2f)
            {
                nearbyDefenders.Add(defender);
            }
        }

        return GetCenter(nearbyDefenders, targetSite.PlantPosition);
    }

    private List<Vector3> GetBObservationPoints()
    {
        List<Vector3> points = new List<Vector3>();
        BoxCollider siteTrigger = targetSite.GetComponent<BoxCollider>();
        if (siteTrigger == null)
        {
            points.Add(targetSite.PlantPosition);
            return points;
        }

        Bounds bounds = siteTrigger.bounds;
        Vector3 center = bounds.center;
        center.y = targetSite.PlantPosition.y;
        points.Add(center);
        points.Add(new Vector3(bounds.min.x, center.y, center.z));
        points.Add(new Vector3(bounds.max.x, center.y, center.z));
        points.Add(new Vector3(center.x, center.y, bounds.min.z));
        points.Add(new Vector3(center.x, center.y, bounds.max.z));
        return points;
    }

    private float GetDistanceFromSiteBoundsXZ(Vector3 position)
    {
        BoxCollider siteTrigger = targetSite.GetComponent<BoxCollider>();
        if (siteTrigger == null)
        {
            return FlatDistance(position, targetSite.PlantPosition);
        }

        Bounds bounds = siteTrigger.bounds;
        float closestX = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
        float closestZ = Mathf.Clamp(position.z, bounds.min.z, bounds.max.z);
        return Vector2.Distance(
            new Vector2(position.x, position.z),
            new Vector2(closestX, closestZ));
    }

    private bool HasNearbyCover(Vector3 position)
    {
        foreach (Transform cover in coverPoints)
        {
            if (cover != null && FlatDistance(cover.position, position) <= 3f &&
                IsLineBlocked(targetSite.PlantPosition, position))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAgentPositionOpen(Vector3 position)
    {
        Collider[] overlaps = Physics.OverlapSphere(
            position + Vector3.up * 0.55f,
            0.42f,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || overlap.GetComponentInParent<AgentStats>() != null ||
                NameContains(overlap.name, "Floor"))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsLineBlocked(Vector3 originPosition, Vector3 targetPosition)
    {
        Vector3 origin = originPosition + Vector3.up * 0.8f;
        Vector3 target = targetPosition + Vector3.up * 0.8f;
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

    private float CalculateEnemyThreat(Vector3 position)
    {
        float threat = 0f;
        foreach (AgentStats defender in livingDefenders)
        {
            if (defender == null)
            {
                continue;
            }

            float distance = FlatDistance(position, defender.transform.position);
            threat += Mathf.Clamp01(1f - distance / 12f);
        }

        return Mathf.Clamp01(threat);
    }

    private float CalculatePathSafety(List<Vector3> path)
    {
        if (path == null || path.Count == 0)
        {
            return 0.5f;
        }

        int exposedPoints = 0;
        int sampledPoints = 0;
        int sampleStep = Mathf.Max(1, path.Count / 12);
        for (int pointIndex = 0; pointIndex < path.Count; pointIndex += sampleStep)
        {
            Vector3 point = path[pointIndex];
            sampledPoints++;
            bool exposed = false;
            foreach (AgentStats defender in livingDefenders)
            {
                if (defender != null &&
                    !IsLineBlocked(defender.transform.position, point))
                {
                    exposed = true;
                    break;
                }
            }

            if (exposed)
            {
                exposedPoints++;
            }
        }

        return 1f - exposedPoints / (float)Mathf.Max(1, sampledPoints);
    }

    private bool IsRealGroupDetectedNearB()
    {
        foreach (AgentStats realAgent in feintRealGroup)
        {
            if (realAgent == null || FlatDistance(
                    realAgent.transform.position,
                    targetSite.PlantPosition) > feintStagingMaximumPlantDistance + 4f)
            {
                continue;
            }

            foreach (AgentStats defender in livingDefenders)
            {
                if (defender == null)
                {
                    continue;
                }

                AgentSensors sensors = defender.GetComponent<AgentSensors>();
                if (sensors != null && sensors.CanDetect(realAgent.gameObject))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool NameContains(string value, string fragment)
    {
        return value != null && value.IndexOf(
            fragment,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Vector3 GetSiteApproachPosition(
        BombSite site,
        Vector3 approachDirection,
        float back,
        float lateral)
    {
        Vector3 perpendicular = Vector3.Cross(
            Vector3.up,
            approachDirection).normalized;
        return site.PlantPosition - approachDirection * back +
               perpendicular * lateral;
    }

    private static float GetFeintGroupSideOffset(
        List<AgentStats> group,
        GameObject agent,
        float spacing)
    {
        int index = group.FindIndex(member =>
            member != null && member.gameObject == agent);
        if (index < 0)
        {
            return 0f;
        }

        return (index - (group.Count - 1) * 0.5f) * spacing;
    }

    private static Vector3 GetFlatDirection(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.01f
            ? direction.normalized
            : Vector3.forward;
    }

    private static string FormatAgentNames(List<AgentStats> agents)
    {
        List<string> names = new List<string>();
        foreach (AgentStats agent in agents)
        {
            if (agent != null)
            {
                names.Add(agent.name);
            }
        }

        return names.Count > 0 ? string.Join(", ", names) : "(none)";
    }

    private Vector3 GetApproachPosition(BombSite site, float back, float lateral)
    {
        Vector3 direction = GetApproachDirection(site);
        Vector3 perpendicular = Vector3.Cross(Vector3.up, direction).normalized;
        return site.PlantPosition - direction * back + perpendicular * lateral;
    }

    private Vector3 GetApproachDirection(BombSite site)
    {
        Vector3 center = GetAttackerCenter();
        Vector3 direction = site.PlantPosition - center;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.forward;
    }

    private float GetFormationSideOffset(GameObject agent, float spacing)
    {
        int index = GetAttackerIndex(agent);
        float centered = index - (livingAttackers.Count - 1) * 0.5f;
        return centered * spacing;
    }

    private Vector3 GetSpreadOffset(GameObject agent, float radius)
    {
        int index = GetAttackerIndex(agent);
        float angle = 31f + index * (360f / Mathf.Max(1, livingAttackers.Count));
        return Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
    }

    private bool EnsureSplitRoutePlan()
    {
        if (hasSplitRoutePlan && targetSite != null &&
            FlatDistance(splitPlannedSitePosition, targetSite.PlantPosition) < 0.5f &&
            HasAssignmentsForLivingAttackers())
        {
            return true;
        }

        return BuildSplitRoutePlan();
    }

    private bool BuildSplitRoutePlan()
    {
        ClearSplitRoutePlan();
        if (targetSite == null || livingAttackers.Count < 2)
        {
            return false;
        }

        for (int i = 0; i < livingAttackers.Count; i++)
        {
            splitSecondGroup[livingAttackers[i].gameObject] =
                IsSecondSplitGroupIndex(i, livingAttackers.Count);
        }

        Vector3 start = GetAttackerCenter();
        Vector3 target = targetSite.PlantPosition;
        Vector3 approach = GetFlatDirection(start, target);
        Vector3 perpendicular = Vector3.Cross(Vector3.up, approach).normalized;
        float entryLateral = Mathf.Max(3.5f, splitRouteLateralDistance * 0.55f);
        Vector3 requestedEntryA = target - approach * 3.5f -
                                  perpendicular * entryLateral;
        Vector3 requestedEntryB = target - approach * 3.5f +
                                  perpendicular * entryLateral;

        float bestScore = Mathf.NegativeInfinity;
        Vector3 bestRouteA = default;
        Vector3 bestRouteB = default;
        Vector3 bestEntryA = default;
        Vector3 bestEntryB = default;
        List<Vector3> bestPathA = null;
        List<Vector3> bestPathB = null;
        float[] progressCandidates = { 0.38f, 0.52f, 0.66f };
        float[] lateralScales = { 0.75f, 1f, 1.25f };
        foreach (float progress in progressCandidates)
        {
            foreach (float lateralScale in lateralScales)
            {
                float lateral = splitRouteLateralDistance * lateralScale;
                Vector3 requestedA = GetSplitRouteCandidate(
                    start,
                    target,
                    false,
                    lateral,
                    progress);
                Vector3 requestedB = GetSplitRouteCandidate(
                    start,
                    target,
                    true,
                    lateral,
                    progress);
                if (!TryResolveSplitLeg(
                        start,
                        requestedA,
                        out Vector3 routeA,
                        out List<Vector3> firstLegA) ||
                    !TryResolveSplitLeg(
                        start,
                        requestedB,
                        out Vector3 routeB,
                        out List<Vector3> firstLegB) ||
                    FlatDistance(routeA, routeB) < 4f ||
                    !TryResolveSplitLeg(
                        routeA,
                        requestedEntryA,
                        out Vector3 entryA,
                        out List<Vector3> secondLegA) ||
                    !TryResolveSplitLeg(
                        routeB,
                        requestedEntryB,
                        out Vector3 entryB,
                        out List<Vector3> secondLegB) ||
                    FlatDistance(entryA, entryB) < 3.5f)
                {
                    continue;
                }

                float midPathSeparation = FlatDistance(
                    GetSplitPathSample(firstLegA, 0.65f, routeA),
                    GetSplitPathSample(firstLegB, 0.65f, routeB));
                float score = FlatDistance(routeA, routeB) * 2f +
                              FlatDistance(entryA, entryB) +
                              midPathSeparation * 1.5f -
                              (firstLegA.Count + firstLegB.Count +
                               secondLegA.Count + secondLegB.Count) * 0.03f;
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestRouteA = routeA;
                bestRouteB = routeB;
                bestEntryA = entryA;
                bestEntryB = entryB;
                bestPathA = CombineSplitLegs(firstLegA, secondLegA);
                bestPathB = CombineSplitLegs(firstLegB, secondLegB);
            }
        }

        if (bestPathA == null || bestPathB == null)
        {
            Debug.LogWarning(
                "Split Push: could not find two reachable divergent routes; " +
                "using separated geometric staging points.");
            bestRouteA = GetSplitRouteCandidate(
                start,
                target,
                false,
                splitRouteLateralDistance,
                0.55f);
            bestRouteB = GetSplitRouteCandidate(
                start,
                target,
                true,
                splitRouteLateralDistance,
                0.55f);
            bestEntryA = requestedEntryA;
            bestEntryB = requestedEntryB;
            bestPathA = new List<Vector3> { start, bestRouteA, bestEntryA };
            bestPathB = new List<Vector3> { start, bestRouteB, bestEntryB };
        }

        Vector3 firstGroupCenter = GetSplitGroupCenter(false, start);
        Vector3 secondGroupCenter = GetSplitGroupCenter(true, start);
        float normalCost = FlatDistance(firstGroupCenter, bestRouteA) +
                           FlatDistance(secondGroupCenter, bestRouteB);
        float swappedCost = FlatDistance(firstGroupCenter, bestRouteB) +
                            FlatDistance(secondGroupCenter, bestRouteA);
        if (swappedCost < normalCost)
        {
            (bestRouteA, bestRouteB) = (bestRouteB, bestRouteA);
            (bestEntryA, bestEntryB) = (bestEntryB, bestEntryA);
            (bestPathA, bestPathB) = (bestPathB, bestPathA);
        }

        splitRoutePointA = bestRouteA;
        splitRoutePointB = bestRouteB;
        splitEntryPointA = bestEntryA;
        splitEntryPointB = bestEntryB;
        splitPlannedSitePosition = target;
        splitDebugPathA.AddRange(bestPathA);
        splitDebugPathB.AddRange(bestPathB);

        AgentStats bombCarrier = livingAttackers.Find(attacker =>
            attacker.GetComponent<BombCarrier>()?.HasBomb == true);
        splitPlantPosition = bombCarrier != null
            ? targetSite.GetNearestPlantPosition(bombCarrier.transform.position)
            : targetSite.PlantPosition;
        hasSplitRoutePlan = true;
        Debug.Log(
            $"Split Push: two-route plan for Site {targetSite.siteId}. " +
            $"Route A via {splitRoutePointA}, Route B via {splitRoutePointB}.");
        return true;
    }

    private bool TryResolveSplitLeg(
        Vector3 start,
        Vector3 requested,
        out Vector3 resolved,
        out List<Vector3> path)
    {
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder == null)
        {
            resolved = requested;
            path = new List<Vector3> { requested };
            return true;
        }

        return pathfinder.TryGetNearestReachablePosition(
                   start,
                   requested,
                   3f,
                   0.65f,
                   out resolved,
                   out path,
                   null,
                   false) &&
               path != null &&
               FlatDistance(resolved, requested) <= 3.1f;
    }

    private static Vector3 GetSplitPathSample(
        List<Vector3> path,
        float progress,
        Vector3 fallback)
    {
        if (path == null || path.Count == 0)
        {
            return fallback;
        }

        int index = Mathf.Clamp(
            Mathf.RoundToInt((path.Count - 1) * Mathf.Clamp01(progress)),
            0,
            path.Count - 1);
        return path[index];
    }

    private static List<Vector3> CombineSplitLegs(
        List<Vector3> first,
        List<Vector3> second)
    {
        List<Vector3> combined = new List<Vector3>();
        if (first != null)
        {
            combined.AddRange(first);
        }
        if (second != null)
        {
            combined.AddRange(second);
        }
        return combined;
    }

    public static bool IsSecondSplitGroupIndex(int index, int attackerCount)
    {
        return attackerCount > 1 &&
               index >= Mathf.CeilToInt(attackerCount * 0.6f);
    }

    public static Vector3 GetSplitRouteCandidate(
        Vector3 start,
        Vector3 target,
        bool secondGroup,
        float lateralDistance,
        float progress)
    {
        Vector3 direction = GetFlatDirection(start, target);
        Vector3 perpendicular = Vector3.Cross(Vector3.up, direction).normalized;
        float side = secondGroup ? 1f : -1f;
        Vector3 candidate = Vector3.Lerp(
            start,
            target,
            Mathf.Clamp(progress, 0.15f, 0.85f));
        candidate += perpendicular * Mathf.Max(2f, lateralDistance) * side;
        candidate.y = start.y;
        return candidate;
    }

    private bool HasAssignmentsForLivingAttackers()
    {
        foreach (AgentStats attacker in livingAttackers)
        {
            if (!splitSecondGroup.ContainsKey(attacker.gameObject))
            {
                return false;
            }
        }
        return livingAttackers.Count >= 2;
    }

    private Vector3 GetSplitGroupCenter(bool secondGroup, Vector3 fallback)
    {
        Vector3 total = Vector3.zero;
        int count = 0;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (splitSecondGroup.TryGetValue(
                    attacker.gameObject,
                    out bool assignedSecond) &&
                assignedSecond == secondGroup)
            {
                total += attacker.transform.position;
                count++;
            }
        }
        return count > 0 ? total / count : fallback;
    }

    private float GetOtherSplitGroupEntryDistance(bool secondGroup)
    {
        bool otherGroup = !secondGroup;
        Vector3 otherEntry = otherGroup ? splitEntryPointB : splitEntryPointA;
        float total = 0f;
        int count = 0;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (splitSecondGroup.TryGetValue(
                    attacker.gameObject,
                    out bool assignedSecond) &&
                assignedSecond == otherGroup)
            {
                total += FlatDistance(attacker.transform.position, otherEntry);
                count++;
            }
        }
        return count > 0 ? total / count : 0f;
    }

    private float GetSplitGroupMemberOffset(
        GameObject agent,
        bool secondGroup,
        float spacing)
    {
        int memberIndex = 0;
        int memberCount = 0;
        foreach (AgentStats attacker in livingAttackers)
        {
            if (!splitSecondGroup.TryGetValue(
                    attacker.gameObject,
                    out bool assignedSecond) ||
                assignedSecond != secondGroup)
            {
                continue;
            }

            if (attacker.gameObject == agent)
            {
                memberIndex = memberCount;
            }
            memberCount++;
        }
        return (memberIndex - (memberCount - 1) * 0.5f) * spacing;
    }

    private void ClearSplitRoutePlan()
    {
        hasSplitRoutePlan = false;
        splitPlannedSitePosition = Vector3.zero;
        splitRoutePointA = Vector3.zero;
        splitRoutePointB = Vector3.zero;
        splitEntryPointA = Vector3.zero;
        splitEntryPointB = Vector3.zero;
        splitPlantPosition = Vector3.zero;
        splitSyncReleaseTime = 0f;
        splitSecondGroup.Clear();
        splitRouteReached.Clear();
        splitEntryReached.Clear();
        splitDebugPathA.Clear();
        splitDebugPathB.Clear();
    }

    private Transform FindAssignedCover(
        GameObject agent,
        Vector3 center,
        float minimumDistance,
        float maximumDistance)
    {
        List<Transform> candidates = new List<Transform>();
        foreach (Transform cover in coverPoints)
        {
            if (cover == null)
            {
                continue;
            }

            float distance = FlatDistance(cover.position, center);
            if (distance >= minimumDistance && distance <= maximumDistance)
            {
                candidates.Add(cover);
            }
        }

        candidates.Sort((left, right) =>
        {
            float leftScore = FlatDistance(left.position, center) +
                              FlatDistance(left.position, agent.transform.position) * 0.2f;
            float rightScore = FlatDistance(right.position, center) +
                               FlatDistance(right.position, agent.transform.position) * 0.2f;
            return leftScore.CompareTo(rightScore);
        });

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[GetAttackerIndex(agent) % candidates.Count];
    }

    private Transform FindAssignedCoverOutsideSite(
        GameObject agent,
        BombSite site,
        Vector3 desiredCenter,
        float maximumDistance)
    {
        List<Transform> candidates = new List<Transform>();
        foreach (Transform cover in coverPoints)
        {
            if (cover == null ||
                FlatDistance(cover.position, desiredCenter) > maximumDistance)
            {
                continue;
            }

            Vector3 holdPosition = GetPositionBesideCover(
                cover,
                GetDefenderCenter());
            if (!IsPointInsideSite(site, holdPosition, -siteInteriorMargin))
            {
                candidates.Add(cover);
            }
        }

        candidates.Sort((left, right) =>
        {
            float leftScore = FlatDistance(left.position, desiredCenter) +
                              FlatDistance(left.position, agent.transform.position) * 0.15f;
            float rightScore = FlatDistance(right.position, desiredCenter) +
                               FlatDistance(right.position, agent.transform.position) * 0.15f;
            return leftScore.CompareTo(rightScore);
        });

        return candidates.Count > 0
            ? candidates[GetAttackerIndex(agent) % candidates.Count]
            : null;
    }

    private Vector3 GetSiteInteriorHoldPosition(GameObject agent, Vector3 bombPosition)
    {
        BombSite plantedSite = objectiveManager != null && objectiveManager.PlantedSite != null
            ? objectiveManager.PlantedSite
            : targetSite;
        if (plantedSite == null)
        {
            return bombPosition + GetSpreadOffset(agent, 3f);
        }

        BoxCollider siteTrigger = plantedSite.GetComponent<BoxCollider>();
        if (siteTrigger == null)
        {
            return bombPosition + GetSpreadOffset(agent, 3f);
        }

        int agentIndex = GetAttackerIndex(agent);
        Vector3 ringOffset = GetBombDefenseOffset(
            agentIndex,
            livingAttackers.Count,
            4f);
        Vector3 idealPosition = bombPosition + ringOffset;
        Transform assignedCover = FindBombDefenseCover(
            agent,
            plantedSite,
            bombPosition);
        Vector3 destination = assignedCover != null
            ? GetPositionBesideCover(assignedCover, GetDefenderCenter())
            : idealPosition;
        return ClampInsideSiteBounds(
            destination,
            siteTrigger.bounds,
            siteInteriorMargin,
            agent.transform.position.y);
    }

    private Transform FindBombDefenseCover(
        GameObject requestedAgent,
        BombSite plantedSite,
        Vector3 bombPosition)
    {
        HashSet<Transform> assigned = new HashSet<Transform>();
        Vector3 defenderCenter = GetDefenderCenter();
        for (int attackerIndex = 0; attackerIndex < livingAttackers.Count; attackerIndex++)
        {
            AgentStats attacker = livingAttackers[attackerIndex];
            Vector3 ideal = bombPosition + GetBombDefenseOffset(
                attackerIndex,
                livingAttackers.Count,
                4f);
            Transform best = null;
            float bestScore = float.PositiveInfinity;
            foreach (Transform cover in coverPoints)
            {
                if (cover == null || assigned.Contains(cover)) continue;
                Vector3 hiddenPosition = GetPositionBesideCover(cover, defenderCenter);
                if (!IsPointInsideSite(plantedSite, hiddenPosition, siteInteriorMargin * 0.15f))
                    continue;

                float score = FlatDistance(hiddenPosition, ideal);
                if (IsLineBlocked(defenderCenter, hiddenPosition)) score -= 8f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = cover;
                }
            }

            if (best != null) assigned.Add(best);
            if (attacker != null && attacker.gameObject == requestedAgent) return best;
        }
        return null;
    }

    public static Vector3 GetBombDefenseOffset(int index, int teamCount, float radius)
    {
        float angle = Mathf.Max(0, index) * (360f / Mathf.Max(1, teamCount));
        return Quaternion.Euler(0f, angle, 0f) *
               Vector3.forward * Mathf.Max(0f, radius);
    }

    private static bool IsPointInsideSite(
        BombSite site,
        Vector3 point,
        float inset)
    {
        BoxCollider siteTrigger = site != null ? site.GetComponent<BoxCollider>() : null;
        if (siteTrigger == null)
        {
            return false;
        }

        Bounds bounds = siteTrigger.bounds;
        float minX = bounds.min.x + inset;
        float maxX = bounds.max.x - inset;
        float minZ = bounds.min.z + inset;
        float maxZ = bounds.max.z - inset;
        return point.x >= minX && point.x <= maxX &&
               point.z >= minZ && point.z <= maxZ;
    }

    public static Vector3 ClampInsideSiteBounds(
        Vector3 point,
        Bounds bounds,
        float margin,
        float height)
    {
        float safeMarginX = Mathf.Min(Mathf.Max(0f, margin), bounds.extents.x * 0.8f);
        float safeMarginZ = Mathf.Min(Mathf.Max(0f, margin), bounds.extents.z * 0.8f);
        return new Vector3(
            Mathf.Clamp(point.x, bounds.min.x + safeMarginX, bounds.max.x - safeMarginX),
            height,
            Mathf.Clamp(point.z, bounds.min.z + safeMarginZ, bounds.max.z - safeMarginZ));
    }

    public static Vector3 PushOutsideSiteBounds(
        Vector3 point,
        Bounds bounds,
        float margin,
        float height)
    {
        float safeMargin = Mathf.Max(0f, margin);
        Bounds expanded = bounds;
        expanded.Expand(new Vector3(safeMargin * 2f, 0f, safeMargin * 2f));
        if (point.x < expanded.min.x || point.x > expanded.max.x ||
            point.z < expanded.min.z || point.z > expanded.max.z)
        {
            point.y = height;
            return point;
        }

        Vector3 direction = point - bounds.center;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();
        float xDistance = Mathf.Abs(direction.x) > 0.001f
            ? bounds.extents.x / Mathf.Abs(direction.x)
            : float.PositiveInfinity;
        float zDistance = Mathf.Abs(direction.z) > 0.001f
            ? bounds.extents.z / Mathf.Abs(direction.z)
            : float.PositiveInfinity;
        float boundaryDistance = Mathf.Min(xDistance, zDistance);
        Vector3 outside = bounds.center +
                          direction * (boundaryDistance + safeMargin);
        outside.y = height;
        return outside;
    }

    private static Vector3 GetPositionBesideCover(Transform cover, Vector3 watchedPosition)
    {
        Vector3 awayFromWatch = cover.position - watchedPosition;
        awayFromWatch.y = 0f;
        if (awayFromWatch.sqrMagnitude < 0.01f)
        {
            awayFromWatch = Vector3.forward;
        }

        return cover.position + awayFromWatch.normalized * 1.1f;
    }

    private static bool MoveOrHold(
        GameObject agent,
        AgentMotor motor,
        Vector3 destination,
        Vector3 watchPosition,
        bool allowRoleRefinement = true)
    {
        AgentRole role = agent != null ? agent.GetComponent<AgentRole>() : null;
        if (allowRoleRefinement && role != null)
        {
            destination = role.RefineDestination(destination, watchPosition);
        }
        float tolerance = Mathf.Max(0.65f, motor.waypointReachDistance);
        if (!motor.HasReachedRequestedDestination(destination, tolerance))
        {
            motor.MoveTo(destination);
        }
        else
        {
            motor.Stop();
            motor.FacePosition(watchPosition);
        }

        return true;
    }

    private bool CanControl(GameObject agent, AgentMotor motor)
    {
        if (agent == null || motor == null || tacticManager == null ||
            roundManager == null || objectiveManager == null ||
            !tacticManager.HasSelectedInitialTactic ||
            !tacticManager.ControlsAttackingTeam(roundManager) ||
            roundManager.CurrentState == RoundState.Preparation ||
            roundManager.CurrentState == RoundState.RoundEnd)
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        HealthSystem health = agent.GetComponent<HealthSystem>();
        return stats != null && stats.team == tacticManager.ControlledTeam &&
               health != null && !health.IsDead;
    }

    private bool CanControlCombat(GameObject agent, AgentSensors sensors)
    {
        if (agent == null || sensors == null || tacticManager == null ||
            roundManager == null || !tacticManager.ControlsAttackingTeam(roundManager))
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        return stats != null && stats.team == tacticManager.ControlledTeam;
    }

    private void RefreshPlanAndWorld()
    {
        PruneUnavailableAgents(livingAttackers);
        PruneUnavailableAgents(livingDefenders);

        if (Time.time >= nextWorldRefreshTime)
        {
            RefreshLivingAgents();
            nextWorldRefreshTime = Time.time + worldRefreshInterval;
        }

        if (tacticManager == null)
        {
            return;
        }

        if (observedPlanRevision != tacticManager.PlanRevision)
        {
            observedPlanRevision = tacticManager.PlanRevision;
            focusFireTarget = null;
        }

        if (!tacticManager.HasSelectedInitialTactic)
        {
            targetSite = null;
            fakeSite = null;
            return;
        }

        if (targetSite == null)
        {
            SelectTargetSites();
        }
    }

    private void RefreshLivingAgents()
    {
        livingAttackers.Clear();
        livingDefenders.Clear();
        if (roundManager == null)
        {
            return;
        }

        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        foreach (AgentStats candidate in agents)
        {
            HealthSystem health = candidate.GetComponent<HealthSystem>();
            if (candidate.team == roundManager.attackingTeam && health != null &&
                observedAttackerHealth.Add(health))
            {
                health.Died += OnAttackerDied;
            }

            if (health == null || health.IsDead)
            {
                continue;
            }

            if (candidate.team == roundManager.attackingTeam)
            {
                livingAttackers.Add(candidate);
            }
            else if (candidate.team == roundManager.defendingTeam)
            {
                livingDefenders.Add(candidate);
            }
        }

        livingAttackers.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        livingDefenders.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
    }

    private static void PruneUnavailableAgents(List<AgentStats> agents)
    {
        agents.RemoveAll(agent =>
        {
            if (agent == null || !agent.gameObject.activeInHierarchy)
            {
                return true;
            }

            HealthSystem health = agent.GetComponent<HealthSystem>();
            return health == null || health.IsDead;
        });
    }

    private void OnAttackerDied(HealthSystem deadTeammate)
    {
        if (deadTeammate == null)
        {
            return;
        }

        AgentStats deadStats = deadTeammate.GetComponent<AgentStats>();
        if (deadStats != null)
        {
            livingAttackers.Remove(deadStats);
        }
    }

    private void SelectTargetSites()
    {
        if (objectiveManager == null)
        {
            return;
        }

        BombSite[] sites = objectiveManager.GetSites();
        if (sites.Length == 0)
        {
            targetSite = null;
            fakeSite = null;
            return;
        }

        targetSite = sites[UnityEngine.Random.Range(0, sites.Length)];
        fakeSite = GetAlternativeSite(targetSite);
        objectiveManager.SetSelectedAttackSite(targetSite);

        if (tacticManager.GetSelectedInitialTactic() ==
            InitialTeamTactic.FeintAndRotate && fakeSite != null)
        {
            Debug.Log(
                $"Feint and Rotate setup: Site {fakeSite.siteId} is the fake; " +
                $"Site {targetSite.siteId} is the real plant target.");
        }
    }

    private BombSite GetAlternativeSite(BombSite realSite)
    {
        if (objectiveManager == null || realSite == null)
        {
            return null;
        }

        BombSite alternative = null;
        float greatestSeparation = float.NegativeInfinity;
        foreach (BombSite site in objectiveManager.GetSites())
        {
            if (site == null || site == realSite)
            {
                continue;
            }

            float separation = FlatDistance(site.PlantPosition, realSite.PlantPosition);
            if (separation > greatestSeparation)
            {
                greatestSeparation = separation;
                alternative = site;
            }
        }

        return alternative;
    }

    private void ResolveReferences()
    {
        if (tacticManager == null)
        {
            tacticManager = TeamTacticManager.Instance;
        }

        if (roundManager == null)
        {
            roundManager = RoundManager.Instance != null
                ? RoundManager.Instance
                : FindAnyObjectByType<RoundManager>();
        }

        if (objectiveManager == null)
        {
            objectiveManager = ObjectiveManager.Instance != null
                ? ObjectiveManager.Instance
                : FindAnyObjectByType<ObjectiveManager>();
        }
    }

    private void CacheCoverPoints()
    {
        coverPoints.Clear();
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
        foreach (Transform candidate in transforms)
        {
            if (candidate.name.IndexOf("Cover", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                coverPoints.Add(candidate);
            }
        }

        coverPoints.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
    }

    private void OnRoundTacticsReset()
    {
        targetSite = null;
        fakeSite = null;
        silentAttackTriggered = false;
        feintState = FeintAndRotateState.Setup;
        feintFakeGroup.Clear();
        feintRealGroup.Clear();
        feintBombCarrier = null;
        defendersNearBAtSetup = 0;
        defendersNearAAtContact = 0;
        feintBaselineCaptured = false;
        fakeContactTime = -1f;
        feintAApproachDirection = Vector3.forward;
        feintBApproachDirection = Vector3.forward;
        feintBStagingPosition = Vector3.zero;
        feintBStagingName = string.Empty;
        hasFeintBStagingPosition = false;
        feintBStagingIsHidden = false;
        feintBStagingGroupSpacing = 1.6f;
        nextFeintStagingReselectTime = 0f;
        feintStagingReselectCount = 0;
        feintHiddenStagingCandidates.Clear();
        feintExposedStagingCandidates.Clear();
        feintSelectedStagingPath.Clear();
        feintSelectedExposureOrigins.Clear();
        ClearSplitRoutePlan();
        focusFireTarget = null;
        scoredPlantSite = null;
        nextPlantSiteEvaluationTime = 0f;
        plantSiteEvaluationRevision = -1;
        observedPlanRevision = -1;
        foreach (AgentMotor motor in FindObjectsByType<AgentMotor>(FindObjectsInactive.Exclude))
        {
            motor.SpeedMultiplier = 1f;
        }
    }

    private int GetAttackerIndex(GameObject agent)
    {
        for (int i = 0; i < livingAttackers.Count; i++)
        {
            if (livingAttackers[i] != null &&
                livingAttackers[i].gameObject == agent)
            {
                return i;
            }
        }

        return 0;
    }

    private Vector3 GetAttackerCenter()
    {
        return GetCenter(livingAttackers, targetSite != null
            ? targetSite.PlantPosition - Vector3.forward * 8f
            : Vector3.zero);
    }

    private Vector3 GetAttackerSafeZone()
    {
        if (attackerSafeZone == null)
        {
            GameObject spawn = GameObject.Find("TSpawn");
            if (spawn != null) attackerSafeZone = spawn.transform;
        }

        if (attackerSafeZone != null) return attackerSafeZone.position;

        Vector3 center = GetAttackerCenter();
        Vector3 awayFromDefenders = center - GetDefenderCenter();
        awayFromDefenders.y = 0f;
        if (awayFromDefenders.sqrMagnitude < 0.01f) awayFromDefenders = Vector3.back;
        return center + awayFromDefenders.normalized * 12f;
    }

    private Vector3 GetDefenderCenter()
    {
        return GetCenter(livingDefenders, targetSite != null
            ? targetSite.PlantPosition
            : Vector3.zero);
    }

    private static Vector3 GetCenter(List<AgentStats> agents, Vector3 fallback)
    {
        Vector3 total = Vector3.zero;
        int validCount = 0;
        foreach (AgentStats agent in agents)
        {
            if (agent == null || !agent.gameObject.activeInHierarchy)
            {
                continue;
            }

            total += agent.transform.position;
            validCount++;
        }

        return validCount > 0 ? total / validCount : fallback;
    }

    private static int CountLivingNear(
        List<AgentStats> agents,
        Vector3 position,
        float radius)
    {
        int count = 0;
        foreach (AgentStats agent in agents)
        {
            if (agent == null || !agent.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (FlatDistance(agent.transform.position, position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    private bool IsLivingEnemy(GameObject candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        AgentStats stats = candidate.GetComponent<AgentStats>();
        HealthSystem health = candidate.GetComponent<HealthSystem>();
        return stats != null && roundManager != null &&
               stats.team == roundManager.defendingTeam &&
               health != null && !health.IsDead;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmos()
    {
        DrawSplitRouteGizmos();
        if (!drawFeintStagingGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.15f, 1f, 0.3f, 0.5f);
        foreach (Vector3 candidate in feintHiddenStagingCandidates)
        {
            Gizmos.DrawSphere(candidate + Vector3.up * 0.15f, 0.18f);
        }

        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.45f);
        foreach (Vector3 candidate in feintExposedStagingCandidates)
        {
            Gizmos.DrawSphere(candidate + Vector3.up * 0.15f, 0.15f);
        }

        if (!hasFeintBStagingPosition)
        {
            return;
        }

        Gizmos.color = feintBStagingIsHidden
            ? new Color(0.1f, 1f, 0.35f, 1f)
            : new Color(1f, 0.3f, 0.1f, 1f);
        Gizmos.DrawWireSphere(feintBStagingPosition + Vector3.up * 0.2f, 0.8f);
        Gizmos.DrawCube(
            feintBStagingPosition + Vector3.up * 0.1f,
            new Vector3(feintBStagingGroupSpacing, 0.08f, 0.35f));

        Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.85f);
        for (int i = 1; i < feintSelectedStagingPath.Count; i++)
        {
            Gizmos.DrawLine(
                feintSelectedStagingPath[i - 1] + Vector3.up * 0.2f,
                feintSelectedStagingPath[i] + Vector3.up * 0.2f);
        }

        Gizmos.color = new Color(1f, 0.05f, 0.05f, 0.85f);
        foreach (Vector3 exposureOrigin in feintSelectedExposureOrigins)
        {
            Gizmos.DrawLine(
                exposureOrigin + Vector3.up * 0.8f,
                feintBStagingPosition + Vector3.up * 0.8f);
        }
    }

    private void DrawSplitRouteGizmos()
    {
        if (!drawSplitRouteGizmos || !hasSplitRoutePlan)
        {
            return;
        }

        Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.95f);
        DrawGizmoPath(splitDebugPathA);
        Gizmos.DrawWireSphere(splitRoutePointA + Vector3.up * 0.2f, 0.55f);
        Gizmos.DrawWireSphere(splitEntryPointA + Vector3.up * 0.2f, 0.7f);

        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.95f);
        DrawGizmoPath(splitDebugPathB);
        Gizmos.DrawWireSphere(splitRoutePointB + Vector3.up * 0.2f, 0.55f);
        Gizmos.DrawWireSphere(splitEntryPointB + Vector3.up * 0.2f, 0.7f);
    }

    private static void DrawGizmoPath(List<Vector3> path)
    {
        for (int i = 1; i < path.Count; i++)
        {
            Gizmos.DrawLine(
                path[i - 1] + Vector3.up * 0.2f,
                path[i] + Vector3.up * 0.2f);
        }
    }
}
