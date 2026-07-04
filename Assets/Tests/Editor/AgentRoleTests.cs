using NUnit.Framework;

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
}
