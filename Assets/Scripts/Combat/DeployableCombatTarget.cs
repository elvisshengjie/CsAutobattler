using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridges normal agents and destructible deployables without making a deployable
/// count as a living team member.
/// </summary>
public static class CombatTargetUtility
{
    public static GameObject GetRoot(GameObject candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        DeployableTurret turret = candidate.GetComponentInParent<DeployableTurret>();
        if (turret != null)
        {
            return turret.gameObject;
        }

        AgentStats agent = candidate.GetComponentInParent<AgentStats>();
        return agent != null ? agent.gameObject : candidate;
    }

    public static bool TryGetTeam(GameObject candidate, out TeamType team)
    {
        team = default;
        if (candidate == null)
        {
            return false;
        }

        DeployableTurret turret = candidate.GetComponentInParent<DeployableTurret>();
        if (turret != null)
        {
            team = turret.Team;
            return true;
        }

        AgentStats agent = candidate.GetComponentInParent<AgentStats>();
        if (agent == null)
        {
            return false;
        }

        team = agent.team;
        return true;
    }

    public static bool IsAlive(GameObject candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        DeployableTurret turret = candidate.GetComponentInParent<DeployableTurret>();
        if (turret != null)
        {
            return !turret.IsDestroyed;
        }

        HealthSystem health = candidate.GetComponentInParent<HealthSystem>();
        return health != null && !health.IsDead;
    }

    public static float GetNormalizedHealth(GameObject candidate)
    {
        if (candidate == null)
        {
            return 0f;
        }

        DeployableTurret turret = candidate.GetComponentInParent<DeployableTurret>();
        if (turret != null)
        {
            return turret.NormalizedHealth;
        }

        HealthSystem health = candidate.GetComponentInParent<HealthSystem>();
        return health != null ? health.NormalizedHealth : 0f;
    }

    public static bool TryApplyDamage(GameObject candidate, float damage, GameObject attacker)
    {
        if (candidate == null)
        {
            return false;
        }

        DeployableTurret turret = candidate.GetComponentInParent<DeployableTurret>();
        if (turret != null)
        {
            if (turret.IsDestroyed)
            {
                return false;
            }

            turret.TakeDamage(damage, attacker);
            return true;
        }

        HealthSystem health = candidate.GetComponentInParent<HealthSystem>();
        if (health == null || health.IsDead)
        {
            return false;
        }

        health.TakeDamage(damage, attacker);
        return true;
    }
}

/// <summary>
/// A temporary colored smoke volume. It has no collider and therefore never
/// affects navigation; perception asks the registry whether a sight line crosses it.
/// </summary>
public sealed class TacticalSmokeCloud : MonoBehaviour
{
    private static readonly List<TacticalSmokeCloud> ActiveClouds =
        new List<TacticalSmokeCloud>();
    private static Material redSmokeMaterial;
    private static Material blueSmokeMaterial;
    private static Material whiteSmokeMaterial;

    private float radius;
    private float expiresAt;
    private TeamType team;
    private readonly List<Transform> visualPuffs = new List<Transform>();
    private readonly List<Vector3> puffPositions = new List<Vector3>();
    private readonly List<Vector3> puffScales = new List<Vector3>();

    public float Radius => radius;
    public TeamType Team => team;

    public static TacticalSmokeCloud Deploy(
        Vector3 position,
        TeamType ownerTeam,
        float cloudRadius,
        float duration)
    {
        GameObject cloudObject = new GameObject(ownerTeam + " Tactical Smoke");
        cloudObject.transform.position = position;
        TacticalSmokeCloud cloud = cloudObject.AddComponent<TacticalSmokeCloud>();
        cloud.Initialize(ownerTeam, cloudRadius, duration);
        return cloud;
    }

    private void Initialize(TeamType ownerTeam, float cloudRadius, float duration)
    {
        team = ownerTeam;
        radius = Mathf.Max(0.5f, cloudRadius);
        expiresAt = Time.time + Mathf.Max(0.5f, duration);
        CreateVisuals();
    }

