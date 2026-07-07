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
    [Header("Striker Palette")]
    [SerializeField] private Color strikerBodyTint = new Color(0.68f, 0.025f, 0f, 1f);
    [SerializeField] private Color strikerRifleTint = new Color(0.98f, 0.18f, 0.02f, 1f);
    [SerializeField] private Color strikerSmgTint = new Color(0.86f, 0.035f, 0f, 1f);
    [SerializeField] private Color strikerSniperTint = new Color(0.96f, 0.48f, 0.02f, 1f);
    [SerializeField] private Color strikerShotgunTint = new Color(1f, 0.24f, 0.01f, 1f);

    private const int TextureSize = 64;
    private const string HumanoidRootName = "GeneratedHumanVisual";
    private const string WeaponRootName = "Weapon";
    private MaterialPropertyBlock propertyBlock;
    private readonly Texture2D[] weaponTextures = new Texture2D[4];
    private readonly Renderer[] bodyRenderers = new Renderer[5];
    private readonly Renderer[] skinRenderers = new Renderer[3];
    private readonly Renderer[] weaponRenderers = new Renderer[8];
    private Transform weaponRoot;
    private WeaponType displayedWeaponType = (WeaponType)(-1);
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
        AgentStats stats = GetComponent<AgentStats>();
        Color baseColor = stats != null && stats.team == TeamType.Red
            ? strikerBodyTint
            : GetRendererBaseColor(material);
        Color accent = GetWeaponTint(weaponType);
        Texture2D texture = GetWeaponTexture(weaponType, baseColor, accent);

        if (!useHumanoidModel)
        {
            ApplyTexturedBlock(targetRenderer, material, texture, Color.white);
            return;
        }

        ApplyBodyTexture(texture);
        ApplySkinColor(new Color(0.78f, 0.58f, 0.42f, 1f));
        ApplyWeaponShape(weaponType);
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
        weaponRoot = CreateWeaponRoot(root.transform);
        BuildWeaponShape(WeaponType.Rifle);

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
        weaponRoot = root.Find(WeaponRootName);
        CacheWeaponRenderers();

        if (bodyMaterial == null && bodyRenderers[0] != null)
        {
            bodyMaterial = bodyRenderers[0].sharedMaterial;
        }

        if (skinMaterial == null && skinRenderers[0] != null)
        {
            skinMaterial = skinRenderers[0].sharedMaterial;
        }

        if (gearMaterial == null)
        {
            Renderer renderer = GetFirstWeaponRenderer();
            if (renderer != null)
            {
                gearMaterial = renderer.sharedMaterial;
            }
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

    private static Transform CreateWeaponRoot(Transform parent)
    {
        GameObject root = new GameObject(WeaponRootName);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0.26f, 0.02f, 0.44f);
        root.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
        root.transform.localScale = Vector3.one;
        return root.transform;
    }

    private void ApplyWeaponShape(WeaponType weaponType)
    {
        if (weaponRoot == null)
        {
            Transform visualRoot = transform.Find(HumanoidRootName);
            if (visualRoot != null)
            {
                weaponRoot = visualRoot.Find(WeaponRootName);
                if (weaponRoot == null)
                {
                    weaponRoot = CreateWeaponRoot(visualRoot);
                    displayedWeaponType = (WeaponType)(-1);
                }
            }
        }

        if (weaponRoot == null)
        {
            return;
        }

        if (displayedWeaponType != weaponType || GetFirstWeaponRenderer() == null)
        {
            BuildWeaponShape(weaponType);
        }
    }

    private void BuildWeaponShape(WeaponType weaponType)
    {
        if (weaponRoot == null)
        {
            return;
        }

        ClearWeaponShape();
        displayedWeaponType = weaponType;

        switch (weaponType)
        {
            case WeaponType.SMG:
                AddWeaponPart(0, "Receiver", PrimitiveType.Cube,
                    new Vector3(0f, 0f, 0.02f), new Vector3(0.16f, 0.14f, 0.36f), Quaternion.identity);
                AddWeaponPart(1, "ShortBarrel", PrimitiveType.Cube,
                    new Vector3(0f, 0f, 0.28f), new Vector3(0.08f, 0.08f, 0.22f), Quaternion.identity);
                AddWeaponPart(2, "Grip", PrimitiveType.Cube,
                    new Vector3(0f, -0.13f, -0.04f), new Vector3(0.08f, 0.24f, 0.08f), Quaternion.Euler(-12f, 0f, 0f));
                AddWeaponPart(3, "Magazine", PrimitiveType.Cube,
                    new Vector3(0f, -0.18f, 0.10f), new Vector3(0.09f, 0.28f, 0.07f), Quaternion.Euler(10f, 0f, 0f));
                break;
            case WeaponType.Sniper:
                AddWeaponPart(0, "LongStock", PrimitiveType.Cube,
                    new Vector3(0f, 0f, -0.16f), new Vector3(0.13f, 0.13f, 0.42f), Quaternion.identity);
                AddWeaponPart(1, "LongBarrel", PrimitiveType.Cube,
                    new Vector3(0f, 0.01f, 0.36f), new Vector3(0.055f, 0.055f, 0.72f), Quaternion.identity);
                AddWeaponPart(2, "Scope", PrimitiveType.Capsule,
                    new Vector3(0f, 0.13f, 0.10f), new Vector3(0.08f, 0.20f, 0.08f), Quaternion.Euler(90f, 0f, 0f));
                AddWeaponPart(3, "Grip", PrimitiveType.Cube,
                    new Vector3(0f, -0.14f, -0.05f), new Vector3(0.07f, 0.22f, 0.08f), Quaternion.Euler(-10f, 0f, 0f));
                break;
            case WeaponType.Shotgun:
                AddWeaponPart(0, "WideBody", PrimitiveType.Cube,
                    new Vector3(0f, 0f, 0.02f), new Vector3(0.18f, 0.15f, 0.52f), Quaternion.identity);
                AddWeaponPart(1, "TwinBarrelA", PrimitiveType.Cube,
                    new Vector3(-0.045f, 0.035f, 0.35f), new Vector3(0.06f, 0.06f, 0.42f), Quaternion.identity);
                AddWeaponPart(2, "TwinBarrelB", PrimitiveType.Cube,
                    new Vector3(0.045f, 0.035f, 0.35f), new Vector3(0.06f, 0.06f, 0.42f), Quaternion.identity);
                AddWeaponPart(3, "Pump", PrimitiveType.Cube,
                    new Vector3(0f, -0.08f, 0.18f), new Vector3(0.16f, 0.08f, 0.24f), Quaternion.identity);
                AddWeaponPart(4, "Stock", PrimitiveType.Cube,
                    new Vector3(0f, -0.03f, -0.28f), new Vector3(0.18f, 0.13f, 0.22f), Quaternion.identity);
                break;
            case WeaponType.Rifle:
            default:
                AddWeaponPart(0, "Receiver", PrimitiveType.Cube,
                    new Vector3(0f, 0f, 0.02f), new Vector3(0.13f, 0.12f, 0.46f), Quaternion.identity);
                AddWeaponPart(1, "Barrel", PrimitiveType.Cube,
                    new Vector3(0f, 0.005f, 0.34f), new Vector3(0.055f, 0.055f, 0.38f), Quaternion.identity);
                AddWeaponPart(2, "Stock", PrimitiveType.Cube,
                    new Vector3(0f, -0.015f, -0.26f), new Vector3(0.15f, 0.11f, 0.22f), Quaternion.identity);
                AddWeaponPart(3, "Magazine", PrimitiveType.Cube,
                    new Vector3(0f, -0.15f, 0.05f), new Vector3(0.08f, 0.22f, 0.08f), Quaternion.Euler(8f, 0f, 0f));
                break;
        }
    }

    private void AddWeaponPart(
        int index,
        string partName,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale,
        Quaternion localRotation)
    {
        if (index < 0 || index >= weaponRenderers.Length)
        {
            return;
        }

        weaponRenderers[index] = CreatePart(partName, weaponRoot, primitiveType,
            localPosition, localScale, localRotation, gearMaterial);
    }

    private void ClearWeaponShape()
    {
        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            weaponRenderers[i] = null;
        }

        for (int i = weaponRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = weaponRoot.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private void CacheWeaponRenderers()
    {
        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            weaponRenderers[i] = null;
        }

        if (weaponRoot == null)
        {
            return;
        }

        int count = Mathf.Min(weaponRoot.childCount, weaponRenderers.Length);
        for (int i = 0; i < count; i++)
        {
            weaponRenderers[i] = weaponRoot.GetChild(i).GetComponent<Renderer>();
        }
    }

    private Renderer GetFirstWeaponRenderer()
    {
        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            if (weaponRenderers[i] != null)
            {
                return weaponRenderers[i];
            }
        }

        return null;
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
        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            ApplyColorBlock(weaponRenderers[i], gearMaterial, color);
        }
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
        AgentStats stats = GetComponent<AgentStats>();
        if (stats != null && stats.team == TeamType.Red)
        {
            return weaponType switch
            {
                WeaponType.SMG => strikerSmgTint,
                WeaponType.Sniper => strikerSniperTint,
                WeaponType.Shotgun => strikerShotgunTint,
                _ => strikerRifleTint
            };
        }

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
