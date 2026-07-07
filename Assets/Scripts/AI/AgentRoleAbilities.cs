using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared role abilities for both teams. Each ability waits for a concrete
/// battlefield need instead of firing merely because its cooldown is ready.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class AgentRoleAbilities : MonoBehaviour
{
    [Header("Defender Wall")]
    [SerializeField] private float wallCooldown = 28f;
    [SerializeField] private float wallLifetime = 14f;
    [SerializeField] private float wallThreatRange = 14f;
    [SerializeField] private Vector3 wallSize = new Vector3(3.6f, 1.6f, 0.35f);
    [SerializeField, Range(-0.25f, 0.75f)] private float flankDotThreshold = 0.35f;

    [Header("Support Heal")]
    [SerializeField] private float healRange = 6f;
    [SerializeField] private float healAmount = 28f;
    [SerializeField] private float healChannelDuration = 1.6f;
    [SerializeField] private float healCooldown = 8f;

    [Header("Assaulter Turret")]
    [SerializeField] private float turretCooldown = 36f;
    [SerializeField] private float turretInstallDuration = 2.6f;
    [SerializeField] private float turretHealth = 30f;
    [SerializeField] private float turretRange = 14f;
    [SerializeField, Range(20f, 140f)] private float turretFiringArc = 75f;
    [SerializeField] private float turretThreatRange = 20f;
    [SerializeField] private float turretObjectiveRange = 11f;

    private static readonly Dictionary<HealthSystem, AgentRoleAbilities> HealClaims =
        new Dictionary<HealthSystem, AgentRoleAbilities>();
    private static Material redWallMaterial;
    private static Material blueWallMaterial;
    private static Material healingBeamMaterial;

    private AgentStats stats;
    private AgentRole role;
    private HealthSystem health;
    private AgentMotor motor;
    private AgentHealthBar3D healthBar;
    private HealthSystem healingTarget;
    private AgentHealthBar3D healingTargetBar;
    private LineRenderer healingBeam;
    private float healingEndsAt;
    private float nextHealTime;
    private float nextWallTime;
    private float nextTurretTime;
    private float nextThinkTime;
    private float wallMessageUntil;
    private float abilityMessageUntil;
    private float lastDamagedAt = Mathf.NegativeInfinity;
    private bool isInstallingTurret;
    private float turretInstallStartedAt;
    private float turretInstallEndsAt;
    private Vector3 turretInstallStart;
    private Vector3 turretInstallPosition;
    private Quaternion turretInstallRotation;
    private DeployableTurret ownedTurret;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToAgents()
    {
        HealClaims.Clear();
        foreach (AgentStats agent in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Include))
        {
            if (agent.GetComponent<AgentController3D>() != null &&
                agent.GetComponent<AgentRoleAbilities>() == null)
            {
                agent.gameObject.AddComponent<AgentRoleAbilities>();
            }
        }
    }

    private void Awake()
    {
        stats = GetComponent<AgentStats>();
        role = GetComponent<AgentRole>();
        health = GetComponent<HealthSystem>();
        motor = GetComponent<AgentMotor>();
        healthBar = GetComponent<AgentHealthBar3D>();
        nextWallTime = Time.time + Random.Range(5f, 8f);
        nextHealTime = Time.time + Random.Range(1.5f, 3f);
        nextTurretTime = Time.time + Random.Range(6f, 9f);
    }

    private void OnEnable()
    {
        health ??= GetComponent<HealthSystem>();
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Damaged += OnDamaged;
        }
    }

    private void OnDamaged(HealthSystem damagedHealth, float amount, GameObject attacker)
    {
        lastDamagedAt = Time.time;
    }

    private void Update()
    {
        if (!CanUseAbilities())
        {
            CancelHealing(false);
            CancelTurretInstallation(false);
            ClearExpiredWallMessage();
            return;
        }

        role ??= GetComponent<AgentRole>();
        healthBar ??= GetComponent<AgentHealthBar3D>();
        if (role == null)
        {
            return;
        }

        if (!ReferenceEquals(healingTarget, null))
        {
            if (healingTarget == null)
            {
                CancelHealing(true);
                return;
            }

            UpdateHealing();
            return;
        }

        if (isInstallingTurret)
        {
            UpdateTurretInstallation();
            return;
        }

        ClearExpiredWallMessage();
        if (Time.time < nextThinkTime)
        {
            return;
        }

        nextThinkTime = Time.time + 0.3f;
        if (role.SelectedRole == AgentRoleType.Support && Time.time >= nextHealTime)
        {
            HealthSystem target = FindHealingTarget();
            if (target != null)
            {
                BeginHealing(target);
                return;
            }
        }

        if (role.SelectedRole == AgentRoleType.Defender && Time.time >= nextWallTime &&
            TryFindFlankThreat(out AgentStats threat) &&
            TryFindWallPose(threat.transform.position, out Vector3 position,
                out Quaternion rotation))
        {
            DeployWall(position, rotation);
            return;
        }

        if (role.SelectedRole == AgentRoleType.Assaulter && Time.time >= nextTurretTime &&
            ownedTurret == null && ShouldDeployTurret(out Vector3 watchPosition) &&
            TryFindTurretPose(watchPosition, out Vector3 turretPosition,
                out Quaternion turretRotation))
        {
            BeginTurretInstallation(turretPosition, turretRotation);
        }
    }

    private bool CanUseAbilities()
    {
        if (stats == null || health == null || health.IsDead ||
            !gameObject.activeInHierarchy)
        {
            return false;
        }

        RoundManager round = RoundManager.Instance;
        return round == null ||
               (round.CurrentState != RoundState.Preparation &&
                round.CurrentState != RoundState.RoundEnd &&
                round.CurrentState != RoundState.Defused &&
                round.CurrentState != RoundState.Exploded);
    }

    private HealthSystem FindHealingTarget()
    {
        PruneHealingClaims();
        HealthSystem best = null;
        float bestScore = 0f;
        foreach (AgentStats ally in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (ally == null || ally == stats || ally.team != stats.team)
            {
                continue;
            }

            HealthSystem allyHealth = ally.GetComponent<HealthSystem>();
            if (allyHealth == null || allyHealth.IsDead ||
                allyHealth.NormalizedHealth >= 0.995f ||
                HealClaims.ContainsKey(allyHealth))
            {
                continue;
            }

            float distance = FlatDistance(transform.position, ally.transform.position);
            if (distance > healRange ||
                DefenderTeamCoordinator.IsLineBlocked(
                    transform.position,
                    ally.transform.position))
            {
                continue;
            }

            float score = (1f - allyHealth.NormalizedHealth) * 100f - distance;
            if (score > bestScore)
            {
                bestScore = score;
                best = allyHealth;
            }
        }

        return best;
    }

    private void BeginHealing(HealthSystem target)
    {
        healingTarget = target;
        healingTargetBar = target.GetComponent<AgentHealthBar3D>();
        healingEndsAt = Time.time + healChannelDuration;
        HealClaims[target] = this;
        EnsureHealingBeam();
        healingBeam.enabled = true;
        UpdateHealingVisuals(healChannelDuration);
    }

    private void UpdateHealing()
    {
        role ??= GetComponent<AgentRole>();
        bool valid = role != null && role.SelectedRole == AgentRoleType.Support &&
                     healingTarget != null && !healingTarget.IsDead &&
                     healingTarget.NormalizedHealth < 0.995f &&
                     healingTarget.gameObject.activeInHierarchy &&
                     FlatDistance(transform.position,
                         healingTarget.transform.position) <= healRange + 0.75f &&
                     !DefenderTeamCoordinator.IsLineBlocked(
                         transform.position,
                         healingTarget.transform.position);
        if (!valid)
        {
            CancelHealing(true);
            return;
        }

        motor?.Stop();
        motor?.FacePosition(healingTarget.transform.position);
        float remaining = Mathf.Max(0f, healingEndsAt - Time.time);
        UpdateHealingVisuals(remaining);
        if (remaining > 0f)
        {
            return;
        }

        healingTarget.Heal(healAmount, gameObject);
        FinishHealing();
    }

    private void UpdateHealingVisuals(float remaining)
    {
        Color green = new Color(0.2f, 1f, 0.4f, 1f);
        healthBar?.SetAbilityStatus("HEALING", remaining, green);
        healingTargetBar?.SetAbilityStatus("RECEIVING HEAL", remaining, green);
        if (healingBeam != null && healingTarget != null)
        {
            healingBeam.SetPosition(0, transform.position + Vector3.up * 0.9f);
            healingBeam.SetPosition(
                1,
                healingTarget.transform.position + Vector3.up * 0.9f);
        }
    }

    private void FinishHealing()
    {
        ReleaseHealingClaim();
        ClearHealingVisuals();
        healingTarget = null;
        healingTargetBar = null;
        nextHealTime = Time.time + healCooldown;
    }

    private void CancelHealing(bool useShortCooldown)
    {
        if (ReferenceEquals(healingTarget, null))
        {
            return;
        }

        ReleaseHealingClaim();
        ClearHealingVisuals();
        healingTarget = null;
        healingTargetBar = null;
        if (useShortCooldown)
        {
            nextHealTime = Time.time + 1f;
        }
    }

    private void ReleaseHealingClaim()
    {
        if (!ReferenceEquals(healingTarget, null) &&
            HealClaims.TryGetValue(healingTarget, out AgentRoleAbilities owner) &&
            owner == this)
        {
            HealClaims.Remove(healingTarget);
        }
    }

    private void ClearHealingVisuals()
    {
        healthBar?.ClearAbilityStatus();
        if (healingTargetBar != null)
        {
            healingTargetBar.ClearAbilityStatus();
        }
        if (healingBeam != null)
        {
            healingBeam.enabled = false;
        }
    }

    private static void PruneHealingClaims()
    {
        List<HealthSystem> stale = null;
        foreach (KeyValuePair<HealthSystem, AgentRoleAbilities> claim in HealClaims)
        {
            if (claim.Key != null && claim.Value != null)
            {
                continue;
            }

            stale ??= new List<HealthSystem>();
            stale.Add(claim.Key);
        }

        if (stale == null)
        {
            return;
        }

        foreach (HealthSystem target in stale)
        {
            HealClaims.Remove(target);
        }
    }

    private void EnsureHealingBeam()
    {
        if (healingBeam != null)
        {
            return;
        }

        GameObject beam = new GameObject("HealingBeam");
        beam.transform.SetParent(transform, false);
        healingBeam = beam.AddComponent<LineRenderer>();
        healingBeam.useWorldSpace = true;
        healingBeam.positionCount = 2;
        healingBeam.startWidth = 0.055f;
        healingBeam.endWidth = 0.025f;
        healingBeam.startColor = new Color(0.2f, 1f, 0.4f, 0.9f);
        healingBeam.endColor = new Color(0.55f, 1f, 0.65f, 0.35f);
        healingBeam.sharedMaterial = GetHealingBeamMaterial();
        healingBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        healingBeam.receiveShadows = false;
        healingBeam.enabled = false;
    }

    private bool TryFindOpenFiringThreat(
        Vector3 protectedPoint,
        float range,
        out AgentStats threat)
    {
        threat = null;
        float bestScore = float.NegativeInfinity;
        foreach (AgentStats enemy in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (enemy == null || enemy.team == stats.team)
            {
                continue;
            }

            HealthSystem enemyHealth = enemy.GetComponent<HealthSystem>();
            float distance = FlatDistance(protectedPoint, enemy.transform.position);
            if (enemyHealth == null || enemyHealth.IsDead || distance > range ||
                DefenderTeamCoordinator.IsLineBlocked(
                    enemy.transform.position, protectedPoint) ||
                TacticalSmokeCloud.BlocksLine(
                    enemy.transform.position + Vector3.up * 0.8f,
                    protectedPoint + Vector3.up * 0.8f))
            {
                continue;
            }

            float score = range - distance;
            if (score > bestScore)
            {
                bestScore = score;
                threat = enemy;
            }
        }

        return threat != null;
    }

    private bool ShouldDeployTurret(out Vector3 watchPosition)
    {
        watchPosition = default;
        if (health == null || health.NormalizedHealth <= 0.45f ||
            Time.time - lastDamagedAt <= 2.5f)
        {
            return false;
        }

        ObjectiveManager objective = ObjectiveManager.Instance;
        RoundManager round = RoundManager.Instance;
        AgentStats threat;

        if (objective != null && objective.ActiveDefuser != null &&
            CombatTargetUtility.TryGetTeam(objective.ActiveDefuser,
                out TeamType defuserTeam) && defuserTeam == stats.team &&
            FlatDistance(transform.position,
                objective.ActiveDefuser.transform.position) <= turretObjectiveRange &&
            TryFindOpenFiringThreat(objective.ActiveDefuser.transform.position,
                turretThreatRange, out threat) && IsSafeTurretThreat(threat))
        {
            watchPosition = threat.transform.position;
            return true;
        }

        foreach (BombCarrier carrier in
                 FindObjectsByType<BombCarrier>(FindObjectsInactive.Exclude))
        {
            AgentStats carrierStats = carrier.GetComponent<AgentStats>();
            if (carrierStats != null && carrierStats.team == stats.team &&
                carrier.IsPlanting &&
                FlatDistance(transform.position, carrier.transform.position) <=
                turretObjectiveRange &&
                TryFindOpenFiringThreat(carrier.transform.position,
                    turretThreatRange, out threat) && IsSafeTurretThreat(threat))
            {
                watchPosition = threat.transform.position;
                return true;
            }
        }

        if (objective != null && objective.IsBombPlanted &&
            FlatDistance(transform.position, objective.PlantedBombPosition) <=
            turretObjectiveRange &&
            TryFindOpenFiringThreat(objective.PlantedBombPosition,
                turretThreatRange, out threat) && IsSafeTurretThreat(threat))
        {
            watchPosition = threat.transform.position;
            return true;
        }

        BombSite criticalSite = null;
        if (objective != null && round != null &&
            stats.team == round.attackingTeam)
        {
            criticalSite = objective.SelectedAttackSite;
        }
        criticalSite ??= objective != null
            ? GetNearestSite(objective, transform.position)
            : null;
        if (criticalSite != null &&
            FlatDistance(transform.position, criticalSite.PlantPosition) <=
            turretObjectiveRange &&
            TryFindOpenFiringThreat(criticalSite.PlantPosition,
                turretThreatRange, out threat) && IsSafeTurretThreat(threat))
        {
            watchPosition = threat.transform.position;
            return true;
        }

        AgentBrain brain = GetComponent<AgentBrain>();
        GameObject currentTarget = brain != null ? brain.CurrentTarget : null;
        if (currentTarget == null || !CombatTargetUtility.IsAlive(currentTarget) ||
            !CombatTargetUtility.TryGetTeam(currentTarget, out TeamType targetTeam) ||
            targetTeam == stats.team)
        {
            return false;
        }

        float targetDistance = FlatDistance(transform.position,
            currentTarget.transform.position);
        if (targetDistance < 5f || targetDistance > turretThreatRange ||
            DefenderTeamCoordinator.IsLineBlocked(
                transform.position, currentTarget.transform.position) ||
            !IsAtContestedPassage(currentTarget.transform.position))
        {
            return false;
        }

        watchPosition = currentTarget.transform.position;
        return true;
    }

    private bool IsSafeTurretThreat(AgentStats threat)
    {
        return threat != null &&
               FlatDistance(transform.position, threat.transform.position) >= 5f;
    }

    private bool IsAtContestedPassage(Vector3 threatPosition)
    {
        Vector3 forward = threatPosition - transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            return false;
        }

        forward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        Vector3 origin = transform.position + Vector3.up * 0.8f;
        bool leftWall = Physics.Raycast(origin, -side, 3.5f, ~0,
            QueryTriggerInteraction.Ignore);
        bool rightWall = Physics.Raycast(origin, side, 3.5f, ~0,
            QueryTriggerInteraction.Ignore);
        return leftWall && rightWall;
    }

    private bool TryFindTurretPose(
        Vector3 watchPosition,
        out Vector3 position,
        out Quaternion rotation)
    {
        Vector3 facing = watchPosition - transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.01f)
        {
            facing = transform.forward;
        }

        facing.Normalize();
        float[] angles = { 0f, -25f, 25f, -45f, 45f };
        float[] distances = { 1.45f, 2f };
        foreach (float distance in distances)
        {
            foreach (float angle in angles)
            {
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * facing;
                Vector3 candidate = transform.position + offset * distance;
                candidate.y = transform.position.y;
                Vector3 candidateFacing = watchPosition - candidate;
                candidateFacing.y = 0f;
                if (candidateFacing.sqrMagnitude < 0.01f)
                {
                    continue;
                }

                candidateFacing.Normalize();
                Quaternion candidateRotation = Quaternion.LookRotation(
                    candidateFacing, Vector3.up);
                if (IsTurretPlacementClear(candidate, candidateRotation) &&
                    HasTurretFiringLane(candidate, watchPosition, candidateRotation))
                {
                    position = candidate;
                    rotation = candidateRotation;
                    return true;
                }
            }
        }

        position = default;
        rotation = default;
        return false;
    }

    private bool IsTurretPlacementClear(Vector3 position, Quaternion rotation)
    {
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder != null && !pathfinder.IsInsideGrid(position, 0.65f))
        {
            return false;
        }

        Collider[] overlaps = Physics.OverlapBox(
            position + Vector3.up * 0.55f,
            new Vector3(0.5f, 0.55f, 0.5f),
            rotation,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || overlap.GetComponentInParent<AgentStats>() == stats ||
                overlap.name.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool HasTurretFiringLane(
        Vector3 position,
        Vector3 watchPosition,
        Quaternion rotation)
    {
        if (DefenderTeamCoordinator.IsLineBlocked(position, watchPosition))
        {
            return false;
        }

        return HasTurretForwardClearance(position, rotation);
    }

    private static bool HasTurretForwardClearance(
        Vector3 position,
        Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            return false;
        }

        forward.Normalize();
        Vector3 muzzle = position + Vector3.up * 0.85f + forward * 0.48f;
        RaycastHit[] hits = Physics.SphereCastAll(
            muzzle,
            0.2f,
            forward,
            1.35f,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                hit.collider.GetComponentInParent<AgentStats>() != null ||
                hit.collider.GetComponentInParent<BombSite>() != null)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void BeginTurretInstallation(Vector3 position, Quaternion rotation)
    {
        isInstallingTurret = true;
        turretInstallStartedAt = Time.time;
        turretInstallEndsAt = Time.time + turretInstallDuration;
        turretInstallStart = transform.position;
        turretInstallPosition = position;
        turretInstallRotation = rotation;
        motor?.Stop();
        motor?.FacePosition(position + rotation * Vector3.forward * 2f);
        healthBar?.SetAbilityStatus(
            "INSTALLING TURRET",
            turretInstallDuration,
            AgentRoleAbilities.GetWallColor(stats.team));
    }

    private void UpdateTurretInstallation()
    {
        bool valid = role != null && role.SelectedRole == AgentRoleType.Assaulter &&
                     health != null && !health.IsDead &&
                     lastDamagedAt <= turretInstallStartedAt &&
                     FlatDistance(transform.position, turretInstallStart) <= 0.8f &&
                     IsTurretPlacementClear(turretInstallPosition,
                         turretInstallRotation) &&
                     HasTurretForwardClearance(turretInstallPosition,
                         turretInstallRotation);
        if (!valid)
        {
            CancelTurretInstallation(true);
            return;
        }

        motor?.Stop();
        motor?.FacePosition(turretInstallPosition +
                            turretInstallRotation * Vector3.forward * 2f);
        float remaining = Mathf.Max(0f, turretInstallEndsAt - Time.time);
        healthBar?.SetAbilityStatus(
            "INSTALLING TURRET",
            remaining,
            AgentRoleAbilities.GetWallColor(stats.team));
        if (remaining > 0f)
        {
            return;
        }

        GameObject turretObject = new GameObject(stats.team + " Assaulter Turret");
        turretObject.layer = ResolveObstacleLayer();
        turretObject.transform.SetPositionAndRotation(
            turretInstallPosition,
            turretInstallRotation);
        ownedTurret = turretObject.AddComponent<DeployableTurret>();
        ownedTurret.Initialize(stats.team, turretHealth, turretRange, turretFiringArc);
        Physics.SyncTransforms();
        AStarPathfinder3D.Instance?.RefreshGrid();

        isInstallingTurret = false;
        nextTurretTime = Time.time + turretCooldown;
        abilityMessageUntil = Time.time + 1.25f;
        healthBar?.SetAbilityStatus(
            "TURRET ONLINE",
            0f,
            AgentRoleAbilities.GetWallColor(stats.team));
    }

    private void CancelTurretInstallation(bool useShortCooldown)
    {
        if (!isInstallingTurret)
        {
            return;
        }

        isInstallingTurret = false;
        healthBar?.ClearAbilityStatus();
        if (useShortCooldown)
        {
            nextTurretTime = Time.time + 2f;
        }
    }

    private static BombSite GetNearestSite(ObjectiveManager objective, Vector3 position)
    {
        BombSite best = null;
        float bestDistance = Mathf.Infinity;
        BombSite[] sites = { objective.siteA, objective.siteB };
        foreach (BombSite site in sites)
        {
            if (site == null)
            {
                continue;
            }

            float distance = FlatDistance(position, site.PlantPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = site;
            }
        }

        return best;
    }

    private bool TryFindFlankThreat(out AgentStats threat)
    {
        threat = null;
        Vector3 teamCenter = GetTeamCenter(stats.team);
        Vector3 enemyCenter = GetEnemyCenter(stats.team, teamCenter + transform.forward);
        Vector3 teamForward = enemyCenter - teamCenter;
        teamForward.y = 0f;
        if (teamForward.sqrMagnitude < 0.01f)
        {
            teamForward = transform.forward;
        }

        float bestScore = float.NegativeInfinity;
        foreach (AgentStats enemy in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (enemy == null || enemy.team == stats.team)
            {
                continue;
            }

            HealthSystem enemyHealth = enemy.GetComponent<HealthSystem>();
            if (enemyHealth == null || enemyHealth.IsDead)
            {
                continue;
            }

            float distance = FlatDistance(transform.position, enemy.transform.position);
            Vector3 teamToEnemy = enemy.transform.position - teamCenter;
            if (distance > wallThreatRange ||
                !IsSideOrRearThreat(teamForward, teamToEnemy, flankDotThreshold))
            {
                continue;
            }

            float alignment = Vector3.Dot(
                teamForward.normalized,
                teamToEnemy.normalized);
            float score = (1f - alignment) * 10f - distance * 0.25f;
            if (score > bestScore)
            {
                bestScore = score;
                threat = enemy;
            }
        }

        return threat != null;
    }

    private bool TryFindWallPose(
        Vector3 threatPosition,
        out Vector3 position,
        out Quaternion rotation)
    {
        Vector3 towardThreat = threatPosition - transform.position;
        towardThreat.y = 0f;
        if (towardThreat.sqrMagnitude < 0.01f)
        {
            towardThreat = transform.forward;
        }

        towardThreat.Normalize();
        float[] angles = { 0f, -22f, 22f, -42f, 42f };
        float[] distances = { 2f, 2.6f };
        foreach (float distance in distances)
        {
            foreach (float angle in angles)
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * towardThreat;
                Vector3 candidate = transform.position + direction * distance;
                candidate.y = transform.position.y;
                Quaternion candidateRotation = Quaternion.LookRotation(direction, Vector3.up);
                if (IsWallPlacementClear(candidate, candidateRotation))
                {
                    position = candidate;
                    rotation = candidateRotation;
                    return true;
                }
            }
        }

        position = default;
        rotation = default;
        return false;
    }

    private bool IsWallPlacementClear(Vector3 position, Quaternion rotation)
    {
        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder != null &&
            !pathfinder.IsInsideGrid(position, wallSize.x * 0.5f + 0.2f))
        {
            return false;
        }

        Vector3 halfExtents = wallSize * 0.45f;
        Collider[] overlaps = Physics.OverlapBox(
            position,
            halfExtents,
            rotation,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null ||
                overlap.GetComponentInParent<AgentStats>() == stats ||
                overlap.name.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void DeployWall(Vector3 position, Quaternion rotation)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"{stats.team} Defender Wall ({name})";
        wall.layer = ResolveObstacleLayer();
        wall.transform.SetPositionAndRotation(position, rotation);
        wall.transform.localScale = wallSize;
        Renderer renderer = wall.GetComponent<Renderer>();
        renderer.sharedMaterial = GetWallMaterial(stats.team);
        DeployedDefenderWall deployed = wall.AddComponent<DeployedDefenderWall>();
        deployed.Initialize(wallLifetime);
        Physics.SyncTransforms();
        AStarPathfinder3D.Instance?.RefreshGrid();

        nextWallTime = Time.time + wallCooldown;
        wallMessageUntil = Time.time + 1.25f;
        healthBar?.SetAbilityStatus(
            "WALL DEPLOYED",
            0f,
            GetWallColor(stats.team));
    }

    private void ClearExpiredWallMessage()
    {
        bool wallExpired = wallMessageUntil > 0f && Time.time >= wallMessageUntil;
        bool abilityExpired = abilityMessageUntil > 0f &&
                              Time.time >= abilityMessageUntil;
        if ((wallExpired || abilityExpired) && healingTarget == null &&
            !isInstallingTurret)
        {
            if (wallExpired) wallMessageUntil = 0f;
            if (abilityExpired) abilityMessageUntil = 0f;
            healthBar?.ClearAbilityStatus();
        }
    }

    private static int ResolveObstacleLayer()
    {
        int layer = LayerMask.NameToLayer("Obstacle3D");
        if (layer < 0)
        {
            layer = LayerMask.NameToLayer("Obstacle");
        }

        return Mathf.Max(0, layer);
    }

    private static Vector3 GetTeamCenter(TeamType team)
    {
        Vector3 total = Vector3.zero;
        int count = 0;
        foreach (AgentStats agent in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            HealthSystem candidateHealth = agent.GetComponent<HealthSystem>();
            if (agent.team == team && candidateHealth != null && !candidateHealth.IsDead)
            {
                total += agent.transform.position;
                count++;
            }
        }

        return count > 0 ? total / count : Vector3.zero;
    }

    private static Vector3 GetEnemyCenter(TeamType team, Vector3 fallback)
    {
        Vector3 total = Vector3.zero;
        int count = 0;
        foreach (AgentStats agent in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            HealthSystem candidateHealth = agent.GetComponent<HealthSystem>();
            if (agent.team != team && candidateHealth != null && !candidateHealth.IsDead)
            {
                total += agent.transform.position;
                count++;
            }
        }

        return count > 0 ? total / count : fallback;
    }

    public static bool IsSideOrRearThreat(
        Vector3 teamForward,
        Vector3 teamToEnemy,
        float dotThreshold = 0.35f)
    {
        teamForward.y = 0f;
        teamToEnemy.y = 0f;
        if (teamForward.sqrMagnitude < 0.001f || teamToEnemy.sqrMagnitude < 0.001f)
        {
            return false;
        }

        return Vector3.Dot(teamForward.normalized, teamToEnemy.normalized) <=
               dotThreshold;
    }

    public static Color GetWallColor(TeamType team)
    {
        return team == TeamType.Red
            ? new Color(0.78f, 0.04f, 0.04f, 1f)
            : new Color(0.04f, 0.18f, 0.88f, 1f);
    }

    private static Material GetWallMaterial(TeamType team)
    {
        ref Material material = ref team == TeamType.Red
            ? ref redWallMaterial
            : ref blueWallMaterial;
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        material = new Material(shader)
        {
            name = team + " Defender Wall Material",
            color = GetWallColor(team)
        };
        return material;
    }

    private static Material GetHealingBeamMaterial()
    {
        if (healingBeamMaterial != null)
        {
            return healingBeamMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        healingBeamMaterial = new Material(shader)
        {
            name = "Healing Beam Material",
            color = new Color(0.2f, 1f, 0.4f, 0.85f)
        };
        return healingBeamMaterial;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
        }

        CancelHealing(false);
        CancelTurretInstallation(false);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}

public sealed class DeployedDefenderWall : MonoBehaviour
{
    private float expiresAt;
    private bool expiring;

    public void Initialize(float lifetime)
    {
        expiresAt = Time.time + Mathf.Max(1f, lifetime);
    }

    private void Update()
    {
        RoundManager round = RoundManager.Instance;
        if (Time.time >= expiresAt ||
            (round != null && round.CurrentState == RoundState.RoundEnd))
        {
            Expire();
        }
    }

    private void Expire()
    {
        if (expiring)
        {
            return;
        }

        expiring = true;
        Collider wallCollider = GetComponent<Collider>();
        if (wallCollider != null)
        {
            wallCollider.enabled = false;
        }

        Physics.SyncTransforms();
        AStarPathfinder3D.Instance?.RefreshGrid();
        Destroy(gameObject);
    }
}
