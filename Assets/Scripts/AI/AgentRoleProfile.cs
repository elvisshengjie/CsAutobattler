using System;
using UnityEngine;

public enum AgentRoleType
{
    Support,
    Flanker,
    Assaulter,
    Defender
}

[Serializable]
public sealed class AgentRoleTuning
{
    [Range(0f, 3f)] public float allyProximity = 1f;
    [Range(0f, 3f)] public float dangerTolerance = 1f;
    [Range(0f, 3f)] public float sideRearPreference = 1f;
    [Range(0f, 3f)] public float objectiveProximity = 1f;
    [Range(0f, 3f)] public float coverPreference = 1f;
    [Range(0f, 3f)] public float targetFocus = 1f;
    [Range(1f, 15f)] public float preferredAllyDistance = 5f;
    [Range(1f, 20f)] public float destinationSearchRadius = 8f;
}

[CreateAssetMenu(menuName = "CsAutobattler/Agent Role Profile", fileName = "AgentRoleProfile")]
public sealed class AgentRoleProfile : ScriptableObject
{
    public AgentRoleType role;
    public AgentRoleTuning tuning = new AgentRoleTuning();
}

public static class AgentRoleDefaults
{
    public static AgentRoleTuning Create(AgentRoleType role)
    {
        AgentRoleTuning result = new AgentRoleTuning();
        switch (role)
        {
            case AgentRoleType.Support:
                result.allyProximity = 2.5f; result.dangerTolerance = 0.45f;
                result.sideRearPreference = 0.5f; result.objectiveProximity = 2f;
                result.coverPreference = 2.25f; result.targetFocus = 2.4f;
                result.preferredAllyDistance = 4f; result.destinationSearchRadius = 7f;
                break;
            case AgentRoleType.Flanker:
                result.allyProximity = 0.35f; result.dangerTolerance = 1.25f;
                result.sideRearPreference = 3f; result.objectiveProximity = 1.1f;
                result.coverPreference = 1.1f; result.targetFocus = 1.8f;
                result.preferredAllyDistance = 9f; result.destinationSearchRadius = 12f;
                break;
            case AgentRoleType.Assaulter:
                result.allyProximity = 1.15f; result.dangerTolerance = 2.6f;
                result.sideRearPreference = 0.45f; result.objectiveProximity = 2.3f;
                result.coverPreference = 0.9f; result.targetFocus = 2.5f;
                result.preferredAllyDistance = 5f; result.destinationSearchRadius = 8f;
                break;
            case AgentRoleType.Defender:
                result.allyProximity = 1f; result.dangerTolerance = 0.4f;
                result.sideRearPreference = 1.1f; result.objectiveProximity = 3f;
                result.coverPreference = 3f; result.targetFocus = 2f;
                result.preferredAllyDistance = 6f; result.destinationSearchRadius = 6f;
                break;
        }
        return result;
    }
}
