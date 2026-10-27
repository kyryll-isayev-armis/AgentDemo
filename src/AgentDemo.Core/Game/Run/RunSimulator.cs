namespace AgentDemo.Web.Game.Run;

public sealed record SimReport(int Runs, int Cleared, IReadOnlyList<int> ReachedEncounter, int Stalled = 0)
{
    public double ClearRate => Runs == 0 ? 0 : (double)Cleared / Runs;
    public override string ToString() =>
        $"{Cleared}/{Runs} cleared ({ClearRate:P1}); runs ending in encounter 1..n: [{string.Join(", ", ReachedEncounter)}]; stalled: {Stalled}";
}

/// <summary>Headless bots used for balance gating. Pure and seedable.</summary>
public static class RunSimulator
{
    public const int MaxActions = 400;

    public static SimReport Simulate(Func<RunSession, DeterministicRng, RunAction> bot, int runs, ulong baseSeed)
    {
        var cleared = 0;
        var stalled = 0;
        var endedAt = new int[Acts.Act1.Count];
        for (var i = 0; i < runs; i++)
        {
            var seed = DeterministicRng.Mix(baseSeed + (ulong)i);
            var session = new RunSession(seed);
            var botRng = new DeterministicRng(seed).Fork("bot");
            for (var n = 0; n < MaxActions && session.Phase is RunPhase.Encounter or RunPhase.Shop; n++)
            {
                if (!session.Apply(bot(session, botRng)).Ok) session.Apply(session.Phase == RunPhase.Shop ? RunAction.Leave() : FirstLegal(session));
            }
            if (session.Phase is RunPhase.Encounter or RunPhase.Shop) stalled++;
            else if (session.Phase == RunPhase.Cleared) cleared++;
            else endedAt[Math.Min(session.EncounterIndex, endedAt.Length - 1)]++;
        }
        return new SimReport(runs, cleared, endedAt, stalled);
    }

    public static RunAction Random(RunSession session, DeterministicRng rng)
    {
        if (session.Phase == RunPhase.Encounter) return RunAction.Place(rng.Pick(session.Round.LegalCells()));
        var affordable = Enumerable.Range(0, session.Offers.Count)
            .Where(i => session.Offers[i] is { } o && o.Cost <= session.Cred && (o.Kind == ShopOfferKind.Tune || session.Mods.Count < RunSession.MaxSlots))
            .ToList();
        return affordable.Count == 0 || rng.Chance(30) ? RunAction.Leave() : RunAction.Buy(rng.Pick(affordable));
    }

    public static RunAction Greedy(RunSession session, DeterministicRng rng)
    {
        if (session.Phase == RunPhase.Encounter) return GreedyPlace(session.Round);

        var ownedTags = session.Mods.Aggregate(ModifierTags.None, (t, m) => t | m.Tags);
        var best = -1;
        var bestValue = int.MinValue;
        for (var i = 0; i < session.Offers.Count; i++)
        {
            if (session.Offers[i] is not { } o || o.Cost > session.Cred) continue;
            int value;
            if (o.Kind == ShopOfferKind.Modifier)
            {
                if (session.Mods.Count >= RunSession.MaxSlots) continue;
                var mod = ModifierCatalog.Get(o.Modifier);
                value = 100 + ((mod.Tags & ownedTags) != 0 ? 20 : 0) - o.Cost;
            }
            else
            {
                value = 50 - o.Cost + (o.Lane is 0 or 2 ? 8 : 0);
            }
            if (value > bestValue)
            {
                best = i;
                bestValue = value;
            }
        }
        return best < 0 ? RunAction.Leave() : RunAction.Buy(best);
    }

    static RunAction FirstLegal(RunSession session) =>
        session.Phase == RunPhase.Encounter ? RunAction.Place(session.Round.LegalCells()[0]) : RunAction.Leave();

    static RunAction GreedyPlace(Round round)
    {
        var best = -1;
        var bestValue = int.MinValue;
        foreach (var cell in round.LegalCells())
        {
            var value = round.Circuit[cell].Kind == CellKind.Hot ? 5 : 0;
            foreach (var line in Circuit.Lines.Where(l => l.Contains(cell)))
            {
                if (line.Any(i => round.Circuit[i].Kind is CellKind.Jam or CellKind.Locked)) continue;
                var others = line.Count(i => i != cell && round.Circuit[i].Marked);
                value += others switch { 2 => 100, 1 => 10, _ => 2 };
            }
            if (value > bestValue)
            {
                best = cell;
                bestValue = value;
            }
        }
        return RunAction.Place(best);
    }
}
