using NUnit.Framework;
using UnityEngine;

public class TacticalSlowMotionTests
{
    [Test]
    public void FocusDrain_UsesRealSecondsAndStopsAtZero()
    {
        Assert.That(TacticalFocusMath.Drain(100f, 25f, 2f), Is.EqualTo(50f));
        Assert.That(TacticalFocusMath.Drain(10f, 25f, 1f), Is.Zero);
    }

    [Test]
    public void FocusRecovery_StopsAtConfiguredMaximum()
    {
        Assert.That(
            TacticalFocusMath.Recover(40f, 100f, 10f, 3f),
            Is.EqualTo(70f));
        Assert.That(
            TacticalFocusMath.Recover(95f, 100f, 10f, 3f),
            Is.EqualTo(100f));
    }

    [TestCase(RoundState.Active, true)]
    [TestCase(RoundState.Planting, true)]
    [TestCase(RoundState.BombPlanted, true)]
    [TestCase(RoundState.Preparation, false)]
    [TestCase(RoundState.RoundEnd, false)]
    [TestCase(RoundState.Defused, false)]
    [TestCase(RoundState.Exploded, false)]
    public void TacticalMode_OnlyAcceptsLiveCombatStates(
        RoundState state,
        bool expected)
    {
        Assert.That(
            TacticalSlowMotionController.IsCommandableRoundState(state),
            Is.EqualTo(expected));
    }

    [Test]
    public void TacticalHud_IsCenteredDirectlyAboveRedTeamStatsPanel()
    {
        Rect redTeamStats = new Rect(100f, 600f, 800f, 116f);

        Rect tacticalHud = TacticalHudLayout.PlaceAbove(
            redTeamStats,
            520f,
            94f,
            12f,
            1000f,
            18f);

        Assert.That(tacticalHud.x, Is.EqualTo(240f));
        Assert.That(tacticalHud.y, Is.EqualTo(494f));
        Assert.That(tacticalHud.yMax, Is.EqualTo(redTeamStats.y - 12f));
    }
}
