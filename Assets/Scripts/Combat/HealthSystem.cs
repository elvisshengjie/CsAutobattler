using UnityEngine;

public class HealthSystem : MonoBehaviour
{
    private AgentStats stats;
    private Animator animator;
    private AgentController controller;
    private WeaponSystem weapon;

    private float currentHealth;
    private bool isDead = false;

    public float CurrentHealth => currentHealth;
    public float HealthNormalized => stats != null && stats.maxHealth > 0f
        ? Mathf.Clamp01(currentHealth / stats.maxHealth)
        : 0f;
    public bool IsDead => isDead;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponent<Animator>();
        controller = GetComponent<AgentController>();
        weapon = GetComponent<WeaponSystem>();

        currentHealth = stats.maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (isDead)
        {
            return;
        }

        currentHealth -= amount;

        Debug.Log(gameObject.name + " took " + amount + " damage. HP: " + currentHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;

        Debug.Log(gameObject.name + " died.");

        if (controller != null)
        {
            controller.enabled = false;
        }

        AgentBrain brain = GetComponent<AgentBrain>();
        AgentMovement movement = GetComponent<AgentMovement>();

        if (brain != null)
        {
            brain.enabled = false;
        }

        if (movement != null)
        {
            movement.Stop();
            movement.enabled = false;
        }

        if (weapon != null)
        {
            weapon.enabled = false;
        }

        if (animator != null)
        {
            animator.SetTrigger("Dead");
        }

        Destroy(gameObject, 1.2f);
    }
}
