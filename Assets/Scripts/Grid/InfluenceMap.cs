using System.Collections.Generic;
using UnityEngine;

public class InfluenceCell
{
    public Vector2 worldPosition;
    public bool walkable;
    public float enemyThreat;
    public float allySupport;
    public float cover;
    public float exposure;
    public float tacticalValue;
}

public class InfluenceMap : MonoBehaviour
{
    public static InfluenceMap Instance { get; private set; }

    [Header("Tactical Weights")]
    public float coverWeight = 0.45f;
    public float supportWeight = 0.3f;
    public float threatWeight = 0.55f;
    public float travelWeight = 0.4f;
    public float coverProbeRadius = 0.75f;

    [Header("Crowd Avoidance")]
    [Min(0.1f)] public float destinationSeparation = 1.25f;
    [Min(0f)] public float crowdPenaltyWeight = 2f;

    [Header("Debug")]
    public bool drawHeatmap;

    private AStarPathfinder pathfinder;
    private InfluenceCell[,] cells;

    private void Awake()
    {
        Instance = this;
        pathfinder = GetComponent<AStarPathfinder>();
    }

    public Vector2 GetBestTacticalPosition(
        AgentStats requester,
        Vector2 origin,
        bool preferSafety,
        float minimumMoveDistance = 0f)
    {
        Recalculate(requester, origin, preferSafety);
        InfluenceCell best = null;
        AgentMovement movementComp = requester.GetComponent<AgentMovement>();

        foreach (InfluenceCell cell in cells)
        {
            if (cell.walkable &&
                (movementComp == null || !movementComp.IsNearUnreachablePosition(cell.worldPosition)) &&
                Vector2.Distance(origin, cell.worldPosition) >= minimumMoveDistance &&
                (best == null || cell.tacticalValue > best.tacticalValue))
            {
                best = cell;
            }
        }

        return best != null ? best.worldPosition : origin;
    }

    public bool TryGetBestCoverPosition(AgentStats requester, Vector2 origin, out Vector2 position)
    {
        Recalculate(requester, origin, true);
        InfluenceCell best = null;
        AgentMovement movementComp = requester.GetComponent<AgentMovement>();

        foreach (InfluenceCell cell in cells)
        {
            // Cover means the cell is close to a wall AND that wall blocks
            // the current enemy's line of sight. Merely touching a wall is not cover.
            if (!cell.walkable || cell.cover < 0.99f)
            {
                continue;
            }

            if (movementComp != null && movementComp.IsNearUnreachablePosition(cell.worldPosition))
            {
                continue;
            }

            if (best == null || cell.tacticalValue > best.tacticalValue)
            {
                best = cell;
            }
        }

        position = best != null ? best.worldPosition : origin;
        return best != null;
    }

    public float GetThreatAt(Vector2 position, AgentStats requester)
    {
        float threat = 0f;
        foreach (AgentStats agent in FindLivingAgents())
        {
            if (agent.team == requester.team)
            {
                continue;
            }

            float distance = Vector2.Distance(position, agent.transform.position);
            threat += 1f / (1f + distance * distance);
        }
        return threat;
    }

