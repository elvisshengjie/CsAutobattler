using NUnit.Framework;
using UnityEngine;

public class RoundResultTests
{
    private GameObject roundObject;

    [TearDown]
    public void TearDown()
    {
        if (roundObject != null)
        {
            Object.DestroyImmediate(roundObject);
        }

        if (RoundResultUI.Instance != null)
        {
            Object.DestroyImmediate(RoundResultUI.Instance.gameObject);
        }
    }

    [Test]
    public void EndRound_IsOneShotAndKeepsFirstResult()
    {
        roundObject = new GameObject("Test Round Manager");
        RoundManager round = roundObject.AddComponent<RoundManager>();
        int resultCount = 0;
        round.RoundResultDeclared += (_, _) => resultCount++;

        round.EndRound(TeamType.Blue, RoundEndReason.BombDefused);
        round.EndRound(TeamType.Red, RoundEndReason.BombExploded);

        Assert.That(round.HasWinner, Is.True);
        Assert.That(round.Winner, Is.EqualTo(TeamType.Blue));
        Assert.That(round.WinnerReason, Is.EqualTo(RoundEndReason.BombDefused));
        Assert.That(round.CurrentState, Is.EqualTo(RoundState.RoundEnd));
        Assert.That(resultCount, Is.EqualTo(1));
    }

    [TestCase(TeamType.Blue, "Defenders")]
    [TestCase(TeamType.Red, "Strikers")]
    public void WinnerNames_MapToRequestedTeams(TeamType team, string expected)
    {
        Assert.That(RoundManager.GetWinnerDisplayName(team), Is.EqualTo(expected));
    }
}
