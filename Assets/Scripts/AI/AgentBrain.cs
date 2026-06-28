using System.Collections.Generic;
using System.Text;
using UnityEngine;

[RequireComponent(typeof(AgentStats), typeof(AgentPerception), typeof(AgentMovement))]
[RequireComponent(typeof(UtilityEvaluator), typeof(WeaponSystem), typeof(HealthSystem))]
public class AgentBrain : MonoBehaviour
{
    [Header("Decision Timing")]
    [Range(0.1f, 1f)] public float evaluationInterval = 0.35f;
    [Min(0f)] public float commitmentTime = 1f;

    [Header("Tactical Movement")]
    public float maximumCoverDistance = 6f;
    [Min(0f)] public float minimumRepositionDistance = 1f;
    [Min(0f)] public float minimumRetreatDistance = 2f;

    [Header("Search")]
    [Min(0f)] public float minimumSearchDistance = 5f;
    [Min(0.1f)] public float searchArrivalDistance = 0.75f;
    [Min(0f)] public float sharedIntelLifetime = 12f;

    [Header("Debug")]
    public bool showDecisionLabel = true;
    public AIActionType CurrentAction { get; private set; } = AIActionType.Reposition;
    public float CurrentScore { get; private set; }

    private AgentStats stats;
    private AgentPerception perception;
    private AgentMovement movement;
    private UtilityEvaluator evaluator;
    private WeaponSystem weapon;
    private HealthSystem health;
    private InfluenceMap influenceMap;
    private AgentStats currentTarget;
    private CoverPoint claimedCover;
    private List<UtilityDecision> lastScores = new List<UtilityDecision>();
    private float nextEvaluationTime;
    private float committedUntil;
    private bool hasInvestigatedLastKnownPosition;
    private Vector2 investigatedLastKnownPosition;

    private void Start()
    {
        stats = GetComponent<AgentStats>();
        perception = GetComponent<AgentPerception>();
        movement = GetComponent<AgentMovement>();
        evaluator = GetComponent<UtilityEvaluator>();
        weapon = GetComponent<WeaponSystem>();
        health = GetComponent<HealthSystem>();

        influenceMap = FindAnyObjectByType<InfluenceMap>();
        if (influenceMap == null && AStarPathfinder.Instance != null)
        {
            influenceMap = AStarPathfinder.Instance.gameObject.AddComponent<InfluenceMap>();
        }

        EvaluateNow(true);
    }

    private void Update()
    {
        if (health.IsDead)
        {
            return;
        }

        if (Time.time >= nextEvaluationTime)
        {
            EvaluateNow(false);
        }

        ExecuteCurrentAction();
    }

    private void EvaluateNow(bool force)
    {
        nextEvaluationTime = Time.time + evaluationInterval;
        List<AgentStats> visibleEnemies = perception.GetVisibleEnemies();
        currentTarget = SelectClosest(visibleEnemies);
        lastScores = evaluator.Evaluate(currentTarget, visibleEnemies, health, weapon);

        UtilityDecision best = lastScores[0];
        foreach (UtilityDecision candidate in lastScores)
        {
            if (candidate.score > best.score)
            {
                best = candidate;
            }
        }

        if (!force && Time.time < committedUntil && best.action != CurrentAction)
        {
            // Check for emergency overrides
            bool isEmergency = currentTarget == null ||
                               weapon.currentAmmo == 0 ||
                               health.HealthNormalized <= 0.2f;
            if (!isEmergency)
            {
                // We are committed to CurrentAction. If we don't have a destination, reissue!
                if (!movement.HasDestination)
                {
                    ReissueMovement(CurrentAction);
                }
                return;
            }
        }

        if (force || best.action != CurrentAction)
        {
            ChangeAction(best);
        }
        else
        {
            CurrentScore = best.score;
            if (!movement.HasDestination)
            {
                ReissueMovement(CurrentAction);
            }
        }
    }

    private void ReissueMovement(AIActionType action)
    {
        switch (action)
        {
            case AIActionType.Retreat:
                MoveUsingInfluence(true);
                break;

            case AIActionType.Reposition:
                MoveUsingInfluence(false);
                break;

            case AIActionType.SearchEnemy:
                IssueSearchMovement();
                break;

            case AIActionType.CreateDistance:
                MoveUsingInfluence(true);
                break;

            case AIActionType.TakeCover:
                if (claimedCover == null)
                {
                    SelectCover();
                }
                else
                {
                    movement.MoveTo(claimedCover.Position);
                }
                break;
        }
    }

