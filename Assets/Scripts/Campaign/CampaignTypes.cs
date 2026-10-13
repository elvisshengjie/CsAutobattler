using System;
using System.Collections.Generic;
using UnityEngine;

public enum CharacterTier
{
    C,
    B,
    A
}

public enum CampaignUpgradeKind
{
    SquadVitality,
    SharpenedWeapons,
    OpeningHaste,
    OpeningSlow
}

public enum CampaignEnemyTactic
{
    BasicHold,
    SplitDefense,
    AggressiveRotation
}

[Serializable]
public sealed class CampaignCharacter
{
    public int id;
    public AgentRoleType role;
    public WeaponType weapon;
    public CharacterTier tier;
    public bool deployed;

    public int Cost => CampaignBalance.GetCharacterCost(tier);
    public string CharacterName => role switch
    {
        AgentRoleType.Support => "Lifeline",
        AgentRoleType.Flanker => "Shade",
        AgentRoleType.Assaulter => "Vanguard",
        AgentRoleType.Defender => "Bulwark",
        _ => role.ToString()
    };
    public string DisplayName =>
        $"{CharacterName} — {tier}-Tier {role} / {weapon}";
}

[Serializable]
public struct CampaignEnemyUnit
{
    public AgentRoleType role;
    public WeaponType weapon;
    public CharacterTier tier;
    public bool abilitiesEnabled;

    public CampaignEnemyUnit(
        AgentRoleType role,
        WeaponType weapon,
        CharacterTier tier,
        bool abilitiesEnabled)
    {
        this.role = role;
        this.weapon = weapon;
        this.tier = tier;
        this.abilitiesEnabled = abilitiesEnabled;
    }
}

[Serializable]
public sealed class CampaignRoundDefinition
{
    public string name;
    public string description;
    public string enemyBuffName;
    public string enemyBuffDescription;
    public int deploymentLimit;
    public float enemyHealthMultiplier = 1f;
    public float enemyDamageMultiplier = 1f;
    public CampaignEnemyTactic enemyTactic;
    public CampaignEnemyUnit[] enemies;

    public static CampaignRoundDefinition[] CreatePrototypeRounds()
    {
        // Enemy count always matches the deployment limit, so the player never
        // wins on numbers alone: the edge has to come from tiers, merges,
        // upgrades, tactics and abilities.
        return new[]
        {
            Round(
                "Opening Skirmish",
                "Two defenders hold the map. Learn your starter's ability.",
                "None",
                "No enemy buffs.",
                2, 1f, 1f,
                CampaignEnemyTactic.BasicHold,
                Enemy(AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.C, false),
                Enemy(AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.C, false)),
            Round(
                "Split Response",
                "Three defenders divide their attention between sites.",
                "Abilities Online",
                "Enemy abilities are now active.",
                3, 1f, 1f,
                CampaignEnemyTactic.SplitDefense,
                Enemy(AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.C, true),
                Enemy(AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.C, true),
                Enemy(AgentRoleType.Support, WeaponType.Rifle, CharacterTier.C, true)),
            Round(
                "Crossfire",
                "A sniper anchors the long lanes while the others rotate.",
                "Drilled",
                "+5% health and damage.",
                3, 1.05f, 1.05f,
                CampaignEnemyTactic.SplitDefense,
                Enemy(AgentRoleType.Defender, WeaponType.Sniper, CharacterTier.B, true),
                Enemy(AgentRoleType.Assaulter, WeaponType.Shotgun, CharacterTier.C, true),
                Enemy(AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.C, true)),
            Round(
                "Hard Point",
                "Four defenders fortify both sites and punish slow pushes.",
                "Fortified",
                "+8% health and +5% damage.",
                4, 1.08f, 1.05f,
                CampaignEnemyTactic.BasicHold,
                Enemy(AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.B, true),
                Enemy(AgentRoleType.Support, WeaponType.Rifle, CharacterTier.B, true),
                Enemy(AgentRoleType.Assaulter, WeaponType.Shotgun, CharacterTier.C, true),
                Enemy(AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.C, true)),
            Round(
                "Hunter Squad",
                "An aggressive squad pushes out to hunt you before you reach a site.",
                "Bloodthirsty",
                "+10% health and +10% damage.",
                4, 1.10f, 1.10f,
                CampaignEnemyTactic.AggressiveRotation,
                Enemy(AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.B, true),
                Enemy(AgentRoleType.Assaulter, WeaponType.Shotgun, CharacterTier.B, true),
                Enemy(AgentRoleType.Defender, WeaponType.Sniper, CharacterTier.B, true),
                Enemy(AgentRoleType.Support, WeaponType.Rifle, CharacterTier.C, true)),
            Round(
                "Coordinated Retake",
                "A full squad holds and retakes together. Every mistake is punished.",
                "Veteran Unit",
                "+12% health and +10% damage.",
                5, 1.12f, 1.10f,
                CampaignEnemyTactic.AggressiveRotation,
                Enemy(AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.A, true),
                Enemy(AgentRoleType.Support, WeaponType.Rifle, CharacterTier.B, true),
                Enemy(AgentRoleType.Assaulter, WeaponType.Shotgun, CharacterTier.B, true),
                Enemy(AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.B, true),
                Enemy(AgentRoleType.Defender, WeaponType.Sniper, CharacterTier.C, true)),
            Round(
                "Final Stand",
                "The enemy's elite squad. Break them and the campaign is yours.",
                "Elite Command",
                "+18% health and +12% damage.",
                5, 1.18f, 1.12f,
                CampaignEnemyTactic.SplitDefense,
                Enemy(AgentRoleType.Defender, WeaponType.Rifle, CharacterTier.A, true),
                Enemy(AgentRoleType.Assaulter, WeaponType.Shotgun, CharacterTier.A, true),
                Enemy(AgentRoleType.Support, WeaponType.Rifle, CharacterTier.B, true),
                Enemy(AgentRoleType.Flanker, WeaponType.SMG, CharacterTier.B, true),
                Enemy(AgentRoleType.Defender, WeaponType.Sniper, CharacterTier.B, true))
        };
    }

