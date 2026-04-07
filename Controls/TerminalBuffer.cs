using System.Windows.Media;

namespace SshClient.Controls;

public struct TerminalCell
{
    public char Char;
    public Color Foreground;
    public Color Background;
    public bool Bold;
    public bool Underline;

    public static readonly Color DefaultFg = Color.FromRgb(204, 204, 204);
    public static readonly Color DefaultBg = Color.FromRgb(13, 17, 23);   // #0d1117

    public static TerminalCell Empty => new()
    {
        Char = ' ',
        Foreground = DefaultFg,
        Background = DefaultBg
    };
}

/// <summary>
/// Fixed-size grid of cells with a scrollback ring buffer.
/// Thread-safe: mutations happen on calling thread; callers must marshal to UI thread for render.
/// </summary>
public class TerminalBuffer
{
    public const int ScrollbackCapacity = 2000;

    private TerminalCell[,] _screen;
    private readonly List<TerminalCell[]> _scrollback = [];

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public int ScrollOffset { get; private set; }   // lines scrolled back (0 = at bottom)
    public bool CursorVisible { get; private set; } = true;

    // Current SGR attributes
    private Color _fg = TerminalCell.DefaultFg;
    private Color _bg = TerminalCell.DefaultBg;
    private bool _bold;
    private bool _underline;
    private bool _reverseVideo;

    // Saved cursor
    private int _savedX, _savedY;

    public event EventHandler? Changed;

    public TerminalBuffer(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
        _screen = new TerminalCell[rows, cols];
        Fill(_screen, 0, 0, rows, cols);
    }

    public void Resize(int cols, int rows)
    {
        var old = _screen;
        int oldRows = Rows, oldCols = Cols;
        _screen = new TerminalCell[rows, cols];
        Fill(_screen, 0, 0, rows, cols);

        int copyRows = Math.Min(oldRows, rows);
        int copyCols = Math.Min(oldCols, cols);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                _screen[r, c] = old[r, c];

        Cols = cols;
        Rows = rows;
        CursorX = Math.Min(CursorX, Cols - 1);
        CursorY = Math.Min(CursorY, Rows - 1);
        NotifyChanged();
    }

    public TerminalCell GetCell(int row, int col)
    {
        if (ScrollOffset > 0)
        {
            // Map visible row through scrollback ring
            int absRow = _scrollback.Count - ScrollOffset + row;
            if (absRow >= 0 && absRow < _scrollback.Count)
                return col < _scrollback[absRow].Length ? _scrollback[absRow][col] : TerminalCell.Empty;

            // Past end of scrollback — fall through to screen
            int screenRow = row - (_scrollback.Count - absRow);
            if (screenRow >= 0 && screenRow < Rows)
                return _screen[screenRow, col];

            return TerminalCell.Empty;
        }
        return _screen[row, col];
    }

    public TerminalCell GetScreenCell(int row, int col) => _screen[row, col];

    public void ScrollUp(int lines = 3)
    {
        ScrollOffset = Math.Min(ScrollOffset + lines, _scrollback.Count);
        NotifyChanged();
    }

    public void ScrollDown(int lines = 3)
    {
        ScrollOffset = Math.Max(0, ScrollOffset - lines);
        NotifyChanged();
    }

    public void ScrollToBottom()
    {
        ScrollOffset = 0;
        NotifyChanged();
    }

    // ---- Write operations (called by AnsiParser) ----

    public void WriteChar(char c)
    {
        if (CursorX >= Cols)
        {
            CursorX = 0;
            AdvanceLine();
        }

        Color fg = _reverseVideo ? _bg : _fg;
        Color bg = _reverseVideo ? _fg : _bg;

        _screen[CursorY, CursorX] = new TerminalCell
        {
            Char = c,
            Foreground = fg,
            Background = bg,
            Bold = _bold,
            Underline = _underline
        };
        CursorX++;
    }

    public void Backspace()
    {
        if (CursorX > 0) CursorX--;
    }

