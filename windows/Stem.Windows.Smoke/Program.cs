using Stem.Windows;
using System.Text;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void TestTerminalBuffer()
{
    var terminal = new TerminalBuffer(4, 10);
    terminal.Feed("abc");
    Assert(terminal.CellAt(0, 0).Character == 'a', "plain text cell 0");
    Assert(terminal.CellAt(0, 2).Character == 'c', "plain text cell 2");

    terminal.Feed("\u001b[2;4HZ");
    Assert(terminal.CellAt(1, 3).Character == 'Z', "CUP cursor positioning");

    terminal.Feed("\u001b[31;1mR");
    var red = terminal.CellAt(1, 4);
    Assert(red.Character == 'R', "SGR glyph");
    Assert(red.Style.Foreground == new TerminalColor(241, 76, 76), "SGR red");
    Assert(red.Style.Bold, "SGR bold");

    var title = string.Empty;
    terminal.TitleChanged += value => title = value;
    terminal.Feed("\u001b]0;stem smoke\a");
    Assert(title == "stem smoke", "OSC window title");

    terminal.Feed("\u001b[?1049hALT");
    Assert(terminal.CellAt(0, 0).Character == 'A', "alternate screen content");
    terminal.Feed("\u001b[?1049l");
    Assert(terminal.CellAt(0, 0).Character == 'a', "primary screen restored");

    terminal.Resize(6, 12);
    Assert(terminal.Rows == 6 && terminal.Columns == 12, "grid resize");

    var unicode = new TerminalBuffer(3, 12);
    unicode.Feed("A\U000F0954B");
    Assert(unicode.CellAt(0, 1).Text == "\U000F0954", "supplementary Nerd Font glyph stays in one cell");
    Assert(unicode.CellAt(0, 2).Character == 'B', "supplementary glyph advances one cell");
    unicode.Feed("\r\n界Z");
    Assert(unicode.CellAt(1, 0).Text == "界", "wide glyph lead cell");
    Assert(unicode.CellAt(1, 1).Continuation, "wide glyph continuation cell");
    Assert(unicode.CellAt(1, 2).Character == 'Z', "wide glyph advances two cells");
    unicode.Feed("\r\ne\u0301");
    Assert(unicode.CellAt(2, 0).Text == "e\u0301", "combining mark joins its base cell");

    var promptEdge = new TerminalBuffer(2, 10);
    promptEdge.Feed("12345678\U000F0954X");
    Assert(promptEdge.CellAt(0, 9).Character == 'X', "Nerd Font icon does not wrap a right-aligned prompt");
    Assert(promptEdge.CursorRow == 0, "right-aligned prompt remains on its intended row");

    var history = new TerminalBuffer(3, 5) { ScrollbackLimit = 2 };
    history.Feed("one\r\ntwo\r\nthree\r\nfour");
    Assert(history.ScrollbackCount == 1, "full-screen scroll enters history");
    Assert(history.ViewLineText(0, 1, trimEnd: true) == "one", "history viewport top line");
    Assert(history.ViewLineText(0, 0, trimEnd: true) == "two", "live viewport remains current");

    history.Feed("\r\nfive\r\nsix");
    Assert(history.ScrollbackCount == 2, "scrollback limit is enforced");
    Assert(history.ViewLineText(0, 2, trimEnd: true) == "two", "oldest retained history line");
    history.Feed("\u001b[3J");
    Assert(history.ScrollbackCount == 0, "CSI 3J clears saved history");

    history.Feed("\u001b[?1049hA\r\nB\r\nC\r\nD");
    Assert(history.ScrollbackCount == 0, "alternate screen never pollutes scrollback");
    history.Feed("\u001b[?1049l");

    var searchable = new TerminalBuffer(3, 14);
    searchable.Feed("Alpha alpha\r\nBeta 界界\r\nGamma\r\nDelta");
    Assert(searchable.ScrollbackCount == 1, "search fixture includes scrollback");
    Assert(searchable.DocumentLineText(0, trimEnd: true) == "Alpha alpha", "document text exposes history");
    var alphaMatches = searchable.FindText("ALPHA");
    Assert(alphaMatches.Count == 2, "search is case-insensitive and finds repeated text");
    Assert(alphaMatches[0] == new TerminalTextMatch(0, 0, 4), "first search match coordinates");
    Assert(alphaMatches[1] == new TerminalTextMatch(0, 6, 10), "second search match coordinates");
    var wideMatch = searchable.FindText("界界");
    Assert(wideMatch.Count == 1, "search finds Unicode text");
    Assert(wideMatch[0] == new TerminalTextMatch(1, 5, 8), "wide-character match spans continuation cells");
    Assert(searchable.FindText("missing").Count == 0, "missing search returns no matches");
}

