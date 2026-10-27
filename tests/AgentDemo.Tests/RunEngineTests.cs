using AgentDemo.Web.Game.Run;

namespace AgentDemo.Tests;

public class RunEngineTests
{
    sealed class FixedRival(int cell) : IRival
    {
        public int ChooseJam(Round round) => cell;
    }

    static Round NewRound(int quota = 1000, int cycles = 9, BossRule boss = BossRule.None, IRival? rival = null, params ModifierId[] mods) =>
        new(new Encounter("T", quota, cycles, boss), mods.Select(ModifierCatalog.Get).ToList(), new LaneLevels(), rival ?? NoRival.Instance);

    static Round WithMods(params ModifierId[] mods) => NewRound(mods: mods);

    static void Place(Round r, params int[] cells)
    {
        foreach (var c in cells) Assert.Null(r.TryPlace(c));
    }

    [Fact]
    public void Rng_IsDeterministic_AndStreamsAreIndependent()
    {
        var a = new DeterministicRng(42);
        var b = new DeterministicRng(42);
        Assert.Equal(Enumerable.Range(0, 20).Select(_ => a.NextUInt64()), Enumerable.Range(0, 20).Select(_ => b.NextUInt64()));

        var root = new DeterministicRng(7);
        var shopFirst = root.Fork("shop").NextUInt64();
        root.NextUInt64();
        Assert.Equal(shopFirst, root.Fork("shop").NextUInt64());
        Assert.NotEqual(shopFirst, root.Fork("rival").NextUInt64());
        Assert.NotEqual(shopFirst, new DeterministicRng(8).Fork("shop").NextUInt64());
    }

    [Fact]
    public void Warden_LocksMiddleRow_AndOnlyOuterRowsCanFire()
    {
        var r = NewRound(boss: BossRule.WardenLockRow);
        for (var i = 3; i <= 5; i++) Assert.Equal(CellKind.Locked, r.Circuit[i].Kind);
        Assert.NotNull(r.TryPlace(4));
        Assert.Equal(6, r.LegalCells().Count);
        Place(r, 0, 1, 2);
        Assert.Equal(30, r.Score);
    }

    [Fact]
    public void Fire_ScoresBaseVolts_ClearsLine_AndLeavesHotScars()
    {
        var r = WithMods();
        Place(r, 0, 1, 2);
        Assert.Equal(30, r.Score);
        Assert.All(new[] { 0, 1, 2 }, i =>
        {
            Assert.Equal(CellKind.Hot, r.Circuit[i].Kind);
            Assert.False(r.Circuit[i].Marked);
            Assert.Equal(Round.BaseHotDuration, r.Circuit[i].Left);
        });
    }

    [Fact]
    public void HotMarks_PayDoubleVolts_AndHalfGainEach()
    {
        var r = WithMods(ModifierId.HeatSink);
        Place(r, 0, 1, 2);
        Place(r, 0, 1, 2);
        // 3 hot marks: 60 volts, gain 1 + 3*0.5 = 2.5 => 150, plus the first 30
        Assert.Equal(30 + 150, r.Score);
    }

    [Fact]
    public void Hot_FadesAfterItsDuration()
    {
        var r = WithMods();
        Place(r, 0, 1, 2, 6, 7);
        Assert.Equal(CellKind.Hot, r.Circuit[0].Kind);
        Place(r, 8);
        Assert.Equal(CellKind.Normal, r.Circuit[0].Kind);
    }

    [Fact]
    public void Cross_DoublesGain_AndCountsEachMarkOnce()
    {
        var r = WithMods();
        Place(r, 1, 2, 3, 6, 0);
        // row0 + col0 => 5 distinct marks = 50 volts, gain 2.0
        Assert.Equal(100, r.Score);
        Assert.Contains(r.Events, e => e.Kind == GameEventKind.Cross);
    }

    [Fact]
    public void LaneTune_AddsVoltsAndGain()
    {
        var lanes = new LaneLevels();
        lanes.Tune(0);
        lanes.Tune(0);
        var r = new Round(new Encounter("T", 1000, 9), [], lanes, NoRival.Instance);
        Place(r, 0, 1, 2);
        // 30 + 20 volts, gain 1.20 => 60
        Assert.Equal(60, r.Score);
        Assert.Equal(7, lanes.TuneCost(0));
    }

