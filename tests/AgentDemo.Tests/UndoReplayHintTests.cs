using AgentDemo.Web.Game;
using AgentDemo.Web.Services;

namespace AgentDemo.Tests;

public class UndoReplayHintTests
{
    private sealed class FakeStore : ILocalStore
    {
        public Dictionary<string, string> Data { get; } = [];

        public Task<string?> GetAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string value)
        {
            Data[key] = value;
            return Task.CompletedTask;
        }
    }

    private static void AssertSame(GameState expected, GameState actual)
    {
        Assert.Equal(expected.Cells, actual.Cells);
        Assert.Equal(expected.CurrentPlayer, actual.CurrentPlayer);
        Assert.Equal(expected.FadingCell, actual.FadingCell);
        Assert.Equal(expected.LastMove, actual.LastMove);
        Assert.Equal(expected.Moves, actual.Moves);
        Assert.Equal(expected.Winner, actual.Winner);
        Assert.Equal(expected.IsDraw, actual.IsDraw);
        Assert.Equal(expected.WinningLine, actual.WinningLine);
    }

    [Theory]
    [InlineData(RuleSet.Classic, Player.X)]
    [InlineData(RuleSet.Classic, Player.O)]
    [InlineData(RuleSet.Decay, Player.X)]
    [InlineData(RuleSet.Decay, Player.O)]
    public void Undo_IsExactInverseOfPlay_OverRandomGames(RuleSet rules, Player starter)
    {
        var rng = new Random(2024);
        for (var game = 0; game < 100; game++)
        {
            var g = new GameState(rules, starter);
            var snapshots = new List<GameState> { g.Clone() };
            for (var i = 0; i < 40 && !g.IsOver; i++)
            {
                g.TryPlay(g.LegalMoves[rng.Next(g.LegalMoves.Count)]);
                snapshots.Add(g.Clone());
            }

            for (var i = snapshots.Count - 2; i >= 0; i--)
            {
                Assert.True(g.Undo());
                AssertSame(snapshots[i], g);
            }
            Assert.False(g.Undo());
        }
    }

    [Fact]
    public void Undo_AfterWin_ReopensTheRound()
    {
        var g = new GameState();
        foreach (var m in new[] { 0, 3, 1, 4, 2 }) g.TryPlay(m);
        Assert.Equal(Player.X, g.Winner);

        Assert.True(g.Undo());

        Assert.Equal(Player.None, g.Winner);
        Assert.Empty(g.WinningLine);
        Assert.Equal(Player.X, g.CurrentPlayer);
        Assert.True(g.TryPlay(2));
    }

    [Theory]
    [InlineData(RuleSet.Classic)]
    [InlineData(RuleSet.Decay)]
    public void Replay_FinalStateMatchesLivePlay(RuleSet rules)
    {
        var rng = new Random(11);
        for (var n = 0; n < 100; n++)
        {
            var live = new GameState(rules, n % 2 == 0 ? Player.X : Player.O);
            while (!live.IsOver && live.Moves.Count < 40) live.TryPlay(live.LegalMoves[rng.Next(live.LegalMoves.Count)]);

            var round = new RoundRecord(live.Starter, live.Moves.ToArray());
            AssertSame(live, Replay.StateAt(rules, round, int.MaxValue));
            AssertSame(new GameState(rules, live.Starter), Replay.StateAt(rules, round, 0));
            Assert.Equal(live.Moves.Count > 0 ? live.Moves[0] : (int?)null, Replay.StateAt(rules, round, 1).LastMove);
        }
    }

    [Fact]
    public void Replay_Decay_ShowsRemovalAtStep()
    {
        var round = new RoundRecord(Player.X, [0, 3, 1, 4, 5, 2, 8]);
        Assert.Equal(Player.X, Replay.StateAt(RuleSet.Decay, round, 6).Cells[0]);
        Assert.Equal(Player.None, Replay.StateAt(RuleSet.Decay, round, 7).Cells[0]);
    }

    [Fact]
    public void Replay_IsValid_RejectsBadRounds()
    {
        Assert.True(Replay.IsValid(RuleSet.Classic, new RoundRecord(Player.X, [0, 3, 1, 4, 2])));
        Assert.False(Replay.IsValid(RuleSet.Classic, new RoundRecord(Player.X, [0, 3, 1, 4])));          // unfinished
        Assert.False(Replay.IsValid(RuleSet.Classic, new RoundRecord(Player.X, [0, 0, 1, 4, 2])));       // occupied
        Assert.False(Replay.IsValid(RuleSet.Classic, new RoundRecord(Player.X, [0, 3, 1, 4, 2, 5])));    // move after end
        Assert.False(Replay.IsValid(RuleSet.Classic, new RoundRecord(Player.X, [9])));
        Assert.False(Replay.IsValid(RuleSet.Classic, new RoundRecord(Player.None, [0, 3, 1, 4, 2])));
        Assert.False(Replay.IsValid(RuleSet.Classic, new RoundRecord(Player.X, [])));
        Assert.False(Replay.IsValid(RuleSet.Classic, null));
        Assert.False(Replay.IsValid(RuleSet.Decay, new RoundRecord(Player.X, Enumerable.Repeat(0, 61).ToArray())));
    }

    private static MatchSession Session(Player human, Difficulty d = Difficulty.Rookie, int seed = 1) =>
        new(new MatchSettings { Opponent = Opponent.Ai, HumanSide = human, Difficulty = d }, new Random(seed));

    [Fact]
    public void Session_Undo_TakesBackHumanMoveAndAiReply()
    {
        var s = Session(Player.X);
        Assert.False(s.CanUndo);

        Assert.True(s.PlayHuman(4));
        Assert.False(s.CanUndo); // AI turn
        Assert.NotNull(s.PlayAiMove());
        Assert.True(s.CanUndo);

        Assert.True(s.Undo());
        Assert.Empty(s.Game.Moves);
        Assert.Equal(Player.X, s.Game.CurrentPlayer);
        Assert.True(s.Match.Assisted);
        Assert.False(s.Undo());
    }

    [Fact]
    public void Session_Undo_WhenAiOpened_KeepsAiOpening()
    {
        var s = Session(Player.O);
        Assert.True(s.IsAiTurn);
        var opening = s.PlayAiMove();
        Assert.False(s.CanUndo); // nothing human to take back

        Assert.True(s.PlayHuman(s.Game.LegalMoves[0]));
        s.PlayAiMove();
        Assert.Equal(3, s.Game.Moves.Count);

        Assert.True(s.Undo());
        Assert.Equal([opening!.Value], s.Game.Moves);
        Assert.Equal(Player.O, s.Game.CurrentPlayer);
    }

    [Fact]
    public void Session_Undo_TwoPlayer_RevertsOneMove()
    {
        var s = new MatchSession(new MatchSettings { Opponent = Opponent.TwoPlayer });
        s.PlayHuman(0);
        s.PlayHuman(1);
        Assert.True(s.Undo());
        Assert.Equal([0], s.Game.Moves);
        Assert.Equal(Player.O, s.Game.CurrentPlayer);
    }

    [Fact]
    public void Session_Undo_UnavailableAfterRoundOver()
    {
        var s = new MatchSession(new MatchSettings { Opponent = Opponent.TwoPlayer, BestOf = 3 });
        foreach (var m in new[] { 0, 3, 1, 4, 2 }) s.PlayHuman(m);
        Assert.True(s.Game.IsOver);
        Assert.False(s.CanUndo);
        Assert.False(s.Undo());
        Assert.False(s.CanHint);
        Assert.Null(s.Hint());
        Assert.False(s.Match.Assisted);
    }

    [Theory]
    [InlineData(RuleSet.Classic)]
    [InlineData(RuleSet.Decay)]
    public void Hint_IsLegal_NeverMutates_AndMarksAssisted(RuleSet rules)
    {
        var s = new MatchSession(new MatchSettings { Opponent = Opponent.TwoPlayer, Rules = rules });
        var rng = new Random(5);
        while (!s.Game.IsOver && s.Game.Moves.Count < 30)
        {
            var before = s.Game.Clone();
            var hint = s.Hint();
            Assert.NotNull(hint);
            Assert.Contains(hint!.Value, s.Game.LegalMoves);
            AssertSame(before, s.Game);
            s.PlayHuman(s.Game.LegalMoves[rng.Next(s.Game.LegalMoves.Count)]);
        }
        Assert.True(s.Match.Assisted);
    }

    [Fact]
    public void Hint_FindsWinningMove()
    {
        var g = new GameState();
        foreach (var m in new[] { 0, 3, 1, 4 }) g.TryPlay(m);
        Assert.Equal(2, HintService.Suggest(g));
        Assert.Null(HintService.Suggest(new GameState().Also(x => { foreach (var m in new[] { 0, 3, 1, 4, 2 }) x.TryPlay(m); })));
    }

    [Fact]
    public void Match_Record_CarriesAssistedFlagAndRounds()
    {
        var m = new Match(new MatchSettings { BestOf = 1, Opponent = Opponent.TwoPlayer });
        m.MarkAssisted();
        foreach (var i in new[] { 0, 3, 1, 4, 2 }) m.TryPlay(i);
        var rec = m.ToRecord(DateTimeOffset.UnixEpoch);

        Assert.True(rec.Assisted);
        var round = Assert.Single(rec.Rounds);
        Assert.Equal(Player.X, round.Starter);
        Assert.Equal([0, 3, 1, 4, 2], round.Moves);

        m.Rematch();
        Assert.False(m.Assisted);
        foreach (var i in new[] { 0, 3, 1, 4, 2 }) m.TryPlay(i);
        Assert.False(m.ToRecord(DateTimeOffset.UnixEpoch).Assisted);
        Assert.Single(m.ToRecord(DateTimeOffset.UnixEpoch).Rounds);
    }

    [Fact]
    public async Task Assisted_Match_IsExcludedFromStats_ButKeptInHistory()
    {
        var svc = new ProfileService(new FakeStore());
        var settings = new MatchSettings { Opponent = Opponent.Ai, Difficulty = Difficulty.Rookie, BestOf = 1 };
        var rec = new MatchRecord(settings, 1, 0, 0, 1, Player.X, DateTimeOffset.UnixEpoch) { Assisted = true };

        var data = await svc.RecordMatchAsync(rec);

        Assert.Equal(0, data.Stats.Total.Total);
        Assert.Empty(data.Achievements);
        Assert.True(Assert.Single(data.History).Assisted);
    }

    [Fact]
    public async Task Rounds_RoundTrip_AndInvalidRoundsAreDropped()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);
        var good = new RoundRecord(Player.X, [0, 3, 1, 4, 2]);
        var bad = new RoundRecord(Player.X, [0, 0, 1]);
        var rec = new MatchRecord(new MatchSettings { Opponent = Opponent.TwoPlayer, BestOf = 1 }, 1, 0, 0, 1, Player.X, DateTimeOffset.UnixEpoch)
        {
            Rounds = [good, bad]
        };
        await svc.RecordMatchAsync(rec);

        var loaded = await new ProfileService(store).LoadAsync();
        var round = Assert.Single(Assert.Single(loaded.History).Rounds);
        Assert.Equal(good.Moves, round.Moves);
        Assert.Equal(Player.X, round.Starter);
    }

    [Fact]
    public async Task Legacy_History_WithoutNewFields_StillLoads()
    {
        var store = new FakeStore();
        store.Data[ProfileService.StorageKey] =
            "{\"version\":1,\"history\":[{\"settings\":{\"bestOf\":3},\"scoreX\":2,\"scoreO\":1,\"draws\":0,\"rounds\":3," +
            "\"winner\":\"X\",\"completedAt\":\"2026-01-01T00:00:00.0000000+00:00\"}]}";

        var data = await new ProfileService(store).LoadAsync();

        var rec = Assert.Single(data.History);
        Assert.False(rec.Assisted);
        Assert.Empty(rec.Rounds);
        Assert.Empty(data.Achievements);
        Assert.Equal(0, data.Challenge.Streak);
    }

    [Fact]
    public async Task Corrupt_Moves_AreIgnored()
    {
        var store = new FakeStore();
        store.Data[ProfileService.StorageKey] =
            "{\"version\":1,\"history\":[{\"scoreX\":1,\"winner\":\"X\",\"completedAt\":\"2026-01-01T00:00:00+00:00\"," +
            "\"roundLog\":[{\"starter\":\"X\",\"moves\":[0,\"a\",2]},{\"starter\":\"Q\",\"moves\":[0]},{\"moves\":5}," +
            "{\"starter\":\"X\",\"moves\":[0,3,1,4,2]}]}]}";

        var data = await new ProfileService(store).LoadAsync();

        var round = Assert.Single(Assert.Single(data.History).Rounds);
        Assert.Equal([0, 3, 1, 4, 2], round.Moves);
    }
}

internal static class Ext
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
