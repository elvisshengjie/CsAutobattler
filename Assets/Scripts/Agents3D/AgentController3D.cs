using UnityEngine;

/// <summary>
/// Backward-compatible facade for existing scenes and prefabs.
/// Runtime behavior now lives in AgentBrain, AgentSensors, AgentMemory, and AgentMotor.
/// </summary>
public class AgentController3D : MonoBehaviour
{
    [Header("Pathfinding")]
    [HideInInspector]
    public float pathRefreshTime = 0.3f;
    [HideInInspector]
    public float waypointReachDistance = 0.3f;
    [HideInInspector]
    public float rotationSpeed = 720f;

    [Header("Agent Avoidance")]
    [HideInInspector]
    public float separationRadius = 0.8f;
    [HideInInspector]
    public float separationStrength = 2f;

    [Header("Vision and Memory")]
    [HideInInspector]
    public float sightRange = 60f;

    [HideInInspector]
    [Range(1f, 360f)]
    public float fieldOfViewAngle = 90f;

    [HideInInspector]
    [Tooltip("Short 360-degree awareness range for nearby enemies.")]
    public float proximityDetectionRange = 7f;
    [HideInInspector]
    public float eyeHeight = 0.8f;
    [HideInInspector]
    public float memoryDuration = 4f;
    [HideInInspector]
    public LayerMask lineOfSightMask = ~0;

    private AgentStats stats;
    private AgentSensors sensors;
    private AgentMemory memory;
    private AgentMotor motor;
    private AgentBrain brain;

    public Vector3 LastKnownEnemyPosition =>
        memory != null ? memory.LastKnownEnemyPosition : Vector3.zero;
    public bool HasLastKnownEnemyPosition =>
        memory != null && memory.HasKnownEnemyPosition;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        EnsureBehaviorComponents();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (brain != null)
        {
            brain.enabled = true;
        }

        if (motor != null)
        {
            motor.enabled = true;
        }
    }

    private void OnDisable()
    {
        if (brain != null)
        {
            brain.enabled = false;
        }

        if (motor != null)
        {
            motor.Stop();
            motor.enabled = false;
        }
    }

    public void NotifyAttackedBy(GameObject attacker)
    {
        if (memory == null)
        {
            EnsureBehaviorComponents();
        }

        if (memory != null && stats != null)
        {
            memory.RememberAttacker(attacker, stats.team);
        }
    }

    private void EnsureBehaviorComponents()
    {
        sensors = GetComponent<AgentSensors>();
        if (sensors == null)
        {
            sensors = gameObject.AddComponent<AgentSensors>();
            sensors.sightRange = sightRange;
            sensors.fieldOfViewAngle = fieldOfViewAngle;
            sensors.proximityDetectionRange = proximityDetectionRange;
            sensors.eyeHeight = eyeHeight;
            sensors.lineOfSightMask = lineOfSightMask;
        }

        memory = GetComponent<AgentMemory>();
        if (memory == null)
        {
            memory = gameObject.AddComponent<AgentMemory>();
            memory.memoryDuration = memoryDuration;
        }

        motor = GetComponent<AgentMotor>();
        if (motor == null)
        {
            motor = gameObject.AddComponent<AgentMotor>();
            motor.pathRefreshTime = pathRefreshTime;
            motor.waypointReachDistance = waypointReachDistance;
            motor.rotationSpeed = rotationSpeed;
            motor.separationRadius = separationRadius;
            motor.separationStrength = separationStrength;
        }

        brain = GetComponent<AgentBrain>();
        if (brain == null)
        {
            brain = gameObject.AddComponent<AgentBrain>();
        }
    }
}
