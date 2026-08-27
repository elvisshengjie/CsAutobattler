using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Executes movement only. It does not select enemies or decide what action to take.
/// </summary>
public class AgentMotor : MonoBehaviour
{
    private const float ExactObjectiveReachDistance = 0.08f;

    [Header("Path Following")]
    public float pathRefreshTime = 0.3f;
    public float waypointReachDistance = 0.3f;
    public float rotationSpeed = 720f;
    [HideInInspector] public float separationRadius = 0.8f;
    [HideInInspector] public float separationStrength = 2f;

    [Header("Optional Multi-Level Surfaces")]
    [Tooltip("When non-empty, the agent follows ramps and platforms on these layers. Leave empty for flat maps.")]
    [SerializeField] private LayerMask walkableSurfaceMask;
    [SerializeField] private float surfaceProbeHeight = 6f;
    [SerializeField] private float surfaceProbeDistance = 12f;
    [SerializeField] private float maximumSurfaceStep = 0.6f;

    [Header("Agent Size and Clearance")]
    [SerializeField] private float agentRadius = 0.4f;
    [FormerlySerializedAs("obstacleClearance")]
    [SerializeField] private float minObstacleClearance = 0.15f;
    [SerializeField] private float minAgentSeparation = 0.8f;
    [SerializeField] private float targetArrivalDistance = 0.25f;
    [SerializeField] private float maximumTargetAdjustment = 4f;
    [SerializeField] private float reservationSearchRadius = 4f;

    [Header("Local Avoidance")]
    [SerializeField] private float separationWeight = 2f;
    [SerializeField] private float maxSeparationContribution = 0.45f;
    [SerializeField] private float separationSmoothing = 8f;
    [SerializeField] private float minimumCrowdSpeedFactor = 0.18f;
    [SerializeField] private float avoidanceDistance = 0.8f;
    [SerializeField] private float avoidanceStrength = 1.5f;
    [SerializeField] private float avoidanceSideHoldTime = 0.45f;
    [Tooltip("Extra clearance required before leaving wall-escape steering. Prevents boundary chatter.")]
    [SerializeField] private float clearanceEscapeReleaseMargin = 0.12f;
    [SerializeField] private float clearanceNormalSmoothing = 10f;

    [Header("Target Stability")]
    [SerializeField] private float minTargetSwitchInterval = 0.75f;
    [Tooltip("A target this far from the current request is a new objective and switches immediately.")]
    [SerializeField] private float targetSwitchScoreMargin = 2f;

    [Header("Stuck Recovery")]
    [SerializeField] private float stuckDistanceThreshold = 0.05f;
    [Tooltip("Emergency-only delay; tactical slots and steering handle normal congestion.")]
    [SerializeField] private float stuckTimeThreshold = 3f;
    [SerializeField] private float hardStuckTimeThreshold = 7f;
    [SerializeField] private float recoveryWaypointDistance = 1.25f;
    [SerializeField] private float emergencySnapDistance = 2f;
    [SerializeField] private bool drawMovementGizmos = true;
    [SerializeField] private bool showRuntimeDebugLabel = false;
    [SerializeField] private bool logDiagnosticStateChanges = true;

    [Header("Runtime Debug")]
    [SerializeField] private Vector3 requestedDestination;
    [SerializeField] private Vector3 lastPosition;
    [SerializeField] private float lastMoveProgressTime;
    [SerializeField] private float stuckTimer;
    [SerializeField] private bool isStuck;
    [SerializeField] private int recoveryAttempts;
    [SerializeField] private bool targetWasAdjusted;
    [SerializeField] private bool currentTargetValid;
    [SerializeField] private bool pathBlocked;
    [SerializeField] private TacticalSlotKind currentSlotKind;
    [SerializeField] private string pathStatusDebug;
    [SerializeField] private string inactivityReasonDebug;
    [SerializeField] private string objectiveDebug;
    [SerializeField] private string roleDebug;
    [SerializeField] private string combatDebug;
    [SerializeField] private bool enemyVisibleDebug;
    [SerializeField] private bool bombPlantedDebug;

    private readonly Collider[] nearbyColliders = new Collider[32];
    private readonly Collider[] nearbyObstacles = new Collider[24];
    private AgentStats stats;
    private Rigidbody body;
    private List<Vector3> currentPath;
    private int currentWaypointIndex;
    private float nextPathRefreshTime;
    private float nextRecoveryAttemptTime;
    private float recoveryResumeTime;
    private float lastTargetSwitchTime;
    private float avoidanceSideUntil;
    private float nextReservationWarningTime;
    private float nextInvalidTargetLogTime;
    private float nextDiagnosticUpdateTime;
    private float nextDiagnosticLogTime;
    private float nextSlotRetryTime;
    private int avoidanceSide;
    private Vector3 destination;
    private Vector3 pendingDestination;
    private Vector3 lastAvoidanceDirection;
    private Vector3 smoothedSeparation;
    private bool hasDestination;
    private bool hasPendingDestination;
    private bool hasReservedSlot;
    private bool exactObjectiveMovement;
    private bool recoveringLocally;
    private bool hasResolvedRequest;
    private bool clearanceEscapeActive;
    private Vector3 smoothedClearanceAwayDirection;
    private string currentTargetDebug;
    private string lastDiagnosticSignature;

    public bool HasDestination => hasDestination;
    public Vector3 Destination => destination;
    public Vector3 RequestedDestination => requestedDestination;
    public bool IsStuck => isStuck;
    public float AgentRadius => agentRadius;
    public float MinObstacleClearance => minObstacleClearance;
    public float MinAgentSeparation => minAgentSeparation;
    public bool CurrentTargetValid => currentTargetValid;
    public bool PathBlocked => pathBlocked;
    public TacticalSlotKind CurrentSlotKind => currentSlotKind;
    public float SpeedMultiplier { get; set; } = 1f;
    private float movementPlaneY;
    private float surfaceOffset = 1f;

    private float ClearanceRadius => agentRadius + minObstacleClearance;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        body = GetComponent<Rigidbody>();
        movementPlaneY = body != null ? body.position.y : transform.position.y;
        RefreshSurfaceOffset();
        Collider agentCollider = GetComponent<Collider>();
        if (agentCollider != null)
        {
            float colliderRadius = Mathf.Min(
                agentCollider.bounds.extents.x,
                agentCollider.bounds.extents.z);
            agentRadius = Mathf.Max(agentRadius, colliderRadius);
        }

