using AgentDemo.Web.Game;

namespace AgentDemo.Tests;

public class GameStateTests
{
    private static GameState Play(params int[] moves)
    {
        var game = new GameState();
        foreach (var m in moves)
        {
            Assert.True(game.TryPlay(m));
        }
        return game;
    }

    public static TheoryData<int[]> WinLines => new()
    {
        { new[] { 0, 1, 2 } }, { new[] { 3, 4, 5 } }, { new[] { 6, 7, 8 } },
        { new[] { 0, 3, 6 } }, { new[] { 1, 4, 7 } }, { new[] { 2, 5, 8 } },
        { new[] { 0, 4, 8 } }, { new[] { 2, 4, 6 } }
    };

    [Theory]
    [MemberData(nameof(WinLines))]
    public void X_WinsOnEveryLine(int[] line)
    {
        var fillers = Enumerable.Range(0, 9).Except(line).Take(2).ToArray();
        var game = Play(line[0], fillers[0], line[1], fillers[1], line[2]);

        Assert.Equal(Player.X, game.Winner);
        Assert.Equal(line, game.WinningLine);
        Assert.True(game.IsOver);
        Assert.False(game.IsDraw);
    }

    [Fact]
    public void O_CanWin()
    {
        var game = Play(0, 3, 1, 4, 8, 5);

        Assert.Equal(Player.O, game.Winner);
        Assert.Equal(new[] { 3, 4, 5 }, game.WinningLine);
    }

    [Fact]
    public void FullBoardWithoutWinner_IsDraw()
    {
        var game = Play(0, 1, 2, 4, 3, 5, 7, 6, 8);

        Assert.True(game.IsDraw);
        Assert.Equal(Player.None, game.Winner);
        Assert.Empty(game.WinningLine);
        Assert.True(game.IsOver);
    }

    [Fact]
    public void LastMoveWinning_IsWinNotDraw()
    {
        var game = Play(0, 1, 2, 3, 4, 5, 7, 6, 8);

        Assert.Equal(Player.X, game.Winner);
        Assert.False(game.IsDraw);
    }

    [Fact]
    public void OccupiedCell_IsRejectedAndTurnKept()
    {
        var game = Play(4);

        Assert.False(game.TryPlay(4));
        Assert.Equal(Player.O, game.CurrentPlayer);
        Assert.Equal(Player.X, game.Cells[4]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void OutOfRange_IsRejected(int index)
    {
        var game = new GameState();

        Assert.False(game.TryPlay(index));
        Assert.Equal(Player.X, game.CurrentPlayer);
    }

    [Fact]
    public void NoMovesAfterWin()
    {
        var game = Play(0, 3, 1, 4, 2);

        Assert.False(game.TryPlay(8));
        Assert.Equal(Player.None, game.Cells[8]);
    }

    [Fact]
    public void NoMovesAfterDraw()
    {
        var game = Play(0, 1, 2, 4, 3, 5, 7, 6, 8);

        Assert.False(game.TryPlay(0));
    }

    [Fact]
    public void TurnsAlternate_XFirst()
    {
        var game = new GameState();
        Assert.Equal(Player.X, game.CurrentPlayer);
        game.TryPlay(0);
        Assert.Equal(Player.O, game.CurrentPlayer);
        game.TryPlay(1);
        Assert.Equal(Player.X, game.CurrentPlayer);
        Assert.Equal(Player.X, game.Cells[0]);
        Assert.Equal(Player.O, game.Cells[1]);
    }

    [Fact]
    public void Restart_ResetsEverything()
    {
        var game = Play(0, 3, 1, 4, 2);

        game.Restart();

        Assert.All(game.Cells, c => Assert.Equal(Player.None, c));
        Assert.Equal(Player.X, game.CurrentPlayer);
        Assert.Equal(Player.None, game.Winner);
        Assert.Empty(game.WinningLine);
        Assert.False(game.IsOver);
        Assert.True(game.TryPlay(0));
    }
}
