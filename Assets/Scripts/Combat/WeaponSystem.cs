using UnityEngine;

public class WeaponSystem : MonoBehaviour
{
    private AgentStats stats;
    private Animator animator;
    private float nextAttackTime = 0f;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        animator = GetComponent<Animator>();
    }

    public void TryAttack(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }

        HealthSystem targetHealth = target.GetComponent<HealthSystem>();

        if (targetHealth != null)
        {
            targetHealth.TakeDamage(stats.damage);
            nextAttackTime = Time.time + stats.attackCooldown;
        }
    }
}