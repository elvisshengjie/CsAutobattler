#if false // Tests live in the special Editor folder; retained only because Unity may lock imported files.
using System.Reflection;
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
#endif
