using System.Collections;
using UnityEngine;

public class WeaponSystem : MonoBehaviour
{
    private const int ShotAudioSourceCount = 4;

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
    private AudioSource[] shotAudioSources;
    private int[] shotAudioGenerations;
    private int nextShotAudioSource;
    private int lastShotVariant = -1;
    private WeaponType lastAudioWeaponType = (WeaponType)(-1);

    public bool IsReady => Time.time >= nextAttackTime && !isReloading;
    public bool IsReloading => isReloading;
    public float LastShotTime { get; private set; } = Mathf.NegativeInfinity;
    public float TimeSinceLastShot => Time.time - LastShotTime;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponentInChildren<Animator>();
        loadout = WeaponLoadout.Get(gameObject);
        currentAmmo = loadout.magazineSize;
        CreateShotAudioSources();
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

        if (!CombatTargetUtility.IsAlive(target))
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

            if (!CombatTargetUtility.IsAlive(target))
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
        PlayShotSound();

        int projectileCount = Mathf.Max(1, loadout.ProjectilesPerShot);
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            FireProjectile(target, projectileCount > 1);
        }
    }

    private void CreateShotAudioSources()
    {
        shotAudioSources = new AudioSource[ShotAudioSourceCount];
        shotAudioGenerations = new int[ShotAudioSourceCount];

        for (int index = 0; index < ShotAudioSourceCount; index++)
        {
            GameObject sourceObject = new GameObject("Weapon Shot Audio " + (index + 1));
            sourceObject.transform.SetParent(transform, false);

            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 3f;
            source.maxDistance = 35f;
            shotAudioSources[index] = source;
        }
    }

    private void PlayShotSound()
    {
        WeaponAudioLibrary library = WeaponAudioLibrary.Instance;
        if (library == null || loadout == null)
        {
            return;
        }

        WeaponType weaponType = loadout.SelectedWeapon;
        AudioClip[] clips = library.GetShotClips(weaponType);
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        if (shotAudioSources == null || shotAudioSources.Length == 0)
        {
            CreateShotAudioSources();
        }

        if (lastAudioWeaponType != weaponType)
        {
            lastAudioWeaponType = weaponType;
            lastShotVariant = -1;
        }

        int variant = Random.Range(0, clips.Length);
        if (clips.Length > 1 && variant == lastShotVariant)
        {
            variant = (variant + Random.Range(1, clips.Length)) % clips.Length;
        }

        lastShotVariant = variant;
        AudioClip clip = clips[variant];
        if (clip == null)
        {
            return;
        }

        int sourceIndex = nextShotAudioSource;
        nextShotAudioSource = (nextShotAudioSource + 1) % shotAudioSources.Length;
        AudioSource source = shotAudioSources[sourceIndex];
        int generation = ++shotAudioGenerations[sourceIndex];

        source.Stop();
        source.clip = clip;
        source.volume = WeaponAudioLibrary.GetVolume(weaponType) * Random.Range(0.96f, 1.04f);
        source.pitch = WeaponAudioLibrary.GetBasePitch(weaponType) * Random.Range(0.98f, 1.02f);
        source.Play();

        float maximumDuration = WeaponAudioLibrary.GetMaximumDuration(weaponType);
        if (maximumDuration > 0f)
        {
            StartCoroutine(FadeAndStopShot(sourceIndex, generation, maximumDuration));
        }
    }

    private IEnumerator FadeAndStopShot(int sourceIndex, int generation, float duration)
    {
        const float fadeDuration = 0.06f;
        AudioSource source = shotAudioSources[sourceIndex];
        float startingVolume = source.volume;

        yield return new WaitForSeconds(Mathf.Max(0f, duration - fadeDuration));

        float elapsed = 0f;
        while (elapsed < fadeDuration &&
               shotAudioGenerations[sourceIndex] == generation)
        {
            elapsed += Time.deltaTime;
            source.volume = startingVolume * (1f - Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        if (shotAudioGenerations[sourceIndex] == generation)
        {
            source.Stop();
            source.volume = startingVolume;
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
