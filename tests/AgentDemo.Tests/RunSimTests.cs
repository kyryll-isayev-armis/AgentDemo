using AgentDemo.Web.Game.Run;
using Xunit.Abstractions;

namespace AgentDemo.Tests;

public class RunSimTests(ITestOutputHelper output)
{
    const int Runs = 2000;

    [Fact]
    public void Sim_GreedyAndRandomBots_ReportAct1ClearRate()
    {
        var greedy = RunSimulator.Simulate(RunSimulator.Greedy, Runs, 12345);
        var random = RunSimulator.Simulate(RunSimulator.Random, Runs, 12345);
        output.WriteLine($"Greedy bot, Act 1: {greedy}");
        output.WriteLine($"Random bot, Act 1: {random}");

        Assert.InRange(greedy.ClearRate, 0.35, 0.65);
        Assert.True(random.ClearRate < greedy.ClearRate, "greedy must outperform random");
        Assert.True(random.ClearRate < 0.25, "random play should rarely clear Act 1");
    }

    [Fact]
    public void Sim_IsDeterministic()
    {
        var a = RunSimulator.Simulate(RunSimulator.Greedy, 200, 99);
        var b = RunSimulator.Simulate(RunSimulator.Greedy, 200, 99);
        Assert.Equal(a.Cleared, b.Cleared);
        Assert.Equal(a.ReachedEncounter, b.ReachedEncounter);
    }

    [Fact]
    public void Sim_NoRunStalls()
    {
        foreach (var bot in new Func<RunSession, DeterministicRng, RunAction>[] { RunSimulator.Greedy, RunSimulator.Random })
        {
            Assert.Equal(0, RunSimulator.Simulate(bot, 300, 7).Stalled);
            for (ulong seed = 0; seed < 300; seed++)
            {
                var s = new RunSession(seed);
                var rng = new DeterministicRng(seed).Fork("bot");
                var n = 0;
                while (s.Phase is RunPhase.Encounter or RunPhase.Shop && n++ < RunSimulator.MaxActions)
                    Assert.True(s.Apply(bot(s, rng)).Ok);
                Assert.True(s.Phase is RunPhase.Cleared or RunPhase.Failed, $"seed {seed} stalled in {s.Phase}");
            }
        }
    }
}