    [Fact]
    public void Gain_IsCappedAtX12()
    {
        var ctx = new ScoreContext(1, 1);
        ctx.AddVolts("v", 10);
        ctx.AddGain("big", 5000);
        ctx.Finish();
        Assert.Equal(ScoreContext.GainCap, ctx.GainCenti);
        Assert.True(ctx.GainCapped);
        Assert.Equal(120, ctx.Total);
    }

    [Fact]
    public void Corrupt_MarksJoinLinesButPayNothing()
    {
        var r = WithMods();
        r.Circuit[0].Kind = CellKind.Corrupt;
        Place(r, 0, 1, 2);
        Assert.Equal(20, r.Score);
    }

    [Fact]
    public void Jam_BlocksCell_AndExpires()
    {
        var r = NewRound(rival: new FixedRival(8));
        Place(r, 0);
        Assert.Equal(CellKind.Jam, r.Circuit[8].Kind);
        Assert.NotNull(r.TryPlace(8));
        Place(r, 1);
        Assert.Equal(CellKind.Jam, r.Circuit[8].Kind);
        Place(r, 2);
        Assert.Equal(CellKind.Normal, r.Circuit[8].Kind);
    }

    [Fact]
    public void Round_ClearsAtQuota_AndFailsWhenCyclesRunOut()
    {
        var win = NewRound(quota: 30, cycles: 6);
        Place(win, 0, 1, 2);
        Assert.Equal(RoundOutcome.Cleared, win.Outcome);
        Assert.Equal(3 + 3, win.Reward);
        Assert.NotNull(win.TryPlace(4));

        var lose = NewRound(quota: 30, cycles: 3);
        Place(lose, 0, 1, 4);
        Assert.Equal(RoundOutcome.Failed, lose.Outcome);
    }

    [Fact]
    public void Preview_ReportsFireWithoutMutating()
    {
        var r = WithMods(ModifierId.Capacitor, ModifierId.RustyRelay);
        Place(r, 0, 1);
        var before = (r.Circuit.Snapshot(), r.Score, r.FireStreak, r.GetCounter(ModifierId.RustyRelay));
        var p = r.PreviewPlace(2);
        Assert.Equal(1, p.Lines);
        Assert.Equal(40 * 125 / 100, p.Score);
        Assert.Equal(default, r.PreviewPlace(8));
        Assert.Equal(before, (r.Circuit.Snapshot(), r.Score, r.FireStreak, r.GetCounter(ModifierId.RustyRelay)));
        Place(r, 2);
        Assert.Equal(p.Score, r.Score);
    }

    [Fact]
    public void Preview_HonoursMarkLimit_AndMatchesRealPlacement()
    {
        var r = WithMods(ModifierId.PhaseShifter);
        Place(r, 0, 1, 8);
        var before = (r.Circuit.Snapshot(), r.Score, r.BonusVolts, string.Join(",", r.MarkOrder), r.Events.Count);
        // Placing 2 is the 4th mark: cell 0 fades first, so row 0 does not fire.
        Assert.Equal(default, r.PreviewPlace(2));
        Assert.Equal(before, (r.Circuit.Snapshot(), r.Score, r.BonusVolts, string.Join(",", r.MarkOrder), r.Events.Count));
        Place(r, 2);
        Assert.Equal(0, r.Score);

        var f = WithMods(ModifierId.PhaseShifter);
        Place(f, 0, 7, 3, 4);
        var preview = f.PreviewPlace(5);
        Assert.Equal(1, preview.Lines);
        Place(f, 5);
        Assert.Equal(preview.Score, f.Score);
    }

    [Fact]
    public void QuotaModifiers_AreOrderIndependent()
    {
        Round Make(params ModifierId[] mods) => NewRound(quota: 95, mods: mods);
        var a = Make(ModifierId.RustyRelay, ModifierId.GridRat);
        var b = Make(ModifierId.GridRat, ModifierId.RustyRelay);
        Assert.Equal(95 * 115 / 100, a.Quota);
        Assert.Equal(a.Quota, b.Quota);
    }

    [Fact]
    public void Jam_RefusalSideEffectsRunOnBlock_NotOnQuery()
    {
        var r = NewRound(rival: new FixedRival(8), mods: ModifierId.Tripwire);
        var tripwire = ModifierCatalog.Get(ModifierId.Tripwire);
        Assert.False(tripwire.AllowJam(r, 8));
        Assert.False(tripwire.AllowJam(r, 8));
        Assert.Equal(0, r.GetCounter(ModifierId.Tripwire));
        Place(r, 0);
        Assert.Equal(1, r.GetCounter(ModifierId.Tripwire));
    }