    private void OnEnable()
    {
        if (!ActiveClouds.Contains(this))
        {
            ActiveClouds.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveClouds.Remove(this);
    }

    private void Update()
    {
        AnimateVisuals();

        RoundManager round = RoundManager.Instance;
        if (Time.time >= expiresAt ||
            (round != null && (round.CurrentState == RoundState.RoundEnd ||
                               round.CurrentState == RoundState.Defused ||
                               round.CurrentState == RoundState.Exploded)))
        {
            Destroy(gameObject);
        }
    }

    public static bool BlocksLine(Vector3 start, Vector3 end)
    {
        for (int i = ActiveClouds.Count - 1; i >= 0; i--)
        {
            TacticalSmokeCloud cloud = ActiveClouds[i];
            if (cloud == null)
            {
                ActiveClouds.RemoveAt(i);
                continue;
            }

            Vector3 center = cloud.transform.position + Vector3.up * 1.05f;
            if (SegmentIntersectsSphere(start, end, center, cloud.radius))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasCloudNear(Vector3 position, float distance)
    {
        float distanceSquared = distance * distance;
        foreach (TacticalSmokeCloud cloud in ActiveClouds)
        {
            if (cloud != null &&
                (cloud.transform.position - position).sqrMagnitude <= distanceSquared)
            {
                return true;
            }
        }

        return false;
    }

    public static bool SegmentIntersectsSphere(
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float sphereRadius)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 0.0001f)
        {
            return (start - center).sqrMagnitude <= sphereRadius * sphereRadius;
        }

        float t = Mathf.Clamp01(Vector3.Dot(center - start, segment) / lengthSquared);
        Vector3 closest = start + segment * t;
        return (closest - center).sqrMagnitude <= sphereRadius * sphereRadius;
    }

    public static Color GetSmokeColor(TeamType ownerTeam)
    {
        return ownerTeam == TeamType.Red
            ? new Color(0.78f, 0.5f, 0.54f, 0.26f)
            : new Color(0.5f, 0.62f, 0.82f, 0.26f);
    }

    private void CreateVisuals()
    {
        Vector3[] offsets =
        {
            new Vector3(0f, 0.95f, 0f),
            new Vector3(0.42f, 0.75f, 0.15f),
            new Vector3(-0.4f, 0.82f, -0.2f),
            new Vector3(0.15f, 1.25f, -0.38f),
            new Vector3(-0.2f, 1.35f, 0.36f),
            new Vector3(0.5f, 1.15f, -0.25f),
            new Vector3(-0.5f, 1.1f, 0.22f),
            new Vector3(0.12f, 1.55f, 0.08f),
            new Vector3(-0.28f, 0.62f, 0.42f),
            new Vector3(0.34f, 0.68f, -0.44f)
        };

        Material teamMaterial = GetSmokeMaterial(team);
        Material whiteMaterial = GetWhiteSmokeMaterial();
        for (int i = 0; i < offsets.Length; i++)
        {
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "Smoke Puff";
            puff.transform.SetParent(transform, false);
            puff.transform.localPosition = offsets[i] * radius * 0.42f;
            float scale = radius * (i == 0 ? 1.15f : 0.82f + (i % 3) * 0.06f);
            puff.transform.localScale = new Vector3(scale, scale * 0.72f, scale);
            Collider collider = puff.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            Renderer renderer = puff.GetComponent<Renderer>();
            // A few neutral wisps break up the team tint and make the cloud read
            // as layered smoke rather than a single solid red/blue volume.
            renderer.sharedMaterial = i == 2 || i == 5 || i == 8
                ? whiteMaterial
                : teamMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            visualPuffs.Add(puff.transform);
            puffPositions.Add(puff.transform.localPosition);
            puffScales.Add(puff.transform.localScale);
        }
    }

    private void AnimateVisuals()
    {
        float time = Time.time;
        for (int i = 0; i < visualPuffs.Count; i++)
        {
            Transform puff = visualPuffs[i];
            if (puff == null)
            {
                continue;
            }

            float phase = i * 1.73f;
            Vector3 drift = new Vector3(
                Mathf.Sin(time * 0.34f + phase),
                Mathf.Sin(time * 0.46f + phase * 0.7f) * 0.65f,
                Mathf.Cos(time * 0.29f + phase)) * radius * 0.055f;
            float breathing = 1f + Mathf.Sin(time * 0.4f + phase) * 0.035f;
            puff.localPosition = puffPositions[i] + drift;
            puff.localScale = puffScales[i] * breathing;
        }
    }

    private static Material GetSmokeMaterial(TeamType ownerTeam)
    {
        ref Material material = ref ownerTeam == TeamType.Red
            ? ref redSmokeMaterial
            : ref blueSmokeMaterial;
        if (material != null)
        {
            return material;
        }

        material = CreateSmokeMaterial(
            ownerTeam + " Smoke Material",
            GetSmokeColor(ownerTeam));
        return material;
    }

    private static Material GetWhiteSmokeMaterial()
    {
        if (whiteSmokeMaterial != null)
        {
            return whiteSmokeMaterial;
        }

        whiteSmokeMaterial = CreateSmokeMaterial(
            "White Smoke Material",
            new Color(0.94f, 0.96f, 0.98f, 0.16f));
        return whiteSmokeMaterial;
    }

    private static Material CreateSmokeMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = materialName,
            color = color,
            renderQueue = 3000
        };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        return material;
    }
}

