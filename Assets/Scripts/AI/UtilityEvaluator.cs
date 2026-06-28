using System.Collections.Generic;
using UnityEngine;

public enum AIActionType
{
    Attack,
    Reload,
    TakeCover,
    Retreat,
    Reposition,
    SearchEnemy,
    ApproachEnemy,
    HoldRange,
    CreateDistance
}

public struct UtilityDecision
{
    public AIActionType action;
    public float score;

    public UtilityDecision(AIActionType action, float score)
    {
        this.action = action;
        this.score = Mathf.Clamp01(score);
    }
}

[RequireComponent(typeof(AgentStats), typeof(AgentPerception))]
public class UtilityEvaluator : MonoBehaviour
{
    [Header("Awareness")]
    public float supportRadius = 4f;
    public float dangerRadius = 5f;

    [Header("Personality")]
    [Tooltip("Global multiplier for personality differences. 1.5 makes traits clearly visible.")]
    [Range(0f, 2f)] public float personalityStrength = 1.5f;

    public List<UtilityDecision> Evaluate(
        AgentStats target,
        IReadOnlyList<AgentStats> visibleEnemies,
        HealthSystem health,
        WeaponSystem weapon)
    {
        float healthFactor = health.HealthNormalized;
        float lowHealth = 1f - healthFactor;
        float ammoFactor = weapon.AmmoNormalized;
        float ammoUrgency = 1f - ammoFactor;
        float danger = CalculateDanger(visibleEnemies);
        float allySupport = CalculateAllySupport();
        int nearbyEnemies = CountNearbyEnemies();
        int nearbyAllies = CountNearbyAllies();
        float outnumbered = Mathf.Clamp01((nearbyEnemies - nearbyAllies + 1f) / 3f);

        // Get Personality Traits
        AgentPersonality personality = GetComponent<AgentPersonality>();
        float aggressiveness = personality != null ? personality.aggressiveness : 0.5f;
        float courage = personality != null ? personality.courage : 0.5f;
        float discipline = personality != null ? personality.discipline : 0.5f;
        float teamwork = personality != null ? personality.teamwork : 0.5f;
        float caution = personality != null ? personality.caution : 0.5f;

        AgentStats stats = GetComponent<AgentStats>();
        float configuredPrefDist = personality != null ? personality.preferredCombatDistance : 3.0f;
        float minDist = personality != null ? personality.minimumCombatDistance : 1.5f;
        float maxDist = personality != null ? personality.maximumCombatDistance : 5.0f;
        float tolerance = personality != null ? personality.distanceTolerance : 0.5f;

        // An agent must never hold beyond its weapon range. Leave a small
        // margin so collider spacing and floating-point error do not prevent firing.
        float prefDist = Mathf.Min(
            configuredPrefDist,
            Mathf.Max(minDist, stats.attackRange - 0.25f));

        bool targetVisible = target != null;
        float distance = targetVisible
            ? Vector2.Distance(transform.position, target.transform.position)
            : Mathf.Infinity;

        float rangeSuitability = targetVisible
            ? Mathf.Clamp01(1f - Mathf.Abs(distance - prefDist) / Mathf.Max(0.1f, maxDist))
            : 0f;

        // 1. Attack score: highly preferred if target is in range and we have ammo
        float attack = 0f;
        if (targetVisible && weapon.currentAmmo > 0 && !weapon.IsReloading)
        {
            if (distance <= stats.attackRange)
            {
                attack =
                    0.5f +
                    aggressiveness * 0.4f * personalityStrength +
                    courage * 0.1f * personalityStrength -
                    caution * 0.15f * personalityStrength;
            }
            else
            {
                attack =
                    0.2f +
                    aggressiveness * 0.3f * personalityStrength +
                    courage * 0.1f * personalityStrength +
                    rangeSuitability * 0.1f -
                    caution * 0.1f * personalityStrength;
            }
        }

        // 2. Reload score
        float reload = weapon.IsReloading
            ? 1f
            : weapon.currentAmmo < weapon.magazineSize
                ? ammoUrgency * ammoUrgency * Mathf.Lerp(0.5f, 1f, danger)
                : 0f;

        // 3. Take Cover score
        float takeCover = targetVisible
            ? Mathf.Clamp01(
                danger * 0.3f +
                lowHealth * 0.2f +
                ammoUrgency * danger * 0.2f +
                caution * 0.45f * personalityStrength +
                teamwork * allySupport * 0.25f * personalityStrength)
            : 0f;

        // 4. Retreat score
        float retreat = 0f;
        if (targetVisible)
        {
            float retreatBase = (distance < minDist) ? 0.6f : (lowHealth * Mathf.Max(0.25f, danger) * Mathf.Max(0.35f, outnumbered));
            retreat =
                retreatBase +
                caution * 0.5f * personalityStrength +
                lowHealth * 0.25f -
                courage * 0.55f * personalityStrength -
                aggressiveness * 0.15f * personalityStrength;
        }
        else
        {
            retreat =
                lowHealth * 0.2f +
                caution * 0.15f * personalityStrength -
                courage * 0.2f * personalityStrength;
        }

        // 5. Reposition score
        float reposition = targetVisible
            ? Mathf.Clamp01(
                0.1f +
                (1f - rangeSuitability) * 0.1f +
                teamwork * allySupport * 0.3f * personalityStrength +
                discipline * 0.15f * personalityStrength)
            : 0.05f;

        // Searching, rather than tactical repositioning, is the default when
        // no enemy is currently visible.
        float searchEnemy = targetVisible ? 0f : 0.7f;

        // 6. ApproachEnemy score
        float approachEnemy = 0f;
        if (targetVisible)
        {
            if (distance > maxDist)
            {
                approachEnemy =
                    0.35f +
                    aggressiveness * 0.5f * personalityStrength +
                    courage * 0.15f * personalityStrength -
                    caution * 0.2f * personalityStrength +
                    discipline * 0.1f * personalityStrength;
            }
            else if (distance > prefDist + tolerance)
            {
                approachEnemy =
                    0.3f +
                    ((distance - prefDist) / maxDist) * 0.2f +
                    aggressiveness * 0.4f * personalityStrength +
                    courage * 0.1f * personalityStrength -
                    caution * 0.15f * personalityStrength +
                    discipline * 0.1f * personalityStrength;
            }
            else
            {
                approachEnemy = 0.05f + aggressiveness * 0.15f * personalityStrength;
            }
        }

        // 7. HoldRange score
        float holdRange = 0f;
        if (targetVisible)
        {
            if (distance <= stats.attackRange && Mathf.Abs(distance - prefDist) <= tolerance)
            {
                holdRange = 0.55f + discipline * 0.4f * personalityStrength;
            }
            else
            {
                float diff = Mathf.Abs(distance - prefDist);
                holdRange = Mathf.Max(
                    0f,
                    0.2f + discipline * 0.15f * personalityStrength - (diff / prefDist) * 0.3f);
            }
        }

        // 8. CreateDistance score
        float createDistance = 0f;
        if (targetVisible)
        {
            if (distance < minDist)
            {
                createDistance =
                    0.35f +
                    caution * 0.5f * personalityStrength +
                    discipline * 0.15f * personalityStrength +
                    lowHealth * 0.2f -
                    courage * 0.2f * personalityStrength;
            }
            else if (distance < prefDist - tolerance)
            {
                createDistance =
                    0.2f +
                    caution * 0.4f * personalityStrength +
                    discipline * 0.15f * personalityStrength +
                    lowHealth * 0.15f -
                    courage * 0.15f * personalityStrength;
            }
            else
            {
                createDistance =
                    caution * 0.15f * personalityStrength +
                    lowHealth * 0.1f;
            }
        }

        // Hard tactical priorities prevent obviously bad choices.
        if (weapon.currentAmmo == 0)
        {
            reload = 1f;
        }

        if (healthFactor <= 0.2f && danger > 0.2f)
        {
            retreat = Mathf.Clamp01(1f - courage * 0.5f);
        }

        return new List<UtilityDecision>
        {
            new UtilityDecision(AIActionType.Attack, Mathf.Clamp01(attack)),
            new UtilityDecision(AIActionType.Reload, Mathf.Clamp01(reload)),
            new UtilityDecision(AIActionType.TakeCover, Mathf.Clamp01(takeCover)),
            new UtilityDecision(AIActionType.Retreat, Mathf.Clamp01(retreat)),
            new UtilityDecision(AIActionType.Reposition, Mathf.Clamp01(reposition)),
            new UtilityDecision(AIActionType.SearchEnemy, searchEnemy),
            new UtilityDecision(AIActionType.ApproachEnemy, Mathf.Clamp01(approachEnemy)),
            new UtilityDecision(AIActionType.HoldRange, Mathf.Clamp01(holdRange)),
            new UtilityDecision(AIActionType.CreateDistance, Mathf.Clamp01(createDistance))
        };
    }

    private float CalculateDanger(IReadOnlyList<AgentStats> enemies)
    {
        float danger = 0f;
        foreach (AgentStats enemy in enemies)
        {
            float distance = Vector2.Distance(transform.position, enemy.transform.position);
            danger += 1f / (1f + distance * distance * 0.2f);
        }
        return Mathf.Clamp01(danger);
    }

    private float CalculateAllySupport()
    {
        return Mathf.Clamp01(CountNearbyAllies() / 3f);
    }

    private int CountNearbyAllies()
    {
        AgentStats self = GetComponent<AgentStats>();
        int count = 0;
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (agent != self && agent.team == self.team &&
                Vector2.Distance(transform.position, agent.transform.position) <= supportRadius)
            {
                count++;
            }
        }
        return count;
    }

    private int CountNearbyEnemies()
    {
        AgentStats self = GetComponent<AgentStats>();
        int count = 0;
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (agent.team != self.team &&
                Vector2.Distance(transform.position, agent.transform.position) <= dangerRadius)
            {
                count++;
            }
        }
        return count;
    }
}
