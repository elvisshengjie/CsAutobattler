using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the tactic controls on the existing screen at runtime. Every button writes
/// to TeamTacticManager, so visual selection and live AI state cannot drift apart.
/// </summary>
[DisallowMultipleComponent]
public sealed class TeamTacticUI : MonoBehaviour
{
    private static readonly Color InactiveColor = new Color(0.10f, 0.12f, 0.15f, 0.96f);
    private static readonly Color ActiveColor = new Color(0.08f, 0.48f, 0.60f, 0.98f);
    private static readonly Color AccentColor = new Color(0.20f, 0.86f, 0.96f, 1f);
    private static readonly Color TextColor = new Color(0.94f, 0.97f, 1f, 1f);
    private static Sprite circleButtonSprite;

    private TeamTacticManager tacticManager;
    private RoundManager roundManager;
    private Font font;
    private GameObject initialOverlay;
    private GameObject initialTacticWindow;
    private GameObject roleSelectionWindow;
    private GameObject loadoutSelectionWindow;
    private GameObject currentTacticPanel;
    private GameObject midRoundPanel;
    private Text currentTacticText;
    private Text duplicateRoleWarning;
    private readonly Dictionary<AgentRole, Text> roleLabels =
        new Dictionary<AgentRole, Text>();
    private readonly Dictionary<AgentRole, Text> roleAgentNames =
        new Dictionary<AgentRole, Text>();
    private readonly Dictionary<WeaponLoadout, Text> loadoutLabels =
        new Dictionary<WeaponLoadout, Text>();
    private readonly Dictionary<AgentRole, Text> loadoutAgentLabels =
        new Dictionary<AgentRole, Text>();
    private AgentRole displayedPlanter;
    private readonly Dictionary<MidRoundTactic, Button> midRoundButtons =
        new Dictionary<MidRoundTactic, Button>();
    private readonly Dictionary<MidRoundTactic, Text> midRoundLabels =
        new Dictionary<MidRoundTactic, Text>();
    private readonly Dictionary<PlantSitePreference, Button> plantPreferenceButtons =
        new Dictionary<PlantSitePreference, Button>();
    private GameObject plantInfoPanel;
    private Text plantInfoText;

    private void Awake()
    {
        tacticManager = GetComponent<TeamTacticManager>();
    }

    private void Start()
    {
        roundManager = RoundManager.Instance != null
            ? RoundManager.Instance
            : FindAnyObjectByType<RoundManager>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();

        tacticManager.InitialTacticSelected += OnInitialTacticSelected;
        tacticManager.TacticsChanged += Refresh;
        tacticManager.RoundTacticsReset += Refresh;
        tacticManager.RolesChanged += Refresh;
        tacticManager.LoadoutsChanged += Refresh;
        if (roundManager != null)
        {
            roundManager.StateChanged += OnRoundStateChanged;
        }

        Refresh();
    }

    private void OnDestroy()
    {
        if (tacticManager != null)
        {
            tacticManager.InitialTacticSelected -= OnInitialTacticSelected;
            tacticManager.TacticsChanged -= Refresh;
            tacticManager.RoundTacticsReset -= Refresh;
            tacticManager.RolesChanged -= Refresh;
            tacticManager.LoadoutsChanged -= Refresh;
        }

        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject(
            "TeamTacticCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        BuildInitialSelection(canvasObject.transform);
        BuildCurrentTacticPanel(canvasObject.transform);
        BuildMidRoundPanel(canvasObject.transform);
    }

    private void BuildInitialSelection(Transform parent)
    {
        initialOverlay = CreatePanel("InitialTacticOverlay", parent,
            new Color(0.015f, 0.025f, 0.04f, 0.82f));
        RectTransform overlayRect = initialOverlay.GetComponent<RectTransform>();
        StretchToParent(overlayRect);

        GameObject window = CreatePanel("SelectionWindow", initialOverlay.transform,
            new Color(0.055f, 0.07f, 0.095f, 0.99f));
        initialTacticWindow = window;
        SetRect(window.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 520f),
            new Vector2(0.5f, 0.5f));
        AddOutline(window, AccentColor, new Vector2(2f, -2f));

        Text title = CreateText("Title", window.transform,
            "CHOOSE INITIAL TEAM TACTIC", 30, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -48f), new Vector2(960f, 55f), new Vector2(0.5f, 0.5f));
        title.color = AccentColor;

