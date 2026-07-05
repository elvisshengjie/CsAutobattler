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
    private WeaponLoadout loadout;
    private float nextAttackTime = 0f;

    private int currentAmmo;
    private bool isReloading = false;

    public bool IsReady => Time.time >= nextAttackTime;
    public float LastShotTime { get; private set; } = Mathf.NegativeInfinity;
    public float TimeSinceLastShot => Time.time - LastShotTime;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponentInChildren<Animator>();
        loadout = WeaponLoadout.Get(gameObject);
        currentAmmo = loadout.magazineSize;
    }

    public void TryAttack(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (isReloading) {
            return;
        }

        if (currentAmmo <= 0) {
            StartCoroutine(ReloadCoroutine());
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

        LastShotTime = Time.time;

        StartCoroutine(FireProjectileAfterDelay(target));
        nextAttackTime = Time.time + loadout.FireCooldown;
    }

    private IEnumerator ReloadCoroutine() 
    {
        isReloading = true;
        AgentHealthBar3D healthBar = GetComponent<AgentHealthBar3D>();
        healthBar?.SetActionStatus("Reloading", loadout.ReloadTime);

        yield return new WaitForSeconds(loadout.ReloadTime);

        currentAmmo = loadout.magazineSize;
        isReloading = false;
        healthBar?.ClearActionStatus();
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

        int burstCount = Mathf.Max(1, loadout.BurstCount);
        for (int burstIndex = 0; burstIndex < burstCount; burstIndex++)
        {
            if (currentAmmo <= 0) 
            {
                if (!isReloading) StartCoroutine(ReloadCoroutine());
                yield break;
            }

            if (target == null)
            {
                yield break;
            }

            HealthSystem currentHealth = target.GetComponent<HealthSystem>();
            if (currentHealth == null || currentHealth.IsDead)
            {
                yield break;
            }

            FireVolley(target);
            currentAmmo--;
            if (burstIndex + 1 < burstCount)
            {
                yield return new WaitForSeconds(loadout.BurstInterval);
            }
        }
    }

    private void FireVolley(GameObject target)
    {
        int projectileCount = Mathf.Max(1, loadout.ProjectilesPerShot);
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            FireProjectile(target, projectileCount > 1);
        }
    }

    private void FireProjectile(GameObject target, bool useFullSpread)
    {
        Vector3 targetPoint = GetAimPoint(target);
        Vector3 flatDirection = targetPoint - transform.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude < 0.001f)
        {
            flatDirection = transform.forward;
        }

        flatDirection.Normalize();

        float normalizedAccuracy = Mathf.Clamp01(loadout.Accuracy / 100f);
        float distanceRatio = Mathf.Clamp01(Vector3.Distance(transform.position, targetPoint) /
                                             Mathf.Max(0.1f, loadout.MaximumRange));
        float rangeAccuracyPenalty = Mathf.Lerp(0.65f, 1.5f, distanceRatio);
        float spread = useFullSpread
            ? loadout.SpreadDegrees
            : loadout.SpreadDegrees * (1f - normalizedAccuracy) * rangeAccuracyPenalty;
        float randomYaw = Random.Range(-spread, spread);
        Vector3 shotDirection = Quaternion.AngleAxis(randomYaw, Vector3.up) * flatDirection;

        Collider shooterCollider = GetComponent<Collider>();
        Vector3 shooterCenter = shooterCollider != null
            ? shooterCollider.bounds.center
            : transform.position + Vector3.up * 0.8f;

        Vector3 spawnPosition = shooterCenter
            + Vector3.up * muzzleOffset.y
            + shotDirection * muzzleOffset.z
            + transform.right * muzzleOffset.x;

        // The prototype map is flat. Keep shots horizontal so their sphere collider
        // cannot drift downward and collide with the floor before reaching the target.
        shotDirection.y = 0f;
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

        projectile.Initialize(shotDirection, loadout.Damage, stats.team, gameObject,
            loadout.ProjectileSpeed, loadout.MaximumRange, loadout.MinimumDamageMultiplier);
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
