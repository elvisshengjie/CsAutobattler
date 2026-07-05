using UnityEngine;

[DisallowMultipleComponent]
public sealed class AgentWeaponVisuals : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private bool useHumanoidModel = true;
    [SerializeField] private Color rifleTint = new Color(0.92f, 0.92f, 0.86f, 1f);
    [SerializeField] private Color smgTint = new Color(0.10f, 0.85f, 0.95f, 1f);
    [SerializeField] private Color sniperTint = new Color(0.98f, 0.88f, 0.20f, 1f);
    [SerializeField] private Color shotgunTint = new Color(1f, 0.36f, 0.18f, 1f);

    private const int TextureSize = 64;
    private const string HumanoidRootName = "GeneratedHumanVisual";
    private MaterialPropertyBlock propertyBlock;
    private readonly Texture2D[] weaponTextures = new Texture2D[4];
    private readonly Renderer[] bodyRenderers = new Renderer[5];
    private readonly Renderer[] skinRenderers = new Renderer[3];
    private Renderer weaponRenderer;
    private Material bodyMaterial;
    private Material skinMaterial;
    private Material gearMaterial;

    private void Awake()
    {
        EnsureTargetRenderer();
    }

    private void OnEnable()
    {
        WeaponLoadout loadout = GetComponent<WeaponLoadout>();
        ApplyWeapon(loadout != null ? loadout.SelectedWeapon : WeaponType.Rifle);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureTargetRenderer();
        WeaponLoadout loadout = GetComponent<WeaponLoadout>();
        ApplyWeapon(loadout != null ? loadout.SelectedWeapon : WeaponType.Rifle);
    }
