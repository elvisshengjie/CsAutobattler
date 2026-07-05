using UnityEngine;

[DisallowMultipleComponent]
public sealed class AgentRoleIndicator : MonoBehaviour
{
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.9f, 0f);
    [SerializeField] private float badgeScale = 0.32f;
    [SerializeField] private float textScale = 0.13f;

    private const string IndicatorRootName = "RoleIndicator";
    private Transform indicatorRoot;
    private Renderer badgeRenderer;
    private TextMesh iconText;
    private Camera targetCamera;
    private AgentRoleType currentRole;
    private Material badgeMaterial;

    private void Awake()
    {
        EnsureIndicator();
    }

    private void OnEnable()
    {
        EnsureIndicator();
    }

    private void LateUpdate()
    {
        if (indicatorRoot == null)
        {
            EnsureIndicator();
        }

        if (indicatorRoot == null)
        {
            return;
        }

        indicatorRoot.position = transform.position + worldOffset;

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            indicatorRoot.rotation = Quaternion.LookRotation(
                indicatorRoot.position - targetCamera.transform.position,
                targetCamera.transform.up);
        }
    }

    public void SetRole(AgentRoleType role)
    {
        currentRole = role;
        EnsureIndicator();
        ApplyRoleStyle(role);
    }

    private void EnsureIndicator()
    {
        if (indicatorRoot != null)
        {
            return;
        }

        Transform existing = transform.Find(IndicatorRootName);
        if (existing != null)
        {
            indicatorRoot = existing;
            badgeRenderer = FindRenderer(indicatorRoot, "Badge");
            iconText = FindText(indicatorRoot, "Icon");
            ApplyRoleStyle(currentRole);
            return;
        }

        GameObject root = new GameObject(IndicatorRootName);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = worldOffset;
        indicatorRoot = root.transform;

        GameObject badge = GameObject.CreatePrimitive(PrimitiveType.Quad);
        badge.name = "Badge";
        badge.transform.SetParent(indicatorRoot, false);
        badge.transform.localPosition = Vector3.zero;
        badge.transform.localRotation = Quaternion.identity;
        badge.transform.localScale = new Vector3(badgeScale, badgeScale, 1f);

        Collider collider = badge.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        badgeRenderer = badge.GetComponent<Renderer>();
        badgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        badgeRenderer.receiveShadows = false;
        badgeMaterial = CreateBadgeMaterial(GetRoleColor(currentRole));
        badgeRenderer.sharedMaterial = badgeMaterial;

        GameObject textObject = new GameObject("Icon");
        textObject.transform.SetParent(indicatorRoot, false);
        textObject.transform.localPosition = new Vector3(0f, -0.005f, -0.02f);
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one * textScale;

        iconText = textObject.AddComponent<TextMesh>();
        iconText.anchor = TextAnchor.MiddleCenter;
        iconText.alignment = TextAlignment.Center;
        iconText.characterSize = 1f;
        iconText.fontSize = 64;
        iconText.color = Color.white;

        MeshRenderer textRenderer = textObject.GetComponent<MeshRenderer>();
        textRenderer.sortingOrder = 10;

        ApplyRoleStyle(currentRole);
    }

    private void ApplyRoleStyle(AgentRoleType role)
    {
        if (iconText != null)
        {
            iconText.text = GetRoleIcon(role);
        }

        Color color = GetRoleColor(role);
        if (badgeRenderer != null)
        {
            if (badgeMaterial == null)
            {
                badgeMaterial = badgeRenderer.sharedMaterial;
            }

            SetMaterialColor(badgeMaterial, color);
        }
    }

    private static Renderer FindRenderer(Transform root, string childName)
    {
        Transform child = root.Find(childName);
        return child != null ? child.GetComponent<Renderer>() : null;
    }

    private static TextMesh FindText(Transform root, string childName)
    {
        Transform child = root.Find(childName);
        return child != null ? child.GetComponent<TextMesh>() : null;
    }

    private static string GetRoleIcon(AgentRoleType role)
    {
        return role switch
        {
            AgentRoleType.Support => "+",
            AgentRoleType.Flanker => ">",
            AgentRoleType.Defender => "D",
            _ => "A"
        };
    }

    private static Color GetRoleColor(AgentRoleType role)
    {
        return role switch
        {
            AgentRoleType.Support => new Color(0.10f, 0.78f, 0.34f, 1f),
            AgentRoleType.Flanker => new Color(0.87f, 0.24f, 0.95f, 1f),
            AgentRoleType.Defender => new Color(0.18f, 0.48f, 1f, 1f),
            _ => new Color(1f, 0.48f, 0.12f, 1f)
        };
    }

    private static Material CreateBadgeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader)
        {
            name = "Role Indicator Badge",
            hideFlags = HideFlags.DontSave
        };
        SetMaterialColor(material, color);
        return material;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
