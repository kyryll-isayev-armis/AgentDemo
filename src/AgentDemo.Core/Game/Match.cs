namespace AgentDemo.Web.Game;

public enum Opponent
{
    TwoPlayer = 0,
    Ai = 1
}

public sealed record MatchSettings
{
    public int BestOf { get; init; } = 3;

    public RuleSet Rules { get; init; } = RuleSet.Classic;

    public Opponent Opponent { get; init; } = Opponent.Ai;

    public Difficulty Difficulty { get; init; } = Difficulty.Tactician;

    public Player HumanSide { get; init; } = Player.X;

    public int WinsNeeded => BestOf / 2 + 1;

    public void Validate()
    {
        if (BestOf is not (1 or 3 or 5))
        {
            throw new ArgumentOutOfRangeException(nameof(BestOf), BestOf, "BestOf must be 1, 3 or 5.");
        }

        if (!Enum.IsDefined(Rules))
        {
            throw new ArgumentOutOfRangeException(nameof(Rules), Rules, "Rules is not a defined rule set.");
        }

        if (!Enum.IsDefined(Opponent))
        {
            throw new ArgumentOutOfRangeException(nameof(Opponent), Opponent, "Opponent is not a defined opponent.");
        }

        if (!Enum.IsDefined(Difficulty))
        {
            throw new ArgumentOutOfRangeException(nameof(Difficulty), Difficulty, "Difficulty is not a defined difficulty.");
        }

        if (HumanSide is not (Player.X or Player.O))
        {
            throw new ArgumentOutOfRangeException(nameof(HumanSide), HumanSide, "HumanSide must be X or O.");
        }
    }
}

public sealed record MatchRecord(
    MatchSettings Settings,
    int ScoreX,
    int ScoreO,
    int Draws,
    int RoundsPlayed,
    Player Winner,
    DateTimeOffset CompletedAt)
{
    // True when Undo or Hint was used; assisted matches never count towards stats or achievements.
    public bool Assisted { get; init; }

    // Replayable rounds (rounds longer than Replay.MaxMovesPerRound are omitted).
    public IReadOnlyList<RoundRecord> Rounds { get; init; } = [];
}

// A match ends only when a player clinches (BestOf/2 + 1 wins); draws award no points and add a round.
public sealed class Match
{
    private readonly List<RoundRecord> _rounds = [];
    private Player _roundStarter = Player.X;
    private bool _roundRecorded;

    public Match(MatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Settings = settings;
        Game = new GameState(settings.Rules, _roundStarter);
    }

    public MatchSettings Settings { get; }

    public bool Assisted { get; private set; }

    public void MarkAssisted() => Assisted = true;

    public GameState Game { get; private set; }

    public int ScoreX { get; private set; }

    public int ScoreO { get; private set; }

    public int Draws { get; private set; }

    public int RoundNumber { get; private set; } = 1;

    public Player Winner { get; private set; } = Player.None;

    public bool IsMatchOver => Winner != Player.None;

    public bool IsRoundOver => Game.IsOver;

    public bool TryPlay(int index)
    {
        if (IsMatchOver || !Game.TryPlay(index))
        {
            return false;
        }

        if (Game.IsOver && !_roundRecorded)
        {
            _roundRecorded = true;
            RecordRound();
        }
        return true;
    }

    public void StartNextRound()
    {
        if (IsMatchOver) throw new InvalidOperationException("The match is over.");
        if (!Game.IsOver) throw new InvalidOperationException("The current round is not finished.");

        // Loser starts; on a draw the starter alternates.
        _roundStarter = Game.Winner switch
        {
            Player.X => Player.O,
            Player.O => Player.X,
            _ => _roundStarter == Player.X ? Player.O : Player.X
        };

        RoundNumber++;
        _roundRecorded = false;
        Game = new GameState(Settings.Rules, _roundStarter);
    }