        minAgentSeparation = Mathf.Max(minAgentSeparation, agentRadius * 2f);
        minObstacleClearance = Mathf.Max(minObstacleClearance, 0.15f);
        clearanceEscapeReleaseMargin = Mathf.Max(
            0.05f,
            clearanceEscapeReleaseMargin);
        clearanceNormalSmoothing = Mathf.Max(1f, clearanceNormalSmoothing);
        targetArrivalDistance = Mathf.Max(0.05f, targetArrivalDistance);
        // Older scene instances serialized the previous 1.2/3 second values.
        // Keep emergency recovery genuinely secondary to slot steering.
        stuckTimeThreshold = Mathf.Max(stuckTimeThreshold, 3f);
        hardStuckTimeThreshold = Mathf.Max(
            hardStuckTimeThreshold,
            stuckTimeThreshold + 4f);
        PositionReservationManager.EnsureInstance();
        lastPosition = transform.position;
        lastMoveProgressTime = Time.time;
    }

    private void Update()
    {
        if (!hasDestination)
        {
            ResetStuckTracking();
            UpdateDiagnostics();
            return;
        }

        if (recoveringLocally &&
            (HasReachedDestination(targetArrivalDistance + 0.1f) ||
             Time.time >= recoveryResumeTime))
        {
            recoveringLocally = false;
            ApplyValidatedDestination(
                requestedDestination,
                true,
                false,
                exactObjectiveMovement);
        }

        if (!recoveringLocally && hasPendingDestination &&
            Time.time >= lastTargetSwitchTime + minTargetSwitchInterval)
        {
            Vector3 next = pendingDestination;
            hasPendingDestination = false;
            ApplyValidatedDestination(next, false);
        }

        if (!recoveringLocally && !exactObjectiveMovement &&
            !hasReservedSlot && hasDestination &&
            Time.time >= nextSlotRetryTime)
        {
            ApplyValidatedDestination(requestedDestination, true);
            if (!hasDestination)
            {
                UpdateDiagnostics();
                return;
            }
        }

        UpdateStuckDetection();
        if (!hasDestination)
        {
            UpdateDiagnostics();
            return;
        }

        if (Time.time >= nextPathRefreshTime)
        {
            AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
            PositionReservationManager reservationManager =
                PositionReservationManager.Instance;
            bool reservationValid = !hasReservedSlot ||
                                    (reservationManager != null &&
                                     reservationManager.IsReservationValid(this));
            currentTargetValid = pathfinder == null ||
                                 pathfinder.IsValidAgentPosition(
                                     destination,
                                     ClearanceRadius,
                                     gameObject,
                                     false);
            currentTargetValid &= reservationValid;
            if (currentTargetValid)
            {
                RefreshPath();
                if (currentPath == null &&
                    !HasReachedDestination(targetArrivalDistance + 0.1f))
                {
                    currentTargetValid = false;
                    ApplyValidatedDestination(
                        requestedDestination,
                        true,
                        true,
                        exactObjectiveMovement);
                }
            }
            else
            {
                ApplyValidatedDestination(
                    requestedDestination,
                    true,
                    true,
                    exactObjectiveMovement);
            }
            nextPathRefreshTime = Time.time + pathRefreshTime;
        }

        UpdateDiagnostics();
    }

    private void FixedUpdate()
    {
        // Flat maps retain the original fixed movement plane. Multi-level maps
        // update height from their authored walkable surfaces in FollowPath.
        if (body != null && walkableSurfaceMask.value == 0 &&
            !Mathf.Approximately(body.position.y, movementPlaneY))
        {
            Vector3 correctedPosition = body.position;
            correctedPosition.y = movementPlaneY;
            body.position = correctedPosition;
        }
        FollowPath();
    }

    public void MoveTo(Vector3 newDestination)
    {
        bool wasExactObjective = exactObjectiveMovement;
        exactObjectiveMovement = false;
        if (!hasDestination || wasExactObjective)
        {
            ApplyValidatedDestination(newDestination, false);
            return;
        }

        float change = FlatDistance(requestedDestination, newDestination);
        if (change <= Mathf.Max(0.1f, waypointReachDistance * 0.65f))
        {
            return;
        }

        if (recoveringLocally)
        {
            pendingDestination = newDestination;
            hasPendingDestination = true;
            return;
        }

        bool significantChange = change >= targetSwitchScoreMargin;
        bool intervalElapsed = Time.time >=
                               lastTargetSwitchTime + minTargetSwitchInterval;
        if (significantChange || intervalElapsed || !currentTargetValid || isStuck)
        {
            ApplyValidatedDestination(newDestination, false);
            return;
        }

        // Keep following the stable path and remember only the newest request.
        // This avoids switching between nearly identical targets every frame.
        pendingDestination = newDestination;
        hasPendingDestination = true;
    }

    public void ForceMoveTo(Vector3 newDestination)
    {
        exactObjectiveMovement = false;
        recoveringLocally = false;
        hasPendingDestination = false;
        ApplyValidatedDestination(newDestination, true);
    }

    /// <summary>
    /// Enables ramp and platform traversal for scenes that author dedicated
    /// walkable surfaces. Flat scenes keep the mask empty and preserve the
    /// original fixed-height movement.
    /// </summary>
    public void ConfigureWalkableSurfaceMask(LayerMask surfaceMask)
    {
        walkableSurfaceMask = surfaceMask;
        RefreshSurfaceOffset();
    }

    /// <summary>
    /// Moves to an interaction point without shifting it into a formation slot.
    /// Use this for exact-range objectives such as bomb defusing.
    /// </summary>
    public void MoveToExactObjective(Vector3 newDestination)
    {
        bool modeChanged = !exactObjectiveMovement;
        float change = hasDestination
            ? FlatDistance(requestedDestination, newDestination)
            : Mathf.Infinity;
        exactObjectiveMovement = true;
        recoveringLocally = false;
        hasPendingDestination = false;
        if (!hasDestination || modeChanged || change > 0.1f ||
            !currentTargetValid || isStuck)
        {
            ApplyValidatedDestination(newDestination, true, false, true);
        }
    }

    public bool HasReachedDestination(float tolerance)
    {
        return hasDestination && FlatDistance(transform.position, destination) <=
               GetEffectiveArrivalTolerance(tolerance);
    }

    public bool HasReachedRequestedDestination(Vector3 request, float tolerance)
    {
        return hasResolvedRequest &&
               FlatDistance(requestedDestination, request) <= waypointReachDistance &&
               FlatDistance(transform.position, destination) <=
               GetEffectiveArrivalTolerance(tolerance);
    }

    private float GetEffectiveArrivalTolerance(float requestedTolerance)
    {
        if (exactObjectiveMovement)
        {
            return Mathf.Min(
                ExactObjectiveReachDistance,
                Mathf.Max(0.02f, requestedTolerance));
        }

        float tolerance = Mathf.Max(targetArrivalDistance, requestedTolerance);
        return hasReservedSlot
            ? Mathf.Min(tolerance, targetArrivalDistance)
            : tolerance;
    }

    public void ForceRepath()
    {
        if (!hasDestination)
        {
            return;
        }

        currentPath = null;
        currentWaypointIndex = 0;
        nextPathRefreshTime = 0f;
        Debug.Log("Repathing after stuck");
    }

    public void Stop()
    {
        exactObjectiveMovement = false;
        hasDestination = false;
        hasPendingDestination = false;
        recoveringLocally = false;
        currentPath = null;
        currentWaypointIndex = 0;
        smoothedSeparation = Vector3.zero;
        pathBlocked = false;
        clearanceEscapeActive = false;
        smoothedClearanceAwayDirection = Vector3.zero;
        avoidanceSide = 0;
        avoidanceSideUntil = 0f;
        SpeedMultiplier = 1f;
        ResetStuckTracking();
    }

    public void FacePosition(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    private void ApplyValidatedDestination(
        Vector3 requested,
        bool forced,
        bool forceSlotReassignment = false,
        bool bypassTacticalReservation = false)
    {
        if (bypassTacticalReservation)
        {
            PositionReservationManager.Instance?.Release(this);
            hasReservedSlot = false;
            currentSlotKind = TacticalSlotKind.Center;
        }

        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        Vector3 resolved = requested;
        List<Vector3> resolvedPath = null;
        bool valid = pathfinder == null ||
                     pathfinder.TryGetNearestReachablePosition(
                         transform.position,
                         requested,
                         maximumTargetAdjustment,
                         ClearanceRadius,
                         out resolved,
                         out resolvedPath,
                         gameObject,
                         false);

        if (!valid && pathfinder != null)
        {
            valid = pathfinder.TryGetNearestWalkablePosition(
                transform.position,
                emergencySnapDistance,
                ClearanceRadius,
                out resolved,
                gameObject,
                false);
            if (valid)
            {
                resolvedPath = pathfinder.FindPath(transform.position, resolved);
            }
        }

        if (!valid)
        {
            Debug.LogWarning(name + " has no reachable movement target near " + requested + ".");
            currentPath = null;
            currentWaypointIndex = 0;
            hasDestination = false;
            hasResolvedRequest = false;
            currentTargetValid = false;
            return;
        }

        Vector3 validatedTarget = resolved;

        PositionReservationManager reservationManager = bypassTacticalReservation
            ? null
            : PositionReservationManager.EnsureInstance();
        Vector3 reserved = resolved;
        TacticalSlotKind reservedKind = TacticalSlotKind.Center;
        bool slotReserved = reservationManager != null &&
                            reservationManager.TryReserveTacticalSlot(
                                this,
                                resolved,
                                resolved - transform.position,
                                minAgentSeparation,
                                reservationSearchRadius,
                                forceSlotReassignment,
                                out reserved,
                                out reservedKind);
        if (slotReserved)
        {
            resolved = reserved;
            currentSlotKind = reservedKind;
            resolvedPath = pathfinder != null
                ? pathfinder.FindPath(transform.position, resolved)
                : new List<Vector3> { resolved };
            nextSlotRetryTime = Mathf.Infinity;
        }
        else if (reservationManager != null)
        {
            // A saturated formation must degrade to a reachable unreserved
            // endpoint, not erase the objective and leave the agent idle.
            currentSlotKind = TacticalSlotKind.Extended;
            nextSlotRetryTime = Time.time + minTargetSwitchInterval;
            if (Time.time >= nextReservationWarningTime)
            {
                nextReservationWarningTime = Time.time + 2f;
                Debug.LogWarning(
                    name + " found no free tactical slot; using reachable fallback.");
            }
        }

        targetWasAdjusted = FlatDistance(requested, resolved) > 0.1f;
        bool validationAdjusted = FlatDistance(requested, validatedTarget) > 0.1f;
        if (validationAdjusted && !forced && Time.time >= nextInvalidTargetLogTime)
        {
            nextInvalidTargetLogTime = Time.time + 2f;
            Debug.Log(
                $"Adjusted invalid target for {name}: {requested} -> {validatedTarget}");
        }

        requestedDestination = requested;
        destination = resolved;
        hasDestination = true;
        hasResolvedRequest = true;
        hasReservedSlot = slotReserved;
        currentTargetValid = true;
        currentPath = resolvedPath;
        currentWaypointIndex = 0;
        lastTargetSwitchTime = Time.time;
        hasPendingDestination = false;
        pathBlocked = false;
        nextPathRefreshTime = Time.time + pathRefreshTime;
        if (currentPath != null && currentPath.Count == 0 &&
            FlatDistance(transform.position, destination) > targetArrivalDistance)
        {
            currentPath.Add(destination);
        }
    }

    private void RefreshPath()
    {
        if (!hasDestination || recoveringLocally)
        {
            return;
        }

        if (AStarPathfinder3D.Instance == null)
        {
            currentPath = new List<Vector3> { destination };
            currentWaypointIndex = 0;
            return;
        }

        currentPath = AStarPathfinder3D.Instance.FindPath(transform.position, destination);
        currentWaypointIndex = 0;
        if (currentPath != null && currentPath.Count == 0 &&
            FlatDistance(transform.position, destination) > targetArrivalDistance)
        {
            currentPath.Add(destination);
        }
    }

    private void UpdateStuckDetection()
    {
        if (HasReachedDestination(targetArrivalDistance + 0.1f))
        {
            ResetStuckTracking();
            return;
        }

        float distanceMoved = FlatDistance(transform.position, lastPosition);
        if (distanceMoved >= stuckDistanceThreshold)
        {
            lastPosition = transform.position;
            lastMoveProgressTime = Time.time;
            stuckTimer = 0f;
            isStuck = false;
            recoveryAttempts = 0;
            return;
        }

        stuckTimer = Time.time - lastMoveProgressTime;
        if (stuckTimer < stuckTimeThreshold || Time.time < nextRecoveryAttemptTime)
        {
            return;
        }

        isStuck = true;
        HandleStuckAgent(distanceMoved);
    }

    private void HandleStuckAgent(float distanceMoved)
    {
        recoveryAttempts++;
        nextRecoveryAttemptTime = Time.time + 0.55f;
        string state = GetCurrentAIState();
        string tactic = TeamTacticManager.Instance != null &&
                        TeamTacticManager.Instance.HasSelectedInitialTactic
            ? TeamTacticManager.Instance.GetSelectedInitialTactic().ToString()
            : "None";
        Debug.Log(
            $"Agent stuck detected: {name}, team {stats?.team}, state {state}, " +
            $"target {requestedDestination}, tactic {tactic}, moved {distanceMoved:0.000}, " +
            $"stuck {stuckTimer:0.00}s");
        Debug.Log("Trying unstuck movement");

        if (recoveryAttempts == 1)
        {
            Vector3 previousSlot = destination;
            ApplyValidatedDestination(
                requestedDestination,
                true,
                true,
                exactObjectiveMovement);
            if (hasDestination && FlatDistance(previousSlot, destination) > 0.1f)
            {
                Debug.Log("Reassigned tactical slot after prolonged blockage");
            }
            return;
        }

        RefreshPath();
        Debug.Log("Repathing after stuck");
        if (stuckTimer >= hardStuckTimeThreshold)
        {
            EmergencyUnstuck();
            return;
        }

        if (recoveryAttempts >= 2 || currentPath == null || currentPath.Count == 0)
        {
            BeginLocalRecovery();
        }
    }

    private void BeginLocalRecovery()
    {
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder == null)
        {
            return;
        }

        Vector3 towardGoal = requestedDestination - transform.position;
        towardGoal.y = 0f;
        if (towardGoal.sqrMagnitude < 0.01f)
        {
            towardGoal = transform.forward;
        }
        towardGoal.Normalize();

        Vector3 bestPosition = default;
        List<Vector3> bestPath = null;
        float bestScore = Mathf.NegativeInfinity;
        for (int i = 0; i < 12; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, i * 30f, 0f) * towardGoal;
            Vector3 sample = transform.position + direction * recoveryWaypointDistance;
            if (!pathfinder.TryGetNearestReachablePosition(
                    transform.position,
                    sample,
                    0.65f,
                    ClearanceRadius,
                    out Vector3 candidate,
                    out List<Vector3> candidatePath,
                    gameObject) ||
                FlatDistance(transform.position, candidate) < 0.45f)
            {
                continue;
            }

            float score = Vector3.Dot(direction.normalized, towardGoal) -
                          FlatDistance(candidate, sample) * 0.25f;
            if (score > bestScore)
            {
                bestScore = score;
                bestPosition = candidate;
                bestPath = candidatePath;
            }
        }

        if (bestPath == null)
        {
            return;
        }

        recoveringLocally = true;
        recoveryResumeTime = Time.time + 1.25f;
        destination = bestPosition;
        currentPath = bestPath;
        currentWaypointIndex = 0;
    }

    private void EmergencyUnstuck()
    {
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder == null || body == null ||
            !pathfinder.TryGetNearestWalkablePosition(
                transform.position,
                emergencySnapDistance,
                ClearanceRadius,
                out Vector3 safePosition,
                gameObject))
        {
            return;
        }

        safePosition.y = body.position.y;
        if (TryGetSurfaceY(safePosition, out float safeSurfaceY))
        {
            safePosition.y = safeSurfaceY;
        }
        body.position = safePosition;
        transform.position = safePosition;
        lastPosition = safePosition;
        lastMoveProgressTime = Time.time;
        stuckTimer = 0f;
        isStuck = false;
        recoveryAttempts = 0;
        recoveringLocally = false;
        Debug.Log("Emergency nearest valid position used");
        ApplyValidatedDestination(
            requestedDestination,
            true,
            false,
            exactObjectiveMovement);
    }

    private void FollowPath()
    {
        if (body == null || stats == null || currentPath == null || currentPath.Count == 0 ||
            currentWaypointIndex >= currentPath.Count)
        {
            return;
        }

        Vector3 currentPosition = body.position;
        Vector3 targetWaypoint = currentPath[currentWaypointIndex];
        targetWaypoint.y = currentPosition.y;

        bool isFinalWaypoint = currentWaypointIndex == currentPath.Count - 1;
        float waypointTolerance = exactObjectiveMovement && isFinalWaypoint
            ? ExactObjectiveReachDistance
            : waypointReachDistance;
        if (FlatDistance(currentPosition, targetWaypoint) <= waypointTolerance)
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= currentPath.Count)
            {
                return;
            }

            targetWaypoint = currentPath[currentWaypointIndex];
            targetWaypoint.y = currentPosition.y;
        }

        Vector3 moveDirection = targetWaypoint - currentPosition;
        moveDirection.y = 0f;
        if (moveDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Vector3 targetSeparation = Vector3.ClampMagnitude(
            GetSeparationDirection() * separationWeight,
            maxSeparationContribution);
        float separationBlend = 1f - Mathf.Exp(
            -Mathf.Max(0.01f, separationSmoothing) * Time.fixedDeltaTime);
        smoothedSeparation = Vector3.Lerp(
            smoothedSeparation,
            targetSeparation,
            separationBlend);
        Vector3 desiredDirection = moveDirection.normalized + smoothedSeparation;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        desiredDirection.Normalize();
        WeaponLoadout loadout = GetComponent<WeaponLoadout>();
        float weaponMovementMultiplier = loadout != null
            ? loadout.MovementSpeedMultiplier : 1f;
        float baseStepDistance = stats.moveSpeed * weaponMovementMultiplier *
                                 SpeedMultiplier * Time.fixedDeltaTime;
        Vector3 safeDirection = GetCollisionSafeDirection(
            currentPosition,
            desiredDirection,
            baseStepDistance);
        if (safeDirection.sqrMagnitude <= 0.001f)
        {
            pathBlocked = true;
            return;
        }

        float stepDistance = baseStepDistance * GetCrowdSpeedScale(
            currentPosition,
            safeDirection,
            baseStepDistance);
        if (stepDistance <= 0.001f)
        {
            pathBlocked = true;
            return;
        }

        pathBlocked = false;
        Quaternion movementRotation = Quaternion.LookRotation(safeDirection, Vector3.up);
        body.MoveRotation(Quaternion.RotateTowards(
            body.rotation,
            movementRotation,
            rotationSpeed * Time.fixedDeltaTime));

        Vector3 newPosition = currentPosition + safeDirection * stepDistance;
        if (TryGetSurfaceY(newPosition, out float surfaceY))
        {
            newPosition.y = surfaceY;
        }
        else
        {
            newPosition.y = walkableSurfaceMask.value == 0
                ? movementPlaneY
                : currentPosition.y;
        }
        body.MovePosition(newPosition);
    }

    private void RefreshSurfaceOffset()
    {
        surfaceProbeHeight = Mathf.Max(1f, surfaceProbeHeight);
        surfaceProbeDistance = Mathf.Max(surfaceProbeHeight + 1f, surfaceProbeDistance);
        maximumSurfaceStep = Mathf.Max(0.1f, maximumSurfaceStep);
        if (walkableSurfaceMask.value == 0)
        {
            surfaceOffset = 1f;
            return;
        }

        Vector3 position = body != null ? body.position : transform.position;
        Vector3 origin = position + Vector3.up * surfaceProbeHeight;
        if (Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                surfaceProbeDistance,
                walkableSurfaceMask,
                QueryTriggerInteraction.Ignore))
        {
            surfaceOffset = Mathf.Max(0.1f, position.y - hit.point.y);
        }
    }

    private bool TryGetSurfaceY(Vector3 position, out float surfaceY)
    {
        surfaceY = position.y;
        if (walkableSurfaceMask.value == 0)
        {
            return false;
        }

        float currentY = body != null ? body.position.y : transform.position.y;
        Vector3 origin = new Vector3(
            position.x,
            currentY + surfaceProbeHeight,
            position.z);
        if (!Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                surfaceProbeDistance,
                walkableSurfaceMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        float desiredY = hit.point.y + surfaceOffset;
        if (Mathf.Abs(desiredY - currentY) > maximumSurfaceStep)
        {
            return false;
        }

        surfaceY = desiredY;
        return true;
    }

    private Vector3 GetCollisionSafeDirection(
        Vector3 currentPosition,
        Vector3 desiredDirection,
        float stepDistance)
    {
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder == null)
        {
            return desiredDirection;
        }

        Vector3 origin = currentPosition + Vector3.up * 0.55f;
        // A sphere cast does not reliably report a collider that already
        // overlaps its starting sphere. Resolve that state first so an agent
        // that drifted inside the clearance band can move away from the wall
        // instead of having every candidate rejected.
        if (TryGetClearanceEscapeDirection(
                pathfinder,
                currentPosition,
                desiredDirection,
                stepDistance,
                out Vector3 clearanceEscape))
        {
            lastAvoidanceDirection = clearanceEscape;
            return clearanceEscape;
        }

        float castDistance = Mathf.Max(
            avoidanceDistance,
            stepDistance + minObstacleClearance);
        if (!Physics.SphereCast(
                origin,
                ClearanceRadius,
                desiredDirection,
                out RaycastHit forwardHit,
                castDistance,
                pathfinder.obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            Vector3 candidate = currentPosition + desiredDirection * stepDistance;
            lastAvoidanceDirection = desiredDirection;
            return IsLocalPositionClear(
                    pathfinder,
                    candidate,
                    ClearanceRadius)
                ? desiredDirection
                : TryGetClearanceEscapeDirection(
                    pathfinder,
                    currentPosition,
                    desiredDirection,
                    stepDistance,
                    out clearanceEscape)
                    ? clearanceEscape
                    : Vector3.zero;
        }

        // The two wall tangents are true slide directions. Blending the chosen
        // tangent and contact normal with the goal avoids repeatedly pushing
        // into the same collider at corners.
        Vector3 left = Vector3.Cross(Vector3.up, forwardHit.normal).normalized;
        Vector3 right = -left;
        if (left.sqrMagnitude < 0.01f)
        {
            left = Quaternion.Euler(0f, -45f, 0f) * desiredDirection;
            right = Quaternion.Euler(0f, 45f, 0f) * desiredDirection;
        }
        float leftClearance = GetObstacleClearance(
            origin,
            left,
            pathfinder.obstacleMask) +
            Mathf.Max(0f, Vector3.Dot(left, desiredDirection)) * avoidanceDistance;
        float rightClearance = GetObstacleClearance(
            origin,
            right,
            pathfinder.obstacleMask) +
            Mathf.Max(0f, Vector3.Dot(right, desiredDirection)) * avoidanceDistance;

        int selectedSide;
        if (Time.time < avoidanceSideUntil && avoidanceSide != 0)
        {
            selectedSide = avoidanceSide;
        }
        else
        {
            selectedSide = leftClearance >= rightClearance ? -1 : 1;
            avoidanceSide = selectedSide;
            avoidanceSideUntil = Time.time + avoidanceSideHoldTime;
        }

        Vector3 steering = selectedSide < 0 ? left : right;
        float selectedClearance = selectedSide < 0 ? leftClearance : rightClearance;
        float alternateClearance = selectedSide < 0 ? rightClearance : leftClearance;
        if (alternateClearance > selectedClearance + minObstacleClearance)
        {
            steering = selectedSide < 0 ? right : left;
            avoidanceSide = -selectedSide;
            avoidanceSideUntil = Time.time + avoidanceSideHoldTime;
        }

        Vector3 obstacleAvoidance = steering + forwardHit.normal * 0.75f;
        obstacleAvoidance.y = 0f;
        Vector3 steered = (desiredDirection +
                           obstacleAvoidance.normalized * avoidanceStrength).normalized;
        lastAvoidanceDirection = steered;
        if (IsMovementDirectionValid(pathfinder, currentPosition, steered, stepDistance))
        {
            return steered;
        }

        if (IsMovementDirectionValid(pathfinder, currentPosition, steering, stepDistance))
        {
            return steering;
        }

        Vector3 alternate = selectedSide < 0 ? right : left;
        if (IsMovementDirectionValid(pathfinder, currentPosition, alternate, stepDistance))
        {
            return alternate;
        }

        return TryGetClearanceEscapeDirection(
            pathfinder,
            currentPosition,
            desiredDirection,
            stepDistance,
            out clearanceEscape)
            ? clearanceEscape
            : Vector3.zero;
    }

    /// <summary>
    /// Finds an outward wall-slide when the agent is already closer to an
    /// obstacle than the configured clearance. These steps may begin inside
    /// the clearance band, but they must remain physically collision-free and
    /// may never reduce the current wall distance.
    /// </summary>
    private bool TryGetClearanceEscapeDirection(
        AStarPathfinder3D pathfinder,
        Vector3 currentPosition,
        Vector3 desiredDirection,
        float stepDistance,
        out Vector3 escapeDirection)
    {
        escapeDirection = Vector3.zero;
        if (!TryGetNearestObstacle(
                currentPosition,
                pathfinder.obstacleMask,
                out Vector3 awayFromObstacle,
                out float currentClearance))
        {
            clearanceEscapeActive = false;
            smoothedClearanceAwayDirection = Vector3.zero;
            return false;
        }

        bool wasEscapeActive = clearanceEscapeActive;
        if (!ShouldContinueClearanceEscape(
                wasEscapeActive,
                currentClearance,
                ClearanceRadius,
                clearanceEscapeReleaseMargin))
        {
            clearanceEscapeActive = false;
            smoothedClearanceAwayDirection = Vector3.zero;
            avoidanceSide = 0;
            avoidanceSideUntil = 0f;
            return false;
        }

        clearanceEscapeActive = true;
        if (!wasEscapeActive || smoothedClearanceAwayDirection.sqrMagnitude < 0.001f)
        {
            smoothedClearanceAwayDirection = awayFromObstacle;
        }
        else
        {
            float normalBlend = 1f - Mathf.Exp(
                -clearanceNormalSmoothing * Time.fixedDeltaTime);
            smoothedClearanceAwayDirection = Vector3.Lerp(
                smoothedClearanceAwayDirection,
                awayFromObstacle,
                normalBlend).normalized;
        }

        awayFromObstacle = smoothedClearanceAwayDirection;

        Vector3 leftTangent = Vector3.Cross(Vector3.up, awayFromObstacle).normalized;
        Vector3 rightTangent = -leftTangent;
        Vector3 preferredTangent =
            Vector3.Dot(leftTangent, desiredDirection) >=
            Vector3.Dot(rightTangent, desiredDirection)
                ? leftTangent
                : rightTangent;
        Vector3 alternateTangent = -preferredTangent;

        if (Time.time < avoidanceSideUntil && avoidanceSide != 0)
        {
            preferredTangent = avoidanceSide < 0 ? leftTangent : rightTangent;
            alternateTangent = -preferredTangent;
        }
        else
        {
            avoidanceSide = preferredTangent == leftTangent ? -1 : 1;
            avoidanceSideUntil = Time.time + avoidanceSideHoldTime;
        }

        if (TryAcceptEscapeCandidate(
                pathfinder,
                currentPosition,
                awayFromObstacle * 1.4f + preferredTangent * 0.75f +
                    desiredDirection * 0.15f,
                stepDistance,
                currentClearance,
                out escapeDirection) ||
            TryAcceptEscapeCandidate(
                pathfinder,
                currentPosition,
                awayFromObstacle + preferredTangent * 0.45f,
                stepDistance,
                currentClearance,
                out escapeDirection) ||
            TryAcceptEscapeCandidate(
                pathfinder,
                currentPosition,
                awayFromObstacle,
                stepDistance,
                currentClearance,
                out escapeDirection) ||
            TryAcceptEscapeCandidate(
                pathfinder,
                currentPosition,
                preferredTangent,
                stepDistance,
                currentClearance,
                out escapeDirection) ||
            TryAcceptEscapeCandidate(
                pathfinder,
                currentPosition,
                awayFromObstacle + alternateTangent * 0.45f,
                stepDistance,
                currentClearance,
                out escapeDirection))
        {
            return true;
        }

        return false;
    }

    public static bool ShouldContinueClearanceEscape(
        bool escapeActive,
        float currentClearance,
        float requiredClearance,
        float releaseMargin)
    {
        float threshold = Mathf.Max(0f, requiredClearance) +
                          (escapeActive
                              ? Mathf.Max(0.02f, releaseMargin)
                              : 0.01f);
        return currentClearance < threshold;
    }

    private bool TryAcceptEscapeCandidate(
        AStarPathfinder3D pathfinder,
        Vector3 currentPosition,
        Vector3 candidateDirection,
        float stepDistance,
        float currentClearance,
        out Vector3 acceptedDirection)
    {
        acceptedDirection = candidateDirection;
        acceptedDirection.y = 0f;
        if (acceptedDirection.sqrMagnitude <= 0.001f)
        {
            acceptedDirection = Vector3.zero;
            return false;
        }

        acceptedDirection.Normalize();
        if (IsClearanceImprovingDirection(
                pathfinder,
                currentPosition,
                acceptedDirection,
                stepDistance,
                currentClearance))
        {
            return true;
        }

        acceptedDirection = Vector3.zero;
        return false;
    }

    private bool IsClearanceImprovingDirection(
        AStarPathfinder3D pathfinder,
        Vector3 currentPosition,
        Vector3 direction,
        float stepDistance,
        float currentClearance)
    {
        Vector3 candidate = currentPosition + direction * stepDistance;
        if (IsLocalPositionClear(pathfinder, candidate, ClearanceRadius))
        {
            return true;
        }

        // While escaping the safety margin, the smaller physical sphere is
        // the hard boundary. The full clearance sphere becomes mandatory
        // again as soon as the agent has room for it.
        if (!pathfinder.IsInsideGrid(candidate, agentRadius) ||
            Physics.CheckSphere(
                candidate + Vector3.up * 0.55f,
                Mathf.Max(0.05f, agentRadius),
                pathfinder.obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return TryGetNearestObstacle(
                   candidate,
                   pathfinder.obstacleMask,
                   out _,
                   out float candidateClearance) &&
               candidateClearance >= currentClearance - 0.002f;
    }

    private bool TryGetNearestObstacle(
        Vector3 position,
        LayerMask obstacleMask,
        out Vector3 awayFromObstacle,
        out float clearance)
    {
        Vector3 origin = position + Vector3.up * 0.55f;
        int count = Physics.OverlapSphereNonAlloc(
            origin,
            ClearanceRadius + avoidanceDistance,
            nearbyObstacles,
            obstacleMask,
            QueryTriggerInteraction.Ignore);
        awayFromObstacle = Vector3.zero;
        clearance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider obstacle = nearbyObstacles[i];
            if (obstacle == null)
            {
                continue;
            }

            Vector3 difference = origin - obstacle.ClosestPoint(origin);
            difference.y = 0f;
            float distance = difference.magnitude;
            if (distance >= clearance)
            {
                continue;
            }

            if (distance <= 0.001f)
            {
                difference = origin - obstacle.bounds.center;
                difference.y = 0f;
                distance = difference.magnitude;
            }

            if (distance <= 0.001f)
            {
                continue;
            }

            clearance = distance;
            awayFromObstacle = difference / distance;
        }

        return awayFromObstacle.sqrMagnitude > 0.001f;
    }

    private bool IsMovementDirectionValid(
        AStarPathfinder3D pathfinder,
        Vector3 currentPosition,
        Vector3 direction,
        float stepDistance)
    {
        return direction.sqrMagnitude > 0.001f &&
               IsLocalPositionClear(
                   pathfinder,
                   currentPosition + direction.normalized * stepDistance,
                   ClearanceRadius);
    }

    private static bool IsLocalPositionClear(
        AStarPathfinder3D pathfinder,
        Vector3 position,
        float radius)
    {
        return pathfinder.IsInsideGrid(position, radius) &&
               !Physics.CheckSphere(
                   position + Vector3.up * 0.55f,
                   Mathf.Max(0.05f, radius),
                   pathfinder.obstacleMask,
                   QueryTriggerInteraction.Ignore);
    }

    private float GetObstacleClearance(
        Vector3 origin,
        Vector3 direction,
        LayerMask obstacleMask)
    {
        if (Physics.SphereCast(
                origin,
                ClearanceRadius,
                direction,
                out RaycastHit hit,
                avoidanceDistance * 1.5f,
                obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            return hit.distance;
        }

        return avoidanceDistance * 1.5f;
    }

    private Vector3 GetSeparationDirection()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position + Vector3.up * 0.55f,
            minAgentSeparation,
            nearbyColliders,
            ~0,
            QueryTriggerInteraction.Ignore);
        Vector3 separation = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            AgentStats candidate = nearbyColliders[i] != null
                ? nearbyColliders[i].GetComponentInParent<AgentStats>()
                : null;
            if (candidate == null || candidate == stats)
            {
                continue;
            }

            HealthSystem health = candidate.GetComponent<HealthSystem>();
            if (health != null && health.IsDead)
            {
                continue;
            }

            Vector3 difference = transform.position - candidate.transform.position;
            difference.y = 0f;
            float distance = difference.magnitude;
            if (distance >= minAgentSeparation)
            {
                continue;
            }

            if (distance <= 0.001f)
            {
                difference = GetEntityId().GetHashCode() <
                             candidate.GetEntityId().GetHashCode()
                    ? Vector3.right
                    : Vector3.left;
                distance = 0f;
            }

            float strength = 1f - distance / minAgentSeparation;
            separation += difference.normalized * strength;
        }

        return Vector3.ClampMagnitude(separation, 1f);
    }

    /// <summary>
    /// Trailing agents slow behind a nearby agent instead of collider-pushing.
    /// A deterministic right-of-way rule keeps head-on traffic from deadlocking.
    /// </summary>
    private float GetCrowdSpeedScale(
        Vector3 currentPosition,
        Vector3 direction,
        float stepDistance)
    {
        int count = Physics.OverlapSphereNonAlloc(
            currentPosition + Vector3.up * 0.55f,
            minAgentSeparation * 1.8f,
            nearbyColliders,
            ~0,
            QueryTriggerInteraction.Ignore);
        float scale = 1f;
        for (int i = 0; i < count; i++)
        {
            AgentStats other = nearbyColliders[i] != null
                ? nearbyColliders[i].GetComponentInParent<AgentStats>()
                : null;
            if (other == null || other == stats)
            {
                continue;
            }

            HealthSystem health = other.GetComponent<HealthSystem>();
            if (health != null && health.IsDead)
            {
                continue;
            }

            Vector3 toOther = other.transform.position - currentPosition;
            toOther.y = 0f;
            float distance = toOther.magnitude;
            if (distance <= 0.001f)
            {
                scale = Mathf.Min(scale, 0.2f);
                continue;
            }

            float forward = Vector3.Dot(direction, toOther);
            Vector3 lateral = toOther - direction * forward;
            if (forward <= 0f || lateral.magnitude > minAgentSeparation * 0.8f)
            {
                continue;
            }

            Vector3 nextPosition = currentPosition + direction * stepDistance;
            if (FlatDistance(nextPosition, other.transform.position) > distance)
            {
                continue;
            }

            AgentMotor otherMotor = other.GetComponent<AgentMotor>();
            bool headOn = otherMotor != null && otherMotor.HasDestination &&
                          Vector3.Dot(direction, other.transform.forward) < -0.35f;
            if (headOn && GetEntityId().GetHashCode() <
                otherMotor.GetEntityId().GetHashCode())
            {
                scale = Mathf.Min(scale, 0.55f);
                continue;
            }

            scale = Mathf.Min(
                scale,
                Mathf.InverseLerp(
                    minAgentSeparation * 0.65f,
                    minAgentSeparation * 1.8f,
                    distance));
        }

        return Mathf.Clamp(
            scale,
            Mathf.Clamp01(minimumCrowdSpeedFactor),
            1f);
    }

    private string GetCurrentAIState()
    {
        DefenderAgentAI defender = GetComponent<DefenderAgentAI>();
        if (defender != null)
        {
            return defender.CurrentState.ToString();
        }

        AttackerCombatAI attacker = GetComponent<AttackerCombatAI>();
        return attacker != null ? attacker.CurrentState.ToString() : "ObjectiveMovement";
    }

    private void UpdateDiagnostics()
    {
        if (Time.time < nextDiagnosticUpdateTime)
        {
            return;
        }

        nextDiagnosticUpdateTime = Time.time + 0.35f;
        RoundManager round = RoundManager.Instance;
        ObjectiveManager objective = ObjectiveManager.Instance;
        bombPlantedDebug = round != null &&
                           round.CurrentState == RoundState.BombPlanted;
        if (bombPlantedDebug && stats != null && round != null)
        {
            objectiveDebug = stats.team == round.defendingTeam
                ? "RetakeAndDefuse"
                : "DefendBomb";
        }
        else
        {
            objectiveDebug = hasResolvedRequest
                ? requestedDestination.ToString("F1")
                : "None";
        }

        DefenderTeamCoordinator coordinator = DefenderTeamCoordinator.Instance;
        roleDebug = stats != null && round != null &&
                    stats.team == round.defendingTeam && coordinator != null
            ? coordinator.GetRole(gameObject).ToString()
            : stats != null && round != null && stats.team == round.attackingTeam
                ? "Striker"
                : "Unassigned";

        AgentBrain brain = GetComponent<AgentBrain>();
        AgentSensors sensors = GetComponent<AgentSensors>();
        WeaponSystem weapon = GetComponent<WeaponSystem>();
        GameObject target = brain != null ? brain.CurrentTarget : null;
        currentTargetDebug = target != null ? target.name : "None";
        enemyVisibleDebug = target != null && sensors != null &&
                            sensors.HasLineOfSight(target);
        if (target == null)
        {
            combatDebug = "No detected enemy";
        }
        else if (!enemyVisibleDebug)
        {
            combatDebug = "Enemy not visible";
        }
        else if (stats != null &&
                 FlatDistance(transform.position, target.transform.position) >
                 (GetComponent<WeaponLoadout>() != null
                     ? GetComponent<WeaponLoadout>().MaximumRange
                     : stats.attackRange))
        {
            combatDebug = "Enemy out of range";
        }
        else if (weapon == null || !weapon.enabled)
        {
            combatDebug = "Weapon unavailable";
        }
        else
        {
            combatDebug = weapon.IsReady ? "Firing enabled" : "Weapon cooldown";
        }

        bool holdingCompletedObjective = !hasDestination && hasResolvedRequest &&
                                         FlatDistance(transform.position, destination) <=
                                         waypointReachDistance + 0.35f;
        if (holdingCompletedObjective)
        {
            pathStatusDebug = "Holding completed slot";
        }
        else if (!hasDestination)
        {
            pathStatusDebug = "No destination";
        }
        else if (currentPath == null)
        {
            pathStatusDebug = "No path";
        }
        else if (pathBlocked)
        {
            pathStatusDebug = "Locally blocked";
        }
        else if (HasReachedDestination(targetArrivalDistance + 0.1f))
        {
            pathStatusDebug = "At reserved slot";
        }
        else
        {
            pathStatusDebug = "Following path";
        }

        if (round != null && round.CurrentState == RoundState.RoundEnd)
        {
            inactivityReasonDebug = "Round ended";
        }
        else if (objective != null && objective.ActiveDefuser == gameObject)
        {
            inactivityReasonDebug = "Defusing";
        }
        else if (enemyVisibleDebug && weapon != null && weapon.IsReady)
        {
            inactivityReasonDebug = "Shooting visible enemy";
        }
        else if (holdingCompletedObjective)
        {
            inactivityReasonDebug = "Holding slot and watching";
        }
        else if (!hasDestination)
        {
            inactivityReasonDebug = "Awaiting valid objective target";
        }
        else if (currentPath == null)
        {
            inactivityReasonDebug = "Repath or slot reassignment pending";
        }
        else if (pathBlocked)
        {
            inactivityReasonDebug = "Yielding/steering around blockage";
        }
        else if (HasReachedDestination(targetArrivalDistance + 0.1f))
        {
            inactivityReasonDebug = "Holding slot and watching";
        }
        else
        {
            inactivityReasonDebug = "Moving to tactical slot";
        }

        string signature = GetCurrentAIState() + "|" + roleDebug + "|" +
                           objectiveDebug + "|" + pathStatusDebug + "|" +
                           combatDebug + "|" + inactivityReasonDebug;
        if (logDiagnosticStateChanges && signature != lastDiagnosticSignature &&
            Time.time >= nextDiagnosticLogTime)
        {
            lastDiagnosticSignature = signature;
            nextDiagnosticLogTime = Time.time + 1f;
            Debug.Log(
                $"AI status [{name}] state={GetCurrentAIState()}, role={roleDebug}, " +
                $"objective={objectiveDebug}, target={currentTargetDebug}, " +
                $"path={pathStatusDebug}, enemyVisible={enemyVisibleDebug}, " +
                $"bombPlanted={bombPlantedDebug}, reason={inactivityReasonDebug}");
        }
    }

    private void ResetStuckTracking()
    {
        lastPosition = transform.position;
        lastMoveProgressTime = Time.time;
        stuckTimer = 0f;
        isStuck = false;
        recoveryAttempts = 0;
    }

    private void OnDisable()
    {
        PositionReservationManager.Instance?.Release(this);
    }

    private void OnDestroy()
    {
        PositionReservationManager.Instance?.Release(this);
    }

    private void OnGUI()
    {
        if (!showRuntimeDebugLabel || Camera.main == null)
        {
            return;
        }

        Vector3 screen = Camera.main.WorldToScreenPoint(
            transform.position + Vector3.up * 2.2f);
        if (screen.z <= 0f)
        {
            return;
        }

        GUI.color = isStuck ? Color.magenta : currentTargetValid ? Color.green : Color.red;
        GUI.Label(
            new Rect(screen.x - 120f, Screen.height - screen.y, 240f, 112f),
            $"{GetCurrentAIState()} | {roleDebug}\n" +
            $"Objective: {objectiveDebug}\nTarget: {currentTargetDebug}\n" +
            $"Path: {pathStatusDebug} ({currentSlotKind})\n" +
            $"Visible: {enemyVisibleDebug} | Bomb: {bombPlantedDebug}\n" +
            inactivityReasonDebug);
        GUI.color = Color.white;
    }

    private void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (!drawMovementGizmos)
        {
            return;
        }

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, agentRadius);
        Gizmos.color = new Color(1f, 0.65f, 0f, 0.8f);
        Gizmos.DrawWireSphere(
            transform.position + Vector3.up * 0.1f,
            minAgentSeparation);
        if (!hasDestination)
        {
            return;
        }

        Gizmos.color = currentTargetValid ? Color.green : Color.red;
        Gizmos.DrawWireSphere(destination + Vector3.up * 0.1f, agentRadius);
        Gizmos.DrawLine(
            transform.position + Vector3.up * 0.1f,
            destination + Vector3.up * 0.1f);
        if (targetWasAdjusted)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(
                requestedDestination + Vector3.up * 0.1f,
                agentRadius);
        }

        Gizmos.color = pathBlocked ? Color.red : Color.cyan;
        Gizmos.DrawRay(
            transform.position + Vector3.up * 0.55f,
            lastAvoidanceDirection * avoidanceDistance);
        if (isStuck)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(
                transform.position + Vector3.up * 0.5f,
                agentRadius * 1.6f);
        }
        if (currentPath == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Vector3 previous = transform.position;
        for (int i = currentWaypointIndex; i < currentPath.Count; i++)
        {
            Gizmos.DrawLine(
                previous + Vector3.up * 0.08f,
                currentPath[i] + Vector3.up * 0.08f);
            previous = currentPath[i];
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
