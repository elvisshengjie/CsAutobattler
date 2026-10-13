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
    public void Campaign_EnemyCountMatchesDeploymentLimitAndNeverEases()
    {
        CampaignRoundDefinition[] rounds =
            CampaignRoundDefinition.CreatePrototypeRounds();

        Assert.That(rounds.Length, Is.GreaterThanOrEqualTo(5));
        for (int i = 0; i < rounds.Length; i++)
        {
            Assert.That(rounds[i].enemies.Length, Is.EqualTo(rounds[i].deploymentLimit),
                $"Round {i + 1} must not let the player win on numbers alone.");
            Assert.That(rounds[i].deploymentLimit, Is.InRange(1, 5),
                "The map supplies five slots per team.");
            if (i > 0)
            {
                Assert.That(rounds[i].deploymentLimit,
                    Is.GreaterThanOrEqualTo(rounds[i - 1].deploymentLimit));
                Assert.That(rounds[i].enemyHealthMultiplier,
                    Is.GreaterThanOrEqualTo(rounds[i - 1].enemyHealthMultiplier));
            }
        }
    }

    [Test]
    public void ShopOdds_HigherTiersUnlockGradually()
    {
        Assert.That(CampaignBalance.RollShopTier(0, 99), Is.EqualTo(CharacterTier.C));
        Assert.That(CampaignBalance.RollShopTier(2, 0), Is.EqualTo(CharacterTier.B));
        Assert.That(CampaignBalance.RollShopTier(2, 99), Is.EqualTo(CharacterTier.C));
        Assert.That(CampaignBalance.RollShopTier(6, 0), Is.EqualTo(CharacterTier.A));
    }

    [Test]
    public void Sell_RefundsLessThanPurchasePrice()
    {
        foreach (CharacterTier tier in new[] { CharacterTier.C, CharacterTier.B, CharacterTier.A })
        {
            Assert.That(CampaignBalance.GetSellValue(tier),
                Is.InRange(1, CampaignBalance.GetCharacterCost(tier) - 1));
        }
    }

    [Test]
    public void ThreeMatchingOperators_MergeIntoNextRankKeepingOldest()
    {
        var roster = new System.Collections.Generic.List<CampaignCharacter>
        {
            Character(1, AgentRoleType.Flanker, WeaponType.Sniper, CharacterTier.C, false),
            Character(2, AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.C, true),
            Character(3, AgentRoleType.Support, WeaponType.Rifle, CharacterTier.C, true),
            Character(4, AgentRoleType.Flanker, WeaponType.Rifle, CharacterTier.C, false)
        };

        var promoted = CampaignRosterRules.MergeAll(roster);

        Assert.That(roster, Has.Count.EqualTo(2));
        Assert.That(promoted, Has.Count.EqualTo(1));
        Assert.That(promoted[0].id, Is.EqualTo(1));
        Assert.That(promoted[0].tier, Is.EqualTo(CharacterTier.B));
        Assert.That(promoted[0].weapon, Is.EqualTo(WeaponType.Sniper));
        Assert.That(promoted[0].deployed, Is.True, "A deployed merge input keeps the result deployed.");
    }

    [Test]
    public void Merge_ChainsFromCToA()
    {
        var roster = new System.Collections.Generic.List<CampaignCharacter>
        {
            Character(1, AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.B, true),
            Character(2, AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.B, false),
            Character(3, AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.C, false),
            Character(4, AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.C, false),
            Character(5, AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.C, false)
        };

        var promoted = CampaignRosterRules.MergeAll(roster);

        Assert.That(roster, Has.Count.EqualTo(1));
        Assert.That(roster[0].tier, Is.EqualTo(CharacterTier.A));
        Assert.That(promoted, Has.Count.EqualTo(1));
    }

    [Test]
    public void FullRoster_CanStillBuyTheThirdCopy()
    {
        var roster = new System.Collections.Generic.List<CampaignCharacter>
        {
            Character(1, AgentRoleType.Support, WeaponType.Rifle, CharacterTier.C, true),
            Character(2, AgentRoleType.Support, WeaponType.SMG, CharacterTier.C, true)
        };
        Assert.That(CampaignRosterRules.PurchaseCompletesMerge(
            roster, Character(3, AgentRoleType.Support, WeaponType.Rifle, CharacterTier.C, false)), Is.True);
        Assert.That(CampaignRosterRules.PurchaseCompletesMerge(
            roster, Character(4, AgentRoleType.Support, WeaponType.Rifle, CharacterTier.B, false)), Is.False);
    }

    private static CampaignCharacter Character(
        int id, AgentRoleType role, WeaponType weapon, CharacterTier tier, bool deployed)
    {
        return new CampaignCharacter { id = id, role = role, weapon = weapon, tier = tier, deployed = deployed };
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
