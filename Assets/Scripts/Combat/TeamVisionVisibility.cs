using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hides enemy presentation from the player until any living controlled-team
/// agent detects that enemy. Gameplay objects remain active so combat and AI
/// continue to use the same authoritative world state.
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class TeamVisionVisibility : MonoBehaviour
{
    public static TeamVisionVisibility Instance { get; private set; }

    private readonly List<AgentSensors> friendlySensors = new List<AgentSensors>();
    private readonly Dictionary<AgentStats, bool> visibleEnemies =
        new Dictionary<AgentStats, bool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<TeamVisionVisibility>() == null)
        {
            new GameObject("Team Vision Visibility").AddComponent<TeamVisionVisibility>();
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
    }

    private void LateUpdate()
    {
        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void RefreshVisibility()
    {
        bool revealAllEnemies = CampaignManager.Instance != null &&
                                CampaignManager.Instance.IsCampaignScene;
        TeamType controlledTeam = TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.ControlledTeam
            : TeamType.Red;
        Scene activeScene = SceneManager.GetActiveScene();
        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Include);

        friendlySensors.Clear();
        foreach (AgentStats agent in agents)
        {
            if (!IsActiveLivingSceneAgent(agent, activeScene) || agent.team != controlledTeam)
            {
                continue;
            }

            AgentSensors sensors = agent.GetComponent<AgentSensors>();
            if (sensors != null && sensors.isActiveAndEnabled)
            {
                friendlySensors.Add(sensors);
            }
        }

        foreach (AgentStats agent in agents)
        {
            if (agent == null || agent.gameObject.scene != activeScene)
            {
                continue;
            }

            bool friendly = agent.team == controlledTeam;
            bool visible = friendly || revealAllEnemies ||
                           IsVisibleToTeam(agent, controlledTeam, friendlySensors);
            SetAgentPresentationVisible(agent, visible);
            if (!friendly)
            {
                visibleEnemies[agent] = visible;
            }
        }
    }

    public static bool IsVisibleToPlayer(AgentStats agent)
    {
        if (agent == null)
        {
            return false;
        }

        TeamType controlledTeam = TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.ControlledTeam
            : TeamType.Red;
        if (agent.team == controlledTeam)
        {
            return true;
        }

        if (CampaignManager.Instance != null &&
            CampaignManager.Instance.IsCampaignScene)
        {
            return true;
        }

        return Instance != null &&
               Instance.visibleEnemies.TryGetValue(agent, out bool visible) &&
               visible;
    }

    public static bool IsVisibleToTeam(
        AgentStats enemy,
        TeamType observingTeam,
        IReadOnlyList<AgentSensors> observers)
    {
        if (enemy == null || enemy.team == observingTeam || observers == null)
        {
            return false;
        }

        HealthSystem enemyHealth = enemy.GetComponent<HealthSystem>();
        if (enemyHealth == null || enemyHealth.IsDead || !enemy.gameObject.activeInHierarchy)
        {
            return false;
        }

        for (int i = 0; i < observers.Count; i++)
        {
            AgentSensors observer = observers[i];
            if (observer == null || !observer.isActiveAndEnabled)
            {
                continue;
            }

            AgentStats observerStats = observer.GetComponent<AgentStats>();
            HealthSystem observerHealth = observer.GetComponent<HealthSystem>();
            if (observerStats == null || observerStats.team != observingTeam ||
                observerHealth == null || observerHealth.IsDead)
            {
                continue;
            }

            if (observer.CanDetect(enemy.gameObject))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActiveLivingSceneAgent(AgentStats agent, Scene scene)
    {
        if (agent == null || agent.gameObject.scene != scene ||
            !agent.gameObject.activeInHierarchy)
        {
            return false;
        }

        HealthSystem health = agent.GetComponent<HealthSystem>();
        return health != null && !health.IsDead;
    }

    private static void SetAgentPresentationVisible(AgentStats agent, bool visible)
    {
        Renderer[] renderers = agent.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.forceRenderingOff = !visible;
            }
        }
    }
}
