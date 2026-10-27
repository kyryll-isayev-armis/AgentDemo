namespace AgentDemo.Web.Game;

public static class BoardNavigator
{
    // Arrow keys move one cell and clamp at the edges (no wrap-around). Other keys leave the index unchanged.
    public static int Move(int index, string? key)
    {
        if (index < 0 || index >= GameState.CellCount)
        {
            return Math.Clamp(index, 0, GameState.CellCount - 1);
        }

        var row = index / GameState.Size;
        var col = index % GameState.Size;
        switch (key)
        {
            case "ArrowLeft": col = Math.Max(0, col - 1); break;
            case "ArrowRight": col = Math.Min(GameState.Size - 1, col + 1); break;
            case "ArrowUp": row = Math.Max(0, row - 1); break;
            case "ArrowDown": row = Math.Min(GameState.Size - 1, row + 1); break;
        }
        return row * GameState.Size + col;
    }

    public static bool IsArrow(string? key) =>
        key is "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown";

    // Keys "1".."9" map to cells 0..8.
    public static int? CellForDigit(string? key) =>
        key is { Length: 1 } && key[0] is >= '1' and <= '9' ? key[0] - '1' : null;
}
