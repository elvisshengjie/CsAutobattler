using System.Collections.Generic;
using UnityEngine;

public class AgentController : MonoBehaviour
{
    [Header("Pathfinding")]
    public float pathRefreshTime = 0.5f;
    public float waypointReachDistance = 0.15f;

    private AgentStats stats;
    private WeaponSystem weapon;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;

    private GameObject currentTarget;
    private List<Vector2> currentPath;
    private int currentWaypointIndex;
    private float nextPathRefreshTime;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        weapon = GetComponent<WeaponSystem>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
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

        float distanceToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);

        WeaponLoadout loadout = WeaponLoadout.Get(gameObject);
        if (distanceToTarget <= loadout.MaximumRange)
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
        if (AStarPathfinder.Instance == null || currentTarget == null)
        {
            return;
        }

        currentPath = AStarPathfinder.Instance.FindPath(transform.position, currentTarget.transform.position);
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

        Vector2 currentPosition = rb.position;
        Vector2 targetWaypoint = currentPath[currentWaypointIndex];

        float distanceToWaypoint = Vector2.Distance(currentPosition, targetWaypoint);

        if (distanceToWaypoint <= waypointReachDistance)
        {
            currentWaypointIndex++;

            if (currentWaypointIndex >= currentPath.Count)
            {
                return;
            }

            targetWaypoint = currentPath[currentWaypointIndex];
        }

        Vector2 moveDirection = (targetWaypoint - currentPosition).normalized;
        WeaponLoadout loadout = WeaponLoadout.Get(gameObject);
        Vector2 newPosition = currentPosition + moveDirection * stats.moveSpeed *
                              loadout.MovementSpeedMultiplier * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
    }

    private GameObject FindClosestEnemy()
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

            float distance = Vector2.Distance(transform.position, agent.transform.position);

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
        if (spriteRenderer == null)
        {
            return;
        }

        float directionX = target.transform.position.x - transform.position.x;

        if (directionX > 0)
        {
            spriteRenderer.flipX = false;
        }
        else if (directionX < 0)
        {
            spriteRenderer.flipX = true;
        }
    }
}
