using System.Collections;
using UnityEngine;

public class WeaponSystem : MonoBehaviour
{
    [Header("Ammunition")]
    [Min(1)] public int magazineSize = 10;
    [Min(0)] public int currentAmmo = 10;
    [Min(0f)] public float reloadDuration = 2f;

    [Header("Projectile")]
    public GameObject projectilePrefab;
    public Vector2 muzzleOffset = new Vector2(0.35f, 0.15f);
    public float projectileSpawnDelay = 0.15f;

    private AgentStats stats;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private AgentPerception perception;
    private float nextAttackTime = 0f;

    public bool IsReloading { get; private set; }
    public float AmmoNormalized => magazineSize > 0 ? (float)currentAmmo / magazineSize : 0f;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        perception = GetComponent<AgentPerception>();

        if (perception == null)
        {
            perception = gameObject.AddComponent<AgentPerception>();
        }

        currentAmmo = Mathf.Clamp(currentAmmo, 0, magazineSize);
    }

    public void TryAttack(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Time.time < nextAttackTime || IsReloading || currentAmmo <= 0)
        {
            return;
        }

        if (!IsAttackable(target))
        {
            return;
        }

        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }

        currentAmmo--;
        nextAttackTime = Time.time + stats.attackCooldown;

        if (projectilePrefab != null)
        {
            StartCoroutine(FireProjectileAfterDelay(target));
            return;
        }

        HealthSystem targetHealth = target.GetComponent<HealthSystem>();

        if (targetHealth != null)
        {
            targetHealth.TakeDamage(stats.damage);
        }
    }

    public bool BeginReload()
    {
        if (IsReloading || currentAmmo >= magazineSize)
        {
            return false;
        }

        StartCoroutine(ReloadRoutine());
        return true;
    }

    private IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        yield return new WaitForSeconds(reloadDuration);
        currentAmmo = magazineSize;
        IsReloading = false;
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

        if (!IsAttackable(target))
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
            Vector2 shotDirection =
                (Vector2)target.transform.position - (Vector2)spawnPosition;
            projectile.Initialize(shotDirection, stats.damage, stats.team);
        }
    }

    private bool IsAttackable(GameObject target)
    {
        if (target == null || stats == null || perception == null)
        {
            return false;
        }

        AgentStats targetStats = target.GetComponent<AgentStats>();

        if (targetStats == null)
        {
            return false;
        }

        float distance = Vector2.Distance(transform.position, target.transform.position);
        return distance <= stats.attackRange && perception.CanSeeEnemy(targetStats);
    }
}
