using System.Collections.Generic;
using UnityEngine;

public enum InfluenceLayerType
{
    Friendly,
    Enemy,
    Danger,
    FriendlyOccupancy,
    EnemyLineOfFire,
    Objective,
    Cover,
    LastKnownEnemy,
    Control,
    Front
}

[DefaultExecutionOrder(-80)]
public sealed class InfluenceMapManager : MonoBehaviour
{
    public static InfluenceMapManager Instance { get; private set; }

    [Min(1f)] public float cellSize = 2f;
    [Min(0.05f)] public float updateInterval = 0.22f;
    [Min(0.01f)] public float influenceDecay = 0.18f;
    [Min(5f)] public float influenceRadius = 24f;
    public Vector2 fallbackMapSize = new Vector2(80f, 80f);

    private readonly Dictionary<InfluenceLayerType, InfluenceMapLayer> layers =
        new Dictionary<InfluenceLayerType, InfluenceMapLayer>();
    private Bounds bounds;
    private int width;
    private int height;
    private float nextUpdate;
    private TeamType perspectiveTeam = TeamType.Red;

    public Bounds MapBounds => bounds;
    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<InfluenceMapManager>() == null)
            new GameObject("Influence Map Manager").AddComponent<InfluenceMapManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildGrid();
        if (GetComponent<InfluenceMapDebugRenderer>() == null)
            gameObject.AddComponent<InfluenceMapDebugRenderer>();
    }

    private void Update()
    {
        if (Time.time < nextUpdate) return;
        nextUpdate = Time.time + updateInterval;
        perspectiveTeam = TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.ControlledTeam : TeamType.Red;
        Recalculate(perspectiveTeam);
    }

    public void Recalculate(TeamType friendlyTeam)
    {
        perspectiveTeam = friendlyTeam;
        ClearLayers();
        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        foreach (AgentStats agent in agents)
        {
            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (health == null || health.IsDead) continue;
            bool friendly = agent.team == friendlyTeam;
            AddRadial(friendly ? InfluenceLayerType.Friendly : InfluenceLayerType.Enemy,
                agent.transform.position,
                Mathf.Max(1f, WeaponLoadout.Get(agent.gameObject).Damage * 0.1f),
                influenceRadius);
            if (friendly)
                AddRadial(InfluenceLayerType.FriendlyOccupancy, agent.transform.position, 1f, cellSize * 2f);
            else
            {
                AgentSensors sensors = agent.GetComponent<AgentSensors>();
                AddLineOfFire(agent.transform.position, agent.transform.forward,
                    sensors != null ? sensors.sightRange : 20f);
            }
        }

        AddObjectives();
        AddCover();
        AddRememberedEnemies(friendlyTeam, agents);
        InfluenceMapLayer friendlyLayer = layers[InfluenceLayerType.Friendly];
        InfluenceMapLayer enemyLayer = layers[InfluenceLayerType.Enemy];
        for (int i = 0; i < width * height; i++)
        {
            layers[InfluenceLayerType.Control][i] = friendlyLayer[i] - enemyLayer[i];
            layers[InfluenceLayerType.Danger][i] = enemyLayer[i] + layers[InfluenceLayerType.EnemyLineOfFire][i];
            float total = friendlyLayer[i] + enemyLayer[i];
            layers[InfluenceLayerType.Front][i] = total <= 0.05f ? 0f :
                Mathf.Clamp01(1f - Mathf.Abs(friendlyLayer[i] - enemyLayer[i]) / total);
        }
    }

    public float Sample(InfluenceLayerType layer, Vector3 worldPosition)
    {
        return TryWorldToIndex(worldPosition, out int index) ? layers[layer][index] : 0f;
    }

    public Vector3 GetCellCenter(int x, int y)
    {
        return new Vector3(bounds.min.x + (x + 0.5f) * cellSize, bounds.center.y,
            bounds.min.z + (y + 0.5f) * cellSize);
    }

    public bool TryWorldToCell(Vector3 position, out int x, out int y)
    {
        x = Mathf.FloorToInt((position.x - bounds.min.x) / cellSize);
        y = Mathf.FloorToInt((position.z - bounds.min.z) / cellSize);
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    public TeamType PerspectiveTeam => perspectiveTeam;

    private void BuildGrid()
    {
        AgentStats[] agents = FindObjectsByType<AgentStats>(FindObjectsInactive.Include);
        if (agents.Length > 0)
        {
            bounds = new Bounds(agents[0].transform.position, Vector3.zero);
            foreach (AgentStats agent in agents) bounds.Encapsulate(agent.transform.position);
            bounds.Expand(new Vector3(40f, 2f, 40f));
        }
        else bounds = new Bounds(Vector3.zero, new Vector3(fallbackMapSize.x, 2f, fallbackMapSize.y));
        width = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize));
        height = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize));
        foreach (InfluenceLayerType type in System.Enum.GetValues(typeof(InfluenceLayerType)))
            layers[type] = new InfluenceMapLayer(type.ToString(), width * height);
    }

    private void ClearLayers()
    {
        int count = width * height;
        foreach (InfluenceMapLayer layer in layers.Values) layer.Resize(count);
    }

    private bool TryWorldToIndex(Vector3 p, out int index)
    {
        if (TryWorldToCell(p, out int x, out int y)) { index = y * width + x; return true; }
        index = -1; return false;
    }

    private void AddRadial(InfluenceLayerType type, Vector3 source, float strength, float radius)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((source.x - radius - bounds.min.x) / cellSize));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt((source.x + radius - bounds.min.x) / cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((source.z - radius - bounds.min.z) / cellSize));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt((source.z + radius - bounds.min.z) / cellSize));
        for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
        {
            float distance = FlatDistance(source, GetCellCenter(x, y));
            if (distance <= radius) layers[type][y * width + x] += strength * Mathf.Exp(-influenceDecay * distance);
        }
    }

    private void AddLineOfFire(Vector3 origin, Vector3 forward, float range)
    {
        forward.y = 0f; if (forward.sqrMagnitude < 0.01f) return;
        for (float d = cellSize; d <= range; d += cellSize)
        {
            Vector3 p = origin + forward.normalized * d;
            if (!TryWorldToIndex(p, out int index)) break;
            layers[InfluenceLayerType.EnemyLineOfFire][index] += Mathf.Exp(-0.05f * d);
        }
    }

    private void AddObjectives()
    {
        ObjectiveManager objective = ObjectiveManager.Instance;
        if (objective == null) return;
        if (objective.ActiveBomb != null)
            AddRadial(InfluenceLayerType.Objective, objective.ActiveBomb.transform.position, 2f, 18f);
        if (objective.siteA != null) AddRadial(InfluenceLayerType.Objective, objective.siteA.PlantPosition, 1f, 18f);
        if (objective.siteB != null) AddRadial(InfluenceLayerType.Objective, objective.siteB.PlantPosition, 1f, 18f);
    }

    private void AddCover()
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
        foreach (Transform t in transforms)
            if (t.name.IndexOf("Cover", System.StringComparison.OrdinalIgnoreCase) >= 0)
                AddRadial(InfluenceLayerType.Cover, t.position, 1f, cellSize * 2.5f);
    }

    private void AddRememberedEnemies(TeamType team, AgentStats[] agents)
    {
        foreach (AgentStats agent in agents)
        {
            if (agent.team != team) continue;
            AgentMemory memory = agent.GetComponent<AgentMemory>();
            if (memory != null && memory.HasKnownEnemyPosition)
                AddRadial(InfluenceLayerType.LastKnownEnemy, memory.LastKnownEnemyPosition, 1f, 12f);
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
}
