using System.Globalization;

namespace AgentDemo.Web.Game.Run;

public enum GameEventKind
{
    RunStarted, EncounterStarted, Placed, MarkFaded, LineFired, Cross, Scored,
    HotCreated, HotFaded, JamPlaced, JamBlocked, JamExpired, JamConverted,
    EncounterCleared, EncounterFailed, ShopOpened, Purchased, Tuned, Sold, Rerolled, RunCleared, RunFailed,
}

/// <summary>Plain value record so event logs compare by value; the UI plays these back.</summary>
public sealed record GameEvent(GameEventKind Kind, int A = 0, int B = 0, int C = 0, string? Text = null);

public sealed record ScoreStep(string Source, int Delta, bool IsGain);

public sealed class ScoreContext
{
    public const int GainCap = 1200;
    readonly List<ScoreStep> _steps = [];

    public ScoreContext(int lines, int cycle)
    {
        Lines = lines;
        Cycle = cycle;
    }

    public int Lines { get; }
    public int Cycle { get; }
    public bool IsCross => Lines >= 2;
    public int Volts { get; private set; }
    /// <summary>Gain in hundredths (100 = x1.00).</summary>
    public int GainCenti { get; private set; } = 100;
    public int CrossFactor { get; private set; } = 2;

    /// <summary>Cross factors never stack; the highest one wins regardless of modifier order.</summary>
    public void RaiseCrossFactor(int factor) => CrossFactor = Math.Max(CrossFactor, factor);
    /// <summary>Reserved for Rare/Legendary modifiers in later milestones.</summary>
    public int FinalMultCenti { get; set; } = 100;
    public bool GainCapped { get; private set; }
    public IReadOnlyList<ScoreStep> Steps => _steps;

    public int Total => (int)((long)Volts * GainCenti * FinalMultCenti / 10000);

    public void AddVolts(string source, int delta)
    {
        Volts += delta;
        _steps.Add(new ScoreStep(source, delta, false));
    }

    public void AddGain(string source, int centi)
    {
        GainCenti += centi;
        _steps.Add(new ScoreStep(source, centi, true));
    }

    public void MultiplyGain(string source, int factor)
    {
        var before = GainCenti;
        GainCenti *= factor;
        _steps.Add(new ScoreStep(source, GainCenti - before, true));
    }

    public void Finish()
    {
        if (Volts < 0) Volts = 0;
        if (GainCenti > GainCap)
        {
            GainCenti = GainCap;
            GainCapped = true;
        }
        if (GainCenti < 100) GainCenti = 100;
    }

    public string Breakdown() =>
        string.Join(", ", _steps.Select(s => $"{s.Source} {s.Delta.ToString("+0;-0", CultureInfo.InvariantCulture)}{(s.IsGain ? "g" : "v")}"))
        + $" => {Volts.ToString(CultureInfo.InvariantCulture)}v x{(GainCenti / 100.0).ToString("0.00", CultureInfo.InvariantCulture)}";
}

public readonly record struct Preview(int Lines, int Score);
