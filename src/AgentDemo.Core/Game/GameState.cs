namespace AgentDemo.Web.Game;

public enum Player
{
    None = 0,
    X = 1,
    O = 2
}

public enum RuleSet
{
    Classic = 0,
    Decay = 1
}

public sealed class GameState
{
    public const int Size = 3;
    public const int CellCount = Size * Size;
    public const int DecayMaxMarks = 3;

    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],
        [0, 3, 6], [1, 4, 7], [2, 5, 8],
        [0, 4, 8], [2, 4, 6]
    ];

    private readonly Player[] _cells = new Player[CellCount];

    private readonly Queue<int> _xMarks = new();
    private readonly Queue<int> _oMarks = new();
    private readonly Player _starter;
    private readonly List<int> _moves = [];

    public GameState() : this(RuleSet.Classic)
    {
    }

    public GameState(RuleSet rules, Player starter = Player.X)
    {
        if (starter is not (Player.X or Player.O))
        {
            throw new ArgumentOutOfRangeException(nameof(starter));
        }

        Rules = rules;
        _starter = starter;
        CurrentPlayer = starter;
    }

    public RuleSet Rules { get; }

    public Player Starter => _starter;

    // Every accepted move in order; the game is fully determined by Rules, Starter and Moves.
    public IReadOnlyList<int> Moves => _moves;

    // Reverts the last move by replaying the remaining ones, so it is exactly the inverse of TryPlay.
    public bool Undo()
    {
        if (_moves.Count == 0)
        {
            return false;
        }

        var remaining = _moves.Take(_moves.Count - 1).ToArray();
        Restart();
        foreach (var move in remaining)
        {
            TryPlay(move);
        }
        return true;
    }

    public IReadOnlyList<Player> Cells => _cells;

    public Player CurrentPlayer { get; private set; }

    public int? LastMove { get; private set; }

    // Design decision (Decay): a legal move is any EMPTY cell. The cell of the mover's own oldest mark
    // (FadingCell) is still occupied when they move, so it is NOT legal; their oldest mark vanishes
    // only as part of the 4th placement.
    public IReadOnlyList<int> LegalMoves
    {
        get
        {
            if (IsOver)
            {
                return [];
            }

            var moves = new List<int>(CellCount);
            for (var i = 0; i < CellCount; i++)
            {
                if (_cells[i] == Player.None)
                {
                    moves.Add(i);
                }
            }
            return moves;
        }
    }

    // In Decay rules: the cell of the current player's oldest mark, which vanishes on their next move.
    public int? FadingCell
    {
        get
        {
            if (Rules != RuleSet.Decay || IsOver)
            {
                return null;
            }

            var marks = MarksOf(CurrentPlayer);
            return marks.Count >= DecayMaxMarks ? marks.Peek() : null;
        }
    }

    public GameState Clone()
    {
        var copy = new GameState(Rules, _starter)
        {
            CurrentPlayer = CurrentPlayer,
            LastMove = LastMove,
            Winner = Winner,
            WinningLine = WinningLine,
            IsDraw = IsDraw
        };
        Array.Copy(_cells, copy._cells, CellCount);
        copy._moves.AddRange(_moves);
        foreach (var m in _xMarks) copy._xMarks.Enqueue(m);
        foreach (var m in _oMarks) copy._oMarks.Enqueue(m);
        return copy;
    }

    // Whether `player` placing on empty `cell` would complete a line, honouring Decay removal.
    public bool WouldWin(Player player, int cell)
    {
        if (IsOver || player == Player.None || cell < 0 || cell >= CellCount || _cells[cell] != Player.None)
        {
            return false;
        }

        var board = (Player[])_cells.Clone();
        var marks = MarksOf(player);
        if (Rules == RuleSet.Decay && marks.Count >= DecayMaxMarks)
        {
            board[marks.Peek()] = Player.None;
        }
        board[cell] = player;
        return FindLine(board, player) is not null;
    }

    private Queue<int> MarksOf(Player p) => p == Player.X ? _xMarks : _oMarks;

    private static int[]? FindLine(Player[] board, Player p)
    {
        foreach (var line in Lines)
        {
            if (board[line[0]] == p && board[line[1]] == p && board[line[2]] == p)
            {
                return line;
            }
        }
        return null;
    }

    public Player Winner { get; private set; } = Player.None;

    public IReadOnlyList<int> WinningLine { get; private set; } = [];

    public bool IsDraw { get; private set; }

    public bool IsOver => Winner != Player.None || IsDraw;

    public string Status =>
        Winner != Player.None ? $"Player {Winner} wins!" :
        IsDraw ? "Draw — no moves left." :
        $"Player {CurrentPlayer}'s turn";

    public bool TryPlay(int index)
    {
        if (IsOver || index < 0 || index >= CellCount || _cells[index] != Player.None)
        {
            return false;
        }

        var marks = MarksOf(CurrentPlayer);
        if (Rules == RuleSet.Decay && marks.Count >= DecayMaxMarks)
        {
            _cells[marks.Dequeue()] = Player.None;
        }

        _cells[index] = CurrentPlayer;
        marks.Enqueue(index);
        LastMove = index;
        _moves.Add(index);

        var winLine = FindLine(_cells, CurrentPlayer);
        if (winLine is not null)
        {
            Winner = CurrentPlayer;
            WinningLine = winLine;
            return true;
        }

        if (Rules == RuleSet.Classic && _cells.All(c => c != Player.None))
        {
            IsDraw = true;
            return true;
        }

        CurrentPlayer = CurrentPlayer == Player.X ? Player.O : Player.X;
        return true;
    }

    public void Restart()
    {
        Array.Clear(_cells);
        _xMarks.Clear();
        _oMarks.Clear();
        _moves.Clear();
        LastMove = null;
        CurrentPlayer = _starter;
        Winner = Player.None;
        WinningLine = [];
        IsDraw = false;
    }
}
