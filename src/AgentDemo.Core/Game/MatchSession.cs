namespace AgentDemo.Web.Game;

// Pure wrapper around Match that knows which side the AI plays. No timing or UI concerns.
public sealed class MatchSession
{
    private readonly IAiStrategy? _ai;
    private readonly Random _rng;

    public MatchSession(MatchSettings settings, Random? rng = null)
        : this(new Match(settings), settings.Opponent == Opponent.Ai ? AiFactory.Create(settings.Difficulty) : null, rng)
    {
    }

    public MatchSession(Match match, IAiStrategy? ai, Random? rng = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        Match = match;
        _ai = match.Settings.Opponent == Opponent.Ai ? ai ?? AiFactory.Create(match.Settings.Difficulty) : null;
        _rng = rng ?? Random.Shared;
    }

    public Match Match { get; }

    public GameState Game => Match.Game;

    public MatchSettings Settings => Match.Settings;

    public IAiStrategy? Ai => _ai;

    public Player HumanSide => Settings.HumanSide;

    public Player AiSide => _ai is null ? Player.None : HumanSide == Player.X ? Player.O : Player.X;

    public bool IsAiTurn =>
        _ai is not null && !Match.IsMatchOver && !Game.IsOver && Game.CurrentPlayer == AiSide;

    public bool CanHumanPlay(int index) =>
        !Match.IsMatchOver && !Game.IsOver && !IsAiTurn && Game.LegalMoves.Contains(index);

    public bool PlayHuman(int index) => CanHumanPlay(index) && Match.TryPlay(index);

    // Undo needs a live human turn and at least one human move to take back.
    public bool CanUndo => CanAssist && HumanMoveCount > 0;

    // Hint is available whenever a human is on turn in a live round.
    public bool CanHint => CanAssist;

    private bool CanAssist => !Match.IsMatchOver && !Game.IsOver && !IsAiTurn;

    private int HumanMoveCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Game.Moves.Count; i++)
            {
                var mover = i % 2 == 0 ? Game.Starter : Opposite(Game.Starter);
                if (_ai is null || mover == HumanSide) count++;
            }
            return count;
        }
    }

    // Takes back the last human move (and the AI reply to it, if any). Marks the match as assisted.
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        do
        {
            Game.Undo();
        }
        while (_ai is not null && Game.CurrentPlayer != HumanSide && Game.Moves.Count > 0);

        Match.MarkAssisted();
        return true;
    }

    // The Oracle's suggested cell for the side to move; marks the match as assisted.
    public int? Hint()
    {
        if (!CanHint)
        {
            return null;
        }

        Match.MarkAssisted();
        return HintService.Suggest(Game);
    }

    private static Player Opposite(Player p) => p == Player.X ? Player.O : Player.X;

    // Returns the cell played, or null when it is not the AI's turn (so a stale call can never double-move).
    public int? PlayAiMove()
    {
        if (!IsAiTurn)
        {
            return null;
        }

        var move = _ai!.Choose(Game, _rng);
        return Match.TryPlay(move) ? move : null;
    }
}