    public void Rematch()
    {
        ScoreX = ScoreO = Draws = 0;
        RoundNumber = 1;
        Winner = Player.None;
        _roundStarter = Player.X;
        _roundRecorded = false;
        _rounds.Clear();
        Assisted = false;
        Game = new GameState(Settings.Rules, _roundStarter);
    }

    public MatchRecord ToRecord(DateTimeOffset completedAt)
    {
        if (!IsMatchOver) throw new InvalidOperationException("The match is not over.");
        return new MatchRecord(Settings, ScoreX, ScoreO, Draws, ScoreX + ScoreO + Draws, Winner, completedAt)
        {
            Assisted = Assisted,
            Rounds = _rounds.ToArray()
        };
    }

    private void RecordRound()
    {
        if (Game.Moves.Count <= Replay.MaxMovesPerRound)
        {
            _rounds.Add(new RoundRecord(Game.Starter, Game.Moves.ToArray()));
        }

        switch (Game.Winner)
        {
            case Player.X: ScoreX++; break;
            case Player.O: ScoreO++; break;
            default: Draws++; break;
        }

        if (ScoreX >= Settings.WinsNeeded) Winner = Player.X;
        else if (ScoreO >= Settings.WinsNeeded) Winner = Player.O;
    }

    // Captures the match as of the start of the current round plus its moves, so replaying the moves
    // through TryPlay on restore reproduces the exact state (board, turn, decay queue, undo history).
    public MatchSnapshot ToSnapshot()
    {
        var x = ScoreX;
        var o = ScoreO;
        var d = Draws;
        var rounds = _rounds.ToList();
        if (_roundRecorded)
        {
            switch (Game.Winner)
            {
                case Player.X: x--; break;
                case Player.O: o--; break;
                default: d--; break;
            }
            if (Game.Moves.Count <= Replay.MaxMovesPerRound && rounds.Count > 0) rounds.RemoveAt(rounds.Count - 1);
        }
        return new MatchSnapshot(Settings, x, o, d, RoundNumber, Game.Starter, Assisted, rounds, Game.Moves.ToArray());
    }

    // Returns null for any snapshot that is inconsistent or cannot be replayed legally.
    public static Match? Restore(MatchSnapshot? s)
    {
        if (s?.Settings is null || s.Rounds is null || s.Moves is null) return null;
        try
        {
            s.Settings.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var need = s.Settings.WinsNeeded;
        if (s.Starter is not (Player.X or Player.O)
            || s.ScoreX < 0 || s.ScoreO < 0 || s.Draws < 0
            || s.ScoreX >= need || s.ScoreO >= need
            || s.RoundNumber < 1 || s.RoundNumber > MaxRounds
            || s.ScoreX + s.ScoreO + s.Draws != s.RoundNumber - 1
            || s.Rounds.Count > s.RoundNumber - 1
            || s.Moves.Count > Replay.MaxMovesPerRound
            || !s.Rounds.All(r => Replay.IsValid(s.Settings.Rules, r)))
        {
            return null;
        }

        var match = new Match(s.Settings)
        {
            ScoreX = s.ScoreX,
            ScoreO = s.ScoreO,
            Draws = s.Draws,
            RoundNumber = s.RoundNumber,
            Assisted = s.Assisted,
            _roundStarter = s.Starter
        };
        match._rounds.AddRange(s.Rounds.Select(r => new RoundRecord(r.Starter, r.Moves.ToArray())));
        match.Game = new GameState(s.Settings.Rules, s.Starter);
        foreach (var move in s.Moves)
        {
            if (!match.TryPlay(move)) return null;
        }
        return match;
    }

    private const int MaxRounds = 99;
}

// Persistable state of an in-progress match (scores exclude the current round, which is replayed from Moves).
public sealed record MatchSnapshot(
    MatchSettings Settings,
    int ScoreX,
    int ScoreO,
    int Draws,
    int RoundNumber,
    Player Starter,
    bool Assisted,
    IReadOnlyList<RoundRecord> Rounds,
    IReadOnlyList<int> Moves);
