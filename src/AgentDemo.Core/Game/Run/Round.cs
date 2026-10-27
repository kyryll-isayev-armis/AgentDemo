namespace AgentDemo.Web.Game.Run;

public enum RoundOutcome { InProgress, Cleared, Failed }

public interface IRival
{
    /// <summary>Returns the cell to Jam, or -1 for none. Invalid choices are ignored by the round.</summary>
    int ChooseJam(Round round);
}

public sealed class NoRival : IRival
{
    public static readonly NoRival Instance = new();
    public int ChooseJam(Round round) => -1;
}

/// <summary>ORBIT: blocks open threats most of the time, otherwise jams at random, always from a seeded stream.</summary>
public sealed class SeededRival(DeterministicRng rng, int blockPercent = 40) : IRival
{
    public int ChooseJam(Round round)
    {
        var cells = round.Circuit.Cells;
        var candidates = new List<int>();
        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i].Playable && (cells[i].Kind != CellKind.Hot || round.RivalMayUseHot)) candidates.Add(i);
        }
        if (candidates.Count == 0) return -1;

        var threats = new SortedSet<int>();
        foreach (var line in Circuit.Lines)
        {
            if (line.Count(i => cells[i].Marked) != 2) continue;
            var open = line.First(i => !cells[i].Marked);
            if (candidates.Contains(open)) threats.Add(open);
        }

        if (threats.Count > 0 && rng.Chance(blockPercent)) return rng.Pick(threats.ToList());
        if (candidates.Contains(4) && rng.Chance(25)) return 4;
        return rng.Pick(candidates);
    }
}

/// <summary>One scored encounter: reach <see cref="Quota"/> Volts within <see cref="Cycles"/> cycles.</summary>
public sealed class Round
{
    public const int BaseHotDuration = 3;
    public const int BaseJamDuration = 2;
    public const int BaseMarkValue = 10;

    readonly List<int> _order = [];
    readonly Dictionary<ModifierId, int> _counters = [];
    readonly IRival _rival;

    public Round(Encounter encounter, IReadOnlyList<Modifier> mods, LaneLevels lanes, IRival rival)
    {
        Encounter = encounter;
        Mods = mods;
        Lanes = lanes;
        _rival = rival;
        Quota = encounter.Quota * (100 + mods.Sum(m => m.QuotaPercent)) / 100;
        Cycles = mods.Aggregate(encounter.Cycles, (c, m) => m.AdjustCycles(c));
        if (encounter.BossRule == BossRule.WardenLockRow)
        {
            for (var i = 3; i <= 5; i++) Circuit[i].Kind = CellKind.Locked;
        }
    }

    public Encounter Encounter { get; }
    public Circuit Circuit { get; } = new();
    public IReadOnlyList<Modifier> Mods { get; }
    public LaneLevels Lanes { get; }
    public int Quota { get; }
    public int Cycles { get; }
    public int Cycle { get; private set; } = 1;
    public int Score { get; private set; }
    public int FireStreak { get; private set; }
    public int FirstFireCycle { get; private set; }
    public int BonusVolts { get; set; }
    public int CredDelta { get; set; }
    public int Reward { get; private set; }
    public RoundOutcome Outcome { get; private set; }
    public List<GameEvent> Events { get; } = [];
    public IReadOnlyList<int> MarkOrder => _order;

    public bool RivalMayUseHot => Mods.Any(m => m.RivalMayUseHot);
    public int HotDuration => Mods.Aggregate(BaseHotDuration, (d, m) => m.HotDuration(d));
    public int JamDuration => Mods.Aggregate(BaseJamDuration, (d, m) => m.JamDuration(d));
    public int MaxMarks => Mods.Aggregate(Circuit.CellCount, (x, m) => m.MaxMarks(x));

    public int GetCounter(ModifierId id) => _counters.GetValueOrDefault(id);
    public void SetCounter(ModifierId id, int value) => _counters[id] = value;

    public void Emit(GameEventKind kind, int a = 0, int b = 0, int c = 0, string? text = null) =>
        Events.Add(new GameEvent(kind, a, b, c, text));

