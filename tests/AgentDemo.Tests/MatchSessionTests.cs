using AgentDemo.Web.Game;

namespace AgentDemo.Tests;

public class MatchSessionTests
{
    private static MatchSession Ai(Player human, Difficulty d = Difficulty.Rookie, RuleSet rules = RuleSet.Classic, int bestOf = 3) =>
        new(new MatchSettings { Opponent = Opponent.Ai, HumanSide = human, Difficulty = d, Rules = rules, BestOf = bestOf }, new Random(3));

    [Fact]
    public void HumanIsO_AiMovesFirst()
    {
        var s = Ai(Player.O);
        Assert.True(s.IsAiTurn);
        Assert.False(s.CanHumanPlay(4));
        Assert.False(s.PlayHuman(4));
        var move = s.PlayAiMove();
        Assert.NotNull(move);
        Assert.Equal(Player.X, s.Game.Cells[move!.Value]);
        Assert.False(s.IsAiTurn);
        Assert.True(s.CanHumanPlay(Enumerable.Range(0, 9).First(i => i != move)));
    }

    [Fact]
    public void HumanIsX_HumanMovesFirst_AiCannotMove()
    {
        var s = Ai(Player.X);
        Assert.False(s.IsAiTurn);
        Assert.Null(s.PlayAiMove());
        Assert.True(s.PlayHuman(4));
        Assert.True(s.IsAiTurn);
    }

    [Fact]
    public void OneAiMovePerHumanMove()
    {
        var s = Ai(Player.X);
        s.PlayHuman(4);
        Assert.NotNull(s.PlayAiMove());
        Assert.Null(s.PlayAiMove());
        Assert.Equal(2, s.Game.Cells.Count(c => c != Player.None));
    }

    [Fact]
    public void HumanRejected_OnOccupiedOrOutOfRange()
    {
        var s = Ai(Player.X);
        s.PlayHuman(4);
        s.PlayAiMove();
        Assert.False(s.CanHumanPlay(4));
        Assert.False(s.CanHumanPlay(-1));
        Assert.False(s.CanHumanPlay(9));
    }

    [Fact]
    public void TwoPlayer_NoAi_BothSidesPlayHuman()
    {
        var s = new MatchSession(new MatchSettings { Opponent = Opponent.TwoPlayer });
        Assert.Null(s.Ai);
        Assert.False(s.IsAiTurn);
        Assert.True(s.PlayHuman(0));
        Assert.True(s.PlayHuman(1));
        Assert.Null(s.PlayAiMove());
        Assert.Equal(Player.O, s.Game.Cells[1]);
    }

    [Theory]
    [InlineData(Difficulty.Rookie, typeof(RookieStrategy))]
    [InlineData(Difficulty.Tactician, typeof(TacticianStrategy))]
    [InlineData(Difficulty.Oracle, typeof(OracleStrategy))]
    public void StrategyMatchesDifficulty(Difficulty d, Type expected)
    {
        Assert.IsType(expected, Ai(Player.X, d).Ai);
    }

    [Fact]
    public void RoundEnd_LocksInput_AndNextRoundHandsStarterToLoser()
    {
        var s = Ai(Player.X, Difficulty.Oracle, bestOf: 3);
        var m = s.Match;
        m.TryPlay(0);
        m.TryPlay(3);
        m.TryPlay(1);
        m.TryPlay(4);
        m.TryPlay(2);
        Assert.True(s.Game.IsOver);
        Assert.False(s.IsAiTurn);
        Assert.False(s.CanHumanPlay(5));
        Assert.Null(s.PlayAiMove());

        m.StartNextRound();
        Assert.True(s.IsAiTurn);
        Assert.NotNull(s.PlayAiMove());
    }

    [Fact]
    public void MatchOver_NoMovesForAnyone()
    {
        var s = Ai(Player.X, bestOf: 1);
        var m = s.Match;
        foreach (var i in new[] { 0, 3, 1, 4, 2 }) m.TryPlay(i);
        Assert.True(m.IsMatchOver);
        Assert.False(s.CanHumanPlay(8));
        Assert.Null(s.PlayAiMove());
    }

    [Fact]
    public void Decay_PassesThrough_AndAiNeverPlaysOwnFadingCell()
    {
        var s = Ai(Player.X, Difficulty.Tactician, RuleSet.Decay, bestOf: 5);
        Assert.Equal(RuleSet.Decay, s.Game.Rules);
        for (var i = 0; i < 40 && !s.Game.IsOver; i++)
        {
            if (s.IsAiTurn)
            {
                var fading = s.Game.FadingCell;
                var move = s.PlayAiMove();
                Assert.NotNull(move);
                Assert.NotEqual(fading, move);
            }
            else
            {
                Assert.True(s.PlayHuman(s.Game.LegalMoves[0]));
            }
        }
    }
}
