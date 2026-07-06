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
    GuerrillaAmbush,
    WolfpackRegroup,
    CutOffRotation,
    PostPlantLockdown,
    ProbeAndPlant
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
            MidRoundTactic.GuerrillaAmbush => "Guerrilla Ambush",
            MidRoundTactic.WolfpackRegroup => "Wolfpack Regroup",
            MidRoundTactic.CutOffRotation => "Cut Off Rotation",
            MidRoundTactic.PostPlantLockdown => "Post-Plant Lockdown",
            MidRoundTactic.ProbeAndPlant => "Probe and Plant",
            _ => tactic.ToString()
        };
    }

    public static string GetShortDescription(MidRoundTactic tactic)
    {
        return tactic switch
        {
            MidRoundTactic.GuerrillaAmbush =>
                "Spread as far as possible and hunt isolated defenders.",
            MidRoundTactic.WolfpackRegroup =>
                "Gather around the bomb carrier and fight as one pack.",
            MidRoundTactic.CutOffRotation =>
                "After the plant, hold cover outside the site and stop rotations.",
            MidRoundTactic.PostPlantLockdown =>
                "After the plant, defend the bomb from inside the planted site.",
            MidRoundTactic.ProbeAndPlant =>
                "Cover the bomb carrier while they commit fully to the plant.",
            _ => string.Empty
        };
    }
}
