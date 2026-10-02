using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PerfHud.Hotkeys;
using PerfHud.Hud;
using PerfHud.Monitoring;
using PerfHud.Monitoring.Monitors;
using PerfHud.Notifications;
using PerfHud.Profiles;
using PerfHud.Sensors.Native;
using PerfHud.Settings;
using PerfHud.Storage;
using PerfHud.SystemTray;
using PerfHud.UI;

namespace PerfHud.Core;

/// <summary>Composition root: creates and wires every service, owns app lifetime.</summary>
public sealed class AppHost : IDisposable
{
    public static AppHost Instance { get; private set; } = null!;

    public SettingsService Settings { get; }
    public MetricStore Store { get; } = new();
    public SensorHub Hub { get; } = new();
    public MonitoringService Monitoring { get; }
    public FpsMonitor Fps { get; } = new();
    public SystemInfoMonitor SysInfo { get; } = new();
    public TemperatureMonitor Temps { get; } = new();
    public HudController Hud { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public TrayIcon Tray { get; private set; } = null!;
    public ProfileManager Profiles { get; private set; } = null!;
    public AlertEngine Alerts { get; private set; } = null!;
    public SessionRecorder Recorder { get; private set; } = null!;

    private MessageWindow _msg = null!;
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _hotkeyDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private SettingsWindow? _settingsWindow;
    private HudEditorWindow? _editor;
    private HistoryWindow? _history;
    private double _gameSeenSeconds;
    private double _autoMissingSeconds;
    private int _tickCount;
    private bool _startupDirty;

    public AppHost()
    {
        Instance = this;
        Settings = new SettingsService();
        var ctx = new MonitorContext { Store = Store, SettingsAccessor = () => Settings.Current, Sensors = Hub };
        Monitoring = new MonitoringService(ctx);
    }

    /// <summary>--diag: periodically logs per-monitor cost (for profiling overhead).</summary>
    public bool DiagnosticLogging { get; set; }

    public void Start(bool fromStartup)
    {
        var s = Settings.Current;
        Log.Info($"PerfHud {typeof(AppHost).Assembly.GetName().Version} starting (admin={Elevation.IsAdmin}, startup={fromStartup}, OS={Environment.OSVersion})");

        // ── Monitoring (each monitor isolated; order = first-run order) ──
        Monitoring.Add(new CpuMonitor());
        Monitoring.Add(new MemoryMonitor());
        Monitoring.Add(new GpuMonitor());
        Monitoring.Add(new BatteryMonitor());
        Monitoring.Add(new DiskMonitor());
        Monitoring.Add(new NetworkMonitor());
        Monitoring.Add(Fps);
        Monitoring.Add(new SelfMonitor(f => Monitoring.ThrottleFactor = f));
        Monitoring.Add(SysInfo);
        Monitoring.Add(Temps);
        Monitoring.Add(new LatencyMonitor());
        Monitoring.Start();

        // ── UI services ──
        _msg = new MessageWindow();
        Hotkeys = new HotkeyManager(_msg);
        Hotkeys.Pressed += OnHotkey;
        Hotkeys.Apply(s.Hotkeys);

        Hud = new HudController(Settings, Store, Monitoring, Hub, Fps);
        Hud.Start(showOnStartup: s.General.ShowHudOnStartup || !fromStartup);

        Tray = new TrayIcon(_msg) { MenuFactory = BuildTrayMenu };
        Tray.DoubleClicked += () => OpenSettings();

        Profiles = new ProfileManager(Settings);
        Profiles.Changed += OnProfileChanged;
        if (Profiles.Current.Profile != null || Profiles.Current.Desktop) Hud.ApplyProfile(Profiles.Current);

        Alerts = new AlertEngine(Store, () => Settings.Current);
        Alerts.Fired += OnAlert;

        Recorder = new SessionRecorder(Store, () => Settings.Current);
        Recorder.RecordingChanged += on => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Fps.FrameSink = on ? Recorder.FrameSink : null;
            Hud.SetRecording(on);
        });
        Recorder.SessionSaved += sum => Application.Current.Dispatcher.BeginInvoke(() =>
            Hud.ShowInfo($"Session saved · {sum.DurationText}{(sum.AvgFps is double f ? $" · avg {f:0} FPS" : "")}"));

