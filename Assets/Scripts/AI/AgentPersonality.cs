using UnityEngine;

[DisallowMultipleComponent]
public class AgentPersonality : MonoBehaviour
{
    [Header("Personality Traits")]
    [Tooltip("Increases attack and approach weights.")]
    [Range(0f, 1f)] public float aggressiveness = 0.5f;

    [Tooltip("Reduces retreating tendencies.")]
    [Range(0f, 1f)] public float courage = 0.5f;

    [Tooltip("Affects how strictly they adhere to tactical positioning.")]
    [Range(0f, 1f)] public float discipline = 0.5f;

    [Tooltip("Increases the value of positions near allies.")]
    [Range(0f, 1f)] public float teamwork = 0.5f;

    [Tooltip("Increases cover and retreat weights under low health/high danger.")]
    [Range(0f, 1f)] public float caution = 0.5f;

    [Header("Combat Distances")]
    [Tooltip("The ideal distance the agent wants to maintain from its target.")]
    [Min(0f)] public float preferredCombatDistance = 3.0f;

    [Tooltip("The distance below which the agent feels too close and wants to create distance or retreat.")]
    [Min(0f)] public float minimumCombatDistance = 1.5f;

    [Tooltip("The distance above which the agent feels too far and wants to approach.")]
    [Min(0f)] public float maximumCombatDistance = 5.0f;

    [Tooltip("Tolerance margin around the preferred combat distance to prevent constant jitter.")]
    [Min(0f)] public float distanceTolerance = 0.5f;
}
