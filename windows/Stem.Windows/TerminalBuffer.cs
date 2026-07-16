using System.Text;

namespace Stem.Windows;

public readonly record struct TerminalColor(byte R, byte G, byte B)
{
    public static readonly TerminalColor DefaultForeground = new(216, 218, 212);
    public static readonly TerminalColor DefaultBackground = new(5, 7, 12);
}

public readonly record struct TerminalStyle(
    TerminalColor Foreground,
    TerminalColor Background,
    bool Bold = false,
    bool Underline = false,
    bool Inverse = false)
{
    public static readonly TerminalStyle Default = new(
        TerminalColor.DefaultForeground,
        TerminalColor.DefaultBackground);
}

public readonly record struct TerminalTextMatch(int DocumentRow, int StartColumn, int EndColumn);

public readonly record struct TerminalCell(
    string Text,
    TerminalStyle Style,
    bool Continuation = false)
{
    public TerminalCell(char character, TerminalStyle style)
        : this(character.ToString(), style)
    {
    }

    public char Character => string.IsNullOrEmpty(Text) ? ' ' : Text[0];
}

/// <summary>
/// Small VT grid used by the temporary Windows host. The mature portable
/// implementation remains term.k; keeping this class independent makes its
/// eventual replacement with the Krypton engine straightforward.
/// </summary>
public sealed class TerminalBuffer
{
    private enum ParserState
    {
        Ground,
        Escape,
        Csi,
        Osc,
        OscEscape
    }

    private readonly TerminalColor[] _ansi16 =
    [
        new(30, 30, 30), new(241, 76, 76), new(35, 209, 139), new(245, 245, 67),
        new(59, 142, 234), new(214, 112, 214), new(41, 184, 219), new(229, 229, 229),
        new(102, 102, 102), new(241, 76, 76), new(35, 209, 139), new(245, 245, 67),
        new(59, 142, 234), new(214, 112, 214), new(41, 184, 219), new(255, 255, 255)
    ];

    private TerminalCell[,] _cells;
    private TerminalCell[,]? _primaryCells;
    private readonly List<TerminalCell[]> _scrollback = [];
    private readonly StringBuilder _sequence = new();
    private ParserState _state;
    private TerminalStyle _style = TerminalStyle.Default;
    private int _savedRow;
    private int _savedColumn;
    private int _primaryRow;
    private int _primaryColumn;
    private int _scrollTop;
    private int _scrollBottom;
    private bool _pendingWrap;
    private bool _alternateScreen;
    private int _scrollbackLimit = 10_000;

    public TerminalBuffer(int rows, int columns)
    {
        Rows = Math.Max(2, rows);
        Columns = Math.Max(2, columns);
        _cells = NewGrid(Rows, Columns, TerminalStyle.Default);
        _scrollBottom = Rows - 1;
    }

