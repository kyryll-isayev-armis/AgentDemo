using AgentDemo.Web.Game;

namespace AgentDemo.Tests;

public class AiTests
{
    private static GameState Setup(RuleSet rules, params int[] moves)
    {
        var g = new GameState(rules);
        foreach (var m in moves) Assert.True(g.TryPlay(m));
        return g;
    }

    // Plays every possible opponent reply; the Oracle must never lose.
    private static void ExploreOracleNeverLoses(GameState s, Player oracle, IAiStrategy ai, ref int games)
    {
        if (s.IsOver)
        {
            games++;
            Assert.NotEqual(oracle == Player.X ? Player.O : Player.X, s.Winner);
            return;
        }

        if (s.CurrentPlayer == oracle)
        {
            var move = ai.Choose(s);
            Assert.Contains(move, s.LegalMoves);
            var next = s.Clone();
            Assert.True(next.TryPlay(move));
            ExploreOracleNeverLoses(next, oracle, ai, ref games);
            return;
        }

        foreach (var move in s.LegalMoves)
        {
            var next = s.Clone();
            next.TryPlay(move);
            ExploreOracleNeverLoses(next, oracle, ai, ref games);
        }
    }

    [Theory]
    [InlineData(Player.X)]
    [InlineData(Player.O)]
    public void Oracle_NeverLoses_AgainstAnyOpponent(Player side)
    {
        var games = 0;
        ExploreOracleNeverLoses(new GameState(), side, new OracleStrategy(), ref games);
        Assert.True(games > 0);
    }

    [Fact]
    public void Oracle_IsDeterministic()
    {
        var a = new OracleStrategy().Choose(Setup(RuleSet.Classic, 0), new Random(1));
        var b = new OracleStrategy().Choose(Setup(RuleSet.Classic, 0), new Random(99));
        Assert.Equal(a, b);
        Assert.Equal(4, a);
    }

    [Fact]
    public void Oracle_TakesImmediateWin_AndBlocks()
    {
        // X: 0,1  O: 3,4 -> X to move wins at 2
        Assert.Equal(2, new OracleStrategy().Choose(Setup(RuleSet.Classic, 0, 3, 1, 4)));
        // X: 0,1  O: 4 -> O must block at 2
        Assert.Equal(2, new OracleStrategy().Choose(Setup(RuleSet.Classic, 0, 4, 1)));
    }

    [Fact]
    public void Oracle_Decay_UsesDepthLimitedSearch_AndPlaysLegally()
    {
        var g = Setup(RuleSet.Decay, 0, 3, 1, 4);
        var move = new OracleStrategy().Choose(g);
        Assert.Equal(2, move);

        var rng = new Random(7);
        var game = new GameState(RuleSet.Decay);
        for (var i = 0; i < 40 && !game.IsOver; i++)
        {
            var m = game.CurrentPlayer == Player.X ? new OracleStrategy().Choose(game) : new RookieStrategy().Choose(game, rng);
            Assert.True(game.TryPlay(m));
        }
    }

    public static TheoryData<int[], int> WinTable => new()
    {
        { new[] { 0, 3, 1, 4 }, 2 },      // X completes top row
        { new[] { 0, 1, 3, 4, 8 }, 7 },   // O completes middle column
        { new[] { 2, 0, 4, 1 }, 6 },      // X completes anti-diagonal
    };

    [Theory]
    [MemberData(nameof(WinTable))]
    public void Tactician_TakesWin(int[] moves, int expected)
    {
        Assert.Equal(expected, new TacticianStrategy().Choose(Setup(RuleSet.Classic, moves)));
    }

    public static TheoryData<int[], int> BlockTable => new()
    {
        { new[] { 0, 4, 1 }, 2 },         // O blocks top row
        { new[] { 0, 4, 8, 1 }, 7 },      // X blocks column 1 (O: 4,1)
        { new[] { 6, 4, 8 }, 7 },         // O blocks bottom row
    };

    [Theory]
    [MemberData(nameof(BlockTable))]
    public void Tactician_BlocksOpponent(int[] moves, int expected)
    {
        Assert.Equal(expected, new TacticianStrategy().Choose(Setup(RuleSet.Classic, moves)));
    }

    [Fact]
    public void Tactician_PrefersWinOverBlock()
    {
        // X: 0,1 ; O: 3,4 ; X to move -> win at 2 rather than block at 5
        Assert.Equal(2, new TacticianStrategy().Choose(Setup(RuleSet.Classic, 0, 3, 1, 4)));
    }

    [Fact]
    public void Tactician_Preference_Center_Corner_Side()
    {
        var s = new TacticianStrategy();
        Assert.Equal(4, s.Choose(new GameState()));
        Assert.Contains(s.Choose(Setup(RuleSet.Classic, 4), new Random(3)), new[] { 0, 2, 6, 8 });
        // X: 4,0  O: 8,... build a position with no corners free and no threats
        var g = Setup(RuleSet.Classic, 4, 0, 2, 6); // X:4,2 O:0,6 -> X to move... O threatens 3, X threatens 3? X has 4,2 -> 6 taken
        var move = s.Choose(g);
        Assert.Equal(3, move); // blocks O's 0-3-6
    }

    [Fact]
    public void Tactician_Side_WhenNoCornersOrCenter()
    {
        // X: 0,4,5,6  O: 2,3,8  -> only sides 1 and 7 free, nobody has won, O to move
        var g = Setup(RuleSet.Classic, 0, 2, 4, 3, 5, 8, 6);
        Assert.False(g.IsOver);
        var move = new TacticianStrategy().Choose(g);
        Assert.Contains(move, new[] { 1, 7 });
    }

    [Fact]
    public void Rookie_OnlyLegalMoves_WithSeed()
    {
        var ai = new RookieStrategy();
        var rng = new Random(12345);
        for (var trial = 0; trial < 200; trial++)
        {
            var g = new GameState();
            while (!g.IsOver)
            {
                var m = ai.Choose(g, rng);
                Assert.Contains(m, g.LegalMoves);
                Assert.True(g.TryPlay(m));
            }
        }
    }

    [Fact]
    public void Rookie_SameSeed_SameChoices_AndIsBeatable()
    {
        var g = Setup(RuleSet.Classic, 0, 4, 1); // O must block at 2; Rookie does not always
        var picks = Enumerable.Range(0, 50).Select(i => new RookieStrategy().Choose(g, new Random(i))).ToList();
        Assert.Contains(picks, p => p != 2);
        Assert.Equal(new RookieStrategy().Choose(g, new Random(5)), new RookieStrategy().Choose(g, new Random(5)));
    }

    [Theory]
    [InlineData(Difficulty.Rookie)]
    [InlineData(Difficulty.Tactician)]
    [InlineData(Difficulty.Oracle)]
    public void AllStrategies_ThrowWhenGameOver_AndStayLegalInDecay(Difficulty d)
    {
        var ai = AiFactory.Create(d);
        var over = Setup(RuleSet.Classic, 0, 3, 1, 4, 2);
        Assert.Throws<InvalidOperationException>(() => ai.Choose(over));

        var g = new GameState(RuleSet.Decay);
        var rng = new Random(42);
        for (var i = 0; i < 30 && !g.IsOver; i++)
        {
            var m = ai.Choose(g, rng);
            Assert.Contains(m, g.LegalMoves);
            g.TryPlay(m);
        }
    }
}
