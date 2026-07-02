using System.Collections.Generic;
using UnityEngine;

public enum BombSiteId
{
    A,
    B
}

[RequireComponent(typeof(BoxCollider))]
public class BombSite : MonoBehaviour
{
    public BombSiteId siteId;
    public Vector3 plantPositionOffset = new Vector3(0f, 0.15f, 0f);

    private readonly HashSet<AgentStats> agentsInside = new HashSet<AgentStats>();

    public Vector3 PlantPosition => transform.position + plantPositionOffset;
    public IReadOnlyCollection<AgentStats> AgentsInside => agentsInside;

    private void Reset()
    {
        BoxCollider trigger = GetComponent<BoxCollider>();
        trigger.isTrigger = true;
    }

    private void Awake()
    {
        BoxCollider trigger = GetComponent<BoxCollider>();
        trigger.isTrigger = true;
    }

    public bool Contains(GameObject agent)
    {
        if (agent == null)
        {
            return false;
        }

        AgentStats stats = agent.GetComponentInParent<AgentStats>();
        return stats != null && agentsInside.Contains(stats);
    }

    public BombCarrier GetCarrierInside()
    {
        foreach (AgentStats agent in agentsInside)
        {
            if (agent == null)
            {
                continue;
            }

            BombCarrier carrier = agent.GetComponent<BombCarrier>();
            if (carrier != null && carrier.HasBomb)
            {
                return carrier;
            }
        }

        return null;
    }

    public Vector3 GetNearestPlantPosition(Vector3 agentPosition)
    {
        if (ObjectiveManager.Instance != null)
        {
            return ObjectiveManager.Instance.FindBestPlantPosition(this, agentPosition);
        }

        BoxCollider siteTrigger = GetComponent<BoxCollider>();
        if (siteTrigger != null && AStarPathfinder3D.Instance != null &&
            AStarPathfinder3D.Instance.TryGetNearestWalkablePositionInBounds(
                agentPosition,
                siteTrigger.bounds,
                0.5f,
                out Vector3 gridPosition))
        {
            gridPosition.y = PlantPosition.y;
            return gridPosition;
        }

        return PlantPosition;
    }

    private void OnTriggerEnter(Collider other)
    {
        AgentStats agent = other.GetComponentInParent<AgentStats>();
        if (agent != null)
        {
            agentsInside.Add(agent);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        AgentStats agent = other.GetComponentInParent<AgentStats>();
        if (agent != null)
        {
            agentsInside.Remove(agent);
        }
    }
}