    private static CampaignRoundDefinition Round(
        string name,
        string description,
        string buffName,
        string buffDescription,
        int deploymentLimit,
        float healthMultiplier,
        float damageMultiplier,
        CampaignEnemyTactic tactic,
        params CampaignEnemyUnit[] enemies)
    {
        return new CampaignRoundDefinition
        {
            name = name,
            description = description,
            enemyBuffName = buffName,
            enemyBuffDescription = buffDescription,
            deploymentLimit = deploymentLimit,
            enemyHealthMultiplier = healthMultiplier,
            enemyDamageMultiplier = damageMultiplier,
            enemyTactic = tactic,
            enemies = enemies
        };
    }

    private static CampaignEnemyUnit Enemy(
        AgentRoleType role,
        WeaponType weapon,
        CharacterTier tier,
        bool abilitiesEnabled)
    {
        return new CampaignEnemyUnit(role, weapon, tier, abilitiesEnabled);
    }
}

public static class CampaignBalance
{
    public const int BaseIncome = 5;
    public const int InterestStep = 5;
    public const int MaximumInterest = 5;
    public const int RerollCost = 1;
    public const int UpgradeCost = 3;
    public const int RosterCapacity = 8;
    public const int ShopSlots = 4;
    public const int StartingGold = 5;
    public const int FlawlessBonus = 2;
    public const int MergeCount = 3;

    public static int GetInterest(int savedGold)
    {
        return Mathf.Clamp(savedGold / InterestStep, 0, MaximumInterest);
    }

    public static int GetRoundIncome(int savedGold)
    {
        return BaseIncome + GetInterest(savedGold);
    }

    public static int GetCharacterCost(CharacterTier tier)
    {
        return tier switch
        {
            CharacterTier.C => 3,
            CharacterTier.B => 5,
            CharacterTier.A => 8,
            _ => 3
        };
    }

    /// <summary>Sell value: full price minus one, so buying is a commitment.</summary>
    public static int GetSellValue(CharacterTier tier)
    {
        return Mathf.Max(1, GetCharacterCost(tier) - 1);
    }

    public static bool TryGetNextTier(CharacterTier tier, out CharacterTier next)
    {
        next = tier == CharacterTier.C ? CharacterTier.B : CharacterTier.A;
        return tier != CharacterTier.A;
    }

    /// <summary>
    /// Shop odds (percent) for B and A tiers per zero-based round index. Higher
    /// tiers appear gradually, so merging is the main early route to power.
    /// </summary>
    public static void GetShopOdds(int roundIndex, out int bPercent, out int aPercent)
    {
        switch (roundIndex)
        {
            case 0:
            case 1:
                bPercent = 0; aPercent = 0; break;
            case 2:
                bPercent = 25; aPercent = 0; break;
            case 3:
                bPercent = 35; aPercent = 5; break;
            case 4:
                bPercent = 45; aPercent = 10; break;
            default:
                bPercent = 45; aPercent = 20; break;
        }
    }

    public static CharacterTier RollShopTier(int roundIndex, int roll)
    {
        GetShopOdds(roundIndex, out int b, out int a);
        if (roll < a)
        {
            return CharacterTier.A;
        }
        return roll < a + b ? CharacterTier.B : CharacterTier.C;
    }

    public static float GetHealthMultiplier(CharacterTier tier)
    {
        return tier switch
        {
            CharacterTier.B => 1.15f,
            CharacterTier.A => 1.30f,
            _ => 1f
        };
    }

