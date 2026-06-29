using UnityEngine;

/// <summary>
/// Owns perception only: FOV, proximity awareness, and wall-blocked line of sight.
/// </summary>
public class AgentSensors : MonoBehaviour
{
    public float sightRange = 60f;

    [Range(1f, 360f)]
    public float fieldOfViewAngle = 90f;

    public float proximityDetectionRange = 7f;
    public float eyeHeight = 0.8f;
    public LayerMask lineOfSightMask = ~0;

    private AgentStats stats;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
    }

    public GameObject FindClosestDetectedEnemy()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        GameObject closestEnemy = null;
        float closestDistance = Mathf.Infinity;

        foreach (AgentStats candidate in allAgents)
        {
            if (candidate == stats || candidate.team == stats.team)
            {
                continue;
            }

            HealthSystem health = candidate.GetComponent<HealthSystem>();
            if (health == null || health.IsDead)
            {
                continue;
            }

            float distance = FlatDistance(transform.position, candidate.transform.position);
            bool detectedNearby = distance <= proximityDetectionRange;
            bool detectedInCone = distance <= sightRange &&
                                  IsInsideFieldOfView(candidate.transform.position);

            if ((!detectedNearby && !detectedInCone) ||
                !HasLineOfSight(candidate.gameObject))
            {
                continue;
            }

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestEnemy = candidate.gameObject;
            }
        }

        return closestEnemy;
    }

    public bool HasLineOfSight(GameObject target)
    {
        if (target == null)
        {
            return false;
        }

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Collider targetCollider = target.GetComponent<Collider>();
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

    public bool IsInsideFieldOfView(Vector3 targetPosition)
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

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