#endif

    public void ApplyWeapon(WeaponType weaponType)
    {
        EnsureTargetRenderer();
        if (targetRenderer == null)
        {
            return;
        }

        if (useHumanoidModel)
        {
            EnsureHumanoidModel();
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        Material material = targetRenderer.sharedMaterial;
        Color baseColor = GetRendererBaseColor(material);
        Color accent = GetWeaponTint(weaponType);
        Texture2D texture = GetWeaponTexture(weaponType, baseColor, accent);

        if (!useHumanoidModel)
        {
            ApplyTexturedBlock(targetRenderer, material, texture, Color.white);
            return;
        }

        ApplyBodyTexture(texture);
        ApplySkinColor(new Color(0.78f, 0.58f, 0.42f, 1f));
        ApplyGearColor(Color.Lerp(baseColor, Color.black, 0.55f));
    }

    private void EnsureTargetRenderer()
    {
        if (targetRenderer != null)
        {
            return;
        }

        targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>(true);
        }
    }

    private void EnsureHumanoidModel()
    {
        Transform existing = transform.Find(HumanoidRootName);
        if (existing != null)
        {
            CacheHumanoidRenderers(existing);
            if (targetRenderer != null)
            {
                targetRenderer.enabled = false;
            }
            return;
        }

        Material sourceMaterial = targetRenderer.sharedMaterial;
        bodyMaterial = CreateRuntimeMaterial(sourceMaterial, Color.white);
        skinMaterial = CreateRuntimeMaterial(sourceMaterial, new Color(0.78f, 0.58f, 0.42f, 1f));
        gearMaterial = CreateRuntimeMaterial(sourceMaterial, Color.Lerp(GetRendererBaseColor(sourceMaterial), Color.black, 0.55f));

        GameObject root = new GameObject(HumanoidRootName);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        bodyRenderers[0] = CreatePart("Torso", root.transform, PrimitiveType.Capsule,
            new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.48f, 0.30f),
            Quaternion.identity, bodyMaterial);
        skinRenderers[0] = CreatePart("Head", root.transform, PrimitiveType.Sphere,
            new Vector3(0f, 0.78f, 0f), new Vector3(0.30f, 0.30f, 0.30f),
            Quaternion.identity, skinMaterial);
        bodyRenderers[1] = CreatePart("LeftArm", root.transform, PrimitiveType.Capsule,
            new Vector3(-0.38f, 0.15f, 0.06f), new Vector3(0.13f, 0.36f, 0.13f),
            Quaternion.Euler(0f, 0f, -18f), bodyMaterial);
        bodyRenderers[2] = CreatePart("RightArm", root.transform, PrimitiveType.Capsule,
            new Vector3(0.38f, 0.15f, 0.06f), new Vector3(0.13f, 0.36f, 0.13f),
            Quaternion.Euler(0f, 0f, 18f), bodyMaterial);
        bodyRenderers[3] = CreatePart("LeftLeg", root.transform, PrimitiveType.Capsule,
            new Vector3(-0.16f, -0.62f, 0f), new Vector3(0.15f, 0.34f, 0.15f),
            Quaternion.identity, bodyMaterial);
        bodyRenderers[4] = CreatePart("RightLeg", root.transform, PrimitiveType.Capsule,
            new Vector3(0.16f, -0.62f, 0f), new Vector3(0.15f, 0.34f, 0.15f),
            Quaternion.identity, bodyMaterial);
        skinRenderers[1] = CreatePart("LeftHand", root.transform, PrimitiveType.Sphere,
            new Vector3(-0.46f, -0.20f, 0.18f), new Vector3(0.12f, 0.12f, 0.12f),
            Quaternion.identity, skinMaterial);
        skinRenderers[2] = CreatePart("RightHand", root.transform, PrimitiveType.Sphere,
            new Vector3(0.46f, -0.20f, 0.18f), new Vector3(0.12f, 0.12f, 0.12f),
            Quaternion.identity, skinMaterial);
        weaponRenderer = CreatePart("Weapon", root.transform, PrimitiveType.Cube,
            new Vector3(0.26f, 0.02f, 0.44f), new Vector3(0.12f, 0.12f, 0.58f),
            Quaternion.Euler(0f, 12f, 0f), gearMaterial);

        targetRenderer.enabled = false;
    }

    private void CacheHumanoidRenderers(Transform root)
    {
        bodyRenderers[0] = FindRenderer(root, "Torso");
        bodyRenderers[1] = FindRenderer(root, "LeftArm");
        bodyRenderers[2] = FindRenderer(root, "RightArm");
        bodyRenderers[3] = FindRenderer(root, "LeftLeg");
        bodyRenderers[4] = FindRenderer(root, "RightLeg");
        skinRenderers[0] = FindRenderer(root, "Head");
        skinRenderers[1] = FindRenderer(root, "LeftHand");
        skinRenderers[2] = FindRenderer(root, "RightHand");
        weaponRenderer = FindRenderer(root, "Weapon");

        if (bodyMaterial == null && bodyRenderers[0] != null)
        {
            bodyMaterial = bodyRenderers[0].sharedMaterial;
        }

        if (skinMaterial == null && skinRenderers[0] != null)
        {
            skinMaterial = skinRenderers[0].sharedMaterial;
        }

        if (gearMaterial == null && weaponRenderer != null)
        {
            gearMaterial = weaponRenderer.sharedMaterial;
        }
    }

    private static Renderer FindRenderer(Transform root, string childName)
    {
        Transform child = root.Find(childName);
        return child != null ? child.GetComponent<Renderer>() : null;
    }

    private static Renderer CreatePart(
        string partName,
        Transform parent,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale,
        Quaternion localRotation,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        Renderer renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static Material CreateRuntimeMaterial(Material source, Color color)
    {
        Material material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.hideFlags = HideFlags.DontSave;
        SetMaterialColor(material, color);
        return material;
    }

    private void ApplyBodyTexture(Texture texture)
    {
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            ApplyTexturedBlock(bodyRenderers[i], bodyMaterial, texture, Color.white);
        }
    }

    private void ApplySkinColor(Color color)
    {
        for (int i = 0; i < skinRenderers.Length; i++)
        {
            ApplyColorBlock(skinRenderers[i], skinMaterial, color);
        }
    }

    private void ApplyGearColor(Color color)
    {
        ApplyColorBlock(weaponRenderer, gearMaterial, color);
    }

    private Texture2D GetWeaponTexture(WeaponType weaponType, Color baseColor, Color accent)
    {
        int index = (int)weaponType;
        if (weaponTextures[index] == null)
        {
            weaponTextures[index] = CreateWeaponTexture(weaponType, baseColor, accent);
        }

        return weaponTextures[index];
    }

    private static Texture2D CreateWeaponTexture(WeaponType weaponType, Color baseColor, Color accent)
    {
        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = weaponType + " Agent Texture",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point
        };

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                bool mark = weaponType switch
                {
                    WeaponType.Rifle => Mathf.Abs((x - y) % 16) < 3,
                    WeaponType.SMG => x % 12 < 5,
                    WeaponType.Sniper => x == TextureSize / 2 || y == TextureSize / 2 ||
                        Mathf.Abs(x - y) < 2 || Mathf.Abs((TextureSize - 1 - x) - y) < 2,
                    WeaponType.Shotgun => ((x % 16) - 8) * ((x % 16) - 8) +
                        ((y % 16) - 8) * ((y % 16) - 8) < 18,
                    _ => false
                };

                Color shade = ((x / 8 + y / 8) % 2 == 0)
                    ? baseColor
                    : Color.Lerp(baseColor, Color.black, 0.12f);
                texture.SetPixel(x, y, mark ? accent : shade);
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private Color GetWeaponTint(WeaponType weaponType)
    {
        return weaponType switch
        {
            WeaponType.SMG => smgTint,
            WeaponType.Sniper => sniperTint,
            WeaponType.Shotgun => shotgunTint,
            _ => rifleTint
        };
    }

    private static Color GetRendererBaseColor(Material material)
    {
        if (material == null)
        {
            return Color.white;
        }

        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }

        return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
    }

    private void ApplyTexturedBlock(Renderer renderer, Material material, Texture texture, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.GetPropertyBlock(propertyBlock);
        SetTextureIfPresent(material, "_BaseMap", texture);
        SetTextureIfPresent(material, "_MainTex", texture);
        SetColorIfPresent(material, "_BaseColor", color);
        SetColorIfPresent(material, "_Color", color);
        renderer.SetPropertyBlock(propertyBlock);
    }

    private void ApplyColorBlock(Renderer renderer, Material material, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.GetPropertyBlock(propertyBlock);
        SetColorIfPresent(material, "_BaseColor", color);
        SetColorIfPresent(material, "_Color", color);
        renderer.SetPropertyBlock(propertyBlock);
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

    private void SetTextureIfPresent(Material material, string propertyName, Texture texture)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            propertyBlock.SetTexture(propertyName, texture);
        }
    }

    private void SetColorIfPresent(Material material, string propertyName, Color color)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            propertyBlock.SetColor(propertyName, color);
        }
    }
}
