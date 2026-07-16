using System.Windows;
using System.Windows.Threading;

namespace Stem.Windows;

public partial class App : Application
{
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

        base.OnStartup(e);
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            CrashReporter.Report("Startup", exception, showDialog: true);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReporter.Report("Dispatcher", e.Exception, showDialog: true);
        e.Handled = true;
        Shutdown(1);
    }
}
