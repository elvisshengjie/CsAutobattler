using System.Collections;
using UnityEngine;

public class WeaponSystem : MonoBehaviour
{
    [Header("Projectile")]
    public GameObject projectilePrefab;
    public Vector3 muzzleOffset = new Vector3(0f, 0.8f, 0.6f);
    public float projectileSpawnDelay = 0.15f;

    [Tooltip("Maximum horizontal aim error when accuracy is 0. Accuracy 100 has no spread.")]
    [Range(0f, 45f)]
    public float maximumSpreadDegrees = 15f;

    private AgentStats stats;
    private Animator animator;
    private float nextAttackTime = 0f;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponentInChildren<Animator>();
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

        // A missing prefab is valid in the prototype: create a simple ball at runtime.
        StartCoroutine(FireProjectileAfterDelay(target));
        nextAttackTime = Time.time + stats.attackCooldown;
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
        Vector3 targetPoint = GetAimPoint(target);
        Vector3 flatDirection = targetPoint - transform.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude < 0.001f)
        {
            flatDirection = transform.forward;
        }

        flatDirection.Normalize();

        float normalizedAccuracy = Mathf.Clamp01(stats.accuracy / 100f);
        float spread = maximumSpreadDegrees * (1f - normalizedAccuracy);
        float randomYaw = Random.Range(-spread, spread);
        Vector3 shotDirection = Quaternion.AngleAxis(randomYaw, Vector3.up) * flatDirection;

        Vector3 spawnPosition = transform.position
            + Vector3.up * muzzleOffset.y
            + shotDirection * muzzleOffset.z
            + transform.right * muzzleOffset.x;

        // Aim at the target's vertical center while preserving the horizontal accuracy spread.
        shotDirection.y = targetPoint.y - spawnPosition.y;
        shotDirection.Normalize();

        GameObject projectileObject = projectilePrefab != null
            ? Instantiate(projectilePrefab, spawnPosition, Quaternion.identity)
            : new GameObject("BallProjectile");

        projectileObject.transform.position = spawnPosition;
        projectileObject.transform.localScale = Vector3.one;

        BulletProjectile projectile = projectileObject.GetComponent<BulletProjectile>();
        if (projectile == null)
        {
            projectile = projectileObject.AddComponent<BulletProjectile>();
        }

        projectile.Initialize(shotDirection, stats.damage, stats.team, gameObject);
    }

    private Vector3 GetAimPoint(GameObject target)
    {
        Collider targetCollider = target.GetComponentInChildren<Collider>();
        if (targetCollider != null)
        {
            return targetCollider.bounds.center;
        }

        return target.transform.position + Vector3.up * 0.8f;
    }
}
