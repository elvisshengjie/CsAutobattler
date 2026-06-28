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
    public bool IsDead => isDead;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponentInChildren<Animator>();
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