    public int Rows { get; private set; }
    public int Columns { get; private set; }
    public int CursorRow { get; private set; }
    public int CursorColumn { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public bool AutoWrap { get; private set; } = true;
    public bool BracketedPaste { get; private set; }
    public bool AlternateScreen => _alternateScreen;
    public int ScrollbackCount => _scrollback.Count;
    public int DocumentLineCount => _scrollback.Count + Rows;
    public long ScrollbackSequence { get; private set; }
    public int ScrollbackLimit
    {
        get => _scrollbackLimit;
        set
        {
            _scrollbackLimit = Math.Clamp(value, 0, 1_000_000);
            TrimScrollback();
        }
    }

    public event Action<string>? TitleChanged;
    public event Action<string>? ResponseRequested;
    public event Action? BellRequested;

    public TerminalCell CellAt(int row, int column) => _cells[row, column];

    public void SetAnsiPalette(IReadOnlyList<TerminalColor> colors)
    {
        var count = Math.Min(_ansi16.Length, colors.Count);
        for (var index = 0; index < count; index++)
        {
            _ansi16[index] = colors[index];
        }
    }

    public TerminalCell DocumentCellAt(int documentRow, int column)
    {
        documentRow = Math.Clamp(documentRow, 0, Math.Max(0, DocumentLineCount - 1));
        column = Math.Clamp(column, 0, Columns - 1);
        if (documentRow < _scrollback.Count)
        {
            var line = _scrollback[documentRow];
            return column < line.Length
                ? line[column]
                : new TerminalCell(' ', TerminalStyle.Default);
        }

        return _cells[documentRow - _scrollback.Count, column];
    }

    public string DocumentLineText(int documentRow, bool trimEnd = false)
    {
        var text = new StringBuilder(Columns);
        for (var column = 0; column < Columns; column++)
        {
            var cell = DocumentCellAt(documentRow, column);
            if (!cell.Continuation)
            {
                text.Append(cell.Text);
            }
        }

        return trimEnd ? text.ToString().TrimEnd() : text.ToString();
    }

    public IReadOnlyList<TerminalTextMatch> FindText(
        string query,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.IsNullOrEmpty(query))
        {
            return [];
        }

        var matches = new List<TerminalTextMatch>();
        for (var row = 0; row < DocumentLineCount; row++)
        {
            var text = new StringBuilder(Columns);
            var columns = new List<int>(Columns);
            for (var column = 0; column < Columns; column++)
            {
                var cell = DocumentCellAt(row, column);
                if (cell.Continuation)
                {
                    continue;
                }

                text.Append(cell.Text);
                for (var index = 0; index < cell.Text.Length; index++)
                {
                    columns.Add(column);
                }
            }

            var line = text.ToString();
            var searchFrom = 0;
            while (searchFrom <= line.Length - query.Length)
            {
                var found = line.IndexOf(query, searchFrom, comparison);
                if (found < 0)
                {
                    break;
                }

                var startColumn = columns[found];
                var finalCharacter = found + query.Length - 1;
                var endColumn = columns[finalCharacter];
                while (endColumn + 1 < Columns && DocumentCellAt(row, endColumn + 1).Continuation)
                {
                    endColumn++;
                }

                matches.Add(new TerminalTextMatch(row, startColumn, endColumn));
                searchFrom = found + 1;
            }
        }

        return matches;
    }

    public TerminalCell ViewCellAt(int viewRow, int column, int scrollOffset)
    {
        viewRow = Math.Clamp(viewRow, 0, Rows - 1);
        scrollOffset = Math.Clamp(scrollOffset, 0, _scrollback.Count);
        var documentRow = _scrollback.Count - scrollOffset + viewRow;
        return DocumentCellAt(documentRow, column);
    }

    public string ViewLineText(int viewRow, int scrollOffset, bool trimEnd = false)
    {
        viewRow = Math.Clamp(viewRow, 0, Rows - 1);
        scrollOffset = Math.Clamp(scrollOffset, 0, _scrollback.Count);
        var documentRow = _scrollback.Count - scrollOffset + viewRow;
        return DocumentLineText(documentRow, trimEnd);
    }

    public void ClearScrollback()
    {
        _scrollback.Clear();
    }

