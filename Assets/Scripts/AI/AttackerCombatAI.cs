using UnityEngine;

public enum AttackerCombatState
{
    SearchingTarget,
    Shooting,
    MovingToCover,
    InCover,
    Peeking,
    Hiding,
    Repositioning,
    AdvancingObjective
}

/// <summary>
/// Combat layer for the red attacking team. It yields to objective/tactic movement
/// when no fight is active and uses cover only after taking a clean shooting chance.
/// </summary>
[DisallowMultipleComponent]
public sealed class AttackerCombatAI : MonoBehaviour
{
    [SerializeField] private float targetMemoryDuration = 4f;
    [SerializeField] private float hideDuration = 0.5f;
    [SerializeField] private float peekDuration = 0.6f;
    [SerializeField] private float shootBurstDuration = 0.8f;
    [SerializeField] private float maxCoverIdleTime = 1.5f;
    [SerializeField] private float maxNoProgressTime = 4f;
    [SerializeField] private float forcedPeekInterval = 1f;
    [SerializeField] private float forcedRepositionInterval = 3f;
    [SerializeField] private float closeEnemyDistance = 4f;
    [SerializeField] private float objectiveUrgencyIncreaseRate = 1.5f;
    [SerializeField] private float maxOpenFireDuration = 1.25f;
    [SerializeField] private float peekOffset = 1.25f;
    [SerializeField] private float closeCombatDistance = 3f;
    [SerializeField] private float coverSearchRadius = 11f;
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.3f;

    private DefenderTeamCoordinator coverService;
    private RoundManager roundManager;
    private ObjectiveManager objectiveManager;
    private TeamTacticManager tacticManager;
    private AgentStats stats;
    private AgentSensors sensors;
    private AgentMotor motor;
    private WeaponSystem weapon;
    private HealthSystem health;
    private AgentMemory memory;

    [SerializeField] private AttackerCombatState currentState =
        AttackerCombatState.SearchingTarget;
    private DefenderCoverSolution currentCover;
    [SerializeField] private GameObject rememberedTarget;
    [SerializeField] private Vector3 lastKnownPosition;
    private float lastSeenTime = Mathf.NegativeInfinity;
    private float stateUntil;
    private float coverCycleStartedAt;
    private float openFireStartedAt = Mathf.NegativeInfinity;
    private float recentlyDamagedUntil;
    private bool hasValidPeek;
    private bool forceNewCover;
    private bool hideTimerStarted;
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

    public AttackerCombatState CurrentState => currentState;
    public Transform CurrentCover => currentCover.cover;
    public bool IsForcedToRetreat => health != null &&
                                     (health.NormalizedHealth <= lowHealthThreshold ||
                                      Time.time <= recentlyDamagedUntil);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAttackerComponents()
    {
        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        foreach (AgentStats agent in agents)
        {
            if (agent.team == TeamType.Red && agent.GetComponent<AttackerCombatAI>() == null)
            {
                agent.gameObject.AddComponent<AttackerCombatAI>();
            }
        }
    }

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        sensors = GetComponent<AgentSensors>();
        motor = GetComponent<AgentMotor>();
        weapon = GetComponent<WeaponSystem>();
        health = GetComponent<HealthSystem>();
        memory = GetComponent<AgentMemory>();
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
        ResolveReferences();
        if (roundManager == null || tacticManager == null || stats == null ||
            sensors == null || motor == null || health == null || health.IsDead ||
            stats.team != tacticManager.ControlledTeam ||
            roundManager.CurrentState == RoundState.Preparation ||
            roundManager.CurrentState == RoundState.RoundEnd)
        {
            return false;
        }

        UpdateProgressDebug();

        if (visibleTarget != null && sensors.CanDetect(visibleTarget))
        {
            if (rememberedTarget != visibleTarget)
            {
                openFireStartedAt = Time.time;
            }

            rememberedTarget = visibleTarget;
            lastKnownPosition = visibleTarget.transform.position;
            lastSeenTime = Time.time;
            memory?.ObserveEnemy(visibleTarget);
            return ExecuteCombat(visibleTarget, lastKnownPosition, true);
        }

        if (rememberedTarget != null && Time.time <= lastSeenTime + targetMemoryDuration &&
            currentCover.cover != null)
        {
            return ExecuteCombat(null, lastKnownPosition, false);
        }

