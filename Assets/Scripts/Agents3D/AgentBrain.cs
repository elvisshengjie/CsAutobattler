using UnityEngine;

/// <summary>
/// Chooses the current action using sensor information and memory.
/// This intentionally remains a small rule-based brain until Utility AI is added.
/// </summary>
[RequireComponent(typeof(AgentSensors))]
[RequireComponent(typeof(AgentMemory))]
[RequireComponent(typeof(AgentMotor))]
public class AgentBrain : MonoBehaviour
{
    private AgentStats stats;
    private AgentSensors sensors;
    private AgentMemory memory;
    private AgentMotor motor;
    private WeaponSystem weapon;

    public GameObject CurrentTarget { get; private set; }

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        sensors = GetComponent<AgentSensors>();
        memory = GetComponent<AgentMemory>();
        motor = GetComponent<AgentMotor>();
        weapon = GetComponent<WeaponSystem>();
    }

    private void Update()
    {
        if (stats == null || sensors == null || memory == null || motor == null)
        {
            return;
        }

        CurrentTarget = sensors.FindClosestDetectedEnemy();

        if (CurrentTarget != null)
        {
            memory.ObserveEnemy(CurrentTarget);
            float distance = FlatDistance(transform.position, CurrentTarget.transform.position);

            if (distance <= stats.attackRange &&
                sensors.HasLineOfSight(CurrentTarget))
            {
                motor.Stop();
                motor.FacePosition(CurrentTarget.transform.position);
                if (weapon != null)
                {
                    weapon.TryAttack(CurrentTarget);
                }

                return;
            }
        }

        if (memory.TryGetKnownPosition(out Vector3 knownPosition))
        {
            if (CurrentTarget == null &&
                FlatDistance(transform.position, knownPosition) <= motor.waypointReachDistance)
            {
                memory.Clear();
                motor.Stop();
                return;
            }

            motor.MoveTo(knownPosition);
            return;
        }

        motor.Stop();
    }

    private void OnDisable()
    {
        if (motor != null)
        {
            motor.Stop();
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
