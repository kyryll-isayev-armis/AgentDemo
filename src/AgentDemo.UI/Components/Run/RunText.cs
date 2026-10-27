using AgentDemo.Web.Game.Run;

namespace AgentDemo.Web.Components.Run;

/// <summary>Pure text/icon helpers for the OVERCLOCK UI so labels stay testable and never rely on colour alone.</summary>
public static class RunText
{
    public static string LaneName(int lane) => lane switch
    {
        0 => "Top row",
        1 => "Middle row",
        2 => "Bottom row",
        3 => "Left column",
        4 => "Centre column",
        5 => "Right column",
        6 => "Diagonal down",
        7 => "Diagonal up",
        _ => "Lane",
    };

    public static string CellName(int index) => $"row {index / Circuit.Size + 1}, column {index % Circuit.Size + 1}";

    /// <summary>Icon glyph and short word for a cell state; the word is always rendered next to the icon.</summary>
    public static (string Icon, string Word) Badge(Cell cell) => cell.Kind switch
    {
        CellKind.Hot => ("▲", "HOT"),
        CellKind.Jam => ("✕", "JAM"),
        CellKind.Corrupt => ("◈", "CORRUPT"),
        CellKind.Locked => ("▣", "LOCKED"),
        _ => ("", ""),
    };

    public static string CellLabel(int index, Cell cell, bool preview)
    {
        var state = cell.Kind switch
        {
            CellKind.Hot => cell.Marked ? "your mark on a hot cell" : "hot cell, doubles Volts",
            CellKind.Jam => "jammed by ORBIT",
            CellKind.Corrupt => cell.Marked ? "your mark on a corrupt cell, scores nothing" : "corrupt cell, scores nothing",
            CellKind.Locked => "locked by the boss",
            _ => cell.Marked ? "your mark" : "empty",
        };
        return $"{CellName(index)}, {state}" + (preview ? ", fire preview" : "");
    }

    public static string Describe(GameEvent e) => e.Kind switch
    {
        GameEventKind.RunStarted => "Run started",
        GameEventKind.EncounterStarted => $"Encounter {e.A + 1}: reach {e.B} Volts in {e.C} cycles",
        GameEventKind.Placed => $"You placed at {CellName(e.A)}",
        GameEventKind.MarkFaded => $"Oldest mark at {CellName(e.A)} faded",
        GameEventKind.LineFired => $"{LaneName(e.A)} fired",
        GameEventKind.Cross => $"Cross! {e.A} lines at once",
        GameEventKind.Scored => $"+{e.C} Volts ({e.A} Volts x{e.B / 100.0:0.00} Gain)",
        GameEventKind.HotCreated => $"{CellName(e.A)} is now hot",
        GameEventKind.HotFaded => $"{CellName(e.A)} cooled",
        GameEventKind.JamPlaced => $"ORBIT jammed {CellName(e.A)}",
        GameEventKind.JamBlocked => $"Jam at {CellName(e.A)} was blocked",
        GameEventKind.JamExpired => $"Jam at {CellName(e.A)} cleared",
        GameEventKind.JamConverted => $"Jam at {CellName(e.A)} converted to hot",
        GameEventKind.EncounterCleared => $"Quota met: {e.A}/{e.B}. Reward {e.C} Cred",
        GameEventKind.EncounterFailed => $"Out of cycles: {e.A}/{e.B}",
        GameEventKind.ShopOpened => "Shop open",
        GameEventKind.Purchased => $"Bought {ModifierCatalog.Get((ModifierId)e.A).Name}",
        GameEventKind.Tuned => $"{LaneName(e.A)} tuned to level {e.B}",
        GameEventKind.Sold => $"Sold {ModifierCatalog.Get((ModifierId)e.A).Name}",
        GameEventKind.Rerolled => "Shop rerolled",
        GameEventKind.RunCleared => "Act 1 cleared",
        GameEventKind.RunFailed => "Run failed",
        _ => e.Kind.ToString(),
    };

    public static bool IsVisualBeat(GameEvent e) =>
        e.Kind is not (GameEventKind.RunStarted or GameEventKind.EncounterStarted or GameEventKind.ShopOpened
            or GameEventKind.RunCleared or GameEventKind.RunFailed);

    /// <summary>One concise sentence for the aria-live region after a placement.</summary>
    public static string Summary(IReadOnlyList<GameEvent> events, Round round)
    {
        var placed = events.FirstOrDefault(e => e.Kind == GameEventKind.Placed);
        var text = placed is null ? "Move." : $"Placed {CellName(placed.A)}.";
        var fired = events.Count(e => e.Kind == GameEventKind.LineFired);
        if (fired > 0)
        {
            var volts = events.Where(e => e.Kind == GameEventKind.Scored).Sum(e => e.C);
            text += $" Fired {fired} {(fired == 1 ? "line" : "lines")} for {volts} Volts.";
        }
        var jam = events.FirstOrDefault(e => e.Kind == GameEventKind.JamPlaced);
        if (jam is not null) text += $" ORBIT jammed {CellName(jam.A)}.";
        text += round.Outcome switch
        {
            RoundOutcome.Cleared => $" Quota met, {round.Score} of {round.Quota}.",
            RoundOutcome.Failed => $" Failed, {round.Score} of {round.Quota}.",
            _ => $" Score {round.Score} of {round.Quota}, cycle {round.Cycle} of {round.Cycles}.",
        };
        return text;
    }

    public static string Gain(int centi) => $"x{centi / 100.0:0.00}";
}

public sealed record RunStats(
    int EncountersCleared, int EncountersTotal, int TotalVolts, int FiresCount, int BestFire, string BestFireBreakdown, bool Cleared)
{
    public static RunStats From(RunSession session)
    {
        var scored = session.Log.Where(e => e.Kind == GameEventKind.Scored).ToList();
        var best = scored.Count == 0 ? null : scored.MaxBy(e => e.C);
        return new RunStats(
            session.Log.Count(e => e.Kind == GameEventKind.EncounterCleared),
            session.Encounters.Count,
            scored.Sum(e => e.C),
            scored.Count,
            best?.C ?? 0,
            best?.Text ?? "",
            session.Phase == RunPhase.Cleared);
    }
}
