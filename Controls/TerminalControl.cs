using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SshClient.Controls;

/// <summary>
/// Custom WPF terminal control — renders a fixed character grid using DrawingContext
/// with full ANSI colour support, text selection, and keyboard input routing.
/// </summary>
public sealed class TerminalControl : FrameworkElement
{
    // ---- Dependency Properties ----

    public static readonly DependencyProperty TerminalFontSizeProperty =
        DependencyProperty.Register(nameof(TerminalFontSize), typeof(double), typeof(TerminalControl),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure, OnFontChanged));

    public double TerminalFontSize
    {
        get => (double)GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    // ---- Events ----

    public event EventHandler<string>? UserInput;
    public event EventHandler<(int Cols, int Rows)>? Resized;

    // ---- Fields ----

    private TerminalBuffer _buffer = new(80, 24);
    private AnsiParser _parser;

    private GlyphTypeface? _glyphTypeface;
    private double _charWidth;
    private double _charHeight;
    private double _charBaseline;

    private readonly DispatcherTimer _blinkTimer;
    private bool _cursorBlink;

    private Point? _selStart, _selEnd;
    private bool _isSelecting;

    private readonly VisualCollection _visuals;

    // Background brush — painted in OnRender (FrameworkElement has no Background property)

    // Brushes cached for performance
    private readonly Dictionary<Color, Brush> _brushCache = [];

    public TerminalBuffer Buffer => _buffer;

    public TerminalControl()
    {
        _parser = new AnsiParser(_buffer);
        _buffer.Changed += (_, _) => InvalidateVisual();

        _visuals = new VisualCollection(this);

        Focusable = true;
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) => { _cursorBlink = !_cursorBlink; InvalidateVisual(); };
        _blinkTimer.Start();

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;

        MeasureFont();
    }

    // ---- Public API ----

    public void FeedData(string data)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _parser.Feed(data);
            InvalidateVisual();
        }, DispatcherPriority.Normal);
    }

    public void Clear()
    {
        _buffer = new TerminalBuffer(_buffer.Cols, _buffer.Rows);
        _parser = new AnsiParser(_buffer);
        _buffer.Changed += (_, _) => InvalidateVisual();
        InvalidateVisual();
    }

    public string GetSelectedText()
    {
        if (_selStart is null || _selEnd is null) return string.Empty;
        var (r1, c1) = HitTestCell(_selStart.Value);
        var (r2, c2) = HitTestCell(_selEnd.Value);
        if (r1 > r2 || (r1 == r2 && c1 > c2)) { (r1, c1, r2, c2) = (r2, c2, r1, c1); }

        var sb = new System.Text.StringBuilder();
        for (int r = r1; r <= r2; r++)
        {
            int cs = r == r1 ? c1 : 0;
            int ce = r == r2 ? c2 : _buffer.Cols - 1;
            for (int c = cs; c <= ce; c++)
                sb.Append(_buffer.GetScreenCell(r, c).Char);
            if (r < r2) sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // ---- Layout / Measure ----

    protected override Size MeasureOverride(Size availableSize)
    {
        MeasureFont();
        return availableSize.Width is double.PositiveInfinity
            ? new Size(_charWidth * 80, _charHeight * 24)
            : availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int newCols = Math.Max(10, (int)(finalSize.Width / _charWidth));
        int newRows = Math.Max(4, (int)(finalSize.Height / _charHeight));
        if (newCols != _buffer.Cols || newRows != _buffer.Rows)
        {
            _buffer.Resize(newCols, newRows);
            Resized?.Invoke(this, (newCols, newRows));
        }
        return finalSize;
    }

    // ---- Rendering ----

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(GetBrush(TerminalCell.DefaultBg), null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        if (_glyphTypeface is null) return;

        int cols = _buffer.Cols;
        int rows = _buffer.Rows;

        for (int row = 0; row < rows; row++)
        {
            double y = row * _charHeight;

            for (int col = 0; col < cols; col++)
            {
                var cell = _buffer.GetScreenCell(row, col);
                double x = col * _charWidth;

                // Background
                if (cell.Background != TerminalCell.DefaultBg)
                    dc.DrawRectangle(GetBrush(cell.Background), null,
                        new Rect(x, y, _charWidth, _charHeight));

                // Glyph
                if (cell.Char > ' ')
                    DrawGlyph(dc, cell.Char, x, y + _charBaseline,
                        cell.Bold ? Color.FromRgb(255, 255, 255) : cell.Foreground,
                        cell.Bold);

                // Underline
                if (cell.Underline)
                    dc.DrawLine(new Pen(GetBrush(cell.Foreground), 1),
                        new Point(x, y + _charHeight - 2),
                        new Point(x + _charWidth, y + _charHeight - 2));
            }
        }

        // Cursor
        if (_buffer.CursorVisible && _cursorBlink && _buffer.ScrollOffset == 0)
        {
            double cx = _buffer.CursorX * _charWidth;
            double cy = _buffer.CursorY * _charHeight;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 217, 255)), null,
                new Rect(cx, cy, _charWidth, _charHeight));
        }

        // Selection highlight
        if (_selStart.HasValue && _selEnd.HasValue)
            DrawSelection(dc);
    }

    private void DrawGlyph(DrawingContext dc, char ch, double x, double baseline, Color color, bool bold)
    {
        if (_glyphTypeface is null) return;
        if (!_glyphTypeface.CharacterToGlyphMap.TryGetValue(ch, out ushort glyphIdx)) return;

        double em = TerminalFontSize * (bold ? 1.0 : 1.0);
        double advance = _glyphTypeface.AdvanceWidths[glyphIdx] * em;

        var glyphRun = new GlyphRun(
            _glyphTypeface,
            bidiLevel: 0,
            isSideways: false,
            renderingEmSize: em,
            pixelsPerDip: (float)VisualTreeHelper.GetDpi(this).PixelsPerDip,
            glyphIndices: [glyphIdx],
            baselineOrigin: new Point(x, baseline),
            advanceWidths: [advance],
            glyphOffsets: null,
            characters: [ch],
            deviceFontName: null,
            clusterMap: null,
            caretStops: null,
            language: null);

        dc.DrawGlyphRun(GetBrush(color), glyphRun);
    }

    private void DrawSelection(DrawingContext dc)
    {
        if (_selStart is null || _selEnd is null) return;
        var (r1, c1) = HitTestCell(_selStart.Value);
        var (r2, c2) = HitTestCell(_selEnd.Value);
        if (r1 > r2 || (r1 == r2 && c1 > c2)) { (r1, c1, r2, c2) = (r2, c2, r1, c1); }

        var selBrush = new SolidColorBrush(Color.FromArgb(90, 0, 217, 255));
        for (int r = r1; r <= r2; r++)
        {
            int cs = r == r1 ? c1 : 0;
            int ce = r == r2 ? c2 : _buffer.Cols - 1;
            dc.DrawRectangle(selBrush, null,
                new Rect(cs * _charWidth, r * _charHeight, (ce - cs + 1) * _charWidth, _charHeight));
        }
    }

    // ---- Keyboard ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        string? seq = TranslateKey(e);
        if (seq is not null)
        {
            UserInput?.Invoke(this, seq);
            e.Handled = true;
            _buffer.ScrollToBottom();
        }
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text))
        {
            UserInput?.Invoke(this, e.Text);
            e.Handled = true;
            _buffer.ScrollToBottom();
        }
        base.OnTextInput(e);
    }

    private static string? TranslateKey(KeyEventArgs e)
    {
        bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && !shift)
        {
            return e.Key switch
            {
                Key.C => "\x03",
                Key.D => "\x04",
                Key.Z => "\x1A",
                Key.A => "\x01",
                Key.E => "\x05",
                Key.K => "\x0B",
                Key.L => "\x0C",
                Key.U => "\x15",
                Key.W => "\x17",
                Key.R => "\x12",
                Key.P => "\x10",
                Key.N => "\x0E",
                Key.B => "\x02",
                Key.F => "\x06",
                Key.OemCloseBrackets => "\x1D",
                Key.OemBackslash => "\x1C",
                _ => null
            };
        }

        return e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7F",
            Key.Delete => "\x1B[3~",
            Key.Tab => shift ? "\x1B[Z" : "\t",
            Key.Escape => "\x1B",
            Key.Up => "\x1B[A",
            Key.Down => "\x1B[B",
            Key.Right => "\x1B[C",
            Key.Left => "\x1B[D",
            Key.Home => "\x1B[H",
            Key.End => "\x1B[F",
            Key.Prior => "\x1B[5~",   // Page Up
            Key.Next => "\x1B[6~",    // Page Down
            Key.Insert => "\x1B[2~",
            Key.F1 => "\x1BOP",
            Key.F2 => "\x1BOQ",
            Key.F3 => "\x1BOR",
            Key.F4 => "\x1BOS",
            Key.F5 => "\x1B[15~",
            Key.F6 => "\x1B[17~",
            Key.F7 => "\x1B[18~",
            Key.F8 => "\x1B[19~",
            Key.F9 => "\x1B[20~",
            Key.F10 => "\x1B[21~",
            Key.F11 => "\x1B[23~",
            Key.F12 => "\x1B[24~",
            _ => null
        };
    }

    // ---- Mouse ----

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isSelecting = true;
            _selStart = e.GetPosition(this);
            _selEnd = _selStart;
            CaptureMouse();
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            ShowContextMenu();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isSelecting)
        {
            _selEnd = e.GetPosition(this);
            InvalidateVisual();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();
            // Auto-copy on selection
            var text = GetSelectedText();
            if (!string.IsNullOrEmpty(text))
            {
                try { Clipboard.SetText(text); } catch { }
            }
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0) _buffer.ScrollUp(3);
        else _buffer.ScrollDown(3);
        InvalidateVisual();
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu
        {
            Style = (Style?)Application.Current.TryFindResource("DarkContextMenu")
        };

        var copy = new MenuItem { Header = "Copy  Ctrl+Shift+C" };
        copy.Click += (_, _) =>
        {
            var t = GetSelectedText();
            if (!string.IsNullOrEmpty(t)) try { Clipboard.SetText(t); } catch { }
        };

        var paste = new MenuItem { Header = "Paste  Ctrl+Shift+V" };
        paste.Click += (_, _) =>
        {
            if (Clipboard.ContainsText())
                UserInput?.Invoke(this, Clipboard.GetText());
        };

        var clear = new MenuItem { Header = "Clear" };
        clear.Click += (_, _) => Clear();

        var selectAll = new MenuItem { Header = "Select All" };
        selectAll.Click += (_, _) =>
        {
            _selStart = new Point(0, 0);
            _selEnd = new Point(ActualWidth, ActualHeight);
            InvalidateVisual();
        };

        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(clear);
        menu.Items.Add(selectAll);
        menu.IsOpen = true;
    }

    // ---- Font measurement ----

    private void MeasureFont()
    {
        var typeface = new Typeface(new FontFamily("JetBrains Mono, Cascadia Code, Fira Code, Consolas"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        if (!typeface.TryGetGlyphTypeface(out _glyphTypeface))
        {
            // Fallback to system monospace
            typeface = new Typeface("Consolas");
            typeface.TryGetGlyphTypeface(out _glyphTypeface);
        }

        if (_glyphTypeface is null) return;

        _glyphTypeface.CharacterToGlyphMap.TryGetValue('M', out ushort gIdx);
        double em = TerminalFontSize;
        _charWidth = _glyphTypeface.AdvanceWidths[gIdx] * em;
        _charHeight = (_glyphTypeface.Height) * em + 2;
        _charBaseline = _glyphTypeface.Baseline * em;
    }

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalControl tc) { tc.MeasureFont(); tc.InvalidateVisual(); }
    }

    // ---- Helpers ----

    private (int row, int col) HitTestCell(Point p)
    {
        int col = Math.Clamp((int)(p.X / _charWidth), 0, _buffer.Cols - 1);
        int row = Math.Clamp((int)(p.Y / _charHeight), 0, _buffer.Rows - 1);
        return (row, col);
    }

    private Brush GetBrush(Color c)
    {
        if (!_brushCache.TryGetValue(c, out var brush))
        {
            brush = new SolidColorBrush(c);
            brush.Freeze();
            _brushCache[c] = brush;
        }
        return brush;
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
}
