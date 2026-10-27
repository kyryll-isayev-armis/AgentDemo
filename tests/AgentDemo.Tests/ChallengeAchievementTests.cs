using AgentDemo.Web.Game;
using AgentDemo.Web.Services;

namespace AgentDemo.Tests;

public class ChallengeAchievementTests
{
    private sealed class FakeStore : ILocalStore
    {
        public Dictionary<string, string> Data { get; } = [];

        public Task<string?> GetAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string value)
        {
            Data[key] = value;
            return Task.CompletedTask;
        }
    }

    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public void ForDate_IsDeterministic_AndAlwaysWinnable()
    {
        var seen = new HashSet<(RuleSet, Difficulty, int, Player, ChallengeGoal)>();
        for (var i = 0; i < 2000; i++)
        {
            var date = Day.AddDays(i);
            var a = Challenges.ForDate(date);
            var b = Challenges.ForDate(date);
            Assert.Equal(a, b);
            Assert.Equal(date, a.Date);
            Assert.Equal(Opponent.Ai, a.Settings.Opponent);
            Assert.Contains(a.Settings.BestOf, new[] { 1, 3, 5 });
            Assert.False(a.Settings.Rules == RuleSet.Classic && a.Settings.Difficulty == Difficulty.Oracle);
            Assert.False(a.Goal == ChallengeGoal.Shutout && a.Settings.BestOf == 1);
            Assert.False(string.IsNullOrWhiteSpace(a.Description));
            a.Settings.Validate();
            seen.Add((a.Settings.Rules, a.Settings.Difficulty, a.Settings.BestOf, a.Settings.HumanSide, a.Goal));
        }
        Assert.True(seen.Count > 10);
    }

    [Fact]
    public void ForDate_DifferentDays_Vary()
    {
        Assert.Contains(Enumerable.Range(1, 30), i => Challenges.ForDate(Day.AddDays(i)) with { Date = Day } != Challenges.ForDate(Day));
    }

    private static (DailyChallenge, MatchRecord) Winning(DateOnly date)
    {
        var c = Challenges.ForDate(date);
        var human = c.Settings.HumanSide;
        var wins = c.Settings.WinsNeeded;
        var rec = new MatchRecord(c.Settings, human == Player.X ? wins : 0, human == Player.O ? wins : 0, 0, wins, human,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
        return (c, rec);
    }

    [Fact]
    public void IsCompleted_AcceptsMatchingWin()
    {
        var (c, rec) = Winning(Day);
        Assert.True(Challenges.IsCompleted(c, rec));
    }

    [Fact]
    public void IsCompleted_RejectsAssistedLossWrongDayAndWrongSettings()
    {
        var (c, rec) = Winning(Day);
        Assert.False(Challenges.IsCompleted(c, rec with { Assisted = true }));
        Assert.False(Challenges.IsCompleted(c, rec with { Winner = c.Settings.HumanSide == Player.X ? Player.O : Player.X }));
        Assert.False(Challenges.IsCompleted(c, rec with { CompletedAt = rec.CompletedAt.AddDays(1) }));
        Assert.False(Challenges.IsCompleted(c, rec with { Settings = c.Settings with { BestOf = c.Settings.BestOf == 1 ? 3 : 1 } }));
        Assert.False(Challenges.IsCompleted(c, rec with { Settings = c.Settings with { Opponent = Opponent.TwoPlayer } }));
    }

    [Fact]
    public void IsCompleted_Shutout_RequiresNoLostRound()
    {
        var date = Enumerable.Range(0, 400).Select(i => Day.AddDays(i)).First(d => Challenges.ForDate(d).Goal == ChallengeGoal.Shutout);
        var (c, rec) = Winning(date);
        Assert.True(Challenges.IsCompleted(c, rec));
        var lost = c.Settings.HumanSide == Player.X ? rec with { ScoreO = 1 } : rec with { ScoreX = 1 };
        Assert.False(Challenges.IsCompleted(c, lost));
    }

    [Fact]
    public void Streak_ContinuesResetsAndIgnoresRepeats()
    {
        var p = new ChallengeProgress();
        p = p.Complete(Day);
        Assert.Equal(1, p.Streak);
        Assert.Same(p, p.Complete(Day));
        p = p.Complete(Day.AddDays(1));
        p = p.Complete(Day.AddDays(2));
        Assert.Equal(3, p.Streak);
        Assert.Equal(3, p.TotalCompleted);

        Assert.Equal(3, p.CurrentStreak(Day.AddDays(2)));
        Assert.Equal(3, p.CurrentStreak(Day.AddDays(3)));
        Assert.Equal(0, p.CurrentStreak(Day.AddDays(4)));

        p = p.Complete(Day.AddDays(5));
        Assert.Equal(1, p.Streak);
        Assert.Equal(4, p.TotalCompleted);
        Assert.Same(p, p.Complete(Day)); // earlier date never rewinds
    }

    private static MatchRecord AiRecord(Player winner, Difficulty d, RuleSet rules = RuleSet.Classic, int bestOf = 3, int loserScore = 0) =>
        new(new MatchSettings { Opponent = Opponent.Ai, Difficulty = d, Rules = rules, BestOf = bestOf },
            winner == Player.X ? 2 : loserScore, winner == Player.O ? 2 : loserScore, 0, 2 + loserScore, winner, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Earned_MapsConditions()
    {
        var flawless = Achievements.Earned(AiRecord(Player.X, Difficulty.Rookie), 1, 0);
        Assert.Contains(Achievement.FirstWin, flawless);
        Assert.Contains(Achievement.Flawless, flawless);
        Assert.DoesNotContain(Achievement.OracleSlayer, flawless);

        var close = Achievements.Earned(AiRecord(Player.X, Difficulty.Rookie, loserScore: 1), 1, 0);
        Assert.DoesNotContain(Achievement.Flawless, close);

        var oracle = Achievements.Earned(AiRecord(Player.X, Difficulty.Oracle, RuleSet.Decay), 1, 0);
        Assert.Contains(Achievement.OracleSlayer, oracle);
        Assert.Contains(Achievement.DecaySurvivor, oracle);

        var rookieDecay = Achievements.Earned(AiRecord(Player.X, Difficulty.Rookie, RuleSet.Decay), 1, 0);
        Assert.DoesNotContain(Achievement.DecaySurvivor, rookieDecay);

        Assert.Empty(Achievements.Earned(AiRecord(Player.O, Difficulty.Oracle), 1, 0));

        Assert.Contains(Achievement.Regular, Achievements.Earned(AiRecord(Player.O, Difficulty.Rookie), 10, 0));
        Assert.DoesNotContain(Achievement.Regular, Achievements.Earned(AiRecord(Player.O, Difficulty.Rookie), 9, 0));
        Assert.Contains(Achievement.Streak3, Achievements.Earned(AiRecord(Player.O, Difficulty.Rookie), 1, 3));

        var assisted = AiRecord(Player.X, Difficulty.Oracle, RuleSet.Decay) with { Assisted = true };
        Assert.Empty(Achievements.Earned(assisted, 1, 0));
    }

    [Fact]
    public void Titles_AndDescriptions_ExistForEveryAchievement()
    {
        foreach (var a in Enum.GetValues<Achievement>())
        {
            Assert.False(string.IsNullOrWhiteSpace(Achievements.Title(a)));
            Assert.False(string.IsNullOrWhiteSpace(Achievements.Describe(a)));
        }
    }

    [Fact]
    public async Task Service_UnlocksOnce_UpdatesStreak_AndRoundTrips()
    {
        var store = new FakeStore();
        var svc = new ProfileService(store);

        for (var day = 0; day < 3; day++)
        {
            var (_, rec) = Winning(Day.AddDays(day));
            await svc.RecordMatchAsync(rec);
        }

        var data = await new ProfileService(store).LoadAsync();
        Assert.Equal(3, data.Challenge.Streak);
        Assert.Equal(3, data.Challenge.TotalCompleted);
        Assert.Equal(Day.AddDays(2), data.Challenge.LastCompleted);
        Assert.Contains(Achievement.FirstWin, data.Achievements.Keys);
        Assert.Contains(Achievement.Streak3, data.Achievements.Keys);

        // First-unlock timestamp is never overwritten by later matches.
        Assert.Equal(new DateTimeOffset(Day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), data.Achievements[Achievement.FirstWin]);
    }

    [Fact]
    public async Task Service_SameDayChallenge_CountsOnce()
    {
        var svc = new ProfileService(new FakeStore());
        var (_, rec) = Winning(Day);
        await svc.RecordMatchAsync(rec);
        var data = await svc.RecordMatchAsync(rec);

        Assert.Equal(1, data.Challenge.Streak);
        Assert.Equal(1, data.Challenge.TotalCompleted);
    }

    [Fact]
    public async Task Corrupt_AchievementAndChallengeData_FallsBackSafely()
    {
        var store = new FakeStore();
        store.Data[ProfileService.StorageKey] =
            "{\"version\":1,\"achievements\":[{\"id\":\"Nope\",\"at\":\"2026-01-01T00:00:00+00:00\"},{\"id\":\"FirstWin\",\"at\":\"x\"}," +
            "{\"id\":\"Flawless\",\"at\":\"2026-01-01T00:00:00+00:00\"},5]," +
            "\"challenge\":{\"streak\":-4,\"total\":\"many\",\"last\":\"not-a-date\"}}";

        var data = await new ProfileService(store).LoadAsync();

        Assert.Equal([Achievement.Flawless], data.Achievements.Keys);
        Assert.Equal(0, data.Challenge.Streak);
        Assert.Equal(0, data.Challenge.TotalCompleted);
        Assert.Null(data.Challenge.LastCompleted);
    }
}
