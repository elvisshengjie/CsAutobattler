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
    public float maxHealth = 100f;

    [Header("Movement")]
    public float moveSpeed = 2f;

    [Header("Weapon")]
    public float attackRange = 3f;
    public float damage = 10f;
    public float attackCooldown = 1f;
}