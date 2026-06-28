using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AgentStats), typeof(Rigidbody2D))]
public class AgentMovement : MonoBehaviour
{
    public float pathRefreshTime = 0.5f;
    public float waypointReachDistance = 0.15f;

    [Header("Avoidance")]
    public bool useAvoidance = true;
    [Tooltip("Disable this to guarantee agents cannot physically overlap.")]
    public bool ignoreAgentCollisions = false;
    public float avoidanceRadius = 1.1f;
    public float avoidanceWeight = 1.5f;
    [Tooltip("Side-step force used when another agent is directly ahead.")]
    public float sideStepWeight = 0.8f;
    [Tooltip("Hard minimum center-to-center spacing. Movement will not enter this radius.")]
    public float minimumAgentSpacing = 0.9f;

    [Header("Wall Steering")]
    public LayerMask obstacleMask;
    [Min(0f)] public float wallSkin = 0.03f;
    public bool drawCurrentPath = true;

    [Header("Stuck Detection")]
    public float stuckCheckInterval = 0.5f;
    public float stuckDistanceThreshold = 0.05f;
    public float maxStuckDuration = 1.5f;

    private AgentStats stats;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Collider2D bodyCollider;
    private List<Vector2> currentPath;
    private int waypointIndex;
    private float nextPathRefreshTime;
    private Vector2 destination;

    private Vector2 lastStuckPosition;
    private float lastStuckPositionTime;
    private float nextStuckCheckTime;
    private float avoidanceSuppressedUntil;
    private int stuckCount = 0;

    private struct UnreachableRecord
    {
        public Vector2 position;
        public float expireTime;
    }
    private List<UnreachableRecord> unreachablePositions = new List<UnreachableRecord>();

    public bool HasDestination { get; private set; }
    public Vector2 Destination => destination;

    public void RegisterUnreachablePosition(Vector2 pos)
    {
        unreachablePositions.RemoveAll(p => Vector2.Distance(p.position, pos) < 0.5f);
        unreachablePositions.Add(new UnreachableRecord { position = pos, expireTime = Time.time + 5f });
    }

    public bool IsNearUnreachablePosition(Vector2 pos)
    {
        unreachablePositions.RemoveAll(p => Time.time > p.expireTime);
        foreach (var p in unreachablePositions)
        {
            if (Vector2.Distance(p.position, pos) < 1.0f)
            {
                return true;
            }
        }
        return false;
    }

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();

