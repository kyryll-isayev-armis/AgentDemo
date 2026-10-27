using AgentDemo.Web.Game.Run;

namespace AgentDemo.Tests;

public class RunSessionTests
{
    static RunSession Play(ulong seed, int maxActions, Func<RunSession, DeterministicRng, RunAction> bot)
    {
        var s = new RunSession(seed);
        var rng = new DeterministicRng(seed).Fork("bot");
        for (var i = 0; i < maxActions && s.Phase is RunPhase.Encounter or RunPhase.Shop; i++)
            s.Apply(bot(s, rng));
        return s;
    }

    [Fact]
    public void SameSeedAndActions_ProduceIdenticalRuns()
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var a = Play(seed, 60, RunSimulator.Greedy);
            var b = Play(seed, 60, RunSimulator.Greedy);
            Assert.Equal(a.Snapshot(), b.Snapshot());
            Assert.Equal(a.Log, b.Log);
            Assert.Equal(a.Actions, b.Actions);
        }
        Assert.NotEqual(Play(1, 60, RunSimulator.Greedy).Snapshot(), Play(2, 60, RunSimulator.Greedy).Snapshot());
    }

    [Fact]
    public void SaveResume_ReproducesStateAtEveryStep()
    {
        for (ulong seed = 100; seed < 130; seed++)
        {
            var live = new RunSession(seed);
            var rng = new DeterministicRng(seed).Fork("bot");
            for (var i = 0; i < 80 && live.Phase is RunPhase.Encounter or RunPhase.Shop; i++)
            {
                live.Apply(RunSimulator.Greedy(live, rng));
                if (i % 7 != 0) continue;
                var resumed = RunSession.Resume(RunSave.FromJson(live.Save().ToJson()));
                Assert.NotNull(resumed);
                Assert.Equal(live.Snapshot(), resumed.Snapshot());
                Assert.Equal(live.Log, resumed.Log);
            }
        }
    }

    [Fact]
    public void Resume_RejectsBadSaves()
    {
        Assert.Null(RunSession.Resume(null));
        Assert.Null(RunSession.Resume(new RunSave(99, 1, [])));
        Assert.Null(RunSession.Resume(new RunSave(RunSave.CurrentVersion, 1, ["P99"])));
        Assert.Null(RunSession.Resume(new RunSave(RunSave.CurrentVersion, 1, ["nonsense"])));
        Assert.Null(RunSave.FromJson("{ not json"));
        Assert.Null(RunSave.FromJson("{\"Version\":1,\"Seed\":1,\"Actions\":null}"));
        Assert.Null(RunSave.FromJson("{\"Version\":1,\"Seed\":-5,\"Actions\":[]}"));
        Assert.Null(RunSave.FromJson("[]"));
        Assert.Null(RunSave.FromJson("null"));
        Assert.Null(RunSession.Resume(new RunSave(RunSave.CurrentVersion, 1, null!)));
        var tooMany = new RunSave(RunSave.CurrentVersion, 1, Enumerable.Repeat("P0", RunSession.MaxSavedActions + 1).ToList());
        Assert.Null(RunSave.FromJson(tooMany.ToJson()));
        Assert.Null(RunSession.Resume(tooMany));
    }

    [Fact]
    public void InvalidActions_AreRejectedAndNotLogged()
    {
        var s = new RunSession(5);
        Assert.False(s.Apply(RunAction.Buy(0)).Ok);
        Assert.False(s.Apply(RunAction.Leave()).Ok);
        Assert.False(s.Apply(RunAction.Place(12)).Ok);
        Assert.True(s.Apply(RunAction.Place(0)).Ok);
        Assert.False(s.Apply(RunAction.Place(0)).Ok);
        Assert.Equal(["P0"], s.Actions);
    }

    [Fact]
    public void ActionEncoding_RoundTrips()
    {
        foreach (var a in new[] { RunAction.Place(8), RunAction.Buy(2), RunAction.Sell(3), RunAction.Reroll(), RunAction.Leave() })
        {
            Assert.True(RunAction.TryParse(a.Encode(), out var back));
            Assert.Equal(a, back);
        }
        Assert.False(RunAction.TryParse("P", out _));
        Assert.False(RunAction.TryParse("Rx", out _));
        foreach (var bad in new[] { "P 3", "P+3", "P03", "P-1", " P3", "P3 ", "R1", "L0" })
            Assert.False(RunAction.TryParse(bad, out _), bad);
    }

    [Fact]
    public void Shop_OpensAfterClear_AndBuySellRerollWork()
    {
        var s = ReachShop();
        Assert.Equal(RunPhase.Shop, s.Phase);
        Assert.Equal(ShopGenerator.OfferCount, s.Offers.Count);

        var credBefore = s.Cred;
        var modIndex = Enumerable.Range(0, 3).First(i => s.Offers[i] is { Kind: ShopOfferKind.Modifier } o && o.Cost <= s.Cred);
        var cost = s.Offers[modIndex]!.Cost;
        Assert.True(s.Apply(RunAction.Buy(modIndex)).Ok);
        Assert.Equal(credBefore - cost, s.Cred);
        Assert.Single(s.Mods);
        Assert.Null(s.Offers[modIndex]);
        Assert.False(s.Apply(RunAction.Buy(modIndex)).Ok);

        Assert.True(s.Apply(RunAction.Sell(0)).Ok);
        Assert.Empty(s.Mods);
        Assert.Equal(credBefore - cost + cost / 2, s.Cred);

        var reroll = s.RerollCost;
        var credNow = s.Cred;
        if (credNow >= reroll)
        {
            Assert.True(s.Apply(RunAction.Reroll()).Ok);
            Assert.Equal(credNow - reroll, s.Cred);
            Assert.Equal(reroll + 1, s.RerollCost);
        }

        Assert.True(s.Apply(RunAction.Leave()).Ok);
        Assert.Equal(RunPhase.Encounter, s.Phase);
        Assert.Equal(1, s.EncounterIndex);
    }

    [Fact]
    public void Shop_RejectsFifthModifier()
    {
        var s = ReachShop();
        var field = typeof(RunSession).GetField("_mods", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var mods = (List<Modifier>)field.GetValue(s)!;
        mods.AddRange(ModifierCatalog.All.Take(RunSession.MaxSlots));
        var index = Enumerable.Range(0, 3).FirstOrDefault(i => s.Offers[i] is { Kind: ShopOfferKind.Modifier }, -1);
        if (index >= 0) Assert.False(s.Apply(RunAction.Buy(index)).Ok);
    }

    [Fact]
    public void ShopPity_OffersAffordableItemWhenPossible_AndSynergyWhenOwned()
    {
        var lanes = new LaneLevels();
        var cheapestOverall = Enumerable.Range(0, Circuit.Lines.Length).Min(lanes.TuneCost);
        for (ulong seed = 0; seed < 300; seed++)
        {
            foreach (var cred in new[] { -2, 0, 2, 3, 4, 7 })
            {
                var offers = ShopGenerator.Generate(new DeterministicRng(seed), [], lanes, cred);
                Assert.Equal(ShopGenerator.OfferCount, offers.Length);
                if (cred >= cheapestOverall) Assert.Contains(offers, o => o is { } x && x.Cost <= cred);
                else Assert.Contains(offers, o => o is { } x && x.Cost == cheapestOverall);
            }

            var owned = new[] { ModifierId.HeatSink };
            var withOwned = ShopGenerator.Generate(new DeterministicRng(seed), owned, lanes, cred: 10);
            Assert.Contains(withOwned, o => o is { Kind: ShopOfferKind.Modifier } x && (ModifierCatalog.Get(x.Modifier).Tags & ModifierTags.Hot) != 0);
            Assert.DoesNotContain(withOwned, o => o is { Kind: ShopOfferKind.Modifier } x && x.Modifier == ModifierId.HeatSink);
            Assert.Equal(withOwned.Length, withOwned.Select(o => (o!.Kind, o.Modifier, o.Lane)).Distinct().Count());

            var poor = ShopGenerator.Generate(new DeterministicRng(seed), owned, lanes, cred: 0);
            Assert.Contains(poor, o => o is { Kind: ShopOfferKind.Modifier } x && (ModifierCatalog.Get(x.Modifier).Tags & ModifierTags.Hot) != 0);
        }
    }

    [Fact]
    public void Property_RandomRuns_NeverThrow_AndKeepInvariants()
    {
        for (ulong seed = 0; seed < 150; seed++)
        {
            var s = new RunSession(seed);
            var rng = new DeterministicRng(seed).Fork("fuzz");
            for (var i = 0; i < RunSimulator.MaxActions && s.Phase is RunPhase.Encounter or RunPhase.Shop; i++)
            {
                var action = rng.Chance(80) ? RunSimulator.Random(s, rng) : new RunAction((RunActionKind)rng.NextInt(5), rng.NextInt(10));
                s.Apply(action);
                Assert.InRange(s.Mods.Count, 0, RunSession.MaxSlots);
                Assert.True(s.Round.Score >= 0);
            }
            Assert.All(s.Log.Where(e => e.Kind == GameEventKind.Scored), e => Assert.InRange(e.B, 100, ScoreContext.GainCap));

            var resumed = RunSession.Resume(RunSave.FromJson(s.Save().ToJson()));
            Assert.NotNull(resumed);
            Assert.Equal(s.Snapshot(), resumed.Snapshot());
        }
    }

    static RunSession ReachShop()
    {
        for (ulong seed = 1; seed < 500; seed++)
        {
            var s = new RunSession(seed);
            var rng = new DeterministicRng(seed).Fork("bot");
            for (var i = 0; i < 40 && s.Phase == RunPhase.Encounter; i++) s.Apply(RunSimulator.Greedy(s, rng));
            if (s.Phase == RunPhase.Shop && s.Cred >= 4) return s;
        }
        throw new InvalidOperationException("No seed reached the shop.");
    }
}
