using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AgentStats))]
public class AgentPerception : MonoBehaviour
{
    [Header("Vision")]
    [Min(0f)] public float sightRange = 15f;
    [SerializeField] private LayerMask obstacleMask;

    private AgentStats stats;

    public Vector2 LastKnownEnemyPosition { get; private set; }
    public bool HasLastKnownEnemyPosition { get; private set; }
    public float LastKnownEnemyTime { get; private set; }

    private void Awake()
    {
        stats = GetComponent<AgentStats>();

        // The project already uses the Obstacle layer for walls.
        if (obstacleMask.value == 0)
        {
            obstacleMask = LayerMask.GetMask("Obstacle");
        }
    }

    public bool CanSeeEnemy(AgentStats enemy)
    {
        if (!IsLivingEnemy(enemy))
        {
            return false;
        }

        Vector2 origin = transform.position;
        Vector2 destination = enemy.transform.position;

        if (Vector2.Distance(origin, destination) > sightRange)
        {
            return false;
        }

        RaycastHit2D wallHit = Physics2D.Linecast(origin, destination, obstacleMask);

        if (wallHit.collider != null)
        {
            return false;
        }

        LastKnownEnemyPosition = destination;
        HasLastKnownEnemyPosition = true;
        LastKnownEnemyTime = Time.time;
        TeamKnowledge.ReportEnemy(stats.team, destination);
        return true;
    }

    public List<AgentStats> GetVisibleEnemies()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);

        List<AgentStats> visibleEnemies = new List<AgentStats>();

        foreach (AgentStats agent in allAgents)
        {
            if (CanSeeEnemy(agent))
            {
                visibleEnemies.Add(agent);
            }
        }

        return visibleEnemies;
    }

    public AgentStats GetBestVisibleEnemy()
    {
        List<AgentStats> visibleEnemies = GetVisibleEnemies();
        AgentStats bestEnemy = null;
        float bestDistanceSquared = Mathf.Infinity;

        foreach (AgentStats enemy in visibleEnemies)
        {
            float distanceSquared =
                ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;

            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestEnemy = enemy;
            }
        }

        return bestEnemy;
    }

    private bool IsLivingEnemy(AgentStats enemy)
    {
        if (enemy == null || enemy == stats || enemy.team == stats.team)
        {
            return false;
        }

        HealthSystem health = enemy.GetComponent<HealthSystem>();
        return health != null && !health.IsDead;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);

        if (HasLastKnownEnemyPosition)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, LastKnownEnemyPosition);
            Gizmos.DrawWireSphere(LastKnownEnemyPosition, 0.15f);
        }
    }
}