public sealed class SmokeGrenadeProjectile : MonoBehaviour
{
    private Vector3 start;
    private Vector3 destination;
    private TeamType team;
    private float radius;
    private float duration;
    private float travelTime;
    private float launchedAt;
    private static Material redMaterial;
    private static Material blueMaterial;

    public static void Throw(
        Vector3 origin,
        Vector3 target,
        TeamType ownerTeam,
        float cloudRadius,
        float cloudDuration,
        float flightDuration = 0.75f)
    {
        GameObject grenade = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        grenade.name = ownerTeam + " Smoke Grenade";
        grenade.transform.position = origin;
        grenade.transform.localScale = Vector3.one * 0.22f;
        Collider collider = grenade.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        SmokeGrenadeProjectile projectile = grenade.AddComponent<SmokeGrenadeProjectile>();
        projectile.start = origin;
        projectile.destination = target;
        projectile.team = ownerTeam;
        projectile.radius = cloudRadius;
        projectile.duration = cloudDuration;
        projectile.travelTime = Mathf.Max(0.15f, flightDuration);
        projectile.launchedAt = Time.time;
        grenade.GetComponent<Renderer>().sharedMaterial = GetMaterial(ownerTeam);
    }

    private void Update()
    {
        float progress = Mathf.Clamp01((Time.time - launchedAt) / travelTime);
        Vector3 position = Vector3.Lerp(start, destination, progress);
        position.y += Mathf.Sin(progress * Mathf.PI) * 2.1f;
        transform.position = position;
        transform.Rotate(420f * Time.deltaTime, 260f * Time.deltaTime, 0f);
        if (progress >= 1f)
        {
            TacticalSmokeCloud.Deploy(destination, team, radius, duration);
            Destroy(gameObject);
        }
    }

    private static Material GetMaterial(TeamType ownerTeam)
    {
        ref Material material = ref ownerTeam == TeamType.Red
            ? ref redMaterial
            : ref blueMaterial;
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader)
            {
                name = ownerTeam + " Smoke Grenade Material",
                color = ownerTeam == TeamType.Red
                    ? new Color(0.8f, 0.03f, 0.03f)
                    : new Color(0.03f, 0.18f, 0.85f)
            };
        }

        return material;
    }
}

