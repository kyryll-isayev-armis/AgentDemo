using AgentDemo.Web.Game;
using AgentDemo.Web.Services;
using Microsoft.JSInterop;

namespace AgentDemo.Tests;

public class ServicesTests
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

    private sealed class CountingJs(bool throwOnCall = false) : IJSRuntime
    {
        public int Calls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => Invoke<TValue>();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => Invoke<TValue>();

        private ValueTask<TValue> Invoke<TValue>()
        {
            Calls++;
            if (throwOnCall) throw new InvalidOperationException("no interop during prerender");
            return default;
        }
    }

    private static MatchRecord Record(Player winner, Difficulty difficulty = Difficulty.Rookie, Player human = Player.X, int n = 0) =>
        new(new MatchSettings { Difficulty = difficulty, HumanSide = human, Opponent = Opponent.Ai },
            2, 0, 0, 2, winner, DateTimeOffset.UnixEpoch.AddMinutes(n));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"version\":0,\"settings\":{\"theme\":\"toxic\"}}")]
    [InlineData("{\"settings\":{\"theme\":\"toxic\"}}")]
    public async Task Corrupt_or_old_data_gives_defaults(string? json)
    {
        var store = new FakeStore();
        if (json is not null) store.Data[ProfileService.StorageKey] = json;

        var data = await new ProfileService(store).LoadAsync();

        Assert.Equal(Themes.NeonNoir, data.Settings.Theme);
        Assert.False(data.Settings.SoundEnabled);
        Assert.Empty(data.History);
        Assert.Equal(0, data.Stats.Total.Total);
    }

    [Fact]
    public async Task Invalid_fields_are_clamped_to_defaults()
    {
        var store = new FakeStore();
        store.Data[ProfileService.StorageKey] =
            "{\"version\":1,\"settings\":{\"theme\":\"bogus\",\"sound\":\"yes\",\"handle\":\"<b>Neo</b>!!\"," +
            "\"lastMatch\":{\"bestOf\":7,\"rules\":\"Nope\",\"humanSide\":\"None\"}}," +
            "\"stats\":{\"Rookie\":{\"wins\":-5,\"losses\":2000000000,\"draws\":\"x\"}}}";

        var data = await new ProfileService(store).LoadAsync();

        Assert.Equal(Themes.NeonNoir, data.Settings.Theme);
        Assert.False(data.Settings.SoundEnabled);
        Assert.Equal("bNeob", data.Settings.Handle);
        Assert.Equal(3, data.Settings.LastMatch.BestOf);
        Assert.Equal(RuleSet.Classic, data.Settings.LastMatch.Rules);
        Assert.Equal(Player.X, data.Settings.LastMatch.HumanSide);
        Assert.Equal(0, data.Stats[Difficulty.Rookie].Wins);
        Assert.Equal(LifetimeStats.MaxCount, data.Stats[Difficulty.Rookie].Losses);
    }

    [Theory]
    [InlineData("  Neo_1-x  ", "Neo_1-x")]
    [InlineData("a<script>b", "ascriptb")]
    [InlineData("ABCDEFGHIJKLMNOPQRST", "ABCDEFGHIJKL")]
    [InlineData("héllo", "hllo")]
    [InlineData(null, "")]
    public void Handle_is_sanitized_and_clamped(string? input, string expected)
    {
        Assert.Equal(expected, PlayerHandle.Sanitize(input));
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);
        await svc.SaveSettingsAsync(new UserSettings
        {
            Theme = Themes.Toxic,
            SoundEnabled = true,
            ReducedEffects = true,
            Handle = "Trin",
            LastMatch = new MatchSettings { BestOf = 5, Rules = RuleSet.Decay, Difficulty = Difficulty.Oracle, HumanSide = Player.O }
        });

        var s = (await new ProfileService(store).LoadAsync()).Settings;

        Assert.Equal(Themes.Toxic, s.Theme);
        Assert.True(s.SoundEnabled);
        Assert.True(s.ReducedEffects);
        Assert.Equal("Trin", s.Handle);
        Assert.Equal(5, s.LastMatch.BestOf);
        Assert.Equal(RuleSet.Decay, s.LastMatch.Rules);
        Assert.Equal(Difficulty.Oracle, s.LastMatch.Difficulty);
        Assert.Equal(Player.O, s.LastMatch.HumanSide);
    }

    [Fact]
    public async Task History_is_capped_at_20_newest_first_and_persisted()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);
        for (var i = 0; i < 25; i++) await svc.RecordMatchAsync(Record(Player.X, n: i));

        var data = await new ProfileService(store).LoadAsync();

        Assert.Equal(ProfileService.MaxHistory, data.History.Count);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(24), data.History[0].CompletedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(5), data.History[^1].CompletedAt);
        Assert.Equal(25, data.Stats[Difficulty.Rookie].Wins);
    }

    [Fact]
    public async Task Stats_are_tracked_per_difficulty_from_human_perspective()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);
        await svc.RecordMatchAsync(Record(Player.X, Difficulty.Rookie));
        await svc.RecordMatchAsync(Record(Player.O, Difficulty.Rookie));
        await svc.RecordMatchAsync(Record(Player.O, Difficulty.Oracle, Player.O));
        await svc.RecordMatchAsync(Record(Player.None, Difficulty.Oracle));
        await svc.RecordMatchAsync(new MatchRecord(
            new MatchSettings { Opponent = Opponent.TwoPlayer }, 2, 1, 0, 3, Player.X, DateTimeOffset.UnixEpoch));

        var data = await new ProfileService(store).LoadAsync();

        Assert.Equal(new StatLine(1, 1, 0), data.Stats[Difficulty.Rookie]);
        Assert.Equal(new StatLine(1, 0, 1), data.Stats[Difficulty.Oracle]);
        Assert.Equal(new StatLine(0, 0, 0), data.Stats[Difficulty.Tactician]);
        Assert.Equal(5, data.History.Count);
    }

    [Fact]
    public async Task Sound_is_noop_when_muted()
    {
        var js = new CountingJs(throwOnCall: true);
        var sound = new SoundService(js);

        await sound.PlayAsync(SoundCue.Click);

        Assert.False(sound.Enabled);
        Assert.Equal(0, js.Calls);
    }

    [Fact]
    public async Task Sound_swallows_interop_failures_when_enabled()
    {
        var js = new CountingJs(throwOnCall: true);
        var sound = new SoundService(js) { Enabled = true };

        await sound.PlayAsync(SoundCue.Win);

        Assert.Equal(1, js.Calls);
    }

    [Fact]
    public async Task Local_store_returns_null_when_interop_unavailable()
    {
        var store = new JsLocalStore(new CountingJs(throwOnCall: true));

        Assert.Null(await store.GetAsync("k"));
        await store.SetAsync("k", "v");
    }
}
