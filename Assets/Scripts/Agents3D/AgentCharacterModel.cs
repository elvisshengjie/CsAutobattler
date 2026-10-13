using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Replaces an agent's placeholder body with an animated, rigged character
/// (Quaternius Toon Shooter, CC0). Animation runs on a manually evaluated
/// PlayableGraph so no Animator Controller asset is needed: locomotion and
/// shooting poses are cross-faded from movement and weapon state, hits add a
/// flinch, and on death the model detaches and plays out as a corpse.
/// </summary>
[DisallowMultipleComponent]
public sealed class AgentCharacterModel : MonoBehaviour
{
    private const string ModelRootName = "CharacterModel";
    private const string PlayerModelPath = "Characters/Character_Soldier";
    private const string EnemyModelPath = "Characters/Character_Enemy";
    private const float BlendSpeed = 9f;
    private const float NominalRunSpeed = 3.2f;

    // Mixer inputs, in order.
    private static readonly string[] ClipNames =
    {
        "Idle", "Run_Gun", "Run_Shoot", "Idle_Shoot", "HitReact", "Death"
    };
    private const int IdleIndex = 0;
    private const int RunIndex = 1;
    private const int RunShootIndex = 2;
    private const int IdleShootIndex = 3;
    private const int HitIndex = 4;
    private const int DeathIndex = 5;

    private static readonly string[] GunNames =
    {
        "AK", "GrenadeLauncher", "Knife_1", "Knife_2", "Pistol", "Revolver",
        "Revolver_Small", "RocketLauncher", "ShortCannon", "Shotgun", "Shovel",
        "SMG", "Sniper", "Sniper_2"
    };

    private static readonly Dictionary<string, GameObject> PrefabCache =
        new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, Dictionary<string, AnimationClip>> ClipCache =
        new Dictionary<string, Dictionary<string, AnimationClip>>();
    private static Material ringMaterialRed;
    private static Material ringMaterialBlue;
    private static Mesh ringMesh;

    /// <summary>Yaw that turns the imported model to face the agent's +Z.</summary>
    public static float ModelYawOffset = 0f;

    private Transform model;
    private Renderer[] modelRenderers;
    private readonly Dictionary<string, GameObject> guns = new Dictionary<string, GameObject>();
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable[] clipPlayables;
    private readonly float[] weights = new float[ClipNames.Length];
    private readonly float[] times = new float[ClipNames.Length];
    private HealthSystem health;
    private WeaponSystem weapon;
    private Vector3 lastPosition;
    private float smoothedSpeed;
    private float hitWeight;
    private bool built;
    private bool dead;

    public bool IsBuilt => built;

    /// <summary>
    /// Ensures the agent shows the animated character holding the given weapon.
    /// Returns false (leaving the caller's fallback visuals) if the model assets
    /// are unavailable.
    /// </summary>
    public static bool TryApply(GameObject agent, WeaponType weaponType)
    {
        AgentCharacterModel character = agent.GetComponent<AgentCharacterModel>();
        if (character == null)
        {
            character = agent.AddComponent<AgentCharacterModel>();
        }
        if (!character.EnsureBuilt())
        {
            return false;
        }
        character.SetWeapon(weaponType);
        return true;
    }

