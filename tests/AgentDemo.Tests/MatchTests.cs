using AgentDemo.Web.Game;

namespace AgentDemo.Tests;

public class MatchTests
{
    private static Match NewMatch(int bestOf = 3) =>
        new(new MatchSettings { BestOf = bestOf, Opponent = Opponent.TwoPlayer });

    private static void Play(Match m, params int[] moves)
    {
        foreach (var i in moves) Assert.True(m.TryPlay(i));
    }

    // Starter wins via top row; other player plays 3,4.
    private static void StarterWins(Match m) => Play(m, 0, 3, 1, 4, 2);

    private static void Draw(Match m) => Play(m, 0, 1, 2, 4, 3, 5, 7, 6, 8);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(7)]
    public void InvalidBestOf_Throws(int bestOf)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Match(new MatchSettings { BestOf = bestOf }));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    public void WinsNeeded(int bestOf, int needed)
    {
        Assert.Equal(needed, new MatchSettings { BestOf = bestOf }.WinsNeeded);
    }

    [Fact]
    public void BestOfOne_EndsAfterFirstWin()
    {
        var m = NewMatch(1);
        StarterWins(m);
        Assert.True(m.IsMatchOver);
        Assert.Equal(Player.X, m.Winner);
        Assert.Equal(1, m.ScoreX);
        Assert.False(m.TryPlay(8));
        Assert.Throws<InvalidOperationException>(m.StartNextRound);
    }

    [Fact]
    public void BestOfThree_ClinchesAtTwo_LoserStarts()
    {
        var m = NewMatch();
        StarterWins(m); // X wins round 1
        Assert.False(m.IsMatchOver);
        m.StartNextRound();
        Assert.Equal(2, m.RoundNumber);
        Assert.Equal(Player.O, m.Game.CurrentPlayer); // loser (O) starts
        Play(m, 0, 3, 1, 4, 2); // O starts and wins
        Assert.Equal(1, m.ScoreO);
        Assert.False(m.IsMatchOver);
        m.StartNextRound();
        Assert.Equal(Player.X, m.Game.CurrentPlayer); // loser X starts
        StarterWins(m);
        Assert.True(m.IsMatchOver);
        Assert.Equal(Player.X, m.Winner);
        Assert.Equal(2, m.ScoreX);
        Assert.Equal(1, m.ScoreO);
    }

    [Fact]
    public void Draw_GivesNoPoint_AndAlternatesStarter()
    {
        var m = NewMatch();
        Draw(m);
        Assert.Equal(0, m.ScoreX + m.ScoreO);
        Assert.Equal(1, m.Draws);
        Assert.False(m.IsMatchOver);
        m.StartNextRound();
        Assert.Equal(Player.O, m.Game.CurrentPlayer);
        Draw(m);
        m.StartNextRound();
        Assert.Equal(Player.X, m.Game.CurrentPlayer);
        Assert.Equal(2, m.Draws);
        Assert.Equal(3, m.RoundNumber);
    }

    [Fact]
    public void StartNextRound_RequiresFinishedRound()
    {
        var m = NewMatch();
        Play(m, 0);
        Assert.Throws<InvalidOperationException>(m.StartNextRound);
    }

    [Fact]
    public void ScoreRecordedOnce_AndNoMovesAfterRoundOver()
    {
        var m = NewMatch(5);
        StarterWins(m);
        Assert.False(m.TryPlay(8));
        Assert.Equal(1, m.ScoreX);
    }

    [Fact]
    public void Rematch_ResetsEverything_KeepsSettings()
    {
        var m = NewMatch(1);
        StarterWins(m);
        m.Rematch();
        Assert.False(m.IsMatchOver);
        Assert.Equal(Player.None, m.Winner);
        Assert.Equal(0, m.ScoreX + m.ScoreO + m.Draws);
        Assert.Equal(1, m.RoundNumber);
        Assert.Equal(Player.X, m.Game.CurrentPlayer);
        Assert.Equal(1, m.Settings.BestOf);
        Assert.True(m.TryPlay(0));
    }

    [Fact]
    public void Decay_RuleSetFlowsIntoRounds()
    {
        var m = new Match(new MatchSettings { Rules = RuleSet.Decay, Opponent = Opponent.TwoPlayer });
        Assert.Equal(RuleSet.Decay, m.Game.Rules);
        Play(m, 0, 3, 2, 4, 8, 7);
        Assert.True(m.TryPlay(5)); // removes 0, X: 2,8,5 wins
        Assert.Equal(Player.X, m.Game.Winner);
        m.StartNextRound();
        Assert.Equal(RuleSet.Decay, m.Game.Rules);
    }

    [Fact]
    public void ToRecord_SummarisesFinishedMatch()
    {
        var m = NewMatch(1);
        Assert.Throws<InvalidOperationException>(() => m.ToRecord(DateTimeOffset.UnixEpoch));
        StarterWins(m);
        var rec = m.ToRecord(DateTimeOffset.UnixEpoch);
        Assert.Equal(Player.X, rec.Winner);
        Assert.Equal(1, rec.ScoreX);
        Assert.Equal(1, rec.RoundsPlayed);
        Assert.Equal(DateTimeOffset.UnixEpoch, rec.CompletedAt);
        Assert.Equal(m.Settings, rec.Settings);
    }
}