    [Fact]
    public void CrossFactor_TakesTheMaximum()
    {
        var ctx = new ScoreContext(2, 1);
        ctx.RaiseCrossFactor(3);
        ctx.RaiseCrossFactor(2);
        Assert.Equal(3, ctx.CrossFactor);
    }

    [Fact]
    public void Breakdown_IsCultureInvariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var ctx = new ScoreContext(1, 1);
            ctx.AddVolts("v", 10);
            ctx.AddGain("g", 25);
            ctx.Finish();
            Assert.Equal("v +10v, g +25g => 10v x1.25", ctx.Breakdown());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Round_ReachesGainCapX12_WithMaxedLanesHotMarksAndSurgeCross()
    {
        var lanes = new LaneLevels();
        for (var l = 0; l < Circuit.Lines.Length; l++)
            while (lanes.CanTune(l)) lanes.Tune(l);
        var r = new Round(new Encounter("T", 1_000_000, 9), [ModifierCatalog.Get(ModifierId.SurgeCoil), ModifierCatalog.Get(ModifierId.DebtCollector)], lanes, NoRival.Instance);
        foreach (var i in new[] { 1, 3, 5, 7 })
        {
            r.Circuit[i].Kind = CellKind.Hot;
            r.Circuit[i].Left = 9;
        }
        Place(r, 0, 1, 3, 5, 7, 8, 4);
        var scored = r.Events.Single(e => e.Kind == GameEventKind.Scored);
        Assert.Equal(ScoreContext.GainCap, scored.B);
        Assert.Equal(scored.A * 12, scored.C);
        Assert.Equal(scored.C, r.Score);
    }

    [Fact]
    public void AllModifierPairs_PlayWithoutThrowing_Deterministically()
    {
        long Play(ModifierId a, ModifierId b, ulong seed, out List<GameEvent> events)
        {
            var rng = new DeterministicRng(seed);
            var r = new Round(new Encounter("T", 150, 7), [ModifierCatalog.Get(a), ModifierCatalog.Get(b)], new LaneLevels(), new SeededRival(rng.Fork("rival")));
            var pick = rng.Fork("pick");
            while (r.Outcome == RoundOutcome.InProgress)
            {
                var legal = r.LegalCells();
                Assert.Null(r.TryPlace(pick.Pick(legal.ToList())));
            }
            events = r.Events;
            return r.Score;
        }

        var ids = Enum.GetValues<ModifierId>();
        for (var i = 0; i < ids.Length; i++)
        {
            for (var j = i + 1; j < ids.Length; j++)
            {
                for (ulong seed = 1; seed <= 3; seed++)
                {
                    var s1 = Play(ids[i], ids[j], seed, out var e1);
                    var s2 = Play(ids[i], ids[j], seed, out var e2);
                    Assert.True(s1 >= 0);
                    Assert.Equal(s1, s2);
                    Assert.Equal(e1, e2);
                }
            }
        }
    }

    // ---- golden scoring per modifier (base row-0 fire = 30) ----

    [Fact]
    public void SparkPlug_Adds15Volts()
    {
        var r = WithMods(ModifierId.SparkPlug);
        Place(r, 0, 1, 2);
        Assert.Equal(45, r.Score);
    }

    [Fact]
    public void Capacitor_StacksOnConsecutiveFires_AndResets()
    {
        var r = WithMods(ModifierId.Capacitor);
        Place(r, 0, 1, 3, 4, 2);
        Assert.Equal(37, r.Score);
        Place(r, 5);
        Assert.Equal(37 + 45, r.Score);
        Place(r, 6);
        Assert.Equal(0, r.FireStreak);
    }

    [Fact]
    public void RustyRelay_DoublesFirstMark_AndRaisesQuota()
    {
        var r = NewRound(quota: 100, mods: ModifierId.RustyRelay);
        Assert.Equal(105, r.Quota);
        Place(r, 0, 1, 2);
        Assert.Equal(40, r.Score);
    }

    [Fact]
    public void DeadDrop_PaysOnlyForEarlyFire()
    {
        var early = NewRound(quota: 30, cycles: 6, mods: ModifierId.DeadDrop);
        Place(early, 0, 1, 2);
        Assert.Equal(9, early.Reward);

        var late = NewRound(quota: 30, cycles: 6, mods: ModifierId.DeadDrop);
        Place(late, 0, 1, 4, 2);
        Assert.Equal(RoundOutcome.Cleared, late.Outcome);
        Assert.Equal(3 + 2, late.Reward);
    }

