namespace AgentDemo.Web.Game.Run;

public enum ModifierId
{
    SparkPlug, Capacitor, RustyRelay, DeadDrop, GridRat, Tripwire,
    HeatSink, Splice, SurgeCoil, PhaseShifter, DebtCollector, Leech,
}

public enum Rarity { Common, Uncommon, Rare, Legendary }

[Flags]
public enum ModifierTags { None = 0, Fire = 1, Hot = 2, Cross = 4, Cred = 8, Tempo = 16, Jam = 32 }

/// <summary>
/// Stateless rule modifier. Hooks run in slot order inside a fixed pipeline:
/// quota/cycles setup -> OnPlace -> mark limit -> fire detect -> Volts -> Gain -> Cross multiplier -> cap -> OnFired -> OnJam -> round-end Cred.
/// Per-round state lives on <see cref="Round"/> (counters), never on the modifier.
/// </summary>
public abstract class Modifier
{
    protected Modifier(ModifierId id, string name, Rarity rarity, ModifierTags tags, string upside, string drawback)
    {
        Id = id;
        Name = name;
        Rarity = rarity;
        Tags = tags;
        Upside = upside;
        Drawback = drawback;
    }

    public ModifierId Id { get; }
    public string Name { get; }
    public Rarity Rarity { get; }
    public ModifierTags Tags { get; }
    public string Upside { get; }
    public string Drawback { get; }
    public int Cost => Rarity == Rarity.Common ? 4 : 6;
    public int SellValue => Cost / 2;

    /// <summary>Percent added to the quota; all modifiers' percents are summed, then applied once (order-independent).</summary>
    public virtual int QuotaPercent => 0;
    public virtual int AdjustCycles(int cycles) => cycles;
    public virtual int HotDuration(int duration) => duration;
    public virtual int JamDuration(int duration) => duration;
    public virtual int MaxMarks(int max) => max;
    public virtual bool RivalMayUseHot => false;
    public virtual bool HotCentreOnly => false;
    public virtual void OnPlace(Round round, int cell) { }
    /// <summary>Pure query: return false to refuse a Jam. Side effects belong in <see cref="OnJamBlocked"/>.</summary>
    public virtual bool AllowJam(Round round, int cell) => true;
    /// <summary>Runs for every modifier that refused the Jam.</summary>
    public virtual void OnJamBlocked(Round round, int cell) { }
    public virtual void Volts(Round round, ScoreContext ctx) { }
    public virtual void Gain(Round round, ScoreContext ctx) { }
    public virtual void OnMarkFaded(Round round, int cell) { }
    public virtual void OnFired(Round round, IReadOnlyList<int> lines) { }
    public virtual int RoundEndCred(Round round) => 0;
}

public static class ModifierCatalog
{
    public static readonly IReadOnlyList<Modifier> All =
    [
        new SparkPlug(), new Capacitor(), new RustyRelay(), new DeadDrop(), new GridRat(), new Tripwire(),
        new HeatSink(), new Splice(), new SurgeCoil(), new PhaseShifter(), new DebtCollector(), new Leech(),
    ];

    public static Modifier Get(ModifierId id) => All[(int)id];

    sealed class SparkPlug() : Modifier(ModifierId.SparkPlug, "Spark Plug", Rarity.Common, ModifierTags.Fire,
        "+15 Volts on every fire.", "None.")
    {
        public override void Volts(Round round, ScoreContext ctx) => ctx.AddVolts(Name, 15);
    }

    sealed class Capacitor() : Modifier(ModifierId.Capacitor, "Capacitor", Rarity.Common, ModifierTags.Fire,
        "+0.25 Gain per consecutive fire cycle.", "Streak resets on a cycle without a fire.")
    {
        public override void Gain(Round round, ScoreContext ctx) => ctx.AddGain(Name, 25 * round.FireStreak);
    }

    sealed class RustyRelay() : Modifier(ModifierId.RustyRelay, "Rusty Relay", Rarity.Common, ModifierTags.Tempo,
        "Your first mark each round is worth x2 Volts.", "Quota +5%.")
    {
        public override int QuotaPercent => 5;

        public override void OnPlace(Round round, int cell)
        {
            if (round.GetCounter(Id) != 0) return;
            round.SetCounter(Id, 1);
            round.Circuit[cell].ValueMult = 2;
        }
    }

