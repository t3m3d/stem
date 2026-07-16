using System.Diagnostics;
using System.Text;

namespace Stem.Windows;

public enum StemProfileKind
{
    Shell,
    Wsl,
    Ssh,
    Custom
}

public sealed record StemProfile(
    string Id,
    string Name,
    string CommandLine,
    string WorkingDirectory,
    StemProfileKind Kind,
    bool AutoDetected = false);

public static class StemProfileCatalog
{
    public static IReadOnlyList<StemProfile> Discover(StemSettings settings)
    {
        var profiles = new List<StemProfile>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var defaultCommand = ShellCommand.Resolve(settings.Shell);
        AddUnique(
            profiles,
            ids,
            new StemProfile(
                "default",
                ShellCommand.DisplayName(defaultCommand),
                defaultCommand,
                settings.WorkingDirectory,
                StemProfileKind.Shell));

        foreach (var configured in settings.Profiles)
        {
            AddUnique(profiles, ids, configured);
        }

        foreach (var distribution in DiscoverWslDistributions())
        {
            var id = "wsl-" + Slug(distribution);
            var command = Quote(WslExecutablePath()!) + " --distribution " + Quote(distribution);
            AddUnique(
                profiles,
                ids,
                new StemProfile(
                    id,
                    distribution + " (WSL)",
                    command,
                    settings.WorkingDirectory,
                    StemProfileKind.Wsl,
                    AutoDetected: true));
        }

        return profiles;
    }

    public static StemProfile Default(StemSettings settings, IReadOnlyList<StemProfile> profiles) =>
        profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.DefaultProfile, StringComparison.OrdinalIgnoreCase))
        ?? profiles.First();

    public static IReadOnlyList<string> ParseWslDistributionOutput(string output) =>
        output
            .Replace(((char)0).ToString(), string.Empty, StringComparison.Ordinal)
            .Split([(char)13, (char)10], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static string DecodeWslOutput(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var zeroBytes = bytes.Count(value => value == 0);
        return zeroBytes > bytes.Length / 8
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
    }

    private static IReadOnlyList<string> DiscoverWslDistributions()
    {
        var executable = WslExecutablePath();
        if (executable is null)
        {
            return [];
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--list");
            startInfo.ArgumentList.Add("--quiet");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            using var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            if (!process.WaitForExit(1500))
            {
                process.Kill(entireProcessTree: true);
                return [];
            }
            copy.GetAwaiter().GetResult();
            return ParseWslDistributionOutput(DecodeWslOutput(output.ToArray()));
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? WslExecutablePath()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, "System32", "wsl.exe");
        return File.Exists(path) ? path : null;
    }

    private static void AddUnique(
        ICollection<StemProfile> profiles,
        ISet<string> ids,
        StemProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) ||
            string.IsNullOrWhiteSpace(profile.CommandLine) ||
            !ids.Add(profile.Id))
        {
            return;
        }
        profiles.Add(profile);
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }
        return builder.ToString().Trim('-');
    }

    private static string Quote(string value)
    {
        if (!value.Contains(' ') && !value.Contains((char)9))
        {
            return value;
        }

        var quote = ((char)34).ToString();
        var escapedQuote = string.Concat((char)92, (char)34);
        return quote + value.Replace(quote, escapedQuote, StringComparison.Ordinal) + quote;
    }
}
