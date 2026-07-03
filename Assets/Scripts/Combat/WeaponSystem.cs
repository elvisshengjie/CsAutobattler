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

    public bool IsReady => Time.time >= nextAttackTime;
    public float LastShotTime { get; private set; } = Mathf.NegativeInfinity;
    public float TimeSinceLastShot => Time.time - LastShotTime;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponentInChildren<Animator>();
    }

    public void TryAttack(GameObject target)
    {
        if (target == null || !HasClearShot(target))
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

        LastShotTime = Time.time;

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

        if (targetHealth == null || targetHealth.IsDead || !HasClearShot(target))
        {
            yield break;
        }

        FireProjectile(target);
    }

    private void FireProjectile(GameObject target)
    {
        Vector3 targetPoint = GetAimPoint(target);
        Vector3 flatDirection = GetFlatAimDirection(targetPoint);

        float normalizedAccuracy = Mathf.Clamp01(stats.accuracy / 100f);
        float spread = maximumSpreadDegrees * (1f - normalizedAccuracy);
        float randomYaw = Random.Range(-spread, spread);
        Vector3 shotDirection = Quaternion.AngleAxis(randomYaw, Vector3.up) * flatDirection;

        Vector3 spawnPosition = GetMuzzlePosition(shotDirection);

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

        projectile.Initialize(shotDirection, stats.damage, stats.team, gameObject);
    }

    /// <summary>
    /// Verifies the same corridor the projectile will use. Perception rays originate
    /// at eye height, so they are not sufficient when the muzzle is beside cover.
    /// </summary>
    public bool HasClearShot(GameObject target)
    {
        if (target == null)
        {
            return false;
        }

        HealthSystem targetHealth = target.GetComponent<HealthSystem>();
        if (targetHealth == null || targetHealth.IsDead)
        {
            return false;
        }

        Vector3 targetPoint = GetAimPoint(target);
        Vector3 shotDirection = GetFlatAimDirection(targetPoint);
        Vector3 origin = GetMuzzlePosition(shotDirection);
        Vector3 destination = targetPoint;
        destination.y = origin.y;
        Vector3 path = destination - origin;
        float distance = path.magnitude;
        if (distance <= 0.001f)
        {
            return true;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            0.08f,
            path / distance,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            AgentStats hitAgent = hit.collider.GetComponentInParent<AgentStats>();
            if (hitAgent != null && stats != null && hitAgent.team == stats.team)
            {
                // Friendly agents do not consume projectiles, matching BulletProjectile.
                continue;
            }

            return hitAgent != null && hitAgent.gameObject == target;
        }

        return false;
    }

    private Vector3 GetFlatAimDirection(Vector3 targetPoint)
    {
        Vector3 direction = targetPoint - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        return direction.normalized;
    }

    private Vector3 GetMuzzlePosition(Vector3 shotDirection)
    {
        Collider shooterCollider = GetComponent<Collider>();
        Vector3 shooterCenter = shooterCollider != null
            ? shooterCollider.bounds.center
            : transform.position + Vector3.up * 0.8f;

        return shooterCenter
            + Vector3.up * muzzleOffset.y
            + shotDirection * muzzleOffset.z
            + transform.right * muzzleOffset.x;
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