        InitialTeamTactic[] tactics = (InitialTeamTactic[])Enum.GetValues(
            typeof(InitialTeamTactic));
        Vector2[] positions =
        {
            new Vector2(-225f, 80f),
            new Vector2(225f, 80f),
            new Vector2(-225f, -120f),
            new Vector2(225f, -120f)
        };

        for (int i = 0; i < tactics.Length; i++)
        {
            InitialTeamTactic captured = tactics[i];
            string label = $"<b>{TeamTacticDefinitions.GetName(captured)}</b>\n\n" +
                           TeamTacticDefinitions.GetShortDescription(captured);
            Button button = CreateTacticButton(
                TeamTacticDefinitions.GetName(captured),
                window.transform,
                label,
                18);
            SetRect(button.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                positions[i], new Vector2(410f, 165f), new Vector2(0.5f, 0.5f));
            button.onClick.AddListener(() => tacticManager.SelectInitialTactic(captured));
        }

        BuildRoleSelection(initialOverlay.transform);
        BuildLoadoutSelection(initialOverlay.transform);
    }

    private void Update()
    {
        if (roleSelectionWindow == null || !roleSelectionWindow.activeInHierarchy)
        {
            return;
        }

        AgentRole currentPlanter = FindPlanter();
        if (currentPlanter != displayedPlanter)
        {
            displayedPlanter = currentPlanter;
            RefreshAgentNames();
        }
    }

    private void BuildRoleSelection(Transform parent)
    {
        roleSelectionWindow = CreatePanel("RoleSelectionWindow", parent,
            new Color(0.055f, 0.07f, 0.095f, 0.99f));
        SetRect(roleSelectionWindow.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 680f), new Vector2(0.5f, 0.5f));
        AddOutline(roleSelectionWindow, AccentColor, new Vector2(2f, -2f));

        Text title = CreateText("RoleTitle", roleSelectionWindow.transform,
            "ASSIGN TACTICAL ROLES", 30, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -42f), new Vector2(800f, 48f), new Vector2(0.5f, 0.5f));
        title.color = AccentColor;
        Text hint = CreateText("RoleHint", roleSelectionWindow.transform,
            "Click an agent's role to cycle it. Duplicate roles are allowed.", 16,
            FontStyle.Normal, TextAnchor.MiddleCenter);
        SetRect(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -82f), new Vector2(800f, 35f), new Vector2(0.5f, 0.5f));

        List<AgentRole> agents = tacticManager.GetControlledRoles();
        roleLabels.Clear();
        roleAgentNames.Clear();
        for (int i = 0; i < agents.Count; i++)
        {
            AgentRole captured = agents[i];
            CreateAgentMaterialSwatch(captured, i);
            Text name = CreateText("AgentName", roleSelectionWindow.transform,
                captured.name, 18, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetRect(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-155f, -145f - i * 78f), new Vector2(370f, 58f), new Vector2(0.5f, 0.5f));
            roleAgentNames[captured] = name;
            Button roleButton = CreateTacticButton("Role_" + captured.name,
                roleSelectionWindow.transform, string.Empty, 17);
            SetRect(roleButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(210f, -145f - i * 78f), new Vector2(330f, 58f), new Vector2(0.5f, 0.5f));
            Text label = roleButton.GetComponentInChildren<Text>();
            roleLabels[captured] = label;
            roleButton.onClick.AddListener(() => CycleRole(captured));
        }
        displayedPlanter = FindPlanter();
        RefreshAgentNames();

        duplicateRoleWarning = CreateText("DuplicateWarning", roleSelectionWindow.transform,
            string.Empty, 15, FontStyle.Normal, TextAnchor.MiddleCenter);
        SetRect(duplicateRoleWarning.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 103f), new Vector2(800f, 35f), new Vector2(0.5f, 0.5f));
        duplicateRoleWarning.color = new Color(1f, 0.82f, 0.35f, 1f);

        Button auto = CreateTacticButton("AutoAssign", roleSelectionWindow.transform,
            "<b>AUTO BALANCE</b>", 17);
        SetRect(auto.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-195f, 48f), new Vector2(330f, 65f), new Vector2(0.5f, 0.5f));
        auto.onClick.AddListener(tacticManager.AssignBalancedRoles);
        Button start = CreateTacticButton("NextLoadouts", roleSelectionWindow.transform,
            "<b>NEXT: CHOOSE WEAPONS</b>", 18);
        SetRect(start.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(195f, 48f), new Vector2(330f, 65f), new Vector2(0.5f, 0.5f));
        start.onClick.AddListener(tacticManager.ConfirmRoles);
    }

    private void BuildLoadoutSelection(Transform parent)
    {
        loadoutSelectionWindow = CreatePanel("LoadoutSelectionWindow", parent,
            new Color(0.055f, 0.07f, 0.095f, 0.99f));
        SetRect(loadoutSelectionWindow.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(1120f, 720f), new Vector2(0.5f, 0.5f));
        AddOutline(loadoutSelectionWindow, AccentColor, new Vector2(2f, -2f));

        Text title = CreateText("LoadoutTitle", loadoutSelectionWindow.transform,
            "CHOOSE AGENT LOADOUTS", 30, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -42f), new Vector2(980f, 48f), new Vector2(0.5f, 0.5f));
        title.color = AccentColor;
        Text hint = CreateText("LoadoutHint", loadoutSelectionWindow.transform,
            "Click a weapon card to cycle Rifle, SMG, Sniper, and Shotgun.", 16,
            FontStyle.Normal, TextAnchor.MiddleCenter);
        SetRect(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -82f), new Vector2(980f, 35f), new Vector2(0.5f, 0.5f));

        loadoutLabels.Clear();
        loadoutAgentLabels.Clear();
        List<AgentRole> roles = tacticManager.GetControlledRoles();
        for (int i = 0; i < roles.Count; i++)
        {
            AgentRole role = roles[i];
            WeaponLoadout loadout = WeaponLoadout.Get(role.gameObject);
            Text agentLabel = CreateText("LoadoutAgentName", loadoutSelectionWindow.transform,
                $"<b>{role.name}</b>\n<color=#33DBF5>{role.SelectedRole}</color>",
                17, FontStyle.Normal, TextAnchor.MiddleLeft);
            SetRect(agentLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-390f, -145f - i * 88f), new Vector2(260f, 70f),
                new Vector2(0.5f, 0.5f));
            loadoutAgentLabels[role] = agentLabel;

            Button weaponButton = CreateTacticButton("Weapon_" + role.name,
                loadoutSelectionWindow.transform, string.Empty, 15);
            SetRect(weaponButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(145f, -145f - i * 88f), new Vector2(760f, 76f),
                new Vector2(0.5f, 0.5f));
            Text weaponLabel = weaponButton.GetComponentInChildren<Text>();
            loadoutLabels[loadout] = weaponLabel;
            weaponButton.onClick.AddListener(() => CycleWeapon(loadout));
        }

        Button recommended = CreateTacticButton("RecommendedLoadouts",
            loadoutSelectionWindow.transform, "<b>ROLE RECOMMENDED</b>", 17);
        SetRect(recommended.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-215f, 48f), new Vector2(380f, 65f), new Vector2(0.5f, 0.5f));
        recommended.onClick.AddListener(tacticManager.AssignRecommendedLoadouts);

        Button start = CreateTacticButton("ConfirmLoadouts",
            loadoutSelectionWindow.transform, "<b>START MATCH</b>", 18);
        SetRect(start.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(215f, 48f), new Vector2(380f, 65f), new Vector2(0.5f, 0.5f));
        start.onClick.AddListener(tacticManager.ConfirmLoadouts);
    }

    private void CycleWeapon(WeaponLoadout loadout)
    {
        WeaponType next = (WeaponType)(((int)loadout.SelectedWeapon + 1) %
            Enum.GetValues(typeof(WeaponType)).Length);
        tacticManager.SetAgentWeapon(loadout, next);
    }

    private void CreateAgentMaterialSwatch(AgentRole agent, int row)
    {
        GameObject swatchObject = new GameObject(
            "MaterialSwatch_" + agent.name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage),
            typeof(Outline));
        swatchObject.transform.SetParent(roleSelectionWindow.transform, false);
        SetRect(swatchObject.GetComponent<RectTransform>(),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-385f, -145f - row * 78f), new Vector2(54f, 54f),
            new Vector2(0.5f, 0.5f));

        RawImage preview = swatchObject.GetComponent<RawImage>();
        AgentPortraitData portraitData = agent.GetComponent<AgentPortraitData>();
        Renderer renderer = portraitData != null ? portraitData.sourceRenderer : null;
        if (renderer == null)
        {
            renderer = FindAgentVisualRenderer(agent.gameObject);
        }
        Material material = renderer != null ? renderer.sharedMaterial : null;
        preview.texture = material != null && material.mainTexture != null
            ? material.mainTexture
            : Texture2D.whiteTexture;
        preview.color = GetMaterialPreviewColor(material);
        preview.raycastTarget = false;

        Outline outline = swatchObject.GetComponent<Outline>();
        outline.effectColor = AccentColor;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private static Renderer FindAgentVisualRenderer(GameObject agent)
    {
        Renderer[] renderers = agent.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer candidate in renderers)
        {
            if (candidate == null) continue;
            string objectName = candidate.gameObject.name.ToLowerInvariant();
            if (objectName.Contains("health") || objectName.Contains("bar") ||
                objectName.Contains("debug")) continue;
            return candidate;
        }
        return null;
    }

    private static Color GetMaterialPreviewColor(Material material)
    {
        if (material == null)
        {
            return new Color(0.45f, 0.48f, 0.52f, 1f);
        }
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }
        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }
        return Color.white;
    }

    private AgentRole FindPlanter()
    {
        foreach (AgentRole role in roleAgentNames.Keys)
        {
            if (role != null && role.GetComponent<BombCarrier>()?.HasBomb == true)
            {
                return role;
            }
        }
        return null;
    }

    private void RefreshAgentNames()
    {
        foreach (KeyValuePair<AgentRole, Text> pair in roleAgentNames)
        {
            if (pair.Key == null || pair.Value == null)
            {
                continue;
            }

            bool isPlanter = pair.Key.GetComponent<BombCarrier>()?.HasBomb == true;
            pair.Value.text = isPlanter
                ? pair.Key.name + "\n<color=#FFD166><b>PLANTER / BOMB CARRIER</b></color>"
                : pair.Key.name;
            pair.Value.color = isPlanter
                ? new Color(1f, 0.88f, 0.48f, 1f)
                : TextColor;
        }
    }

    private void CycleRole(AgentRole agent)
    {
        AgentRoleType next = (AgentRoleType)(((int)agent.SelectedRole + 1) %
            Enum.GetValues(typeof(AgentRoleType)).Length);
        tacticManager.SetAgentRole(agent, next);
    }

    private void BuildCurrentTacticPanel(Transform parent)
    {
        currentTacticPanel = CreatePanel("CurrentInitialTactic", parent,
            new Color(0.045f, 0.06f, 0.08f, 0.97f));
        SetRect(currentTacticPanel.GetComponent<RectTransform>(),
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-14f, -14f), new Vector2(220f, 74f), new Vector2(1f, 1f));
        HudLayoutUtility.TryCopyPreviewRect(
            "InitialTacticPreview",
            currentTacticPanel.GetComponent<RectTransform>());
        AddOutline(currentTacticPanel, AccentColor, new Vector2(-2f, -2f));

        currentTacticText = CreateText("CurrentTacticText", currentTacticPanel.transform,
            string.Empty, 16, FontStyle.Normal, TextAnchor.MiddleLeft);
        StretchToParent(currentTacticText.rectTransform, 14f, 14f, 8f, 8f);
    }

    private void BuildMidRoundPanel(Transform parent)
    {
        midRoundPanel = CreatePanel("MidRoundTactics", parent,
            new Color(0.035f, 0.045f, 0.06f, 0.93f));
        SetRect(midRoundPanel.GetComponent<RectTransform>(),
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-20f, -126f), new Vector2(300f, 430f), new Vector2(1f, 1f));

        Text heading = CreateText("Heading", midRoundPanel.transform,
            "MID-ROUND TACTICS", 20, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(heading.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -30f), new Vector2(-28f, 42f), new Vector2(0.5f, 0.5f));
        heading.color = AccentColor;

        Text hint = CreateText("Hint", midRoundPanel.transform,
            "Clicks run in order. Click again to cancel.", 13,
            FontStyle.Normal, TextAnchor.MiddleLeft);
        SetRect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -61f), new Vector2(-28f, 28f), new Vector2(0.5f, 0.5f));
        hint.color = new Color(0.64f, 0.70f, 0.76f, 1f);

        MidRoundTactic[] tactics = (MidRoundTactic[])Enum.GetValues(typeof(MidRoundTactic));
        for (int i = 0; i < tactics.Length; i++)
        {
            MidRoundTactic captured = tactics[i];
            Button button = CreateTacticButton(
                TeamTacticDefinitions.GetName(captured),
                midRoundPanel.transform,
                GetMidRoundLabel(captured),
                16);
            SetRect(button.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                captured == MidRoundTactic.Plant ? new Vector2(0f, 1f) : new Vector2(1f, 1f),
                captured == MidRoundTactic.Plant
                    ? new Vector2(55f, -106f - i * 74f)
                    : new Vector2(0f, -106f - i * 74f),
                captured == MidRoundTactic.Plant
                    ? new Vector2(82f, 58f)
                    : new Vector2(-28f, 58f),
                new Vector2(0.5f, 0.5f));
            button.onClick.AddListener(() => tacticManager.ToggleMidRoundTactic(captured));
            midRoundButtons[captured] = button;
            midRoundLabels[captured] = button.GetComponentInChildren<Text>();
            if (captured == MidRoundTactic.Plant)
            {
                Text plantLabel = midRoundLabels[captured];
                plantLabel.alignment = TextAnchor.MiddleCenter;
                StretchToParent(plantLabel.rectTransform, 3f, 3f, 3f, 3f);
            }
        }

        BuildPlantPreferenceControls();
    }

    private void BuildPlantPreferenceControls()
    {
        const float plantRowY = -180f;
        PlantSitePreference[] preferences =
            (PlantSitePreference[])Enum.GetValues(typeof(PlantSitePreference));
        for (int i = 0; i < preferences.Length; i++)
        {
            PlantSitePreference captured = preferences[i];
            Button button = CreateTacticButton(
                "PlantPreference_" + captured,
                midRoundPanel.transform,
                captured == PlantSitePreference.Auto ? "AUTO" : captured.ToString(),
                captured == PlantSitePreference.Auto ? 11 : 14);
            SetRect(button.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(123f + i * 42f, plantRowY),
                new Vector2(40f, 58f), new Vector2(0.5f, 0.5f));
            Text label = button.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleCenter;
            StretchToParent(label.rectTransform, 1f, 1f, 1f, 1f);
            button.onClick.AddListener(() =>
                tacticManager.SetPlantSitePreference(captured));
            plantPreferenceButtons[captured] = button;
        }

        Button infoButton = CreateTacticButton(
            "PlantPreferenceInfo",
            midRoundPanel.transform,
            "!",
            17);
        SetRect(infoButton.GetComponent<RectTransform>(),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(249f, plantRowY),
            new Vector2(30f, 30f), new Vector2(0.5f, 0.5f));
        Text infoLabel = infoButton.GetComponentInChildren<Text>();
        infoLabel.alignment = TextAnchor.MiddleCenter;
        StretchToParent(infoLabel.rectTransform);
        Image circleImage = infoButton.GetComponent<Image>();
        circleImage.sprite = GetCircleButtonSprite();
        circleImage.preserveAspect = true;
        infoButton.onClick.AddListener(() =>
            plantInfoPanel.SetActive(!plantInfoPanel.activeSelf));

        plantInfoPanel = CreatePanel(
            "PlantPreferenceHelp",
            midRoundPanel.transform,
            new Color(0.045f, 0.06f, 0.08f, 0.99f));
        SetRect(plantInfoPanel.GetComponent<RectTransform>(),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(-10f, -130f), new Vector2(360f, 156f),
            new Vector2(1f, 1f));
        AddOutline(plantInfoPanel, AccentColor, new Vector2(-2f, -2f));
        plantInfoText = CreateText(
            "PlantPreferenceHelpText",
            plantInfoPanel.transform,
            string.Empty,
            13,
            FontStyle.Normal,
            TextAnchor.MiddleLeft);
        StretchToParent(plantInfoText.rectTransform, 16f, 16f, 12f, 12f);
        plantInfoPanel.SetActive(false);
    }

    private static Sprite GetCircleButtonSprite()
    {
        if (circleButtonSprite != null) return circleButtonSprite;

        const int size = 32;
        Texture2D texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false)
        {
            name = "RuntimeCircleButton",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = size * 0.48f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x, y),
                    new Vector2(center, center));
                float alpha = Mathf.Clamp01(radius - distance + 0.75f);
                pixels[y * size + x] = new Color32(
                    255,
                    255,
                    255,
                    (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        circleButtonSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size,
            0,
            SpriteMeshType.FullRect);
        circleButtonSprite.name = "RuntimeCircleButtonSprite";
        circleButtonSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleButtonSprite;
    }

    private Button CreateTacticButton(
        string objectName,
        Transform parent,
        string label,
        int fontSize)
    {
        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(Outline));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = InactiveColor;
        image.raycastTarget = true;

        Outline outline = buttonObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.28f, 0.33f, 0.39f, 1f);
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.75f, 0.82f, 0.88f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Text text = CreateText("Label", buttonObject.transform, label, fontSize,
            FontStyle.Normal, TextAnchor.MiddleLeft);
        StretchToParent(text.rectTransform, 18f, 18f, 10f, 10f);
        text.raycastTarget = false;
        text.supportRichText = true;
        return button;
    }

    private void Refresh()
    {
        if (tacticManager == null || roundManager == null || initialOverlay == null)
        {
            return;
        }

        bool controlledRound = tacticManager.ControlsAttackingTeam(roundManager);
        bool selected = tacticManager.HasSelectedInitialTactic;
        bool roundEnded = roundManager.CurrentState == RoundState.RoundEnd;

        bool rolesConfirmed = tacticManager.RolesConfirmed;
        bool loadoutsConfirmed = tacticManager.LoadoutsConfirmed;
        initialOverlay.SetActive(controlledRound &&
                                 roundManager.CurrentState == RoundState.Preparation &&
                                 (!selected || !rolesConfirmed || !loadoutsConfirmed));
        initialTacticWindow.SetActive(!selected);
        roleSelectionWindow.SetActive(selected && !rolesConfirmed);
        loadoutSelectionWindow.SetActive(selected && rolesConfirmed && !loadoutsConfirmed);
        currentTacticPanel.SetActive(controlledRound && selected && !roundEnded);
        midRoundPanel.SetActive(controlledRound && selected &&
                                roundManager.CurrentState != RoundState.Preparation &&
                                !roundEnded);

        if (selected)
        {
            if (tacticManager.TryGetCurrentMidRoundTactic(out _))
            {
                List<string> commands = new List<string>();
                foreach (MidRoundTactic command in tacticManager.GetActiveMidRoundTactics())
                {
                    string name = TeamTacticDefinitions.GetName(command);
                    if (command == MidRoundTactic.Plant &&
                        tacticManager.QueuedPlantSitePreference != PlantSitePreference.Auto)
                        name += "[" + tacticManager.QueuedPlantSitePreference + "]";
                    commands.Add(name);
                }
                currentTacticText.text = "<b>MID-ROUND:</b>\n" +
                    $"<color=#33DBF5>{string.Join(" > ", commands)}</color>";
            }
            else
            {
                currentTacticText.text = "<b>INITIAL TACTIC:</b>\n" +
                    $"<color=#33DBF5>{TeamTacticDefinitions.GetName(tacticManager.GetSelectedInitialTactic())}</color>";
            }
        }

        int duplicateCount = 0;
        HashSet<AgentRoleType> seenRoles = new HashSet<AgentRoleType>();
        foreach (KeyValuePair<AgentRole, Text> pair in roleLabels)
        {
            if (pair.Key == null) continue;
            pair.Value.text = $"<b>{pair.Key.SelectedRole}</b>\n<color=#AAB5C2>click to change</color>";
            if (!seenRoles.Add(pair.Key.SelectedRole)) duplicateCount++;
        }
        if (duplicateRoleWarning != null)
            duplicateRoleWarning.text = duplicateCount > 0
                ? $"Warning: {duplicateCount} duplicate role assignment(s)" : "Balanced role coverage";

        foreach (KeyValuePair<WeaponLoadout, Text> pair in loadoutLabels)
        {
            if (pair.Key == null || pair.Value == null) continue;
            WeaponDefinition weapon = pair.Key.Definition;
            float baseSpeed = pair.Key.GetComponent<AgentStats>() != null
                ? pair.Key.GetComponent<AgentStats>().moveSpeed : 0f;
            float damagePerSecond = CalculateSustainedDamagePerSecond(weapon);
            pair.Value.text = $"<b>{weapon.weaponType}</b>  |  " +
                $"{weapon.agentHealth:0.#} HEALTH  |  " +
                $"{GetDamageLabel(weapon)} DMG  |  " +
                $"{damagePerSecond:0.#} DPS  |  " +
                $"{weapon.effectiveMinimumRange:0.#}–{weapon.effectiveMaximumRange:0.#} RANGE  |  " +
                $"{baseSpeed * weapon.movementSpeedMultiplier:0.##} MOV\n" +
                $"<color=#AAB5C2>{weapon.behaviorDescription}</color>";
        }
        foreach (KeyValuePair<AgentRole, Text> pair in loadoutAgentLabels)
        {
            if (pair.Key != null && pair.Value != null)
                pair.Value.text = $"<b>{pair.Key.name}</b>\n" +
                    $"<color=#33DBF5>{pair.Key.SelectedRole}</color>";
        }

        foreach (KeyValuePair<MidRoundTactic, Button> pair in midRoundButtons)
        {
            bool active = tacticManager.IsMidRoundTacticActive(pair.Key);
            Image image = pair.Value.GetComponent<Image>();
            Outline outline = pair.Value.GetComponent<Outline>();
            image.color = active ? ActiveColor : InactiveColor;
            outline.effectColor = active ? AccentColor : new Color(0.28f, 0.33f, 0.39f, 1f);
            outline.effectDistance = active ? new Vector2(2f, -2f) : new Vector2(1f, -1f);

            midRoundLabels[pair.Key].text = GetMidRoundLabel(pair.Key);
        }

        foreach (KeyValuePair<PlantSitePreference, Button> pair in plantPreferenceButtons)
        {
            bool selectedPreference =
                pair.Key == tacticManager.SelectedPlantSitePreference;
            Image image = pair.Value.GetComponent<Image>();
            Outline outline = pair.Value.GetComponent<Outline>();
            image.color = selectedPreference ? ActiveColor : InactiveColor;
            outline.effectColor = selectedPreference
                ? AccentColor
                : new Color(0.28f, 0.33f, 0.39f, 1f);
        }

        if (plantInfoText != null)
        {
            plantInfoText.text =
                "<b>PLANT SITE PREFERENCE</b>\n" +
                "A/B is advice, not an order. AI still weighs route safety, " +
                "enemy pressure, teammate support, carrier distance and round time." +
                (string.IsNullOrEmpty(tacticManager.PlantDecisionSummary)
                    ? string.Empty
                    : "\n<color=#33DBF5>" + tacticManager.PlantDecisionSummary + "</color>");
        }
    }

    private static float CalculateSustainedDamagePerSecond(WeaponDefinition weapon)
    {
        int magazineSize = Mathf.Max(1, weapon.magazineSize);
        int burstCount = Mathf.Max(1, weapon.burstCount);
        int projectilesPerShot = Mathf.Max(1, weapon.projectilesPerShot);
        int firingCycles = Mathf.CeilToInt((float)magazineSize / burstCount);
        float magazineDamage = weapon.damage * projectilesPerShot * magazineSize;
        float cycleTime = firingCycles * Mathf.Max(0.01f, weapon.fireCooldown);
        float totalTime = cycleTime + Mathf.Max(0f, weapon.reloadTime);
        return totalTime > 0f ? magazineDamage / totalTime : 0f;
    }

    private static string GetDamageLabel(WeaponDefinition weapon)
    {
        if (weapon.projectilesPerShot > 1)
            return $"{weapon.projectilesPerShot}×{weapon.damage:0.#}";
        if (weapon.burstCount > 1)
            return $"{weapon.damage:0.#}×{weapon.burstCount}";
        return weapon.damage.ToString("0.#");
    }

    private string GetMidRoundLabel(MidRoundTactic tactic)
    {
        return $"<b>{TeamTacticDefinitions.GetName(tactic)}</b>";
    }

    private void OnInitialTacticSelected(InitialTeamTactic tactic)
    {
        Refresh();
    }

    private void OnRoundStateChanged(RoundState state)
    {
        Refresh();
    }

    private Text CreateText(
        string objectName,
        Transform parent,
        string content,
        int fontSize,
        FontStyle style,
        TextAnchor alignment)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = TextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = true;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreatePanel(string objectName, Transform parent, Color color)
    {
        GameObject panel = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        panel.transform.SetParent(parent, false);
        Image image = panel.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = objectName == "InitialTacticOverlay";
        return panel;
    }

    private static void AddOutline(GameObject target, Color color, Vector2 distance)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 pivot)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.pivot = pivot;
    }

    private static void StretchToParent(
        RectTransform rect,
        float left = 0f,
        float right = 0f,
        float top = 0f,
        float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