/// <summary>
/// Stationary, destructible, limited-range sentry with a fixed firing cone.
/// </summary>
public sealed class DeployableTurret : MonoBehaviour
{
    [SerializeField] private TeamType team;
    [SerializeField] private float maximumHealth = 120f;
    [SerializeField] private float currentHealth;
    [SerializeField] private float range = 14f;
    [SerializeField, Range(10f, 160f)] private float fieldOfView = 75f;
    [SerializeField] private float damage = 8f;
    [SerializeField] private float fireCooldown = 0.7f;
    [SerializeField] private float projectileSpeed = 22f;

    private float nextTargetScan;
    private float nextFireTime;
    private GameObject target;
    private Transform barrel;
    private TextMesh statusText;
    private bool destroyed;
    private static Material redMaterial;
    private static Material blueMaterial;

    public TeamType Team => team;
    public bool IsDestroyed => destroyed;
    public float NormalizedHealth => maximumHealth <= 0f
        ? 0f
        : Mathf.Clamp01(currentHealth / maximumHealth);
    public float Range => range;
    public float FieldOfView => fieldOfView;

    public void Initialize(TeamType ownerTeam, float health, float attackRange,
        float firingArc)
    {
        team = ownerTeam;
        maximumHealth = Mathf.Max(1f, health);
        currentHealth = maximumHealth;
        range = Mathf.Max(1f, attackRange);
        fieldOfView = Mathf.Clamp(firingArc, 10f, 160f);
        CreateVisuals();
        UpdateStatusText();
    }

