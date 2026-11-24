using System.Windows;
using System.Windows.Threading;

namespace MeshhessenClient.Tests;

/// <summary>
/// One long-lived STA thread with a running dispatcher and the WPF <see cref="Application"/> for all window tests.
/// <c>Application.Current</c> is process-wide but owned by the thread that created it; a second test creating a
/// window on its own STA thread would hit "the calling thread cannot access this object" as soon as a resource
/// lookup reaches the application dictionaries. So every window test runs its body on this thread instead.
/// </summary>
internal static class WpfTestApp
{
    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? startError = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                    _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MeshhessenClient;component/Resources/Strings.de.xaml")
                });
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                startError = ex;
            }
            finally
            {
                ready.Set();
            }
            if (startError == null) Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WpfTestApp",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (startError != null) throw new InvalidOperationException("WPF test application failed to start", startError);
        return dispatcher!;
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread; exceptions propagate to the caller.</summary>
    public static void Run(Action body) => Ui.Value.Invoke(body);
}
