using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class TeamVisionVisibilityTests
{
    private readonly List<GameObject> createdObjects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void EnemyIsSharedWhenAnyLivingTeammateDetectsIt()
    {
        Vector3 isolatedOrigin = new Vector3(500f, 1f, 500f);
        AgentSensors first = CreateAgent(
            "First",
            TeamType.Red,
            isolatedOrigin + Vector3.left * 2f);
        AgentSensors second = CreateAgent(
            "Second",
            TeamType.Red,
            isolatedOrigin + Vector3.right * 2f);
        AgentSensors enemySensors = CreateAgent(
            "Enemy",
            TeamType.Blue,
            isolatedOrigin + Vector3.right * 2f + Vector3.forward * 5f);
        AgentStats enemy = enemySensors.GetComponent<AgentStats>();

        first.transform.forward = Vector3.back;
        second.transform.forward = Vector3.forward;
        Physics.SyncTransforms();

        Assert.IsTrue(
            second.IsInsideFieldOfView(enemy.transform.position),
            "The observing teammate should face the enemy.");
        Assert.IsTrue(
            second.HasLineOfSight(enemy.gameObject),
            "The observing teammate should have an unobstructed sight line.");
        Assert.IsTrue(
            second.CanDetect(enemy.gameObject),
            "The observing teammate should detect the enemy.");
        Assert.IsTrue(TeamVisionVisibility.IsVisibleToTeam(
            enemy,
            TeamType.Red,
            new[] { first, second }));

        second.transform.forward = Vector3.back;
        Physics.SyncTransforms();

        Assert.IsFalse(TeamVisionVisibility.IsVisibleToTeam(
            enemy,
            TeamType.Red,
            new[] { first, second }));
    }

    [Test]
    public void WallBlockedEnemyIsNotShared()
    {
        Vector3 isolatedOrigin = new Vector3(500f, 1f, 500f);
        AgentSensors observer = CreateAgent("Observer", TeamType.Red, isolatedOrigin);
        AgentSensors enemySensors = CreateAgent(
            "Enemy",
            TeamType.Blue,
            isolatedOrigin + Vector3.forward * 6f);
        AgentStats enemy = enemySensors.GetComponent<AgentStats>();
        observer.transform.forward = Vector3.forward;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Vision Blocker";
        wall.transform.position = isolatedOrigin + new Vector3(0f, 0.5f, 3f);
        wall.transform.localScale = new Vector3(3f, 3f, 0.5f);
        createdObjects.Add(wall);
        Physics.SyncTransforms();

        Assert.IsFalse(TeamVisionVisibility.IsVisibleToTeam(
            enemy,
            TeamType.Red,
            new[] { observer }));
    }

    private AgentSensors CreateAgent(string name, TeamType team, Vector3 position)
    {
        GameObject agent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        agent.name = name;
        agent.transform.position = position;
        createdObjects.Add(agent);

        AgentStats stats = agent.AddComponent<AgentStats>();
        stats.team = team;
        agent.AddComponent<HealthSystem>();
        AgentSensors sensors = agent.AddComponent<AgentSensors>();
        sensors.sightRange = 30f;
        sensors.fieldOfViewAngle = 90f;
        sensors.proximityDetectionRange = 0.5f;
        sensors.lineOfSightMask = ~0;
        // EditMode does not automatically invoke Awake on newly added runtime
        // components, so initialize the sensor's cached AgentStats explicitly.
        typeof(AgentSensors)
            .GetField("stats", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(sensors, stats);
        return sensors;
    }
}