    private void ChangeAction(UtilityDecision decision)
    {
        if (decision.action != AIActionType.TakeCover)
        {
            ReleaseCover();
        }

        CurrentAction = decision.action;
        CurrentScore = decision.score;
        committedUntil = Time.time + commitmentTime;

        switch (CurrentAction)
        {
            case AIActionType.Reload:
                movement.Stop();
                weapon.BeginReload();
                break;

            case AIActionType.TakeCover:
                SelectCover();
                break;

            case AIActionType.Retreat:
                MoveUsingInfluence(true);
                break;

            case AIActionType.Reposition:
                MoveUsingInfluence(false);
                break;

            case AIActionType.SearchEnemy:
                IssueSearchMovement();
                break;

            case AIActionType.ApproachEnemy:
                if (currentTarget != null)
                {
                    movement.MoveTo(currentTarget.transform.position);
                }
                break;

            case AIActionType.HoldRange:
                movement.Stop();
                break;

            case AIActionType.CreateDistance:
                MoveUsingInfluence(true);
                break;
        }
    }

    private void ExecuteCurrentAction()
    {
        switch (CurrentAction)
        {
            case AIActionType.Attack:
                ExecuteAttack();
                break;

            case AIActionType.TakeCover:
                if (claimedCover != null)
                {
                    movement.MoveTo(claimedCover.Position);
                }
                TryShootWhileMoving();
                break;

            case AIActionType.Retreat:
                TryShootWhileMoving();
                break;

            case AIActionType.Reload:
                if (!weapon.IsReloading && weapon.currentAmmo < weapon.magazineSize)
                {
                    weapon.BeginReload();
                }
                break;

            case AIActionType.SearchEnemy:
                if (!movement.HasDestination ||
                    Vector2.Distance(transform.position, movement.Destination) <= searchArrivalDistance)
                {
                    IssueSearchMovement();
                }
                break;

            case AIActionType.ApproachEnemy:
                if (currentTarget != null)
                {
                    movement.Face(currentTarget.transform.position);
                    float distance = Vector2.Distance(transform.position, currentTarget.transform.position);
                    if (distance <= stats.attackRange)
                    {
                        movement.Stop();
                        weapon.TryAttack(currentTarget.gameObject);
                    }
                    else
                    {
                        movement.MoveTo(currentTarget.transform.position);
                    }
                }
                break;

            case AIActionType.HoldRange:
                if (currentTarget != null)
                {
                    movement.Face(currentTarget.transform.position);
                    float distance = Vector2.Distance(transform.position, currentTarget.transform.position);
                    if (distance <= stats.attackRange)
                    {
                        movement.Stop();
                        weapon.TryAttack(currentTarget.gameObject);
                    }
                    else
                    {
                        // Defensive fallback: never hold at a position where the
                        // equipped weapon cannot actually attack the target.
                        movement.MoveTo(currentTarget.transform.position);
                    }
                }
                break;

            case AIActionType.CreateDistance:
                if (currentTarget != null)
                {
                    movement.Face(currentTarget.transform.position);
                    float distance = Vector2.Distance(transform.position, currentTarget.transform.position);
                    if (distance <= stats.attackRange)
                    {
                        weapon.TryAttack(currentTarget.gameObject);
                    }
                }
                break;
        }
    }

    private void ExecuteAttack()
    {
        if (currentTarget == null || !perception.CanSeeEnemy(currentTarget))
        {
            return;
        }

        movement.Face(currentTarget.transform.position);
        float distance = Vector2.Distance(transform.position, currentTarget.transform.position);

        if (distance <= stats.attackRange)
        {
            movement.Stop();
            weapon.TryAttack(currentTarget.gameObject);
        }
        else
        {
            movement.MoveTo(currentTarget.transform.position);
        }
    }

    private void TryShootWhileMoving()
    {
        if (currentTarget == null || weapon.IsReloading || weapon.currentAmmo <= 0)
        {
            return;
        }

        float distance = Vector2.Distance(transform.position, currentTarget.transform.position);
        if (distance > stats.attackRange || !perception.CanSeeEnemy(currentTarget))
        {
            return;
        }

        // Face and fire, but deliberately do not call movement.Stop().
        movement.Face(currentTarget.transform.position);
        weapon.TryAttack(currentTarget.gameObject);
    }

