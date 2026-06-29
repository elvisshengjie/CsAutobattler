using System.Collections.Generic;
using UnityEngine;

public class AgentController3D : MonoBehaviour
{
    [Header("Pathfinding")]
    public float pathRefreshTime = 0.3f;
    public float waypointReachDistance = 0.3f;
    public float rotationSpeed = 720f;

    [Header("Agent Avoidance")]
    public float separationRadius = 1.2f;
    public float separationStrength = 1.5f;

    [Header("Vision and Memory")]
    public float sightRange = 60f;
    [Range(1f, 360f)]
    public float fieldOfViewAngle = 90f;
    [Tooltip("Short 360-degree awareness range for nearby enemies.")]
    public float proximityDetectionRange = 7f;
    public float eyeHeight = 0.8f;
    public float memoryDuration = 4f;
    public LayerMask lineOfSightMask = ~0;

    private AgentStats stats;
    private WeaponSystem weapon;
    private Rigidbody rb;

    private GameObject currentTarget;
    private List<Vector3> currentPath;
    private int currentWaypointIndex;
    private float nextPathRefreshTime;
    private Vector3 lastKnownEnemyPosition;
    private float lastSeenEnemyTime = Mathf.NegativeInfinity;
    private bool hasLastKnownEnemyPosition;

    public Vector3 LastKnownEnemyPosition => lastKnownEnemyPosition;
    public bool HasLastKnownEnemyPosition => hasLastKnownEnemyPosition;

    public void NotifyAttackedBy(GameObject attacker)
    {
        if (attacker == null)
        {
            return;
        }

        AgentStats attackerStats = attacker.GetComponentInParent<AgentStats>();
        if (attackerStats == null || attackerStats.team == stats.team)
        {
            return;
        }

        lastKnownEnemyPosition = attackerStats.transform.position;
        lastSeenEnemyTime = Time.time;
        hasLastKnownEnemyPosition = true;
    }

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        weapon = GetComponent<WeaponSystem>();
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        currentTarget = FindClosestVisibleEnemy();

        if (currentTarget != null)
        {
            lastKnownEnemyPosition = currentTarget.transform.position;
            lastSeenEnemyTime = Time.time;
            hasLastKnownEnemyPosition = true;
        }

        if (currentTarget != null)
        {
            FaceTarget(currentTarget.transform.position);

            float distanceToTarget = GetFlatDistance(
                transform.position,
                currentTarget.transform.position);

            if (distanceToTarget <= stats.attackRange && HasLineOfSight(currentTarget))
            {
                currentPath = null;
                weapon.TryAttack(currentTarget);
                return;
            }
        }

        if (!hasLastKnownEnemyPosition || Time.time > lastSeenEnemyTime + memoryDuration)
        {
            hasLastKnownEnemyPosition = false;
            currentPath = null;
            return;
        }

        if (currentTarget == null &&
            GetFlatDistance(transform.position, lastKnownEnemyPosition) <= waypointReachDistance)
        {
            hasLastKnownEnemyPosition = false;
            currentPath = null;
            return;
        }

        if (Time.time >= nextPathRefreshTime)
        {
            RefreshPath(lastKnownEnemyPosition);
            nextPathRefreshTime = Time.time + pathRefreshTime;
        }
    }

    private void FixedUpdate()
    {
        FollowPath();
    }

    private void RefreshPath(Vector3 destination)
    {
        if (AStarPathfinder3D.Instance == null)
        {
            return;
        }

        currentPath = AStarPathfinder3D.Instance.FindPath(
            transform.position,
            destination
        );

        currentWaypointIndex = 0;
    }

    private void FollowPath()
    {
        if (rb == null || currentPath == null || currentPath.Count == 0)
        {
            return;
        }

        if (currentWaypointIndex >= currentPath.Count)
        {
            return;
        }

        Vector3 currentPosition = rb.position;
        Vector3 targetWaypoint = currentPath[currentWaypointIndex];

        targetWaypoint.y = currentPosition.y;

        float distanceToWaypoint = GetFlatDistance(currentPosition, targetWaypoint);

        if (distanceToWaypoint <= waypointReachDistance)
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

        if (moveDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        moveDirection.Normalize();

        Vector3 separationDirection = GetSeparationDirection();

        Vector3 finalDirection = moveDirection + separationDirection * separationStrength;
        finalDirection.y = 0f;

        if (finalDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        finalDirection.Normalize();

        Quaternion movementRotation = Quaternion.LookRotation(finalDirection, Vector3.up);
        rb.MoveRotation(Quaternion.RotateTowards(
            rb.rotation,
            movementRotation,
            rotationSpeed * Time.fixedDeltaTime));

        Vector3 newPosition = currentPosition + finalDirection * stats.moveSpeed * Time.fixedDeltaTime;
        newPosition.y = currentPosition.y;

        rb.MovePosition(newPosition);
    }

    private Vector3 GetSeparationDirection()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);

        Vector3 separation = Vector3.zero;

        foreach (AgentStats agent in allAgents)
        {
            if (agent == stats)
            {
                continue;
            }

            HealthSystem health = agent.GetComponent<HealthSystem>();

            if (health != null && health.IsDead)
            {
                continue;
            }

            Vector3 difference = transform.position - agent.transform.position;
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

    private GameObject FindClosestVisibleEnemy()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);

        GameObject closestEnemy = null;
        float closestDistance = Mathf.Infinity;

        foreach (AgentStats agent in allAgents)
        {
            if (agent == stats)
            {
                continue;
            }

            if (agent.team == stats.team)
            {
                continue;
            }

            HealthSystem health = agent.GetComponent<HealthSystem>();

            if (health == null || health.IsDead)
            {
                continue;
            }

            float distance = GetFlatDistance(transform.position, agent.transform.position);

            bool detectedNearby = distance <= proximityDetectionRange;
            bool detectedInCone = distance <= sightRange &&
                                  IsInsideFieldOfView(agent.transform.position);

            if ((!detectedNearby && !detectedInCone) ||
                !HasLineOfSight(agent.gameObject))
            {
                continue;
            }

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestEnemy = agent.gameObject;
            }
        }

        return closestEnemy;
    }

    private bool IsInsideFieldOfView(Vector3 targetPosition)
    {
        Vector3 directionToTarget = targetPosition - transform.position;
        directionToTarget.y = 0f;

        if (directionToTarget.sqrMagnitude <= 0.001f)
        {
            return true;
        }

        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;

        return Vector3.Angle(flatForward, directionToTarget) <= fieldOfViewAngle * 0.5f;
    }

    private bool HasLineOfSight(GameObject target)
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        Vector3 targetPoint = targetCollider != null
            ? targetCollider.bounds.center
            : target.transform.position + Vector3.up * eyeHeight;

        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
        {
            return true;
        }

        if (!Physics.Raycast(
                origin,
                direction / distance,
                out RaycastHit hit,
                distance,
                lineOfSightMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        AgentStats hitAgent = hit.collider.GetComponentInParent<AgentStats>();
        return hitAgent != null && hitAgent.gameObject == target;
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private float GetFlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;

        return Vector3.Distance(a, b);
    }
}
