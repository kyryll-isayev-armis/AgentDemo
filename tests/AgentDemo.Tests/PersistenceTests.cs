using AgentDemo.Web.Game;
using AgentDemo.Web.Game.Run;
using AgentDemo.Web.Services;

namespace AgentDemo.Tests;

public class PersistenceTests
{
    private sealed class MemoryStore : ILocalStore
    {
        private int _active;

        public Dictionary<string, string> Data { get; } = [];

        public int MaxConcurrent { get; private set; }

        public int Writes { get; private set; }

        public TimeSpan WriteDelay { get; init; }

        public Task<string?> GetAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);

        public async Task SetAsync(string key, string value)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _active));
            try
            {
                if (WriteDelay > TimeSpan.Zero) await Task.Delay(WriteDelay);
                Data[key] = value;
                Writes++;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private static MatchSession Session(RuleSet rules, Player human = Player.X, Opponent opponent = Opponent.Ai) =>
        new(new MatchSettings { Rules = rules, HumanSide = human, Opponent = opponent, Difficulty = Difficulty.Rookie, BestOf = 3 }, new Random(5));

    private static async Task<Match?> SaveAndReloadAsync(MemoryStore store, Match match)
    {
        await new ProfileService(store).SaveMatchAsync(match.ToSnapshot());
        var loaded = await new ProfileService(store).LoadAsync();
        return Match.Restore(loaded.ActiveMatch);
    }

    // ---- run content version ----

    [Fact]
    public async Task RunWithOlderContentVersionIsRetiredAndProfileKept()
    {
        var store = new MemoryStore();
        var svc = new ProfileService(store);
        await svc.SaveSettingsAsync(new UserSettings { Handle = "NEO" });
        await svc.SaveRunAsync(new RunSession(7).Save());
        Assert.Contains("\"contentVersion\":1", store.Data[ProfileService.StorageKey]);

        store.Data[ProfileService.StorageKey] = store.Data[ProfileService.StorageKey].Replace("\"contentVersion\":1", "\"contentVersion\":0");
        var loaded = await new ProfileService(store).LoadAsync();

        Assert.Null(loaded.ActiveRun);
        Assert.True(loaded.RunRetired);
        Assert.Equal("NEO", loaded.Settings.Handle);
        Assert.DoesNotContain("\"run\"", store.Data[ProfileService.StorageKey]);
        Assert.False(store.Data.ContainsKey(ProfileService.CorruptKey));
    }

    [Fact]
    public async Task RunWithoutContentVersionOrUnknownActionIsRetiredNotCrashing()
    {
        foreach (var run in new[]
                 {
                     "{\"version\":1,\"seed\":3,\"actions\":[]}",
                     "{\"version\":1,\"contentVersion\":1,\"seed\":3,\"actions\":[\"Z99\"]}",
                     "{\"version\":1,\"contentVersion\":1,\"seed\":\"x\",\"actions\":[]}",
                     "\"junk\""
                 })
        {
            var store = new MemoryStore();
            store.Data[ProfileService.StorageKey] = "{\"version\":2,\"settings\":{\"handle\":\"KEEP\"},\"run\":" + run + "}";
            var loaded = await new ProfileService(store).LoadAsync();
            Assert.Null(loaded.ActiveRun);
            Assert.True(loaded.RunRetired, run);
            Assert.Equal("KEEP", loaded.Settings.Handle);
        }
    }

    [Fact]
    public async Task CurrentRunSurvivesReload()
    {
        var store = new MemoryStore();
        var session = new RunSession(11);
        await new ProfileService(store).SaveRunAsync(session.Save());
        var loaded = await new ProfileService(store).LoadAsync();
        Assert.False(loaded.RunRetired);
        Assert.NotNull(RunSession.Resume(loaded.ActiveRun));
    }

    // ---- profile migration and corruption ----

    [Fact]
    public async Task VersionOneProfileIsMigratedNotWiped()
    {
        var store = new MemoryStore();
        store.Data[ProfileService.StorageKey] =
            "{\"version\":1,\"settings\":{\"theme\":\"toxic\",\"handle\":\"OLD\",\"sound\":true},\"stats\":{\"Rookie\":{\"wins\":4,\"losses\":1,\"draws\":2}}}";

        var loaded = await new ProfileService(store).LoadAsync();

        Assert.Equal("OLD", loaded.Settings.Handle);
        Assert.Equal(Themes.Toxic, loaded.Settings.Theme);
        Assert.Equal(4, loaded.Stats[Difficulty.Rookie].Wins);
        Assert.Contains($"\"version\":{ProfileService.Version}", store.Data[ProfileService.StorageKey]);
        Assert.False(store.Data.ContainsKey(ProfileService.CorruptKey));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"settings\":{}}")]
    [InlineData("{\"version\":99,\"settings\":{\"handle\":\"FUTURE\"}}")]
    [InlineData("{\"version\":0}")]
    public async Task UnreadableProfileIsBackedUpAndDefaultsReturned(string raw)
    {
        var store = new MemoryStore();
        store.Data[ProfileService.StorageKey] = raw;

        var loaded = await new ProfileService(store).LoadAsync();

        Assert.Equal(string.Empty, loaded.Settings.Handle);
        Assert.Empty(loaded.History);
        Assert.Equal(raw, store.Data[ProfileService.CorruptKey]);
    }

    [Fact]
    public async Task EmptyStoreDoesNotCreateBackup()
    {
        var store = new MemoryStore();
        await new ProfileService(store).LoadAsync();
        Assert.Empty(store.Data);
    }

    // ---- Classic / Decay match save + resume ----

    [Fact]
    public async Task ClassicMatchRoundTripsMidRound()
    {
        var session = Session(RuleSet.Classic, opponent: Opponent.TwoPlayer);
        foreach (var cell in new[] { 4, 0, 8 }) Assert.True(session.PlayHuman(cell));

        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);

        Assert.NotNull(restored);
        Assert.Equal(session.Game.Cells, restored.Game.Cells);
        Assert.Equal(session.Game.CurrentPlayer, restored.Game.CurrentPlayer);
        Assert.Equal(session.Game.Moves, restored.Game.Moves);
        Assert.Equal(RuleSet.Classic, restored.Settings.Rules);
    }

    [Fact]
    public async Task MatchRoundTripKeepsScoresRoundStarterAndAssistedAcrossRounds()
    {
        var session = Session(RuleSet.Classic, opponent: Opponent.TwoPlayer);
        foreach (var cell in new[] { 0, 3, 1, 4, 2 }) Assert.True(session.PlayHuman(cell));
        Assert.Equal(1, session.Match.ScoreX);
        session.Match.StartNextRound();
        Assert.True(session.PlayHuman(4));
        session.Match.MarkAssisted();

        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.ScoreX);
        Assert.Equal(0, restored.ScoreO);
        Assert.Equal(2, restored.RoundNumber);
        Assert.Equal(Player.O, restored.Game.Starter);
        Assert.True(restored.Assisted);
        Assert.Equal(session.Game.Cells, restored.Game.Cells);
    }

    [Fact]
    public async Task RoundOverAwaitingNextIsNotDoubleCounted()
    {
        var session = Session(RuleSet.Classic, opponent: Opponent.TwoPlayer);
        foreach (var cell in new[] { 0, 3, 1, 4, 2 }) Assert.True(session.PlayHuman(cell));

        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);

        Assert.NotNull(restored);
        Assert.True(restored.Game.IsOver);
        Assert.Equal(1, restored.ScoreX);
        Assert.Equal(1, restored.RoundNumber);
        Assert.Empty(restored.ToSnapshot().Rounds);
    }

    [Fact]
    public async Task DecayMatchRoundTripKeepsFadingQueue()
    {
        var session = Session(RuleSet.Decay, opponent: Opponent.TwoPlayer);
        foreach (var cell in new[] { 0, 4, 1, 5, 6, 7 }) Assert.True(session.PlayHuman(cell));
        var fading = session.Game.FadingCell;
        Assert.NotNull(fading);

        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);

        Assert.NotNull(restored);
        Assert.Equal(RuleSet.Decay, restored.Settings.Rules);
        Assert.Equal(fading, restored.Game.FadingCell);
        Assert.Equal(session.Game.Cells, restored.Game.Cells);
        Assert.Equal(session.Game.LegalMoves, restored.Game.LegalMoves);
    }

    [Fact]
    public async Task UndoStackSurvivesResume()
    {
        var session = Session(RuleSet.Classic, opponent: Opponent.TwoPlayer);
        foreach (var cell in new[] { 4, 0, 8 }) Assert.True(session.PlayHuman(cell));

        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);

        Assert.NotNull(restored);
        Assert.True(restored.Game.Undo());
        Assert.Equal(Player.None, restored.Game.Cells[8]);
        Assert.Equal(Player.X, restored.Game.CurrentPlayer);
    }

    [Fact]
    public async Task FinishedMatchIsNeverSavedAndClearWorks()
    {
        var store = new MemoryStore();
        var svc = new ProfileService(store);
        var session = new MatchSession(new MatchSettings { BestOf = 1, Opponent = Opponent.TwoPlayer }, new Random(1));
        foreach (var cell in new[] { 0, 3, 1, 4 }) session.PlayHuman(cell);
        await svc.SaveMatchAsync(session.Match.ToSnapshot());
        Assert.NotNull((await new ProfileService(store).LoadAsync()).ActiveMatch);

        session.PlayHuman(2);
        Assert.True(session.Match.IsMatchOver);
        await svc.SaveMatchAsync(session.Match.ToSnapshot());
        Assert.Null((await new ProfileService(store).LoadAsync()).ActiveMatch);

        await svc.SaveMatchAsync(null);
        Assert.DoesNotContain("activeMatch", store.Data[ProfileService.StorageKey]);
    }

    [Fact]
    public async Task TamperedMatchSnapshotsAreDroppedWithoutCrashing()
    {
        foreach (var match in new[]
                 {
                     "{\"settings\":{\"bestOf\":3},\"scoreX\":0,\"scoreO\":0,\"draws\":0,\"round\":1,\"starter\":\"X\",\"moves\":[4,4]}",
                     "{\"settings\":{\"bestOf\":3},\"scoreX\":5,\"scoreO\":0,\"draws\":0,\"round\":1,\"starter\":\"X\",\"moves\":[]}",
                     "{\"settings\":{\"bestOf\":3},\"scoreX\":0,\"scoreO\":0,\"draws\":0,\"round\":1,\"starter\":\"None\",\"moves\":[]}",
                     "{\"settings\":{\"bestOf\":3},\"scoreX\":0,\"scoreO\":0,\"draws\":0,\"round\":1,\"starter\":\"X\",\"moves\":[99]}",
                     "{\"settings\":{\"bestOf\":3},\"round\":1,\"starter\":\"X\",\"moves\":\"nope\"}"
                 })
        {
            var store = new MemoryStore();
            store.Data[ProfileService.StorageKey] = "{\"version\":2,\"settings\":{\"handle\":\"KEEP\"},\"activeMatch\":" + match + "}";
            var loaded = await new ProfileService(store).LoadAsync();
            Assert.Null(loaded.ActiveMatch);
            Assert.Equal("KEEP", loaded.Settings.Handle);
        }
    }

    // ---- AI-turn resume ----

    [Fact]
    public async Task ResumeOnAiTurnLetsAiMoveAfterHumanMove()
    {
        var session = Session(RuleSet.Classic);
        Assert.True(session.PlayHuman(4));
        Assert.True(session.IsAiTurn);

        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);
        Assert.NotNull(restored);
        var resumed = new MatchSession(restored, null, new Random(3));

        Assert.True(resumed.IsAiTurn);
        Assert.Equal(Player.O, resumed.AiSide);
        var move = resumed.PlayAiMove();
        Assert.NotNull(move);
        Assert.NotEqual(4, move);
        Assert.False(resumed.IsAiTurn);
    }

    [Fact]
    public async Task ResumeWithAiStartingRoundStillHasAiToMove()
    {
        var session = Session(RuleSet.Decay, human: Player.O);
        Assert.True(session.IsAiTurn);

        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);
        Assert.NotNull(restored);
        var resumed = new MatchSession(restored, null, new Random(3));

        Assert.True(resumed.IsAiTurn);
        Assert.Equal(Player.X, resumed.AiSide);
        Assert.NotNull(resumed.PlayAiMove());
    }

    [Fact]
    public async Task ResumeOnHumanTurnDoesNotMoveAi()
    {
        var session = Session(RuleSet.Classic);
        var restored = await SaveAndReloadAsync(new MemoryStore(), session.Match);
        var resumed = new MatchSession(restored!, null, new Random(3));
        Assert.False(resumed.IsAiTurn);
        Assert.Null(resumed.PlayAiMove());
    }

    // ---- write serialisation ----

    [Fact]
    public async Task RapidSavesAreSerializedAndLastWriteWins()
    {
        var store = new MemoryStore { WriteDelay = TimeSpan.FromMilliseconds(3) };
        var svc = new ProfileService(store);
        await svc.LoadAsync();
        var session = Session(RuleSet.Classic, opponent: Opponent.TwoPlayer);

        var tasks = new List<Task>();
        for (var i = 0; i < 9; i++)
        {
            Assert.True(session.PlayHuman(session.Game.LegalMoves[0]) || session.Game.IsOver);
            tasks.Add(svc.SaveMatchAsync(session.Match.IsMatchOver ? null : session.Match.ToSnapshot()));
            if (session.Game.IsOver) break;
        }
        await Task.WhenAll(tasks);

        Assert.Equal(1, store.MaxConcurrent);
        Assert.Equal(tasks.Count, store.Writes);
        var final = await new ProfileService(store).LoadAsync();
        Assert.NotNull(final);
    }

    [Fact]
    public async Task ConcurrentSettingsAndMatchSavesNeverInterleave()
    {
        var store = new MemoryStore { WriteDelay = TimeSpan.FromMilliseconds(2) };
        var svc = new ProfileService(store);
        await svc.LoadAsync();
        var snapshot = Session(RuleSet.Classic).Match.ToSnapshot();

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 2 == 0
            ? svc.SaveMatchAsync(snapshot)
            : svc.SaveSettingsAsync(new UserSettings { Handle = "H" + i })));

        Assert.Equal(1, store.MaxConcurrent);
        Assert.NotNull((await new ProfileService(store).LoadAsync()).Settings);
    }
}