static void TestProfiles()
{
    var output = string.Concat(
        "Ubuntu", (char)0, (char)13, (char)10,
        "Debian", (char)0, (char)13, (char)10,
        "Ubuntu", (char)0);
    var distributions = StemProfileCatalog.ParseWslDistributionOutput(output);
    Assert(distributions.SequenceEqual(["Ubuntu", "Debian"]), "WSL distribution output is normalized");
}

static void TestSettings()
{
    var path = Path.Combine(Path.GetTempPath(), $"stem-settings-{Guid.NewGuid():N}.conf");
    try
    {
        File.WriteAllText(path, """
            shell = pwsh.exe -NoLogo
            working_directory = C:\work
            term = screen-256color
            theme = krypton
            title = Configured STEM
            cols = 144
            rows = 42
            font_family = Cascadia Mono
            font_size = 15.5
            padding = 12
            line_spacing = 2
            opacity = 0.72
            default_profile = ssh-prod
            profile.ssh-prod.name = Production SSH
            profile.ssh-prod.kind = ssh
            profile.ssh-prod.command = ssh ops@example.com
            profile.ssh-prod.working_directory = C:\work
            scrollback_lines = 4321
            cursor_blink_ms = 0
            cursor_style = block
            bell = audible
            background = #112233
            foreground = #DDEEFF
            cursor_color = #8B5CF6
            selection_background = #302050
            accent = #9A6CFF
            color1 = #AA1122
            copy_on_select = true
            confirm_close = yes
            """);
        var settings = StemSettings.Load(path);
        Assert(settings.Shell == "pwsh.exe -NoLogo", "configured shell");
        Assert(settings.WorkingDirectory == @"C:\work", "configured working directory");
        Assert(settings.Term == "screen-256color", "configured TERM");
        Assert(settings.Theme == "krypton", "configured theme");
        Assert(settings.WindowTitle == "Configured STEM", "configured title");
        Assert(settings.Columns == 144 && settings.Rows == 42, "configured grid");
        Assert(settings.FontFamily == "Cascadia Mono", "configured font");
        Assert(Math.Abs(settings.FontSize - 15.5) < 0.01, "configured font size");
        Assert(settings.Padding == 12, "configured padding");
        Assert(settings.LineSpacing == 2, "configured line spacing");
        Assert(Math.Abs(settings.Opacity - 0.72) < 0.001, "configured transparency");
        Assert(settings.ScrollbackLines == 4321, "configured scrollback");
        Assert(settings.CursorBlinkMilliseconds == 0, "configured steady cursor");
        Assert(settings.CursorStyle == StemCursorStyle.Block, "configured cursor shape");
        Assert(settings.Bell == StemBellMode.Audible, "configured bell");
        Assert(settings.BackgroundColor == new TerminalColor(0x11, 0x22, 0x33), "configured background");
        Assert(settings.ForegroundColor == new TerminalColor(0xDD, 0xEE, 0xFF), "configured foreground");
        Assert(settings.CursorColor == new TerminalColor(0x8B, 0x5C, 0xF6), "configured cursor color");
        Assert(settings.SelectionColor == new TerminalColor(0x30, 0x20, 0x50), "configured selection color");
        Assert(settings.AccentColor == new TerminalColor(0x9A, 0x6C, 0xFF), "configured accent");
        Assert(settings.AnsiPalette[1] == new TerminalColor(0xAA, 0x11, 0x22), "configured ANSI palette");
        Assert(settings.CopyOnSelect, "configured copy-on-select");
        Assert(settings.ConfirmClose, "configured close confirmation");
        Assert(settings.DefaultProfile == "ssh-prod", "configured default profile");
        Assert(settings.Profiles.Count == 1, "configured profile count");
        Assert(settings.Profiles[0].Name == "Production SSH" && settings.Profiles[0].Kind == StemProfileKind.Ssh, "configured terminal profile");
        Assert(settings.Profiles[0].CommandLine == "ssh ops@example.com", "configured profile command");

        File.WriteAllText(path, "title = STEM - Krypton Terminal");
        Assert(StemSettings.Load(path).WindowTitle == "STEM", "legacy default title is cleaned up");
    }
    finally
    {
        File.Delete(path);
    }

    var generatedDirectory = Path.Combine(Path.GetTempPath(), $"stem-generated-{Guid.NewGuid():N}");
    var generatedPath = Path.Combine(generatedDirectory, "stem.conf");
    try
    {
        Assert(StemSettings.EnsureDefaultFile(generatedPath), "default stem.conf is created");
        var generated = File.ReadAllText(generatedPath);
        Assert(generated.Contains("opacity = 1.0", StringComparison.Ordinal), "generated config documents transparency");
        Assert(generated.Contains("color15 = #FFFFFF", StringComparison.Ordinal), "generated config documents ANSI colors");
    }
    finally
    {
        File.Delete(generatedPath);
        if (Directory.Exists(generatedDirectory)) Directory.Delete(generatedDirectory);
    }
}

