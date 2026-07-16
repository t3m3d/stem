using System.Text;
using System.Text.Json;

namespace Stem.Windows;

public sealed record StemPaneLayoutState
{
    public string Type { get; init; } = "pane";
    public string ProfileId { get; init; } = "default";
    public bool SideBySide { get; init; }
    public StemPaneLayoutState? First { get; init; }
    public StemPaneLayoutState? Second { get; init; }

    public static StemPaneLayoutState Pane(string profileId) =>
        new() { Type = "pane", ProfileId = profileId };

    public static StemPaneLayoutState Split(
        bool sideBySide,
        StemPaneLayoutState first,
        StemPaneLayoutState second) =>
        new()
        {
            Type = "split",
            SideBySide = sideBySide,
            First = first,
            Second = second
        };
}

public sealed record StemTabSessionState
{
    public int ActivePaneIndex { get; init; }
    public StemPaneLayoutState Root { get; init; } = StemPaneLayoutState.Pane("default");
}

public sealed record StemSessionState
{
    public int Version { get; init; } = 1;
    public int ActiveTabIndex { get; init; }
    public double WindowLeft { get; init; }
    public double WindowTop { get; init; }
    public double WindowWidth { get; init; }
    public double WindowHeight { get; init; }
    public bool Maximized { get; init; }
    public List<StemTabSessionState> Tabs { get; init; } = [];
}

public static class StemSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string SessionPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("STEM_SESSION");
            return !string.IsNullOrWhiteSpace(overridePath)
                ? Environment.ExpandEnvironmentVariables(overridePath)
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "stem",
                    "session.json");
        }
    }

    public static StemSessionState? Load(string? path = null)
    {
        path ??= SessionPath;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<StemSessionState>(
                File.ReadAllText(path, Encoding.UTF8),
                JsonOptions);
            return state is { Version: 1 } && state.Tabs.Count > 0
                ? state
                : null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static bool Save(StemSessionState state, string? path = null)
    {
        path ??= SessionPath;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(
                temporary,
                json + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
