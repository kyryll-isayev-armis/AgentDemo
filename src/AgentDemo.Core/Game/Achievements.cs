namespace AgentDemo.Web.Game;

public enum Achievement
{
    FirstWin = 0,
    Flawless = 1,
    DecaySurvivor = 2,
    OracleSlayer = 3,
    Regular = 4,
    Streak3 = 5
}

public static class Achievements
{
    public const int RegularMatches = 10;
    public const int StreakDays = 3;

    public static string Title(Achievement a) => a switch
    {
        Achievement.FirstWin => "First blood",
        Achievement.Flawless => "Flawless",
        Achievement.DecaySurvivor => "Decay survivor",
        Achievement.OracleSlayer => "Oracle slayer",
        Achievement.Regular => "Regular",
        Achievement.Streak3 => "Hot streak",
        _ => a.ToString()
    };

    public static string Describe(Achievement a) => a switch
    {
        Achievement.FirstWin => "Win a match against the AI without Undo or Hint.",
        Achievement.Flawless => "Win a best-of-3 or best-of-5 against the AI without losing a round.",
        Achievement.DecaySurvivor => "Beat Tactician or Oracle under DECAY rules.",
        Achievement.OracleSlayer => "Beat the Oracle.",
        Achievement.Regular => $"Finish {RegularMatches} unassisted matches against the AI.",
        Achievement.Streak3 => $"Complete the daily challenge {StreakDays} days in a row.",
        _ => ""
    };

    // Achievements the finished match qualifies for; the caller merges them so each unlocks once.
    public static IReadOnlyList<Achievement> Earned(MatchRecord record, int unassistedAiMatches, int currentStreak)
    {
        var earned = new List<Achievement>();
        var s = record.Settings;
        var vsAi = s.Opponent == Opponent.Ai;
        var humanWon = vsAi && !record.Assisted && record.Winner == s.HumanSide;

        if (humanWon)
        {
            earned.Add(Achievement.FirstWin);
            var opponentScore = s.HumanSide == Player.X ? record.ScoreO : record.ScoreX;
            if (s.BestOf >= 3 && opponentScore == 0) earned.Add(Achievement.Flawless);
            if (s.Rules == RuleSet.Decay && s.Difficulty is Difficulty.Tactician or Difficulty.Oracle) earned.Add(Achievement.DecaySurvivor);
            if (s.Difficulty == Difficulty.Oracle) earned.Add(Achievement.OracleSlayer);
        }

        if (unassistedAiMatches >= RegularMatches) earned.Add(Achievement.Regular);
        if (currentStreak >= StreakDays) earned.Add(Achievement.Streak3);
        return earned;
    }
}
