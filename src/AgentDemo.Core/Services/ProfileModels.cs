using AgentDemo.Web.Game;

namespace AgentDemo.Web.Services;

public static class Themes
{
    public const string NeonNoir = "neon-noir";
    public const string SolarFlare = "solar-flare";
    public const string Toxic = "toxic";
    public const string HighContrast = "high-contrast";

    public static IReadOnlyList<string> All { get; } = [NeonNoir, SolarFlare, Toxic, HighContrast];

    public static string Normalize(string? theme) =>
        All.FirstOrDefault(t => string.Equals(t, theme, StringComparison.Ordinal)) ?? NeonNoir;
}

public static class PlayerHandle
{
    public const int MaxLength = 12;

    // Keeps ASCII letters, digits, space, '-' and '_'; trims and truncates to MaxLength.
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var chars = input.Where(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or ' ' or '-' or '_');
        var cleaned = new string(chars.ToArray()).Trim();
        return cleaned.Length > MaxLength ? cleaned[..MaxLength].TrimEnd() : cleaned;
    }
}

public sealed record UserSettings
{
    public string Theme { get; init; } = Themes.NeonNoir;

    public bool SoundEnabled { get; init; }

    public bool ReducedEffects { get; init; }

    public MatchSettings LastMatch { get; init; } = new();

    public string Handle { get; init; } = string.Empty;

    public UserSettings Normalized() => this with
    {
        Theme = Themes.Normalize(Theme),
        Handle = PlayerHandle.Sanitize(Handle),
        LastMatch = ProfileValidation.Normalize(LastMatch)
    };
}

public sealed record StatLine(int Wins, int Losses, int Draws)
{
    public int Total => Wins + Losses + Draws;
}

public sealed class LifetimeStats
{
    public const int MaxCount = 1_000_000;

    private readonly Dictionary<Difficulty, StatLine> _lines = [];

    public StatLine this[Difficulty difficulty] =>
        _lines.TryGetValue(difficulty, out var line) ? line : new StatLine(0, 0, 0);

    public StatLine Total => new(
        Enum.GetValues<Difficulty>().Sum(d => this[d].Wins),
        Enum.GetValues<Difficulty>().Sum(d => this[d].Losses),
        Enum.GetValues<Difficulty>().Sum(d => this[d].Draws));

    internal void Set(Difficulty difficulty, StatLine line) =>
        _lines[difficulty] = new StatLine(Clamp(line.Wins), Clamp(line.Losses), Clamp(line.Draws));

    // Only matches versus the AI count; stats are from the human's perspective. Assisted matches are excluded.
    internal void Add(MatchRecord record)
    {
        if (record.Settings.Opponent != Opponent.Ai || record.Assisted) return;
        var line = this[record.Settings.Difficulty];
        _lines[record.Settings.Difficulty] = record.Winner == record.Settings.HumanSide
            ? line with { Wins = Clamp(line.Wins + 1) }
            : record.Winner == Player.None
                ? line with { Draws = Clamp(line.Draws + 1) }
                : line with { Losses = Clamp(line.Losses + 1) };
    }

    private static int Clamp(int v) => Math.Clamp(v, 0, MaxCount);
}

public sealed class ProfileData
{
    public UserSettings Settings { get; set; } = new();

    // Newest first, at most ProfileService.MaxHistory entries.
    public List<MatchRecord> History { get; set; } = [];

    public LifetimeStats Stats { get; set; } = new();

    public Dictionary<Achievement, DateTimeOffset> Achievements { get; set; } = [];

    public ChallengeProgress Challenge { get; set; } = new();

    // In-progress OVERCLOCK run (seed + action log); null when no run is saved.
    public AgentDemo.Web.Game.Run.RunSave? ActiveRun { get; set; }

    // In-progress Classic/Decay match; null when none is saved.
    public MatchSnapshot? ActiveMatch { get; set; }

    // Transient (not persisted): set on load when a saved run was discarded as unreadable or from older content.
    public bool RunRetired { get; set; }
}

internal static class ProfileValidation
{
    public static MatchSettings Normalize(MatchSettings? s)
    {
        s ??= new MatchSettings();
        var d = new MatchSettings();
        return new MatchSettings
        {
            BestOf = s.BestOf is 1 or 3 or 5 ? s.BestOf : d.BestOf,
            Rules = Enum.IsDefined(s.Rules) ? s.Rules : d.Rules,
            Opponent = Enum.IsDefined(s.Opponent) ? s.Opponent : d.Opponent,
            Difficulty = Enum.IsDefined(s.Difficulty) ? s.Difficulty : d.Difficulty,
            HumanSide = s.HumanSide is Player.X or Player.O ? s.HumanSide : d.HumanSide
        };
    }
}
