using UnityEngine;

public enum TeamType
{
    Blue,
    Red
}

public class AgentStats : MonoBehaviour
{
    [Header("Team")]
    public TeamType team;

    [Header("Health")]
    public float maxHealth = 200f;

    [Header("Movement")]
    public float moveSpeed = 2f;

    [Header("Legacy Weapon Fallback (runtime uses WeaponLoadout)")]
    [HideInInspector]
    public float attackRange = 3f;
    [HideInInspector]
    public float damage = 10f;
    [HideInInspector]
    public float attackCooldown = 1f;

    [Tooltip("Aim accuracy from 0 to 100. Lower values produce a wider random shot spread.")]
    [Range(0f, 100f)]
    [HideInInspector]
    public float accuracy = 75f;
}
