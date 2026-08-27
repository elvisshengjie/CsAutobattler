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
        return new[]
        {
            new CampaignRoundDefinition
            {
                name = "Opening Skirmish",
                description = "A single rifle defender holds the map.",
                enemyBuffName = "None",
                enemyBuffDescription = "Learn the map and your starter's ability.",
                deploymentLimit = 5,
                enemyTactic = CampaignEnemyTactic.BasicHold,
                enemies = new[]
                {
                    new CampaignEnemyUnit(
                        AgentRoleType.Defender,
                        WeaponType.Rifle,
                        CharacterTier.C,
                        false)
                }
            },
            new CampaignRoundDefinition
            {
                name = "Split Response",
                description = "Two defenders divide their attention between sites.",
                enemyBuffName = "Drilled",
                enemyBuffDescription = "+5% health and damage.",
                deploymentLimit = 5,
                enemyHealthMultiplier = 1.05f,
                enemyDamageMultiplier = 1.05f,
                enemyTactic = CampaignEnemyTactic.SplitDefense,
                enemies = new[]
                {
                    new CampaignEnemyUnit(
                        AgentRoleType.Defender,
                        WeaponType.Rifle,
                        CharacterTier.C,
                        true),
                    new CampaignEnemyUnit(
                        AgentRoleType.Flanker,
                        WeaponType.SMG,
                        CharacterTier.C,
                        true)
                }
            },
            new CampaignRoundDefinition
            {
                name = "Coordinated Retake",
                description = "A mixed squad rotates aggressively after contact.",
                enemyBuffName = "Fortified Response",
                enemyBuffDescription = "+12% health and +8% damage.",
                deploymentLimit = 5,
                enemyHealthMultiplier = 1.12f,
                enemyDamageMultiplier = 1.08f,
                enemyTactic = CampaignEnemyTactic.AggressiveRotation,
                enemies = new[]
                {
                    new CampaignEnemyUnit(
                        AgentRoleType.Support,
                        WeaponType.Rifle,
                        CharacterTier.B,
                        true),
                    new CampaignEnemyUnit(
                        AgentRoleType.Assaulter,
                        WeaponType.Shotgun,
                        CharacterTier.B,
                        true),
                    new CampaignEnemyUnit(
                        AgentRoleType.Flanker,
                        WeaponType.SMG,
                        CharacterTier.C,
                        true)
                }
            }
        };
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
