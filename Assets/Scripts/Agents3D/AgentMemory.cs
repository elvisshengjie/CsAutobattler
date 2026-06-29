using UnityEngine;

/// <summary>
/// Stores imperfect knowledge. It never searches the scene for enemies.
/// </summary>
public class AgentMemory : MonoBehaviour
{
    public float memoryDuration = 4f;

    private Vector3 lastKnownEnemyPosition;
    private float lastObservationTime = Mathf.NegativeInfinity;
    private bool hasKnownEnemyPosition;

    public Vector3 LastKnownEnemyPosition => lastKnownEnemyPosition;
    public bool HasKnownEnemyPosition =>
        hasKnownEnemyPosition && Time.time <= lastObservationTime + memoryDuration;

    public void ObserveEnemy(GameObject enemy)
    {
        if (enemy == null)
        {
            return;
        }

        RememberPosition(enemy.transform.position);
    }

    public void RememberAttacker(GameObject attacker, TeamType ownTeam)
    {
        if (attacker == null)
        {
            return;
        }

        AgentStats attackerStats = attacker.GetComponentInParent<AgentStats>();
        if (attackerStats == null || attackerStats.team == ownTeam)
        {
            return;
        }

        RememberPosition(attackerStats.transform.position);
    }

    public bool TryGetKnownPosition(out Vector3 position)
    {
        if (!HasKnownEnemyPosition)
        {
            Clear();
            position = default;
            return false;
        }

        position = lastKnownEnemyPosition;
        return true;
    }

    public void Clear()
    {
        hasKnownEnemyPosition = false;
        lastObservationTime = Mathf.NegativeInfinity;
    }

    private void RememberPosition(Vector3 position)
    {
        lastKnownEnemyPosition = position;
        lastObservationTime = Time.time;
        hasKnownEnemyPosition = true;
    }
}
