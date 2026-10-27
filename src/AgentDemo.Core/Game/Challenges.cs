namespace AgentDemo.Web.Game;

public enum ChallengeGoal
{
    WinMatch = 0,
    Shutout = 1
}

public sealed record DailyChallenge(DateOnly Date, MatchSettings Settings, ChallengeGoal Goal)
{
    public string Description
    {
        get
        {
            var rules = Settings.Rules == RuleSet.Decay ? "DECAY" : "CLASSIC";
            var goal = Goal == ChallengeGoal.Shutout ? "without losing a round" : "to win the match";
            return $"Beat {Settings.Difficulty} ({rules}), best of {Settings.BestOf}, playing {Settings.HumanSide} - {goal}";
        }
    }
}

public static class Challenges
{
    // Same date always yields the same challenge on every machine (SplitMix64, no Random/GetHashCode).
    public static DailyChallenge ForDate(DateOnly date)
    {
        var state = (ulong)date.DayNumber * 0x9E3779B97F4A7C15UL + 0x1234567UL;
        ulong Next()
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        var rules = Next() % 2 == 0 ? RuleSet.Classic : RuleSet.Decay;
        var bestOf = (int)(Next() % 4) switch { 0 => 1, 1 or 2 => 3, _ => 5 };
        var side = Next() % 2 == 0 ? Player.X : Player.O;
        var roll = (int)(Next() % 10);

        // Classic Oracle is unbeatable, so it is only offered under Decay where the AI is depth-limited.
        var difficulty = rules == RuleSet.Classic
            ? (roll < 3 ? Difficulty.Rookie : Difficulty.Tactician)
            : (roll < 5 ? Difficulty.Tactician : Difficulty.Oracle);

        var goal = bestOf >= 3 && Next() % 3 == 0 ? ChallengeGoal.Shutout : ChallengeGoal.WinMatch;

        return new DailyChallenge(date, new MatchSettings
        {
            BestOf = bestOf,
            Rules = rules,
            Opponent = Opponent.Ai,
            Difficulty = difficulty,
            HumanSide = side
        }, goal);
    }

    // The match must use the challenge settings, be unassisted, be won by the human, and finish on the challenge's UTC day.
    public static bool IsCompleted(DailyChallenge challenge, MatchRecord record)
    {
        var s = record.Settings;
        var c = challenge.Settings;
        if (record.Assisted
            || s.Opponent != Opponent.Ai
            || s.BestOf != c.BestOf || s.Rules != c.Rules
            || s.Difficulty != c.Difficulty || s.HumanSide != c.HumanSide
            || record.Winner != s.HumanSide
            || DateOnly.FromDateTime(record.CompletedAt.UtcDateTime) != challenge.Date)
        {
            return false;
        }

        var opponentScore = s.HumanSide == Player.X ? record.ScoreO : record.ScoreX;
        return challenge.Goal != ChallengeGoal.Shutout || opponentScore == 0;
    }
}

public sealed record ChallengeProgress(int Streak = 0, DateOnly? LastCompleted = null, int TotalCompleted = 0)
{
    public const int MaxCount = 100_000;

    public bool IsDoneOn(DateOnly date) => LastCompleted == date;

    // A streak is alive only if the last completion was today or yesterday.
    public int CurrentStreak(DateOnly today) =>
        LastCompleted is { } last && last.DayNumber >= today.DayNumber - 1 && last.DayNumber <= today.DayNumber ? Streak : 0;

    public ChallengeProgress Complete(DateOnly date)
    {
        if (LastCompleted is { } last)
        {
            if (last == date) return this;
            if (date.DayNumber < last.DayNumber) return this;
        }

        var continues = LastCompleted is { } l && l.DayNumber == date.DayNumber - 1;
        return new ChallengeProgress(
            Math.Min(continues ? Streak + 1 : 1, MaxCount),
            date,
            Math.Min(TotalCompleted + 1, MaxCount));
    }
}
