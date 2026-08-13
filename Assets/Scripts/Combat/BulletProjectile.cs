using System.Collections.Generic;
using UnityEngine;

public class BulletProjectile : MonoBehaviour
{
    private enum ImpactTone
    {
        Natural,
        BrightMetal,
        HeavyWall
    }

    public float speed = 18f;
    public float maxLifetime = 3f;
    public float ballRadius = 0.1f;
    public Color ballColor = new Color(1f, 0.75f, 0.1f);

    private float damage;
    private TeamType ownerTeam;
    private GameObject owner;
    private Rigidbody rb;
    private bool initialized;
    private Vector3 previousPosition;
    private Vector3 originPosition;
    private float damageFalloffRange;
    private float minimumDamageMultiplier = 1f;
    private static Material sharedBallMaterial;
    private static readonly Dictionary<HealthSystem, int> LastFleshHitFrameByAgent =
        new Dictionary<HealthSystem, int>();
    private static int lastMetalHitFrame = -1;
    private static int lastWallHitFrame = -1;

    private void Awake()
    {
        // Destroy any leftover 2D physics components to prevent conflict with 3D Rigidbody
        Rigidbody2D rb2d = GetComponent<Rigidbody2D>();
        if (rb2d != null)
        {
            DestroyImmediate(rb2d);
        }
        Collider2D col2d = GetComponent<Collider2D>();
        if (col2d != null)
        {
            DestroyImmediate(col2d);
        }

        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.useGravity = false;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        SphereCollider sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = gameObject.AddComponent<SphereCollider>();
        }

        sphereCollider.radius = ballRadius;
        sphereCollider.isTrigger = true;

