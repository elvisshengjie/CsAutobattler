using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AgentRole : MonoBehaviour
{
    [SerializeField] private AgentRoleType selectedRole = AgentRoleType.Assaulter;
    [SerializeField] private AgentRoleProfile profile;
    [SerializeField] private AgentRoleTuning tuning = new AgentRoleTuning();
    [Header("Runtime Debug")]
    [SerializeField] private Vector3 currentRoleDestination;
    [SerializeField] private string currentDebugReason = "Not evaluated";
    [SerializeField] private float currentDebugScore;
    [Header("Destination Stability")]
    [SerializeField, Min(0f)] private float minimumScoreImprovement = 0.75f;
    [SerializeField, Min(0f)] private float destinationLockDuration = 3f;
    [SerializeField, Min(0f)] private float immediateDangerThreshold = 1.25f;

    private AgentStats stats;
    private AgentMotor motor;
    private AgentRoleIndicator roleIndicator;
    private float nextDestinationEvaluation;
    private Vector3 lastRequestedDestination;
    private float destinationLockedUntil;
    private bool hasRoleDestination;

    public AgentRoleType SelectedRole => selectedRole;
    public Vector3 CurrentRoleDestination => currentRoleDestination;
    public string CurrentDebugReason => currentDebugReason;
    public float CurrentDebugScore => currentDebugScore;
    public AgentRoleTuning Tuning => profile != null && profile.role == selectedRole
        ? profile.tuning : tuning;

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        motor = GetComponent<AgentMotor>();
        if (tuning == null) tuning = AgentRoleDefaults.Create(selectedRole);
        EnsureRoleIndicator();
    }

    private void OnEnable()
    {
        EnsureRoleIndicator();
    }

    public void SetRole(AgentRoleType role)
    {
        selectedRole = role;
        if (profile == null || profile.role != role) tuning = AgentRoleDefaults.Create(role);
        nextDestinationEvaluation = 0f;
        destinationLockedUntil = 0f;
        hasRoleDestination = false;
        EnsureRoleIndicator();
    }

    private void EnsureRoleIndicator()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (roleIndicator == null)
        {
            roleIndicator = GetComponent<AgentRoleIndicator>();
            if (roleIndicator == null)
            {
                roleIndicator = gameObject.AddComponent<AgentRoleIndicator>();
            }
        }

        roleIndicator.SetRole(selectedRole);
    }

    public GameObject SelectPreferredTarget(GameObject fallback, AgentSensors sensors)
    {
        if (stats == null || sensors == null) return fallback;
        AgentStats[] candidates = FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude);
        GameObject best = fallback;
        float bestScore = fallback != null ? ScoreTarget(fallback) : float.NegativeInfinity;
        foreach (AgentStats candidate in candidates)
        {
            HealthSystem health = candidate.GetComponent<HealthSystem>();
            if (candidate == stats || candidate.team == stats.team || health == null || health.IsDead ||
                !sensors.CanDetect(candidate.gameObject)) continue;
            float score = ScoreTarget(candidate.gameObject);
            if (score > bestScore) { bestScore = score; best = candidate.gameObject; }
        }
        return best;
    }

    public Vector3 RefineDestination(Vector3 requested, Vector3 watchPosition)
    {
        if (stats == null || motor == null || InfluenceMapManager.Instance == null) return requested;
        if (RequiresExactObjectivePosition())
        {
            currentRoleDestination = requested;
            currentDebugScore = 0f;
            currentDebugReason = "Exact plant/defuse objective overrides role positioning";
            lastRequestedDestination = requested;
            hasRoleDestination = true;
            destinationLockedUntil = 0f;
            return requested;
        }

        bool objectiveChanged = !hasRoleDestination ||
                                FlatDistance(requested, lastRequestedDestination) >= 1f;
        float currentDanger = hasRoleDestination
            ? InfluenceMapManager.Instance.Sample(
                InfluenceLayerType.Danger,
                currentRoleDestination)
            : 0f;
        bool immediateDanger = currentDanger >= immediateDangerThreshold;
        if (!objectiveChanged && !immediateDanger &&
            Time.time < destinationLockedUntil)
        {
            return currentRoleDestination;
        }
        if (Time.time < nextDestinationEvaluation &&
            FlatDistance(requested, lastRequestedDestination) < 1f) return currentRoleDestination;
        nextDestinationEvaluation = Time.time + 0.55f;
        lastRequestedDestination = requested;

        Vector3 best = requested;
        float bestScore = ScorePosition(requested, requested, watchPosition, out string reason);
        float radius = Tuning.destinationSearchRadius;
        const int angles = 16;
        for (int ring = 1; ring <= 3; ring++) for (int i = 0; i < angles; i++)
        {
            float distance = radius * ring / 3f;
            Vector3 candidate = requested + Quaternion.Euler(0f, i * 360f / angles, 0f) * Vector3.forward * distance;
            candidate.y = transform.position.y;
            AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
            if (pathfinder != null && !pathfinder.IsValidAgentPosition(
                    candidate, motor.AgentRadius + motor.MinObstacleClearance, gameObject, false)) continue;
            float score = ScorePosition(candidate, requested, watchPosition, out string candidateReason);
            if (score > bestScore)
            {
                if (pathfinder != null && pathfinder.FindPath(transform.position, candidate) == null) continue;
                bestScore = score; best = candidate; reason = candidateReason;
            }
        }
        if (!objectiveChanged && hasRoleDestination)
        {
            float existingScore = ScorePosition(
                currentRoleDestination,
                requested,
                watchPosition,
                out string existingReason);
            float requiredImprovement = immediateDanger ? (minimumScoreImprovement * 0.05f) : minimumScoreImprovement;
            if (bestScore < existingScore + requiredImprovement)
            {
                currentDebugScore = existingScore;
                currentDebugReason = existingReason + " | holding: improvement too small";
                destinationLockedUntil = Time.time + destinationLockDuration;
                return currentRoleDestination;
            }
        }

        currentRoleDestination = best;
        currentDebugScore = bestScore;
        currentDebugReason = immediateDanger
            ? reason + " | danger override"
            : reason;
        hasRoleDestination = true;
        destinationLockedUntil = Time.time + destinationLockDuration;
        return currentRoleDestination;
    }

    private bool RequiresExactObjectivePosition()
    {
        ObjectiveManager objective = ObjectiveManager.Instance;
        if (objective != null && objective.ActiveDefuser == gameObject)
        {
            return true;
        }

        BombCarrier carrier = GetComponent<BombCarrier>();
        return carrier != null && (carrier.HasBomb || carrier.IsPlanting);
    }

    public bool TryExecuteRoleMovement()
    {
        if (motor == null || InfluenceMapManager.Instance == null) return false;
        ObjectiveManager objective = ObjectiveManager.Instance;
        Vector3 goal;
        if (objective != null && objective.ActiveBomb != null) goal = objective.ActiveBomb.transform.position;
        else if (objective != null)
        {
            BombSite nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (BombSite site in objective.GetSites())
            {
                float distance = FlatDistance(transform.position, site.PlantPosition);
                if (distance >= nearestDistance) continue;
                nearest = site;
                nearestDistance = distance;
            }
            if (nearest == null) return false;
            goal = nearest.PlantPosition;
        }
        else return false;
        motor.MoveTo(RefineDestination(goal, goal));
        return true;
    }

    private float ScorePosition(Vector3 p, Vector3 objective, Vector3 watch, out string reason)
    {
        InfluenceMapManager map = InfluenceMapManager.Instance;
        AgentRoleTuning w = Tuning;
        float danger = map.Sample(InfluenceLayerType.Danger, p);
        float occupancy = map.Sample(InfluenceLayerType.FriendlyOccupancy, p);
        float cover = map.Sample(InfluenceLayerType.Cover, p);
        float front = map.Sample(InfluenceLayerType.Front, p);
        float enemy = map.Sample(InfluenceLayerType.Enemy, p);
        float objectiveValue = map.Sample(InfluenceLayerType.Objective, p);
        float allyDistanceValue = GetAllyDistanceValue(p, w.preferredAllyDistance);
        float sideRear = GetSideRearValue(p, watch);
        float dangerPenalty = danger * Mathf.Max(0.15f, 3f - w.dangerTolerance);
        float movementPenalty = FlatDistance(transform.position, p) * 0.08f;
        float score = allyDistanceValue * w.allyProximity + sideRear * w.sideRearPreference +
                      objectiveValue * w.objectiveProximity + cover * w.coverPreference +
                      front * (selectedRole == AgentRoleType.Assaulter ? w.dangerTolerance : -0.8f) -
                      dangerPenalty - occupancy * 3f - movementPenalty -
                      FlatDistance(p, objective) * 0.025f;
        
        WeaponSystem weaponSystem = GetComponent<WeaponSystem>();
        if (weaponSystem != null && weaponSystem.IsReloading) 
        {
            score += cover * 8f;
            score -= danger * 10f;
        }

        if (selectedRole == AgentRoleType.Flanker) score -= enemy * 1.5f;
        float weaponScore = ScoreWeaponPosition(p, cover, occupancy, danger,
            front, sideRear);
        score += weaponScore;
        WeaponLoadout loadout = WeaponLoadout.Get(gameObject);
        reason = $"{selectedRole}+{loadout.SelectedWeapon}: obj {objectiveValue:F1}, " +
                 $"cover {cover:F1}, danger {danger:F1}, weapon {weaponScore:F1}";
        return score;
    }

    private float ScoreWeaponPosition(Vector3 position, float cover,
        float occupancy, float danger, float front, float sideRear)
    {
        WeaponLoadout loadout = WeaponLoadout.Get(gameObject);
        float nearestEnemyDistance = GetNearestEnemyDistance(position);
        float rangeFit = GetRangeFit(nearestEnemyDistance,
            loadout.MinimumRange, loadout.MaximumRange);
        switch (loadout.SelectedWeapon)
        {
            case WeaponType.SMG:
                return rangeFit * 2.2f + sideRear * 1.5f +
                       loadout.MovementSpeedMultiplier * 0.5f - danger * 0.25f;
            case WeaponType.Sniper:
                return rangeFit * 3f + cover * 1.6f - occupancy * 2f -
                       front * 0.7f - danger * 0.45f;
            case WeaponType.Shotgun:
                return rangeFit * 3f + cover * 1.4f + sideRear * 0.8f -
                       danger * 0.2f;
            case WeaponType.Rifle:
            default:
                return rangeFit * 1.8f + cover * 0.65f - danger * 0.25f;
        }
    }

    private float GetNearestEnemyDistance(Vector3 position)
    {
        float nearest = float.PositiveInfinity;
        foreach (AgentStats candidate in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            HealthSystem health = candidate.GetComponent<HealthSystem>();
            if (candidate == stats || candidate.team == stats.team ||
                health == null || health.IsDead) continue;
            nearest = Mathf.Min(nearest,
                FlatDistance(position, candidate.transform.position));
        }
        return float.IsInfinity(nearest) ? 20f : nearest;
    }

    private static float GetRangeFit(float distance, float minimum, float maximum)
    {
        maximum = Mathf.Max(minimum + 0.1f, maximum);
        if (distance < minimum)
            return Mathf.Clamp01(distance / Mathf.Max(0.1f, minimum));
        if (distance > maximum)
            return Mathf.Clamp01(1f - (distance - maximum) / maximum);
        float preferred = Mathf.Lerp(minimum, maximum, 0.7f);
        return 1f - Mathf.Abs(distance - preferred) /
               Mathf.Max(0.1f, maximum - minimum);
    }

    private float ScoreTarget(GameObject target)
    {
        float distance = FlatDistance(transform.position, target.transform.position);
        int alliesEngaging = 0;
        foreach (AgentBrain brain in FindObjectsByType<AgentBrain>(FindObjectsInactive.Exclude))
        {
            AgentStats ally = brain.GetComponent<AgentStats>();
            if (ally != null && ally.team == stats.team && brain.CurrentTarget == target) alliesEngaging++;
        }
        Vector3 targetToSelf = transform.position - target.transform.position; targetToSelf.y = 0f;
        Vector3 forward = target.transform.forward; forward.y = 0f;
        float rear = targetToSelf.sqrMagnitude > 0.01f && forward.sqrMagnitude > 0.01f
            ? Mathf.Clamp01((-Vector3.Dot(forward.normalized, targetToSelf.normalized) + 1f) * 0.5f) : 0f;
        float weak = 1f - CombatTargetUtility.GetNormalizedHealth(target);
        float score = -distance * 0.08f;
        switch (selectedRole)
        {
            case AgentRoleType.Support: score += alliesEngaging * 2.5f; break;
            case AgentRoleType.Flanker: score += rear * 4f - alliesEngaging * 0.35f; break;
            case AgentRoleType.Assaulter: score += weak * 3f + alliesEngaging * 1.5f; break;
            case AgentRoleType.Defender:
                score += InfluenceMapManager.Instance != null
                    ? InfluenceMapManager.Instance.Sample(InfluenceLayerType.Objective, target.transform.position) * 3f : 0f;
                break;
        }
        return score * Tuning.targetFocus;
    }

    private float GetAllyDistanceValue(Vector3 p, float preferred)
    {
        float nearest = float.PositiveInfinity;
        foreach (AgentStats ally in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
            if (ally != stats && ally.team == stats.team) nearest = Mathf.Min(nearest, FlatDistance(p, ally.transform.position));
        return float.IsInfinity(nearest) ? 0f : Mathf.Exp(-Mathf.Abs(nearest - preferred) * 0.35f);
    }

    private float GetSideRearValue(Vector3 p, Vector3 watch)
    {
        Vector3 toPosition = p - watch; toPosition.y = 0f;
        AgentStats nearestEnemy = null; float nearest = float.PositiveInfinity;
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            HealthSystem health = agent.GetComponent<HealthSystem>();
            if (agent == stats || agent.team == stats.team || health == null || health.IsDead) continue;
            float d = FlatDistance(agent.transform.position, watch);
            if (d < nearest) { nearest = d; nearestEnemy = agent; }
        }
        if (nearestEnemy == null || toPosition.sqrMagnitude < 0.01f) return 0f;
        Vector3 forward = nearestEnemy.transform.forward; forward.y = 0f;
        return forward.sqrMagnitude < 0.01f ? 0f :
            Mathf.Clamp01((-Vector3.Dot(forward.normalized, toPosition.normalized) + 1f) * 0.5f);
    }

    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
}