    public void MakeHot(int cell)
    {
        var c = Circuit[cell];
        c.Kind = CellKind.Hot;
        c.Left = HotDuration;
        c.Since = Cycle;
        Emit(GameEventKind.HotCreated, cell);
    }

    public IReadOnlyList<int> LegalCells() =>
        Outcome != RoundOutcome.InProgress
            ? []
            : Enumerable.Range(0, Circuit.CellCount).Where(i => Circuit[i].Playable).ToList();

    /// <summary>Returns null on success or an error message; invalid placements change nothing.</summary>
    public string? TryPlace(int cell)
    {
        if (Outcome != RoundOutcome.InProgress) return "Round is over.";
        if (cell < 0 || cell >= Circuit.CellCount) return "Cell out of range.";
        if (!Circuit[cell].Playable) return "Cell is not playable.";

        var target = Circuit[cell];
        target.Marked = true;
        target.ValueMult = 1;
        _order.Add(cell);
        Emit(GameEventKind.Placed, cell, Cycle);
        foreach (var m in Mods) m.OnPlace(this, cell);
        EnforceMarkLimit();

        var fired = FiredLines();
        if (fired.Count > 0) Fire(fired);
        else FireStreak = 0;

        if (Score >= Quota)
        {
            Clear();
            return null;
        }

        PlaceJam();
        Tick();
        if (Cycle >= Cycles || !Circuit.Cells.Any(c => c.Playable))
        {
            Outcome = RoundOutcome.Failed;
            Emit(GameEventKind.EncounterFailed, Score, Quota);
            return null;
        }

        Cycle++;
        return null;
    }

    /// <summary>What placing here would fire right now, including mark-limit fades. Mutates nothing.</summary>
    public Preview PreviewPlace(int cell)
    {
        if (Outcome != RoundOutcome.InProgress || cell < 0 || cell >= Circuit.CellCount || !Circuit[cell].Playable)
            return default;

        var savedCounters = new Dictionary<ModifierId, int>(_counters);
        var savedOrder = _order.ToList();
        var savedMarks = Circuit.Cells.Select(c => (c.Marked, c.ValueMult)).ToArray();
        var eventCount = Events.Count;
        var (bonus, cred, streak, firstFire) = (BonusVolts, CredDelta, FireStreak, FirstFireCycle);
        var target = Circuit[cell];
        target.Marked = true;
        target.ValueMult = 1;
        _order.Add(cell);
        try
        {
            foreach (var m in Mods) m.OnPlace(this, cell);
            EnforceMarkLimit();
            var fired = FiredLines();
            if (fired.Count == 0) return default;
            FireStreak++;
            return new Preview(fired.Count, Scorer.Score(this, fired).Total);
        }
        finally
        {
            for (var i = 0; i < savedMarks.Length; i++)
                (Circuit[i].Marked, Circuit[i].ValueMult) = savedMarks[i];
            _order.Clear();
            _order.AddRange(savedOrder);
            Events.RemoveRange(eventCount, Events.Count - eventCount);
            _counters.Clear();
            foreach (var (k, v) in savedCounters) _counters[k] = v;
            (BonusVolts, CredDelta, FireStreak, FirstFireCycle) = (bonus, cred, streak, firstFire);
        }
    }

    List<int> FiredLines()
    {
        var fired = new List<int>();
        for (var l = 0; l < Circuit.Lines.Length; l++)
        {
            if (Circuit.Lines[l].All(i => Circuit[i].Marked)) fired.Add(l);
        }
        return fired;
    }

    void EnforceMarkLimit()
    {
        var max = Math.Max(1, MaxMarks);
        while (_order.Count > max)
        {
            var oldest = _order[0];
            _order.RemoveAt(0);
            Circuit[oldest].Marked = false;
            Circuit[oldest].ValueMult = 1;
            Emit(GameEventKind.MarkFaded, oldest);
            foreach (var m in Mods) m.OnMarkFaded(this, oldest);
        }
    }

