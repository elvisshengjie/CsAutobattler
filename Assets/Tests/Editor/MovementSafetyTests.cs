using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MovementSafetyTests
{
    [TearDown]
    public void TearDown()
    {
        foreach (AgentMotor motor in
                 Object.FindObjectsByType<AgentMotor>(FindObjectsInactive.Include))
        {
            Object.DestroyImmediate(motor.gameObject);
        }

        if (PositionReservationManager.Instance != null)
        {
            Object.DestroyImmediate(PositionReservationManager.Instance.gameObject);
        }

        if (AStarPathfinder3D.Instance != null)
        {
            Object.DestroyImmediate(AStarPathfinder3D.Instance.gameObject);
        }

        foreach (GameObject obstacle in GameObject.FindGameObjectsWithTag("Untagged"))
        {
            if (obstacle.name.StartsWith("TestObstacle"))
            {
                Object.DestroyImmediate(obstacle);
            }
        }
    }

    [Test]
    public void SharedObjective_AssignsDistinctReservedSlots()
    {
        AgentMotor first = CreateAgent("Agent A", new Vector3(-4f, 0f, 0f));
        AgentMotor second = CreateAgent("Agent B", new Vector3(-3f, 0f, 0f));

        Vector3 sharedTarget = new Vector3(8f, 0f, 2f);
        first.MoveTo(sharedTarget);
        second.MoveTo(sharedTarget);

        Assert.That(
            FlatDistance(first.Destination, second.Destination),
            Is.GreaterThanOrEqualTo(first.MinAgentSeparation - 0.01f));
    }

    [Test]
    public void FiveAgents_ReceiveCenterLeftRightAndRearSlots()
    {
        Vector3 start = new Vector3(-5f, 0f, 0f);
        Vector3 objective = new Vector3(8f, 0f, 0f);
        HashSet<TacticalSlotKind> kinds = new HashSet<TacticalSlotKind>();
        List<Vector3> positions = new List<Vector3>();

        for (int i = 0; i < 5; i++)
        {
            AgentMotor motor = CreateAgent("Slot Agent " + i, start);
            motor.MoveTo(objective);
            kinds.Add(motor.CurrentSlotKind);
            positions.Add(motor.Destination);
        }

        Assert.That(kinds, Does.Contain(TacticalSlotKind.Center));
        Assert.That(kinds, Does.Contain(TacticalSlotKind.Left));
        Assert.That(kinds, Does.Contain(TacticalSlotKind.Right));
        Assert.That(kinds, Does.Contain(TacticalSlotKind.BackLeft));
        Assert.That(kinds, Does.Contain(TacticalSlotKind.BackRight));
        for (int left = 0; left < positions.Count; left++)
        {
            for (int right = left + 1; right < positions.Count; right++)
            {
                Assert.That(FlatDistance(positions[left], positions[right]), Is.GreaterThan(0.9f));
            }
        }
    }

    [Test]
    public void StableReservation_IsKeptUntilForcedReassignment()
    {
        AgentMotor motor = CreateAgent("Stable Slot Agent", new Vector3(-5f, 0f, 0f));
        Vector3 objective = new Vector3(8f, 0f, 0f);
        PositionReservationManager manager = PositionReservationManager.EnsureInstance();

        Assert.That(manager.TryReserveTacticalSlot(
            motor,
            objective,
            objective - motor.transform.position,
            motor.MinAgentSeparation,
            2.4f,
            false,
            out Vector3 first,
            out _), Is.True);
        Assert.That(manager.TryReserveTacticalSlot(
            motor,
            objective,
            objective - motor.transform.position,
            motor.MinAgentSeparation,
            2.4f,
            false,
            out Vector3 stable,
            out _), Is.True);
        Assert.That(FlatDistance(first, stable), Is.LessThan(0.01f));

        Assert.That(manager.TryReserveTacticalSlot(
            motor,
            objective,
            objective - motor.transform.position,
            motor.MinAgentSeparation,
            2.4f,
            true,
            out Vector3 reassigned,
            out _), Is.True);
        Assert.That(FlatDistance(first, reassigned), Is.GreaterThan(0.4f));
    }

    [Test]
    public void TemporaryAgentOccupancy_DoesNotInvalidateReservedSlot()
    {
        AgentMotor owner = CreateAgent("Reservation Owner", new Vector3(-5f, 0f, 0f));
        AgentMotor passer = CreateAgent("Passing Agent", new Vector3(-3f, 0f, 0f));
        Vector3 objective = new Vector3(8f, 0f, 0f);
        PositionReservationManager manager = PositionReservationManager.EnsureInstance();

        Assert.That(manager.TryReserveTacticalSlot(
            owner,
            objective,
            objective - owner.transform.position,
            owner.MinAgentSeparation,
            2.4f,
            false,
            out Vector3 reserved,
            out _), Is.True);
        passer.transform.position = reserved;
        Physics.SyncTransforms();

        Assert.That(manager.IsReservationValid(owner), Is.True);
    }

    [Test]
    public void ForcedReassignmentWithoutAlternative_PreservesExistingSlot()
    {
        AgentMotor motor = CreateAgent("Preserved Slot Agent", new Vector3(-5f, 0f, 0f));
        Vector3 objective = new Vector3(8f, 0f, 0f);
        PositionReservationManager manager = PositionReservationManager.EnsureInstance();
        Assert.That(manager.TryReserveTacticalSlot(
            motor,
            objective,
            Vector3.right,
            motor.MinAgentSeparation,
            0f,
            false,
            out Vector3 original,
            out _), Is.True);

        Assert.That(manager.TryReserveTacticalSlot(
            motor,
            objective,
            Vector3.right,
            motor.MinAgentSeparation,
            0f,
            true,
            out Vector3 preserved,
            out _), Is.True);
        Assert.That(FlatDistance(original, preserved), Is.LessThan(0.01f));
    }

    [Test]
    public void ReservedSlot_DoesNotUseLooseCoverArrivalTolerance()
    {
        AgentMotor motor = CreateAgent("Precise Arrival Agent", Vector3.zero);
        motor.MoveTo(new Vector3(8f, 0f, 0f));
        motor.transform.position = motor.Destination + Vector3.right * 0.4f;

        Assert.That(motor.HasReachedDestination(0.65f), Is.False);

        motor.transform.position = motor.Destination + Vector3.right * 0.2f;
        Assert.That(motor.HasReachedDestination(0.65f), Is.True);
    }

    [Test]
    public void ExactObjectiveMovement_BypassesTacticalSlotReservation()
    {
        AgentMotor motor = CreateAgent("Exact Objective Agent", Vector3.zero);
        Vector3 objective = new Vector3(8f, 0f, 0f);
        PositionReservationManager manager = PositionReservationManager.EnsureInstance();

        motor.MoveTo(objective);
        Assert.That(manager.TryGetReservedSlot(motor, out _, out _), Is.True);

        motor.MoveToExactObjective(objective);

        Assert.That(manager.TryGetReservedSlot(motor, out _, out _), Is.False);
        Assert.That(FlatDistance(motor.Destination, objective), Is.LessThan(0.01f));
    }

    [Test]
    public void SmallImmediateTargetChange_KeepsStableAcceptedTarget()
    {
        AgentMotor motor = CreateAgent("Stable Agent", Vector3.zero);
        Vector3 initialTarget = new Vector3(8f, 0f, 0f);
        motor.MoveTo(initialTarget);

        motor.MoveTo(initialTarget + Vector3.right * 0.35f);

        Assert.That(FlatDistance(motor.RequestedDestination, initialTarget), Is.LessThan(0.01f));
    }

    [Test]
    public void PositionValidation_RejectsObstacleAndOtherAgent()
    {
        const int obstacleLayer = 6;
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = "TestObstacle";
        obstacle.layer = obstacleLayer;
        obstacle.transform.position = new Vector3(2f, 0.5f, 2f);
        obstacle.transform.localScale = new Vector3(2f, 1f, 2f);

        GameObject gridObject = new GameObject("Test Grid");
        AStarPathfinder3D pathfinder = gridObject.AddComponent<AStarPathfinder3D>();
        pathfinder.obstacleMask = 1 << obstacleLayer;
        Physics.SyncTransforms();
        typeof(AStarPathfinder3D)
            .GetMethod("CreateGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(pathfinder, null);

        AgentMotor agent = CreateAgent("Blocking Agent", new Vector3(6f, 0f, 6f));
        Physics.SyncTransforms();

        Assert.That(pathfinder.IsValidAgentPosition(new Vector3(2f, 0f, 2f), 0.5f), Is.False);
        Assert.That(pathfinder.IsValidAgentPosition(agent.transform.position, 0.5f), Is.False);
        Assert.That(
            pathfinder.IsValidAgentPosition(
                agent.transform.position,
                0.5f,
                agent.gameObject,
                true),
            Is.True);
    }

    [Test]
    public void AgentInsideWallClearanceBand_SteersOutWhileSlidingAlongWall()
    {
        const int obstacleLayer = 6;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "TestObstacleWall";
        wall.layer = obstacleLayer;
        wall.transform.position = new Vector3(0f, 0.5f, 0f);
        wall.transform.localScale = new Vector3(1f, 1f, 8f);

        GameObject gridObject = new GameObject("Test Grid");
        AStarPathfinder3D pathfinder = gridObject.AddComponent<AStarPathfinder3D>();
        pathfinder.obstacleMask = 1 << obstacleLayer;
        Physics.SyncTransforms();
        typeof(AStarPathfinder3D)
            .GetMethod("CreateGrid", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(pathfinder, null);

        AgentMotor agent = CreateAgent(
            "Wall Sliding Agent",
            new Vector3(1.1f, 0f, 0f));
        Physics.SyncTransforms();

        MethodInfo safeDirectionMethod = typeof(AgentMotor).GetMethod(
            "GetCollisionSafeDirection",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(safeDirectionMethod, Is.Not.Null);
        Vector3 safeDirection = (Vector3)safeDirectionMethod.Invoke(
            agent,
            new object[] { agent.transform.position, Vector3.forward, 0.1f });

        Assert.That(safeDirection.sqrMagnitude, Is.GreaterThan(0.01f));
        Assert.That(
            Vector3.Dot(safeDirection.normalized, Vector3.right),
            Is.GreaterThan(0.05f),
            "The wall slide must also increase clearance instead of only scraping the wall.");
    }

    [Test]
    public void WallEscape_UsesReleaseHysteresisInsteadOfBoundaryChatter()
    {
        const float requiredClearance = 0.65f;
        const float releaseMargin = 0.12f;

        Assert.That(
            AgentMotor.ShouldContinueClearanceEscape(
                false,
                0.64f,
                requiredClearance,
                releaseMargin),
            Is.True);
        Assert.That(
            AgentMotor.ShouldContinueClearanceEscape(
                false,
                0.68f,
                requiredClearance,
                releaseMargin),
            Is.False,
            "An agent outside the entry band should follow its path normally.");
        Assert.That(
            AgentMotor.ShouldContinueClearanceEscape(
                true,
                0.68f,
                requiredClearance,
                releaseMargin),
            Is.True,
            "An active escape must not switch off immediately after crossing the entry boundary.");
        Assert.That(
            AgentMotor.ShouldContinueClearanceEscape(
                true,
                0.78f,
                requiredClearance,
                releaseMargin),
            Is.False,
            "Escape steering should release only after the agent clears the hysteresis band.");
    }

    [Test]
    public void DefenderEncirclementOffsets_CoverBothFlanksAndEnemyRear()
    {
        Vector3 front = Vector3.forward;
        Vector3 left = DefenderTeamCoordinator.GetEncirclementOffset(
            DefenderEngagementRole.LeftFlank,
            front,
            5f);
        Vector3 right = DefenderTeamCoordinator.GetEncirclementOffset(
            DefenderEngagementRole.RightFlank,
            front,
            5f);
        Vector3 rear = DefenderTeamCoordinator.GetEncirclementOffset(
            DefenderEngagementRole.RearCutoff,
            front,
            5f);

        Assert.That(left.magnitude, Is.EqualTo(5f).Within(0.01f));
        Assert.That(right.magnitude, Is.EqualTo(5f).Within(0.01f));
        Assert.That(Vector3.Dot(left.normalized, right.normalized), Is.LessThan(-0.85f));
        Assert.That(Vector3.Dot(rear.normalized, front), Is.LessThan(-0.99f));
    }

    [Test]
    public void SmallerDefenderTeams_AdaptFormationToRisk()
    {
        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(4, 1, 0.9f, 7f),
            Is.EqualTo(DefenderFormationStyle.FullEncirclement));
        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(4, 2, 0.8f, 3f),
            Is.EqualTo(DefenderFormationStyle.Pincer));
        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(4, 1, 0.3f, 7f),
            Is.EqualTo(DefenderFormationStyle.Concentrated));

        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(3, 1, 0.9f, 7f),
            Is.EqualTo(DefenderFormationStyle.Pincer));
        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(3, 3, 0.8f, 7f),
            Is.EqualTo(DefenderFormationStyle.SingleFlank));
        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(3, 3, 0.8f, 3f),
            Is.EqualTo(DefenderFormationStyle.Concentrated));

        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(2, 1, 0.9f, 7f),
            Is.EqualTo(DefenderFormationStyle.SingleFlank));
        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(2, 2, 0.9f, 7f),
            Is.EqualTo(DefenderFormationStyle.Concentrated));
        Assert.That(
            DefenderTeamCoordinator.SelectFormationForSituation(5, 5, 0.2f, 1f),
            Is.EqualTo(DefenderFormationStyle.FullEncirclement));
    }

    [Test]
    public void CoveredDesignatedDefuser_PrioritizesBombOverVisibleThreat()
    {
        Assert.That(
            DefenderTeamCoordinator.ShouldCommitToDefuse(
                true,
                true,
                false,
                true,
                true),
            Is.True,
            "A designated defuser should commit while a teammate covers the threat.");
        Assert.That(
            DefenderTeamCoordinator.ShouldCommitToDefuse(
                true,
                true,
                false,
                true,
                false),
            Is.False,
            "An uncovered defuser may pause for an immediate threat.");
        Assert.That(
            DefenderTeamCoordinator.ShouldCommitToDefuse(
                true,
                true,
                true,
                true,
                false),
            Is.True,
            "A critical bomb timer must override combat hesitation.");
        Assert.That(
            DefenderTeamCoordinator.ShouldCommitToDefuse(
                false,
                true,
                true,
                false,
                true),
            Is.False,
            "Only the designated defender should abandon combat for the bomb.");
    }

    [Test]
    public void DefuserSelection_PrefersAvailableTeammateOverActiveFighter()
    {
        float activeFighterScore =
            DefenderTeamCoordinator.CalculateDefuserSelectionScore(
                2f,
                true,
                1f);
        float availableTeammateScore =
            DefenderTeamCoordinator.CalculateDefuserSelectionScore(
                7f,
                false,
                1f);

        Assert.That(availableTeammateScore, Is.LessThan(activeFighterScore));
    }

    [Test]
    public void SplitPush_CreatesStableThreeTwoGroupsOnOppositeRoutes()
    {
        Assert.That(TeamTacticExecutor.IsSecondSplitGroupIndex(0, 5), Is.False);
        Assert.That(TeamTacticExecutor.IsSecondSplitGroupIndex(2, 5), Is.False);
        Assert.That(TeamTacticExecutor.IsSecondSplitGroupIndex(3, 5), Is.True);
        Assert.That(TeamTacticExecutor.IsSecondSplitGroupIndex(4, 5), Is.True);

        Vector3 start = new Vector3(0f, 0f, -15f);
        Vector3 site = new Vector3(20f, 0f, 12f);
        Vector3 routeA = TeamTacticExecutor.GetSplitRouteCandidate(
            start,
            site,
            false,
            8f,
            0.5f);
        Vector3 routeB = TeamTacticExecutor.GetSplitRouteCandidate(
            start,
            site,
            true,
            8f,
            0.5f);
        Vector3 direct = site - start;
        direct.y = 0f;
        Vector3 midpoint = Vector3.Lerp(start, site, 0.5f);
        Vector3 sideA = routeA - midpoint;
        Vector3 sideB = routeB - midpoint;

        Assert.That(Vector3.Dot(sideA, sideB), Is.LessThan(0f));
        Assert.That(FlatDistance(routeA, routeB), Is.GreaterThanOrEqualTo(15.9f));
        Assert.That(
            Mathf.Abs(Vector3.Dot(sideA.normalized, direct.normalized)),
            Is.LessThan(0.05f));
    }

    [Test]
    public void FourPostPlantSupporters_ReceiveFourDifferentCoverAngles()
    {
        HashSet<float> angles = new HashSet<float>();
        for (int index = 0; index < 4; index++)
        {
            angles.Add(DefenderTeamCoordinator.GetPostPlantCoverAngle(index, 4));
        }

        Assert.That(angles.Count, Is.EqualTo(4));
        Assert.That(angles, Does.Contain(-55f));
        Assert.That(angles, Does.Contain(55f));
        Assert.That(angles, Does.Contain(-135f));
        Assert.That(angles, Does.Contain(135f));
    }

    [Test]
    public void GuerrillaSpread_AssignsWidelySeparatedDirections()
    {
        const int teamCount = 5;
        const float radius = 14f;
        List<Vector3> offsets = new List<Vector3>();
        for (int index = 0; index < teamCount; index++)
        {
            offsets.Add(TeamTacticExecutor.GetGuerrillaSpreadOffset(
                index,
                teamCount,
                radius));
        }

        for (int left = 0; left < offsets.Count; left++)
        {
            Assert.That(offsets[left].magnitude, Is.EqualTo(radius).Within(0.01f));
            for (int right = left + 1; right < offsets.Count; right++)
            {
                Assert.That(
                    FlatDistance(offsets[left], offsets[right]),
                    Is.GreaterThan(8f));
            }
        }
    }

    [Test]
    public void PostPlantPositions_RespectInsideAndOutsideSiteCommands()
    {
        Bounds site = new Bounds(Vector3.zero, new Vector3(10f, 1f, 8f));
        Vector3 inside = TeamTacticExecutor.ClampInsideSiteBounds(
            new Vector3(20f, 0f, -20f),
            site,
            0.75f,
            1f);
        Assert.That(inside.x, Is.InRange(site.min.x + 0.74f, site.max.x - 0.74f));
        Assert.That(inside.z, Is.InRange(site.min.z + 0.74f, site.max.z - 0.74f));

        Vector3 outside = TeamTacticExecutor.PushOutsideSiteBounds(
            Vector3.zero,
            site,
            0.75f,
            1f);
        Assert.That(
            outside.x < site.min.x || outside.x > site.max.x ||
            outside.z < site.min.z || outside.z > site.max.z,
            Is.True);
    }

    [Test]
    public void ExclusiveDefuseArea_IsReservedForOneDesignatedDefender()
    {
        Assert.That(
            DefenderTeamCoordinator.ShouldYieldDefuseArea(false, 1.2f, 3.25f),
            Is.True);
        Assert.That(
            DefenderTeamCoordinator.ShouldYieldDefuseArea(true, 1.2f, 3.25f),
            Is.False);
        Assert.That(
            DefenderTeamCoordinator.ShouldYieldDefuseArea(false, 3.5f, 3.25f),
            Is.False);
    }

    private static AgentMotor CreateAgent(string name, Vector3 position)
    {
        GameObject agent = new GameObject(name);
        agent.transform.position = position;
        agent.AddComponent<AgentStats>();
        CapsuleCollider collider = agent.AddComponent<CapsuleCollider>();
        collider.radius = 0.5f;
        collider.height = 2f;
        collider.center = Vector3.up;
        Rigidbody body = agent.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        return agent.AddComponent<AgentMotor>();
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
