using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executes movement only. It does not select enemies or decide what action to take.
/// </summary>
public class AgentMotor : MonoBehaviour
{
    [Header("Path Following")]
    public float pathRefreshTime = 0.3f;
    public float waypointReachDistance = 0.3f;
    public float rotationSpeed = 720f;
    public float separationRadius = 1.2f;
    public float separationStrength = 1.5f;

    [Header("Clearance")]
    [SerializeField] private float agentRadius = 0.4f;
    [SerializeField] private float obstacleClearance = 0.1f;
    [SerializeField] private float wallAvoidanceDistance = 0.2f;
    [SerializeField] private float maximumTargetAdjustment = 4f;

    [Header("Stuck Recovery")]
    [SerializeField] private float stuckDistanceThreshold = 0.05f;
    [SerializeField] private float stuckTimeThreshold = 1.2f;
    [SerializeField] private float hardStuckTimeThreshold = 3f;
    [SerializeField] private float recoveryWaypointDistance = 1.25f;
    [SerializeField] private float emergencySnapDistance = 2f;
    [SerializeField] private bool drawMovementGizmos = true;

    [Header("Runtime Debug")]
    [SerializeField] private Vector3 requestedDestination;
    [SerializeField] private Vector3 lastPosition;
    [SerializeField] private float lastMoveProgressTime;
    [SerializeField] private float stuckTimer;
    [SerializeField] private bool isStuck;
    [SerializeField] private int recoveryAttempts;
    [SerializeField] private bool targetWasAdjusted;

    private AgentStats stats;
    private Rigidbody body;
    private List<Vector3> currentPath;
    private int currentWaypointIndex;
    private float nextPathRefreshTime;
    private float nextRecoveryAttemptTime;
    private float recoveryResumeTime;
    private Vector3 destination;
    private bool hasDestination;
    private bool recoveringLocally;
    private bool hasResolvedRequest;

    public bool HasDestination => hasDestination;
    public Vector3 Destination => destination;
    public Vector3 RequestedDestination => requestedDestination;
    public bool IsStuck => isStuck;
    public float SpeedMultiplier { get; set; } = 1f;

    private float ClearanceRadius => agentRadius + obstacleClearance;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        body = GetComponent<Rigidbody>();
        Collider agentCollider = GetComponent<Collider>();
        if (agentCollider != null)
        {
            float colliderRadius = Mathf.Min(
                agentCollider.bounds.extents.x,
                agentCollider.bounds.extents.z);
            agentRadius = Mathf.Max(agentRadius, colliderRadius);
        }

