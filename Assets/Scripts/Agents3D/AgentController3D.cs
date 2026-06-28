using System.Collections.Generic;
using UnityEngine;

public class AgentController3D : MonoBehaviour
{
    [Header("Pathfinding")]
    public float pathRefreshTime = 0.3f;
    public float waypointReachDistance = 0.3f;

    [Header("Agent Avoidance")]
    public float separationRadius = 1.2f;
    public float separationStrength = 1.5f;

    private AgentStats stats;
    private WeaponSystem weapon;
    private Rigidbody rb;

    private GameObject currentTarget;
    private List<Vector3> currentPath;
    private int currentWaypointIndex;
    private float nextPathRefreshTime;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        weapon = GetComponent<WeaponSystem>();
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        currentTarget = FindClosestEnemy();

        if (currentTarget == null)
        {
            currentPath = null;
            return;
        }

        FaceTarget(currentTarget);

        float distanceToTarget = GetFlatDistance(transform.position, currentTarget.transform.position);

        if (distanceToTarget <= stats.attackRange)
        {
            currentPath = null;
            weapon.TryAttack(currentTarget);
            return;
        }

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

    private void RefreshPath()
    {
        if (AStarPathfinder3D.Instance == null || currentTarget == null)
        {
            return;
        }

        currentPath = AStarPathfinder3D.Instance.FindPath(
            transform.position,
            currentTarget.transform.position
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

        Vector3 newPosition = currentPosition + finalDirection * stats.moveSpeed * Time.fixedDeltaTime;
        newPosition.y = currentPosition.y;

        rb.MovePosition(newPosition);
    }

    private Vector3 GetSeparationDirection()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

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

    private GameObject FindClosestEnemy()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

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

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestEnemy = agent.gameObject;
            }
        }

        return closestEnemy;
    }

    private void FaceTarget(GameObject target)
    {
        Vector3 direction = target.transform.position - transform.position;
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