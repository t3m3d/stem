using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Stem.Windows;

public readonly record struct TerminalSearchResult(int Current, int Total);

public sealed class TerminalView : Control
{
    private readonly record struct SelectionPoint(int Row, int Column);

    private TerminalColor _cursorColor = new(139, 92, 246);
    private TerminalColor _selectionBackground = new(58, 42, 96);
    private TerminalColor _configuredForeground = TerminalColor.DefaultForeground;
    private TerminalColor _configuredBackground = TerminalColor.DefaultBackground;
    private TerminalColor _searchBackground = new(70, 48, 110);
    private TerminalColor _activeSearchBackground = new(139, 92, 246);
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly Dictionary<TerminalColor, SolidColorBrush> _brushes = new();
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _bellTimer;
    private double _cellWidth = 8;
    private double _cellHeight = 18;
    private double _inset = 10;
    private double _lineSpacing;
    private StemCursorStyle _cursorStyle = StemCursorStyle.Bar;
    private StemBellMode _bellMode = StemBellMode.Visual;
    private bool _visualBell;
    private bool _cursorPhase = true;
    private bool _selecting;
    private int _scrollOffset;
    private SelectionPoint? _selectionStart;
    private SelectionPoint? _selectionEnd;
    private IReadOnlyList<TerminalTextMatch> _searchMatches = [];
    private readonly Dictionary<int, List<int>> _searchRows = [];
    private string _searchQuery = string.Empty;
    private int _activeSearchIndex = -1;
    private bool _searchDirty;