        lastPosition = transform.position;
        lastMoveProgressTime = Time.time;
    }

    private void Update()
    {
        if (!hasDestination)
        {
            ResetStuckTracking();
            return;
        }

        if (recoveringLocally &&
            (HasReachedDestination(waypointReachDistance + 0.1f) ||
             Time.time >= recoveryResumeTime))
        {
            recoveringLocally = false;
            ApplyValidatedDestination(requestedDestination, true);
        }

        UpdateStuckDetection();
        if (Time.time >= nextPathRefreshTime)
        {
            RefreshPath();
            nextPathRefreshTime = Time.time + pathRefreshTime;
        }
    }

    private void FixedUpdate()
    {
        FollowPath();
    }

    public void MoveTo(Vector3 newDestination)
    {
        bool destinationChanged = !hasDestination ||
                                  FlatDistance(requestedDestination, newDestination) >
                                  waypointReachDistance;
        requestedDestination = newDestination;
        if (recoveringLocally)
        {
            return;
        }

        if (destinationChanged)
        {
            ApplyValidatedDestination(newDestination, false);
        }
    }

    public bool HasReachedDestination(float tolerance)
    {
        return hasDestination && FlatDistance(transform.position, destination) <= tolerance;
    }

    public bool HasReachedRequestedDestination(Vector3 request, float tolerance)
    {
        return hasResolvedRequest &&
               FlatDistance(requestedDestination, request) <= waypointReachDistance &&
               FlatDistance(transform.position, destination) <= tolerance;
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
        Debug.Log("Repathing because stuck");
    }

    public void Stop()
    {
        hasDestination = false;
        recoveringLocally = false;
        currentPath = null;
        currentWaypointIndex = 0;
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

    private void ApplyValidatedDestination(Vector3 requested, bool forced)
    {
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
                         out resolvedPath);

        if (!valid && pathfinder != null)
        {
            Debug.Log("Target invalid, finding nearest valid point");
            valid = pathfinder.TryGetNearestWalkablePosition(
                transform.position,
                emergencySnapDistance,
                ClearanceRadius,
                out resolved);
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
            return;
        }

        targetWasAdjusted = FlatDistance(requested, resolved) > 0.15f;
        if (targetWasAdjusted && !forced)
        {
            Debug.Log("Target invalid, finding nearest valid point");
        }

        destination = resolved;
        hasDestination = true;
        hasResolvedRequest = true;
        currentPath = resolvedPath;
        currentWaypointIndex = 0;
        nextPathRefreshTime = Time.time + pathRefreshTime;
        if (currentPath != null && currentPath.Count == 0 &&
            FlatDistance(transform.position, destination) > waypointReachDistance)
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
            FlatDistance(transform.position, destination) > waypointReachDistance)
        {
            currentPath.Add(destination);
        }
    }

    private void UpdateStuckDetection()
    {
        if (HasReachedDestination(waypointReachDistance + 0.1f))
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
        Debug.Log("Repathing because stuck");

        RefreshPath();
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
                    out List<Vector3> candidatePath) ||
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
                out Vector3 safePosition))
        {
            return;
        }

        safePosition.y = body.position.y;
        body.position = safePosition;
        transform.position = safePosition;
        lastPosition = safePosition;
        lastMoveProgressTime = Time.time;
        stuckTimer = 0f;
        isStuck = false;
        recoveryAttempts = 0;
        recoveringLocally = false;
        Debug.Log("Emergency unstuck to nearest valid position");
        ApplyValidatedDestination(requestedDestination, true);
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

        if (FlatDistance(currentPosition, targetWaypoint) <= waypointReachDistance)
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

        Vector3 desiredDirection = moveDirection.normalized +
                                   GetSeparationDirection() * separationStrength;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        desiredDirection.Normalize();
        float stepDistance = stats.moveSpeed * SpeedMultiplier * Time.fixedDeltaTime;
        Vector3 safeDirection = GetCollisionSafeDirection(
            currentPosition,
            desiredDirection,
            stepDistance);
        if (safeDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion movementRotation = Quaternion.LookRotation(safeDirection, Vector3.up);
        body.MoveRotation(Quaternion.RotateTowards(
            body.rotation,
            movementRotation,
            rotationSpeed * Time.fixedDeltaTime));

        Vector3 newPosition = currentPosition + safeDirection * stepDistance;
        newPosition.y = currentPosition.y;
        body.MovePosition(newPosition);
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
        if (!Physics.SphereCast(
                origin,
                agentRadius,
                desiredDirection,
                out RaycastHit hit,
                stepDistance + wallAvoidanceDistance,
                pathfinder.obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            Vector3 candidate = currentPosition + desiredDirection * stepDistance;
            return pathfinder.IsValidAgentPosition(candidate, ClearanceRadius)
                ? desiredDirection
                : Vector3.zero;
        }

        Vector3 tangentA = Vector3.Cross(Vector3.up, hit.normal).normalized;
        Vector3 tangentB = -tangentA;
        Vector3 preferred = Vector3.Dot(tangentA, desiredDirection) >=
                            Vector3.Dot(tangentB, desiredDirection)
            ? tangentA
            : tangentB;
        Vector3 alternate = preferred == tangentA ? tangentB : tangentA;
        if (pathfinder.IsValidAgentPosition(
                currentPosition + preferred * stepDistance,
                ClearanceRadius))
        {
            return preferred;
        }

        return pathfinder.IsValidAgentPosition(
                currentPosition + alternate * stepDistance,
                ClearanceRadius)
            ? alternate
            : Vector3.zero;
    }

    private Vector3 GetSeparationDirection()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        Vector3 separation = Vector3.zero;

        foreach (AgentStats candidate in allAgents)
        {
            if (candidate == stats)
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
            if (distance <= 0.001f || distance > separationRadius)
            {
                continue;
            }

            separation += difference.normalized / distance;
        }

        return separation;
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

    private void ResetStuckTracking()
    {
        lastPosition = transform.position;
        lastMoveProgressTime = Time.time;
        stuckTimer = 0f;
        isStuck = false;
        recoveryAttempts = 0;
    }

    private void OnDrawGizmos()
    {
        if (!drawMovementGizmos || !hasDestination)
        {
            return;
        }

        Gizmos.color = targetWasAdjusted ? Color.green : Color.cyan;
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
