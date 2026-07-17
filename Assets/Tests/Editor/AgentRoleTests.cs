using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class AgentRoleTests
{
    [Test]
    public void MidRoundTactics_AreExactlyFourShortCommands()
    {
        MidRoundTactic[] tactics =
            (MidRoundTactic[])System.Enum.GetValues(typeof(MidRoundTactic));

        Assert.That(tactics, Has.Length.EqualTo(4));
        Assert.That(TeamTacticDefinitions.GetName(MidRoundTactic.Regroup),
            Is.EqualTo("REGROUP"));
        Assert.That(TeamTacticDefinitions.GetName(MidRoundTactic.Plant),
            Is.EqualTo("PLANT"));
        Assert.That(TeamTacticDefinitions.GetName(MidRoundTactic.DefendBomb),
            Is.EqualTo("DEFEND"));
        Assert.That(TeamTacticDefinitions.GetName(MidRoundTactic.Retreat),
            Is.EqualTo("RETREAT"));
    }

    [Test]
    public void MidRoundTactics_RunInClickOrder()
    {
        GameObject managerObject = new GameObject("Tactic Queue Test");
        try
        {
            TeamTacticManager manager = managerObject.AddComponent<TeamTacticManager>();
            manager.SelectInitialTactic(InitialTeamTactic.FastExecute);
            manager.ToggleMidRoundTactic(MidRoundTactic.Regroup);
            manager.SetPlantSitePreference(PlantSitePreference.B);
            manager.ToggleMidRoundTactic(MidRoundTactic.Plant);

            List<MidRoundTactic> commands =
                new List<MidRoundTactic>(manager.GetActiveMidRoundTactics());
            Assert.That(commands, Is.EqualTo(new[]
            {
                MidRoundTactic.Regroup,
                MidRoundTactic.Plant
            }));
            Assert.That(manager.TryGetCurrentMidRoundTactic(out MidRoundTactic current),
                Is.True);
            Assert.That(current, Is.EqualTo(MidRoundTactic.Regroup));
            Assert.That(manager.QueuedPlantSitePreference,
                Is.EqualTo(PlantSitePreference.B));

            manager.CompleteCurrentMidRoundTactic(MidRoundTactic.Regroup);
            Assert.That(manager.TryGetCurrentMidRoundTactic(out current), Is.True);
            Assert.That(current, Is.EqualTo(MidRoundTactic.Plant));
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void BombDefense_AssignsDifferentAnglesAroundBomb()
    {
        HashSet<Vector3> offsets = new HashSet<Vector3>();
        for (int i = 0; i < 5; i++)
            offsets.Add(TeamTacticExecutor.GetBombDefenseOffset(i, 5, 4f));

        Assert.That(offsets, Has.Count.EqualTo(5));
    }

    [Test]
    public void PlantPreference_IsABonusRatherThanAForcedSite()
    {
        Assert.That(TeamTacticExecutor.GetPlantPreferenceBonus(
            PlantSitePreference.A, BombSiteId.A), Is.GreaterThan(0f));
        Assert.That(TeamTacticExecutor.GetPlantPreferenceBonus(
            PlantSitePreference.A, BombSiteId.B), Is.EqualTo(0f));
        Assert.That(TeamTacticExecutor.GetPlantPreferenceBonus(
            PlantSitePreference.C, BombSiteId.C), Is.GreaterThan(0f));
        Assert.That(TeamTacticExecutor.GetPlantPreferenceBonus(
            PlantSitePreference.C, BombSiteId.A), Is.EqualTo(0f));
        Assert.That(TeamTacticExecutor.GetPlantPreferenceBonus(
            PlantSitePreference.Auto, BombSiteId.A), Is.EqualTo(0f));
    }

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
    public void ManualWall_UsesTheSameCooldownAsAiAbility()
    {
        GameObject agent = new GameObject("Manual Wall Test Agent");
        try
        {
            AgentStats stats = agent.AddComponent<AgentStats>();
            stats.team = TeamType.Red;
            agent.AddComponent<HealthSystem>();
            AgentRole role = agent.AddComponent<AgentRole>();
            role.SetRole(AgentRoleType.Defender);
            AgentRoleAbilities abilities = agent.AddComponent<AgentRoleAbilities>();
            typeof(AgentRoleAbilities)
                .GetField("nextWallTime", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(abilities, 0f);

            abilities.SetPlayerCommandSelected(true);
            Assert.That(abilities.IsReservedForPlayerCommand, Is.True);
            abilities.SetPlayerCommandSelected(false);
            Assert.That(abilities.IsReservedForPlayerCommand, Is.False);

            Assert.That(
                abilities.TryManualDeployWall(new Vector3(4f, 0f, 0f)),
                Is.True);
            Assert.That(
                abilities.CanManuallyDeployWall(
                    new Vector3(8f, 0f, 0f),
                    out _,
                    out string reason),
                Is.False);
            Assert.That(reason, Does.Contain("cooldown"));
        }
        finally
        {
            foreach (DeployedDefenderWall wall in
                     Object.FindObjectsByType<DeployedDefenderWall>(
                         FindObjectsInactive.Include))
            {
                Object.DestroyImmediate(wall.gameObject);
            }
            Object.DestroyImmediate(agent);
        }
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
    public void SupportManualHeal_ReachesFifteenMeters()
    {
        GameObject support = new GameObject("Support Range Test Healer");
        GameObject ally = new GameObject("Support Range Test Ally");
        try
        {
            AgentStats supportStats = support.AddComponent<AgentStats>();
            supportStats.team = TeamType.Red;
            support.AddComponent<HealthSystem>();
            AgentRole supportRole = support.AddComponent<AgentRole>();
            supportRole.SetRole(AgentRoleType.Support);
            AgentRoleAbilities abilities = support.AddComponent<AgentRoleAbilities>();
            typeof(AgentRoleAbilities)
                .GetField("nextHealTime", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(abilities, 0f);

            AgentStats allyStats = ally.AddComponent<AgentStats>();
            allyStats.team = TeamType.Red;
            HealthSystem allyHealth = ally.AddComponent<HealthSystem>();
            allyHealth.TakeDamage(10f);

            ally.transform.position = new Vector3(14.5f, 0f, 0f);
            Assert.That(abilities.CanManuallyHeal(allyHealth, out string nearReason),
                Is.True,
                nearReason);

            ally.transform.position = new Vector3(15.6f, 0f, 0f);
            Assert.That(abilities.CanManuallyHeal(allyHealth, out string farReason),
                Is.False);
            Assert.That(farReason, Does.Contain("15"));
        }
        finally
        {
            Object.DestroyImmediate(support);
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
    public void Turret_CanUseFullCircleFiringArc()
    {
        GameObject turretObject = new GameObject("Turret Full Circle Test");
        try
        {
            DeployableTurret turret = turretObject.AddComponent<DeployableTurret>();
            turret.Initialize(TeamType.Red, 120f, 14f, 360f);

            Assert.That(turret.IsInsideFiringArc(Vector3.forward * 10f), Is.True);
            Assert.That(turret.IsInsideFiringArc(Vector3.right * 10f), Is.True);
            Assert.That(turret.IsInsideFiringArc(Vector3.back * 10f), Is.True);
            Assert.That(turret.IsInsideFiringArc(Vector3.back * 15f), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(turretObject);
        }
    }

    [Test]
    public void ManualTurretInstall_CancelledBeforeCompletionRaisesRefundEvent()
    {
        GameObject agent = new GameObject("Manual Turret Cancel Test Agent");
        try
        {
            AgentStats stats = agent.AddComponent<AgentStats>();
            stats.team = TeamType.Red;
            agent.AddComponent<HealthSystem>();
            AgentRole role = agent.AddComponent<AgentRole>();
            role.SetRole(AgentRoleType.Assaulter);
            AgentRoleAbilities abilities = agent.AddComponent<AgentRoleAbilities>();
            typeof(AgentRoleAbilities)
                .GetField("nextTurretTime", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(abilities, 0f);

            bool cancelled = false;
            abilities.ManualTurretInstallationCancelled += _ => cancelled = true;

            Assert.That(abilities.TryManualInstallTurret(new Vector3(2f, 0f, 0f)),
                Is.True);
            typeof(AgentRoleAbilities)
                .GetField("lastDamagedAt", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(abilities, Time.time + 1f);
            typeof(AgentRoleAbilities)
                .GetMethod(
                    "UpdateTurretInstallation",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(abilities, null);

            Assert.That(cancelled, Is.True);
        }
        finally
        {
            foreach (DeployableTurret turret in
                     Object.FindObjectsByType<DeployableTurret>(
                         FindObjectsInactive.Include))
            {
                Object.DestroyImmediate(turret.gameObject);
            }
            Object.DestroyImmediate(agent);
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