    private void Recalculate(AgentStats requester, Vector2 origin, bool preferSafety)
    {
        if (pathfinder == null)
        {
            pathfinder = AStarPathfinder.Instance;
        }

        if (pathfinder == null)
        {
            cells = new InfluenceCell[0, 0];
            return;
        }

        EnsureCells();
        AgentStats[] agents = FindLivingAgents();
        float maximumTravel = Mathf.Max(pathfinder.gridWidth, pathfinder.gridHeight);
        LayerMask obstacleMask = pathfinder.obstacleMask;

        // Get Personality and Combat distance stats
        AgentPersonality personality = requester.GetComponent<AgentPersonality>();
        float teamworkFactor = personality != null ? personality.teamwork : 0.5f;
        float prefDist = personality != null ? personality.preferredCombatDistance : 3.0f;
        float minDist = personality != null ? personality.minimumCombatDistance : 1.5f;
        float tolerance = personality != null ? personality.distanceTolerance : 0.5f;

        // Find requester's current target via perception
        AgentPerception perception = requester.GetComponent<AgentPerception>();
        AgentStats currentTarget = (perception != null) ? perception.GetBestVisibleEnemy() : null;

        for (int x = 0; x < pathfinder.GridSizeX; x++)
        {
            for (int y = 0; y < pathfinder.GridSizeY; y++)
            {
                InfluenceCell cell = cells[x, y];
                cell.enemyThreat = 0f;
                cell.allySupport = 0f;
                cell.exposure = 0f;
                float crowdPenalty = 0f;

                foreach (AgentStats agent in agents)
                {
                    float distance = Vector2.Distance(cell.worldPosition, agent.transform.position);
                    float influence = 1f / (1f + distance * distance);

                    if (agent.team == requester.team)
                    {
                        cell.allySupport += influence;

                        if (agent != requester)
                        {
                            float positionDistance = Vector2.Distance(cell.worldPosition, agent.transform.position);
                            if (positionDistance < destinationSeparation)
                            {
                                crowdPenalty += 1f - positionDistance / destinationSeparation;
                            }

                            AgentMovement allyMovement = agent.GetComponent<AgentMovement>();
                            if (allyMovement != null && allyMovement.HasDestination)
                            {
                                float destinationDistance = Vector2.Distance(
                                    cell.worldPosition,
                                    allyMovement.Destination);

                                if (destinationDistance < destinationSeparation)
                                {
                                    // Reserving destinations is more important than
                                    // avoiding an ally who is merely passing through.
                                    crowdPenalty += 1.5f *
                                        (1f - destinationDistance / destinationSeparation);
                                }
                            }
                        }
                    }
                    else
                    {
                        cell.enemyThreat += influence;
                        if (!Physics2D.Linecast(cell.worldPosition, agent.transform.position, obstacleMask))
                        {
                            cell.exposure += influence;
                        }
                    }
                }

                bool nearWall = Physics2D.OverlapCircle(
                    cell.worldPosition,
                    coverProbeRadius,
                    obstacleMask);
                bool hiddenFromTarget = currentTarget != null && Physics2D.Linecast(
                    cell.worldPosition,
                    currentTarget.transform.position,
                    obstacleMask).collider != null;

                cell.cover = nearWall && hiddenFromTarget ? 1f : 0f;
                float normalizedTravel = Vector2.Distance(origin, cell.worldPosition)
                    / Mathf.Max(0.01f, maximumTravel);
                float safetyMultiplier = preferSafety ? 1.5f : 1f;

                // Range Desirability calculation
                float rangeDesirability = 0f;
                if (currentTarget != null)
                {
                    float cellDistToTarget = Vector2.Distance(cell.worldPosition, currentTarget.transform.position);
                    if (cellDistToTarget < minDist)
                    {
                        // Strong penalty for being closer than minimum distance
                        rangeDesirability = -1.5f * (1f - (cellDistToTarget / minDist));
                    }
                    else if (Mathf.Abs(cellDistToTarget - prefDist) <= tolerance)
                    {
                        // Strong reward for being near preferred distance within tolerance
                        rangeDesirability = 1.0f;
                    }
                    else
                    {
                        // Moderate reward decay further away from preferred range
                        float diff = Mathf.Abs(cellDistToTarget - prefDist);
                        rangeDesirability = Mathf.Max(0f, 0.6f - (diff / prefDist) * 0.4f);
                    }
                }

                cell.tacticalValue =
                    coverWeight * cell.cover +
                    supportWeight * (1f + teamworkFactor) * cell.allySupport -
                    threatWeight * safetyMultiplier * cell.enemyThreat -
                    0.35f * cell.exposure -
                    travelWeight * normalizedTravel +
                    0.5f * rangeDesirability -
                    crowdPenaltyWeight * crowdPenalty;
            }
        }
    }

    private void EnsureCells()
    {
        if (cells != null && cells.GetLength(0) == pathfinder.GridSizeX &&
            cells.GetLength(1) == pathfinder.GridSizeY)
        {
            return;
        }

        cells = new InfluenceCell[pathfinder.GridSizeX, pathfinder.GridSizeY];
        for (int x = 0; x < pathfinder.GridSizeX; x++)
        {
            for (int y = 0; y < pathfinder.GridSizeY; y++)
            {
                cells[x, y] = new InfluenceCell
                {
                    worldPosition = pathfinder.GridToWorld(x, y),
                    walkable = pathfinder.IsWalkable(x, y)
                };
            }
        }
    }

    private AgentStats[] FindLivingAgents()
    {
        List<AgentStats> living = new List<AgentStats>();
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (health != null && !health.IsDead)
            {
                living.Add(agent);
            }
        }
        return living.ToArray();
    }

    private void OnDrawGizmos()
    {
        if (!drawHeatmap || cells == null || pathfinder == null)
        {
            return;
        }

        foreach (InfluenceCell cell in cells)
        {
            float display = Mathf.InverseLerp(-1f, 1f, cell.tacticalValue);
            Gizmos.color = Color.Lerp(new Color(1f, 0f, 0f, 0.35f), new Color(0f, 1f, 0f, 0.35f), display);
            Gizmos.DrawCube(cell.worldPosition, Vector3.one * pathfinder.cellSize * 0.85f);
        }
    }
}