    private bool EnsureBuilt()
    {
        if (built)
        {
            return true;
        }

        AgentStats stats = GetComponent<AgentStats>();
        bool playerTeam = stats == null || stats.team == TeamType.Red;
        string path = playerTeam ? PlayerModelPath : EnemyModelPath;
        GameObject prefab = LoadPrefab(path);
        Dictionary<string, AnimationClip> clips = LoadClips(path);
        if (prefab == null || clips == null)
        {
            return false;
        }

        model = Instantiate(prefab, transform).transform;
        model.name = ModelRootName;
        model.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
        foreach (string gunName in GunNames)
        {
            Transform gun = FindDeep(model, gunName);
            if (gun != null)
            {
                guns[gunName] = gun.gameObject;
            }
        }

        Animator animator = model.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = model.gameObject.AddComponent<Animator>();
        }
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        BuildGraph(animator, clips);
        modelRenderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in modelRenderers)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = false;
            }
        }

        if (!playerTeam)
        {
            RecolorEnemy();
        }
        FitToAgent();
        CreateTeamRing(playerTeam);

        health = GetComponent<HealthSystem>();
        weapon = GetComponent<WeaponSystem>();
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        lastPosition = transform.position;
        weights[IdleIndex] = 1f;
        built = true;
        Evaluate(0f);
        return true;
    }

    private void BuildGraph(Animator animator, Dictionary<string, AnimationClip> clips)
    {
        graph = PlayableGraph.Create(name + " Character");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        mixer = AnimationMixerPlayable.Create(graph, ClipNames.Length);
        output.SetSourcePlayable(mixer);

        clipPlayables = new AnimationClipPlayable[ClipNames.Length];
        for (int i = 0; i < ClipNames.Length; i++)
        {
            clips.TryGetValue(ClipNames[i], out AnimationClip clip);
            if (clip == null)
            {
                clips.TryGetValue("Idle", out clip);
            }
            clipPlayables[i] = AnimationClipPlayable.Create(graph, clip);
            clipPlayables[i].SetApplyFootIK(false);
            graph.Connect(clipPlayables[i], 0, mixer, i);
        }
    }

    private void FitToAgent()
    {
        // The agent's own collider/placeholder defines its footprint and height.
        Bounds agentBounds;
        Collider agentCollider = GetComponent<Collider>();
        if (agentCollider != null)
        {
            agentBounds = agentCollider.bounds;
        }
        else
        {
            Renderer placeholder = GetComponent<Renderer>();
            agentBounds = placeholder != null
                ? placeholder.bounds
                : new Bounds(transform.position, new Vector3(1f, 2f, 1f));
        }

        Vector3 parentScale = transform.lossyScale;
        model.localScale = new Vector3(
            1f / Mathf.Max(0.0001f, parentScale.x),
            1f / Mathf.Max(0.0001f, parentScale.y),
            1f / Mathf.Max(0.0001f, parentScale.z));
        model.position = new Vector3(transform.position.x, agentBounds.min.y, transform.position.z);
        Evaluate(0f);

        Bounds modelBounds = MeasureBody();
        float targetHeight = Mathf.Clamp(agentBounds.size.y * 1.05f, 1.4f, 3f);
        float scale = modelBounds.size.y > 0.01f ? targetHeight / modelBounds.size.y : 1f;
        model.localScale *= scale;

        // Re-measure after scaling so the feet sit exactly on the floor.
        modelBounds = MeasureBody();
        model.position += Vector3.up * (agentBounds.min.y - modelBounds.min.y);
    }

    private Bounds MeasureBody()
    {
        bool initialized = false;
        Bounds bounds = new Bounds(model.position, Vector3.zero);
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!(renderer is SkinnedMeshRenderer))
            {
                continue; // ignore guns
            }
            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }

    private void RecolorEnemy()
    {
        // The enemy model's red accents would read as the player's (red) team.
        Color teamBlue = new Color(0.16f, 0.34f, 0.72f, 1f);
        foreach (Renderer renderer in modelRenderers)
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name.StartsWith("Enemy_Red"))
                {
                    materials[i] = new Material(materials[i]) { name = "Enemy_TeamBlue (Runtime)" };
                    materials[i].SetColor("_BaseColor", teamBlue);
                    changed = true;
                }
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    private void CreateTeamRing(bool playerTeam)
    {
        GameObject ring = new GameObject("TeamRing");
        ring.transform.SetParent(model, false);
        ring.transform.localPosition = Vector3.up * 0.03f / Mathf.Max(0.01f, model.lossyScale.y);
        ring.transform.localRotation = Quaternion.identity;
        ring.transform.localScale = Vector3.one * 1.25f;
        ring.AddComponent<MeshFilter>().sharedMesh = GetRingMesh();
        MeshRenderer renderer = ring.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetRingMaterial(playerTeam);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public void SetWeapon(WeaponType weaponType)
    {
        string selected = weaponType switch
        {
            WeaponType.SMG => "SMG",
            WeaponType.Sniper => "Sniper",
            WeaponType.Shotgun => "Shotgun",
            _ => "AK"
        };
        foreach (KeyValuePair<string, GameObject> gun in guns)
        {
            gun.Value.SetActive(gun.Key == selected);
        }
    }

    private void Update()
    {
        if (!built || dead)
        {
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;
        float speed = delta.magnitude / dt;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-dt * 10f));

        bool moving = smoothedSpeed > 0.4f;
        bool shooting = weapon != null && weapon.TimeSinceLastShot < 0.45f;
        int pose = moving
            ? (shooting ? RunShootIndex : RunIndex)
            : (shooting ? IdleShootIndex : IdleIndex);

        for (int i = 0; i <= IdleShootIndex; i++)
        {
            weights[i] = Mathf.MoveTowards(weights[i], i == pose ? 1f : 0f, dt * BlendSpeed);
        }
        hitWeight = Mathf.MoveTowards(hitWeight, 0f, dt * 3f);

        float runRate = Mathf.Clamp(smoothedSpeed / NominalRunSpeed, 0.75f, 1.5f);
        times[IdleIndex] += dt;
        times[RunIndex] += dt * runRate;
        times[RunShootIndex] += dt * runRate;
        times[IdleShootIndex] += dt;
        times[HitIndex] += dt;
        Evaluate(dt);
    }

    private void Evaluate(float dt)
    {
        if (!graph.IsValid())
        {
            return;
        }

        float locomotionTotal = 0f;
        for (int i = 0; i <= IdleShootIndex; i++)
        {
            locomotionTotal += weights[i];
        }
        float locomotionScale = locomotionTotal > 0.0001f ? (1f - hitWeight) / locomotionTotal : 0f;
        for (int i = 0; i < ClipNames.Length; i++)
        {
            float weight = i <= IdleShootIndex
                ? weights[i] * locomotionScale
                : i == HitIndex ? hitWeight : 0f;
            mixer.SetInputWeight(i, weight);
            SetLoopedTime(i, i == HitIndex ? Mathf.Min(times[i], ClipLength(i)) : times[i]);
        }
        graph.Evaluate(0f);
    }

    private float ClipLength(int index)
    {
        AnimationClip clip = clipPlayables[index].GetAnimationClip();
        return clip != null ? Mathf.Max(0.01f, clip.length) : 1f;
    }

    private void SetLoopedTime(int index, float time)
    {
        clipPlayables[index].SetTime(time % ClipLength(index));
    }

    private void OnDamaged(HealthSystem source, float amount, GameObject attacker)
    {
        if (dead || amount <= 0f)
        {
            return;
        }
        hitWeight = Mathf.Max(hitWeight, 0.65f);
        times[HitIndex] = 0f;
    }

    private void OnDied(HealthSystem source)
    {
        if (dead || model == null)
        {
            return;
        }
        dead = true;

        // The agent deactivates this frame, so hand the model and graph to a
        // standalone corpse that finishes the death animation.
        Transform ring = model.Find("TeamRing");
        if (ring != null)
        {
            Destroy(ring.gameObject);
        }
        model.SetParent(null, true);
        CharacterCorpse corpse = model.gameObject.AddComponent<CharacterCorpse>();
        corpse.Begin(graph, mixer, clipPlayables[DeathIndex], DeathIndex);
        graph = default;
        model = null;
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }

    // --- Asset loading ------------------------------------------------------

    private static GameObject LoadPrefab(string path)
    {
        if (!PrefabCache.TryGetValue(path, out GameObject prefab))
        {
            prefab = Resources.Load<GameObject>(path);
            PrefabCache[path] = prefab;
        }
        return prefab;
    }

    private static Dictionary<string, AnimationClip> LoadClips(string path)
    {
        if (ClipCache.TryGetValue(path, out Dictionary<string, AnimationClip> clips))
        {
            return clips;
        }

        clips = new Dictionary<string, AnimationClip>();
        foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(path))
        {
            // Clips import both as "CharacterArmature|Run" and "Run".
            string key = clip.name.Contains("|")
                ? clip.name.Substring(clip.name.LastIndexOf('|') + 1)
                : clip.name;
            if (!clip.name.StartsWith("__preview") && !clips.ContainsKey(key))
            {
                clips[key] = clip;
            }
        }
        if (clips.Count == 0)
        {
            clips = null;
        }
        ClipCache[path] = clips;
        return clips;
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        foreach (Transform child in root)
        {
            if (child.name == childName)
            {
                return child;
            }
            Transform found = FindDeep(child, childName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static Material GetRingMaterial(bool playerTeam)
    {
        Material cached = playerTeam ? ringMaterialRed : ringMaterialBlue;
        if (cached != null)
        {
            return cached;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }
        Color color = playerTeam
            ? new Color(1f, 0.32f, 0.22f, 0.85f)
            : new Color(0.3f, 0.6f, 1f, 0.85f);
        Material material = new Material(shader) { name = "Team Ring (Runtime)" };
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        material.SetColor("_BaseColor", color);
        material.color = color;
        if (playerTeam) ringMaterialRed = material; else ringMaterialBlue = material;
        return material;
    }

    private static Mesh GetRingMesh()
    {
        if (ringMesh != null)
        {
            return ringMesh;
        }

        // Flat annulus with a soft inner edge, lying on the XZ plane.
        const int segments = 48;
        const float outer = 0.5f;
        const float inner = 0.4f;
        Vector3[] vertices = new Vector3[segments * 2];
        int[] triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = direction * inner;
            vertices[i * 2 + 1] = direction * outer;
            int next = (i + 1) % segments;
            int t = i * 6;
            triangles[t] = i * 2;
            triangles[t + 1] = next * 2;
            triangles[t + 2] = i * 2 + 1;
            triangles[t + 3] = i * 2 + 1;
            triangles[t + 4] = next * 2;
            triangles[t + 5] = next * 2 + 1;
        }
        ringMesh = new Mesh { name = "Team Ring" };
        ringMesh.vertices = vertices;
        ringMesh.triangles = triangles;
        ringMesh.RecalculateNormals();
        ringMesh.RecalculateBounds();
        return ringMesh;
    }
}