    public void CarriageReturn() => CursorX = 0;

    public void LineFeed() => AdvanceLine();

    public void Tab()
    {
        int next = ((CursorX / 8) + 1) * 8;
        CursorX = Math.Min(next, Cols - 1);
    }

    public void SetCursor(int row, int col)
    {
        CursorY = Math.Clamp(row, 0, Rows - 1);
        CursorX = Math.Clamp(col, 0, Cols - 1);
    }

    public void MoveCursorRelative(int dRow, int dCol)
        => SetCursor(CursorY + dRow, CursorX + dCol);

    public void SaveCursor() { _savedX = CursorX; _savedY = CursorY; }
    public void RestoreCursor() { CursorX = _savedX; CursorY = _savedY; }

    public void SetCursorVisible(bool visible)
    {
        CursorVisible = visible;
        NotifyChanged();
    }

    public void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // cursor to end
                EraseLine(0);
                for (int r = CursorY + 1; r < Rows; r++)
                    FillRow(r);
                break;
            case 1: // start to cursor
                for (int r = 0; r < CursorY; r++)
                    FillRow(r);
                EraseLine(1);
                break;
            case 2: // entire screen
            case 3:
                Fill(_screen, 0, 0, Rows, Cols);
                break;
        }
    }

    public void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0: // cursor to end of line
                for (int c = CursorX; c < Cols; c++)
                    _screen[CursorY, c] = TerminalCell.Empty;
                break;
            case 1: // start to cursor
                for (int c = 0; c <= CursorX; c++)
                    _screen[CursorY, c] = TerminalCell.Empty;
                break;
            case 2: // whole line
                FillRow(CursorY);
                break;
        }
    }

    public void DeleteChars(int n)
    {
        for (int c = CursorX; c < Cols - n; c++)
            _screen[CursorY, c] = _screen[CursorY, c + n];
        for (int c = Cols - n; c < Cols; c++)
            _screen[CursorY, c] = TerminalCell.Empty;
    }

    public void InsertLines(int n)
    {
        for (int i = 0; i < n; i++)
        {
            for (int r = Rows - 1; r > CursorY; r--)
                for (int c = 0; c < Cols; c++)
                    _screen[r, c] = _screen[r - 1, c];
            FillRow(CursorY);
        }
    }

    public void DeleteLines(int n)
    {
        for (int i = 0; i < n; i++)
        {
            for (int r = CursorY; r < Rows - 1; r++)
                for (int c = 0; c < Cols; c++)
                    _screen[r, c] = _screen[r + 1, c];
            FillRow(Rows - 1);
        }
    }

    public void ApplySgr(int[] parameters)
    {
        if (parameters.Length == 0) { ResetAttrs(); return; }

        for (int i = 0; i < parameters.Length; i++)
        {
            int p = parameters[i];
            switch (p)
            {
                case 0: ResetAttrs(); break;
                case 1: _bold = true; break;
                case 4: _underline = true; break;
                case 7: _reverseVideo = true; break;
                case 22: _bold = false; break;
                case 24: _underline = false; break;
                case 27: _reverseVideo = false; break;

                // Standard foreground colours (30-37)
                case >= 30 and <= 37: _fg = Ansi16[p - 30]; break;
                case 39: _fg = TerminalCell.DefaultFg; break;
                // Standard background colours (40-47)
                case >= 40 and <= 47: _bg = Ansi16[p - 40]; break;
                case 49: _bg = TerminalCell.DefaultBg; break;
                // Bright foreground (90-97)
                case >= 90 and <= 97: _fg = Ansi16[p - 90 + 8]; break;
                // Bright background (100-107)
                case >= 100 and <= 107: _bg = Ansi16[p - 100 + 8]; break;

                // 256-colour / truecolour
                case 38:
                    if (i + 1 < parameters.Length && parameters[i + 1] == 5 && i + 2 < parameters.Length)
                    { _fg = Index256(parameters[i + 2]); i += 2; }
                    else if (i + 1 < parameters.Length && parameters[i + 1] == 2 && i + 4 < parameters.Length)
                    { _fg = Color.FromRgb((byte)parameters[i + 2], (byte)parameters[i + 3], (byte)parameters[i + 4]); i += 4; }
                    break;
                case 48:
                    if (i + 1 < parameters.Length && parameters[i + 1] == 5 && i + 2 < parameters.Length)
                    { _bg = Index256(parameters[i + 2]); i += 2; }
                    else if (i + 1 < parameters.Length && parameters[i + 1] == 2 && i + 4 < parameters.Length)
                    { _bg = Color.FromRgb((byte)parameters[i + 2], (byte)parameters[i + 3], (byte)parameters[i + 4]); i += 4; }
                    break;
            }
        }
    }

    private void ResetAttrs()
    {
        _fg = TerminalCell.DefaultFg;
        _bg = TerminalCell.DefaultBg;
        _bold = false;
        _underline = false;
        _reverseVideo = false;
    }

    private void AdvanceLine()
    {
        CursorY++;
        if (CursorY >= Rows)
        {
            // Scroll: push top line into scrollback
            var line = new TerminalCell[Cols];
            for (int c = 0; c < Cols; c++) line[c] = _screen[0, c];
            _scrollback.Add(line);
            if (_scrollback.Count > ScrollbackCapacity)
                _scrollback.RemoveAt(0);

            for (int r = 0; r < Rows - 1; r++)
                for (int c = 0; c < Cols; c++)
                    _screen[r, c] = _screen[r + 1, c];
            FillRow(Rows - 1);
            CursorY = Rows - 1;
        }
    }

    private void FillRow(int row)
    {
        for (int c = 0; c < Cols; c++) _screen[row, c] = TerminalCell.Empty;
    }

    private static void Fill(TerminalCell[,] grid, int startRow, int startCol, int rows, int cols)
    {
        for (int r = startRow; r < rows; r++)
            for (int c = startCol; c < cols; c++)
                grid[r, c] = TerminalCell.Empty;
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // ---- Colour tables ----

    public static readonly Color[] Ansi16 =
    [
        Color.FromRgb(0,   0,   0),    // 0 black
        Color.FromRgb(170, 0,   0),    // 1 red
        Color.FromRgb(0,   170, 0),    // 2 green
        Color.FromRgb(170, 85,  0),    // 3 yellow/brown
        Color.FromRgb(0,   0,   170),  // 4 blue
        Color.FromRgb(170, 0,   170),  // 5 magenta
        Color.FromRgb(0,   170, 170),  // 6 cyan
        Color.FromRgb(170, 170, 170),  // 7 white
        Color.FromRgb(85,  85,  85),   // 8 bright black
        Color.FromRgb(255, 85,  85),   // 9 bright red
        Color.FromRgb(85,  255, 85),   // 10 bright green
        Color.FromRgb(255, 255, 85),   // 11 bright yellow
        Color.FromRgb(85,  85,  255),  // 12 bright blue
        Color.FromRgb(255, 85,  255),  // 13 bright magenta
        Color.FromRgb(85,  255, 255),  // 14 bright cyan
        Color.FromRgb(255, 255, 255),  // 15 bright white
    ];

    public static Color Index256(int idx)
    {
        if (idx < 16) return Ansi16[idx];
        if (idx < 232)
        {
            idx -= 16;
            int b = idx % 6, g = (idx / 6) % 6, r = idx / 36;
            static byte Scale(int v) => v == 0 ? (byte)0 : (byte)(55 + v * 40);
            return Color.FromRgb(Scale(r), Scale(g), Scale(b));
        }
        // Greyscale 232-255
        byte gray = (byte)(8 + (idx - 232) * 10);
        return Color.FromRgb(gray, gray, gray);
    }

    public int ScrollbackCount => _scrollback.Count;

    public TerminalCell[] GetScrollbackLine(int index) =>
        index < _scrollback.Count ? _scrollback[index] : [];
}
