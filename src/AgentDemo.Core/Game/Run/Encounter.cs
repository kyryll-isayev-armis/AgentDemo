namespace AgentDemo.Web.Game.Run;

public enum BossRule { None, WardenLockRow }

public sealed record Encounter(string Name, int Quota, int Cycles, BossRule BossRule = BossRule.None)
{
    public bool IsBoss => BossRule != BossRule.None;
}

public static class Acts
{
    /// <summary>Warden locks the middle row (cells 3-5), leaving only the top and bottom rows fireable.</summary>
    public static readonly IReadOnlyList<Encounter> Act1 =
    [
        new("Skirmish I", 30, 6),
        new("Skirmish II", 80, 7),
        new("The Warden", 110, 8, BossRule.WardenLockRow),
    ];
}