/// <summary>Plays a detached character's death animation, then sinks and removes it.</summary>
public sealed class CharacterCorpse : MonoBehaviour
{
    private const float HoldTime = 3f;
    private const float SinkTime = 1.2f;

    private PlayableGraph graph;
    private AnimationClipPlayable deathPlayable;
    private float age;
    private float deathLength = 1f;

    public void Begin(
        PlayableGraph ownedGraph,
        AnimationMixerPlayable mixer,
        AnimationClipPlayable death,
        int deathIndex)
    {
        graph = ownedGraph;
        deathPlayable = death;
        AnimationClip clip = death.GetAnimationClip();
        deathLength = clip != null ? Mathf.Max(0.05f, clip.length) : 0.5f;
        for (int i = 0; i < mixer.GetInputCount(); i++)
        {
            mixer.SetInputWeight(i, i == deathIndex ? 1f : 0f);
        }
        deathPlayable.SetTime(0f);
        graph.Evaluate(0f);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (graph.IsValid())
        {
            deathPlayable.SetTime(Mathf.Min(age, deathLength - 0.001f));
            graph.Evaluate(0f);
        }

        if (age > HoldTime)
        {
            transform.position += Vector3.down * (Time.deltaTime * 0.9f / SinkTime);
        }
        if (age > HoldTime + SinkTime)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }
}
