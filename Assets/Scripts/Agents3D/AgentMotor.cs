using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executes movement only. It does not select enemies or decide what action to take.
/// </summary>
public class AgentMotor : MonoBehaviour
{
    public float pathRefreshTime = 0.3f;
    public float waypointReachDistance = 0.3f;
    public float rotationSpeed = 720f;
    public float separationRadius = 1.2f;
    public float separationStrength = 1.5f;

    private AgentStats stats;
    private Rigidbody body;
    private List<Vector3> currentPath;
    private int currentWaypointIndex;
    private float nextPathRefreshTime;
    private Vector3 destination;
    private bool hasDestination;

    public bool HasDestination => hasDestination;
    public Vector3 Destination => destination;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        body = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!hasDestination || Time.time < nextPathRefreshTime)
        {
            return;
        }

        RefreshPath();
        nextPathRefreshTime = Time.time + pathRefreshTime;
    }

    private void FixedUpdate()
    {
        FollowPath();
    }

    public void MoveTo(Vector3 newDestination)
    {
        bool destinationChanged = !hasDestination ||
                                  FlatDistance(destination, newDestination) > waypointReachDistance;
        destination = newDestination;
        hasDestination = true;

        if (destinationChanged)
        {
            nextPathRefreshTime = 0f;
        }
    }

    public void Stop()
    {
        hasDestination = false;
        currentPath = null;
        currentWaypointIndex = 0;
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

    private void RefreshPath()
    {
        if (AStarPathfinder3D.Instance == null)
        {
            currentPath = null;
            return;
        }

        currentPath = AStarPathfinder3D.Instance.FindPath(transform.position, destination);
        currentWaypointIndex = 0;
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

        Vector3 finalDirection = moveDirection.normalized +
                                 GetSeparationDirection() * separationStrength;
        finalDirection.y = 0f;
        if (finalDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        finalDirection.Normalize();
        Quaternion movementRotation = Quaternion.LookRotation(finalDirection, Vector3.up);
        body.MoveRotation(Quaternion.RotateTowards(
            body.rotation,
            movementRotation,
            rotationSpeed * Time.fixedDeltaTime));

        Vector3 newPosition = currentPosition +
                              finalDirection * stats.moveSpeed * Time.fixedDeltaTime;
        newPosition.y = currentPosition.y;
        body.MovePosition(newPosition);
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

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
