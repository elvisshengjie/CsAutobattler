using UnityEngine;

/// <summary>
/// Small world-space health bar created automatically for every 3D agent.
/// </summary>
public class AgentHealthBar3D : MonoBehaviour
{
    public Vector2 size = new Vector2(0.9f, 0.1f);
    public float heightPadding = 0.3f;

    private HealthSystem health;
    private Transform barRoot;
    private Transform fill;
    private Renderer fillRenderer;
    private TextMesh actionStatusText;
    private TextMesh abilityStatusText;
    private Camera targetCamera;

    private static Material backgroundMaterial;
    private static Material healthyMaterial;
    private static Material hurtMaterial;
    private static Material criticalMaterial;

    private void Start()
    {
        health = GetComponent<HealthSystem>();
        targetCamera = Camera.main;
        CreateBar();
        UpdateBar();
    }

    public void SetActionStatus(string actionName, float timeRemaining)
    {
        if (actionStatusText == null)
        {
            return;
        }

        actionStatusText.text = $"{actionName} {Mathf.Max(0f, timeRemaining):0.0}s";
        actionStatusText.gameObject.SetActive(true);
    }

    public void ClearActionStatus()
    {
        if (actionStatusText != null)
        {
            actionStatusText.text = string.Empty;
            actionStatusText.gameObject.SetActive(false);
        }
    }

    public void SetAbilityStatus(string actionName, float timeRemaining, Color color)
    {
        if (abilityStatusText == null)
        {
            return;
        }

        abilityStatusText.text = timeRemaining > 0f
            ? $"{actionName} {timeRemaining:0.0}s"
            : actionName;
        abilityStatusText.color = color;
        abilityStatusText.gameObject.SetActive(true);
    }

    public void ClearAbilityStatus()
    {
        if (abilityStatusText != null)
        {
            abilityStatusText.text = string.Empty;
            abilityStatusText.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (barRoot == null || health == null)
        {
            return;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            barRoot.rotation = Quaternion.LookRotation(
                barRoot.position - targetCamera.transform.position,
                targetCamera.transform.up);
        }

        UpdateBar();
    }

    private void CreateBar()
    {
        GameObject root = new GameObject("HealthBar");
        barRoot = root.transform;
        barRoot.SetParent(transform, false);

        Collider agentCollider = GetComponentInChildren<Collider>();
        float top = agentCollider != null
            ? agentCollider.bounds.max.y - transform.position.y
            : 1f;
        barRoot.localPosition = Vector3.up * (top + heightPadding);

        GameObject background = CreateQuad("Background", barRoot);
        background.transform.localScale = new Vector3(size.x, size.y, 1f);
        background.GetComponent<Renderer>().sharedMaterial = GetBackgroundMaterial();

        GameObject fillObject = CreateQuad("Fill", barRoot);
        fill = fillObject.transform;
        fill.localPosition = new Vector3(0f, 0f, -0.01f);
        fillRenderer = fillObject.GetComponent<Renderer>();

        GameObject statusObject = new GameObject("ActionStatus");
        statusObject.transform.SetParent(barRoot, false);
        statusObject.transform.localPosition = new Vector3(0f, 0.18f, -0.02f);
        actionStatusText = statusObject.AddComponent<TextMesh>();
        actionStatusText.anchor = TextAnchor.MiddleCenter;
        actionStatusText.alignment = TextAlignment.Center;
        actionStatusText.fontSize = 64;
        actionStatusText.characterSize = 0.035f;
        actionStatusText.fontStyle = FontStyle.Bold;
        actionStatusText.color = Color.white;
        actionStatusText.text = string.Empty;
        statusObject.SetActive(false);

        GameObject abilityObject = new GameObject("AbilityStatus");
        abilityObject.transform.SetParent(barRoot, false);
        abilityObject.transform.localPosition = new Vector3(0f, 0.32f, -0.02f);
        abilityStatusText = abilityObject.AddComponent<TextMesh>();
        abilityStatusText.anchor = TextAnchor.MiddleCenter;
        abilityStatusText.alignment = TextAlignment.Center;
        abilityStatusText.fontSize = 64;
        abilityStatusText.characterSize = 0.032f;
        abilityStatusText.fontStyle = FontStyle.Bold;
        abilityStatusText.color = new Color(0.25f, 1f, 0.45f, 1f);
        abilityStatusText.text = string.Empty;
        abilityObject.SetActive(false);
    }

    private void UpdateBar()
    {
        float ratio = health.NormalizedHealth;
        float fillWidth = size.x * ratio;

        fill.localScale = new Vector3(fillWidth, size.y * 0.72f, 1f);
        fill.localPosition = new Vector3(
            -(size.x - fillWidth) * 0.5f,
            0f,
            -0.01f);

        fillRenderer.sharedMaterial = ratio > 0.55f
            ? GetHealthyMaterial()
            : ratio > 0.25f
                ? GetHurtMaterial()
                : GetCriticalMaterial();
    }

    private GameObject CreateQuad(string objectName, Transform parent)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = objectName;
        quad.transform.SetParent(parent, false);

        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
        {
            Destroy(quadCollider);
        }

        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return quad;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private static Material GetBackgroundMaterial()
    {
        if (backgroundMaterial == null)
        {
            backgroundMaterial = CreateMaterial(new Color(0.04f, 0.04f, 0.04f));
        }

        return backgroundMaterial;
    }

    private static Material GetHealthyMaterial()
    {
        if (healthyMaterial == null)
        {
            healthyMaterial = CreateMaterial(new Color(0.15f, 0.85f, 0.25f));
        }

        return healthyMaterial;
    }

    private static Material GetHurtMaterial()
    {
        if (hurtMaterial == null)
        {
            hurtMaterial = CreateMaterial(new Color(1f, 0.65f, 0.08f));
        }

        return hurtMaterial;
    }

    private static Material GetCriticalMaterial()
    {
        if (criticalMaterial == null)
        {
            criticalMaterial = CreateMaterial(new Color(0.95f, 0.12f, 0.12f));
        }

        return criticalMaterial;
    }
}
