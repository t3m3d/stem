using System.Globalization;

namespace Stem.Windows;

public enum StemCursorStyle
{
    Bar,
    Block,
    Underline
}

public enum StemBellMode
{
    Visual,
    Audible,
    Off
}

public sealed record StemSettings
{
    public const string DefaultFontFamily = "MesloLGS Nerd Font, CaskaydiaCove Nerd Font Mono, JetBrainsMono Nerd Font Mono, Cascadia Mono, Consolas";

    public const string DefaultConfigText = """
# STEM configuration — simple Linux-style key = value syntax.
# Canonical location: ~/.config/stem/stem.conf
# Set STEM_CONF to load another file. Restart for shell/session changes.

theme = krypton
title = STEM

# Shell/session. Leave shell blank for the platform default.
shell =
working_directory = ~
term = xterm-256color

# Initial grid.
cols = 117
rows = 30

# Text and spacing.
font_family = MesloLGS Nerd Font, CaskaydiaCove Nerd Font Mono, JetBrainsMono Nerd Font Mono, Cascadia Mono, Consolas
font_size = 13.5
padding = 10
line_spacing = 0

# Terminal history and interaction.
scrollback_lines = 10000
copy_on_select = false
confirm_close = false
bell = visual

# Cursor.
cursor_style = bar
cursor_blink_ms = 530
cursor_color = #8B5CF6

# Krypton palette.
background = #160A2A
foreground = #F4EEFF
selection_background = #3A2A60
accent = #8B5CF6

# ANSI 16-color palette.
color0 = #1E1E1E
color1 = #F14C4C
color2 = #23D18B
color3 = #F5F543
color4 = #3B8EEA
color5 = #D670D6
color6 = #29B8DB
color7 = #E5E5E5
color8 = #666666
color9 = #F14C4C
color10 = #23D18B
color11 = #F5F543
color12 = #3B8EEA
color13 = #D670D6
color14 = #29B8DB
color15 = #FFFFFF

# Background-only transparency. Text, cursor, and controls remain opaque.
# 1.0 is opaque; 0.2 is the minimum.
opacity = 1.0

# Split-pane focus and divider treatment.
unfocused_pane_opacity = 0.82
split_divider_color = #8B5CF6
focus_follows_mouse = false
restore_session = true

# GUI-managed terminal profiles. Dotted profile keys are portable.
default_profile = default

""";

    public static string DefaultWorkingDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string Shell { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = DefaultWorkingDirectory;
    public string Term { get; init; } = "xterm-256color";
    public string Theme { get; init; } = "krypton";
    public string WindowTitle { get; init; } = "STEM";
    public int Columns { get; init; } = 117;
    public int Rows { get; init; } = 30;
    public string FontFamily { get; init; } = DefaultFontFamily;
    public double FontSize { get; init; } = 13.5;
    public double Padding { get; init; } = 10;
    public double LineSpacing { get; init; }
    public double Opacity { get; init; } = 1;
    public double UnfocusedPaneOpacity { get; init; } = 0.82;
    public TerminalColor SplitDividerColor { get; init; } = new(139, 92, 246);
    public bool FocusFollowsMouse { get; init; }
    public int ScrollbackLines { get; init; } = 10_000;
    public int CursorBlinkMilliseconds { get; init; } = 530;
    public StemCursorStyle CursorStyle { get; init; } = StemCursorStyle.Bar;
    public StemBellMode Bell { get; init; } = StemBellMode.Visual;
    public TerminalColor BackgroundColor { get; init; } = new(22, 10, 42);
    public TerminalColor ForegroundColor { get; init; } = new(244, 238, 255);
    public TerminalColor CursorColor { get; init; } = new(139, 92, 246);
    public TerminalColor SelectionColor { get; init; } = new(58, 42, 96);
    public TerminalColor AccentColor { get; init; } = new(139, 92, 246);
    public TerminalColor[] AnsiPalette { get; init; } = DefaultAnsiPalette();
    public bool CopyOnSelect { get; init; }
    public bool ConfirmClose { get; init; }
    public bool RestoreSession { get; init; } = true;
    public string DefaultProfile { get; init; } = "default";
    public IReadOnlyList<StemProfile> Profiles { get; init; } = [];

    public static string CanonicalConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config",
        "stem",
        "stem.conf");