    public void Feed(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            Feed(rune);
        }
    }

    public void Resize(int rows, int columns)
    {
        rows = Math.Max(2, rows);
        columns = Math.Max(2, columns);
        if (rows == Rows && columns == Columns)
        {
            return;
        }

        _cells = ResizeGrid(_cells, rows, columns);
        if (_primaryCells is not null)
        {
            _primaryCells = ResizeGrid(_primaryCells, rows, columns);
        }
        Rows = rows;
        Columns = columns;
        CursorRow = Math.Clamp(CursorRow, 0, Rows - 1);
        CursorColumn = Math.Clamp(CursorColumn, 0, Columns - 1);
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        _pendingWrap = false;
    }

    private void Feed(Rune rune)
    {
        if (rune.IsAscii)
        {
            Feed((char)rune.Value);
            return;
        }

        if (Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.Control)
        {
            return;
        }

        switch (_state)
        {
            case ParserState.Ground:
                Put(rune);
                return;
            case ParserState.Csi:
                if (_sequence.Length < 256)
                {
                    _sequence.Append(rune.ToString());
                }
                return;
            case ParserState.Osc:
                if (_sequence.Length < 4096)
                {
                    _sequence.Append(rune.ToString());
                }
                return;
            case ParserState.OscEscape:
                _sequence.Append('\u001b');
                _sequence.Append(rune.ToString());
                _state = ParserState.Osc;
                return;
            case ParserState.Escape:
                _state = ParserState.Ground;
                return;
        }
    }

    private void Feed(char c)    {
        switch (_state)
        {
            case ParserState.Ground:
                FeedGround(c);
                return;
            case ParserState.Escape:
                FeedEscape(c);
                return;
            case ParserState.Csi:
                if (c is >= '@' and <= '~')
                {
                    ExecuteCsi(c, _sequence.ToString());
                    _sequence.Clear();
                    _state = ParserState.Ground;
                }
                else if (_sequence.Length < 256)
                {
                    _sequence.Append(c);
                }
                return;
            case ParserState.Osc:
                if (c == '\a')
                {
                    ExecuteOsc();
                    _state = ParserState.Ground;
                }
                else if (c == '\u001b')
                {
                    _state = ParserState.OscEscape;
                }
                else if (_sequence.Length < 4096)
                {
                    _sequence.Append(c);
                }
                return;
            case ParserState.OscEscape:
                if (c == '\\')
                {
                    ExecuteOsc();
                    _state = ParserState.Ground;
                }
                else
                {
                    _sequence.Append('\u001b');
                    _sequence.Append(c);
                    _state = ParserState.Osc;
                }
                return;
        }
    }

    private void FeedGround(char c)
    {
        switch (c)
        {
            case '\u001b':
                _state = ParserState.Escape;
                return;
            case '\r':
                CursorColumn = 0;
                _pendingWrap = false;
                return;
            case '\n':
            case '\v':
            case '\f':
                LineFeed();
                return;
            case '\b':
                CursorColumn = Math.Max(0, CursorColumn - 1);
                _pendingWrap = false;
                return;
            case '\t':
                CursorColumn = Math.Min(Columns - 1, ((CursorColumn / 8) + 1) * 8);
                return;
            case '\a':
                BellRequested?.Invoke();
                return;
        }

        if (!char.IsControl(c))
        {
            Put(c);
        }
    }

    private void FeedEscape(char c)
    {
        _state = ParserState.Ground;
        switch (c)
        {
            case '[':
                _sequence.Clear();
                _state = ParserState.Csi;
                break;
            case ']':
                _sequence.Clear();
                _state = ParserState.Osc;
                break;
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                CursorColumn = 0;
                LineFeed();
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                Reset();
                break;
        }
    }

    private void Put(char c) => Put(new Rune(c));

    private void Put(Rune rune)
    {
        var width = CellWidth(rune);
        if (width == 0)
        {
            AppendToPreviousCell(rune.ToString());
            return;
        }

        if (_pendingWrap)
        {
            CursorColumn = 0;
            LineFeed();
            _pendingWrap = false;
        }

        if (width == 2 && CursorColumn == Columns - 1)
        {
            if (AutoWrap)
            {
                CursorColumn = 0;
                LineFeed();
            }
            else
            {
                width = 1;
            }
        }

        _cells[CursorRow, CursorColumn] = new TerminalCell(rune.ToString(), _style);
        if (width == 2)
        {
            _cells[CursorRow, CursorColumn + 1] = new TerminalCell(string.Empty, _style, Continuation: true);
        }

        var nextColumn = CursorColumn + width;
        if (nextColumn >= Columns)
        {
            CursorColumn = Columns - 1;
            _pendingWrap = AutoWrap;
        }
        else
        {
            CursorColumn = nextColumn;
        }
    }

    private void AppendToPreviousCell(string text)
    {
        var column = _pendingWrap ? CursorColumn : CursorColumn - 1;
        if (column < 0)
        {
            return;
        }

        while (column > 0 && _cells[CursorRow, column].Continuation)
        {
            column--;
        }

        var cell = _cells[CursorRow, column];
        if (!cell.Continuation)
        {
            _cells[CursorRow, column] = cell with { Text = cell.Text + text };
        }
    }

    private static int CellWidth(Rune rune)
    {
        var value = rune.Value;
        var category = Rune.GetUnicodeCategory(rune);
        if (category is System.Globalization.UnicodeCategory.NonSpacingMark or
            System.Globalization.UnicodeCategory.SpacingCombiningMark or
            System.Globalization.UnicodeCategory.EnclosingMark ||
            value == 0x200D ||
            value is >= 0xFE00 and <= 0xFE0F ||
            value is >= 0xE0100 and <= 0xE01EF)
        {
            return 0;
        }

        return IsWide(value) ? 2 : 1;
    }

    private static bool IsWide(int value) =>
        value is >= 0x1100 and <= 0x115F ||
        value is 0x2329 or 0x232A ||
        value is >= 0x2E80 and <= 0xA4CF and not 0x303F ||
        value is >= 0xAC00 and <= 0xD7A3 ||
        value is >= 0xF900 and <= 0xFAFF ||
        value is >= 0xFE10 and <= 0xFE19 ||
        value is >= 0xFE30 and <= 0xFE6F ||
        value is >= 0xFF00 and <= 0xFF60 ||
        value is >= 0xFFE0 and <= 0xFFE6 ||
        value is >= 0x1F300 and <= 0x1FAFF ||
        value is >= 0x20000 and <= 0x3FFFD;

    private void LineFeed()
    {
        if (CursorRow == _scrollBottom)
        {
            ScrollUp(_scrollTop, _scrollBottom, 1);
        }
        else
        {
            CursorRow = Math.Min(Rows - 1, CursorRow + 1);
        }
    }

    private void ReverseIndex()
    {
        if (CursorRow == _scrollTop)
        {
            ScrollDown(_scrollTop, _scrollBottom, 1);
        }
        else
        {
            CursorRow = Math.Max(0, CursorRow - 1);
        }
    }

    private void ExecuteCsi(char command, string raw)
    {
        var privateMode = raw.StartsWith('?');
        if (privateMode)
        {
            raw = raw[1..];
        }
        var args = ParseArguments(raw);
        var count = Value(args, 0, 1);

        switch (command)
        {
            case 'A': CursorRow = Math.Max(_scrollTop, CursorRow - count); break;
            case 'B': CursorRow = Math.Min(_scrollBottom, CursorRow + count); break;
            case 'C': CursorColumn = Math.Min(Columns - 1, CursorColumn + count); break;
            case 'D': CursorColumn = Math.Max(0, CursorColumn - count); break;
            case 'E': CursorRow = Math.Min(_scrollBottom, CursorRow + count); CursorColumn = 0; break;
            case 'F': CursorRow = Math.Max(_scrollTop, CursorRow - count); CursorColumn = 0; break;
            case 'G': CursorColumn = Math.Clamp(count - 1, 0, Columns - 1); break;
            case 'd': CursorRow = Math.Clamp(count - 1, 0, Rows - 1); break;
            case 'H':
            case 'f':
                CursorRow = Math.Clamp(Value(args, 0, 1) - 1, 0, Rows - 1);
                CursorColumn = Math.Clamp(Value(args, 1, 1) - 1, 0, Columns - 1);
                break;
            case 'J': EraseDisplay(Value(args, 0, 0)); break;
            case 'K': EraseLine(Value(args, 0, 0)); break;
            case 'm': ApplySgr(args); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'r': SetScrollRegion(args); break;
            case '@': InsertCharacters(count); break;
            case 'P': DeleteCharacters(count); break;
            case 'X': EraseCharacters(count); break;
            case 'L': InsertLines(count); break;
            case 'M': DeleteLines(count); break;
            case 'S': ScrollUp(_scrollTop, _scrollBottom, count); break;
            case 'T': ScrollDown(_scrollTop, _scrollBottom, count); break;
            case 'h': SetMode(args, privateMode, true); break;
            case 'l': SetMode(args, privateMode, false); break;
            case 'n': DeviceStatus(args, privateMode); break;
            case 'c': ResponseRequested?.Invoke("\u001b[?1;2c"); break;
            case 't': WindowStatus(args); break;
        }

        if (command is not 'm')
        {
            _pendingWrap = false;
        }
    }

    private static int[] ParseArguments(string raw)
    {
        if (raw.Length == 0)
        {
            return [0];
        }

        return raw.Split(';').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
    }

    private static int Value(int[] args, int index, int defaultValue)
    {
        if (index >= args.Length || args[index] == 0)
        {
            return defaultValue;
        }
        return args[index];
    }

    private void ApplySgr(int[] args)
    {
        if (args.Length == 0)
        {
            args = [0];
        }

        for (var i = 0; i < args.Length; i++)
        {
            var code = args[i];
            switch (code)
            {
                case 0: _style = TerminalStyle.Default; break;
                case 1: _style = _style with { Bold = true }; break;
                case 4: _style = _style with { Underline = true }; break;
                case 7: _style = _style with { Inverse = true }; break;
                case 22: _style = _style with { Bold = false }; break;
                case 24: _style = _style with { Underline = false }; break;
                case 27: _style = _style with { Inverse = false }; break;
                case 39: _style = _style with { Foreground = TerminalColor.DefaultForeground }; break;
                case 49: _style = _style with { Background = TerminalColor.DefaultBackground }; break;
                default:
                    if (code is >= 30 and <= 37)
                        _style = _style with { Foreground = _ansi16[code - 30] };
                    else if (code is >= 90 and <= 97)
                        _style = _style with { Foreground = _ansi16[8 + code - 90] };
                    else if (code is >= 40 and <= 47)
                        _style = _style with { Background = _ansi16[code - 40] };
                    else if (code is >= 100 and <= 107)
                        _style = _style with { Background = _ansi16[8 + code - 100] };
                    else if ((code == 38 || code == 48) && i + 1 < args.Length)
                    {
                        var foreground = code == 38;
                        if (args[i + 1] == 2 && i + 4 < args.Length)
                        {
                            var color = new TerminalColor(
                                (byte)Math.Clamp(args[i + 2], 0, 255),
                                (byte)Math.Clamp(args[i + 3], 0, 255),
                                (byte)Math.Clamp(args[i + 4], 0, 255));
                            _style = foreground
                                ? _style with { Foreground = color }
                                : _style with { Background = color };
                            i += 4;
                        }
                        else if (args[i + 1] == 5 && i + 2 < args.Length)
                        {
                            var color = XtermColor(Math.Clamp(args[i + 2], 0, 255));
                            _style = foreground
                                ? _style with { Foreground = color }
                                : _style with { Background = color };
                            i += 2;
                        }
                    }
                    break;
            }
        }
    }

    private TerminalColor XtermColor(int index)
    {
        if (index < 16)
        {
            return _ansi16[index];
        }
        if (index >= 232)
        {
            var level = (byte)(8 + (index - 232) * 10);
            return new(level, level, level);
        }

        var cube = index - 16;
        var r = cube / 36;
        var g = (cube / 6) % 6;
        var b = cube % 6;
        static byte Level(int n) => (byte)(n == 0 ? 0 : 55 + n * 40);
        return new(Level(r), Level(g), Level(b));
    }

    private void SetMode(int[] args, bool privateMode, bool enabled)
    {
        if (!privateMode)
        {
            return;
        }

        foreach (var mode in args)
        {
            switch (mode)
            {
                case 7: AutoWrap = enabled; break;
                case 25: CursorVisible = enabled; break;
                case 1047:
                case 1049:
                    if (enabled) EnterAlternateScreen(); else ExitAlternateScreen();
                    break;
                case 2004: BracketedPaste = enabled; break;
            }
        }
    }

    private void DeviceStatus(int[] args, bool privateMode)
    {
        if (privateMode)
        {
            return;
        }
        if (Value(args, 0, 0) == 5)
        {
            ResponseRequested?.Invoke("\u001b[0n");
        }
        else if (Value(args, 0, 0) == 6)
        {
            ResponseRequested?.Invoke($"\u001b[{CursorRow + 1};{CursorColumn + 1}R");
        }
    }

    private void WindowStatus(int[] args)
    {
        if (Value(args, 0, 0) == 18)
        {
            ResponseRequested?.Invoke($"\u001b[8;{Rows};{Columns}t");
        }
    }

    private void SetScrollRegion(int[] args)
    {
        var top = Value(args, 0, 1) - 1;
        var bottom = Value(args, 1, Rows) - 1;
        if (top >= 0 && bottom < Rows && top < bottom)
        {
            _scrollTop = top;
            _scrollBottom = bottom;
            CursorRow = 0;
            CursorColumn = 0;
        }
    }

    private void EraseDisplay(int mode)
    {
        if (mode == 3)
        {
            ClearScrollback();
            return;
        }
        if (mode == 2)
        {
            ClearRows(0, Rows - 1);
            return;
        }
        if (mode == 1)
        {
            for (var row = 0; row < CursorRow; row++) ClearRow(row);
            for (var col = 0; col <= CursorColumn; col++) ClearCell(CursorRow, col);
            return;
        }
        for (var col = CursorColumn; col < Columns; col++) ClearCell(CursorRow, col);
        for (var row = CursorRow + 1; row < Rows; row++) ClearRow(row);
    }

    private void EraseLine(int mode)
    {
        var start = mode == 0 ? CursorColumn : 0;
        var end = mode == 1 ? CursorColumn : Columns - 1;
        for (var col = start; col <= end; col++) ClearCell(CursorRow, col);
    }

    private void InsertCharacters(int count)
    {
        count = Math.Min(count, Columns - CursorColumn);
        for (var col = Columns - 1; col >= CursorColumn + count; col--)
            _cells[CursorRow, col] = _cells[CursorRow, col - count];
        for (var col = CursorColumn; col < CursorColumn + count; col++) ClearCell(CursorRow, col);
    }

    private void DeleteCharacters(int count)
    {
        count = Math.Min(count, Columns - CursorColumn);
        for (var col = CursorColumn; col < Columns - count; col++)
            _cells[CursorRow, col] = _cells[CursorRow, col + count];
        for (var col = Columns - count; col < Columns; col++) ClearCell(CursorRow, col);
    }

    private void EraseCharacters(int count)
    {
        for (var col = CursorColumn; col < Math.Min(Columns, CursorColumn + count); col++)
            ClearCell(CursorRow, col);
    }

    private void InsertLines(int count)
    {
        if (CursorRow < _scrollTop || CursorRow > _scrollBottom) return;
        ScrollDown(CursorRow, _scrollBottom, count);
    }

    private void DeleteLines(int count)
    {
        if (CursorRow < _scrollTop || CursorRow > _scrollBottom) return;
        ScrollUp(CursorRow, _scrollBottom, count);
    }

    private void ScrollUp(int top, int bottom, int count)
    {
        count = Math.Clamp(count, 1, bottom - top + 1);
        if (!_alternateScreen && top == 0 && bottom == Rows - 1)
        {
            for (var row = top; row < top + count; row++)
            {
                AppendScrollback(row);
            }
        }

        for (var row = top; row <= bottom - count; row++)
            CopyRow(row + count, row);
        ClearRows(bottom - count + 1, bottom);
    }

    private void AppendScrollback(int row)
    {
        var line = new TerminalCell[Columns];
        for (var column = 0; column < Columns; column++)
        {
            line[column] = _cells[row, column];
        }

        ScrollbackSequence++;
        if (_scrollbackLimit > 0)
        {
            _scrollback.Add(line);
            TrimScrollback();
        }
    }

    private void TrimScrollback()
    {
        var excess = _scrollback.Count - _scrollbackLimit;
        if (excess > 0)
        {
            _scrollback.RemoveRange(0, excess);
        }
    }

    private void ScrollDown(int top, int bottom, int count)
    {
        count = Math.Clamp(count, 1, bottom - top + 1);
        for (var row = bottom; row >= top + count; row--)
            CopyRow(row - count, row);
        ClearRows(top, top + count - 1);
    }

    private void CopyRow(int source, int destination)
    {
        for (var col = 0; col < Columns; col++)
            _cells[destination, col] = _cells[source, col];
    }

    private void ClearRows(int start, int end)
    {
        for (var row = start; row <= end; row++) ClearRow(row);
    }

    private void ClearRow(int row)
    {
        for (var col = 0; col < Columns; col++) ClearCell(row, col);
    }

    private void ClearCell(int row, int column)
    {
        _cells[row, column] = new TerminalCell(' ', _style);
    }

    private void SaveCursor()
    {
        _savedRow = CursorRow;
        _savedColumn = CursorColumn;
    }

    private void RestoreCursor()
    {
        CursorRow = Math.Clamp(_savedRow, 0, Rows - 1);
        CursorColumn = Math.Clamp(_savedColumn, 0, Columns - 1);
    }

    private void EnterAlternateScreen()
    {
        if (_alternateScreen) return;
        _primaryCells = _cells;
        _primaryRow = CursorRow;
        _primaryColumn = CursorColumn;
        _cells = NewGrid(Rows, Columns, TerminalStyle.Default);
        CursorRow = CursorColumn = 0;
        _alternateScreen = true;
    }

    private void ExitAlternateScreen()
    {
        if (!_alternateScreen || _primaryCells is null) return;
        _cells = _primaryCells;
        _primaryCells = null;
        CursorRow = Math.Clamp(_primaryRow, 0, Rows - 1);
        CursorColumn = Math.Clamp(_primaryColumn, 0, Columns - 1);
        _alternateScreen = false;
    }

    private void ExecuteOsc()
    {
        var value = _sequence.ToString();
        _sequence.Clear();
        var separator = value.IndexOf(';');
        if (separator <= 0) return;
        var command = value[..separator];
        if (command is "0" or "2")
        {
            TitleChanged?.Invoke(value[(separator + 1)..]);
        }
    }

    private void Reset()
    {
        _style = TerminalStyle.Default;
        CursorRow = CursorColumn = 0;
        CursorVisible = true;
        AutoWrap = true;
        BracketedPaste = false;
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        _pendingWrap = false;
        _scrollback.Clear();
        _cells = NewGrid(Rows, Columns, TerminalStyle.Default);
    }

    private static TerminalCell[,] NewGrid(int rows, int columns, TerminalStyle style)
    {
        var grid = new TerminalCell[rows, columns];
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < columns; col++)
            grid[row, col] = new TerminalCell(' ', style);
        return grid;
    }

    private static TerminalCell[,] ResizeGrid(TerminalCell[,] old, int rows, int columns)
    {
        var resized = NewGrid(rows, columns, TerminalStyle.Default);
        var copyRows = Math.Min(rows, old.GetLength(0));
        var copyColumns = Math.Min(columns, old.GetLength(1));
        for (var row = 0; row < copyRows; row++)
        for (var col = 0; col < copyColumns; col++)
            resized[row, col] = old[row, col];
        return resized;
    }
}
