using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class BulletProjectile : MonoBehaviour
{
    public float speed = 8f;
    public float maxLifetime = 2f;
    public float defaultColliderRadius = 0.08f;

    private GameObject target;
    private float damage;
    private TeamType ownerTeam;
    private float spawnTime;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        Collider2D projectileCollider = GetComponent<Collider2D>();

        if (projectileCollider == null)
        {
            CircleCollider2D circleCollider = gameObject.AddComponent<CircleCollider2D>();
            circleCollider.radius = defaultColliderRadius;
            projectileCollider = circleCollider;
        }

        projectileCollider.isTrigger = true;
    }

    public void Initialize(GameObject newTarget, float newDamage, TeamType newOwnerTeam)
    {
        target = newTarget;
        damage = newDamage;
        ownerTeam = newOwnerTeam;
        spawnTime = Time.time;
        AimAtTarget();
    }

    private void FixedUpdate()
    {
        if (target == null || Time.time >= spawnTime + maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        AimAtTarget();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamage(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryDamage(collision.gameObject);
    }

    private void AimAtTarget()
    {
        Vector2 direction = target.transform.position - transform.position;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        rb.linearVelocity = direction.normalized * speed;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void TryDamage(GameObject hitObject)
    {
        AgentStats targetStats = hitObject.GetComponent<AgentStats>();

        if (targetStats == null || targetStats.team == ownerTeam)
        {
            return;
        }

        HealthSystem targetHealth = hitObject.GetComponent<HealthSystem>();

        if (targetHealth != null && !targetHealth.IsDead)
        {
            targetHealth.TakeDamage(damage);
        }

        Destroy(gameObject);
    }
}
