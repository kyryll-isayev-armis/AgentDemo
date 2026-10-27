using AgentDemo.Web.Components.Run;
using AgentDemo.Web.Game.Run;
using AgentDemo.Web.Services;

namespace AgentDemo.Tests;

public class RunUiTests
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

    private static RunSession PlayUntilShop()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var run = new RunSession(seed);
            for (var i = 0; i < 400 && run.Phase == RunPhase.Encounter; i++)
            {
                var cell = run.Round.LegalCells().First();
                run.Apply(RunAction.Place(cell));
            }
            if (run.Phase == RunPhase.Shop) return run;
        }
        throw new InvalidOperationException("no seed reached the shop");
    }

    [Fact]
    public async Task Active_run_round_trips_through_profile_storage()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);
        var run = PlayUntilShop();
        run.Apply(RunAction.Reroll());

        await svc.SaveRunAsync(run.Save());
        var loaded = await new ProfileService(store).LoadAsync();

        Assert.NotNull(loaded.ActiveRun);
        Assert.Equal(run.Seed, loaded.ActiveRun!.Seed);
        Assert.Equal(run.Actions, loaded.ActiveRun.Actions);
        var resumed = RunSession.Resume(loaded.ActiveRun);
        Assert.NotNull(resumed);
        Assert.Equal(run.Cred, resumed!.Cred);
        Assert.Equal(run.Phase, resumed.Phase);
        Assert.Equal(run.Log.Count, resumed.Log.Count);
    }

    [Fact]
    public async Task Saving_null_clears_the_run()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);
        await svc.SaveRunAsync(new RunSession(7).Save());
        await svc.SaveRunAsync(null);

        Assert.Null((await new ProfileService(store).LoadAsync()).ActiveRun);
    }

    [Fact]
    public async Task Fresh_run_is_kept_but_forged_actions_are_dropped()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);
        await svc.SaveRunAsync(new RunSession(7).Save());
        Assert.NotNull((await new ProfileService(store).LoadAsync()).ActiveRun);

        await svc.SaveRunAsync(new RunSave(RunSave.CurrentVersion, 7, ["bogus", "p99"]));
        Assert.Null((await new ProfileService(store).LoadAsync()).ActiveRun);
    }

    [Fact]
    public async Task Profile_without_run_field_still_loads()
    {
        var store = new FakeStore();
        store.Data[ProfileService.StorageKey] = "{\"version\":1,\"settings\":{\"theme\":\"neon-noir\"}}";

        var data = await new ProfileService(store).LoadAsync();

        Assert.Null(data.ActiveRun);
    }

    [Fact]
    public void Every_special_cell_kind_has_a_distinct_icon_and_word()
    {
        var kinds = new[] { CellKind.Hot, CellKind.Jam, CellKind.Corrupt, CellKind.Locked };
        var badges = kinds.Select(k => RunText.Badge(new Cell { Kind = k })).ToList();

        Assert.All(badges, b => Assert.False(string.IsNullOrEmpty(b.Icon) || string.IsNullOrEmpty(b.Word)));
        Assert.Equal(4, badges.Select(b => b.Word).Distinct().Count());
        Assert.Equal(4, badges.Select(b => b.Icon).Distinct().Count());
        Assert.Equal(("", ""), RunText.Badge(new Cell()));
    }

    [Fact]
    public void Cell_labels_name_position_and_state()
    {
        var label = RunText.CellLabel(4, new Cell { Kind = CellKind.Jam }, preview: true);

        Assert.Contains("row 2, column 2", label);
        Assert.Contains("jammed", label);
        Assert.Contains("fire preview", label);
    }

    [Fact]
    public void Run_stats_match_the_session()
    {
        var run = PlayUntilShop();
        var stats = RunStats.From(run);

        Assert.True(stats.EncountersCleared >= 1);
        Assert.Equal(run.Encounters.Count, stats.EncountersTotal);
        Assert.True(stats.TotalVolts > 0);
        Assert.True(stats.FiresCount >= 1);
        Assert.False(stats.Cleared);
    }

    [Fact]
    public void Gain_formats_hundredths()
    {
        Assert.Equal("x1.00", RunText.Gain(100));
        Assert.Equal("x2.50", RunText.Gain(250));
    }
}
