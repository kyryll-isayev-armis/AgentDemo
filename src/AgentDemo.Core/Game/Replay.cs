namespace AgentDemo.Web.Game;

// One finished round: who opened and the exact cells played in order.
public sealed record RoundRecord(Player Starter, IReadOnlyList<int> Moves);

public static class Replay
{
    public const int MaxMovesPerRound = 60;

    // The state after the first `step` moves (clamped to 0..Moves.Count).
    public static GameState StateAt(RuleSet rules, RoundRecord round, int step)
    {
        var game = new GameState(rules, round.Starter);
        var count = Math.Clamp(step, 0, round.Moves.Count);
        for (var i = 0; i < count; i++)
        {
            game.TryPlay(round.Moves[i]);
        }
        return game;
    }

    // A stored round is trusted only if every move is legal and the last one finishes the game.
    public static bool IsValid(RuleSet rules, RoundRecord? round)
    {
        if (round is null || round.Starter is not (Player.X or Player.O)
            || round.Moves is null || round.Moves.Count == 0 || round.Moves.Count > MaxMovesPerRound)
        {
            return false;
        }

        var game = new GameState(rules, round.Starter);
        for (var i = 0; i < round.Moves.Count; i++)
        {
            if (game.IsOver || !game.TryPlay(round.Moves[i]))
            {
                return false;
            }
        }
        return game.IsOver;
    }

    public static Player WinnerOf(RuleSet rules, RoundRecord round) => StateAt(rules, round, int.MaxValue).Winner;
}

public static class HintService
{
    // The Oracle's choice for the side to move; null when the game is over. Never mutates `state`.
    public static int? Suggest(GameState state) =>
        state.IsOver ? null : new OracleStrategy().Choose(state.Clone());
}