        EnsureBallVisual();
    }

    public void Initialize(
        Vector3 direction,
        float newDamage,
        TeamType newOwnerTeam,
        GameObject newOwner,
        float newSpeed = 18f,
        float newDamageFalloffRange = 0f,
        float newMinimumDamageMultiplier = 1f)
    {
        damage = newDamage;
        ownerTeam = newOwnerTeam;
        owner = newOwner;
        speed = Mathf.Max(0.1f, newSpeed);
        initialized = true;
        previousPosition = transform.position;
        originPosition = transform.position;
        damageFalloffRange = Mathf.Max(0f, newDamageFalloffRange);
        minimumDamageMultiplier = Mathf.Clamp01(newMinimumDamageMultiplier);

        Vector3 normalizedDirection = direction.sqrMagnitude > 0.001f
            ? direction.normalized
            : transform.forward;

        transform.forward = normalizedDirection;
        rb.linearVelocity = normalizedDirection * speed;
        Destroy(gameObject, maxLifetime);
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        Vector3 movement = transform.position - previousPosition;
        float distance = movement.magnitude;

        if (distance > 0.001f)
        {
            RaycastHit[] hits = Physics.SphereCastAll(
                previousPosition,
                ballRadius,
                movement / distance,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (TryDamage(hit.collider.gameObject))
                {
                    return;
                }
            }
        }

        previousPosition = transform.position;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized || other.isTrigger)
        {
            return;
        }

        TryDamage(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryDamage(collision.gameObject);
    }

    private bool TryDamage(GameObject hitObject)
    {
        if (!initialized ||
            hitObject == gameObject ||
            hitObject.transform.IsChildOf(transform) ||
            (owner != null &&
             (hitObject == owner || hitObject.transform.IsChildOf(owner.transform))))
        {
            return false;
        }

        if (CombatTargetUtility.TryGetTeam(hitObject, out TeamType targetTeam) &&
            targetTeam == ownerTeam)
        {
            return false;
        }

        bool hitLivingTarget = CombatTargetUtility.IsAlive(hitObject);
        if (hitLivingTarget)
        {
            HealthSystem agentHealth = hitObject.GetComponentInParent<HealthSystem>();
            DeployableTurret turret = hitObject.GetComponentInParent<DeployableTurret>();
            float appliedDamage = damage;
            if (damageFalloffRange > 0f && minimumDamageMultiplier < 1f)
            {
                float travelled = Vector3.Distance(originPosition, transform.position);
                float falloff = Mathf.Clamp01(travelled / damageFalloffRange);
                appliedDamage *= Mathf.Lerp(1f, minimumDamageMultiplier, falloff);
            }
            bool damaged = CombatTargetUtility.TryApplyDamage(
                hitObject, appliedDamage, owner);
            if (damaged && agentHealth != null)
            {
                PlayFleshHitSound(agentHealth);
            }
            else if (damaged && turret != null)
            {
                PlayMetalHitSound();
            }
        }
        else if (IsWallImpact(hitObject))
        {
            PlayWallHitSound();
        }

        // Any solid wall or enemy collision consumes the projectile.
        initialized = false;
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
        }
        Destroy(gameObject);
        return true;
    }

    private void PlayFleshHitSound(HealthSystem agentHealth)
    {
        if (LastFleshHitFrameByAgent.TryGetValue(agentHealth, out int lastFrame) &&
            lastFrame == Time.frameCount)
        {
            return;
        }

        AudioClip clip = WeaponAudioLibrary.Instance?.FleshHit;
        if (clip == null)
        {
            return;
        }

        LastFleshHitFrameByAgent[agentHealth] = Time.frameCount;
        PlayImpactClip(
            clip, "Flesh Hit Audio", 0.92f, 1f, 0.96f, 1.04f,
            12f, 100f, 0.35f, ImpactTone.Natural);
    }

    private void PlayMetalHitSound()
    {
        if (lastMetalHitFrame == Time.frameCount)
        {
            return;
        }

        AudioClip clip = WeaponAudioLibrary.Instance?.MetalHit;
        if (clip == null)
        {
            return;
        }

        lastMetalHitFrame = Time.frameCount;
        PlayImpactClip(
            clip, "Turret Metal Hit Audio", 0.98f, 1f, 1.12f, 1.18f,
            15f, 100f, 0.25f, ImpactTone.BrightMetal);
    }

    private void PlayWallHitSound()
    {
        if (lastWallHitFrame == Time.frameCount)
        {
            return;
        }

        AudioClip clip = WeaponAudioLibrary.Instance?.WallHit;
        if (clip == null)
        {
            return;
        }

        lastWallHitFrame = Time.frameCount;
        PlayImpactClip(
            clip, "Wall Hit Audio", 0.95f, 1f, 0.82f, 0.9f,
            15f, 100f, 0.3f, ImpactTone.HeavyWall);
    }

    private static bool IsWallImpact(GameObject hitObject)
    {
        if (hitObject == null)
        {
            return false;
        }

        if (hitObject.GetComponentInParent<DeployedDefenderWall>() != null)
        {
            return true;
        }

        int layer = hitObject.layer;
        if (layer == LayerMask.NameToLayer("Obstacle") ||
            layer == LayerMask.NameToLayer("Obstacle3D"))
        {
            return true;
        }

        string objectName = hitObject.name.ToLowerInvariant();
        return objectName.Contains("wall") ||
               objectName.Contains("cover") ||
               objectName.Contains("barrier") ||
               objectName.Contains("pillar");
    }

    private void PlayImpactClip(
        AudioClip clip,
        string objectName,
        float minimumVolume,
        float maximumVolume,
        float minimumPitch,
        float maximumPitch,
        float minimumDistance,
        float maximumDistance,
        float spatialBlend,
        ImpactTone tone)
    {
        GameObject audioObject = new GameObject(objectName);
        audioObject.transform.position = transform.position;
        AudioSource source = audioObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = spatialBlend;
        source.dopplerLevel = 0f;
        source.priority = 64;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minimumDistance;
        source.maxDistance = maximumDistance;
        source.volume = Random.Range(minimumVolume, maximumVolume);
        source.pitch = Random.Range(minimumPitch, maximumPitch);
        source.clip = clip;

        if (tone == ImpactTone.BrightMetal)
        {
            AudioHighPassFilter highPass = audioObject.AddComponent<AudioHighPassFilter>();
            highPass.cutoffFrequency = 900f;
            highPass.highpassResonanceQ = 1.25f;
        }
        else if (tone == ImpactTone.HeavyWall)
        {
            AudioLowPassFilter lowPass = audioObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 5200f;
            lowPass.lowpassResonanceQ = 1.1f;
        }

        source.Play();
        Destroy(audioObject, clip.length / Mathf.Abs(source.pitch) + 0.1f);
    }

    private void EnsureBallVisual()
    {
        Transform existingVisual = transform.Find("BallVisual");
        if (existingVisual != null)
        {
            return;
        }

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "BallVisual";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * ballRadius * 2f;

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            Destroy(visualCollider);
        }

        Renderer visualRenderer = visual.GetComponent<Renderer>();
        if (visualRenderer != null)
        {
            visualRenderer.sharedMaterial = GetBallMaterial();
        }
    }

    private Material GetBallMaterial()
    {
        if (sharedBallMaterial != null)
        {
            return sharedBallMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        sharedBallMaterial = new Material(shader);
        sharedBallMaterial.color = ballColor;
        return sharedBallMaterial;
    }
}