        rememberedTarget = null;
        currentCover = default;
        currentState = AttackerCombatState.AdvancingObjective;
        return false;
    }

    private void UpdateProgressDebug()
    {
        if (FlatDistance(transform.position, lastMovementSample) >= 0.35f)
        {
            lastMovementSample = transform.position;
            lastMovementTime = Time.time;
        }

        Vector3 objective = GetObjectivePosition();
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
            ? "Hold post-plant and deny defuse"
            : tacticManager.HasSelectedInitialTactic
                ? "Execute " + tacticManager.GetSelectedInitialTactic()
                : "Enter site / plant";
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
        bool visible,
        out bool actionResult)
    {
        actionResult = false;
        float targetDistance = FlatDistance(transform.position, targetPosition);
        if (visible && target != null && weapon != null && weapon.IsReady &&
            targetDistance <= stats.attackRange && sensors.HasLineOfSight(target))
        {
            return false;
        }

        float lastShot = weapon != null ? weapon.LastShotTime : Mathf.NegativeInfinity;
        float noProgressDuration = Time.time - Mathf.Max(
            lastObjectiveProgressTime,
            lastShot);
        float activeThreshold = IsAggressiveTactic()
            ? maxNoProgressTime / Mathf.Max(1f, objectiveUrgencyIncreaseRate)
            : maxNoProgressTime;

        if (!visible && targetDistance <= closeEnemyDistance &&
            Time.time >= nextForcedRepositionTime)
        {
            nextForcedRepositionTime = Time.time + forcedRepositionInterval;
            currentReasonDebug = "Close remembered enemy has no firing angle";
            Debug.Log("Anti-stalemate: forcing reposition");
            if (currentCover.cover != null)
            {
                RepositionNoPeek();
            }
            else
            {
                forceNewCover = true;
                currentState = AttackerCombatState.Repositioning;
            }
            actionResult = true;
            return true;
        }

        if (noProgressDuration < activeThreshold)
        {
            return false;
        }

        float objectiveUrgency = 1f +
            (noProgressDuration - activeThreshold) * objectiveUrgencyIncreaseRate;
        if (noProgressDuration >= activeThreshold + forcedRepositionInterval * 2f)
        {
            currentReasonDebug = $"Objective urgency {objectiveUrgency:0.0}";
            Debug.Log("Anti-stalemate: forcing objective push");
            currentCover = default;
            forceNewCover = false;
            hasValidPeek = false;
            currentState = AttackerCombatState.AdvancingObjective;
            lastObjectiveProgressTime = Time.time;
            actionResult = false;
            return true;
        }

        if (currentCover.cover != null &&
            noProgressDuration >= activeThreshold + forcedRepositionInterval &&
            Time.time >= nextForcedRepositionTime)
        {
            nextForcedRepositionTime = Time.time + forcedRepositionInterval;
            currentReasonDebug = "Cover angle produced no progress";
            Debug.Log("Anti-stalemate: forcing reposition");
            RepositionNoPeek();
            actionResult = true;
            return true;
        }

        bool canForcePeek = currentCover.cover != null &&
                            (currentState == AttackerCombatState.InCover ||
                             currentState == AttackerCombatState.Hiding);
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

    private bool ExecuteCombat(GameObject target, Vector3 targetPosition, bool visible)
    {
        if (TryBreakCombatStalemate(
                target,
                targetPosition,
                visible,
                out bool antiStalemateAction))
        {
            return antiStalemateAction;
        }

        float targetDistance = FlatDistance(transform.position, targetPosition);
        bool lowHealth = health.NormalizedHealth <= lowHealthThreshold;
        bool priorityTarget = target != null && objectiveManager != null &&
                              objectiveManager.ActiveDefuser == target;
        bool aggressiveTactic = IsAggressiveTactic();
        bool hasShot = visible && target != null && targetDistance <= stats.attackRange &&
                       sensors.HasLineOfSight(target);

        if (visible && target != null && targetDistance <= closeCombatDistance)
        {
            return ShootAndStrafe(target, targetPosition, true);
        }

        bool seriousDanger = lowHealth || Time.time <= recentlyDamagedUntil;
        if (hasShot &&
            (priorityTarget || (aggressiveTactic && !lowHealth) || !seriousDanger) &&
            (priorityTarget || aggressiveTactic ||
             Time.time <= openFireStartedAt + maxOpenFireDuration))
        {
            return ShootAndStrafe(target, targetPosition, false);
        }

        bool openExposureElapsed = hasShot &&
                                   Time.time > openFireStartedAt + maxOpenFireDuration;
        bool shouldUseCover = seriousDanger || currentCover.cover != null ||
                              forceNewCover || IsCoverOrientedTactic() ||
                              openExposureElapsed;
        if (!shouldUseCover)
        {
            currentState = AttackerCombatState.AdvancingObjective;
            return false;
        }

        if (currentCover.cover == null || forceNewCover)
        {
            Transform excluded = forceNewCover ? currentCover.cover : null;
            if (coverService != null && coverService.TryFindBestCover(
                    gameObject,
                    targetPosition,
                    GetObjectivePosition(),
                    coverSearchRadius,
                    out DefenderCoverSolution solution,
                    excluded))
            {
                currentCover = solution;
                currentState = forceNewCover
                    ? AttackerCombatState.Repositioning
                    : AttackerCombatState.MovingToCover;
                forceNewCover = false;
                hasValidPeek = false;
                Debug.Log(name + " moving to cover because exposed.");
            }
            else
            {
                currentCover = default;
                forceNewCover = false;
                return visible && target != null
                    ? ShootAndStrafe(target, targetPosition, false)
                    : false;
            }
        }

        switch (currentState)
        {
            case AttackerCombatState.MovingToCover:
            case AttackerCombatState.Repositioning:
                motor.SpeedMultiplier = 1.1f;
                if (MoveTo(currentCover.hiddenPosition, 0.65f))
                {
                    if (hasShot)
                    {
                        Shoot(target);
                    }
                    return true;
                }

                motor.Stop();
                motor.FacePosition(targetPosition);
                currentState = AttackerCombatState.InCover;
                coverCycleStartedAt = Time.time;
                stateUntil = Time.time + hideDuration;
                Debug.Log(name + " reached cover, preparing peek.");
                return true;

            case AttackerCombatState.InCover:
                motor.Stop();
                motor.FacePosition(targetPosition);
                if (Time.time >= stateUntil ||
                    Time.time >= coverCycleStartedAt + maxCoverIdleTime)
                {
                    BeginPeekOrReposition(targetPosition);
                }
                return true;

            case AttackerCombatState.Peeking:
                motor.FacePosition(targetPosition);
                if (!hasValidPeek || DefenderTeamCoordinator.IsLineBlocked(
                        currentCover.peekPosition,
                        targetPosition))
                {
                    RepositionNoPeek();
                    return true;
                }

                MoveTo(currentCover.peekPosition, 0.4f);
                if (visible && target != null && Shoot(target))
                {
                    currentState = AttackerCombatState.Shooting;
                    stateUntil = Time.time + shootBurstDuration;
                }
                else if (Time.time >= stateUntil)
                {
                    ReturnToCover();
                }
                return true;

            case AttackerCombatState.Shooting:
                motor.FacePosition(targetPosition);
                if (visible && target != null)
                {
                    Shoot(target);
                }

                if (!visible || Time.time >= stateUntil)
                {
                    ReturnToCover();
                }
                return true;

            case AttackerCombatState.Hiding:
                motor.FacePosition(targetPosition);
                if (MoveTo(currentCover.hiddenPosition, 0.5f))
                {
                    return true;
                }

                motor.Stop();
                if (!hideTimerStarted)
                {
                    hideTimerStarted = true;
                    coverCycleStartedAt = Time.time;
                    stateUntil = Time.time + hideDuration;
                }

                if (Time.time >= stateUntil ||
                    Time.time >= coverCycleStartedAt + maxCoverIdleTime)
                {
                    BeginPeekOrReposition(targetPosition);
                }
                return true;

            default:
                currentState = AttackerCombatState.MovingToCover;
                return true;
        }
    }

    private bool ShootAndStrafe(GameObject target, Vector3 targetPosition, bool close)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized);
        motor.SpeedMultiplier = close ? 1f : 0.65f;
        motor.MoveTo(transform.position + side * (close ? 1.1f : 0.45f));
        motor.FacePosition(targetPosition);
        Shoot(target);
        currentState = AttackerCombatState.Shooting;
        return true;
    }

    private bool Shoot(GameObject target)
    {
        if (target == null || weapon == null ||
            FlatDistance(transform.position, target.transform.position) > stats.attackRange ||
            !sensors.HasLineOfSight(target))
        {
            return false;
        }

        bool ready = weapon.IsReady;
        weapon.TryAttack(target);
        if (ready)
        {
            Debug.Log(name + " shooting visible enemy " + target.name + ".");
        }
        return true;
    }

    private void BeginPeekOrReposition(Vector3 targetPosition)
    {
        if (!TryCalculatePeek(targetPosition, out Vector3 peek))
        {
            Debug.Log("Agent cannot find peek angle, repositioning");
            RepositionNoPeek();
            return;
        }

        currentCover.peekPosition = peek;
        hasValidPeek = true;
        hideTimerStarted = false;
        currentState = AttackerCombatState.Peeking;
        stateUntil = Time.time + peekDuration;
        motor.MoveTo(peek);
        motor.FacePosition(targetPosition);
        Debug.Log(name + " peeking from cover.");
    }

    private bool TryCalculatePeek(Vector3 targetPosition, out Vector3 peek)
    {
        peek = default;
        if (currentCover.cover == null)
        {
            return false;
        }

        Vector3 toTarget = targetPosition - currentCover.hiddenPosition;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f)
        {
            return false;
        }

        toTarget.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toTarget).normalized;
        Collider coverCollider = currentCover.cover.GetComponent<Collider>();
        float offset = peekOffset;
        if (coverCollider != null)
        {
            offset = Mathf.Abs(side.x) * coverCollider.bounds.extents.x +
                     Mathf.Abs(side.z) * coverCollider.bounds.extents.z + 0.65f;
        }

        Vector3 left = currentCover.hiddenPosition + side * offset + toTarget * 0.35f;
        Vector3 right = currentCover.hiddenPosition - side * offset + toTarget * 0.35f;
        bool leftClear = IsValidPeek(left, targetPosition);
        bool rightClear = IsValidPeek(right, targetPosition);
        if (!leftClear && !rightClear)
        {
            return false;
        }

        peek = leftClear && rightClear
            ? FlatDistance(transform.position, left) <= FlatDistance(transform.position, right)
                ? left
                : right
            : leftClear ? left : right;
        return true;
    }

    private bool IsValidPeek(Vector3 candidate, Vector3 targetPosition)
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
        currentState = AttackerCombatState.Hiding;
        hasValidPeek = false;
        hideTimerStarted = false;
        motor.MoveTo(currentCover.hiddenPosition);
        Debug.Log(name + " returning to cover.");
    }

    private void RepositionNoPeek()
    {
        Debug.Log(name + " repositioning because no peek angle.");
        currentState = AttackerCombatState.Repositioning;
        forceNewCover = true;
        hasValidPeek = false;
        hideTimerStarted = false;
        motor.Stop();
    }

    private bool IsAggressiveTactic()
    {
        if (tacticManager == null || !tacticManager.HasSelectedInitialTactic)
        {
            return false;
        }

        InitialTeamTactic tactic = tacticManager.GetSelectedInitialTactic();
        return tactic == InitialTeamTactic.FastExecute ||
               tactic == InitialTeamTactic.FeintAndRotate ||
               tactic == InitialTeamTactic.SplitPush;
    }

    private bool IsCoverOrientedTactic()
    {
        return tacticManager != null &&
               (tacticManager.IsMidRoundTacticActive(MidRoundTactic.GuerrillaAmbush) ||
                tacticManager.IsMidRoundTacticActive(MidRoundTactic.PostPlantLockdown) ||
                tacticManager.IsMidRoundTacticActive(MidRoundTactic.ProbeAndPlant));
    }

    private Vector3 GetObjectivePosition()
    {
        if (objectiveManager != null && objectiveManager.ActiveBomb != null &&
            roundManager.CurrentState == RoundState.BombPlanted)
        {
            return objectiveManager.ActiveBomb.transform.position;
        }

        TeamTacticExecutor executor = tacticManager != null
            ? tacticManager.GetComponent<TeamTacticExecutor>()
            : null;
        if (executor != null && executor.TargetSite != null)
        {
            return executor.TargetSite.PlantPosition;
        }

        return objectiveManager != null && objectiveManager.SelectedAttackSite != null
            ? objectiveManager.SelectedAttackSite.PlantPosition
            : transform.position;
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

    private void OnDamaged(HealthSystem damaged, float amount, GameObject attacker)
    {
        recentlyDamagedUntil = Time.time + 1.5f;
    }

    private void ResolveReferences()
    {
        coverService ??= DefenderTeamCoordinator.Instance;
        roundManager ??= RoundManager.Instance;
        objectiveManager ??= ObjectiveManager.Instance;
        tacticManager ??= TeamTacticManager.Instance;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