    private void Update()
    {
        if (destroyed)
        {
            return;
        }

        RoundManager round = RoundManager.Instance;
        if (round != null && (round.CurrentState == RoundState.RoundEnd ||
                              round.CurrentState == RoundState.Defused ||
                              round.CurrentState == RoundState.Exploded))
        {
            Destroy(gameObject);
            return;
        }

        if (Time.time >= nextTargetScan)
        {
            nextTargetScan = Time.time + 0.2f;
            target = FindTarget();
        }

        if (target != null)
        {
            Vector3 direction = target.transform.position - transform.position;
            direction.y = 0f;
            if (barrel != null && direction.sqrMagnitude > 0.01f)
            {
                barrel.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            if (Time.time >= nextFireTime && HasClearShot(target))
            {
                Fire(target);
            }
        }
    }

    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (destroyed || amount <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        UpdateStatusText();
        if (currentHealth <= 0f)
        {
            destroyed = true;
            Collider[] colliders = GetComponentsInChildren<Collider>();
            foreach (Collider collider in colliders)
            {
                collider.enabled = false;
            }

            Destroy(gameObject);
        }
    }

    public bool IsInsideFiringArc(Vector3 worldPosition)
    {
        Vector3 direction = worldPosition - transform.position;
        direction.y = 0f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return direction.sqrMagnitude <= range * range &&
               direction.sqrMagnitude > 0.001f &&
               Vector3.Angle(forward, direction) <= fieldOfView * 0.5f;
    }

    private GameObject FindTarget()
    {
        AgentStats[] candidates =
            FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        GameObject best = null;
        float bestDistance = Mathf.Infinity;
        foreach (AgentStats candidate in candidates)
        {
            if (candidate == null || candidate.team == team ||
                !CombatTargetUtility.IsAlive(candidate.gameObject) ||
                !IsInsideFiringArc(candidate.transform.position) ||
                !HasClearShot(candidate.gameObject))
            {
                continue;
            }

            float distance = FlatDistance(transform.position, candidate.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate.gameObject;
            }
        }

        return best;
    }

    private bool HasClearShot(GameObject candidate)
    {
        if (candidate == null || !CombatTargetUtility.IsAlive(candidate))
        {
            return false;
        }

        Vector3 origin = GetMuzzlePosition();
        Collider targetCollider = candidate.GetComponentInChildren<Collider>();
        Vector3 targetPoint = targetCollider != null
            ? targetCollider.bounds.center
            : candidate.transform.position + Vector3.up * 0.8f;
        if (TacticalSmokeCloud.BlocksLine(origin, targetPoint))
        {
            return false;
        }

        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit,
                distance, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return CombatTargetUtility.GetRoot(hit.collider.gameObject) == candidate;
    }

    private void Fire(GameObject candidate)
    {
        nextFireTime = Time.time + fireCooldown;
        Vector3 origin = GetMuzzlePosition();
        Collider targetCollider = candidate.GetComponentInChildren<Collider>();
        Vector3 targetPoint = targetCollider != null
            ? targetCollider.bounds.center
            : candidate.transform.position + Vector3.up * 0.8f;
        Vector3 direction = targetPoint - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        GameObject projectileObject = new GameObject(team + " Turret Projectile");
        projectileObject.transform.position = origin;
        BulletProjectile projectile = projectileObject.AddComponent<BulletProjectile>();
        projectile.Initialize(direction.normalized, damage, team, gameObject,
            projectileSpeed, range, 1f);
    }

    private Vector3 GetMuzzlePosition()
    {
        return barrel != null
            ? barrel.position + barrel.forward * 0.7f
            : transform.position + Vector3.up * 0.85f + transform.forward * 0.65f;
    }

    private void CreateVisuals()
    {
        if (barrel != null)
        {
            return;
        }

        BoxCollider hitbox = gameObject.AddComponent<BoxCollider>();
        hitbox.center = new Vector3(0f, 0.58f, 0f);
        hitbox.size = new Vector3(0.85f, 1.15f, 0.85f);

        Material material = GetMaterial(team);
        GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = "Turret Base";
        baseObject.transform.SetParent(transform, false);
        baseObject.transform.localPosition = new Vector3(0f, 0.22f, 0f);
        baseObject.transform.localScale = new Vector3(0.75f, 0.22f, 0.75f);
        RemoveColliderAndApplyMaterial(baseObject, material);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Turret Head";
        head.transform.SetParent(transform, false);
        head.transform.localPosition = new Vector3(0f, 0.72f, 0f);
        head.transform.localScale = new Vector3(0.7f, 0.42f, 0.65f);
        RemoveColliderAndApplyMaterial(head, material);

        GameObject barrelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrelObject.name = "Turret Barrel";
        barrelObject.transform.SetParent(transform, false);
        barrelObject.transform.localPosition = new Vector3(0f, 0.75f, 0.48f);
        barrelObject.transform.localScale = new Vector3(0.2f, 0.2f, 1.15f);
        RemoveColliderAndApplyMaterial(barrelObject, material);
        barrel = barrelObject.transform;

        GameObject statusObject = new GameObject("Turret Status");
        statusObject.transform.SetParent(transform, false);
        statusObject.transform.localPosition = new Vector3(0f, 1.45f, 0f);
        statusText = statusObject.AddComponent<TextMesh>();
        statusText.anchor = TextAnchor.MiddleCenter;
        statusText.alignment = TextAlignment.Center;
        statusText.fontStyle = FontStyle.Bold;
        statusText.fontSize = 48;
        statusText.characterSize = 0.035f;
        statusText.color = AgentRoleAbilities.GetWallColor(team);
    }

    private void LateUpdate()
    {
        if (statusText != null && Camera.main != null)
        {
            statusText.transform.rotation = Quaternion.LookRotation(
                statusText.transform.position - Camera.main.transform.position,
                Camera.main.transform.up);
        }
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        Physics.SyncTransforms();
        AStarPathfinder3D.Instance?.RefreshGrid();
    }

    private void UpdateStatusText()
    {
        if (statusText != null)
        {
            statusText.text = $"TURRET {Mathf.CeilToInt(currentHealth)}/{Mathf.CeilToInt(maximumHealth)}";
        }
    }

    private static void RemoveColliderAndApplyMaterial(GameObject part, Material material)
    {
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material GetMaterial(TeamType ownerTeam)
    {
        ref Material material = ref ownerTeam == TeamType.Red
            ? ref redMaterial
            : ref blueMaterial;
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader)
            {
                name = ownerTeam + " Turret Material",
                color = AgentRoleAbilities.GetWallColor(ownerTeam)
            };
        }

        return material;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
