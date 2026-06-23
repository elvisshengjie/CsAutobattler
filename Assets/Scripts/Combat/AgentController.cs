using UnityEngine;

public class AgentController : MonoBehaviour
{
    [Header("Obstacle Avoidance")]
    public LayerMask obstacleMask;
    public float obstacleCheckDistance = 0.8f;
    public float obstacleCheckRadius = 0.25f;
    public float avoidanceStrength = 1.2f;

    private AgentStats stats;
    private WeaponSystem weapon;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;

    private GameObject currentTarget;
    private Vector2 moveDirection;

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
            moveDirection = Vector2.zero;
            return;
        }

        FaceTarget(currentTarget);

        float distance = Vector2.Distance(transform.position, currentTarget.transform.position);

        if (distance > stats.attackRange)
        {
            moveDirection = GetMoveDirection(currentTarget);
        }
        else
        {
            moveDirection = Vector2.zero;
            weapon.TryAttack(currentTarget);
        }
    }

    private void FixedUpdate()
    {
        if (rb == null)
        {
            return;
        }

        if (moveDirection == Vector2.zero)
        {
            return;
        }

        Vector2 newPosition = rb.position + moveDirection * stats.moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(newPosition);
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

            float distance = Vector2.Distance(transform.position, agent.transform.position);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestEnemy = agent.gameObject;
            }
        }

        return closestEnemy;
    }

    private Vector2 GetMoveDirection(GameObject target)
    {
        Vector2 currentPosition = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 targetPosition = target.transform.position;

        Vector2 directDirection = (targetPosition - currentPosition).normalized;

        RaycastHit2D hit = Physics2D.CircleCast(
            currentPosition,
            obstacleCheckRadius,
            directDirection,
            obstacleCheckDistance,
            obstacleMask
        );

        if (hit.collider == null)
        {
            return directDirection;
        }

        Vector2 perpendicularA = new Vector2(-directDirection.y, directDirection.x);
        Vector2 perpendicularB = new Vector2(directDirection.y, -directDirection.x);

        Vector2 optionA = (directDirection + perpendicularA * avoidanceStrength).normalized;
        Vector2 optionB = (directDirection + perpendicularB * avoidanceStrength).normalized;

        Vector2 testA = currentPosition + optionA;
        Vector2 testB = currentPosition + optionB;

        float distanceA = Vector2.Distance(testA, targetPosition);
        float distanceB = Vector2.Distance(testB, targetPosition);

        if (distanceA < distanceB)
        {
            return optionA;
        }

        return optionB;
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