    public static float GetDamageMultiplier(CharacterTier tier)
    {
        return tier switch
        {
            CharacterTier.B => 1.12f,
            CharacterTier.A => 1.25f,
            _ => 1f
        };
    }

    public static float GetAccuracyBonus(CharacterTier tier)
    {
        return tier switch
        {
            CharacterTier.B => 4f,
            CharacterTier.A => 8f,
            _ => 0f
        };
    }

    public static float GetMovementMultiplier(CharacterTier tier)
    {
        return tier switch
        {
            CharacterTier.B => 1.05f,
            CharacterTier.A => 1.10f,
            _ => 1f
        };
    }

    public static string GetUpgradeName(CampaignUpgradeKind kind)
    {
        return kind switch
        {
            CampaignUpgradeKind.SquadVitality => "Squad Vitality",
            CampaignUpgradeKind.SharpenedWeapons => "Sharpened Weapons",
            CampaignUpgradeKind.OpeningHaste => "Opening Haste",
            CampaignUpgradeKind.OpeningSlow => "Disruption Field",
            _ => kind.ToString()
        };
    }

    public static string GetUpgradeDescription(CampaignUpgradeKind kind)
    {
        return kind switch
        {
            CampaignUpgradeKind.SquadVitality =>
                "Permanent: all player units gain +5% maximum health.",
            CampaignUpgradeKind.SharpenedWeapons =>
                "Permanent: all player units deal +5% weapon damage.",
            CampaignUpgradeKind.OpeningHaste =>
                "Next battle: allies move 20% faster for the first 10 seconds.",
            CampaignUpgradeKind.OpeningSlow =>
                "Next battle: enemies move 20% slower for the first 10 seconds.",
            _ => string.Empty
        };
    }

    public static bool IsPermanent(CampaignUpgradeKind kind)
    {
        return kind == CampaignUpgradeKind.SquadVitality ||
               kind == CampaignUpgradeKind.SharpenedWeapons;
    }
}

/// <summary>
/// Auto-chess style roster rules: three operators with the same role and rank
/// merge into one operator of the next rank.
/// </summary>
public static class CampaignRosterRules
{
    public static int CountMatching(
        IEnumerable<CampaignCharacter> roster,
        AgentRoleType role,
        CharacterTier tier)
    {
        int count = 0;
        foreach (CampaignCharacter character in roster)
        {
            if (character.role == role && character.tier == tier)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// True when buying this offer completes a merge, so it can be bought even
    /// with a full roster.
    /// </summary>
    public static bool PurchaseCompletesMerge(
        IEnumerable<CampaignCharacter> roster,
        CampaignCharacter offer)
    {
        return offer.tier != CharacterTier.A &&
               CountMatching(roster, offer.role, offer.tier) >=
               CampaignBalance.MergeCount - 1;
    }

    /// <summary>
    /// Repeatedly merges triples (C into B, then B into A). The oldest operator
    /// of each triple survives, keeping its id and weapon; it stays deployed if
    /// any merged operator was deployed. Returns every promoted operator.
    /// </summary>
    public static List<CampaignCharacter> MergeAll(List<CampaignCharacter> roster)
    {
        List<CampaignCharacter> promoted = new List<CampaignCharacter>();
        bool merged = true;
        while (merged)
        {
            merged = false;
            foreach (CharacterTier tier in new[] { CharacterTier.C, CharacterTier.B })
            {
                foreach (AgentRoleType role in Enum.GetValues(typeof(AgentRoleType)))
                {
                    List<CampaignCharacter> matches = new List<CampaignCharacter>();
                    foreach (CampaignCharacter character in roster)
                    {
                        if (character.role == role && character.tier == tier)
                        {
                            matches.Add(character);
                        }
                    }
                    if (matches.Count < CampaignBalance.MergeCount)
                    {
                        continue;
                    }

                    matches.Sort((a, b) => a.id.CompareTo(b.id));
                    CampaignCharacter survivor = matches[0];
                    bool anyDeployed = false;
                    for (int i = 0; i < CampaignBalance.MergeCount; i++)
                    {
                        anyDeployed |= matches[i].deployed;
                    }
                    for (int i = 1; i < CampaignBalance.MergeCount; i++)
                    {
                        roster.Remove(matches[i]);
                        promoted.Remove(matches[i]);
                    }

                    CampaignBalance.TryGetNextTier(tier, out CharacterTier next);
                    survivor.tier = next;
                    survivor.deployed = anyDeployed;
                    if (!promoted.Contains(survivor))
                    {
                        promoted.Add(survivor);
                    }
                    merged = true;
                }
            }
        }
        return promoted;
    }
}
