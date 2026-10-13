using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Presentation-only "game feel" layer: damage numbers, kill banners with
/// multi-kill streaks, death bursts, camera shake, a low-health pressure
/// vignette with heartbeat, bomb beeps and round/shop stingers. It listens to
/// global gameplay events and never changes gameplay state.
/// </summary>
[DefaultExecutionOrder(500)]
public sealed class CombatFeedbackSystem : MonoBehaviour
{
    private const string ClipFolder = "Audio/Feedback/";
    private const float MultiKillWindow = 4f;
    private const float DamageMergeWindow = 0.12f;
    private const float PressureHealthFraction = 0.35f;

    private sealed class DamageNumber
    {
        public HealthSystem target;
        public Vector3 worldPosition;
        public float amount;
        public float age;
        public Color color;
        public bool lethal;
    }

    private sealed class Banner
    {
        public string text;
        public string subtitle;
        public Color color;
        public float age;
        public float duration;
    }

    public static CombatFeedbackSystem Instance { get; private set; }

    private readonly List<DamageNumber> damageNumbers = new List<DamageNumber>();
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    private Banner banner;
    private AudioSource sfxSource;
    private AudioSource uiSource;
    private RoundManager boundRoundManager;
    private Texture2D vignetteTexture;
    private GUIStyle numberStyle;
    private GUIStyle bannerStyle;
    private GUIStyle bannerSubtitleStyle;
    private Material burstMaterial;

