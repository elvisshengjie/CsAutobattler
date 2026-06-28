using UnityEngine;

[RequireComponent(typeof(AgentPerception))]
[RequireComponent(typeof(AgentMovement))]
[RequireComponent(typeof(UtilityEvaluator))]
[RequireComponent(typeof(AgentBrain))]
public class AgentController : MonoBehaviour
{
    [Header("Pathfinding")]
    public float pathRefreshTime = 0.5f;
    public float waypointReachDistance = 0.15f;

    private void Awake()
    {
        AgentPerception perception = GetComponent<AgentPerception>();
        if (perception == null)
        {
            gameObject.AddComponent<AgentPerception>();
        }

        AgentMovement movement = GetComponent<AgentMovement>();
        if (movement == null)
        {
            movement = gameObject.AddComponent<AgentMovement>();
        }

        if (GetComponent<UtilityEvaluator>() == null)
        {
            gameObject.AddComponent<UtilityEvaluator>();
        }

        if (GetComponent<AgentPersonality>() == null)
        {
            gameObject.AddComponent<AgentPersonality>();
        }

        if (GetComponent<AgentBrain>() == null)
        {
            gameObject.AddComponent<AgentBrain>();
        }

        movement.pathRefreshTime = pathRefreshTime;
        movement.waypointReachDistance = waypointReachDistance;
    }
}