    private void SelectCover()
    {
        ReleaseCover();
        CoverPoint candidate = CoverPoint.FindBest(this, currentTarget, maximumCoverDistance);

        if (candidate != null && candidate.TryClaim(this))
        {
            claimedCover = candidate;
            movement.MoveTo(candidate.Position);
            return;
        }

        // If no authored CoverPoint is available, use only an influence-map
        // cell where a wall actually blocks the enemy's line of sight.
        if (influenceMap != null &&
            influenceMap.TryGetBestCoverPosition(stats, transform.position, out Vector2 coverPosition))
        {
            movement.MoveTo(coverPosition);
            return;
        }

        // No real cover exists for the current enemy direction. A general
        // safety move is preferable to falsely treating an exposed wall as cover.
        MoveUsingInfluence(true);
    }

    private void MoveUsingInfluence(bool preferSafety)
    {
        if (influenceMap == null)
        {
            return;
        }

        float minimumMoveDistance = preferSafety
            ? minimumRetreatDistance
            : minimumRepositionDistance;
        Vector2 destination = influenceMap.GetBestTacticalPosition(
            stats,
            transform.position,
            preferSafety,
            minimumMoveDistance);

        // No useful destination was found. Do not leave a fake movement order
        // active for the current cell.
        if (Vector2.Distance(transform.position, destination) < 0.1f)
        {
            movement.Stop();
            return;
        }

        movement.MoveTo(destination);
    }

    private void IssueSearchMovement()
    {
        // Investigate real remembered information before starting a blind patrol.
        bool hasSharedIntel = TeamKnowledge.TryGetLatestEnemyPosition(
            stats.team,
            sharedIntelLifetime,
            out Vector2 sharedPosition,
            out float sharedObservationTime);
        bool hasPersonalIntel = perception.HasLastKnownEnemyPosition;

        if (hasPersonalIntel || hasSharedIntel)
        {
            Vector2 lastKnown = hasPersonalIntel &&
                (!hasSharedIntel || perception.LastKnownEnemyTime >= sharedObservationTime)
                ? perception.LastKnownEnemyPosition
                : sharedPosition;
            bool isNewInformation = !hasInvestigatedLastKnownPosition ||
                Vector2.Distance(lastKnown, investigatedLastKnownPosition) > 0.25f;

            if (isNewInformation)
            {
                if (Vector2.Distance(transform.position, lastKnown) > searchArrivalDistance)
                {
                    movement.MoveTo(lastKnown);
                    return;
                }

                investigatedLastKnownPosition = lastKnown;
                hasInvestigatedLastKnownPosition = true;
            }
        }

        if (AStarPathfinder.Instance != null &&
            AStarPathfinder.Instance.TryGetRandomWalkablePosition(
                transform.position,
                minimumSearchDistance,
                out Vector2 searchPosition))
        {
            movement.MoveTo(searchPosition);
        }
        else
        {
            movement.Stop();
        }
    }

    private AgentStats SelectClosest(List<AgentStats> enemies)
    {
        AgentStats closest = null;
        float closestDistance = Mathf.Infinity;

        foreach (AgentStats enemy in enemies)
        {
            float distance = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = enemy;
            }
        }

        return closest;
    }

    private void ReleaseCover()
    {
        if (claimedCover != null)
        {
            claimedCover.Release(this);
            claimedCover = null;
        }
    }

    private void OnDisable()
    {
        ReleaseCover();
    }

    private void OnGUI()
    {
        if (!showDecisionLabel || Camera.main == null || health == null || health.IsDead)
        {
            return;
        }

        Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.3f);
        if (screen.z < 0f)
        {
            return;
        }

        AgentPersonality personality = GetComponent<AgentPersonality>();
        float agg = personality != null ? personality.aggressiveness : 0.5f;
        float prefDist = personality != null ? personality.preferredCombatDistance : 3.0f;

        float dist = currentTarget != null ? Vector2.Distance(transform.position, currentTarget.transform.position) : 0f;
        string distStr = currentTarget != null ? $"{dist:F1}m" : "N/A";

        StringBuilder label = new StringBuilder();
        label.AppendLine($"Action: {CurrentAction} ({CurrentScore:0.00})");
        label.AppendLine($"Dist: {distStr} / Pref: {prefDist:F1}m");
        label.AppendLine($"Aggressiveness: {agg:F2}");
        label.Append($"HP: {health.CurrentHealth:F0} | Ammo: {weapon.currentAmmo}/{weapon.magazineSize}");

        GUI.Box(new Rect(screen.x - 80f, Screen.height - screen.y - 45f, 160f, 75f), "");
        GUI.Label(new Rect(screen.x - 75f, Screen.height - screen.y - 40f, 150f, 70f), label.ToString());
    }
}
