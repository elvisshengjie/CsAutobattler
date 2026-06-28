using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class BulletProjectile : MonoBehaviour
{
    public float speed = 8f;
    public float maxLifetime = 2f;
    public float defaultColliderRadius = 0.08f;

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

    public void Initialize(Vector2 direction, float newDamage, TeamType newOwnerTeam)
    {
        damage = newDamage;
        ownerTeam = newOwnerTeam;
        spawnTime = Time.time;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            Destroy(gameObject);
            return;
        }

        direction.Normalize();
        rb.linearVelocity = direction * speed;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void FixedUpdate()
    {
        if (Time.time >= spawnTime + maxLifetime)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamage(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryDamage(collision.gameObject);
    }

    private void TryDamage(GameObject hitObject)
    {
        if (hitObject.layer == LayerMask.NameToLayer("Obstacle"))
        {
            Destroy(gameObject);
            return;
        }

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