    [Fact]
    public void GridRat_AddsCycle_AndRaisesQuota()
    {
        var r = NewRound(quota: 100, cycles: 6, mods: ModifierId.GridRat);
        Assert.Equal(7, r.Cycles);
        Assert.Equal(110, r.Quota);
    }

    [Fact]
    public void Tripwire_RemovesFirstJamOnly_AndCostsCred()
    {
        var r = NewRound(rival: new FixedRival(8), mods: ModifierId.Tripwire);
        Place(r, 0);
        Assert.NotEqual(CellKind.Jam, r.Circuit[8].Kind);
        Assert.Contains(r.Events, e => e.Kind == GameEventKind.JamBlocked);
        Place(r, 1);
        Assert.Equal(CellKind.Jam, r.Circuit[8].Kind);

        var win = NewRound(quota: 30, cycles: 6, mods: ModifierId.Tripwire);
        Place(win, 0, 1, 2);
        Assert.Equal(6 - 1, win.Reward);
    }

    [Fact]
    public void HeatSink_ExtendsHot_AndLetsRivalJamIt()
    {
        var jammed = NewRound(rival: new FixedRival(0), mods: ModifierId.HeatSink);
        Place(jammed, 0, 1, 2);
        Assert.Equal(CellKind.Jam, jammed.Circuit[0].Kind);

        var safe = NewRound(rival: new FixedRival(0));
        Place(safe, 0, 1, 2);
        Assert.Equal(CellKind.Hot, safe.Circuit[0].Kind);

        var fire = WithMods(ModifierId.HeatSink);
        Place(fire, 0, 1, 2);
        Assert.Equal(Round.BaseHotDuration + 1, fire.Circuit[0].Left);
        Assert.True(fire.RivalMayUseHot);
    }

    [Fact]
    public void Splice_LeavesOnlyCentreHot()
    {
        var r = WithMods(ModifierId.Splice);
        Place(r, 0, 1, 2);
        Assert.Equal(CellKind.Hot, r.Circuit[1].Kind);
        Assert.Equal(CellKind.Normal, r.Circuit[0].Kind);
        Assert.Equal(CellKind.Normal, r.Circuit[2].Kind);
    }

    [Fact]
    public void SurgeCoil_TriplesCross_AndPenalisesSingles()
    {
        var single = WithMods(ModifierId.SurgeCoil);
        Place(single, 0, 1, 2);
        Assert.Equal(20, single.Score);

        var cross = WithMods(ModifierId.SurgeCoil);
        Place(cross, 1, 2, 3, 6, 0);
        Assert.Equal(150, cross.Score);
    }

    [Fact]
    public void PhaseShifter_CapsMarks_AndBanksVolts()
    {
        var r = WithMods(ModifierId.PhaseShifter);
        Place(r, 0, 1, 7, 3);
        Assert.False(r.Circuit[0].Marked);
        Assert.Equal(5, r.BonusVolts);
        Place(r, 4, 5);
        Assert.Equal(30 + 15, r.Score);
        Assert.Equal(0, r.BonusVolts);
    }

    [Fact]
    public void DebtCollector_BoostsGain_AndSpendsCred()
    {
        var r = NewRound(quota: 30, cycles: 6, mods: ModifierId.DebtCollector);
        Place(r, 0, 1, 2);
        Assert.Equal(45, r.Score);
        Assert.Equal(-1, r.CredDelta);
        Assert.Equal(3 + 3 + (45 - 30) / 25 - 1, r.Reward);
    }

    [Fact]
    public void Leech_ConvertsAJamToHot_AndExtendsJams()
    {
        var r = NewRound(rival: new FixedRival(8), mods: ModifierId.Leech);
        Place(r, 0);
        Assert.Equal(Round.BaseJamDuration + 1, r.Circuit[8].Left);
        Place(r, 1, 2);
        Assert.Equal(CellKind.Hot, r.Circuit[8].Kind);
        Assert.Contains(r.Events, e => e.Kind == GameEventKind.JamConverted);
    }

    [Fact]
    public void Catalog_HasTwelveCommonAndUncommon_InIdOrder()
    {
        Assert.Equal(12, ModifierCatalog.All.Count);
        Assert.All(ModifierCatalog.All, m => Assert.Contains(m.Rarity, new[] { Rarity.Common, Rarity.Uncommon }));
        for (var i = 0; i < ModifierCatalog.All.Count; i++) Assert.Equal((ModifierId)i, ModifierCatalog.All[i].Id);
        Assert.Equal(6, ModifierCatalog.All.Count(m => m.Rarity == Rarity.Common));
    }
}
