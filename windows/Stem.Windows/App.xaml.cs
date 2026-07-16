using System.Windows;
using System.Windows.Threading;

namespace Stem.Windows;

public partial class App : Application
{
    private bool _startupSmoke;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                CrashReporter.Report("AppDomain", exception, showDialog: false);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashReporter.Report("TaskScheduler", args.Exception, showDialog: false);
            args.SetObserved();
        };

        _startupSmoke = e.Args.Any(argument =>
            string.Equals(argument, "--startup-smoke", StringComparison.OrdinalIgnoreCase));

        base.OnStartup(e);
        try
        {
            MainWindow = new MainWindow();
            if (_startupSmoke)
            {
                MainWindow.ApplyTemplate();
                MainWindow.Measure(new Size(1120, 740));
                MainWindow.Arrange(new Rect(0, 0, 1120, 740));
                MainWindow.UpdateLayout();
                Environment.ExitCode = 0;
                Shutdown(0);
                return;
            }

            MainWindow.Show();
        }
        catch (Exception exception)
        {
            if (_startupSmoke)
            {
                WriteStartupSmokeFailure(exception);
                Environment.ExitCode = 1;
                Shutdown(1);
                return;
            }

            CrashReporter.Report("Startup", exception, showDialog: true);
            Shutdown(1);
        }
    }

    private static void WriteStartupSmokeFailure(Exception exception)
    {
        var path = Environment.GetEnvironmentVariable("STEM_STARTUP_SMOKE_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, exception.ToString());
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
            // The process exit code remains the authoritative smoke-test result.
        }
    }
    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        if (_startupSmoke)
        {
            WriteStartupSmokeFailure(e.Exception);
            Environment.ExitCode = 1;
            e.Handled = true;
            Shutdown(1);
            return;
        }

        CrashReporter.Report("Dispatcher", e.Exception, showDialog: true);
        e.Handled = true;
        Shutdown(1);
    }
}
