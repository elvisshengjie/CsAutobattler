using NUnit.Framework;

public sealed class CampaignSystemTests
{
    [TestCase(0, 0)]
    [TestCase(4, 0)]
    [TestCase(5, 1)]
    [TestCase(12, 2)]
    [TestCase(25, 5)]
    [TestCase(100, 5)]
    public void Interest_IsOnePerFiveGoldAndCapped(int gold, int expected)
    {
        Assert.That(CampaignBalance.GetInterest(gold), Is.EqualTo(expected));
    }

    [Test]
    public void TierStats_IncreaseWithoutLargePowerSpikes()
    {
        Assert.That(
            CampaignBalance.GetHealthMultiplier(CharacterTier.B),
            Is.GreaterThan(CampaignBalance.GetHealthMultiplier(CharacterTier.C)));
        Assert.That(
            CampaignBalance.GetHealthMultiplier(CharacterTier.A),
            Is.GreaterThan(CampaignBalance.GetHealthMultiplier(CharacterTier.B)));
        Assert.That(CampaignBalance.GetHealthMultiplier(CharacterTier.A), Is.LessThanOrEqualTo(1.3f));
    }

    [Test]
    public void Prototype_HasThreeIncreasingEnemyRounds()
    {
        CampaignRoundDefinition[] rounds =
            CampaignRoundDefinition.CreatePrototypeRounds();

        Assert.That(rounds, Has.Length.EqualTo(3));
        Assert.That(rounds[0].enemies, Has.Length.EqualTo(1));
        Assert.That(rounds[1].enemies, Has.Length.EqualTo(2));
        Assert.That(rounds[2].enemies, Has.Length.EqualTo(3));
        Assert.That(rounds[0].deploymentLimit, Is.EqualTo(5));
        Assert.That(rounds[1].deploymentLimit, Is.EqualTo(5));
        Assert.That(rounds[2].deploymentLimit, Is.EqualTo(5));
    }

    [Test]
    public void TemporaryAndPermanentUpgrades_AreDistinguished()
    {
        Assert.That(
            CampaignBalance.IsPermanent(CampaignUpgradeKind.SquadVitality),
            Is.True);
        Assert.That(
            CampaignBalance.IsPermanent(CampaignUpgradeKind.SharpenedWeapons),
            Is.True);
        Assert.That(
            CampaignBalance.IsPermanent(CampaignUpgradeKind.OpeningHaste),
            Is.False);
        Assert.That(
            CampaignBalance.IsPermanent(CampaignUpgradeKind.OpeningSlow),
            Is.False);
    }
}
