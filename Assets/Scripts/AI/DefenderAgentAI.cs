using UnityEngine;

public enum DefenderCombatState
{
    HoldingAngle,
    MovingToCover,
    InCover,
    Hiding,
    Peeking,
    Shooting,
    Repositioning,
    FallingBack,
    PursuingLastKnownEnemy,
    SearchingLastKnownArea,
    SearchingBombSite,
    RetakingBombSite,
    Defusing,
    CoveringDefuser,
    Flanking
}

/// <summary>
/// Per-defender combat movement. It only receives enemies confirmed by this
/// agent's sensors; shared team knowledge is an expiring position or site alert.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenderAgentAI : MonoBehaviour
{
    [Header("Combat Movement")]
    [SerializeField] private float coverSearchRadius = 13f;
    [SerializeField] private float peekDuration = 0.6f;
    [SerializeField] private float hideDuration = 0.5f;
    [SerializeField] private float shootBurstDuration = 0.8f;
    [SerializeField, Range(4f, 6f)] private float targetMemoryDuration = 5f;
    [SerializeField] private float peekOffset = 1.25f;
    [SerializeField] private float closeCombatDistance = 3f;
    [SerializeField] private float maxCoverIdleTime = 1.5f;
    [SerializeField] private float maxNoProgressTime = 4f;
    [SerializeField] private float forcedPeekInterval = 1f;
    [SerializeField] private float forcedRepositionInterval = 3f;
    [SerializeField] private float closeEnemyDistance = 4f;
    [SerializeField] private float objectiveUrgencyIncreaseRate = 1.5f;
    [SerializeField] private float maxOpenFireDuration = 1.2f;
    [SerializeField] private float strafeDistance = 2f;
    [SerializeField] private float strafeInterval = 0.65f;
    [SerializeField] private float repositionInterval = 4f;
    [SerializeField, Range(0f, 1f)] private float lowHealthRetreatThreshold = 0.35f;
    [SerializeField] private float lowHealthStopPursuit = 30f;
    [SerializeField] private float retreatMovementThreshold = 0.4f;
    [SerializeField] private float searchLastKnownAreaDuration = 3f;
    [SerializeField] private float lastKnownSearchRadius = 1.8f;
    [SerializeField] private float defuseDamageCancelThreshold = 20f;
    [Header("Retake Utility Clearing")]
    [SerializeField] private float turretBlockClearRadius = 2.25f;
    [SerializeField] private float turretBlockObjectiveRadius = 3.5f;
    [SerializeField] private float turretClearNoProgressDelay = 1.25f;
    [SerializeField] private bool drawCombatGizmos = true;

    private DefenderTeamCoordinator coordinator;
    private RoundManager roundManager;
    private ObjectiveManager objectiveManager;
    private AgentStats stats;
    private AgentSensors sensors;
    private AgentMemory memory;
    private WeaponLoadout loadout;
    private AgentMotor motor;
    private WeaponSystem weapon;
    private HealthSystem health;

    [SerializeField] private DefenderCombatState currentState =
        DefenderCombatState.HoldingAngle;
    private DefenderCoverSolution currentCover;
    [SerializeField] private GameObject lastVisibleTarget;
    [SerializeField] private Vector3 lastKnownTargetPosition;
    private float lastSeenTime = Mathf.NegativeInfinity;
    private float stateUntil;
    private float nextRepositionTime;
    private float nextStrafeTime;
    private int strafeDirection = 1;
    private bool forceReposition;
    private bool hideTimerStarted;
    private bool hasValidPeekPosition;
    private float coverCycleStartedAt;
    private float openFireStartedAt = Mathf.NegativeInfinity;
    private GameObject openFireTarget;
    private Vector3 previousObservedTargetPosition;
    private float previousObservationTime = Mathf.NegativeInfinity;
    private Vector3 lastKnownMovementDirection;
    private float lastKnownSearchUntil;
    private float nextSearchPointTime;
    private int searchPointIndex;
    private Vector3 lastMovementSample;
    private Vector3 trackedObjectivePosition;
    private float lastMovementTime;
    private float lastObjectiveProgressTime;
    private float bestObjectiveDistance = Mathf.Infinity;
    private float nextForcedPeekTime;
    private float nextForcedRepositionTime;

    [Header("Runtime Debug")]
    [SerializeField] private string currentObjectiveDebug;
    [SerializeField] private string currentReasonDebug;
    [SerializeField] private float timeInCoverDebug;
    [SerializeField] private float timeSinceLastShotDebug;
    [SerializeField] private float timeSinceLastMovementDebug;

    public DefenderCombatState CurrentState => currentState;
    public Transform CurrentCover => currentCover.cover;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        sensors = GetComponent<AgentSensors>();
        memory = GetComponent<AgentMemory>();
        loadout = WeaponLoadout.Get(gameObject);
        motor = GetComponent<AgentMotor>();
        weapon = GetComponent<WeaponSystem>();
        health = GetComponent<HealthSystem>();
        lastMovementSample = transform.position;
        lastMovementTime = Time.time;
        lastObjectiveProgressTime = Time.time;
    }

    private void OnEnable()
    {
        if (health == null)
        {
            health = GetComponent<HealthSystem>();
        }

        if (health != null)
        {
            health.Damaged += OnDamaged;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
        }
    }

    public bool TryExecute(GameObject visibleTarget)
    {
        GetComponent<AgentDebugVisual>()?.SetAIState(currentState.ToString());

        ResolveReferences();
        if (coordinator == null || roundManager == null || stats == null ||
            sensors == null || motor == null || health == null || health.IsDead ||
            stats.team != roundManager.defendingTeam ||
            roundManager.CurrentState == RoundState.Preparation ||
            roundManager.CurrentState == RoundState.RoundEnd)
        {
            return false;
        }

        UpdateProgressDebug();

        if (roundManager.CurrentState == RoundState.BombPlanted &&
            coordinator.BombPositionKnown)
        {
            bool allStrikersDead = !roundManager.AreAttackersAlive;
            bool designatedDefuserShouldCommit =
                coordinator.ShouldPrioritizeDefuse(gameObject, visibleTarget);
            bool mustRiskDefuse = coordinator.BombTimerIsCritical &&
                                  designatedDefuserShouldCommit;
            bool mustClearDefuseArea =
                coordinator.MustClearDefuseArea(gameObject);
            bool directlyBlockingDefuse = visibleTarget != null &&
                coordinator.ShouldEngagePostPlantThreat(gameObject, visibleTarget);
            if (allStrikersDead || mustRiskDefuse ||
                designatedDefuserShouldCommit || mustClearDefuseArea ||
                !directlyBlockingDefuse)
            {
                lastVisibleTarget = null;
                currentCover = default;
                forceReposition = false;
                memory?.Clear();
                currentReasonDebug = allStrikersDead
                    ? "All strikers eliminated: forced defuse"
                    : mustRiskDefuse
                        ? "Bomb timer critical: risk defuse"
                        : designatedDefuserShouldCommit
                            ? "Teammate covering: commit to defuse"
                        : mustClearDefuseArea
                            ? "Yielding exclusive defuse area"
                        : "Post-plant retake overrides local combat";
                return ExecuteTeamOrder(visibleTarget);
            }
        }

        if (visibleTarget != null && sensors.CanDetect(visibleTarget))
        {
            bool retreating = ObserveTargetMovement(visibleTarget);
            lastVisibleTarget = visibleTarget;
            lastKnownTargetPosition = visibleTarget.transform.position;
            lastSeenTime = Time.time;
            memory?.ObserveEnemy(visibleTarget);
            coordinator.ReportVisibleAttacker(gameObject, visibleTarget);
            if (coordinator.TryGetEncirclementOrder(
                    gameObject,
                    visibleTarget,
                    out DefenderOrder encirclementOrder))
            {
                return ExecuteEncirclement(encirclementOrder, visibleTarget);
            }
            if ((retreating ||
                 currentState == DefenderCombatState.PursuingLastKnownEnemy) &&
                ShouldPursue(lastKnownTargetPosition))
            {
                if (retreating && currentState !=
                    DefenderCombatState.PursuingLastKnownEnemy)
                {
                    Debug.Log(
                        "Defender saw attacker retreating, pursuing last known position");
                }

                return ExecutePursuit(visibleTarget, lastKnownTargetPosition, true);
            }

            return ExecuteCombat(
                visibleTarget,
                visibleTarget.transform.position,
                true);
        }

        if (coordinator.ShouldImmediatelyReinforce(gameObject))
        {
            lastVisibleTarget = null;
            currentCover = default;
            memory?.Clear();
            currentReasonDebug = "Leaving empty site to reinforce";
            return ExecuteTeamOrder();
        }

        openFireTarget = null;
        openFireStartedAt = Mathf.NegativeInfinity;

        if (currentState == DefenderCombatState.SearchingLastKnownArea)
        {
            return ExecuteLastKnownAreaSearch();
        }

        bool hasRecentTargetMemory = lastVisibleTarget != null &&
                                     Time.time <= lastSeenTime +
                                     targetMemoryDuration;
        if (roundManager.CurrentState == RoundState.BombPlanted &&
            (!hasRecentTargetMemory ||
             !coordinator.ShouldRememberedThreatDelayBombOrder(
                 lastKnownTargetPosition)))
        {
            lastVisibleTarget = null;
            return ExecuteTeamOrder();
        }

        if (hasRecentTargetMemory)
        {
            lastKnownTargetPosition = memory != null &&
                                      memory.TryGetKnownPosition(out Vector3 remembered)
                ? remembered
                : lastKnownTargetPosition;
            if (coordinator.TryGetEncirclementOrder(
                    gameObject,
                    lastVisibleTarget,
                    out DefenderOrder encirclementOrder))
            {
                return ExecuteEncirclement(encirclementOrder, null);
            }
            if (currentState == DefenderCombatState.PursuingLastKnownEnemy &&
                ShouldPursue(lastKnownTargetPosition))
            {
                return ExecutePursuit(null, lastKnownTargetPosition, false);
            }

            return ExecuteCombat(null, lastKnownTargetPosition, false);
        }

        lastVisibleTarget = null;
        lastKnownMovementDirection = Vector3.zero;

        return ExecuteTeamOrder();
    }

    private bool ObserveTargetMovement(GameObject target)
    {
        Vector3 currentPosition = target.transform.position;
        if (lastVisibleTarget != target ||
            Time.time > previousObservationTime + 0.75f)
        {
            previousObservedTargetPosition = currentPosition;
            previousObservationTime = Time.time;
            lastKnownMovementDirection = Vector3.zero;
            return false;
        }

        Vector3 movement = currentPosition - previousObservedTargetPosition;
        movement.y = 0f;
        float elapsed = Mathf.Max(0.02f, Time.time - previousObservationTime);
        previousObservedTargetPosition = currentPosition;
        previousObservationTime = Time.time;
        if (movement.magnitude / elapsed < retreatMovementThreshold)
        {
            return false;
        }

        lastKnownMovementDirection = movement.normalized;
        Vector3 awayFromDefender = currentPosition - transform.position;
        awayFromDefender.y = 0f;
        return awayFromDefender.sqrMagnitude > 0.01f &&
               Vector3.Dot(lastKnownMovementDirection, awayFromDefender.normalized) > 0.35f;
    }

    private bool ShouldPursue(Vector3 lastKnownPosition)
    {
        return health != null && health.CurrentHealth > lowHealthStopPursuit &&
               coordinator != null &&
               coordinator.CanPursueLastKnownEnemy(gameObject, lastKnownPosition);
    }

    private bool ExecutePursuit(
        GameObject target,
        Vector3 lastKnownPosition,
        bool hasCurrentVision)
    {
        currentCover = default;
        forceReposition = false;
        currentState = DefenderCombatState.PursuingLastKnownEnemy;
        motor.SpeedMultiplier = 1.2f;
        motor.FacePosition(lastKnownPosition);
        if (hasCurrentVision)
        {
            TryShootWhileMoving(target);
        }

        if (MoveTo(lastKnownPosition, Mathf.Max(0.7f, motor.waypointReachDistance)))
        {
            return true;
        }

        motor.Stop();
        currentState = DefenderCombatState.SearchingLastKnownArea;
        lastKnownSearchUntil = Time.time + searchLastKnownAreaDuration;
        nextSearchPointTime = Time.time;
        searchPointIndex = 0;
        Debug.Log("Defender lost sight, searching last known area");
        return true;
    }

    private bool ExecuteLastKnownAreaSearch()
    {
        if (roundManager.CurrentState == RoundState.BombPlanted ||
            health.CurrentHealth <= lowHealthStopPursuit ||
            Time.time >= lastKnownSearchUntil)
        {
            memory?.Clear();
            lastVisibleTarget = null;
            lastKnownMovementDirection = Vector3.zero;
            currentState = DefenderCombatState.HoldingAngle;
            return ExecuteTeamOrder();
        }

        Vector3 forward = lastKnownMovementDirection.sqrMagnitude > 0.01f
            ? lastKnownMovementDirection.normalized
            : transform.forward;
        forward.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3[] offsets =
        {
            forward,
            side,
            -side,
            -forward
        };

        if (Time.time >= nextSearchPointTime)
        {
            nextSearchPointTime = Time.time + 0.75f;
            searchPointIndex = (searchPointIndex + 1) % offsets.Length;
        }

        Vector3 searchPoint = lastKnownTargetPosition +
                              offsets[searchPointIndex] * lastKnownSearchRadius;
        motor.SpeedMultiplier = 0.85f;
        MoveTo(searchPoint, 0.45f);
        motor.FacePosition(
            lastKnownTargetPosition + offsets[(searchPointIndex + 1) % offsets.Length]);
        return true;
    }

    private void UpdateProgressDebug()
    {
        if (FlatDistance(transform.position, lastMovementSample) >= 0.35f)
        {
            lastMovementSample = transform.position;
            lastMovementTime = Time.time;
        }

        Vector3 objective = coordinator.GetObjectivePositionFor(gameObject);
        if (FlatDistance(objective, trackedObjectivePosition) > 2f)
        {
            trackedObjectivePosition = objective;
            bestObjectiveDistance = FlatDistance(transform.position, objective);
            lastObjectiveProgressTime = Time.time;
        }

        float objectiveDistance = FlatDistance(transform.position, objective);
        if (objectiveDistance < bestObjectiveDistance - 0.4f)
        {
            bestObjectiveDistance = objectiveDistance;
            lastObjectiveProgressTime = Time.time;
        }

        currentObjectiveDebug = roundManager.CurrentState == RoundState.BombPlanted
            ? coordinator.BombPositionKnown
                ? "Retake / defuse planted bomb"
                : "Search planted bomb sites"
            : coordinator.ShouldImmediatelyReinforce(gameObject)
                ? "Reinforce attacked site"
                : "Hold / investigate assigned site";
        timeInCoverDebug = currentCover.cover != null
            ? Mathf.Max(0f, Time.time - coverCycleStartedAt)
            : 0f;
        timeSinceLastShotDebug = weapon != null
            ? weapon.TimeSinceLastShot
            : Mathf.Infinity;
        timeSinceLastMovementDebug = Time.time - lastMovementTime;
    }

    private bool TryBreakCombatStalemate(
        GameObject target,
        Vector3 targetPosition,
        bool hasCurrentVision,
        out bool actionResult)
    {
        actionResult = false;
        float lastShot = weapon != null ? weapon.LastShotTime : Mathf.NegativeInfinity;
        float noProgressDuration = Time.time - Mathf.Max(
            lastObjectiveProgressTime,
            lastShot);
        float targetDistance = FlatDistance(transform.position, targetPosition);

        if (hasCurrentVision && target != null && weapon != null && weapon.IsReady &&
            targetDistance <= loadout.MaximumRange && sensors.HasLineOfSight(target))
        {
            return false;
        }

        if (!hasCurrentVision && currentCover.cover != null &&
            targetDistance <= closeEnemyDistance &&
            Time.time >= nextForcedRepositionTime)
        {
            nextForcedRepositionTime = Time.time + forcedRepositionInterval;
            currentReasonDebug = "Close remembered enemy has no firing angle";
            Debug.Log("Anti-stalemate: forcing reposition");
            RequestReposition("close enemy is blocked by cover");
            actionResult = true;
            return true;
        }

        if (noProgressDuration < maxNoProgressTime)
        {
            return false;
        }

        float objectiveUrgency = 1f +
            (noProgressDuration - maxNoProgressTime) * objectiveUrgencyIncreaseRate;
        if (noProgressDuration >= maxNoProgressTime +
            forcedRepositionInterval * 2f)
        {
            currentReasonDebug = $"Objective urgency {objectiveUrgency:0.0}";
            Debug.Log("Anti-stalemate: forcing objective push");
            currentCover = default;
            forceReposition = false;
            if (ShouldPursue(targetPosition))
            {
                actionResult = ExecutePursuit(null, targetPosition, false);
            }
            else
            {
                lastVisibleTarget = null;
                memory?.Clear();
                actionResult = ExecuteTeamOrder();
            }
            lastObjectiveProgressTime = Time.time;
            return true;
        }

        if (currentCover.cover != null &&
            noProgressDuration >= maxNoProgressTime + forcedRepositionInterval &&
            Time.time >= nextForcedRepositionTime)
        {
            nextForcedRepositionTime = Time.time + forcedRepositionInterval;
            currentReasonDebug = "Cover angle produced no progress";
            Debug.Log("Anti-stalemate: forcing reposition");
            RequestReposition("combat made no progress");
            actionResult = true;
            return true;
        }

        bool canForcePeek = currentCover.cover != null &&
                            (currentState == DefenderCombatState.InCover ||
                             currentState == DefenderCombatState.Hiding);
        if (canForcePeek && Time.time >= nextForcedPeekTime)
        {
            nextForcedPeekTime = Time.time + forcedPeekInterval;
            currentReasonDebug = "Cover idle with remembered enemy";
            Debug.Log("Anti-stalemate: forcing peek");
            BeginPeekOrReposition(targetPosition);
            actionResult = true;
            return true;
        }

        return false;
    }

    private bool ExecuteCombat(
        GameObject target,
        Vector3 targetPosition,
        bool hasCurrentVision)
    {
        bool lowHealth = health.NormalizedHealth <= lowHealthRetreatThreshold;
        if (TryBreakCombatStalemate(
                target,
                targetPosition,
                hasCurrentVision,
                out bool antiStalemateAction))
        {
            return antiStalemateAction;
        }
        bool isReloading = weapon != null && weapon.IsReloading;
        if (hasCurrentVision && target != null && !isReloading &&
            FlatDistance(transform.position, targetPosition) <= closeCombatDistance)
        {
            return ExecuteCloseCombat(target, targetPosition);
        }

        if (hasCurrentVision && target != null && !lowHealth && !forceReposition &&
            currentCover.cover == null &&
            FlatDistance(transform.position, targetPosition) <= loadout.MaximumRange &&
            sensors.HasLineOfSight(target))
        {
            if (openFireTarget != target)
            {
                openFireTarget = target;
                openFireStartedAt = Time.time;
            }

            if (Time.time <= openFireStartedAt + maxOpenFireDuration)
            {
                return ExecuteOpenFire(target, targetPosition);
            }
        }

        bool needsNewCover = currentCover.cover == null || forceReposition ||
                             Time.time >= nextRepositionTime;
        if (needsNewCover)
        {
            Transform previousCover = currentCover.cover;
            bool wasForcedToMove = forceReposition || Time.time >= nextRepositionTime;
            Vector3 objective = GetCurrentObjectivePosition();
            if (coordinator.TryFindBestCover(
                    gameObject,
                    targetPosition,
                    objective,
                    coverSearchRadius,
                    out DefenderCoverSolution cover,
                    wasForcedToMove ? previousCover : null))
            {
                currentCover = cover;
                hasValidPeekPosition = false;
                currentState = lowHealth
                    ? DefenderCombatState.FallingBack
                    : needsNewCover && forceReposition
                        ? DefenderCombatState.Repositioning
                        : DefenderCombatState.MovingToCover;
                forceReposition = false;
                nextRepositionTime = Time.time + repositionInterval;
                Debug.Log(
                    $"{name} selected cover {cover.cover.name} " +
                    $"(score {cover.score:0.0}, state {currentState}).");
            }
            else
            {
                currentCover = default;
                currentState = DefenderCombatState.Repositioning;
                forceReposition = false;
                nextRepositionTime = Time.time + repositionInterval;
            }
        }

        if (currentCover.cover == null)
        {
            return ExecuteStrafeCombat(target, targetPosition, hasCurrentVision);
        }

        switch (currentState)
        {
            case DefenderCombatState.MovingToCover:
            case DefenderCombatState.Repositioning:
            case DefenderCombatState.FallingBack:
                motor.SpeedMultiplier = lowHealth ? 1.25f : 1.12f;
                if (MoveTo(currentCover.hiddenPosition, 0.7f))
                {
                    if (hasCurrentVision)
                    {
                        TryShootWhileMoving(target);
                    }
                    return true;
                }

                motor.Stop();
                motor.FacePosition(targetPosition);
                currentState = DefenderCombatState.InCover;
                stateUntil = Time.time + (lowHealth ? hideDuration * 1.5f : hideDuration);
                hideTimerStarted = true;
                coverCycleStartedAt = Time.time;
                Debug.Log(name + " reached cover " + currentCover.cover.name + ".");
                return true;

            case DefenderCombatState.InCover:
                motor.Stop();
                motor.FacePosition(targetPosition);
                if ((Time.time >= stateUntil ||
                    Time.time >= coverCycleStartedAt + maxCoverIdleTime) && !isReloading)
                {
                    BeginPeekOrReposition(targetPosition);
                }
                return true;

            case DefenderCombatState.Hiding:
                motor.FacePosition(targetPosition);
                if (MoveTo(currentCover.hiddenPosition, 0.55f))
                {
                    return true;
                }

                motor.Stop();
                if (!hideTimerStarted)
                {
                    hideTimerStarted = true;
                    coverCycleStartedAt = Time.time;
                    stateUntil = Time.time + (lowHealth
                        ? Mathf.Max(hideDuration, 0.9f)
                        : hideDuration);
                }

                if ((Time.time >= stateUntil ||
                    Time.time >= coverCycleStartedAt + maxCoverIdleTime) && !isReloading)
                {
                    BeginPeekOrReposition(targetPosition);
                }
                return true;

            case DefenderCombatState.Peeking:
                motor.SpeedMultiplier = 1f;
                motor.FacePosition(targetPosition);
                if (!hasValidPeekPosition ||
                    DefenderTeamCoordinator.IsLineBlocked(
                        currentCover.peekPosition,
                        targetPosition))
                {
                    RequestReposition(
                        "cannot find a clear peek angle from " + currentCover.cover.name);
                    return true;
                }

                MoveTo(currentCover.peekPosition, 0.45f);
                if (hasCurrentVision && TryShootWhileMoving(target))
                {
                    currentState = DefenderCombatState.Shooting;
                    stateUntil = Time.time + shootBurstDuration;
                    Debug.Log(name + " has line of sight from peek and is shooting.");
                    return true;
                }

                if (Time.time >= stateUntil)
                {
                    ReturnToCover();
                }
                return true;

            case DefenderCombatState.Shooting:
                motor.FacePosition(targetPosition);
                if (hasCurrentVision)
                {
                    TryShootWhileMoving(target);
                }

                if (!hasCurrentVision || Time.time >= stateUntil)
                {
                    ReturnToCover();
                }
                return true;

            default:
                currentState = DefenderCombatState.MovingToCover;
                return true;
        }
    }

    private bool ExecuteCloseCombat(GameObject target, Vector3 targetPosition)
    {
        if (Time.time >= nextStrafeTime)
        {
            nextStrafeTime = Time.time + strafeInterval;
            strafeDirection *= -1;
        }

        Vector3 toEnemy = targetPosition - transform.position;
        toEnemy.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toEnemy.normalized) * strafeDirection;
        motor.SpeedMultiplier = 1.1f;
        motor.MoveTo(transform.position + side * Mathf.Min(1.25f, strafeDistance));
        motor.FacePosition(targetPosition);
        TryShootWhileMoving(target);
        currentState = DefenderCombatState.Shooting;
        stateUntil = Time.time + shootBurstDuration;
        return true;
    }

    private bool ExecuteOpenFire(GameObject target, Vector3 targetPosition)
    {
        Vector3 toEnemy = targetPosition - transform.position;
        toEnemy.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toEnemy.normalized) * strafeDirection;
        if (Time.time >= nextStrafeTime)
        {
            nextStrafeTime = Time.time + strafeInterval;
            strafeDirection *= -1;
        }

        motor.SpeedMultiplier = 0.7f;
        motor.MoveTo(transform.position + side * 0.65f);
        motor.FacePosition(targetPosition);
        if (weapon != null && weapon.IsReady)
        {
            Debug.Log(name + " forcing shoot because enemy is visible.");
        }

        TryShootWhileMoving(target);
        currentState = DefenderCombatState.Shooting;
        return true;
    }

    private void BeginPeekOrReposition(Vector3 targetPosition)
    {
        if (!TryCalculatePeekPosition(targetPosition, out Vector3 peekPosition))
        {
            Debug.Log("Agent cannot find peek angle, repositioning");
            RequestReposition(
                "cannot find a valid left or right peek angle from " +
                (currentCover.cover != null ? currentCover.cover.name : "cover"));
            return;
        }

        currentCover.peekPosition = peekPosition;
        hasValidPeekPosition = true;
        hideTimerStarted = false;
        currentState = DefenderCombatState.Peeking;
        stateUntil = Time.time + peekDuration;
        motor.MoveTo(peekPosition);
        motor.FacePosition(targetPosition);
        Debug.Log(name + " switching to peek from " + currentCover.cover.name + ".");
    }

    private bool TryCalculatePeekPosition(
        Vector3 targetPosition,
        out Vector3 peekPosition)
    {
        peekPosition = default;
        if (currentCover.cover == null)
        {
            return false;
        }

        Vector3 hiddenPosition = currentCover.hiddenPosition;
        Vector3 enemyDirection = targetPosition - hiddenPosition;
        enemyDirection.y = 0f;
        if (enemyDirection.sqrMagnitude < 0.01f)
        {
            return false;
        }

        enemyDirection.Normalize();
        Vector3 sideDirection = Vector3.Cross(Vector3.up, enemyDirection).normalized;
        Collider coverCollider = currentCover.cover.GetComponent<Collider>();
        float sideExtent = peekOffset;
        if (coverCollider != null)
        {
            sideExtent = Mathf.Abs(sideDirection.x) * coverCollider.bounds.extents.x +
                         Mathf.Abs(sideDirection.z) * coverCollider.bounds.extents.z +
                         0.65f;
        }

        Vector3 candidateLeft = hiddenPosition + sideDirection * sideExtent +
                                enemyDirection * 0.35f;
        Vector3 candidateRight = hiddenPosition - sideDirection * sideExtent +
                                 enemyDirection * 0.35f;
        bool leftValid = IsPeekCandidateValid(candidateLeft, targetPosition);
        bool rightValid = IsPeekCandidateValid(candidateRight, targetPosition);
        if (!leftValid && !rightValid)
        {
            return false;
        }

        if (leftValid && rightValid)
        {
            // Both angles can fire. Prefer the shorter move, limiting the time spent
            // exposed while leaving cover.
            peekPosition = FlatDistance(transform.position, candidateLeft) <=
                           FlatDistance(transform.position, candidateRight)
                ? candidateLeft
                : candidateRight;
        }
        else
        {
            peekPosition = leftValid ? candidateLeft : candidateRight;
        }

        return true;
    }

    private bool IsPeekCandidateValid(Vector3 candidate, Vector3 targetPosition)
    {
        if (FlatDistance(candidate, currentCover.hiddenPosition) > 4f ||
            DefenderTeamCoordinator.IsLineBlocked(candidate, targetPosition) ||
            !IsAgentPositionOpen(candidate) ||
            (AStarPathfinder3D.Instance != null &&
             !AStarPathfinder3D.Instance.IsValidAgentPosition(candidate, 0.5f)))
        {
            return false;
        }

        if (AStarPathfinder3D.Instance == null)
        {
            return true;
        }

        System.Collections.Generic.List<Vector3> path =
            AStarPathfinder3D.Instance.FindPath(transform.position, candidate);
        return path != null && (path.Count == 0 ||
               FlatDistance(path[path.Count - 1], candidate) <= 2f);
    }

    private static bool IsAgentPositionOpen(Vector3 position)
    {
        Collider[] overlaps = Physics.OverlapSphere(
            position + Vector3.up * 0.55f,
            0.4f,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || overlap.GetComponentInParent<AgentStats>() != null ||
                overlap.name.IndexOf(
                    "Floor",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void ReturnToCover()
    {
        currentState = DefenderCombatState.Hiding;
        hideTimerStarted = false;
        hasValidPeekPosition = false;
        coverCycleStartedAt = Time.time;
        motor.MoveTo(currentCover.hiddenPosition);
        Debug.Log(name + " returning to cover after peek burst.");
    }

    private void RequestReposition(string reason)
    {
        Debug.Log(name + " " + reason + "; repositioning.");
        currentState = DefenderCombatState.Repositioning;
        forceReposition = true;
        hasValidPeekPosition = false;
        hideTimerStarted = false;
        motor.Stop();
    }

    private bool ExecuteStrafeCombat(
        GameObject target,
        Vector3 targetPosition,
        bool hasCurrentVision)
    {
        if (Time.time >= nextStrafeTime)
        {
            nextStrafeTime = Time.time + strafeInterval;
            strafeDirection *= -1;
        }

        Vector3 toEnemy = targetPosition - transform.position;
        toEnemy.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toEnemy.normalized) * strafeDirection;
        Vector3 objective = GetCurrentObjectivePosition();
        Vector3 destination = transform.position + side * strafeDistance;
        float enemyDistance = FlatDistance(transform.position, targetPosition);
        if (enemyDistance > loadout.MaximumRange * 0.9f)
        {
            destination += toEnemy.normalized * Mathf.Min(
                strafeDistance,
                enemyDistance - loadout.MaximumRange * 0.8f);
        }

        if (FlatDistance(destination, objective) > 14f)
        {
            destination = Vector3.Lerp(destination, objective, 0.45f);
        }

        motor.SpeedMultiplier = 1.08f;
        motor.MoveTo(destination);
        motor.FacePosition(targetPosition);
        if (hasCurrentVision)
        {
            TryShootWhileMoving(target);
        }
        currentState = DefenderCombatState.Repositioning;
        return true;
    }

    private bool ExecuteEncirclement(
        DefenderOrder order,
        GameObject visibleTarget)
    {
        currentCover = default;
        forceReposition = false;
        currentState = DefenderCombatState.Flanking;
        currentObjectiveDebug = order.engagementRole + " around shared threat";
        currentReasonDebug = "Squad encirclement instead of frontal crowding";
        motor.SpeedMultiplier = order.speedMultiplier;

        bool moving = MoveTo(
            order.destination,
            Mathf.Max(0.65f, motor.waypointReachDistance));
        motor.FacePosition(order.watchPosition);
        if (visibleTarget != null)
        {
            TryShootWhileMoving(visibleTarget);
        }

        if (!moving)
        {
            motor.Stop();
            motor.FacePosition(order.watchPosition);
        }

        return true;
    }

    private bool ExecuteTeamOrder(GameObject visibleThreat = null)
    {
        if (!coordinator.TryGetOrder(gameObject, out DefenderOrder order))
        {
            motor.Stop();
            return true;
        }

        currentCover = default;
        motor.SpeedMultiplier = order.speedMultiplier;
        switch (order.type)
        {
            case DefenderOrderType.Flank:
                return ExecuteEncirclement(order, visibleThreat);

            case DefenderOrderType.SearchBombSite:
                currentState = DefenderCombatState.SearchingBombSite;
                if (!MoveTo(
                        order.destination,
                        Mathf.Max(0.75f, motor.waypointReachDistance)))
                {
                    motor.Stop();
                    motor.FacePosition(order.watchPosition);
                    coordinator.ReportSiteChecked(gameObject, order.site);
                }
                return true;

            case DefenderOrderType.Defuse:
                if (coordinator.CanStartDefuse(gameObject, visibleThreat) &&
                    objectiveManager != null && objectiveManager.CanDefuse(gameObject))
                {
                    if (currentState != DefenderCombatState.Defusing)
                    {
                        Debug.Log("Defender starting defuse");
                    }
                    currentState = DefenderCombatState.Defusing;
                    motor.Stop();
                    objectiveManager.BeginDefuse(gameObject);
                    return true;
                }

                if (TryClearBlockingTurret(order.destination))
                {
                    return true;
                }

                // Defuse positions may sit near the edge of the interaction radius.
                // The normal 0.65 movement tolerance can stop the agent outside
                // that radius, so approach this objective with a strict tolerance.
                currentState = DefenderCombatState.RetakingBombSite;
                motor.MoveToExactObjective(order.destination);
                if (motor.HasReachedRequestedDestination(order.destination, 0.08f))
                {
                    motor.Stop();
                    motor.FacePosition(order.watchPosition);
                }
                return true;

            case DefenderOrderType.CoverDefuser:
                if (currentState != DefenderCombatState.CoveringDefuser)
                {
                    Debug.Log("Defender covering defuser");
                }
                currentState = DefenderCombatState.CoveringDefuser;
                MoveOrHold(order.destination, order.watchPosition);
                return true;

            case DefenderOrderType.Rotate:
            case DefenderOrderType.Retake:
                if (order.type == DefenderOrderType.Retake &&
                    currentState != DefenderCombatState.RetakingBombSite)
                {
                    Debug.Log("Defender moving to planted bomb");
                }
                currentState = order.type == DefenderOrderType.Retake
                    ? DefenderCombatState.RetakingBombSite
                    : DefenderCombatState.Repositioning;
                currentCover = default;
                if (order.type == DefenderOrderType.Retake &&
                    TryClearBlockingTurret(order.destination))
                {
                    return true;
                }

                MoveOrHold(order.destination, order.watchPosition);
                return true;

            case DefenderOrderType.Investigate:
            case DefenderOrderType.HoldSite:
            default:
                currentState = DefenderCombatState.HoldingAngle;
                MoveOrHold(order.destination, order.watchPosition);
                return true;
        }
    }

    private bool TryClearBlockingTurret(Vector3 objective)
    {
        if (stats == null || motor == null || weapon == null ||
            loadout == null || sensors == null)
        {
            return false;
        }

        DeployableTurret turret = FindBlockingEnemyTurret(objective);
        if (turret == null)
        {
            return false;
        }

        GameObject turretObject = turret.gameObject;
        float distance = FlatDistance(transform.position, turretObject.transform.position);
        if (distance > loadout.MaximumRange || !sensors.HasLineOfSight(turretObject))
        {
            return false;
        }

        currentState = DefenderCombatState.Shooting;
        currentReasonDebug = "Clearing enemy turret blocking retake";
        currentObjectiveDebug = "Destroy enemy turret";
        currentCover = default;
        forceReposition = false;
        motor.Stop();
        motor.FacePosition(turretObject.transform.position);
        weapon.TryAttack(turretObject);
        coordinator?.ReportCombat(gameObject, null);
        return true;
    }

    private DeployableTurret FindBlockingEnemyTurret(Vector3 objective)
    {
        bool noObjectiveProgress =
            Time.time >= lastObjectiveProgressTime + turretClearNoProgressDelay;
        if (!motor.IsStuck && !noObjectiveProgress)
        {
            return null;
        }

        DeployableTurret best = null;
        float bestScore = Mathf.NegativeInfinity;
        foreach (DeployableTurret turret in
                 FindObjectsByType<DeployableTurret>(FindObjectsInactive.Exclude))
        {
            if (turret == null || turret.IsDestroyed || turret.Team == stats.team)
            {
                continue;
            }

            Vector3 turretPosition = turret.transform.position;
            float distanceToAgent = FlatDistance(transform.position, turretPosition);
            float distanceToObjective = FlatDistance(turretPosition, objective);
            float distanceToApproach =
                FlatDistanceToSegment(turretPosition, transform.position, objective);
            bool blocksApproach = distanceToApproach <= turretBlockClearRadius ||
                                  distanceToObjective <= turretBlockObjectiveRadius ||
                                  distanceToAgent <= turretBlockClearRadius;
            if (!blocksApproach || distanceToAgent > loadout.MaximumRange)
            {
                continue;
            }

            float score = (turretBlockClearRadius - distanceToApproach) * 4f +
                          (turretBlockObjectiveRadius - distanceToObjective) * 2f -
                          distanceToAgent;
            if (score > bestScore)
            {
                bestScore = score;
                best = turret;
            }
        }

        return best;
    }

    private bool TryShootWhileMoving(GameObject target)
    {
        if (target == null || weapon == null || stats == null ||
            FlatDistance(transform.position, target.transform.position) > loadout.MaximumRange ||
            !sensors.HasLineOfSight(target))
        {
            return false;
        }

        bool weaponWasReady = weapon.IsReady;
        motor.FacePosition(target.transform.position);
        weapon.TryAttack(target);
        coordinator.ReportCombat(gameObject, target);
        if (weaponWasReady)
        {
            Debug.Log(name + " shooting visible enemy " + target.name + ".");
        }
        return true;
    }

    private bool MoveTo(Vector3 destination, float tolerance)
    {
        if (motor.HasReachedRequestedDestination(destination, tolerance))
        {
            return false;
        }

        motor.MoveTo(destination);
        return motor.HasDestination && !motor.HasReachedDestination(tolerance);
    }

    private void MoveOrHold(Vector3 destination, Vector3 watchPosition)
    {
        if (MoveTo(destination, Mathf.Max(0.65f, motor.waypointReachDistance)))
        {
            return;
        }

        motor.Stop();
        motor.FacePosition(watchPosition);
    }

    private Vector3 GetCurrentObjectivePosition()
    {
        if (coordinator != null)
        {
            return coordinator.GetObjectivePositionFor(gameObject);
        }

        BombSite nearest = null;
        float nearestDistance = float.PositiveInfinity;
        if (objectiveManager != null)
        {
            foreach (BombSite site in objectiveManager.GetSites())
            {
                float distance = FlatDistance(transform.position, site.PlantPosition);
                if (distance >= nearestDistance) continue;
                nearest = site;
                nearestDistance = distance;
            }
        }

        return nearest != null ? nearest.PlantPosition : transform.position;
    }

    private void OnDamaged(HealthSystem damagedHealth, float amount, GameObject attacker)
    {
        ResolveReferences();
        coordinator?.ReportDefenderDamaged(gameObject, attacker);
        bool immediateThreat = attacker != null && sensors != null &&
                               FlatDistance(transform.position, attacker.transform.position) <=
                               closeEnemyDistance && sensors.HasLineOfSight(attacker);
        if (currentState == DefenderCombatState.Defusing &&
            objectiveManager != null && objectiveManager.ActiveDefuser == gameObject &&
            coordinator != null && !coordinator.BombTimerIsCritical &&
            (amount >= defuseDamageCancelThreshold || immediateThreat))
        {
            objectiveManager.CancelDefuse(gameObject);
        }

        forceReposition = true;
    }

    private void ResolveReferences()
    {
        if (coordinator == null)
        {
            coordinator = DefenderTeamCoordinator.Instance;
        }

        if (roundManager == null)
        {
            roundManager = RoundManager.Instance;
        }

        if (objectiveManager == null)
        {
            objectiveManager = ObjectiveManager.Instance;
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static float FlatDistanceToSegment(Vector3 point, Vector3 segmentStart,
        Vector3 segmentEnd)
    {
        point.y = 0f;
        segmentStart.y = 0f;
        segmentEnd.y = 0f;
        Vector3 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 0.001f)
        {
            return Vector3.Distance(point, segmentStart);
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - segmentStart, segment) /
                                lengthSquared);
        Vector3 closest = segmentStart + segment * t;
        return Vector3.Distance(point, closest);
    }

    private void OnDrawGizmos()
    {
        if (!drawCombatGizmos)
        {
            return;
        }

        if (currentState == DefenderCombatState.PursuingLastKnownEnemy ||
            currentState == DefenderCombatState.SearchingLastKnownArea)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(lastKnownTargetPosition + Vector3.up * 0.2f, 0.45f);
            Gizmos.DrawLine(
                transform.position + Vector3.up * 0.2f,
                lastKnownTargetPosition + Vector3.up * 0.2f);
        }

        if (currentCover.cover == null)
        {
            return;
        }

        Gizmos.color = new Color(0.15f, 0.55f, 1f, 0.9f);
        Gizmos.DrawWireSphere(currentCover.hiddenPosition + Vector3.up * 0.15f, 0.35f);

        if (!hasValidPeekPosition)
        {
            return;
        }

        bool blocked = DefenderTeamCoordinator.IsLineBlocked(
            currentCover.peekPosition,
            lastKnownTargetPosition);
        Gizmos.color = blocked ? Color.red : Color.green;
        Gizmos.DrawWireSphere(currentCover.peekPosition + Vector3.up * 0.15f, 0.3f);
        Gizmos.DrawLine(
            currentCover.peekPosition + Vector3.up * 0.8f,
            lastKnownTargetPosition + Vector3.up * 0.8f);
    }
}
