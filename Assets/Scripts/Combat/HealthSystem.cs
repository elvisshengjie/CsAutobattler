using System;
using UnityEngine;

public class HealthSystem : MonoBehaviour
{
    private AgentStats stats;
    private AgentController controller;
    private AgentController3D controller3D;
    private AgentBrain brain3D;
    private AgentMotor motor3D;
    private WeaponSystem weapon;

    private float currentHealth;
    private bool isDead = false;

    public float CurrentHealth => currentHealth;
    public float NormalizedHealth => stats == null || stats.maxHealth <= 0f
        ? 0f
        : Mathf.Clamp01(currentHealth / stats.maxHealth);
    public bool IsDead => isDead;
    public GameObject LastAttacker { get; private set; }
    public event Action<HealthSystem, float, GameObject> Damaged;
    public event Action<HealthSystem> Died;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        WeaponLoadout startingLoadout = GetComponent<WeaponLoadout>();
        stats.maxHealth = startingLoadout != null
            ? startingLoadout.AgentHealth
            : WeaponDefaults.Get(WeaponType.Rifle).agentHealth;
        controller = GetComponent<AgentController>();
        controller3D = GetComponent<AgentController3D>();
        brain3D = GetComponent<AgentBrain>();
        motor3D = GetComponent<AgentMotor>();
        weapon = GetComponent<WeaponSystem>();

        currentHealth = stats.maxHealth;

        if (GetComponent<AgentController3D>() != null &&
            GetComponent<AgentHealthBar3D>() == null)
        {
            gameObject.AddComponent<AgentHealthBar3D>();
        }
    }

    public void SetLoadoutHealth(float newMaximumHealth)
    {
        if (stats == null || isDead) return;
        stats.maxHealth = Mathf.Max(1f, newMaximumHealth);
        currentHealth = stats.maxHealth;
    }

    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (isDead)
        {
            return;
        }

        if (attacker != null)
        {
            LastAttacker = attacker;
            if (controller3D != null)
            {
                controller3D.NotifyAttackedBy(attacker);
            }
        }

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        Damaged?.Invoke(this, amount, attacker);

        Debug.Log(gameObject.name + " took " + amount + " damage. HP: " + currentHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        Debug.Log(gameObject.name + " died.");
        Died?.Invoke(this);

        if (controller != null)
        {
            controller.enabled = false;
        }

        if (controller3D != null)
        {
            controller3D.enabled = false;
        }

        if (brain3D != null)
        {
            brain3D.enabled = false;
        }

        if (motor3D != null)
        {
            motor3D.Stop();
            motor3D.enabled = false;
        }

        if (weapon != null)
        {
            weapon.enabled = false;
        }

        Rigidbody body3D = GetComponent<Rigidbody>();
        if (body3D != null && !body3D.isKinematic)
        {
            body3D.linearVelocity = Vector3.zero;
            body3D.angularVelocity = Vector3.zero;
        }

        // Died subscribers (bomb drop, scoring, round checks) run before this.
        // Deactivation is immediate, so visuals, health bars, colliders, and AI
        // disappear in the fatal-damage frame. Keep the inactive object briefly so
        // squad caches can discard their references before final destruction.
        gameObject.SetActive(false);
        Destroy(gameObject, 1.2f);
    }
}