static async Task TestConPtyAsync()
{
    var configuredShell = Environment.GetEnvironmentVariable("STEM_SHELL");
    string shell;
    try
    {
        Environment.SetEnvironmentVariable("STEM_SHELL", null);
        shell = ShellCommand.Resolve();
    }
    finally
    {
        Environment.SetEnvironmentVariable("STEM_SHELL", configuredShell);
    }
    Assert(ShellCommand.DisplayName(shell).Contains("PowerShell", StringComparison.Ordinal), "PowerShell is the default Windows shell");
    using var session = ConPtySession.Start(shell, 80, 25);
    var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var output = new StringBuilder();
    session.OutputReceived += bytes =>
    {
        lock (output)
        {
            output.Append(Encoding.UTF8.GetString(bytes));
            if (output.ToString().Contains("STEM_CONPTY_OK", StringComparison.Ordinal))
            {
                marker.TrySetResult();
            }
        }
    };
    session.StartReading();

    await session.WriteAsync(Encoding.UTF8.GetBytes("Write-Output ('STEM_TERM=' + $env:TERM); if ([string]::IsNullOrEmpty($env:NO_COLOR) -and $env:COLORTERM -eq 'truecolor') { Write-Output ('STEM_COLOR_' + 'OK') }; Write-Output ('STEM_CWD=' + $PWD.Path); Write-Output ('STEM_' + 'CONPTY_OK')\r\nexit\r\n"));
    var completed = await Task.WhenAny(marker.Task, Task.Delay(TimeSpan.FromSeconds(10)));
    if (completed != marker.Task)
    {
        string captured;
        lock (output) captured = output.ToString();
        throw new TimeoutException("ConPTY marker was not received. Output: " + captured);
    }

    string finalOutput;
    lock (output) finalOutput = output.ToString();
    Assert(finalOutput.Contains("STEM_TERM=xterm-256color", StringComparison.Ordinal),
        "ConPTY advertises xterm-256color for Starship and terminal-aware tools. Output: " + finalOutput);
    Assert(finalOutput.Contains("STEM_COLOR_OK", StringComparison.Ordinal),
        "ConPTY enables truecolor and does not leak NO_COLOR into STEM. Output: " + finalOutput);
    var expectedHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    Assert(finalOutput.Contains("STEM_CWD=" + expectedHome, StringComparison.OrdinalIgnoreCase),
        "ConPTY starts PowerShell in the user home directory. Output: " + finalOutput);
}

TestTerminalBuffer();
TestProfiles();
TestSettings();
await TestConPtyAsync();
Console.WriteLine("PASS: VT grid + PowerShell ConPTY round trip");
