namespace AgentDemo.Web.Game;

public enum Difficulty
{
    Rookie = 0,
    Tactician = 1,
    Oracle = 2
}

public interface IAiStrategy
{
    // Returns a legal cell index. Throws InvalidOperationException if the game is over.
    int Choose(GameState state, Random? rng = null);
}

public static class AiFactory
{
    public static IAiStrategy Create(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Rookie => new RookieStrategy(),
        Difficulty.Tactician => new TacticianStrategy(),
        Difficulty.Oracle => new OracleStrategy(),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
    };
}

internal static class AiHelpers
{
    public static IReadOnlyList<int> RequireMoves(GameState state)
    {
        var moves = state.LegalMoves;
        if (moves.Count == 0)
        {
            throw new InvalidOperationException("No legal moves are available.");
        }
        return moves;
    }

    public static int Pick(IReadOnlyList<int> options, Random? rng) =>
        rng is null ? options[0] : options[rng.Next(options.Count)];
}

public sealed class RookieStrategy : IAiStrategy
{
    public int Choose(GameState state, Random? rng = null)
    {
        var moves = AiHelpers.RequireMoves(state);
        return AiHelpers.Pick(moves, rng ?? Random.Shared);
    }
}

public sealed class TacticianStrategy : IAiStrategy
{
    private static readonly int[] Corners = [0, 2, 6, 8];
    private static readonly int[] Sides = [1, 3, 5, 7];

    public int Choose(GameState state, Random? rng = null)
    {
        var moves = AiHelpers.RequireMoves(state);
        var me = state.CurrentPlayer;
        var opponent = me == Player.X ? Player.O : Player.X;

        var win = moves.FirstOrDefault(m => state.WouldWin(me, m), -1);
        if (win >= 0) return win;

        var block = moves.FirstOrDefault(m => state.WouldWin(opponent, m), -1);
        if (block >= 0) return block;

        if (moves.Contains(4)) return 4;

        var corners = moves.Where(Corners.Contains).ToList();
        if (corners.Count > 0) return AiHelpers.Pick(corners, rng);

        var sides = moves.Where(Sides.Contains).ToList();
        return AiHelpers.Pick(sides.Count > 0 ? sides : moves, rng);
    }
}

public sealed class OracleStrategy : IAiStrategy
{
    // Classic rules have a finite game tree, so the search is exhaustive (perfect play).
    // Decay rules can cycle forever, so they use a bounded search; this is NOT perfect play.
    public const int DecayDepthLimitedSearchPlies = 6;

    private const int WinScore = 100;

    public int Choose(GameState state, Random? rng = null)
    {
        var moves = AiHelpers.RequireMoves(state);
        var me = state.CurrentPlayer;
        var limit = state.Rules == RuleSet.Classic ? GameState.CellCount : DecayDepthLimitedSearchPlies;

        var best = moves[0];
        var bestScore = int.MinValue;
        foreach (var move in moves)
        {
            var next = state.Clone();
            next.TryPlay(move);
            var score = Search(next, me, 1, limit, int.MinValue, int.MaxValue);
            if (score > bestScore)
            {
                bestScore = score;
                best = move;
            }
        }
        return best;
    }

    // Alpha-beta minimax; faster wins and slower losses are preferred.
    private static int Search(GameState s, Player me, int depth, int limit, int alpha, int beta)
    {
        if (s.Winner != Player.None) return s.Winner == me ? WinScore - depth : depth - WinScore;
        if (s.IsDraw || depth >= limit) return 0;

        var maximizing = s.CurrentPlayer == me;
        var value = maximizing ? int.MinValue : int.MaxValue;
        foreach (var move in s.LegalMoves)
        {
            var next = s.Clone();
            next.TryPlay(move);
            var score = Search(next, me, depth + 1, limit, alpha, beta);
            if (maximizing)
            {
                value = Math.Max(value, score);
                alpha = Math.Max(alpha, value);
            }
            else
            {
                value = Math.Min(value, score);
                beta = Math.Min(beta, value);
            }
            if (alpha >= beta) break;
        }
        return value;
    }
}