    private int streakCount;
    private float lastPlayerKillTime = -100f;
    private float shakeStrength;
    private Vector3 appliedShakeOffset;
    private Camera shakenCamera;
    private float pressure;
    private float nextHeartbeatTime;
    private float nextBombBeepTime;
    private bool lastStandAnnounced;
    private bool roundStartAnnounced;
    private float nextPressureSampleTime;
    private float pressureTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null)
        {
            new GameObject("Combat Feedback System").AddComponent<CombatFeedbackSystem>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        sfxSource = CreateSource(0.9f);
        uiSource = CreateSource(0.7f);
        vignetteTexture = CreateVignetteTexture();
    }

    private void OnEnable()
    {
        HealthSystem.AnyDamaged += OnAnyDamaged;
        HealthSystem.AnyDied += OnAnyDied;
        CampaignManager.ShopAction += OnShopAction;
        CampaignManager.CharacterPromoted += OnCharacterPromoted;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        HealthSystem.AnyDamaged -= OnAnyDamaged;
        HealthSystem.AnyDied -= OnAnyDied;
        CampaignManager.ShopAction -= OnShopAction;
        CampaignManager.CharacterPromoted -= OnCharacterPromoted;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        BindRoundManager(null);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (vignetteTexture != null)
        {
            Destroy(vignetteTexture);
        }
        if (burstMaterial != null)
        {
            Destroy(burstMaterial);
        }
    }

    private TeamType PlayerTeam =>
        boundRoundManager != null ? boundRoundManager.attackingTeam : TeamType.Red;

    private void Update()
    {
        if (boundRoundManager != RoundManager.Instance)
        {
            BindRoundManager(RoundManager.Instance);
        }

        float dt = Time.unscaledDeltaTime;
        for (int i = damageNumbers.Count - 1; i >= 0; i--)
        {
            damageNumbers[i].age += dt;
            if (damageNumbers[i].age > 0.9f)
            {
                damageNumbers.RemoveAt(i);
            }
        }

        if (banner != null)
        {
            banner.age += dt;
            if (banner.age > banner.duration)
            {
                banner = null;
            }
        }

        shakeStrength = Mathf.MoveTowards(shakeStrength, 0f, dt * 2.2f);
        UpdatePressure(dt);
        UpdateBombBeeps();
    }

    // --- Event handlers ------------------------------------------------------

    private void OnAnyDamaged(HealthSystem target, float amount, GameObject attacker)
    {
        if (target == null || amount <= 0f || !IsBattleActive())
        {
            return;
        }

        AgentStats stats = target.GetComponent<AgentStats>();
        bool playerHurt = stats != null && stats.team == PlayerTeam;
        Vector3 position = target.transform.position + Vector3.up * 2.2f;

        // Shotgun pellets and rapid hits on one target collapse into one number.
        for (int i = damageNumbers.Count - 1; i >= 0; i--)
        {
            DamageNumber existing = damageNumbers[i];
            if (existing.target == target && existing.age < DamageMergeWindow + 0.05f)
            {
                existing.amount += amount;
                existing.age = 0f;
                existing.worldPosition = position;
                existing.lethal = target.IsDead || target.CurrentHealth <= 0f;
                return;
            }
        }

        damageNumbers.Add(new DamageNumber
        {
            target = target,
            worldPosition = position,
            amount = amount,
            color = playerHurt
                ? new Color(1f, 0.32f, 0.26f, 1f)
                : new Color(1f, 0.93f, 0.62f, 1f),
            lethal = target.CurrentHealth <= 0f
        });

        if (playerHurt)
        {
            AddShake(0.05f);
        }
    }

    private void OnAnyDied(HealthSystem victim)
    {
        if (victim == null || !IsBattleActive())
        {
            return;
        }

        AgentStats stats = victim.GetComponent<AgentStats>();
        bool playerLost = stats != null && stats.team == PlayerTeam;
        SpawnDeathBurst(victim.transform.position, playerLost);

        if (playerLost)
        {
            streakCount = 0;
            Play("ally_down", 0.9f);
            AddShake(0.45f);
            ShowBanner("OPERATOR DOWN", FormatAgentName(victim.gameObject),
                new Color(1f, 0.3f, 0.24f, 1f), 1.6f);
            return;
        }

        float now = Time.unscaledTime;
        streakCount = now - lastPlayerKillTime <= MultiKillWindow ? streakCount + 1 : 1;
        lastPlayerKillTime = now;

        bool ace = CountLiving(OpposingTeam(PlayerTeam), victim) == 0;
        string title = ace
            ? "ACE"
            : streakCount switch
            {
                1 => "ELIMINATED",
                2 => "DOUBLE KILL",
                3 => "TRIPLE KILL",
                4 => "QUAD KILL",
                _ => "UNSTOPPABLE"
            };
        Color color = ace || streakCount >= 3
            ? new Color(1f, 0.55f, 0.15f, 1f)
            : streakCount == 2
                ? new Color(1f, 0.78f, 0.25f, 1f)
                : new Color(0.95f, 0.95f, 0.95f, 1f);

        Play("kill_confirm", 1f);
        if (ace || streakCount >= 2)
        {
            Play("multikill", 0.8f);
        }
        AddShake(ace ? 0.6f : 0.18f + 0.1f * streakCount);
        ShowBanner(title, FormatAgentName(victim.gameObject), color,
            ace ? 2.2f : 1.2f + 0.2f * streakCount);
    }

    private void OnShopAction(string action)
    {
        string clip = action switch
        {
            "buy" => "ui_buy",
            "sell" => "ui_sell",
            "reroll" => "ui_reroll",
            "deny" => "ui_deny",
            _ => "ui_click"
        };
        PlayUi(clip, 0.85f);
    }

    private void OnCharacterPromoted(CampaignCharacter character)
    {
        PlayUi("rank_up", 1f);
    }

    private void BindRoundManager(RoundManager manager)
    {
        if (boundRoundManager != null)
        {
            boundRoundManager.StateChanged -= OnRoundStateChanged;
            boundRoundManager.RoundResultDeclared -= OnRoundResultDeclared;
        }

        boundRoundManager = manager;
        ResetBattleState();
        roundStartAnnounced = false;
        if (boundRoundManager != null)
        {
            boundRoundManager.StateChanged += OnRoundStateChanged;
            boundRoundManager.RoundResultDeclared += OnRoundResultDeclared;
        }
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Active && !roundStartAnnounced)
        {
            // Active is re-entered when a plant is cancelled; announce only once.
            roundStartAnnounced = true;
            Play("round_start", 0.8f);
            ShowBanner("GO GO GO", "Take a site. Plant the bomb.",
                HudLayoutUtility.TacticalAccent, 1.6f);
        }
        else if (state == RoundState.BombPlanted)
        {
            nextBombBeepTime = 0f;
            ShowBanner("BOMB PLANTED", "Hold the site until it detonates.",
                new Color(1f, 0.42f, 0.2f, 1f), 2f);
        }
        else if (state == RoundState.Exploded)
        {
            AddShake(1f);
        }
    }

    private void OnRoundResultDeclared(TeamType winner, RoundEndReason reason)
    {
        bool won = winner == PlayerTeam;
        pressure = 0f;
        pressureTarget = 0f;
        Play(won ? "round_win" : "round_lose", 0.9f);
    }

    // --- Continuous feedback ----------------------------------------------------

    private void UpdatePressure(float dt)
    {
        if (Time.unscaledTime >= nextPressureSampleTime)
        {
            nextPressureSampleTime = Time.unscaledTime + 0.25f;
            pressureTarget = SamplePressureTarget();
        }

        pressure = Mathf.MoveTowards(pressure, pressureTarget, dt * 1.5f);
        if (pressure > 0.2f && Time.unscaledTime >= nextHeartbeatTime)
        {
            Play("heartbeat", Mathf.Lerp(0.35f, 0.85f, pressure));
            nextHeartbeatTime = Time.unscaledTime + Mathf.Lerp(1.05f, 0.6f, pressure);
        }
    }

    private float SamplePressureTarget()
    {
        float target = 0f;
        if (IsBattleActive())
        {
            int alive = 0;
            float health = 0f;
            float maximum = 0f;
            foreach (HealthSystem agent in FindObjectsByType<HealthSystem>(FindObjectsInactive.Exclude))
            {
                AgentStats stats = agent.GetComponent<AgentStats>();
                if (stats == null || stats.team != PlayerTeam || agent.IsDead)
                {
                    continue;
                }
                alive++;
                health += agent.CurrentHealth;
                maximum += stats.maxHealth;
            }

            bool lastStand = alive == 1 && boundRoundManager != null &&
                             CountLiving(OpposingTeam(PlayerTeam), null) > 0;
            float healthFraction = maximum > 0f ? health / maximum : 1f;
            if (alive > 0 && (lastStand || healthFraction < PressureHealthFraction))
            {
                target = lastStand ? 1f : Mathf.InverseLerp(PressureHealthFraction, 0.1f, healthFraction);
                target = Mathf.Max(target, 0.45f);
            }

            if (lastStand && !lastStandAnnounced)
            {
                lastStandAnnounced = true;
                ShowBanner("LAST OPERATOR STANDING", "Everything rides on this one.",
                    new Color(1f, 0.3f, 0.24f, 1f), 2f);
            }
        }
        return target;
    }

    private void UpdateBombBeeps()
    {
        if (boundRoundManager == null || boundRoundManager.CurrentState != RoundState.BombPlanted)
        {
            return;
        }

        float remaining = boundRoundManager.BombTimeRemaining;
        float fraction = boundRoundManager.bombDuration > 0f
            ? Mathf.Clamp01(remaining / boundRoundManager.bombDuration)
            : 1f;
        if (Time.time >= nextBombBeepTime)
        {
            Play("bomb_beep", Mathf.Lerp(0.85f, 0.45f, fraction));
            // Accelerates from one beep a second to a frantic trill near zero.
            nextBombBeepTime = Time.time + Mathf.Lerp(0.12f, 1.1f, fraction * fraction);
        }
    }

    private void ResetBattleState()
    {
        damageNumbers.Clear();
        streakCount = 0;
        lastPlayerKillTime = -100f;
        lastStandAnnounced = false;
        pressure = 0f;
        pressureTarget = 0f;
        nextBombBeepTime = 0f;
    }

    private bool IsBattleActive()
    {
        if (boundRoundManager == null)
        {
            return false;
        }
        RoundState state = boundRoundManager.CurrentState;
        return state == RoundState.Active || state == RoundState.Planting ||
               state == RoundState.BombPlanted;
    }

    private static TeamType OpposingTeam(TeamType team)
    {
        return team == TeamType.Red ? TeamType.Blue : TeamType.Red;
    }

    private static int CountLiving(TeamType team, HealthSystem excluding)
    {
        int count = 0;
        foreach (HealthSystem agent in FindObjectsByType<HealthSystem>(FindObjectsInactive.Exclude))
        {
            if (agent == excluding || agent.IsDead)
            {
                continue;
            }
            AgentStats stats = agent.GetComponent<AgentStats>();
            if (stats != null && stats.team == team && agent.GetComponent<AgentController3D>() != null)
            {
                count++;
            }
        }
        return count;
    }

    private static string FormatAgentName(GameObject agent)
    {
        AgentRole role = agent.GetComponent<AgentRole>();
        CampaignUnitMarker marker = agent.GetComponent<CampaignUnitMarker>();
        string roleName = role != null ? role.SelectedRole.ToString().ToUpperInvariant() : agent.name;
        return marker != null ? $"{marker.Tier}-TIER {roleName}" : roleName;
    }

    // --- Camera shake ---------------------------------------------------------

    private void AddShake(float amount)
    {
        shakeStrength = Mathf.Clamp01(Mathf.Max(shakeStrength, amount));
    }

    // The offset is applied only while a camera renders and removed right after,
    // so camera controllers that move incrementally never accumulate drift.
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != Camera.main || shakeStrength <= 0.001f)
        {
            return;
        }

        float t = Time.unscaledTime * 38f;
        float magnitude = shakeStrength * shakeStrength * 0.55f;
        appliedShakeOffset = new Vector3(
            (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f,
            0f,
            (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f) * magnitude;
        camera.transform.position += appliedShakeOffset;
        shakenCamera = camera;
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != shakenCamera)
        {
            return;
        }
        camera.transform.position -= appliedShakeOffset;
        appliedShakeOffset = Vector3.zero;
        shakenCamera = null;
    }

    // --- Death burst ---------------------------------------------------------

    private void SpawnDeathBurst(Vector3 position, bool playerUnit)
    {
        GameObject burst = new GameObject("Death Burst");
        burst.transform.position = position + Vector3.up * 0.8f;
        ParticleSystem particles = burst.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Color core = playerUnit ? new Color(1f, 0.35f, 0.25f, 1f) : new Color(1f, 0.8f, 0.35f, 1f);
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.4f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(core, Color.white);
        main.gravityModifier = 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(core, 0.3f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystemRenderer renderer = burst.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetBurstMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.05f;
        renderer.lengthScale = 1.5f;

        particles.Play();
    }

    private Material GetBurstMaterial()
    {
        if (burstMaterial != null)
        {
            return burstMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        burstMaterial = new Material(shader) { name = "Death Burst (Runtime)" };
        if (burstMaterial.HasProperty("_Surface"))
        {
            // Transparent + additive so sparks glow against the dark map.
            burstMaterial.SetFloat("_Surface", 1f);
            burstMaterial.SetFloat("_Blend", 2f);
            burstMaterial.SetOverrideTag("RenderType", "Transparent");
            burstMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            burstMaterial.SetInt("_DstBlend", (int)BlendMode.One);
            burstMaterial.SetInt("_ZWrite", 0);
            burstMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            burstMaterial.renderQueue = (int)RenderQueue.Transparent;
        }
        return burstMaterial;
    }

    // --- Audio ---------------------------------------------------------------

    private AudioSource CreateSource(float volume)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = volume;
        source.ignoreListenerPause = true;
        return source;
    }

    private AudioClip GetClip(string clipName)
    {
        if (!clips.TryGetValue(clipName, out AudioClip clip))
        {
            clip = Resources.Load<AudioClip>(ClipFolder + clipName);
            clips[clipName] = clip;
        }
        return clip;
    }

    private void Play(string clipName, float volume)
    {
        AudioClip clip = GetClip(clipName);
        if (clip != null)
        {
            sfxSource.PlayOneShot(clip, volume);
        }
    }

    private void PlayUi(string clipName, float volume)
    {
        AudioClip clip = GetClip(clipName);
        if (clip != null)
        {
            uiSource.PlayOneShot(clip, volume);
        }
    }

    // --- Banner / damage number presentation ---------------------------------

    private void ShowBanner(string text, string subtitle, Color color, float duration)
    {
        banner = new Banner
        {
            text = text,
            subtitle = subtitle,
            color = color,
            duration = duration
        };
    }

    private void OnGUI()
    {
        if (boundRoundManager == null)
        {
            return;
        }

        EnsureStyles();
        GUI.depth = -500;
        DrawVignette();
        DrawDamageNumbers();
        DrawBanner();
    }

    private void EnsureStyles()
    {
        if (numberStyle != null)
        {
            return;
        }

        numberStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 22
        };
        bannerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 54
        };
        bannerSubtitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 18
        };
    }

    private void DrawVignette()
    {
        if (pressure <= 0.01f || vignetteTexture == null)
        {
            return;
        }

        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(5f, 9f, pressure));
        Color previous = GUI.color;
        GUI.color = new Color(0.85f, 0.05f, 0.04f, pressure * pulse * 0.75f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignetteTexture,
            ScaleMode.StretchToFill, true);
        GUI.color = previous;
    }

    private void DrawDamageNumbers()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Color previous = GUI.color;
        foreach (DamageNumber number in damageNumbers)
        {
            Vector3 screen = camera.WorldToScreenPoint(number.worldPosition);
            if (screen.z <= 0f)
            {
                continue;
            }

            float age = number.age;
            // Pop in large, settle, then float up and fade.
            float pop = age < 0.08f ? Mathf.Lerp(1.7f, 1f, age / 0.08f) : 1f;
            float scale = (number.lethal ? 1.45f : 1f) * pop *
                          Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(10f, 120f, number.amount));
            float alpha = 1f - Mathf.InverseLerp(0.55f, 0.9f, age);
            float rise = 26f + age * 60f;
            string label = number.lethal ? $"{number.amount:0}!" : $"{number.amount:0}";

            int fontSize = Mathf.RoundToInt(22f * scale);
            numberStyle.fontSize = fontSize;
            Rect rect = new Rect(screen.x - 80f, Screen.height - screen.y - rise - fontSize * 0.5f, 160f, fontSize + 8f);

            GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, numberStyle);
            Color color = number.lethal ? new Color(1f, 0.45f, 0.15f, 1f) : number.color;
            color.a = alpha;
            GUI.color = color;
            GUI.Label(rect, label, numberStyle);
        }
        GUI.color = previous;
    }

    private void DrawBanner()
    {
        if (banner == null)
        {
            return;
        }

        float age = banner.age;
        float appear = Mathf.Clamp01(age / 0.12f);
        float vanish = 1f - Mathf.InverseLerp(banner.duration - 0.3f, banner.duration, age);
        float alpha = Mathf.Min(appear, vanish);
        // Overshoot "slam" on entry.
        float scale = age < 0.12f ? Mathf.Lerp(1.6f, 0.95f, appear) : Mathf.Lerp(0.95f, 1f, Mathf.Clamp01((age - 0.12f) / 0.1f));

        float y = Screen.height * 0.2f;
        float width = Screen.width;
        Color previous = GUI.color;

        float stripeHeight = 92f * Mathf.Min(1f, appear * 1.4f);
        GUI.color = new Color(0f, 0f, 0f, 0.45f * alpha);
        GUI.DrawTexture(new Rect(0f, y - stripeHeight * 0.5f, width, stripeHeight), Texture2D.whiteTexture);
        GUI.color = new Color(banner.color.r, banner.color.g, banner.color.b, 0.9f * alpha);
        float lineWidth = Mathf.Lerp(0f, width * 0.42f, appear);
        GUI.DrawTexture(new Rect((width - lineWidth) * 0.5f, y + 30f, lineWidth, 3f), Texture2D.whiteTexture);

        bannerStyle.fontSize = Mathf.RoundToInt(54f * scale * Mathf.Clamp(Screen.height / 1080f, 0.6f, 1.4f));
        Rect titleRect = new Rect(0f, y - 44f, width, 70f);
        GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
        GUI.Label(new Rect(titleRect.x + 3f, titleRect.y + 3f, titleRect.width, titleRect.height), banner.text, bannerStyle);
        GUI.color = new Color(banner.color.r, banner.color.g, banner.color.b, alpha);
        GUI.Label(titleRect, banner.text, bannerStyle);

        if (!string.IsNullOrEmpty(banner.subtitle))
        {
            GUI.color = new Color(0.92f, 0.92f, 0.92f, alpha * 0.9f);
            GUI.Label(new Rect(0f, y + 34f, width, 28f), banner.subtitle, bannerSubtitleStyle);
        }
        GUI.color = previous;
    }

    private static Texture2D CreateVignetteTexture()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Pressure Vignette",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float distance = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, distance));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
