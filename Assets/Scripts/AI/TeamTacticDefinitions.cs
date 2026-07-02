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
                "Spread into cover and punish isolated or exposed defenders.",
            MidRoundTactic.WolfpackRegroup =>
                "Gather around the leader and win fights with focus fire and trades.",
            MidRoundTactic.CutOffRotation =>
                "Assign suitable players to deny defender reinforcement routes.",
            MidRoundTactic.PostPlantLockdown =>
                "Hold separate post-plant angles and make the defuser top priority.",
            MidRoundTactic.ProbeAndPlant =>
                "Clear dangerous angles carefully and create a protected plant window.",
            _ => string.Empty
        };
    }
}