        // ── System events: sleep/wake, AC changes, displays, network ──
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Hub.PowerSourceChanged += onBattery => Application.Current.Dispatcher.BeginInvoke(() => UpdatePowerSaving(onBattery));
        Settings.Changed += OnSettingsChanged;
        _hotkeyDebounce.Tick += (_, _) => { _hotkeyDebounce.Stop(); Hotkeys.Apply(Settings.Current.Hotkeys); };

        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        SessionStore.Prune(s.History.RetentionDays);
        SyncStartupRegistration(showErrors: false);

        if (!s.General.FirstRunDone)
        {
            s.General.FirstRunDone = true;
            OpenSettings("Welcome");
        }
        else if (!fromStartup && !s.General.StartMinimized) OpenSettings();
        else if (!fromStartup) Tray.ShowBalloon("PerfHud is running", $"Toggle the HUD with {s.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.ToggleHud)?.Gesture}. Right-click the tray icon for options.");
    }

    // ── Events ───────────────────────────────────────────

    private void OnSettingsChanged(object sender, string? prop)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (sender is HotkeyBinding || sender is ObservableList<HotkeyBinding> || sender is AppSettings) { _hotkeyDebounce.Stop(); _hotkeyDebounce.Start(); }
            if (sender is GeneralSettings && prop is nameof(GeneralSettings.StartWithWindows) or nameof(GeneralSettings.StartElevated)) _startupDirty = true;
            if (sender is BatterySettings || sender is PerformanceSettings) { UpdatePowerSaving(Hub.OnBattery); Monitoring.Kick(); }
            if (sender is SensorSettings) Monitoring.RequestResetAll("sensor settings changed");
            if (sender is ProfileSettings || sender is AppProfile || sender is ObservableList<AppProfile>) Profiles.Evaluate();
        });
    }

    private void UpdatePowerSaving(bool onBattery)
    {
        bool saving = onBattery && Settings.Current.Battery.OptimizationEnabled;
        Monitoring.Context.OnBattery = onBattery;
        if (Monitoring.Context.PowerSaving == saving) return;
        Monitoring.Context.PowerSaving = saving;
        Log.Info(saving ? "On battery: battery optimization active" : "Battery optimization inactive");
        Hud.RequestRebuild();
        Monitoring.Kick();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Log.Info("System resumed from sleep");
            Monitoring.RequestResetAll("resume from sleep");
            Application.Current.Dispatcher.BeginInvoke(() => { Hud.OnDisplayChanged(); Profiles.Evaluate(); });
        }
        else if (e.Mode == PowerModes.StatusChange)
        {
            // AC plugged/unplugged or battery state changed: refresh now instead of waiting for the next poll.
            Monitoring.Kick<BatteryMonitor>();
        }
        else if (e.Mode == PowerModes.Suspend)
        {
            Log.Info("System suspending");
            if (Recorder.IsRecording) Recorder.Stop();
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        Log.Info("Display configuration changed");
        SysInfo.MarkDisplayChanged();
        Hub.InvalidateAdapters();
        Monitoring.RequestReset<GpuMonitor>();
        Hud.OnDisplayChanged();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect) Hud.OnDisplayChanged();
    }

    private void OnProfileChanged(ProfileMatch m)
    {
        Hud.ApplyProfile(m);
        if (m.Profile?.AutoRecord == true && !Recorder.IsRecording)
            Recorder.Start($"{Path.GetFileNameWithoutExtension(m.Profile.ExeName)} session", m.Profile.ExeName, "Profile");
        else if (m.Profile == null && Recorder.IsRecording && Recorder.Trigger == "Profile")
            Recorder.Stop();
    }

    private void OnAlert(AlertEvent e)
    {
        Hud.ShowAlert(e);
        if (e.Rule.Actions.HasFlag(AlertActions.Notification)) ToastWindow.Show(e.Title, e.Message, e.Severity, Hud.CurrentStyle);
    }

    private void OnTick()
    {
        _tickCount++;
        if (!Monitoring.IsPaused) Alerts.Evaluate(Hub.OnBattery);
        if (_startupDirty) { _startupDirty = false; SyncStartupRegistration(showErrors: true); }
        if (DiagnosticLogging && _tickCount % 10 == 0)
            Log.Info("diag: " + string.Join(" | ", Monitoring.Statuses.Select(m => $"{m.Name} {m.LastDurationMs:0}ms")) + $" | app.cpu {Store.Get("app.cpu"):0.00}%");
        if (_tickCount % 2 == 0)
        {
            UpdateTooltip();
            AutoRecord(2);
        }
    }

    private void UpdateTooltip()
    {
        string P(string id, string unit = "%") => Store.Has(id) ? $"{Store.Get(id):0}{unit}" : "N/A";
        var tip = $"PerfHud · CPU {P("cpu.usage")} · GPU {P("gpu.usage")}";
        if (Store.Has("bat.pct")) tip += $" · BAT {P("bat.pct")}";
        if (Store.Has("fps.current")) tip += $" · {Store.Get("fps.current"):0} FPS";
        if (Monitoring.IsPaused) tip += " · Paused";
        if (Recorder.IsRecording) tip += " · Recording";
        Tray.SetTooltip(tip);
    }

    /// <summary>Starts a session when a game is detected (fullscreen app rendering ≥15 FPS for 20 s), stops 30 s after it goes away.</summary>
    private void AutoRecord(double dt)
    {
        var mode = Settings.Current.History.AutoRecord;
        bool gameActive = Fps.TargetPid != 0 && Store.Get("fps.current") >= 15 && IsForegroundFullscreen();

        if (Recorder.IsRecording && Recorder.Trigger == "Auto")
        {
            bool sameApp = Fps.TargetName != null && Fps.TargetName == Recorder.Current?.App;
            _autoMissingSeconds = sameApp && Store.Get("fps.current") > 0 ? 0 : _autoMissingSeconds + dt;
            if (_autoMissingSeconds >= 30 || mode != AutoRecordMode.GamesDetected) { Recorder.Stop(); _autoMissingSeconds = 0; }
            return;
        }
        if (mode != AutoRecordMode.GamesDetected || Recorder.IsRecording || Monitoring.IsPaused) { _gameSeenSeconds = 0; return; }
        _gameSeenSeconds = gameActive ? _gameSeenSeconds + dt : 0;
        if (_gameSeenSeconds >= 20 && Fps.TargetName != null)
        {
            _gameSeenSeconds = 0;
            _autoMissingSeconds = 0;
            Recorder.Start($"{Path.GetFileNameWithoutExtension(Fps.TargetName)} session", Fps.TargetName, "Auto");
        }
    }

    private static bool IsForegroundFullscreen()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero || !Win32.GetWindowRect(fg, out var r)) return false;
        var mon = Win32.GetMonitor(Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST));
        if (mon == null) return false;
        return r.Left <= mon.Bounds.Left + 2 && r.Top <= mon.Bounds.Top + 2 && r.Right >= mon.Bounds.Right - 2 && r.Bottom >= mon.Bounds.Bottom - 2;
    }

    public void SyncStartupRegistration(bool showErrors)
    {
        var g = Settings.Current.General;
        bool want = g.StartWithWindows;
        bool haveRun = StartupManager.IsRunKeySet();
        if (!want && !haveRun && !g.StartElevated) return;
        if (want && haveRun && !g.StartElevated) return;
        var err = StartupManager.Apply(want, g.StartElevated);
        if (err != null && showErrors) Tray.ShowBalloon("PerfHud startup", err);
    }

    // ── Hotkeys ──────────────────────────────────────────

    public void OnHotkey(HotkeyAction a)
    {
        switch (a)
        {
            case HotkeyAction.ToggleHud: Hud.Toggle(); break;
            case HotkeyAction.CyclePreset: Hud.CyclePreset(); break;
            case HotkeyAction.ToggleCompact: Hud.ToggleCompact(); break;
            case HotkeyAction.ToggleGraphs: Hud.ToggleGraphs(); break;
            case HotkeyAction.ToggleClickThrough: Hud.ToggleClickThrough(); break;
            case HotkeyAction.ToggleSettings:
                if (_settingsWindow is { IsVisible: true }) _settingsWindow.Close(); else OpenSettings();
                break;
            case HotkeyAction.TogglePause: Monitoring.SetPaused(!Monitoring.IsPaused); break;
            case HotkeyAction.ScreenshotHud: Hud.Screenshot(); break;
            case HotkeyAction.ResetPosition: Hud.ResetPosition(); break;
            case HotkeyAction.ToggleRecording: ToggleRecording(); break;
            case HotkeyAction.OpenEditor: OpenEditor(); break;
        }
    }

    public void ToggleRecording()
    {
        if (Recorder.IsRecording) Recorder.Stop();
        else
        {
            var app = Fps.TargetName;
            Recorder.Start(app != null ? $"{Path.GetFileNameWithoutExtension(app)} session" : "Manual session", app, "Manual");
        }
    }

    // ── Windows ──────────────────────────────────────────

    public void OpenSettings(string? page = null)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (page != null) _settingsWindow.Navigate(page);
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public void OpenEditor(string? layoutName = null)
    {
        if (_editor == null)
        {
            _editor = new HudEditorWindow(this);
            _editor.Closed += (_, _) => _editor = null;
        }
        if (layoutName != null) _editor.LoadLayout(layoutName);
        _editor.Show();
        _editor.Activate();
    }

    public void OpenHistory()
    {
        if (_history == null)
        {
            _history = new HistoryWindow(this);
            _history.Closed += (_, _) => _history = null;
        }
        _history.Show();
        _history.Activate();
    }

    // ── Tray menu ────────────────────────────────────────

    private ContextMenu BuildTrayMenu()
    {
        var s = Settings.Current;
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click, bool? check = null, string? gesture = null)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
            if (check.HasValue) { mi.IsCheckable = true; mi.IsChecked = check.Value; }
            mi.Click += (_, _) => click();
            return mi;
        }
        string? G(HotkeyAction a) => s.Hotkeys.FirstOrDefault(h => h.Action == a)?.Gesture;

        menu.Items.Add(new MenuItem { Header = "Performance HUD", IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("HUD Enabled", Hud.Toggle, Hud.IsHudVisible, G(HotkeyAction.ToggleHud)));

        var preset = new MenuItem { Header = "Preset" };
        foreach (var name in HudPresets.AllNames(s))
            preset.Items.Add(Item(name, () => Hud.SetPreset(name), Hud.EffectivePreset == name));
        menu.Items.Add(preset);

        var hud = new MenuItem { Header = "HUD" };
        hud.Items.Add(Item("Show", () => Hud.SetVisible(true)));
        hud.Items.Add(Item("Hide", () => Hud.SetVisible(false)));
        hud.Items.Add(new Separator());
        hud.Items.Add(Item("Compact mode", Hud.ToggleCompact, s.Hud.Compact, G(HotkeyAction.ToggleCompact)));
        hud.Items.Add(Item("Show graphs", Hud.ToggleGraphs, s.Hud.ShowGraphs, G(HotkeyAction.ToggleGraphs)));
        hud.Items.Add(Item("Locked (click-through)", Hud.ToggleClickThrough, s.Hud.ClickThrough, G(HotkeyAction.ToggleClickThrough)));
        hud.Items.Add(Item("Reset position", Hud.ResetPosition, null, G(HotkeyAction.ResetPosition)));
        hud.Items.Add(Item("Screenshot HUD", () => Hud.Screenshot(), null, G(HotkeyAction.ScreenshotHud)));
        menu.Items.Add(hud);

        var rec = new MenuItem { Header = "Recording" };
        rec.Items.Add(Item(Recorder.IsRecording ? "Stop session" : "Start session", ToggleRecording, null, G(HotkeyAction.ToggleRecording)));
        rec.Items.Add(Item("Session history…", OpenHistory));
        menu.Items.Add(rec);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", () => OpenSettings(), null, G(HotkeyAction.ToggleSettings)));
        menu.Items.Add(Item("HUD Editor…", () => OpenEditor(), null, G(HotkeyAction.OpenEditor)));
        menu.Items.Add(Item("Start with Windows", () => s.General.StartWithWindows = !s.General.StartWithWindows, s.General.StartWithWindows));
        menu.Items.Add(Item("Pause Monitoring", () => Monitoring.SetPaused(!Monitoring.IsPaused), Monitoring.IsPaused, G(HotkeyAction.TogglePause)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", () => Application.Current.Shutdown()));
        return menu;
    }

    public void Dispose()
    {
        try { _tick.Stop(); } catch { }
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        try { Recorder?.Stop(); } catch { }
        try { Profiles?.Dispose(); } catch { }
        try { Hotkeys?.Dispose(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { Hud?.Dispose(); } catch { }
        try { Monitoring.Dispose(); } catch { }
        try { _msg?.Dispose(); } catch { }
        Settings.Dispose();
        Log.Info("PerfHud stopped");
        Log.Flush();
    }
}
