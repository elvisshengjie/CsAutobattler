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
    private DefenderAgentAI defenderAI;
    private AttackerCombatAI attackerCombatAI;
    private AgentRole role;

    public GameObject CurrentTarget { get; private set; }

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        sensors = GetComponent<AgentSensors>();
        memory = GetComponent<AgentMemory>();
        motor = GetComponent<AgentMotor>();
        weapon = GetComponent<WeaponSystem>();
        defenderAI = GetComponent<DefenderAgentAI>();
        attackerCombatAI = GetComponent<AttackerCombatAI>();
        role = GetComponent<AgentRole>();
    }

    private void Update()
    {
        if (stats == null || sensors == null || memory == null || motor == null)
        {
            return;
        }

        GameObject normallyDetectedTarget = sensors.FindClosestDetectedEnemy();
        TeamTacticExecutor tacticExecutor = TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.GetComponent<TeamTacticExecutor>()
            : null;
        CurrentTarget = tacticExecutor != null
            ? tacticExecutor.SelectCombatTarget(
                gameObject,
                sensors,
                normallyDetectedTarget)
            : normallyDetectedTarget;
        if (role != null)
        {
            CurrentTarget = role.SelectPreferredTarget(CurrentTarget, sensors);
        }

        if (CurrentTarget != null)
        {
            memory.ObserveEnemy(CurrentTarget);
            TryShootWithoutInterruptingMovement(CurrentTarget);
        }

        if (ObjectiveManager.Instance != null &&
            ObjectiveManager.Instance.TryStartPriorityPlant(gameObject, CurrentTarget))
        {
            return;
        }

        if (defenderAI == null)
        {
            defenderAI = GetComponent<DefenderAgentAI>();
        }

        if (defenderAI != null && defenderAI.TryExecute(CurrentTarget))
        {
            return;
        }

        if (attackerCombatAI == null)
        {
            attackerCombatAI = GetComponent<AttackerCombatAI>();
        }

        if (attackerCombatAI != null && attackerCombatAI.TryExecute(CurrentTarget))
        {
            return;
        }

        if (CurrentTarget != null)
        {
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

        // Immediate combat stays decentralized, while ObjectiveManager supplies
        // the squad's claimed destination (assault, escort, defend, or rotate).
        if (ObjectiveManager.Instance != null &&
            ObjectiveManager.Instance.TryExecuteTacticalObjective(gameObject, motor))
        {
            return;
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

        if (role != null && role.TryExecuteRoleMovement())
        {
            return;
        }

        motor.Stop();
    }

    private void TryShootWithoutInterruptingMovement(GameObject target)
    {
        if (target == null || weapon == null || !weapon.IsReady ||
            FlatDistance(transform.position, target.transform.position) > stats.attackRange ||
            !sensors.HasLineOfSight(target))
        {
            return;
        }

        // Projectile aiming is target-based, so firing does not need to stop or
        // replace the current tactical slot movement.
        weapon.TryAttack(target);
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
