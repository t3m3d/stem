using System.Text;
using System.Windows;

namespace Stem.Windows;

internal static class CrashReporter
{
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config",
        "stem",
        "logs",
        "stem-crash.log");

    public static void Report(string stage, Exception exception, bool showDialog)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                var entry = new StringBuilder()
                    .AppendLine($"[{DateTimeOffset.Now:O}] {stage}")
                    .AppendLine(exception.ToString())
                    .AppendLine()
                    .ToString();
                File.AppendAllText(LogPath, entry, new UTF8Encoding(false));
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // A crash reporter must never replace the original exception.
        }

        if (showDialog)
        {
            MessageBox.Show(
                $"STEM encountered a fatal error.\n\nA diagnostic log was written to:\n{LogPath}",
                "STEM",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
