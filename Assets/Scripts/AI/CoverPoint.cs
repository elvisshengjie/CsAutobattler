using UnityEngine;

public class CoverPoint : MonoBehaviour
{
    public Transform standPosition;
    public bool IsOccupied => occupant != null;

    private AgentBrain occupant;

    public Vector2 Position => standPosition != null ? standPosition.position : transform.position;

    public bool TryClaim(AgentBrain agent)
    {
        if (occupant != null && occupant != agent)
        {
            return false;
        }

        occupant = agent;
        return true;
    }

    public void Release(AgentBrain agent)
    {
        if (occupant == agent)
        {
            occupant = null;
        }
    }

    public static CoverPoint FindBest(AgentBrain requester, AgentStats enemy, float maximumDistance)
    {
        CoverPoint best = null;
        float bestScore = float.NegativeInfinity;
        AgentStats requesterStats = requester.GetComponent<AgentStats>();

        foreach (CoverPoint point in FindObjectsByType<CoverPoint>(FindObjectsInactive.Exclude))
        {
            if (point.IsOccupied ||
                Vector2.Distance(requester.transform.position, point.Position) > maximumDistance ||
                !IsPositionAvailable(requester, point.Position))
            {
                continue;
            }

            ListPathResult pathResult = CanReach(requester.transform.position, point.Position);
            if (!pathResult.reachable)
            {
                continue;
            }

            bool hidden = enemy == null || Physics2D.Linecast(
                point.Position,
                enemy.transform.position,
                LayerMask.GetMask("Obstacle"));
            float threat = InfluenceMap.Instance != null
                ? InfluenceMap.Instance.GetThreatAt(point.Position, requesterStats)
                : 0f;
            float score = (hidden ? 1f : 0f) - threat - pathResult.distance * 0.05f;

            if (score > bestScore)
            {
                bestScore = score;
                best = point;
            }
        }

        return best;
    }

    private static bool IsPositionAvailable(AgentBrain requester, Vector2 position)
    {
        const float requiredSpacing = 1.1f;

        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (agent.gameObject == requester.gameObject)
            {
                continue;
            }

            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (health != null && health.IsDead)
            {
                continue;
            }

            if (Vector2.Distance(position, agent.transform.position) < requiredSpacing)
            {
                return false;
            }

            AgentMovement movement = agent.GetComponent<AgentMovement>();
            if (movement != null && movement.HasDestination &&
                Vector2.Distance(position, movement.Destination) < requiredSpacing)
            {
                return false;
            }
        }

        return true;
    }

    private static ListPathResult CanReach(Vector2 start, Vector2 destination)
    {
        if (AStarPathfinder.Instance == null)
        {
            return new ListPathResult(false, 0f);
        }

        var path = AStarPathfinder.Instance.FindPath(start, destination);
        return new ListPathResult(path != null && path.Count > 0, path != null ? path.Count : 0f);
    }

    private readonly struct ListPathResult
    {
        public readonly bool reachable;
        public readonly float distance;

        public ListPathResult(bool reachable, float distance)
        {
            this.reachable = reachable;
            this.distance = distance;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsOccupied ? Color.red : Color.cyan;
        Gizmos.DrawWireSphere(Position, 0.2f);
    }
}
