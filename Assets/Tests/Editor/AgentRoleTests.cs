using NUnit.Framework;
using UnityEngine;

public sealed class AgentRoleTests
{
    [Test]
    public void FlankerHasStrongestSideRearPreference()
    {
        AgentRoleTuning flanker = AgentRoleDefaults.Create(AgentRoleType.Flanker);
        Assert.Greater(flanker.sideRearPreference,
            AgentRoleDefaults.Create(AgentRoleType.Support).sideRearPreference);
        Assert.Greater(flanker.sideRearPreference,
            AgentRoleDefaults.Create(AgentRoleType.Assaulter).sideRearPreference);
        Assert.Greater(flanker.sideRearPreference,
            AgentRoleDefaults.Create(AgentRoleType.Defender).sideRearPreference);
    }

    [Test]
    public void AssaulterAcceptsMostDanger()
    {
        AgentRoleTuning assaulter = AgentRoleDefaults.Create(AgentRoleType.Assaulter);
        Assert.Greater(assaulter.dangerTolerance,
            AgentRoleDefaults.Create(AgentRoleType.Support).dangerTolerance);
        Assert.Greater(assaulter.dangerTolerance,
            AgentRoleDefaults.Create(AgentRoleType.Flanker).dangerTolerance);
        Assert.Greater(assaulter.dangerTolerance,
            AgentRoleDefaults.Create(AgentRoleType.Defender).dangerTolerance);
    }

    [Test]
    public void DefenderHasStrongestObjectiveAndCoverWeights()
    {
        AgentRoleTuning defender = AgentRoleDefaults.Create(AgentRoleType.Defender);
        Assert.AreEqual(3f, defender.objectiveProximity);
        Assert.AreEqual(3f, defender.coverPreference);
    }

    [Test]
    public void InfluenceLayerResizesAndClearsValues()
    {
        InfluenceMapLayer layer = new InfluenceMapLayer("Test", 3);
        layer[1] = 7f;
        layer.Resize(3);
        Assert.AreEqual(0f, layer[1]);
        layer.Resize(5);
        Assert.AreEqual(5, layer.values.Length);
    }

    [Test]
    public void DefenderWallThreatDetection_AcceptsSideAndRearOnly()
    {
        Assert.That(
            AgentRoleAbilities.IsSideOrRearThreat(Vector3.forward, Vector3.right),
            Is.True);
        Assert.That(
            AgentRoleAbilities.IsSideOrRearThreat(Vector3.forward, Vector3.back),
            Is.True);
        Assert.That(
            AgentRoleAbilities.IsSideOrRearThreat(Vector3.forward, Vector3.forward),
            Is.False);
    }

    [Test]
    public void DefenderWallColors_FollowTeamColor()
    {
        Color red = AgentRoleAbilities.GetWallColor(TeamType.Red);
        Color blue = AgentRoleAbilities.GetWallColor(TeamType.Blue);
        Assert.That(red.r, Is.GreaterThan(red.b));
        Assert.That(blue.b, Is.GreaterThan(blue.r));
    }

    [Test]
    public void SupportHealing_ClampsAtMaximumHealth()
    {
        GameObject ally = new GameObject("Healing Test Ally");
        try
        {
            ally.AddComponent<AgentStats>();
            HealthSystem health = ally.AddComponent<HealthSystem>();
            float maximum = health.CurrentHealth;
            health.TakeDamage(20f);

            float restored = health.Heal(50f);

            Assert.That(restored, Is.EqualTo(20f).Within(0.01f));
            Assert.That(health.CurrentHealth, Is.EqualTo(maximum).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(ally);
        }
    }

    [Test]
    public void SmokeCloud_BlocksOnlyLinesThatCrossItsRadius()
    {
        Assert.That(
            TacticalSmokeCloud.SegmentIntersectsSphere(
                new Vector3(-5f, 1f, 0f),
                new Vector3(5f, 1f, 0f),
                new Vector3(0f, 1f, 0f),
                2f),
            Is.True);
        Assert.That(
            TacticalSmokeCloud.SegmentIntersectsSphere(
                new Vector3(-5f, 1f, 4f),
                new Vector3(5f, 1f, 4f),
                new Vector3(0f, 1f, 0f),
                2f),
            Is.False);
    }

    [Test]
    public void SmokeColors_FollowThrowingTeam()
    {
        Color red = TacticalSmokeCloud.GetSmokeColor(TeamType.Red);
        Color blue = TacticalSmokeCloud.GetSmokeColor(TeamType.Blue);
        Assert.That(red.r, Is.GreaterThan(red.b));
        Assert.That(blue.b, Is.GreaterThan(blue.r));
    }

    [Test]
    public void EscapeSmoke_RequiresLowHealthOrRecentDamage()
    {
        Assert.That(AgentRoleAbilities.IsEscapeSmokeNeeded(0.8f, 10f), Is.False);
        Assert.That(AgentRoleAbilities.IsEscapeSmokeNeeded(0.35f, 10f), Is.True);
        Assert.That(AgentRoleAbilities.IsEscapeSmokeNeeded(0.8f, 1f), Is.True);
    }

    [Test]
    public void Turret_UsesLimitedRangeAndForwardFiringArc()
    {
        GameObject turretObject = new GameObject("Turret Arc Test");
        try
        {
            DeployableTurret turret = turretObject.AddComponent<DeployableTurret>();
            turret.Initialize(TeamType.Red, 120f, 14f, 70f);

            Assert.That(turret.IsInsideFiringArc(Vector3.forward * 10f), Is.True);
            Assert.That(turret.IsInsideFiringArc(Vector3.right * 10f), Is.False);
            Assert.That(turret.IsInsideFiringArc(Vector3.forward * 15f), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(turretObject);
        }
    }

    [Test]
    public void Turret_TakesDamageWithoutCountingAsAnAgent()
    {
        GameObject turretObject = new GameObject("Turret Damage Test");
        try
        {
            DeployableTurret turret = turretObject.AddComponent<DeployableTurret>();
            turret.Initialize(TeamType.Blue, 100f, 12f, 60f);
            turret.TakeDamage(25f);

            Assert.That(turret.NormalizedHealth, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(turretObject.GetComponent<AgentStats>(), Is.Null);
            Assert.That(CombatTargetUtility.IsAlive(turretObject), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(turretObject);
        }
    }
}
