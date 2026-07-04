using UnityEngine;

public class BulletProjectile : MonoBehaviour
{
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

        AgentStats targetStats = hitObject.GetComponentInParent<AgentStats>();

        if (targetStats != null && targetStats.team == ownerTeam)
        {
            return false;
        }

        HealthSystem targetHealth = hitObject.GetComponentInParent<HealthSystem>();

        if (targetHealth != null && !targetHealth.IsDead)
        {
            float appliedDamage = damage;
            if (damageFalloffRange > 0f && minimumDamageMultiplier < 1f)
            {
                float travelled = Vector3.Distance(originPosition, transform.position);
                float falloff = Mathf.Clamp01(travelled / damageFalloffRange);
                appliedDamage *= Mathf.Lerp(1f, minimumDamageMultiplier, falloff);
            }
            targetHealth.TakeDamage(appliedDamage, owner);
        }

        // Any solid wall or enemy collision consumes the projectile.
        initialized = false;
        rb.linearVelocity = Vector3.zero;
        Destroy(gameObject);
        return true;
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
