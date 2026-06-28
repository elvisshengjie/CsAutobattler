using UnityEngine;

public static class TeamKnowledge
{
    private static readonly Vector2[] enemyPositions = new Vector2[2];
    private static readonly float[] observationTimes = new float[2];
    private static readonly bool[] hasObservation = new bool[2];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        for (int i = 0; i < hasObservation.Length; i++)
        {
            hasObservation[i] = false;
            observationTimes[i] = float.NegativeInfinity;
            enemyPositions[i] = Vector2.zero;
        }
    }

    public static void ReportEnemy(TeamType observingTeam, Vector2 enemyPosition)
    {
        int index = (int)observingTeam;
        enemyPositions[index] = enemyPosition;
        observationTimes[index] = Time.time;
        hasObservation[index] = true;
    }

    public static bool TryGetLatestEnemyPosition(
        TeamType team,
        float maximumAge,
        out Vector2 position,
        out float observationTime)
    {
        int index = (int)team;
        bool isFresh = hasObservation[index] &&
            Time.time - observationTimes[index] <= maximumAge;

        position = enemyPositions[index];
        observationTime = observationTimes[index];
        return isFresh;
    }
}