        if (obstacleMask.value == 0)
        {
            obstacleMask = LayerMask.GetMask("Obstacle");
        }
    }

    private void Start()
    {
        if (ignoreAgentCollisions)
        {
            IgnoreOtherAgentCollisions();
        }
    }

    private void Update()
    {
        if (HasDestination)
        {
            if (Time.time >= nextPathRefreshTime)
            {
                RefreshPath();
                nextPathRefreshTime = Time.time + pathRefreshTime;
            }

            if (Time.time >= nextStuckCheckTime)
            {
                CheckIfStuck();
                nextStuckCheckTime = Time.time + stuckCheckInterval;
            }
        }
    }

    private void FixedUpdate()
    {
        FollowPath();
    }

    public void MoveTo(Vector2 newDestination)
    {
        if (HasDestination && Vector2.Distance(destination, newDestination) < 0.1f)
        {
            return;
        }

        destination = newDestination;
        HasDestination = true;
        stuckCount = 0;

        lastStuckPosition = rb.position;
        lastStuckPositionTime = Time.time;
        nextStuckCheckTime = Time.time + stuckCheckInterval;

        RefreshPath();
    }

    public void Stop()
    {
        HasDestination = false;
        currentPath = null;
        waypointIndex = 0;
        stuckCount = 0;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    public void Face(Vector2 worldPosition)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        float directionX = worldPosition.x - transform.position.x;
        if (Mathf.Abs(directionX) > 0.01f)
        {
            spriteRenderer.flipX = directionX < 0f;
        }
    }

    private void RefreshPath()
    {
        if (AStarPathfinder.Instance == null)
        {
            return;
        }

        currentPath = AStarPathfinder.Instance.FindPath(rb.position, destination);
        waypointIndex = 0;

        if (currentPath == null || currentPath.Count == 0)
        {
            RegisterUnreachablePosition(destination);
            Stop();
        }
    }

    private void CheckIfStuck()
    {
        if (currentPath == null || currentPath.Count == 0)
        {
            return;
        }

        float distanceMoved = Vector2.Distance(rb.position, lastStuckPosition);
        if (distanceMoved > stuckDistanceThreshold)
        {
            lastStuckPosition = rb.position;
            lastStuckPositionTime = Time.time;
            stuckCount = 0;
        }
        else if (Time.time - lastStuckPositionTime >= maxStuckDuration)
        {
            stuckCount++;
            if (stuckCount >= 2)
            {
                Debug.Log(gameObject.name + " permanently stuck. Stopping and registering destination as unreachable.");
                RegisterUnreachablePosition(destination);
                Stop();
            }
            else
            {
                Debug.Log(gameObject.name + " detected stuck. Replanning with avoidance temporarily suppressed.");
                avoidanceSuppressedUntil = Time.time + 1.5f;
                lastStuckPosition = rb.position;
                lastStuckPositionTime = Time.time;
                RefreshPath();
            }
        }
    }

    private void FollowPath()
    {
        if (!HasDestination || currentPath == null || waypointIndex >= currentPath.Count)
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        Vector2 waypoint = currentPath[waypointIndex];

        if (Vector2.Distance(rb.position, waypoint) <= waypointReachDistance)
        {
            waypointIndex++;

            if (waypointIndex >= currentPath.Count)
            {
                Stop();
                return;
            }

            waypoint = currentPath[waypointIndex];
        }

        Vector2 direction = (waypoint - rb.position).normalized;

        if (useAvoidance && Time.time >= avoidanceSuppressedUntil)
        {
            Vector2 avoidance = Vector2.zero;
            int avoidCount = 0;
            Collider2D[] colliders = Physics2D.OverlapCircleAll(rb.position, avoidanceRadius);

            foreach (var col in colliders)
            {
                if (col.gameObject == gameObject)
                {
                    continue;
                }

                AgentStats otherStats = col.GetComponent<AgentStats>();
                if (otherStats != null)
                {
                    HealthSystem otherHealth = otherStats.GetComponent<HealthSystem>();
                    if (otherHealth != null && otherHealth.IsDead)
                    {
                        continue;
                    }

                    Vector2 otherPos = col.attachedRigidbody != null ? col.attachedRigidbody.position : (Vector2)col.transform.position;
                    Vector2 away = rb.position - otherPos;
                    float distance = away.magnitude;

                    if (distance < 0.01f)
                    {
                        away = UnityEngine.Random.insideUnitCircle.normalized;
                        distance = 0.1f;
                    }

                    float strength = 1f - (distance / avoidanceRadius);
                    avoidance += away.normalized * strength;

                    // Agents approaching head-on choose opposite world-space sides
                    // because their travel directions are opposite. This avoids a
                    // symmetric stop-and-push deadlock in narrow routes.
                    Vector2 toOther = otherPos - rb.position;
                    if (toOther.sqrMagnitude > 0.001f &&
                        Vector2.Dot(direction, toOther.normalized) > 0.35f)
                    {
                        avoidance += new Vector2(-direction.y, direction.x) * strength * sideStepWeight;
                    }

                    avoidCount++;
                }
            }

            if (avoidCount > 0)
            {
                avoidance /= avoidCount;
                direction = (direction + avoidance * avoidanceWeight).normalized;
            }
        }

        Face(waypoint);

        float movementStep = stats.moveSpeed * Time.fixedDeltaTime;
        direction = GetWallSafeDirection(direction, movementStep);
        rb.linearVelocity = direction * stats.moveSpeed;
    }

    private Vector2 GetWallSafeDirection(Vector2 desiredDirection, float movementStep)
    {
        if (desiredDirection.sqrMagnitude <= 0.001f || obstacleMask.value == 0)
        {
            return desiredDirection;
        }

        float radius = 0.2f;
        if (bodyCollider != null)
        {
            Bounds bounds = bodyCollider.bounds;
            radius = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.y) * 0.9f);
        }

        float checkDistance = movementStep + wallSkin;
        RaycastHit2D hit = Physics2D.CircleCast(
            rb.position,
            radius,
            desiredDirection,
            checkDistance,
            obstacleMask);

        if (hit.collider == null)
        {
            return desiredDirection;
        }

        // Remove the component pointing into the wall. The remaining tangent
        // lets the Rigidbody slide around corners toward the next A* waypoint.
        Vector2 slideDirection = desiredDirection -
            Vector2.Dot(desiredDirection, hit.normal) * hit.normal;

        if (slideDirection.sqrMagnitude <= 0.001f)
        {
            return Vector2.zero;
        }

        slideDirection.Normalize();
        RaycastHit2D slideHit = Physics2D.CircleCast(
            rb.position,
            radius,
            slideDirection,
            checkDistance,
            obstacleMask);

        return slideHit.collider == null ? slideDirection : Vector2.zero;
    }

    private void IgnoreOtherAgentCollisions()
    {
        Collider2D ownCollider = GetComponent<Collider2D>();
        if (ownCollider == null)
        {
            return;
        }

        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (agent.gameObject == gameObject)
            {
                continue;
            }

            Collider2D otherCollider = agent.GetComponent<Collider2D>();
            if (otherCollider != null)
            {
                Physics2D.IgnoreCollision(ownCollider, otherCollider, true);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawCurrentPath || currentPath == null || currentPath.Count == 0)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Vector2 previous = Application.isPlaying && rb != null
            ? rb.position
            : (Vector2)transform.position;

        for (int i = waypointIndex; i < currentPath.Count; i++)
        {
            Vector2 waypoint = currentPath[i];
            Gizmos.DrawLine(previous, waypoint);
            Gizmos.DrawWireSphere(waypoint, 0.08f);
            previous = waypoint;
        }
    }
}
