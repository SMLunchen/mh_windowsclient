using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Crash visibility. Bug report 2026-11-15: the process died during shutdown and the log simply
        // ended — no handler, no trace. Every unhandled exception now lands in the log file first.
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Logger.WriteException($"Unhandled exception (terminating={a.IsTerminating})", a.ExceptionObject as Exception);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Logger.WriteLine($"[WARN] Unobserved task exception: {a.Exception?.Flatten().InnerException ?? a.Exception}");
            a.SetObserved();
        };

        // Show tooltips faster (system default is ~400ms; 150ms feels much more responsive)
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(UIElement),
            new FrameworkPropertyMetadata(150));
        // Keep tooltips visible longer (default ~5s is too short for multi-line tips)
        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(UIElement),
            new FrameworkPropertyMetadata(20000));
    }

    private static int _dispatcherFaults;

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs a)
    {
        Logger.WriteException("UI thread exception", a.Exception);
        // One stray UI exception should not take the whole session (and the device link) down with it.
        // If they keep coming the window is broken anyway — then let the process end with a trace.
        if (System.Threading.Interlocked.Increment(ref _dispatcherFaults) <= 5)
            a.Handled = true;
    }
}
