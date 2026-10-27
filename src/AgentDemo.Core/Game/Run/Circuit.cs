using System.Text;

namespace AgentDemo.Web.Game.Run;

public enum CellKind { Normal, Hot, Jam, Corrupt, Locked }

public sealed class Cell
{
    public CellKind Kind { get; set; }
    public bool Marked { get; set; }
    /// <summary>Cycles of Hot / Jam remaining.</summary>
    public int Left { get; set; }
    /// <summary>Cycle in which Hot / Jam was applied; it does not tick that cycle.</summary>
    public int Since { get; set; }
    public int ValueMult { get; set; } = 1;

    public bool Playable => !Marked && (Kind is CellKind.Normal or CellKind.Hot or CellKind.Corrupt);
}

public sealed class Circuit
{
    public const int Size = 3;
    public const int CellCount = Size * Size;

    public static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],
        [0, 3, 6], [1, 4, 7], [2, 5, 8],
        [0, 4, 8], [2, 4, 6],
    ];

    readonly Cell[] _cells = Enumerable.Range(0, CellCount).Select(_ => new Cell()).ToArray();

    public Cell this[int index] => _cells[index];
    public IReadOnlyList<Cell> Cells => _cells;

    public string Snapshot()
    {
        var sb = new StringBuilder(CellCount);
        foreach (var c in _cells)
        {
            sb.Append(c.Kind switch
            {
                CellKind.Hot => c.Marked ? 'H' : 'h',
                CellKind.Corrupt => c.Marked ? 'C' : 'c',
                CellKind.Jam => 'J',
                CellKind.Locked => '#',
                _ => c.Marked ? 'X' : '.',
            });
        }
        return sb.ToString();
    }
}

/// <summary>Run-long level of each of the 8 lanes (shop "Tune"): +10 Volts and +0.10 Gain per level on that line.</summary>
public sealed class LaneLevels
{
    public const int MaxLevel = 5;
    readonly int[] _levels = new int[Circuit.Lines.Length];

    public int this[int lane] => _levels[lane];
    public int TuneCost(int lane) => 3 + 2 * _levels[lane];
    public bool CanTune(int lane) => lane >= 0 && lane < _levels.Length && _levels[lane] < MaxLevel;

    public void Tune(int lane)
    {
        if (!CanTune(lane)) throw new InvalidOperationException("Lane cannot be tuned.");
        _levels[lane]++;
    }

    public LaneLevels Clone()
    {
        var copy = new LaneLevels();
        Array.Copy(_levels, copy._levels, _levels.Length);
        return copy;
    }

    public override string ToString() => string.Concat(_levels);
}