    sealed class DeadDrop() : Modifier(ModifierId.DeadDrop, "Dead Drop", Rarity.Common, ModifierTags.Cred | ModifierTags.Tempo,
        "+3 Cred if your first fire lands by cycle 3.", "Nothing if you fire later.")
    {
        public override int RoundEndCred(Round round) => round.FirstFireCycle is > 0 and <= 3 ? 3 : 0;
    }

    sealed class GridRat() : Modifier(ModifierId.GridRat, "Grid Rat", Rarity.Common, ModifierTags.Tempo,
        "+1 Cycle per round.", "Quota +10%.")
    {
        public override int AdjustCycles(int cycles) => cycles + 1;
        public override int QuotaPercent => 10;
    }

    sealed class Tripwire() : Modifier(ModifierId.Tripwire, "Tripwire", Rarity.Common, ModifierTags.Jam,
        "The first Jam each round is removed.", "-1 Cred at round end.")
    {
        public override bool AllowJam(Round round, int cell) => round.GetCounter(Id) != 0;

        public override void OnJamBlocked(Round round, int cell) => round.SetCounter(Id, 1);

        public override int RoundEndCred(Round round) => -1;
    }

    sealed class HeatSink() : Modifier(ModifierId.HeatSink, "Heat Sink", Rarity.Uncommon, ModifierTags.Hot,
        "Hot cells last 1 cycle longer.", "ORBIT may jam Hot cells.")
    {
        public override int HotDuration(int duration) => duration + 1;
        public override bool RivalMayUseHot => true;
    }

    sealed class Splice() : Modifier(ModifierId.Splice, "Splice", Rarity.Uncommon, ModifierTags.Hot | ModifierTags.Fire,
        "A fired line leaves its centre cell Hot.", "Its edge cells stay cold.")
    {
        public override bool HotCentreOnly => true;
    }

    sealed class SurgeCoil() : Modifier(ModifierId.SurgeCoil, "Surge Coil", Rarity.Uncommon, ModifierTags.Cross,
        "Cross multiplier is x3 instead of x2.", "Single-line fires lose 10 Volts.")
    {
        public override void Volts(Round round, ScoreContext ctx)
        {
            if (ctx.IsCross) ctx.RaiseCrossFactor(3);
            else ctx.AddVolts(Name, -10);
        }
    }

    sealed class PhaseShifter() : Modifier(ModifierId.PhaseShifter, "Phase Shifter", Rarity.Uncommon, ModifierTags.Tempo | ModifierTags.Fire,
        "Your oldest mark fades for +5 banked Volts.", "You can hold at most 3 marks.")
    {
        public override int MaxMarks(int max) => Math.Min(max, 3);
        public override void OnMarkFaded(Round round, int cell) => round.BonusVolts += 5;
    }

    sealed class DebtCollector() : Modifier(ModifierId.DebtCollector, "Debt Collector", Rarity.Uncommon, ModifierTags.Cred | ModifierTags.Fire,
        "Each fire spends 1 Cred for +0.5 Gain.", "Cred can go negative.")
    {
        public override void Gain(Round round, ScoreContext ctx)
        {
            ctx.AddGain(Name, 50);
            round.CredDelta -= 1;
        }
    }

    sealed class Leech() : Modifier(ModifierId.Leech, "Leech", Rarity.Uncommon, ModifierTags.Jam | ModifierTags.Hot,
        "Each fire converts one Jam into a Hot cell.", "Jams last 1 cycle longer.")
    {
        public override int JamDuration(int duration) => duration + 1;

        public override void OnFired(Round round, IReadOnlyList<int> lines)
        {
            for (var i = 0; i < Circuit.CellCount; i++)
            {
                if (round.Circuit[i].Kind != CellKind.Jam) continue;
                round.MakeHot(i);
                round.Emit(GameEventKind.JamConverted, i);
                return;
            }
        }
    }
}
