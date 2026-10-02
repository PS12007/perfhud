using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PerfHud.Core;

namespace PerfHud;

public partial class App : Application
{
    private const string MutexName = @"Local\PerfHud.SingleInstance";
    private const string ShowEventName = @"Local\PerfHud.ShowSettings";
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Start();

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask the existing instance to open its settings window, then exit.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Error("Unhandled exception", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Error("Unobserved task exception", ev.Exception); ev.SetObserved(); };

        // Keep animations cheap: 30 fps is plenty for fades and bar transitions on an overlay.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 30 });

        base.OnStartup(e);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PerfHud;component/UI/Theme.xaml") });

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _host?.OpenSettings());
        })
        { IsBackground = true, Name = "PerfHud.SingleInstance" };
        listener.Start();

        bool fromStartup = e.Args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        try
        {
            _host = new AppHost { DiagnosticLogging = e.Args.Contains("--diag") };
            _host.Start(fromStartup);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal startup error", ex);
            Log.Flush();
            MessageBox.Show($"PerfHud failed to start:\n\n{ex.Message}\n\nSee logs in {AppPaths.LogDir}", "PerfHud", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // The HUD must never crash because of a UI glitch: log and continue.
        Log.Error("UI exception (recovered)", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