    public static string ConfigPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("STEM_CONF");
            return !string.IsNullOrWhiteSpace(overridePath)
                ? Environment.ExpandEnvironmentVariables(overridePath)
                : CanonicalConfigPath;
        }
    }

    public static StemSettings Load()
    {
        var path = ConfigPath;
        EnsureDefaultFile(path);
        return Load(path);
    }

    public static StemSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new StemSettings();
            }

            var values = Parse(File.ReadAllLines(path));
            var shell = Get(values, "shell", string.Empty, allowEmpty: true);
            if (shell.Contains("kryofetch", StringComparison.OrdinalIgnoreCase))
            {
                // Kryofetch is startup output, not an interactive shell. PowerShell profiles may still run it.
                shell = string.Empty;
            }
            var theme = Get(values, "theme", "krypton");
            var themePalette = KryptonTheme.TerminalPalette(theme);
            var scrollback = GetInt(
                values,
                "scrollback_lines",
                GetInt(values, "scrollback", 10_000, 0, 1_000_000),
                0,
                1_000_000);
            var background = GetColor(
                values,
                "background",
                GetColor(values, "bg", themePalette.Background));
            var foreground = GetColor(
                values,
                "foreground",
                GetColor(values, "fg", themePalette.Foreground));
            // Migrate the original neutral-black preview defaults to the real Krypton palette.
            if (background == new TerminalColor(5, 7, 12))
            {
                background = themePalette.Background;
            }
            if (foreground == new TerminalColor(216, 218, 212))
            {
                foreground = themePalette.Foreground;
            }
            var palette = themePalette.Ansi.ToArray();
            for (var index = 0; index < palette.Length; index++)
            {
                palette[index] = GetColor(values, $"color{index}", palette[index]);
            }

            var windowTitle = Get(values, "title", "STEM");
            if (string.Equals(windowTitle, "STEM - Krypton Terminal", StringComparison.OrdinalIgnoreCase))
            {
                windowTitle = "STEM";
            }

            return new StemSettings
            {
                Shell = shell,
                WorkingDirectory = Get(values, "working_directory", DefaultWorkingDirectory),
                Term = Get(values, "term", "xterm-256color"),
                Theme = theme,
                WindowTitle = windowTitle,
                Columns = GetInt(values, "cols", 117, 20, 400),
                Rows = GetInt(values, "rows", 30, 5, 200),
                FontFamily = Get(values, "font_family", Get(values, "font", DefaultFontFamily)),
                FontSize = GetDouble(values, "font_size", 13.5, 8, 40),
                Padding = GetDouble(values, "padding", 10, 0, 40),
                LineSpacing = GetDouble(values, "line_spacing", 0, -4, 20),
                Opacity = GetDouble(values, "opacity", 1, 0.2, 1),
                UnfocusedPaneOpacity = GetDouble(values, "unfocused_pane_opacity", 0.82, 0.15, 1),
                SplitDividerColor = GetColor(values, "split_divider_color", themePalette.SplitDivider),
                FocusFollowsMouse = GetBool(values, "focus_follows_mouse", false),
                ScrollbackLines = scrollback,
                CursorBlinkMilliseconds = GetInt(values, "cursor_blink_ms", 530, 0, 5_000),
                CursorStyle = GetCursorStyle(values, "cursor_style", StemCursorStyle.Bar),
                Bell = GetBellMode(values, "bell", StemBellMode.Visual),
                BackgroundColor = background,
                ForegroundColor = foreground,
                CursorColor = GetColor(values, "cursor_color", themePalette.Cursor),
                SelectionColor = GetColor(values, "selection_background", themePalette.Selection),
                AccentColor = GetColor(values, "accent", themePalette.Accent),
                AnsiPalette = palette,
                CopyOnSelect = GetBool(values, "copy_on_select", false),
                ConfirmClose = GetBool(values, "confirm_close", false),
                RestoreSession = GetBool(values, "restore_session", true),
                DefaultProfile = Get(values, "default_profile", "default"),
                Profiles = GetProfiles(values)
            };
        }
        catch (IOException)
        {
            return new StemSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new StemSettings();
        }
    }

    public static bool EnsureDefaultFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return false;
            }

            if (string.Equals(path, CanonicalConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var legacyPath in LegacyConfigPaths())
                {
                    if (!File.Exists(legacyPath))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.Copy(legacyPath, path, overwrite: false);
                    return true;
                }
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, DefaultConfigText + Environment.NewLine);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> LegacyConfigPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".config", "stem", "config");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "stem",
            "config");
    }

    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in lines)
        {
            var line = source.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            var comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0)
            {
                value = value[..comment].TrimEnd();
            }
            values[key] = value.Trim('"');
        }
        return values;
    }

    private static IReadOnlyList<StemProfile> GetProfiles(
        IReadOnlyDictionary<string, string> values)
    {
        const string prefix = "profile.";
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in values.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tail = key[prefix.Length..];
            var separator = tail.IndexOf('.');
            if (separator > 0)
            {
                ids.Add(tail[..separator]);
            }
        }

        var profiles = new List<StemProfile>();
        foreach (var id in ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var command = Get(values, $"profile.{id}.command", string.Empty, allowEmpty: true);
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var kind = Get(values, $"profile.{id}.kind", "custom").ToLowerInvariant() switch
            {
                "shell" => StemProfileKind.Shell,
                "wsl" => StemProfileKind.Wsl,
                "ssh" => StemProfileKind.Ssh,
                _ => StemProfileKind.Custom
            };
            profiles.Add(new StemProfile(
                id,
                Get(values, $"profile.{id}.name", id),
                command,
                Get(values, $"profile.{id}.working_directory", DefaultWorkingDirectory),
                kind));
        }
        return profiles;
    }

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback,
        bool allowEmpty = false)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return allowEmpty || !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static int GetInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback,
        int minimum,
        int maximum) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static double GetDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        double fallback,
        double minimum,
        double maximum) =>
        values.TryGetValue(key, out var value) &&
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static bool GetBool(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool fallback)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }

    private static TerminalColor GetColor(
        IReadOnlyDictionary<string, string> values,
        string key,
        TerminalColor fallback)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        var hex = value.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }
        else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        if (hex.Length != 6 ||
            !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return fallback;
        }

        return new TerminalColor(
            (byte)((rgb >> 16) & 0xff),
            (byte)((rgb >> 8) & 0xff),
            (byte)(rgb & 0xff));
    }

    public static TerminalColor[] DefaultAnsiPalette() =>
    [
        new(30, 30, 30), new(241, 76, 76), new(35, 209, 139), new(245, 245, 67),
        new(59, 142, 234), new(214, 112, 214), new(41, 184, 219), new(229, 229, 229),
        new(102, 102, 102), new(241, 76, 76), new(35, 209, 139), new(245, 245, 67),
        new(59, 142, 234), new(214, 112, 214), new(41, 184, 219), new(255, 255, 255)
    ];

    private static StemCursorStyle GetCursorStyle(
        IReadOnlyDictionary<string, string> values,
        string key,
        StemCursorStyle fallback) =>
        Get(values, key, string.Empty).Trim().ToLowerInvariant() switch
        {
            "bar" or "beam" => StemCursorStyle.Bar,
            "block" => StemCursorStyle.Block,
            "underline" or "line" => StemCursorStyle.Underline,
            _ => fallback
        };

    private static StemBellMode GetBellMode(
        IReadOnlyDictionary<string, string> values,
        string key,
        StemBellMode fallback) =>
        Get(values, key, string.Empty).Trim().ToLowerInvariant() switch
        {
            "visual" => StemBellMode.Visual,
            "audible" or "audio" => StemBellMode.Audible,
            "off" or "none" => StemBellMode.Off,
            _ => fallback
        };
}
