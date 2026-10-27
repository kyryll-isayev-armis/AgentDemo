using AgentDemo.Web.Game;

namespace AgentDemo.Tests;

public class DecayRuleTests
{
    private static GameState Play(params int[] moves)
    {
        var g = new GameState(RuleSet.Decay);
        foreach (var m in moves) Assert.True(g.TryPlay(m));
        return g;
    }

    [Fact]
    public void FourthMark_RemovesOldest()
    {
        // X: 0,1,5 then 4th mark at 8 ; O: 3,4,2 (no wins)
        var g = Play(0, 3, 1, 4, 5, 2);
        Assert.Equal(0, g.FadingCell);
        Assert.True(g.TryPlay(8));
        Assert.Equal(Player.None, g.Cells[0]);
        Assert.Equal(Player.X, g.Cells[8]);
        Assert.Equal(3, g.Cells.Count(c => c == Player.X));
        Assert.Equal(8, g.LastMove);
    }

    [Fact]
    public void FadingCell_NullUntilThreeMarks_AndPerPlayer()
    {
        var g = Play(0, 3, 1, 4);
        Assert.Null(g.FadingCell);
        g.TryPlay(5);
        Assert.Null(g.FadingCell); // O has 2 marks
        g.TryPlay(2);
        Assert.Equal(0, g.FadingCell); // X to move
        g.TryPlay(8);
        Assert.Equal(3, g.FadingCell); // O's oldest
    }

    [Fact]
    public void NeverDraws_BoardNeverFull()
    {
        var g = new GameState(RuleSet.Decay);
        var rng = new Random(1);
        for (var i = 0; i < 500 && !g.IsOver; i++)
        {
            g.TryPlay(new RookieStrategy().Choose(g, rng));
            Assert.False(g.IsDraw);
            Assert.True(g.Cells.Count(c => c != Player.None) <= 6);
        }
    }

    [Fact]
    public void Win_IsCheckedAfterRemoval()
    {
        // X: 0,4,8? build: X plays 0,1,5 ; O plays 3,4,6 ; X's 4th mark at 2 removes 0 -> X: 1,5,2 (no line) ; 
        var g = Play(0, 3, 1, 4, 5, 6);
        g.TryPlay(2);
        Assert.Equal(Player.None, g.Winner);
        Assert.Equal(Player.None, g.Cells[0]);
    }

    [Fact]
    public void Win_AfterRemovalCompletesLine()
    {
        // X: 0,2,8 ; O: 3,4,7. X plays 6 -> removes 0 -> X: 2,8,6 no. Use: X plays 5 after removal => X: 2,8,5 = column 2 win.
        var g = Play(0, 3, 2, 4, 8, 7);
        Assert.True(g.TryPlay(5));
        Assert.Equal(Player.X, g.Winner);
        Assert.Equal(new[] { 2, 5, 8 }, g.WinningLine);
    }

    [Fact]
    public void Line_ThatNeedsRemovedMark_DoesNotWin()
    {
        // X: 0,1,8 ; O: 3,4,7 ; X plays 2: oldest (0) is removed first, so 0-1-2 is NOT a win.
        var g = Play(0, 3, 1, 4, 8, 7);
        Assert.False(g.WouldWin(Player.X, 2));
        g.TryPlay(2);
        Assert.Equal(Player.None, g.Winner);
    }

    [Fact]
    public void OwnFadingCell_IsNotALegalMove_OldestMarkVanishesOnlyWithFourthPlacement()
    {
        var g = Play(0, 3, 1, 4, 5, 2);
        Assert.Equal(0, g.FadingCell);
        Assert.False(g.TryPlay(0));
        Assert.DoesNotContain(0, g.LegalMoves);
        Assert.Equal(Player.X, g.Cells[0]);
    }

    [Fact]
    public void LegalMoves_NeverIncludeFadingCell_DuringPlayout()
    {
        var g = new GameState(RuleSet.Decay);
        var rng = new Random(7);
        for (var i = 0; i < 200 && !g.IsOver; i++)
        {
            if (g.FadingCell is { } fading) Assert.DoesNotContain(fading, g.LegalMoves);
            g.TryPlay(new RookieStrategy().Choose(g, rng));
        }
    }

    [Fact]
    public void Clone_IsIndependent_AndKeepsQueue()
    {
        var g = Play(0, 3, 1, 4, 5, 2);
        var c = g.Clone();
        c.TryPlay(8);
        Assert.Equal(Player.X, g.Cells[0]);
        Assert.Equal(Player.None, c.Cells[0]);
        Assert.Equal(0, g.FadingCell);
        Assert.Equal(3, c.FadingCell);
        Assert.Equal(RuleSet.Decay, c.Rules);
    }

    [Fact]
    public void Restart_ClearsDecayQueues()
    {
        var g = Play(0, 3, 1, 4, 5, 2);
        g.Restart();
        Assert.Null(g.FadingCell);
        Assert.Null(g.LastMove);
        Assert.Equal(9, g.LegalMoves.Count);
    }

    [Fact]
    public void Classic_LastMoveAndLegalMoves()
    {
        var g = new GameState();
        Assert.Null(g.LastMove);
        Assert.Null(g.FadingCell);
        g.TryPlay(4);
        Assert.Equal(4, g.LastMove);
        Assert.Equal(8, g.LegalMoves.Count);
        Assert.DoesNotContain(4, g.LegalMoves);
    }

    [Fact]
    public void Starter_CanBeO()
    {
        var g = new GameState(RuleSet.Classic, Player.O);
        Assert.Equal(Player.O, g.CurrentPlayer);
        g.TryPlay(0);
        Assert.Equal(Player.O, g.Cells[0]);
        g.Restart();
        Assert.Equal(Player.O, g.CurrentPlayer);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameState(RuleSet.Classic, Player.None));
    }
}
