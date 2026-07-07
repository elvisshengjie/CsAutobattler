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

    [Header("Flanker Shadow Blink")]
    [SerializeField] private float shadowBlinkCooldown = 30f;
    [SerializeField] private float shadowBlinkOpeningCooldown = 10f;
    [SerializeField] private float shadowBlinkMaxRange = 22f;
    [SerializeField] private float shadowBlinkMinImprovement = 1.4f;
    [SerializeField] private float shadowBlinkCombatRange = 12f;
    [SerializeField] private float shadowBlinkRecentDamageWindow = 2.2f;
    [SerializeField] private float shadowBlinkFinisherHealth = 0.35f;
    [SerializeField] private float shadowBlinkPreferredDistance = 2.6f;
    [SerializeField] private float shadowBlinkDangerLimit = 4.5f;
    [SerializeField] private float shadowBlinkEnemyPileupRadius = 5.2f;
    [SerializeField] private int shadowBlinkMaxNearbyEnemies = 2;
    [SerializeField] private float shadowBlinkAgentClearance = 1.1f;
    [SerializeField] private float shadowBlinkTeamSpacing = 5f;
    [SerializeField] private float shadowBlinkMinimumScore = 8f;

    [Header("Debug")]
    [SerializeField] private bool drawAbilityRangeGizmos;
    [SerializeField] private bool drawOnlyCurrentRoleAbility = true;

    private static readonly Dictionary<HealthSystem, AgentRoleAbilities> HealClaims =
        new Dictionary<HealthSystem, AgentRoleAbilities>();
    private static readonly Dictionary<TeamType, float> NextTeamShadowBlinkTime =
        new Dictionary<TeamType, float>();
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
    private float nextShadowBlinkTime;
    private float nextThinkTime;
    private float wallMessageUntil;
    private float abilityMessageUntil;
    private float lastDamagedAt = Mathf.NegativeInfinity;
    private bool shadowBlinkOpeningStarted;
    private bool isInstallingTurret;
    private float turretInstallStartedAt;
    private float turretInstallEndsAt;
    private Vector3 turretInstallStart;
    private Vector3 turretInstallPosition;
    private Quaternion turretInstallRotation;
    private DeployableTurret ownedTurret;
    private static Material shadowBlinkMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToAgents()
    {
        HealClaims.Clear();
        NextTeamShadowBlinkTime.Clear();
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
        nextShadowBlinkTime = float.PositiveInfinity;
    }

    private void OnEnable()
    {
        health ??= GetComponent<HealthSystem>();
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Damaged += OnDamaged;
        }

        RoundManager round = RoundManager.Instance;
        if (round != null)
        {
            round.StateChanged -= OnRoundStateChanged;
            round.StateChanged += OnRoundStateChanged;
            SyncShadowBlinkCooldownWithRound(round.CurrentState);
        }
        else
        {
            ArmShadowBlinkOpeningCooldown();
        }
    }

    private void OnDamaged(HealthSystem damagedHealth, float amount, GameObject attacker)
    {
        lastDamagedAt = Time.time;
    }

    private void OnRoundStateChanged(RoundState state)
    {
        SyncShadowBlinkCooldownWithRound(state);
    }

    private void SyncShadowBlinkCooldownWithRound(RoundState state)
    {
        if (state == RoundState.Preparation || state == RoundState.RoundEnd ||
            state == RoundState.Defused || state == RoundState.Exploded)
        {
            shadowBlinkOpeningStarted = false;
            nextShadowBlinkTime = float.PositiveInfinity;
            return;
        }

        if (!shadowBlinkOpeningStarted)
        {
            ArmShadowBlinkOpeningCooldown();
        }
    }

    private void ArmShadowBlinkOpeningCooldown()
    {
        shadowBlinkOpeningStarted = true;
        nextShadowBlinkTime = Time.time + shadowBlinkOpeningCooldown;
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
            return;
        }

        if (role.SelectedRole == AgentRoleType.Flanker &&
            Time.time >= nextShadowBlinkTime &&
            TryFindShadowBlink(out AgentStats blinkTarget, out Vector3 blinkPosition,
                out string triggerReason))
        {
            ExecuteShadowBlink(blinkTarget, blinkPosition, triggerReason);
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

    private bool TryFindShadowBlink(
        out AgentStats target,
        out Vector3 blinkPosition,
        out string triggerReason)
    {
        target = null;
        blinkPosition = default;
        triggerReason = string.Empty;
        if (stats == null || role == null || role.SelectedRole != AgentRoleType.Flanker)
        {
            return false;
        }

        if (IsTeamShadowBlinkLocked())
        {
            return false;
        }

        bool teamEngaged = IsTeamEngaged();
        bool recentlyDamaged = Time.time - lastDamagedAt <= shadowBlinkRecentDamageWindow;
        bool hasUtilityTarget = TryFindUtilityOwner(out AgentStats utilityOwner);
        float currentSafetyScore = ScoreShadowBlinkPosition(
            transform.position,
            null,
            false,
            out _);
        float bestScore = float.NegativeInfinity;
        string bestReason = string.Empty;

        foreach (AgentStats enemy in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (!IsLivingEnemy(enemy))
            {
                continue;
            }

            ShadowBlinkTrigger trigger = GetShadowBlinkTrigger(
                enemy,
                teamEngaged,
                recentlyDamaged,
                hasUtilityTarget && enemy == utilityOwner);
            if (trigger == ShadowBlinkTrigger.None)
            {
                continue;
            }

            if (IsAlreadyGoodAttackPosition(enemy))
            {
                continue;
            }

            if (!TryFindShadowBlinkDestination(enemy, trigger, out Vector3 candidate,
                    out float candidateScore))
            {
                continue;
            }

            if (recentlyDamaged &&
                candidateScore < currentSafetyScore + shadowBlinkMinImprovement)
            {
                continue;
            }

            float targetScore = ScoreShadowBlinkTarget(enemy, trigger);
            float totalScore = candidateScore + targetScore;
            if (trigger != ShadowBlinkTrigger.TakingDamage &&
                totalScore < shadowBlinkMinimumScore)
            {
                continue;
            }

            if (totalScore > bestScore)
            {
                bestScore = totalScore;
                target = enemy;
                blinkPosition = candidate;
                bestReason = GetShadowBlinkReason(trigger);
            }
        }

        if (target == null)
        {
            return false;
        }

        triggerReason = bestReason;
        return true;
    }

    private bool IsTeamShadowBlinkLocked()
    {
        return stats != null &&
               NextTeamShadowBlinkTime.TryGetValue(stats.team, out float nextAllowed) &&
               Time.time < nextAllowed;
    }

    private void ReserveTeamShadowBlinkWindow()
    {
        if (stats == null)
        {
            return;
        }

        NextTeamShadowBlinkTime[stats.team] = Time.time + shadowBlinkTeamSpacing;
    }

    private enum ShadowBlinkTrigger
    {
        None,
        TeamEngage,
        TakingDamage,
        BacklineTarget,
        Finisher,
        AntiUtility
    }

    private ShadowBlinkTrigger GetShadowBlinkTrigger(
        AgentStats enemy,
        bool teamEngaged,
        bool recentlyDamaged,
        bool utilityOwner)
    {
        HealthSystem enemyHealth = enemy.GetComponent<HealthSystem>();
        if (enemyHealth == null || enemyHealth.IsDead)
        {
            return ShadowBlinkTrigger.None;
        }

        if (!teamEngaged)
        {
            return recentlyDamaged
                ? ShadowBlinkTrigger.TakingDamage
                : ShadowBlinkTrigger.None;
        }

        if (enemyHealth.NormalizedHealth <= shadowBlinkFinisherHealth)
        {
            return ShadowBlinkTrigger.Finisher;
        }

        if (utilityOwner)
        {
            return ShadowBlinkTrigger.AntiUtility;
        }

        if (IsPriorityBacklineTarget(enemy))
        {
            return ShadowBlinkTrigger.BacklineTarget;
        }

        return IsTeamEngageBlinkOpportunity(enemy)
            ? ShadowBlinkTrigger.TeamEngage
            : ShadowBlinkTrigger.None;
    }

    private bool IsTeamEngageBlinkOpportunity(AgentStats enemy)
    {
        return GetEnemyIsolation(enemy) >= 0.55f ||
               IsAttackingSomeoneElse(enemy) ||
               GetSideRearScore(transform.position, enemy) >= 0.45f;
    }

    private bool TryFindShadowBlinkDestination(
        AgentStats target,
        ShadowBlinkTrigger trigger,
        out Vector3 blinkPosition,
        out float score)
    {
        blinkPosition = default;
        score = float.NegativeInfinity;
        if (FlatDistance(transform.position, target.transform.position) >
            shadowBlinkMaxRange)
        {
            return false;
        }

        Vector3 targetForward = target.transform.forward;
        targetForward.y = 0f;
        if (targetForward.sqrMagnitude < 0.01f)
        {
            Vector3 teamCenter = GetTeamCenter(stats.team);
            targetForward = target.transform.position - teamCenter;
            targetForward.y = 0f;
        }

        if (targetForward.sqrMagnitude < 0.01f)
        {
            targetForward = transform.forward;
        }

        targetForward.Normalize();
        Vector3 targetRight = Vector3.Cross(Vector3.up, targetForward).normalized;
        Vector3 awayFromTarget = transform.position - target.transform.position;
        awayFromTarget.y = 0f;
        if (awayFromTarget.sqrMagnitude < 0.01f)
        {
            awayFromTarget = -targetForward;
        }

        awayFromTarget.Normalize();
        Vector3[] directions = trigger == ShadowBlinkTrigger.TakingDamage
            ? new[]
            {
                awayFromTarget,
                (awayFromTarget + targetRight).normalized,
                (awayFromTarget - targetRight).normalized,
                targetRight,
                -targetRight
            }
            : new[]
            {
                -targetForward,
                (-targetForward + targetRight).normalized,
                (-targetForward - targetRight).normalized,
                targetRight,
                -targetRight
            };
        float[] distances =
        {
            shadowBlinkPreferredDistance,
            shadowBlinkPreferredDistance + 0.8f,
            Mathf.Max(1.6f, shadowBlinkPreferredDistance - 0.6f)
        };

        foreach (float distance in distances)
        {
            foreach (Vector3 direction in directions)
            {
                Vector3 candidate = target.transform.position + direction * distance;
                candidate.y = transform.position.y;
                if (!IsValidShadowBlinkDestination(candidate, target))
                {
                    continue;
                }

                float candidateScore = ScoreShadowBlinkPosition(
                    candidate,
                    target,
                    trigger == ShadowBlinkTrigger.TakingDamage,
                    out _);
                if (candidateScore > score)
                {
                    score = candidateScore;
                    blinkPosition = candidate;
                }
            }
        }

        return score > float.NegativeInfinity;
    }

    private bool IsValidShadowBlinkDestination(Vector3 candidate, AgentStats target)
    {
        if (FlatDistance(transform.position, candidate) > shadowBlinkMaxRange)
        {
            return false;
        }

        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        float radius = motor != null
            ? motor.AgentRadius + motor.MinObstacleClearance
            : 0.55f;
        if (pathfinder != null &&
            !pathfinder.IsValidAgentPosition(
                candidate,
                radius,
                gameObject,
                true,
                shadowBlinkAgentClearance))
        {
            return false;
        }

        if (pathfinder == null &&
            Physics.CheckSphere(
                candidate + Vector3.up * 0.55f,
                radius,
                ~0,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (DefenderTeamCoordinator.IsLineBlocked(
                candidate,
                target.transform.position))
        {
            return false;
        }

        float danger = GetDanger(candidate);
        if (danger > shadowBlinkDangerLimit)
        {
            return false;
        }

        return CountLivingEnemiesNear(candidate, shadowBlinkEnemyPileupRadius) <=
               shadowBlinkMaxNearbyEnemies;
    }

    private float ScoreShadowBlinkTarget(
        AgentStats target,
        ShadowBlinkTrigger trigger)
    {
        HealthSystem targetHealth = target.GetComponent<HealthSystem>();
        float weak = targetHealth != null ? 1f - targetHealth.NormalizedHealth : 0f;
        float isolation = GetEnemyIsolation(target);
        float score = weak * 8f + isolation * 4f -
                      FlatDistance(transform.position, target.transform.position) * 0.12f;

        AgentRole targetRole = target.GetComponent<AgentRole>();
        WeaponLoadout targetLoadout = target.GetComponent<WeaponLoadout>();
        if (targetRole != null && targetRole.SelectedRole == AgentRoleType.Support)
        {
            score += 6f;
        }

        if ((targetRole != null && targetRole.SelectedRole == AgentRoleType.Flanker) ||
            (targetLoadout != null && targetLoadout.SelectedWeapon == WeaponType.Sniper))
        {
            score += 4f;
        }

        if (IsAttackingSomeoneElse(target))
        {
            score += 3f;
        }

        switch (trigger)
        {
            case ShadowBlinkTrigger.Finisher:
                score += 8f;
                break;
            case ShadowBlinkTrigger.BacklineTarget:
                score += 5f;
                break;
            case ShadowBlinkTrigger.AntiUtility:
                score += 6f;
                break;
            case ShadowBlinkTrigger.TakingDamage:
                score += 2f;
                break;
        }

        return score;
    }

    private float ScoreShadowBlinkPosition(
        Vector3 position,
        AgentStats target,
        bool escapeBlink,
        out string reason)
    {
        float danger = GetDanger(position);
        int nearbyEnemies = CountLivingEnemiesNear(position, shadowBlinkEnemyPileupRadius);
        float nearestAlly = GetNearestAllyDistance(position);
        float score = -danger * 2.5f - nearbyEnemies * 2.2f;

        if (target != null)
        {
            float distance = FlatDistance(position, target.transform.position);
            float rangeFit = GetShadowBlinkRangeFit(distance);
            score += rangeFit * 5f + GetSideRearScore(position, target) * 4f;
            if (DefenderTeamCoordinator.IsLineBlocked(position, target.transform.position))
            {
                score -= 10f;
            }
        }

        if (escapeBlink)
        {
            score += Mathf.Clamp(nearestAlly, 2f, 9f) * 0.35f;
        }
        else
        {
            score -= Mathf.Abs(nearestAlly - 5.5f) * 0.18f;
        }

        reason = $"danger {danger:0.0}, enemies {nearbyEnemies}";
        return score;
    }

    private void ExecuteShadowBlink(
        AgentStats target,
        Vector3 blinkPosition,
        string triggerReason)
    {
        Vector3 start = transform.position;
        motor?.Stop();
        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = blinkPosition;
        }

        transform.position = blinkPosition;
        motor?.FacePosition(target.transform.position);
        Physics.SyncTransforms();
        nextShadowBlinkTime = Time.time + shadowBlinkCooldown;
        ReserveTeamShadowBlinkWindow();
        abilityMessageUntil = Time.time + 1.2f;
        healthBar?.SetAbilityStatus(
            "SHADOW BLINK",
            0f,
            new Color(0.86f, 0.24f, 1f, 1f));
        SpawnShadowBlinkTrail(start, blinkPosition);
        Debug.Log($"{name} used Shadow Blink ({triggerReason}) near {target.name}.");
    }

    private bool IsTeamEngaged()
    {
        int alliesAttacking = 0;
        foreach (AgentBrain brain in
                 FindObjectsByType<AgentBrain>(FindObjectsInactive.Exclude))
        {
            AgentStats ally = brain.GetComponent<AgentStats>();
            if (ally == null || ally.team != stats.team)
            {
                continue;
            }

            GameObject currentTarget = brain.CurrentTarget;
            if (currentTarget != null &&
                CombatTargetUtility.TryGetTeam(currentTarget, out TeamType team) &&
                team != stats.team &&
                CombatTargetUtility.IsAlive(currentTarget))
            {
                alliesAttacking++;
            }
        }

        if (alliesAttacking >= 2)
        {
            return true;
        }

        foreach (AgentRole allyRole in
                 FindObjectsByType<AgentRole>(FindObjectsInactive.Exclude))
        {
            AgentStats ally = allyRole.GetComponent<AgentStats>();
            AgentBrain brain = allyRole.GetComponent<AgentBrain>();
            if (ally != null && brain != null && ally.team == stats.team &&
                allyRole.SelectedRole == AgentRoleType.Assaulter &&
                brain.CurrentTarget != null &&
                CombatTargetUtility.IsAlive(brain.CurrentTarget))
            {
                return true;
            }
        }

        if (alliesAttacking > 0)
        {
            Vector3 teamCenter = GetTeamCenter(stats.team);
            foreach (AgentStats enemy in
                     FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
            {
                if (IsLivingEnemy(enemy) &&
                    FlatDistance(enemy.transform.position, teamCenter) <=
                    shadowBlinkCombatRange)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsPriorityBacklineTarget(AgentStats enemy)
    {
        HealthSystem enemyHealth = enemy.GetComponent<HealthSystem>();
        if (enemyHealth == null || enemyHealth.IsDead)
        {
            return false;
        }

        AgentRole enemyRole = enemy.GetComponent<AgentRole>();
        WeaponLoadout enemyLoadout = enemy.GetComponent<WeaponLoadout>();
        return enemyHealth.NormalizedHealth <= 0.55f ||
               GetEnemyIsolation(enemy) >= 0.6f ||
               IsAttackingSomeoneElse(enemy) ||
               (enemyRole != null && enemyRole.SelectedRole == AgentRoleType.Support) ||
               (enemyLoadout != null && enemyLoadout.SelectedWeapon == WeaponType.Sniper);
    }

    private bool TryFindUtilityOwner(out AgentStats owner)
    {
        owner = null;
        float bestDistance = Mathf.Infinity;
        foreach (DeployableTurret turret in
                 FindObjectsByType<DeployableTurret>(FindObjectsInactive.Exclude))
        {
            if (turret == null || turret.IsDestroyed || turret.Team == stats.team)
            {
                continue;
            }

            AgentStats candidate = FindNearestLivingEnemyRole(
                turret.transform.position,
                AgentRoleType.Assaulter);
            if (candidate != null)
            {
                float distance = FlatDistance(transform.position,
                    candidate.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    owner = candidate;
                }
            }
        }

        foreach (DeployedDefenderWall wall in
                 FindObjectsByType<DeployedDefenderWall>(FindObjectsInactive.Exclude))
        {
            if (wall == null || wall.Team == stats.team)
            {
                continue;
            }

            AgentStats candidate = FindNearestLivingEnemyRole(
                wall.transform.position,
                AgentRoleType.Defender);
            if (candidate != null)
            {
                float distance = FlatDistance(transform.position,
                    candidate.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    owner = candidate;
                }
            }
        }

        return owner != null;
    }

    private AgentStats FindNearestLivingEnemyRole(
        Vector3 position,
        AgentRoleType requiredRole)
    {
        AgentStats best = null;
        float bestDistance = Mathf.Infinity;
        foreach (AgentStats candidate in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            AgentRole candidateRole = candidate.GetComponent<AgentRole>();
            if (!IsLivingEnemy(candidate) || candidateRole == null ||
                candidateRole.SelectedRole != requiredRole)
            {
                continue;
            }

            float distance = FlatDistance(position, candidate.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private bool IsAlreadyGoodAttackPosition(AgentStats target)
    {
        float distance = FlatDistance(transform.position, target.transform.position);
        WeaponLoadout loadout = WeaponLoadout.Get(gameObject);
        float maxRange = loadout != null ? loadout.MaximumRange : stats.attackRange;
        return distance <= Mathf.Min(maxRange, shadowBlinkPreferredDistance + 1.3f) &&
               GetSideRearScore(transform.position, target) >= 0.65f &&
               GetDanger(transform.position) <= shadowBlinkDangerLimit * 0.65f;
    }

    private bool IsLivingEnemy(AgentStats candidate)
    {
        if (candidate == null || candidate == stats || candidate.team == stats.team)
        {
            return false;
        }

        HealthSystem candidateHealth = candidate.GetComponent<HealthSystem>();
        return candidateHealth != null && !candidateHealth.IsDead;
    }

    private float GetDanger(Vector3 position)
    {
        float danger = InfluenceMapManager.Instance != null
            ? InfluenceMapManager.Instance.Sample(InfluenceLayerType.Danger, position)
            : 0f;
        return danger + CountLivingEnemiesNear(position, 3.5f) * 0.9f;
    }

    private int CountLivingEnemiesNear(Vector3 position, float radius)
    {
        int count = 0;
        foreach (AgentStats enemy in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (IsLivingEnemy(enemy) &&
                FlatDistance(position, enemy.transform.position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    private float GetNearestAllyDistance(Vector3 position)
    {
        float nearest = Mathf.Infinity;
        foreach (AgentStats ally in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (ally == null || ally == stats || ally.team != stats.team)
            {
                continue;
            }

            HealthSystem allyHealth = ally.GetComponent<HealthSystem>();
            if (allyHealth == null || allyHealth.IsDead)
            {
                continue;
            }

            nearest = Mathf.Min(nearest, FlatDistance(position, ally.transform.position));
        }

        return float.IsInfinity(nearest) ? 8f : nearest;
    }

    private float GetEnemyIsolation(AgentStats enemy)
    {
        int nearbyAllies = 0;
        foreach (AgentStats other in
                 FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (other == null || other == enemy || other.team != enemy.team)
            {
                continue;
            }

            HealthSystem otherHealth = other.GetComponent<HealthSystem>();
            if (otherHealth == null || otherHealth.IsDead)
            {
                continue;
            }

            if (FlatDistance(enemy.transform.position, other.transform.position) <= 5f)
            {
                nearbyAllies++;
            }
        }

        return nearbyAllies == 0 ? 1f : nearbyAllies == 1 ? 0.55f : 0f;
    }

    private bool IsAttackingSomeoneElse(AgentStats enemy)
    {
        AgentBrain brain = enemy.GetComponent<AgentBrain>();
        if (brain == null || brain.CurrentTarget == null)
        {
            return false;
        }

        GameObject currentTarget = CombatTargetUtility.GetRoot(brain.CurrentTarget);
        return currentTarget != null && currentTarget != gameObject &&
               CombatTargetUtility.TryGetTeam(currentTarget, out TeamType targetTeam) &&
               targetTeam == stats.team &&
               CombatTargetUtility.IsAlive(currentTarget);
    }

    private float GetSideRearScore(Vector3 position, AgentStats target)
    {
        Vector3 targetToPosition = position - target.transform.position;
        targetToPosition.y = 0f;
        Vector3 forward = target.transform.forward;
        forward.y = 0f;
        if (targetToPosition.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f)
        {
            return 0f;
        }

        float dot = Vector3.Dot(forward.normalized, targetToPosition.normalized);
        return Mathf.Clamp01((-dot + 1f) * 0.5f);
    }

    private float GetShadowBlinkRangeFit(float distance)
    {
        WeaponLoadout loadout = WeaponLoadout.Get(gameObject);
        float minimum = loadout != null ? loadout.MinimumRange : 0f;
        float maximum = loadout != null ? loadout.MaximumRange : stats.attackRange;
        float preferred = Mathf.Clamp(
            shadowBlinkPreferredDistance,
            minimum + 0.2f,
            Mathf.Max(minimum + 0.3f, maximum - 0.2f));
        return Mathf.Clamp01(1f - Mathf.Abs(distance - preferred) /
            Mathf.Max(0.2f, maximum - minimum));
    }

    private static string GetShadowBlinkReason(ShadowBlinkTrigger trigger)
    {
        switch (trigger)
        {
            case ShadowBlinkTrigger.TeamEngage:
                return "team engage";
            case ShadowBlinkTrigger.TakingDamage:
                return "taking damage";
            case ShadowBlinkTrigger.BacklineTarget:
                return "backline target";
            case ShadowBlinkTrigger.Finisher:
                return "finisher";
            case ShadowBlinkTrigger.AntiUtility:
                return "anti-utility";
            default:
                return "tactical";
        }
    }

    private void SpawnShadowBlinkTrail(Vector3 start, Vector3 end)
    {
        GameObject trailObject = new GameObject("Shadow Blink Trail");
        LineRenderer line = trailObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start + Vector3.up * 0.8f);
        line.SetPosition(1, end + Vector3.up * 0.8f);
        line.startWidth = 0.16f;
        line.endWidth = 0.04f;
        line.startColor = new Color(0.86f, 0.24f, 1f, 0.75f);
        line.endColor = new Color(0.25f, 0.04f, 0.35f, 0.1f);
        line.sharedMaterial = GetShadowBlinkMaterial();
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        Destroy(trailObject, 0.35f);
    }

    private static Material GetShadowBlinkMaterial()
    {
        if (shadowBlinkMaterial != null)
        {
            return shadowBlinkMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        shadowBlinkMaterial = new Material(shader)
        {
            name = "Shadow Blink Material",
            color = new Color(0.86f, 0.24f, 1f, 0.8f)
        };
        return shadowBlinkMaterial;
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
        deployed.Initialize(stats.team, wallLifetime);
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

        RoundManager round = RoundManager.Instance;
        if (round != null)
        {
            round.StateChanged -= OnRoundStateChanged;
        }

        CancelHealing(false);
        CancelTurretInstallation(false);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawAbilityRangeGizmos)
        {
            return;
        }

        AgentRole activeRole = role != null ? role : GetComponent<AgentRole>();
        AgentRoleType selectedRole = activeRole != null
            ? activeRole.SelectedRole
            : AgentRoleType.Flanker;
        Vector3 origin = transform.position + Vector3.up * 0.08f;

        if (ShouldDrawAbilityRange(selectedRole, AgentRoleType.Support))
        {
            DrawRangeGizmo(origin, healRange, new Color(0.15f, 1f, 0.35f, 0.85f));
        }

        if (ShouldDrawAbilityRange(selectedRole, AgentRoleType.Defender))
        {
            DrawRangeGizmo(origin, wallThreatRange, new Color(0.2f, 0.55f, 1f, 0.85f));
        }

        if (ShouldDrawAbilityRange(selectedRole, AgentRoleType.Assaulter))
        {
            DrawRangeGizmo(origin, turretRange, new Color(1f, 0.55f, 0.1f, 0.9f));
            DrawRangeGizmo(origin, turretThreatRange, new Color(1f, 0.2f, 0.1f, 0.45f));
        }

        if (ShouldDrawAbilityRange(selectedRole, AgentRoleType.Flanker))
        {
            DrawRangeGizmo(origin, shadowBlinkMaxRange, new Color(0.86f, 0.24f, 1f, 0.9f));
        }
    }

    private bool ShouldDrawAbilityRange(
        AgentRoleType selectedRole,
        AgentRoleType abilityRole)
    {
        return !drawOnlyCurrentRoleAbility || selectedRole == abilityRole;
    }

    private static void DrawRangeGizmo(Vector3 origin, float radius, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(origin, Mathf.Max(0f, radius));
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
    [SerializeField] private TeamType team;
    private float expiresAt;
    private bool expiring;

    public TeamType Team => team;

    public void Initialize(TeamType ownerTeam, float lifetime)
    {
        team = ownerTeam;
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
