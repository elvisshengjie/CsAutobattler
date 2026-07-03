using System.Collections.Generic;
using UnityEngine;

public enum TacticalSlotKind
{
    Center,
    Left,
    Right,
    FrontLeft,
    FrontRight,
    BackLeft,
    BackRight,
    Rear,
    Extended
}

/// <summary>
/// Owns final standing slots so independent tactics cannot send several agents
/// to the same point. The tactical systems still choose the objective; this
/// component only makes the final position safe and unique.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class PositionReservationManager : MonoBehaviour
{
    private struct Reservation
    {
        public Vector3 requestedPosition;
        public Vector3 position;
        public float radius;
        public TacticalSlotKind slotKind;
    }

    private struct SlotCandidate
    {
        public Vector3 position;
        public TacticalSlotKind kind;
        public int preference;
    }

    public static PositionReservationManager Instance { get; private set; }

    [SerializeField] private bool drawReservedSlots = true;
    [SerializeField] private float slotSpacingMultiplier = 1.5f;
    [SerializeField] private float objectiveMatchDistance = 0.35f;
    [SerializeField] private int fallbackAngularSamples = 12;

    private readonly Dictionary<AgentMotor, Reservation> reservations =
        new Dictionary<AgentMotor, Reservation>();
    private readonly List<AgentMotor> staleOwners = new List<AgentMotor>();

    public static PositionReservationManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        PositionReservationManager existing =
            FindAnyObjectByType<PositionReservationManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject host = new GameObject("Position Reservation Manager");
        return host.AddComponent<PositionReservationManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void LateUpdate()
    {
        RemoveStaleReservations();
    }

    public bool TryReserveNearest(
        AgentMotor owner,
        Vector3 desiredPosition,
        float minimumSeparation,
        float searchRadius,
        out Vector3 reservedPosition)
    {
        Vector3 approach = owner != null
            ? desiredPosition - owner.transform.position
            : Vector3.forward;
        return TryReserveTacticalSlot(
            owner,
            desiredPosition,
            approach,
            minimumSeparation,
            searchRadius,
            false,
            out reservedPosition,
            out _);
    }

    public bool TryReserveTacticalSlot(
        AgentMotor owner,
        Vector3 desiredPosition,
        Vector3 approachDirection,
        float minimumSeparation,
        float searchRadius,
        bool forceReassignment,
        out Vector3 reservedPosition,
        out TacticalSlotKind slotKind)
    {
        reservedPosition = desiredPosition;
        slotKind = TacticalSlotKind.Center;
        if (owner == null)
        {
            return false;
        }

        RemoveStaleReservations();
        minimumSeparation = Mathf.Max(0.1f, minimumSeparation);
        searchRadius = Mathf.Max(0f, searchRadius);

        bool hadExisting = reservations.TryGetValue(owner, out Reservation existing);
        if (!forceReassignment && hadExisting &&
            FlatDistance(existing.requestedPosition, desiredPosition) <=
            objectiveMatchDistance &&
            IsCandidateStaticallyValid(owner, existing.position, minimumSeparation))
        {
            reservedPosition = existing.position;
            slotKind = existing.slotKind;
            return true;
        }

        reservations.Remove(owner);
        List<SlotCandidate> candidates = BuildTacticalSlots(
            owner,
            desiredPosition,
            approachDirection,
            minimumSeparation,
            searchRadius);
        List<SlotCandidate> available = CollectAvailableSlots(
            owner,
            candidates,
            minimumSeparation,
            forceReassignment,
            hadExisting,
            existing,
            true);
        // A teammate merely crossing a slot must not make the objective fail.
        // If every geometrically valid slot is temporarily occupied, reserve
        // one anyway and let runtime separation/queueing resolve the traffic.
        if (available.Count == 0)
        {
            available = CollectAvailableSlots(
                owner,
                candidates,
                minimumSeparation,
                forceReassignment,
                hadExisting,
                existing,
                false);
        }

        available.Sort((left, right) =>
        {
            float leftScore = left.preference * 10f +
                              FlatDistance(owner.transform.position, left.position) * 0.05f;
            float rightScore = right.preference * 10f +
                               FlatDistance(owner.transform.position, right.position) * 0.05f;
            return leftScore.CompareTo(rightScore);
        });

        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        foreach (SlotCandidate candidate in available)
        {
            if (pathfinder != null &&
                pathfinder.FindPath(owner.transform.position, candidate.position) == null)
            {
                continue;
            }

            reservations[owner] = new Reservation
            {
                requestedPosition = desiredPosition,
                position = candidate.position,
                radius = minimumSeparation,
                slotKind = candidate.kind
            };
            reservedPosition = candidate.position;
            slotKind = candidate.kind;
            return true;
        }

        // Never discard a still-valid assignment just because no better slot
        // was reachable this frame. Losing it leaves the motor with no action.
        if (hadExisting &&
            IsCandidateStaticallyValid(owner, existing.position, existing.radius))
        {
            reservations[owner] = existing;
            reservedPosition = existing.position;
            slotKind = existing.slotKind;
            return true;
        }

        return false;
    }

    public bool IsReservationValid(AgentMotor owner)
    {
        if (owner == null || !reservations.TryGetValue(owner, out Reservation reservation))
        {
            return false;
        }

        return IsCandidateStaticallyValid(
            owner,
            reservation.position,
            reservation.radius);
    }

    public bool TryGetReservedSlot(
        AgentMotor owner,
        out Vector3 position,
        out TacticalSlotKind kind)
    {
        if (owner != null && reservations.TryGetValue(owner, out Reservation reservation))
        {
            position = reservation.position;
            kind = reservation.slotKind;
            return true;
        }

        position = default;
        kind = TacticalSlotKind.Center;
        return false;
    }

    private List<SlotCandidate> BuildTacticalSlots(
        AgentMotor owner,
        Vector3 objective,
        Vector3 approachDirection,
        float minimumSeparation,
        float searchRadius)
    {
        approachDirection.y = 0f;
        if (approachDirection.sqrMagnitude < 0.01f)
        {
            approachDirection = objective - owner.transform.position;
            approachDirection.y = 0f;
        }
        if (approachDirection.sqrMagnitude < 0.01f)
        {
            approachDirection = owner.transform.forward;
            approachDirection.y = 0f;
        }

        Vector3 forward = approachDirection.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 back = -forward;
        float spacing = minimumSeparation * Mathf.Max(0.75f, slotSpacingMultiplier);
        List<SlotCandidate> result = new List<SlotCandidate>();
        AddSlot(result, objective, Vector3.zero, TacticalSlotKind.Center, 0, searchRadius);
        AddSlot(result, objective, -right * spacing, TacticalSlotKind.Left, 1, searchRadius);
        AddSlot(result, objective, right * spacing, TacticalSlotKind.Right, 2, searchRadius);
        AddSlot(
            result,
            objective,
            (forward - right) * spacing,
            TacticalSlotKind.FrontLeft,
            3,
            searchRadius);
        AddSlot(
            result,
            objective,
            (forward + right) * spacing,
            TacticalSlotKind.FrontRight,
            4,
            searchRadius);
        AddSlot(
            result,
            objective,
            (back - right) * spacing,
            TacticalSlotKind.BackLeft,
            5,
            searchRadius);
        AddSlot(
            result,
            objective,
            (back + right) * spacing,
            TacticalSlotKind.BackRight,
            6,
            searchRadius);
        AddSlot(result, objective, back * spacing, TacticalSlotKind.Rear, 7, searchRadius);

        int extendedRings = Mathf.FloorToInt(searchRadius / spacing);
        for (int ring = 2; ring <= extendedRings; ring++)
        {
            float distance = ring * spacing;
            int samples = Mathf.Max(8, fallbackAngularSamples);
            for (int sample = 0; sample < samples; sample++)
            {
                Vector3 direction = Quaternion.Euler(
                    0f,
                    sample * 360f / samples,
                    0f) * forward;
                AddSlot(
                    result,
                    objective,
                    direction * distance,
                    TacticalSlotKind.Extended,
                    8 + ring * samples + sample,
                    searchRadius);
            }
        }

        return result;
    }

    private static void AddSlot(
        List<SlotCandidate> slots,
        Vector3 objective,
        Vector3 offset,
        TacticalSlotKind kind,
        int preference,
        float searchRadius)
    {
        if (offset.magnitude > searchRadius + 0.01f && offset.sqrMagnitude > 0.001f)
        {
            return;
        }

        Vector3 position = objective + offset;
        position.y = objective.y;
        slots.Add(new SlotCandidate
        {
            position = position,
            kind = kind,
            preference = preference
        });
    }

    private List<SlotCandidate> CollectAvailableSlots(
        AgentMotor owner,
        List<SlotCandidate> candidates,
        float minimumSeparation,
        bool forceReassignment,
        bool hadExisting,
        Reservation existing,
        bool includeCurrentAgents)
    {
        List<SlotCandidate> available = new List<SlotCandidate>();
        foreach (SlotCandidate candidate in candidates)
        {
            if (forceReassignment && hadExisting &&
                FlatDistance(candidate.position, existing.position) <
                minimumSeparation * 0.5f)
            {
                continue;
            }

            bool valid = includeCurrentAgents
                ? IsCandidateAvailable(owner, candidate.position, minimumSeparation)
                : IsCandidateStaticallyValid(owner, candidate.position, minimumSeparation);
            if (valid)
            {
                available.Add(candidate);
            }
        }

        return available;
    }

    public bool IsReservedByOther(
        AgentMotor owner,
        Vector3 position,
        float minimumSeparation)
    {
        foreach (KeyValuePair<AgentMotor, Reservation> pair in reservations)
        {
            if (pair.Key == null || pair.Key == owner)
            {
                continue;
            }

            float required = Mathf.Max(minimumSeparation, pair.Value.radius);
            if (FlatDistance(pair.Value.position, position) < required)
            {
                return true;
            }
        }

        return false;
    }

    public void Release(AgentMotor owner)
    {
        if (owner != null)
        {
            reservations.Remove(owner);
        }
    }

    private bool IsCandidateAvailable(
        AgentMotor owner,
        Vector3 candidate,
        float minimumSeparation)
    {
        if (!IsCandidateStaticallyValid(owner, candidate, minimumSeparation))
        {
            return false;
        }

        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder == null)
        {
            Collider[] overlaps = Physics.OverlapSphere(
                candidate + Vector3.up * 0.55f,
                owner.AgentRadius,
                ~0,
                QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                AgentStats other = overlap != null
                    ? overlap.GetComponentInParent<AgentStats>()
                    : null;
                if (other == null || other.gameObject == owner.gameObject)
                {
                    continue;
                }

                HealthSystem health = other.GetComponent<HealthSystem>();
                if (health == null || !health.IsDead)
                {
                    return false;
                }
            }

            return true;
        }

        if (!pathfinder.IsValidAgentPosition(
                candidate,
                owner.AgentRadius + owner.MinObstacleClearance,
                owner.gameObject,
                true,
                owner.AgentRadius))
        {
            return false;
        }

        return true;
    }

    private bool IsCandidateStaticallyValid(
        AgentMotor owner,
        Vector3 candidate,
        float minimumSeparation)
    {
        if (owner == null ||
            IsReservedByOther(owner, candidate, minimumSeparation))
        {
            return false;
        }

        AStarPathfinder3D pathfinder = AStarPathfinder3D.Instance;
        if (pathfinder != null)
        {
            return pathfinder.IsValidAgentPosition(
                candidate,
                owner.AgentRadius + owner.MinObstacleClearance,
                owner.gameObject,
                false);
        }

        int obstacleMask = LayerMask.GetMask(
            "Obstacle3D",
            "Obstacle",
            "Wall",
            "Cover");
        return obstacleMask == 0 || !Physics.CheckSphere(
            candidate + Vector3.up * 0.55f,
            owner.AgentRadius + owner.MinObstacleClearance,
            obstacleMask,
            QueryTriggerInteraction.Ignore);
    }

    private void RemoveStaleReservations()
    {
        staleOwners.Clear();
        foreach (KeyValuePair<AgentMotor, Reservation> pair in reservations)
        {
            if (pair.Key == null || !pair.Key.isActiveAndEnabled)
            {
                staleOwners.Add(pair.Key);
                continue;
            }

            HealthSystem health = pair.Key.GetComponent<HealthSystem>();
            if (health != null && health.IsDead)
            {
                staleOwners.Add(pair.Key);
            }
        }

        foreach (AgentMotor owner in staleOwners)
        {
            reservations.Remove(owner);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawReservedSlots)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        foreach (KeyValuePair<AgentMotor, Reservation> pair in reservations)
        {
            if (pair.Key != null)
            {
                Gizmos.DrawWireSphere(
                    pair.Value.position + Vector3.up * 0.12f,
                    pair.Value.radius * 0.5f);
                Gizmos.DrawLine(
                    pair.Value.requestedPosition + Vector3.up * 0.12f,
                    pair.Value.position + Vector3.up * 0.12f);
            }
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
