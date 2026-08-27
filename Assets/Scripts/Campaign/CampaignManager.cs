using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent three-round campaign prototype. The current map supplies five agent
/// slots per team; this manager turns those slots into a roster, shop and deployment.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class CampaignManager : MonoBehaviour
{
    private enum CampaignScreen
    {
        MainMenu,
        None,
        StarterDraft,
        Preparation,
        Result,
        Complete
    }

    public static CampaignManager Instance { get; private set; }

    private readonly List<CampaignCharacter> roster = new List<CampaignCharacter>();
    private readonly CampaignCharacter[] starterOffers = new CampaignCharacter[3];
    private readonly CampaignCharacter[] shopOffers = new CampaignCharacter[3];
    private readonly List<CampaignUpgradeKind> permanentUpgrades =
        new List<CampaignUpgradeKind>();
    private readonly List<CampaignUpgradeKind> pendingTemporaryUpgrades =
        new List<CampaignUpgradeKind>();
    private readonly Dictionary<int, Texture2D> characterPortraitTextures =
        new Dictionary<int, Texture2D>();

    private CampaignRoundDefinition[] rounds;
    private CampaignScreen screen;
    private System.Random random;
    private RoundManager roundManager;
    private string campaignSceneName;
    private Vector2 scrollPosition;
    private int currentRoundIndex;
    private int gold = 5;
    private int nextCharacterId = 1;
    private int preparedShopRound = -1;
    private int lastIncome;
    private int sceneGeneration;
    private Scene preparedScene;
    private bool hasPreparedScene;
    private bool deploymentCommitted;
    private bool shopFrozen;
    private bool modifierOfferPurchased;
    private bool lastRoundWon;
    private bool campaignStarted;
    private bool showGameOverMessage;
    private CampaignUpgradeKind modifierOffer;
    private string feedback = string.Empty;
    private Texture2D campaignPanelTexture;
    private Texture2D campaignCardTexture;
    private Texture2D campaignButtonTexture;
    private Texture2D campaignButtonHoverTexture;
    private Texture2D campaignButtonPressedTexture;
    private Texture2D campaignAccentTexture;
    private Texture2D menuBackgroundTexture;
    private GUIStyle campaignPanelStyle;
    private GUIStyle campaignCardStyle;
    private GUIStyle campaignButtonStyle;
    private GUIStyle campaignToggleStyle;
    private GUIStyle menuTitleStyle;
    private GUIStyle menuSubtitleStyle;
    private GUIStyle menuButtonStyle;
    private GUIStyle menuSmallStyle;
    private bool showMenuHelp;

    public bool IsCampaignScene { get; private set; }
    public bool IsPreparationBlocking => IsCampaignScene && !deploymentCommitted;
    public bool UsesLockedCharacters => IsCampaignScene;
    public CampaignEnemyTactic CurrentEnemyTactic => CurrentRound.enemyTactic;
    public CampaignRoundDefinition CurrentRound =>
        rounds[Mathf.Clamp(currentRoundIndex, 0, rounds.Length - 1)];
    public IReadOnlyList<CampaignCharacter> Roster => roster;
    public int Gold => gold;
    public int CurrentRoundNumber => currentRoundIndex + 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null)
        {
            new GameObject("Campaign System").AddComponent<CampaignManager>();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAfterSceneLoad()
    {
        EnsureForCurrentScene();
    }

    /// <summary>
    /// Called by the legacy tactic bootstrap as well as our own runtime hooks. This
    /// makes campaign ownership deterministic even when runtime initializer order or
    /// Enter Play Mode domain-reload settings differ.
    /// </summary>
    public static bool EnsureForCurrentScene()
    {
        if (Instance == null)
        {
            new GameObject("Campaign System").AddComponent<CampaignManager>();
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded)
        {
            Instance.PrepareCampaignScene(scene);
        }
        return Instance.IsCampaignScene;
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
        rounds = CampaignRoundDefinition.CreatePrototypeRounds();
        random = new System.Random(Environment.TickCount);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        // Also covers entering Play Mode after a script/domain reload while the
        // active map is already loaded.
        EnsureForCurrentScene();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PrepareCampaignScene(scene);
    }

    private void PrepareCampaignScene(Scene scene)
    {
        if (hasPreparedScene && preparedScene == scene)
        {
            return;
        }
        preparedScene = scene;
        hasPreparedScene = true;

        UnbindRoundManager();
        StopAllCoroutines();
        sceneGeneration++;

        roundManager = RoundManager.Instance != null
            ? RoundManager.Instance
            : FindAnyObjectByType<RoundManager>();
        IsCampaignScene = roundManager != null;
        if (!IsCampaignScene)
        {
            screen = CampaignScreen.None;
            return;
        }

        campaignSceneName = scene.name;
        deploymentCommitted = false;
        feedback = string.Empty;
        ConfigureSceneSlots();
        PrepareShopForCurrentRound();

        roundManager.RoundResultDeclared += OnRoundResultDeclared;
        roundManager.StateChanged += OnRoundStateChanged;
        screen = !campaignStarted
            ? CampaignScreen.MainMenu
            : roster.Count == 0
                ? CampaignScreen.StarterDraft
                : CampaignScreen.Preparation;

        if (screen == CampaignScreen.StarterDraft)
        {
            GenerateStarterOffers();
        }
    }

    private void ConfigureSceneSlots()
    {
        AgentStats[] allAgents = FindObjectsByType<AgentStats>(FindObjectsInactive.Include);
        List<AgentStats> playerSlots = allAgents
            .Where(agent => agent.team == TeamType.Red &&
                            agent.GetComponent<AgentController3D>() != null)
            .OrderBy(agent => agent.name, StringComparer.Ordinal)
            .ToList();
        List<AgentStats> enemySlots = allAgents
            .Where(agent => agent.team == TeamType.Blue &&
                            agent.GetComponent<AgentController3D>() != null)
            .OrderBy(agent => agent.name, StringComparer.Ordinal)
            .ToList();

        foreach (AgentStats slot in playerSlots)
        {
            slot.gameObject.SetActive(false);
        }

        for (int i = 0; i < enemySlots.Count; i++)
        {
            bool used = i < CurrentRound.enemies.Length;
            enemySlots[i].gameObject.SetActive(used);
            if (!used)
            {
                continue;
            }

            ConfigureAgent(
                enemySlots[i],
                CurrentRound.enemies[i].role,
                CurrentRound.enemies[i].weapon,
                CurrentRound.enemies[i].tier,
                CurrentRound.enemyHealthMultiplier,
                CurrentRound.enemyDamageMultiplier,
                CurrentRound.enemies[i].abilitiesEnabled,
                0);
        }
    }

    private void ConfigureAgent(
        AgentStats agent,
        AgentRoleType roleType,
        WeaponType weaponType,
        CharacterTier tier,
        float extraHealthMultiplier,
        float extraDamageMultiplier,
        bool abilitiesEnabled,
        int characterId)
    {
        AgentRole role = agent.GetComponent<AgentRole>();
        if (role == null)
        {
            role = agent.gameObject.AddComponent<AgentRole>();
        }
        role.SetRole(roleType);

        WeaponLoadout loadout = WeaponLoadout.Get(agent.gameObject);
        loadout.ConfigureCampaignMultipliers(
            CampaignBalance.GetHealthMultiplier(tier) * extraHealthMultiplier,
            CampaignBalance.GetDamageMultiplier(tier) * extraDamageMultiplier,
            CampaignBalance.GetAccuracyBonus(tier),
            CampaignBalance.GetMovementMultiplier(tier));
        loadout.SelectWeapon(weaponType);

        AgentRoleAbilities abilities = agent.GetComponent<AgentRoleAbilities>();
        if (abilities == null)
        {
            abilities = agent.gameObject.AddComponent<AgentRoleAbilities>();
        }
        abilities.ApplyCampaignTier(tier);
        abilities.enabled = abilitiesEnabled;

        CampaignUnitMarker marker = agent.GetComponent<CampaignUnitMarker>();
        if (marker == null)
        {
            marker = agent.gameObject.AddComponent<CampaignUnitMarker>();
        }
        marker.Configure(characterId, tier);
    }

    private void GenerateStarterOffers()
    {
        List<AgentRoleType> roles = Enum.GetValues(typeof(AgentRoleType))
            .Cast<AgentRoleType>()
            .OrderBy(_ => random.Next())
            .Take(3)
            .ToList();

        for (int i = 0; i < starterOffers.Length; i++)
        {
            starterOffers[i] = CreateRandomCharacter(CharacterTier.C, roles[i]);
        }
    }

    private CampaignCharacter CreateRandomCharacter(
        CharacterTier tier,
        AgentRoleType? forcedRole = null)
    {
        return new CampaignCharacter
        {
            id = nextCharacterId++,
            role = forcedRole ?? (AgentRoleType)random.Next(0, 4),
            weapon = (WeaponType)random.Next(0, 4),
            tier = tier
        };
    }

    private void ChooseStarter(int index)
    {
        if (index < 0 || index >= starterOffers.Length || starterOffers[index] == null)
        {
            return;
        }

        CampaignCharacter starter = starterOffers[index];
        starter.deployed = true;
        roster.Add(starter);
        screen = CampaignScreen.Preparation;
        feedback = starter.DisplayName + " joined your roster.";
    }

    private void PrepareShopForCurrentRound()
    {
        if (preparedShopRound == currentRoundIndex)
        {
            return;
        }

        if (preparedShopRound >= 0 && shopFrozen)
        {
            shopFrozen = false;
        }
        else
        {
            GenerateShopOffers();
        }

        preparedShopRound = currentRoundIndex;
    }

    private void GenerateShopOffers()
    {
        for (int i = 0; i < shopOffers.Length; i++)
        {
            shopOffers[i] = CreateRandomCharacter(RollShopTier());
        }

        modifierOffer = (CampaignUpgradeKind)random.Next(
            0,
            Enum.GetValues(typeof(CampaignUpgradeKind)).Length);
        modifierOfferPurchased = false;
    }

    private CharacterTier RollShopTier()
    {
        if (currentRoundIndex < 2)
        {
            return CharacterTier.C;
        }

        int roll = random.Next(0, 100);
        if (roll < 5)
        {
            return CharacterTier.A;
        }
        return roll < 35 ? CharacterTier.B : CharacterTier.C;
    }

    private void BuyCharacter(int index)
    {
        CampaignCharacter offer = index >= 0 && index < shopOffers.Length
            ? shopOffers[index]
            : null;
        if (offer == null)
        {
            return;
        }
        if (roster.Count >= CampaignBalance.RosterCapacity)
        {
            feedback = "Roster is full.";
            return;
        }
        if (gold < offer.Cost)
        {
            feedback = "Not enough gold.";
            return;
        }

        gold -= offer.Cost;
        offer.deployed = GetDeployedCount() < CurrentRound.deploymentLimit;
        roster.Add(offer);
        shopOffers[index] = null;
        feedback = "Purchased " + offer.DisplayName +
                   (offer.deployed ? " and deployed it." : " to the bench.");
    }

    private void BuyModifier()
    {
        if (modifierOfferPurchased)
        {
            return;
        }
        if (gold < CampaignBalance.UpgradeCost)
        {
            feedback = "Not enough gold.";
            return;
        }

        gold -= CampaignBalance.UpgradeCost;
        if (CampaignBalance.IsPermanent(modifierOffer))
        {
            permanentUpgrades.Add(modifierOffer);
        }
        else
        {
            pendingTemporaryUpgrades.Add(modifierOffer);
        }
        modifierOfferPurchased = true;
        feedback = "Purchased " + CampaignBalance.GetUpgradeName(modifierOffer) + ".";
    }

    private void RerollShop()
    {
        if (gold < CampaignBalance.RerollCost)
        {
            feedback = "Not enough gold to reroll.";
            return;
        }

        gold -= CampaignBalance.RerollCost;
        shopFrozen = false;
        GenerateShopOffers();
        feedback = "Shop rerolled.";
    }

    private void ToggleDeployment(CampaignCharacter character)
    {
        if (character.deployed)
        {
            character.deployed = false;
            return;
        }

        if (GetDeployedCount() >= CurrentRound.deploymentLimit)
        {
            feedback = $"This round allows {CurrentRound.deploymentLimit} deployed unit(s).";
            return;
        }

        character.deployed = true;
    }

    private int GetDeployedCount()
    {
        return roster.Count(character => character.deployed);
    }

    private void CommitDeploymentAndOpenTactics()
    {
        List<CampaignCharacter> deployed = roster.Where(character => character.deployed).ToList();
        if (deployed.Count == 0 || deployed.Count > CurrentRound.deploymentLimit)
        {
            feedback = $"Deploy between 1 and {CurrentRound.deploymentLimit} unit(s).";
            return;
        }

        List<AgentStats> playerSlots = FindObjectsByType<AgentStats>(FindObjectsInactive.Include)
            .Where(agent => agent.team == TeamType.Red &&
                            agent.GetComponent<AgentController3D>() != null)
            .OrderBy(agent => agent.name, StringComparer.Ordinal)
            .ToList();
        if (deployed.Count > playerSlots.Count)
        {
            feedback = "The map does not contain enough player deployment slots.";
            return;
        }

        float healthBonus = 1f + 0.05f * permanentUpgrades.Count(
            upgrade => upgrade == CampaignUpgradeKind.SquadVitality);
        float damageBonus = 1f + 0.05f * permanentUpgrades.Count(
            upgrade => upgrade == CampaignUpgradeKind.SharpenedWeapons);

        for (int i = 0; i < playerSlots.Count; i++)
        {
            bool used = i < deployed.Count;
            playerSlots[i].gameObject.SetActive(false);
            if (!used)
            {
                continue;
            }

            CampaignCharacter character = deployed[i];
            ConfigureAgent(
                playerSlots[i],
                character.role,
                character.weapon,
                character.tier,
                healthBonus,
                damageBonus,
                true,
                character.id);
            playerSlots[i].name =
                $"Red_{character.tier}_{character.role}_{character.weapon}_{character.id}";
            playerSlots[i].gameObject.SetActive(true);
            AgentRoleAbilities abilities =
                playerSlots[i].GetComponent<AgentRoleAbilities>();
            if (abilities != null)
            {
                abilities.RefreshCampaignRoundBinding();
            }
            HoldAgentForPreparation(playerSlots[i]);
        }

        PlayerAbilityCommandController.RefreshForCampaignRound();

        deploymentCommitted = true;
        screen = CampaignScreen.None;
        ObjectiveManager.Instance?.RefreshStartingCarrier();

        if (TeamTacticManager.Instance == null)
        {
            new GameObject("Team Tactic System").AddComponent<TeamTacticManager>();
        }
    }

    private static void HoldAgentForPreparation(AgentStats agent)
    {
        AgentBrain brain = agent.GetComponent<AgentBrain>();
        if (brain != null)
        {
            brain.enabled = false;
        }

        AgentMotor motor = agent.GetComponent<AgentMotor>();
        if (motor != null)
        {
            motor.Stop();
            motor.enabled = false;
        }

        WeaponSystem weapon = agent.GetComponent<WeaponSystem>();
        if (weapon != null)
        {
            weapon.enabled = false;
        }
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Active)
        {
            int generation = sceneGeneration;
            bool haste = pendingTemporaryUpgrades.Remove(CampaignUpgradeKind.OpeningHaste);
            bool slow = pendingTemporaryUpgrades.Remove(CampaignUpgradeKind.OpeningSlow);
            if (haste || slow)
            {
                StartCoroutine(ApplyOpeningMovementModifier(generation, haste, slow));
            }
        }
    }

    private IEnumerator ApplyOpeningMovementModifier(int generation, bool haste, bool slow)
    {
        ApplyTeamSpeed(TeamType.Red, haste ? 1.2f : 1f);
        ApplyTeamSpeed(TeamType.Blue, slow ? 0.8f : 1f);
        yield return new WaitForSeconds(10f);
        if (generation != sceneGeneration)
        {
            yield break;
        }
        ApplyTeamSpeed(TeamType.Red, 1f);
        ApplyTeamSpeed(TeamType.Blue, 1f);
    }

    private static void ApplyTeamSpeed(TeamType team, float multiplier)
    {
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (agent.team == team)
            {
                AgentMotor motor = agent.GetComponent<AgentMotor>();
                if (motor != null)
                {
                    motor.SpeedMultiplier = multiplier;
                }
            }
        }
    }

    private void OnRoundResultDeclared(TeamType winner, RoundEndReason reason)
    {
        lastRoundWon = roundManager != null && winner == roundManager.attackingTeam;
        if (lastRoundWon)
        {
            screen = CampaignScreen.Result;
            return;
        }

        // A campaign defeat is final. Clear the run before presenting the menu so
        // Play can only begin a fresh starter draft, never resume the lost roster.
        ClearCampaignProgress();
        campaignStarted = false;
        showGameOverMessage = true;
        screen = CampaignScreen.MainMenu;
        RoundResultUI.Instance?.Hide();
        StartCoroutine(ReturnToMenuAfterLoss());
    }

    private IEnumerator ReturnToMenuAfterLoss()
    {
        // Finish dispatching the round-result event before replacing its scene.
        yield return null;
        if (!campaignStarted)
        {
            hasPreparedScene = false;
            LoadCampaignScene();
        }
    }

    private void ContinueAfterWin()
    {
        int income = CampaignBalance.GetRoundIncome(gold);
        gold += income;
        lastIncome = income;
        currentRoundIndex++;

        if (currentRoundIndex >= rounds.Length)
        {
            screen = CampaignScreen.Complete;
            return;
        }

        LoadCampaignScene();
    }

    private void StartNewCampaign()
    {
        ClearCampaignProgress();
        campaignStarted = true;
        showGameOverMessage = false;
        hasPreparedScene = false;
        LoadCampaignScene();
    }

    private void LoadCampaignScene()
    {
        if (!string.IsNullOrEmpty(campaignSceneName))
        {
            SceneManager.LoadScene(campaignSceneName);
        }
    }

    private void ResetCampaign()
    {
        StartNewCampaign();
    }

    private void ClearCampaignProgress()
    {
        roster.Clear();
        permanentUpgrades.Clear();
        pendingTemporaryUpgrades.Clear();
        Array.Clear(shopOffers, 0, shopOffers.Length);
        currentRoundIndex = 0;
        gold = 5;
        nextCharacterId = 1;
        preparedShopRound = -1;
        lastIncome = 0;
        shopFrozen = false;
        modifierOfferPurchased = false;
    }

    private void OnGUI()
    {
        if (!IsCampaignScene || screen == CampaignScreen.None)
        {
            return;
        }

        GUI.depth = -1000;
        EnsureCampaignStyles();

        if (screen == CampaignScreen.MainMenu)
        {
            DrawMainMenu();
            return;
        }

        float width = Mathf.Min(1080f, Screen.width - 40f);
        float height = Mathf.Min(760f, Screen.height - 40f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, 20f, width, height);

        GUIStyle previousBox = GUI.skin.box;
        GUIStyle previousButton = GUI.skin.button;
        GUIStyle previousToggle = GUI.skin.toggle;
        GUI.skin.box = campaignCardStyle;
        GUI.skin.button = campaignButtonStyle;
        GUI.skin.toggle = campaignToggleStyle;

        GUILayout.BeginArea(panel, campaignPanelStyle);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        if (screen == CampaignScreen.StarterDraft)
        {
            DrawStarterDraft();
        }
        else if (screen == CampaignScreen.Preparation)
        {
            DrawPreparation();
        }
        else if (screen == CampaignScreen.Result)
        {
            DrawResult();
        }
        else if (screen == CampaignScreen.Complete)
        {
            DrawComplete();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.skin.box = previousBox;
        GUI.skin.button = previousButton;
        GUI.skin.toggle = previousToggle;

        DrawCampaignBorder(panel, 3f);
        DrawCharacterTooltip(GUI.tooltip);
    }

    private void DrawStarterDraft()
    {
        GUILayout.Label("CHOOSE YOUR STARTER", TitleStyle());
        GUILayout.Label("Choose one C-tier character. Role, weapon and ability belong to the character.");
        GUILayout.Space(20f);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < starterOffers.Length; i++)
        {
            CampaignCharacter offer = starterOffers[i];
            GUILayout.BeginVertical(
                new GUIContent(string.Empty, GetCharacterTooltip(offer)),
                GUI.skin.box,
                GUILayout.MinHeight(250f));
            DrawCharacterCardHeader(offer);
            GUILayout.FlexibleSpace();
            int captured = i;
            if (GUILayout.Button("SELECT", GUILayout.Height(42f)))
            {
                ChooseStarter(captured);
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawMainMenu()
    {
        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            menuBackgroundTexture,
            ScaleMode.StretchToFill,
            false);

        const float referenceWidth = 1920f;
        const float referenceHeight = 1080f;
        float scale = Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight);
        float offsetX = (Screen.width - referenceWidth * scale) * 0.5f;
        float offsetY = (Screen.height - referenceHeight * scale) * 0.5f;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(
            new Vector3(offsetX, offsetY, 0f),
            Quaternion.identity,
            new Vector3(scale, scale, 1f));

        DrawMenuFrame();
        GUI.Label(new Rect(360f, 120f, 1200f, 120f), "CS AUTOBATTLER", menuTitleStyle);
        GUI.Label(
            new Rect(510f, 236f, 900f, 42f),
            "RECRUIT  •  DEPLOY  •  OUTMANEUVER",
            menuSubtitleStyle);

        if (showGameOverMessage)
        {
            GUIStyle gameOverStyle = new GUIStyle(menuSubtitleStyle)
            {
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.32f, 0.22f, 1f) }
            };
            GUI.Label(new Rect(610f, 305f, 700f, 54f), "GAME OVER — SQUAD DEFEATED", gameOverStyle);
        }

        if (showMenuHelp)
        {
            DrawMenuHelp();
        }
        else
        {
            if (DrawMenuButton(new Rect(660f, 405f, 600f, 125f), "▶   PLAY"))
            {
                StartNewCampaign();
            }

            if (DrawMenuButton(new Rect(725f, 560f, 470f, 94f), "◆   HOW TO PLAY"))
            {
                showMenuHelp = true;
            }
        }

        GUI.Label(new Rect(42f, 1018f, 500f, 32f), "PROTOTYPE CAMPAIGN  •  MAP 1", menuSmallStyle);
        GUIStyle rightFooter = new GUIStyle(menuSmallStyle)
        {
            alignment = TextAnchor.MiddleRight
        };
        GUI.Label(new Rect(1378f, 1018f, 500f, 32f), "3 ROUNDS  •  DEPLOYMENT LIMIT 5", rightFooter);
        GUI.matrix = previousMatrix;
    }

    private void DrawMenuFrame()
    {
        DrawSolidRect(new Rect(0f, 0f, 1920f, 18f), new Color(0.08f, 0.78f, 0.83f, 0.75f));
        DrawSolidRect(new Rect(0f, 1062f, 1920f, 18f), new Color(0.025f, 0.08f, 0.11f, 0.95f));
        DrawSolidRect(new Rect(238f, 285f, 1444f, 4f), new Color(0.2f, 0.86f, 0.96f, 0.4f));
        DrawSolidRect(new Rect(460f, 290f, 1000f, 2f), new Color(0.2f, 0.86f, 0.96f, 0.2f));
    }

    private bool DrawMenuButton(Rect rect, string label)
    {
        DrawSolidRect(
            new Rect(rect.x + 14f, rect.y + 16f, rect.width, rect.height),
            new Color(0.015f, 0.045f, 0.055f, 0.85f));
        DrawSolidRect(
            new Rect(rect.x - 7f, rect.y - 7f, rect.width + 14f, rect.height + 14f),
            new Color(0.16f, 0.78f, 0.82f, 0.95f));
        DrawSolidRect(rect, new Color(0.055f, 0.11f, 0.14f, 1f));
        return GUI.Button(rect, label, menuButtonStyle);
    }

    private void DrawMenuHelp()
    {
        Rect panel = new Rect(555f, 365f, 810f, 385f);
        DrawSolidRect(
            new Rect(panel.x + 12f, panel.y + 14f, panel.width, panel.height),
            new Color(0.01f, 0.035f, 0.045f, 0.85f));
        DrawSolidRect(
            new Rect(panel.x - 5f, panel.y - 5f, panel.width + 10f, panel.height + 10f),
            new Color(0.16f, 0.78f, 0.82f, 0.95f));
        DrawSolidRect(panel, new Color(0.045f, 0.075f, 0.095f, 0.98f));

        GUIStyle heading = new GUIStyle(menuSubtitleStyle)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold
        };
        GUIStyle body = new GUIStyle(menuSmallStyle)
        {
            fontSize = 22,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(590f, 395f, 740f, 50f), "HOW TO PLAY", heading);
        GUI.Label(
            new Rect(625f, 465f, 670f, 165f),
            "1. Choose one of three starting characters.\n" +
            "2. Buy recruits and upgrades in the shop.\n" +
            "3. Deploy up to five characters and choose an initial tactic.\n" +
            "4. Win all three rounds. A defeat ends the campaign.",
            body);
        if (DrawMenuButton(new Rect(770f, 650f, 380f, 72f), "◀   BACK"))
        {
            showMenuHelp = false;
        }
    }

    private static void DrawSolidRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private void DrawPreparation()
    {
        GUILayout.Label($"ROUND {CurrentRoundNumber} / {rounds.Length}: {CurrentRound.name}",
            TitleStyle());
        GUILayout.Label(CurrentRound.description);
        GUILayout.Label(
            $"Enemy tactic: {CurrentRound.enemyTactic}    |    " +
            $"Enemy buff: {CurrentRound.enemyBuffName} — {CurrentRound.enemyBuffDescription}");
        GUILayout.Space(8f);
        GUILayout.Label(
            $"GOLD: {gold}    |    NEXT CLEAR INCOME: " +
            $"{CampaignBalance.BaseIncome} + {CampaignBalance.GetInterest(gold)} interest    |    " +
            $"DEPLOYED: {GetDeployedCount()} / {CurrentRound.deploymentLimit}",
            HeadingStyle());

        GUILayout.Space(14f);
        GUILayout.Label("CHARACTER SHOP", HeadingStyle());
        GUILayout.BeginHorizontal();
        for (int i = 0; i < shopOffers.Length; i++)
        {
            CampaignCharacter offer = shopOffers[i];
            string tooltip = offer != null ? GetCharacterTooltip(offer) : string.Empty;
            GUILayout.BeginVertical(
                new GUIContent(string.Empty, tooltip),
                GUI.skin.box,
                GUILayout.MinHeight(235f));
            if (offer == null)
            {
                GUILayout.Label("SOLD", HeadingStyle());
            }
            else
            {
                DrawCharacterCardHeader(offer);
                int captured = i;
                if (GUILayout.Button($"BUY — {offer.Cost} GOLD", GUILayout.Height(34f)))
                {
                    BuyCharacter(captured);
                }
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"REROLL ({CampaignBalance.RerollCost} GOLD)", GUILayout.Height(34f)))
        {
            RerollShop();
        }
        bool newFrozen = GUILayout.Toggle(shopFrozen, " FREEZE SHOP FOR NEXT ROUND");
        shopFrozen = newFrozen;
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.Label("UPGRADE / TACTIC OFFER", HeadingStyle());
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label(CampaignBalance.GetUpgradeName(modifierOffer), HeadingStyle());
        GUILayout.Label(CampaignBalance.GetUpgradeDescription(modifierOffer));
        GUI.enabled = !modifierOfferPurchased;
        if (GUILayout.Button(
                modifierOfferPurchased
                    ? "PURCHASED"
                    : $"BUY — {CampaignBalance.UpgradeCost} GOLD",
                GUILayout.Height(34f)))
        {
            BuyModifier();
        }
        GUI.enabled = true;
        GUILayout.EndVertical();

        GUILayout.Space(12f);
        GUILayout.Label($"ROSTER ({roster.Count}/{CampaignBalance.RosterCapacity})", HeadingStyle());
        foreach (CampaignCharacter character in roster)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(character.DisplayName, GUILayout.Width(310f));
            GUILayout.Label(GetRoleAbilityDescription(character.role));
            bool selected = GUILayout.Toggle(
                character.deployed,
                character.deployed ? "DEPLOYED" : "BENCHED",
                GUILayout.Width(120f));
            if (selected != character.deployed)
            {
                ToggleDeployment(character);
            }
            GUILayout.EndHorizontal();
        }

        if (permanentUpgrades.Count > 0 || pendingTemporaryUpgrades.Count > 0)
        {
            GUILayout.Label(
                "OWNED EFFECTS: " + string.Join(", ",
                    permanentUpgrades.Concat(pendingTemporaryUpgrades)
                        .Select(CampaignBalance.GetUpgradeName)));
        }

        if (!string.IsNullOrEmpty(feedback))
        {
            GUILayout.Label(feedback, HeadingStyle());
        }

        GUILayout.Space(12f);
        if (GUILayout.Button("LOCK DEPLOYMENT AND CHOOSE INITIAL TEAM TACTIC",
                GUILayout.Height(48f)))
        {
            CommitDeploymentAndOpenTactics();
        }
    }

    private void DrawResult()
    {
        GUILayout.FlexibleSpace();
        GUILayout.Label(lastRoundWon ? "ROUND CLEARED" : "ROUND LOST", TitleStyle());
        GUILayout.Label(lastRoundWon
            ? $"Your saved {gold} gold will earn {CampaignBalance.GetInterest(gold)} interest."
            : "You can retry with the same roster and gold. Defeats do not grant farmable income.",
            HeadingStyle());
        GUILayout.Space(20f);
        if (lastRoundWon)
        {
            if (GUILayout.Button(
                    currentRoundIndex + 1 >= rounds.Length
                        ? "COMPLETE PROTOTYPE CAMPAIGN"
                        : "COLLECT INCOME AND CONTINUE",
                    GUILayout.Height(52f)))
            {
                ContinueAfterWin();
            }
        }
        GUILayout.FlexibleSpace();
    }

    private void DrawComplete()
    {
        GUILayout.FlexibleSpace();
        GUILayout.Label("THREE-ROUND PROTOTYPE COMPLETE", TitleStyle());
        GUILayout.Label(
            $"Final gold: {gold}  |  Last income: {lastIncome}  |  Roster: {roster.Count}",
            HeadingStyle());
        GUILayout.Space(18f);
        if (GUILayout.Button("START A NEW CAMPAIGN", GUILayout.Height(52f)))
        {
            ResetCampaign();
        }
        GUILayout.FlexibleSpace();
    }

    private static string GetRoleAbilityDescription(AgentRoleType role)
    {
        return role switch
        {
            AgentRoleType.Support => "Ability: heal an injured ally.",
            AgentRoleType.Flanker => "Ability: Shadow Blink into a better flank.",
            AgentRoleType.Assaulter => "Ability: install a combat turret.",
            AgentRoleType.Defender => "Ability: deploy a defensive wall.",
            _ => string.Empty
        };
    }

    private static GUIStyle TitleStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
    }

    private static GUIStyle HeadingStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 0.86f, 0.96f) }
        };
    }

    private void EnsureCampaignStyles()
    {
        if (campaignPanelStyle != null)
        {
            return;
        }

        campaignPanelTexture = CreateSolidTexture(
            "CampaignPanel",
            new Color(0.055f, 0.07f, 0.095f, 1f));
        campaignCardTexture = CreateSolidTexture(
            "CampaignCard",
            new Color(0.10f, 0.12f, 0.15f, 1f));
        campaignButtonTexture = CreateSolidTexture(
            "CampaignButton",
            new Color(0.10f, 0.12f, 0.15f, 1f));
        campaignButtonHoverTexture = CreateSolidTexture(
            "CampaignButtonHover",
            new Color(0.08f, 0.48f, 0.60f, 1f));
        campaignButtonPressedTexture = CreateSolidTexture(
            "CampaignButtonPressed",
            new Color(0.055f, 0.34f, 0.44f, 1f));
        campaignAccentTexture = CreateSolidTexture(
            "CampaignAccent",
            new Color(0.20f, 0.86f, 0.96f, 1f));
        menuBackgroundTexture = CreateMenuBackgroundTexture();

        campaignPanelStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = campaignPanelTexture },
            padding = new RectOffset(24, 24, 22, 22),
            border = new RectOffset(0, 0, 0, 0)
        };
        campaignCardStyle = new GUIStyle(GUI.skin.box)
        {
            normal =
            {
                background = campaignCardTexture,
                textColor = Color.white
            },
            padding = new RectOffset(14, 14, 12, 12),
            margin = new RectOffset(5, 5, 5, 5),
            border = new RectOffset(0, 0, 0, 0)
        };
        campaignButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(12, 12, 8, 8),
            border = new RectOffset(0, 0, 0, 0)
        };
        campaignButtonStyle.normal.background = campaignButtonTexture;
        campaignButtonStyle.normal.textColor = Color.white;
        campaignButtonStyle.hover.background = campaignButtonHoverTexture;
        campaignButtonStyle.hover.textColor = Color.white;
        campaignButtonStyle.active.background = campaignButtonPressedTexture;
        campaignButtonStyle.active.textColor = Color.white;
        campaignButtonStyle.focused.background = campaignButtonHoverTexture;
        campaignButtonStyle.focused.textColor = Color.white;

        campaignToggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            onNormal = { textColor = new Color(0.20f, 0.86f, 0.96f, 1f) },
            hover = { textColor = Color.white },
            onHover = { textColor = new Color(0.20f, 0.86f, 0.96f, 1f) }
        };

        menuTitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 86,
            fontStyle = FontStyle.BoldAndItalic,
            normal = { textColor = new Color(0.82f, 0.97f, 1f, 1f) }
        };
        menuSubtitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.20f, 0.86f, 0.96f, 1f) }
        };
        menuButtonStyle = new GUIStyle(campaignButtonStyle)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(0, 0, 0, 0)
        };
        menuButtonStyle.normal.background = campaignButtonTexture;
        menuButtonStyle.hover.background = campaignButtonHoverTexture;
        menuButtonStyle.active.background = campaignButtonPressedTexture;
        menuSmallStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.55f, 0.79f, 0.83f, 1f) }
        };
    }

    private void DrawCampaignBorder(Rect rect, float thickness)
    {
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), campaignAccentTexture);
        GUI.DrawTexture(
            new Rect(rect.x, rect.yMax - thickness, rect.width, thickness),
            campaignAccentTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), campaignAccentTexture);
        GUI.DrawTexture(
            new Rect(rect.xMax - thickness, rect.y, thickness, rect.height),
            campaignAccentTexture);
    }

    private void DrawCharacterCardHeader(CampaignCharacter character)
    {
        GUILayout.Label($"RANK {character.tier}", RankStyle());
        Rect portraitRect = GUILayoutUtility.GetRect(
            120f,
            120f,
            GUILayout.ExpandWidth(true));
        GUI.DrawTexture(
            portraitRect,
            GetCharacterPortrait(character),
            ScaleMode.ScaleToFit,
            true);
        GUILayout.Label(character.CharacterName.ToUpperInvariant(), CharacterNameStyle());
        GUILayout.Label(
            $"{character.role}  •  {character.weapon}",
            CharacterSubtitleStyle());
    }

    private Texture2D GetCharacterPortrait(CampaignCharacter character)
    {
        int key = ((int)character.role * 10) + (int)character.weapon;
        if (characterPortraitTextures.TryGetValue(key, out Texture2D portrait))
        {
            return portrait;
        }

        const int size = 128;
        portrait = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = $"CampaignPortrait_{character.role}_{character.weapon}",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color background = new Color(0.025f, 0.04f, 0.065f, 1f);
        Color roleColor = GetRoleColor(character.role);
        Color weaponColor = GetWeaponColor(character.weapon);
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool border = x < 3 || y < 3 || x >= size - 3 || y >= size - 3;
                Color color = border ? roleColor : Color.Lerp(background, roleColor, y / 512f);

                float headDistance = Vector2.Distance(new Vector2(x, y), new Vector2(64f, 82f));
                bool head = headDistance <= 18f;
                float bodyHalfWidth = Mathf.Lerp(36f, 22f, Mathf.InverseLerp(22f, 68f, y));
                bool body = y >= 18 && y <= 68 && Mathf.Abs(x - 64f) <= bodyHalfWidth;
                bool shoulder = y >= 50 && y <= 62 && Mathf.Abs(x - 64f) <= 46f;
                if (head || body || shoulder)
                {
                    color = new Color(0.78f, 0.86f, 0.90f, 1f);
                }

                if (y >= 8 && y <= 14 && x >= 20 && x <= 108)
                {
                    color = weaponColor;
                }
                pixels[y * size + x] = color;
            }
        }
        portrait.SetPixels32(pixels);
        portrait.Apply(false, true);
        characterPortraitTextures[key] = portrait;
        return portrait;
    }

    private static Color GetRoleColor(AgentRoleType role)
    {
        return role switch
        {
            AgentRoleType.Support => new Color(0.15f, 0.75f, 0.48f, 1f),
            AgentRoleType.Flanker => new Color(0.66f, 0.28f, 0.88f, 1f),
            AgentRoleType.Assaulter => new Color(0.94f, 0.30f, 0.20f, 1f),
            AgentRoleType.Defender => new Color(0.16f, 0.55f, 0.94f, 1f),
            _ => new Color(0.20f, 0.86f, 0.96f, 1f)
        };
    }

    private static Color GetWeaponColor(WeaponType weapon)
    {
        return weapon switch
        {
            WeaponType.Rifle => new Color(0.20f, 0.86f, 0.96f, 1f),
            WeaponType.SMG => new Color(1f, 0.72f, 0.22f, 1f),
            WeaponType.Sniper => new Color(0.46f, 0.72f, 1f, 1f),
            WeaponType.Shotgun => new Color(1f, 0.38f, 0.22f, 1f),
            _ => Color.white
        };
    }

    private static string GetCharacterTooltip(CampaignCharacter character)
    {
        WeaponDefinition weapon = WeaponDefaults.Get(character.weapon);
        float health = weapon.agentHealth * CampaignBalance.GetHealthMultiplier(character.tier);
        float damage = weapon.damage * CampaignBalance.GetDamageMultiplier(character.tier);
        float accuracy = Mathf.Clamp(
            weapon.accuracy + CampaignBalance.GetAccuracyBonus(character.tier),
            0f,
            100f);
        float movement = weapon.movementSpeedMultiplier *
                         CampaignBalance.GetMovementMultiplier(character.tier);
        return $"{character.DisplayName}\n\n" +
               $"HEALTH  {health:0.#}\n" +
               $"DAMAGE  {damage:0.#}" +
               (weapon.projectilesPerShot > 1 ? $" × {weapon.projectilesPerShot}" : string.Empty) +
               $"\nACCURACY  {accuracy:0.#}%\n" +
               $"RANGE  {weapon.effectiveMinimumRange:0.#}–{weapon.effectiveMaximumRange:0.#}\n" +
               $"MOVE MULTIPLIER  {movement:0.##}×\n" +
               $"MAGAZINE  {weapon.magazineSize}  •  RELOAD  {weapon.reloadTime:0.#}s\n\n" +
               GetRoleAbilityDescription(character.role) + "\n" +
               GetTierAbilityDescription(character.tier);
    }

    private static string GetTierAbilityDescription(CharacterTier tier)
    {
        return tier switch
        {
            CharacterTier.B => "B-tier ability: +15% power and 10% shorter cooldown.",
            CharacterTier.A => "A-tier ability: +30% power and 20% shorter cooldown.",
            _ => "C-tier ability: standard power and cooldown."
        };
    }

    private void DrawCharacterTooltip(string tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            return;
        }

        GUIStyle style = new GUIStyle(campaignCardStyle)
        {
            fontSize = 14,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
            normal =
            {
                background = campaignCardTexture,
                textColor = Color.white
            }
        };
        const float width = 390f;
        float height = style.CalcHeight(new GUIContent(tooltip), width) + 16f;
        Vector2 mouse = Event.current.mousePosition;
        float x = Mathf.Min(mouse.x + 18f, Screen.width - width - 12f);
        float y = Mathf.Min(mouse.y + 18f, Screen.height - height - 12f);
        Rect tooltipRect = new Rect(Mathf.Max(12f, x), Mathf.Max(12f, y), width, height);
        GUI.Box(tooltipRect, tooltip, style);
        DrawCampaignBorder(tooltipRect, 2f);
    }

    private static GUIStyle RankStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.20f, 0.86f, 0.96f, 1f) }
        };
    }

    private static GUIStyle CharacterNameStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
    }

    private static GUIStyle CharacterSubtitleStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.68f, 0.74f, 0.80f, 1f) }
        };
    }

    private static Texture2D CreateSolidTexture(string textureName, Color color)
    {
        Texture2D texture = new Texture2D(1, 1)
        {
            name = textureName,
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateMenuBackgroundTexture()
    {
        const int width = 640;
        const int height = 360;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "CampaignMenuBackground",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color32[] pixels = new Color32[width * height];
        Color skyTop = new Color(0.015f, 0.20f, 0.25f, 1f);
        Color skyBottom = new Color(0.02f, 0.34f, 0.38f, 1f);
        Color farRange = new Color(0.025f, 0.25f, 0.29f, 1f);
        Color middleRange = new Color(0.02f, 0.17f, 0.21f, 1f);
        Color nearRange = new Color(0.012f, 0.105f, 0.14f, 1f);

        for (int y = 0; y < height; y++)
        {
            float ny = y / (height - 1f);
            for (int x = 0; x < width; x++)
            {
                float nx = x / (width - 1f);
                Color color = Color.Lerp(skyBottom, skyTop, ny);
                float farHeight = 0.24f + 0.055f * Mathf.Sin(nx * 24f) +
                                  0.035f * Mathf.Sin(nx * 53f + 1.2f);
                float middleHeight = 0.16f + 0.06f * Mathf.Sin(nx * 18f + 0.6f) +
                                     0.025f * Mathf.Sin(nx * 41f);
                float nearHeight = 0.08f + 0.045f * Mathf.Sin(nx * 13f + 2.3f) +
                                   0.022f * Mathf.Sin(nx * 36f + 0.4f);
                if (ny < farHeight) color = farRange;
                if (ny < middleHeight) color = middleRange;
                if (ny < nearHeight) color = nearRange;

                int hash = unchecked(x * 73856093 ^ y * 19349663);
                if (ny > 0.34f && (hash & 2047) == 17)
                {
                    color = (hash & 4096) == 0
                        ? new Color(0.45f, 0.92f, 1f, 1f)
                        : new Color(0.15f, 0.68f, 0.76f, 1f);
                }
                pixels[y * width + x] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void UnbindRoundManager()
    {
        if (roundManager != null)
        {
            roundManager.RoundResultDeclared -= OnRoundResultDeclared;
            roundManager.StateChanged -= OnRoundStateChanged;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindRoundManager();
        if (Instance == this)
        {
            Instance = null;
        }

        DestroyCampaignTextures();
    }

    private void DestroyCampaignTextures()
    {
        Texture2D[] textures =
        {
            campaignPanelTexture,
            campaignCardTexture,
            campaignButtonTexture,
            campaignButtonHoverTexture,
            campaignButtonPressedTexture,
            campaignAccentTexture,
            menuBackgroundTexture
        };
        foreach (Texture2D texture in textures)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
        foreach (Texture2D portrait in characterPortraitTextures.Values)
        {
            if (portrait != null)
            {
                Destroy(portrait);
            }
        }
        characterPortraitTextures.Clear();
    }
}
