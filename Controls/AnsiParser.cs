namespace SshClient.Controls;

/// <summary>
/// Stateful VT100/VT220/xterm-256color ANSI escape sequence parser.
/// Feed raw text from the SSH stream; it updates the TerminalBuffer accordingly.
/// </summary>
public class AnsiParser
{
    private enum State { Normal, Escape, Csi, OscStart, Osc, Dcs }

    private State _state = State.Normal;
    private readonly System.Text.StringBuilder _seq = new();
    private readonly TerminalBuffer _buf;

    public AnsiParser(TerminalBuffer buffer) => _buf = buffer;

    public void Feed(string text)
    {
        foreach (char ch in text)
            ProcessChar(ch);
        _buf.NotifyChanged();
    }

    private void ProcessChar(char ch)
    {
        switch (_state)
        {
            case State.Normal:
                HandleNormal(ch);
                break;
            case State.Escape:
                HandleEscape(ch);
                break;
            case State.Csi:
                HandleCsi(ch);
                break;
            case State.OscStart:
            case State.Osc:
                // OSC: read until ST (ESC \) or BEL — we swallow them
                if (ch == '\x07' || ch == '\x1B') { _state = State.Normal; _seq.Clear(); }
                else _state = State.Osc;
                break;
            case State.Dcs:
                if (ch == '\x1B') _state = State.Escape;
                break;
        }
    }

    private void HandleNormal(char ch)
    {
        switch (ch)
        {
            case '\x1B': _state = State.Escape; _seq.Clear(); break;
            case '\r': _buf.CarriageReturn(); break;
            case '\n': _buf.LineFeed(); break;
            case '\b': _buf.Backspace(); break;
            case '\t': _buf.Tab(); break;
            case '\a': break; // bell — ignore
            case '\x0F': break; // shift-in
            case '\x0E': break; // shift-out
            default:
                if (ch >= ' ') _buf.WriteChar(ch);
                break;
        }
    }

    private void HandleEscape(char ch)
    {
        switch (ch)
        {
            case '[':
                _state = State.Csi;
                _seq.Clear();
                break;
            case ']':
                _state = State.OscStart;
                _seq.Clear();
                break;
            case 'P':
                _state = State.Dcs;
                break;
            case 'D': // IND — line feed
                _buf.LineFeed();
                _state = State.Normal;
                break;
            case 'E': // NEL — new line
                _buf.CarriageReturn();
                _buf.LineFeed();
                _state = State.Normal;
                break;
            case 'M': // RI — reverse line feed
                _buf.MoveCursorRelative(-1, 0);
                _state = State.Normal;
                break;
            case '7': // DECSC
                _buf.SaveCursor();
                _state = State.Normal;
                break;
            case '8': // DECRC
                _buf.RestoreCursor();
                _state = State.Normal;
                break;
            case 'c': // full reset
                FullReset();
                _state = State.Normal;
                break;
            case '\\': // ST — string terminator
                _state = State.Normal;
                break;
            default:
                // Unknown escape — back to normal
                _state = State.Normal;
                break;
        }
    }

    private void HandleCsi(char ch)
    {
        if (ch >= 0x20 && ch < 0x40)
        {
            // Intermediate or parameter byte
            _seq.Append(ch);
            return;
        }

        if (ch >= 0x40 && ch <= 0x7E)
        {
            // Final byte — execute the sequence
            ExecuteCsi(_seq.ToString(), ch);
            _seq.Clear();
            _state = State.Normal;
            return;
        }

        // Unexpected — reset
        _seq.Clear();
        _state = State.Normal;
    }

    private void ExecuteCsi(string param, char cmd)
    {
        // Strip leading '?' or '>' markers
        bool isPrivate = param.StartsWith('?');
        bool isGt = param.StartsWith('>');
        if (isPrivate || isGt) param = param[1..];

        int[] ps = ParseParams(param);
        int p0 = ps.Length > 0 ? ps[0] : 0;
        int p1 = ps.Length > 1 ? ps[1] : 0;

        switch (cmd)
        {
            case 'A': _buf.MoveCursorRelative(-(Math.Max(1, p0)), 0); break;            // CUU
            case 'B': _buf.MoveCursorRelative(Math.Max(1, p0), 0); break;              // CUD
            case 'C': _buf.MoveCursorRelative(0, Math.Max(1, p0)); break;              // CUF
            case 'D': _buf.MoveCursorRelative(0, -(Math.Max(1, p0))); break;           // CUB
            case 'E': _buf.SetCursor(_buf.CursorY + Math.Max(1, p0), 0); break;        // CNL
            case 'F': _buf.SetCursor(_buf.CursorY - Math.Max(1, p0), 0); break;        // CPL
            case 'G': _buf.SetCursor(_buf.CursorY, Math.Max(1, p0) - 1); break;       // CHA
            case 'H': case 'f':                                                         // CUP / HVP
                _buf.SetCursor(Math.Max(1, p0) - 1, Math.Max(1, p1) - 1);
                break;
            case 'J': _buf.EraseDisplay(p0); break;                                    // ED
            case 'K': _buf.EraseLine(p0); break;                                       // EL
            case 'L': _buf.InsertLines(Math.Max(1, p0)); break;                        // IL
            case 'M': _buf.DeleteLines(Math.Max(1, p0)); break;                        // DL
            case 'P': _buf.DeleteChars(Math.Max(1, p0)); break;                        // DCH
            case 'S': /* scroll up */ break;
            case 'T': /* scroll down */ break;
            case 'd': _buf.SetCursor(Math.Max(1, p0) - 1, _buf.CursorX); break;       // VPA
            case 'm': _buf.ApplySgr(ps); break;                                        // SGR
            case 'h':
                if (isPrivate) HandleDecSet(p0, true); break;
            case 'l':
                if (isPrivate) HandleDecSet(p0, false); break;
            case 's': _buf.SaveCursor(); break;
            case 'u': _buf.RestoreCursor(); break;
            case 'r': /* DECSTBM scrolling region — ignore for simplicity */ break;
            // Device attributes — we just swallow them
            case 'c': case 'n': break;
        }
    }

    private void HandleDecSet(int mode, bool set)
    {
        switch (mode)
        {
            case 25: _buf.SetCursorVisible(set); break; // DECTCEM cursor visibility
            case 1049: /* alt screen — ignore */ break;
            case 2004: /* bracketed paste mode — ignore */ break;
        }
    }

    private void FullReset()
    {
        _buf.EraseDisplay(2);
        _buf.SetCursor(0, 0);
    }

    private static int[] ParseParams(string param)
    {
        if (string.IsNullOrEmpty(param)) return [];
        var parts = param.Split(';');
        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }
}