    void Fire(List<int> fired)
    {
        FireStreak++;
        if (FirstFireCycle == 0) FirstFireCycle = Cycle;
        var ctx = Scorer.Score(this, fired);
        Score += ctx.Total;
        foreach (var l in fired) Emit(GameEventKind.LineFired, l);
        if (ctx.IsCross) Emit(GameEventKind.Cross, fired.Count);
        Emit(GameEventKind.Scored, ctx.Volts, ctx.GainCenti, ctx.Total, ctx.Breakdown());

        var cells = fired.SelectMany(l => Circuit.Lines[l]).Distinct().OrderBy(i => i).ToList();
        var centreOnly = Mods.Any(m => m.HotCentreOnly);
        var hotCells = centreOnly ? fired.Select(l => Circuit.Lines[l][1]).ToHashSet() : cells.ToHashSet();
        foreach (var i in cells)
        {
            var c = Circuit[i];
            c.Marked = false;
            c.ValueMult = 1;
            c.Kind = CellKind.Normal;
            c.Left = 0;
            _order.Remove(i);
            if (hotCells.Contains(i)) MakeHot(i);
        }
        foreach (var m in Mods) m.OnFired(this, fired);
    }

    void Clear()
    {
        Outcome = RoundOutcome.Cleared;
        Reward = 3 + (Cycles - Cycle) + (Score - Quota) / 25 + Mods.Sum(m => m.RoundEndCred(this)) + CredDelta;
        Emit(GameEventKind.EncounterCleared, Score, Quota, Reward);
    }

    void PlaceJam()
    {
        var cell = _rival.ChooseJam(this);
        if (cell < 0 || cell >= Circuit.CellCount) return;
        var c = Circuit[cell];
        if (!c.Playable || (c.Kind == CellKind.Hot && !RivalMayUseHot)) return;

        var refusers = Mods.Where(m => !m.AllowJam(this, cell)).ToList();
        if (refusers.Count > 0)
        {
            foreach (var m in refusers) m.OnJamBlocked(this, cell);
            Emit(GameEventKind.JamBlocked, cell);
            return;
        }

        c.Kind = CellKind.Jam;
        c.Left = JamDuration;
        c.Since = Cycle;
        Emit(GameEventKind.JamPlaced, cell);
    }

    void Tick()
    {
        for (var i = 0; i < Circuit.CellCount; i++)
        {
            var c = Circuit[i];
            if (c.Kind is not (CellKind.Hot or CellKind.Jam) || c.Since == Cycle) continue;
            if (--c.Left > 0) continue;
            Emit(c.Kind == CellKind.Hot ? GameEventKind.HotFaded : GameEventKind.JamExpired, i);
            c.Kind = CellKind.Normal;
        }
    }
}

internal static class Scorer
{
    public static ScoreContext Score(Round round, IReadOnlyList<int> lines)
    {
        var ctx = new ScoreContext(lines.Count, round.Cycle);
        var cells = lines.SelectMany(l => Circuit.Lines[l]).Distinct().ToList();

        int marks = 0, hot = 0;
        foreach (var i in cells)
        {
            var c = round.Circuit[i];
            var value = c.Kind == CellKind.Corrupt ? 0 : Round.BaseMarkValue * c.ValueMult;
            if (c.Kind == CellKind.Hot)
            {
                value *= 2;
                hot++;
            }
            marks += value;
        }
        ctx.AddVolts("Marks", marks);

        var laneLevels = lines.Sum(l => round.Lanes[l]);
        if (laneLevels > 0) ctx.AddVolts("Lanes", laneLevels * 10);
        if (round.BonusVolts != 0)
        {
            ctx.AddVolts("Banked", round.BonusVolts);
            round.BonusVolts = 0;
        }
        foreach (var m in round.Mods) m.Volts(round, ctx);

        if (hot > 0) ctx.AddGain("Hot", hot * 50);
        if (laneLevels > 0) ctx.AddGain("Lanes", laneLevels * 10);
        foreach (var m in round.Mods) m.Gain(round, ctx);
        if (ctx.IsCross) ctx.MultiplyGain("Cross", ctx.CrossFactor);

        ctx.Finish();
        return ctx;
    }
}
