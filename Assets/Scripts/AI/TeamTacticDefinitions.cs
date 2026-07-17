using System;

public enum InitialTeamTactic
{
    FastExecute,
    FeintAndRotate,
    SplitPush,
    SilentInfiltration
}

public enum MidRoundTactic
{
    Regroup,
    Plant,
    DefendBomb,
    Retreat
}

public enum PlantSitePreference
{
    Auto,
    A,
    B,
    C
}

public enum TeamTacticRole
{
    Entry,
    Trader,
    Support,
    BombCarrier,
    Lurker,
    RotationBlocker,
    Lockdown
}

/// <summary>
/// Single source of player-facing tactic names and descriptions.
/// </summary>
public static class TeamTacticDefinitions
{
    public static string GetName(InitialTeamTactic tactic)
    {
        return tactic switch
        {
            InitialTeamTactic.FastExecute => "Fast Execute",
            InitialTeamTactic.FeintAndRotate => "Feint and Rotate",
            InitialTeamTactic.SplitPush => "Split Push",
            InitialTeamTactic.SilentInfiltration => "Silent Infiltration",
            _ => tactic.ToString()
        };
    }

    public static string GetShortDescription(InitialTeamTactic tactic)
    {
        return tactic switch
        {
            InitialTeamTactic.FastExecute =>
                "Rush one site, overwhelm its defenders, and plant immediately.",
            InitialTeamTactic.FeintAndRotate =>
                "Fake one site with three players while two prepare the real hit.",
            InitialTeamTactic.SplitPush =>
                "Attack one site from two routes and collapse from multiple angles.",
            InitialTeamTactic.SilentInfiltration =>
                "Approach quietly, protect the bomb, then burst into the execute.",
            _ => string.Empty
        };
    }

    public static string GetName(MidRoundTactic tactic)
    {
        return tactic switch
        {
            MidRoundTactic.Regroup => "REGROUP",
            MidRoundTactic.Plant => "PLANT",
            MidRoundTactic.DefendBomb => "DEFEND",
            MidRoundTactic.Retreat => "RETREAT",
            _ => tactic.ToString()
        };
    }

    public static string GetShortDescription(MidRoundTactic tactic)
    {
        return tactic switch
        {
            MidRoundTactic.Regroup => "Gather on carrier.",
            MidRoundTactic.Plant => "Escort and plant.",
            MidRoundTactic.DefendBomb => "Surround the bomb.",
            MidRoundTactic.Retreat => "Fall back safe.",
            _ => string.Empty
        };
    }
}