    public TerminalView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        FontFamily = new FontFamily(StemSettings.DefaultFontFamily);
        FontSize = 13.5;
        Background = Brush(TerminalColor.DefaultBackground);
        Foreground = Brush(TerminalColor.DefaultForeground);
        Buffer = new TerminalBuffer(40, 120);
        Buffer.TitleChanged += title => TitleChanged?.Invoke(title);
        Buffer.ResponseRequested += response => InputReady?.Invoke(response);
        Buffer.BellRequested += RingBell;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorPhase = !_cursorPhase;
            InvalidateVisual();
        };
        _cursorTimer.Start();

        _bellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _bellTimer.Tick += (_, _) =>
        {
            _bellTimer.Stop();
            _visualBell = false;
            InvalidateVisual();
        };

        Loaded += (_, _) =>
        {
            RecalculateCellSize();
            UpdateTerminalSize();
            NotifyScrollState();
        };
    }

    public TerminalBuffer Buffer { get; }
    public int Columns => Buffer.Columns;
    public int Rows => Buffer.Rows;
    public int ScrollOffset => _scrollOffset;
    public bool HasSelection => _selectionStart.HasValue && _selectionEnd.HasValue;
    public bool CopyOnSelect { get; private set; }

    public event Action<string>? InputReady;
    public event Action<int, int>? TerminalSizeChanged;
    public event Action<string>? TitleChanged;
    public event Action<int, int>? ScrollStateChanged;
    public event Action? SearchRequested;

    public void ApplySettings(StemSettings settings)
    {
        FontFamily = new FontFamily(settings.FontFamily);
        FontSize = settings.FontSize;
        _inset = settings.Padding;
        _lineSpacing = settings.LineSpacing;
        _cursorStyle = settings.CursorStyle;
        _bellMode = settings.Bell;
        _configuredBackground = settings.BackgroundColor;
        _configuredForeground = settings.ForegroundColor;
        _cursorColor = settings.CursorColor;
        _selectionBackground = settings.SelectionColor;
        _activeSearchBackground = settings.AccentColor;
        _searchBackground = Mix(settings.BackgroundColor, settings.AccentColor, 0.46);
        Background = BackgroundBrush(_configuredBackground, settings.Opacity);
        Foreground = Brush(_configuredForeground);
        CopyOnSelect = settings.CopyOnSelect;
        Buffer.ScrollbackLimit = settings.ScrollbackLines;
        Buffer.SetAnsiPalette(settings.AnsiPalette);

        if (settings.CursorBlinkMilliseconds <= 0)
        {
            _cursorTimer.Stop();
            _cursorPhase = true;
        }
        else
        {
            _cursorTimer.Interval = TimeSpan.FromMilliseconds(settings.CursorBlinkMilliseconds);
            _cursorTimer.Start();
        }

        RecalculateCellSize();
        if (IsLoaded)
        {
            UpdateTerminalSize();
        }
        InvalidateVisual();
        NotifyScrollState();
    }

    public Size EstimatedGridSize(int columns, int rows) => new(
        _inset * 2 + Math.Max(2, columns) * _cellWidth,
        _inset * 2 + Math.Max(2, rows) * _cellHeight);

    public TerminalSearchResult StartSearch(string query)
    {
        _searchQuery = query ?? string.Empty;
        RebuildSearch();
        if (_searchMatches.Count == 0)
        {
            InvalidateVisual();
            return new TerminalSearchResult(0, 0);
        }

        var viewportTop = Buffer.ScrollbackCount - _scrollOffset;
        _activeSearchIndex = 0;
        for (var index = 0; index < _searchMatches.Count; index++)
        {
            if (_searchMatches[index].DocumentRow >= viewportTop)
            {
                _activeSearchIndex = index;
                break;
            }
        }
        ActivateSearchMatch();
        return SearchResult();
    }

    public TerminalSearchResult FindNextSearch(bool previous = false)
    {
        if (string.IsNullOrEmpty(_searchQuery))
        {
            return new TerminalSearchResult(0, 0);
        }

        if (_searchDirty)
        {
            RebuildSearch();
        }
        if (_searchMatches.Count == 0)
        {
            InvalidateVisual();
            return new TerminalSearchResult(0, 0);
        }

        _activeSearchIndex = previous
            ? (_activeSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count
            : (_activeSearchIndex + 1) % _searchMatches.Count;
        ActivateSearchMatch();
        return SearchResult();
    }

    public void ClearSearch()
    {
        _searchQuery = string.Empty;
        _searchMatches = [];
        _searchRows.Clear();
        _activeSearchIndex = -1;
        _searchDirty = false;
        InvalidateVisual();
    }

    public void Write(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        var sequenceBefore = Buffer.ScrollbackSequence;
        var chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length, flush: false)];
        var count = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
        Buffer.Feed(new string(chars, 0, count));
        _searchDirty = _searchQuery.Length > 0;

        if (_scrollOffset > 0)
        {
            var appended = Buffer.ScrollbackSequence - sequenceBefore;
            _scrollOffset = Math.Clamp(
                _scrollOffset + (int)Math.Min(appended, int.MaxValue),
                0,
                Buffer.ScrollbackCount);
        }

        InvalidateVisual();
        NotifyScrollState();
    }

    public void WriteText(string text)
    {
        var sequenceBefore = Buffer.ScrollbackSequence;
        Buffer.Feed(text);
        _searchDirty = _searchQuery.Length > 0;
        if (_scrollOffset > 0)
        {
            var appended = Buffer.ScrollbackSequence - sequenceBefore;
            _scrollOffset = Math.Clamp(
                _scrollOffset + (int)Math.Min(appended, int.MaxValue),
                0,
                Buffer.ScrollbackCount);
        }
        InvalidateVisual();
        NotifyScrollState();
    }

    public void ClearSelection()
    {
        _selectionStart = null;
        _selectionEnd = null;
        _selecting = false;
        InvalidateVisual();
    }

    public void CopySelection()
    {
        var text = SelectedText();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard ownership can briefly be unavailable. A later copy can
            // retry without destabilizing the terminal session.
        }
    }

    public void SelectViewport()
    {
        _selectionStart = new SelectionPoint(0, 0);
        _selectionEnd = new SelectionPoint(Buffer.Rows - 1, Buffer.Columns - 1);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var row = 0; row < Buffer.Rows; row++)
        {
            DrawRowBackgrounds(drawingContext, row);
            DrawRowText(drawingContext, row, dpi);
        }

        if (_scrollOffset == 0 &&
            IsKeyboardFocusWithin &&
            Buffer.CursorVisible &&
            _cursorPhase)
        {
            var x = _inset + Buffer.CursorColumn * _cellWidth;
            var y = _inset + Buffer.CursorRow * _cellHeight;
            drawingContext.DrawRectangle(
                Brush(_cursorColor),
                null,
                CursorRectangle(x, y));
        }

        DrawScrollIndicator(drawingContext);
        if (_visualBell)
        {
            drawingContext.DrawRectangle(
                null,
                new Pen(Brush(_cursorColor), 2),
                new Rect(1, 1, Math.Max(0, RenderSize.Width - 2), Math.Max(0, RenderSize.Height - 2)));
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateTerminalSize();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _cursorPhase = true;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var point = CellFromPoint(e.GetPosition(this));
        if (e.ClickCount >= 3)
        {
            _selectionStart = new SelectionPoint(point.Row, 0);
            _selectionEnd = new SelectionPoint(point.Row, Buffer.Columns - 1);
            _selecting = false;
        }
        else if (e.ClickCount == 2)
        {
            SelectWord(point);
            _selecting = false;
        }
        else
        {
            _selectionStart = point;
            _selectionEnd = point;
            _selecting = true;
            CaptureMouse();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _selectionEnd = CellFromPoint(e.GetPosition(this));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Left || !_selecting)
        {
            return;
        }

        _selectionEnd = CellFromPoint(e.GetPosition(this));
        _selecting = false;
        ReleaseMouseCapture();
        if (CopyOnSelect)
        {
            CopySelection();
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _selecting = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Buffer.AlternateScreen &&
            _scrollOffset == 0 &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            var sequence = e.Delta > 0 ? "\u001b[A\u001b[A\u001b[A" : "\u001b[B\u001b[B\u001b[B";
            InputReady?.Invoke(sequence);
        }
        else
        {
            ScrollBy(e.Delta > 0 ? 3 : -3);
        }
        e.Handled = true;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text) && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            ReturnToLive();
            ClearSelection();
            var prefix = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? "\u001b" : string.Empty;
            InputReady?.Invoke(prefix + e.Text);
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var control = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;

        if (control && shift && e.Key == Key.F)
        {
            SearchRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.C)
        {
            CopySelection();
            e.Handled = true;
            return;
        }

        if ((control && shift && e.Key == Key.V) || (shift && e.Key == Key.Insert))
        {
            PasteClipboard();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.A)
        {
            SelectViewport();
            e.Handled = true;
            return;
        }

        if (shift && e.Key == Key.PageUp)
        {
            ScrollBy(Math.Max(1, Buffer.Rows - 1));
            e.Handled = true;
            return;
        }

        if (shift && e.Key == Key.PageDown)
        {
            ScrollBy(-Math.Max(1, Buffer.Rows - 1));
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.Home)
        {
            SetScrollOffset(Buffer.ScrollbackCount);
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.End)
        {
            ReturnToLive();
            e.Handled = true;
            return;
        }

        if (control && e.Key is >= Key.A and <= Key.Z)
        {
            ReturnToLive();
            ClearSelection();
            var value = (char)(1 + e.Key - Key.A);
            InputReady?.Invoke(value.ToString());
            e.Handled = true;
            return;
        }

        string? sequence = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\u007f",
            Key.Tab when shift => "\u001b[Z",
            Key.Tab => "\t",
            Key.Escape => "\u001b",
            Key.Up => "\u001b[A",
            Key.Down => "\u001b[B",
            Key.Right => "\u001b[C",
            Key.Left => "\u001b[D",
            Key.Home => "\u001b[H",
            Key.End => "\u001b[F",
            Key.Insert => "\u001b[2~",
            Key.Delete => "\u001b[3~",
            Key.PageUp => "\u001b[5~",
            Key.PageDown => "\u001b[6~",
            Key.F1 => "\u001bOP",
            Key.F2 => "\u001bOQ",
            Key.F3 => "\u001bOR",
            Key.F4 => "\u001bOS",
            Key.F5 => "\u001b[15~",
            Key.F6 => "\u001b[17~",
            Key.F7 => "\u001b[18~",
            Key.F8 => "\u001b[19~",
            Key.F9 => "\u001b[20~",
            Key.F10 => "\u001b[21~",
            Key.F11 => "\u001b[23~",
            Key.F12 => "\u001b[24~",
            _ => null
        };

        if (sequence is not null)
        {
            ReturnToLive();
            ClearSelection();
            if ((modifiers & ModifierKeys.Alt) != 0)
            {
                sequence = "\u001b" + sequence;
            }
            InputReady?.Invoke(sequence);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    public void PasteClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        ReturnToLive();
        ClearSelection();
        var text = Clipboard.GetText().Replace("\r\n", "\n").Replace('\r', '\n');
        if (Buffer.BracketedPaste)
        {
            text = "\u001b[200~" + text + "\u001b[201~";
        }
        InputReady?.Invoke(text);
    }

    private void SelectWord(SelectionPoint point)
    {
        var line = Buffer.ViewLineText(point.Row, _scrollOffset);
        if (line.Length == 0)
        {
            _selectionStart = _selectionEnd = point;
            return;
        }

        var column = Math.Clamp(point.Column, 0, line.Length - 1);
        var word = IsWordCharacter(line[column]);
        var start = column;
        var end = column;
        while (start > 0 && IsWordCharacter(line[start - 1]) == word) start--;
        while (end + 1 < line.Length && IsWordCharacter(line[end + 1]) == word) end++;
        _selectionStart = new SelectionPoint(point.Row, start);
        _selectionEnd = new SelectionPoint(point.Row, end);
    }

    private static bool IsWordCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.' or '/' or '\\' or ':' or '~';

    private string SelectedText()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue)
        {
            return string.Empty;
        }

        var (start, end) = NormalizedSelection();
        var result = new StringBuilder();
        for (var row = start.Row; row <= end.Row; row++)
        {
            var firstColumn = row == start.Row ? start.Column : 0;
            var lastColumn = row == end.Row ? end.Column : Buffer.Columns - 1;
            var line = new StringBuilder(lastColumn - firstColumn + 1);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var cell = Buffer.ViewCellAt(row, column, _scrollOffset);
                if (!cell.Continuation)
                {
                    line.Append(cell.Text);
                }
            }

            result.Append(line.ToString().TrimEnd());
            if (row < end.Row)
            {
                result.AppendLine();
            }
        }
        return result.ToString();
    }

    private (SelectionPoint Start, SelectionPoint End) NormalizedSelection()
    {
        var start = _selectionStart!.Value;
        var end = _selectionEnd!.Value;
        var startIndex = start.Row * Buffer.Columns + start.Column;
        var endIndex = end.Row * Buffer.Columns + end.Column;
        return startIndex <= endIndex ? (start, end) : (end, start);
    }

    private bool IsCellSelected(int row, int column)
    {
        if (!HasSelection)
        {
            return false;
        }

        var (start, end) = NormalizedSelection();
        var index = row * Buffer.Columns + column;
        var startIndex = start.Row * Buffer.Columns + start.Column;
        var endIndex = end.Row * Buffer.Columns + end.Column;
        return index >= startIndex && index <= endIndex;
    }

    private SelectionPoint CellFromPoint(Point point)
    {
        var column = (int)((point.X - _inset) / _cellWidth);
        var row = (int)((point.Y - _inset) / _cellHeight);
        return new SelectionPoint(
            Math.Clamp(row, 0, Buffer.Rows - 1),
            Math.Clamp(column, 0, Buffer.Columns - 1));
    }

    private void ScrollBy(int lines)
    {
        SetScrollOffset(_scrollOffset + lines);
    }

    private void SetScrollOffset(int offset)
    {
        var next = Math.Clamp(offset, 0, Buffer.ScrollbackCount);
        if (next == _scrollOffset)
        {
            return;
        }

        _scrollOffset = next;
        ClearSelection();
        InvalidateVisual();
        NotifyScrollState();
    }

    private void ReturnToLive()
    {
        SetScrollOffset(0);
    }

    private void NotifyScrollState()
    {
        ScrollStateChanged?.Invoke(_scrollOffset, Buffer.ScrollbackCount);
    }

    private void DrawRowBackgrounds(DrawingContext drawingContext, int row)
    {
        var start = 0;
        while (start < Buffer.Columns)
        {
            var style = EffectiveStyle(Buffer.ViewCellAt(row, start, _scrollOffset).Style);
            var selected = IsCellSelected(row, start);
            var searchHighlight = SearchHighlightAt(row, start);
            var end = start + 1;
            while (end < Buffer.Columns)
            {
                var nextStyle = EffectiveStyle(Buffer.ViewCellAt(row, end, _scrollOffset).Style);
                if (nextStyle.Background != style.Background ||
                    IsCellSelected(row, end) != selected ||
                    SearchHighlightAt(row, end) != searchHighlight)
                {
                    break;
                }
                end++;
            }

            var background = selected
                ? _selectionBackground
                : searchHighlight == 2
                    ? _activeSearchBackground
                    : searchHighlight == 1
                        ? _searchBackground
                        : style.Background;
            if (selected || background != TerminalColor.DefaultBackground)
            {
                drawingContext.DrawRectangle(
                    Brush(background),
                    null,
                    new Rect(
                        _inset + start * _cellWidth,
                        _inset + row * _cellHeight,
                        (end - start) * _cellWidth,
                        _cellHeight));
            }
            start = end;
        }
    }

    private void DrawRowText(DrawingContext drawingContext, int row, double pixelsPerDip)
    {
        var start = 0;
        while (start < Buffer.Columns)
        {
            var style = EffectiveStyle(Buffer.ViewCellAt(row, start, _scrollOffset).Style);
            var text = new StringBuilder();
            var end = start;
            while (end < Buffer.Columns &&
                   EffectiveStyle(Buffer.ViewCellAt(row, end, _scrollOffset).Style) == style)
            {
                var cell = Buffer.ViewCellAt(row, end, _scrollOffset);
                if (!cell.Continuation)
                {
                    text.Append(cell.Text);
                }
                end++;
            }

            if (!IsBlank(text))
            {
                var typeface = new Typeface(
                    FontFamily,
                    FontStyles.Normal,
                    style.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal);
                var formatted = new FormattedText(
                    text.ToString(),
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    FontSize,
                    Brush(style.Foreground),
                    pixelsPerDip)
                {
                    Trimming = TextTrimming.None
                };
                if (style.Underline)
                {
                    formatted.SetTextDecorations(TextDecorations.Underline);
                }
                drawingContext.DrawText(
                    formatted,
                    new Point(_inset + start * _cellWidth, _inset + row * _cellHeight));
            }
            start = end;
        }
    }

    private void DrawScrollIndicator(DrawingContext drawingContext)
    {
        if (Buffer.ScrollbackCount == 0)
        {
            return;
        }

        var trackHeight = Math.Max(1, RenderSize.Height - _inset * 2);
        var totalLines = Buffer.ScrollbackCount + Buffer.Rows;
        var thumbHeight = Math.Max(20, trackHeight * Buffer.Rows / totalLines);
        var travel = Math.Max(0, trackHeight - thumbHeight);
        var position = Buffer.ScrollbackCount == 0
            ? travel
            : travel * (Buffer.ScrollbackCount - _scrollOffset) / Buffer.ScrollbackCount;
        var trackX = Math.Max(0, RenderSize.Width - 5);

        drawingContext.DrawRoundedRectangle(
            Brush(new TerminalColor(20, 29, 43)),
            null,
            new Rect(trackX, _inset, 2, trackHeight),
            1,
            1);
        drawingContext.DrawRoundedRectangle(
            Brush(_scrollOffset > 0 ? _cursorColor : new TerminalColor(70, 83, 103)),
            null,
            new Rect(trackX, _inset + position, 2, thumbHeight),
            1,
            1);
    }

    private static bool IsBlank(StringBuilder text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ')
            {
                return false;
            }
        }
        return true;
    }

    private TerminalStyle EffectiveStyle(TerminalStyle style)
    {
        if (style.Foreground == TerminalColor.DefaultForeground)
        {
            style = style with { Foreground = _configuredForeground };
        }
        if (style.Background == TerminalColor.DefaultBackground)
        {
            style = style with { Background = _configuredBackground };
        }
        return style.Inverse
            ? style with { Foreground = style.Background, Background = style.Foreground, Inverse = false }
            : style;
    }

    private void RecalculateCellSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var sample = new FormattedText(
            "M",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            FontSize,
            Foreground,
            dpi);
        _cellWidth = Math.Max(1, Math.Ceiling(sample.WidthIncludingTrailingWhitespace));
        _cellHeight = Math.Max(1, Math.Ceiling(sample.Height * 1.08 + _lineSpacing));
    }

    private Rect CursorRectangle(double x, double y) => _cursorStyle switch
    {
        StemCursorStyle.Block => new Rect(x, y, _cellWidth, _cellHeight),
        StemCursorStyle.Underline => new Rect(x, y + Math.Max(0, _cellHeight - 2), _cellWidth, 2),
        _ => new Rect(x, y, Math.Max(2, _cellWidth * 0.14), _cellHeight)
    };

    private void RingBell()
    {
        switch (_bellMode)
        {
            case StemBellMode.Audible:
                System.Media.SystemSounds.Beep.Play();
                break;
            case StemBellMode.Visual:
                _visualBell = true;
                _bellTimer.Stop();
                _bellTimer.Start();
                InvalidateVisual();
                break;
        }
    }

    private void UpdateTerminalSize()
    {
        if (ActualWidth <= _inset * 2 || ActualHeight <= _inset * 2)
        {
            return;
        }

        var columns = Math.Max(2, (int)((ActualWidth - _inset * 2) / _cellWidth));
        var rows = Math.Max(2, (int)((ActualHeight - _inset * 2) / _cellHeight));
        if (columns == Buffer.Columns && rows == Buffer.Rows)
        {
            return;
        }

        Buffer.Resize(rows, columns);
        _searchDirty = _searchQuery.Length > 0;
        _scrollOffset = Math.Min(_scrollOffset, Buffer.ScrollbackCount);
        ClearSelection();
        TerminalSizeChanged?.Invoke(columns, rows);
        InvalidateVisual();
        NotifyScrollState();
    }

    private void RebuildSearch()
    {
        _searchMatches = Buffer.FindText(_searchQuery);
        _searchRows.Clear();
        for (var index = 0; index < _searchMatches.Count; index++)
        {
            var row = _searchMatches[index].DocumentRow;
            if (!_searchRows.TryGetValue(row, out var indices))
            {
                indices = [];
                _searchRows[row] = indices;
            }
            indices.Add(index);
        }
        _activeSearchIndex = _searchMatches.Count == 0
            ? -1
            : Math.Clamp(_activeSearchIndex, 0, _searchMatches.Count - 1);
        _searchDirty = false;
    }

    private void ActivateSearchMatch()
    {
        if (_activeSearchIndex < 0 || _activeSearchIndex >= _searchMatches.Count)
        {
            return;
        }

        var match = _searchMatches[_activeSearchIndex];
        var viewportTop = Buffer.ScrollbackCount - _scrollOffset;
        var viewportBottom = viewportTop + Buffer.Rows - 1;
        if (match.DocumentRow < viewportTop || match.DocumentRow > viewportBottom)
        {
            var targetRow = Math.Max(0, Buffer.Rows / 3);
            var offset = Buffer.ScrollbackCount + targetRow - match.DocumentRow;
            SetScrollOffset(offset);
        }
        InvalidateVisual();
    }

    private TerminalSearchResult SearchResult() => new(
        _activeSearchIndex < 0 ? 0 : _activeSearchIndex + 1,
        _searchMatches.Count);

    private int SearchHighlightAt(int viewRow, int column)
    {
        if (_searchMatches.Count == 0)
        {
            return 0;
        }

        var documentRow = Buffer.ScrollbackCount - _scrollOffset + viewRow;
        if (!_searchRows.TryGetValue(documentRow, out var indices))
        {
            return 0;
        }

        foreach (var index in indices)
        {
            var match = _searchMatches[index];
            if (column >= match.StartColumn && column <= match.EndColumn)
            {
                return index == _activeSearchIndex ? 2 : 1;
            }
        }
        return 0;
    }

    private static TerminalColor Mix(TerminalColor first, TerminalColor second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        static byte Channel(byte a, byte b, double value) =>
            (byte)Math.Clamp((int)Math.Round(a + (b - a) * value), 0, 255);
        return new TerminalColor(
            Channel(first.R, second.R, amount),
            Channel(first.G, second.G, amount),
            Channel(first.B, second.B, amount));
    }

    private static SolidColorBrush BackgroundBrush(TerminalColor color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0.2, 1) * 255),
            color.R,
            color.G,
            color.B));
        brush.Freeze();
        return brush;
    }

    private SolidColorBrush Brush(TerminalColor color)
    {
        if (_brushes.TryGetValue(color, out var existing))
        {
            return existing;
        }

        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        _brushes[color] = brush;
        return brush;
    }
}
