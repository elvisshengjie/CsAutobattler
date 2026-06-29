using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Press V during Play Mode to show or hide every 3D agent's field-of-view cone.
/// </summary>
public class AgentVisionDebug3D : MonoBehaviour
{
    private const int ConeArcSegments = 36;

    public KeyCode legacyToggleKey = KeyCode.V;
    public bool visibleAtStart;
    public float refreshInterval = 0.5f;
    public float lineWidth = 0.06f;

    private readonly Dictionary<AgentController3D, LineRenderer> rings =
        new Dictionary<AgentController3D, LineRenderer>();
    private readonly Dictionary<AgentController3D, LineRenderer> proximityRings =
        new Dictionary<AgentController3D, LineRenderer>();

    private bool ringsVisible;
    private float nextRefreshTime;
    private static Material ringMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<AgentVisionDebug3D>() == null)
        {
            new GameObject("AgentVisionDebug3D").AddComponent<AgentVisionDebug3D>();
        }
    }

    private void Awake()
    {
        ringsVisible = visibleAtStart;
    }

    private void Update()
    {
        if (TogglePressed())
        {
            ringsVisible = !ringsVisible;
            RefreshAgents();
            Debug.Log("Agent vision display: " + (ringsVisible ? "ON" : "OFF"));
        }

        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + refreshInterval;
            RefreshAgents();
        }

        UpdateRings();
    }

    private bool TogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(legacyToggleKey);
#else
        return false;
#endif
    }

    private void RefreshAgents()
    {
        AgentController3D[] agents =
            FindObjectsByType<AgentController3D>(FindObjectsInactive.Exclude);

        HashSet<AgentController3D> activeAgents = new HashSet<AgentController3D>(agents);
        List<AgentController3D> removedAgents = new List<AgentController3D>();

        foreach (KeyValuePair<AgentController3D, LineRenderer> entry in rings)
        {
            if (entry.Key == null || !activeAgents.Contains(entry.Key))
            {
                if (entry.Value != null)
                {
                    Destroy(entry.Value.gameObject);
                }

                if (entry.Key != null &&
                    proximityRings.TryGetValue(entry.Key, out LineRenderer proximityRing) &&
                    proximityRing != null)
                {
                    Destroy(proximityRing.gameObject);
                }

                removedAgents.Add(entry.Key);
            }
        }

        foreach (AgentController3D removedAgent in removedAgents)
        {
            rings.Remove(removedAgent);
            proximityRings.Remove(removedAgent);
        }

        foreach (AgentController3D agent in agents)
        {
            if (!rings.ContainsKey(agent))
            {
                rings.Add(agent, CreateCone(agent));
                proximityRings.Add(agent, CreateProximityRing(agent));
            }

            rings[agent].enabled = ringsVisible;
            proximityRings[agent].enabled = ringsVisible;
        }
    }

    private void UpdateRings()
    {
        foreach (KeyValuePair<AgentController3D, LineRenderer> entry in rings)
        {
            AgentController3D agent = entry.Key;
            LineRenderer ring = entry.Value;

            if (agent == null || ring == null)
            {
                continue;
            }

            AgentSensors sensors = agent.GetComponent<AgentSensors>();
            float sightRange = sensors != null ? sensors.sightRange : agent.sightRange;
            float fieldOfViewAngle = sensors != null
                ? sensors.fieldOfViewAngle
                : agent.fieldOfViewAngle;
            float radius = Mathf.Max(0f, sightRange);
            float halfAngle = Mathf.Clamp(fieldOfViewAngle, 1f, 360f) * 0.5f;

            ring.SetPosition(0, new Vector3(0f, 0.05f, 0f));

            for (int i = 0; i <= ConeArcSegments; i++)
            {
                float angleDegrees = Mathf.Lerp(-halfAngle, halfAngle, i / (float)ConeArcSegments);
                float angle = angleDegrees * Mathf.Deg2Rad;
                Vector3 localDirection = new Vector3(
                    Mathf.Sin(angle),
                    0f,
                    Mathf.Cos(angle));
                float visibleDistance = GetVisibleDistance(agent, sensors, localDirection, radius);

                ring.SetPosition(i + 1, new Vector3(
                    localDirection.x * visibleDistance,
                    0.05f,
                    localDirection.z * visibleDistance));
            }

            ring.SetPosition(ConeArcSegments + 2, new Vector3(0f, 0.05f, 0f));

            if (proximityRings.TryGetValue(agent, out LineRenderer proximityRing))
            {
                float proximityRange = sensors != null
                    ? sensors.proximityDetectionRange
                    : agent.proximityDetectionRange;
                float proximityRadius = Mathf.Max(0f, proximityRange);
                for (int i = 0; i <= ConeArcSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / ConeArcSegments;
                    proximityRing.SetPosition(i, new Vector3(
                        Mathf.Cos(angle) * proximityRadius,
                        0.06f,
                        Mathf.Sin(angle) * proximityRadius));
                }
            }
        }
    }

    private float GetVisibleDistance(
        AgentController3D agent,
        AgentSensors sensors,
        Vector3 localDirection,
        float maximumDistance)
    {
        float eyeHeight = sensors != null ? sensors.eyeHeight : agent.eyeHeight;
        LayerMask lineOfSightMask = sensors != null
            ? sensors.lineOfSightMask
            : agent.lineOfSightMask;
        Vector3 origin = agent.transform.position + Vector3.up * eyeHeight;
        Vector3 worldDirection = agent.transform.TransformDirection(localDirection).normalized;

        if (Physics.Raycast(
                origin,
                worldDirection,
                out RaycastHit hit,
                maximumDistance,
                lineOfSightMask,
                QueryTriggerInteraction.Ignore))
        {
            return Mathf.Max(0f, hit.distance - 0.03f);
        }

        return maximumDistance;
    }

    private LineRenderer CreateCone(AgentController3D agent)
    {
        GameObject ringObject = new GameObject("VisionRange");
        ringObject.transform.SetParent(agent.transform, false);

        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = false;
        ring.positionCount = ConeArcSegments + 3;
        ring.startWidth = lineWidth;
        ring.endWidth = lineWidth;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sharedMaterial = GetRingMaterial();

        AgentStats stats = agent.GetComponent<AgentStats>();
        Color teamColor = stats != null && stats.team == TeamType.Red
            ? new Color(1f, 0.15f, 0.15f, 0.8f)
            : new Color(0.15f, 0.5f, 1f, 0.8f);
        ring.startColor = teamColor;
        ring.endColor = teamColor;
        ring.enabled = ringsVisible;
        return ring;
    }

    private LineRenderer CreateProximityRing(AgentController3D agent)
    {
        GameObject ringObject = new GameObject("ProximityDetectionRange");
        ringObject.transform.SetParent(agent.transform, false);

        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = ConeArcSegments + 1;
        ring.startWidth = lineWidth * 1.25f;
        ring.endWidth = lineWidth * 1.25f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sharedMaterial = GetRingMaterial();
        ring.startColor = new Color(1f, 0.85f, 0.1f, 0.9f);
        ring.endColor = ring.startColor;
        ring.enabled = ringsVisible;
        return ring;
    }

    private static Material GetRingMaterial()
    {
        if (ringMaterial != null)
        {
            return ringMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        ringMaterial = new Material(shader);
        return ringMaterial;
    }
}
