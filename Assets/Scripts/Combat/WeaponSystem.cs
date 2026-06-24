using System.Collections;
using UnityEngine;

public class WeaponSystem : MonoBehaviour
{
    [Header("Projectile")]
    public GameObject projectilePrefab;
    public Vector2 muzzleOffset = new Vector2(0.35f, 0.15f);
    public float projectileSpawnDelay = 0.15f;

    private AgentStats stats;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private float nextAttackTime = 0f;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void TryAttack(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }

        if (projectilePrefab != null)
        {
            StartCoroutine(FireProjectileAfterDelay(target));
            nextAttackTime = Time.time + stats.attackCooldown;
            return;
        }

        HealthSystem targetHealth = target.GetComponent<HealthSystem>();

        if (targetHealth != null)
        {
            targetHealth.TakeDamage(stats.damage);
            nextAttackTime = Time.time + stats.attackCooldown;
        }
    }

    private IEnumerator FireProjectileAfterDelay(GameObject target)
    {
        if (projectileSpawnDelay > 0f)
        {
            yield return new WaitForSeconds(projectileSpawnDelay);
        }

        if (target == null)
        {
            yield break;
        }

        HealthSystem targetHealth = target.GetComponent<HealthSystem>();

        if (targetHealth == null || targetHealth.IsDead)
        {
            yield break;
        }

        FireProjectile(target);
    }

    private void FireProjectile(GameObject target)
    {
        Vector2 spawnOffset = muzzleOffset;

        if (spriteRenderer != null && spriteRenderer.flipX)
        {
            spawnOffset.x *= -1f;
        }

        Vector3 spawnPosition = transform.position + (Vector3)spawnOffset;
        GameObject projectileObject = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
        BulletProjectile projectile = projectileObject.GetComponent<BulletProjectile>();

        if (projectile != null)
        {
            projectile.Initialize(target, stats.damage, stats.team);
        }
    }
}
