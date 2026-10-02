using PerfHud.Core;
using PerfHud.Hud;

namespace PerfHud.Settings;

/// <summary>Root of everything persisted to %APPDATA%\PerfHud\settings.json.</summary>
public sealed class AppSettings : Observable
{
    public int Version { get; set; } = 1;
    public GeneralSettings General { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public HudSettings Hud { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public BatterySettings Battery { get; set; } = new();
    public ThresholdSettings Thresholds { get; set; } = new();
    public SensorSettings Sensors { get; set; } = new();
    public ProfileSettings Profiles { get; set; } = new();
    public HistorySettings History { get; set; } = new();
    public PrivacySettings Privacy { get; set; } = new();
    public ObservableList<HotkeyBinding> Hotkeys { get; set; } = new();
    public ObservableList<AlertRule> Alerts { get; set; } = new();

    /// <summary>User-editable layouts. Built-in presets (Minimal/Gaming/Full) are generated in code and not stored.</summary>
    public ObservableList<HudLayout> Layouts { get; set; } = new();

    /// <summary>Fills in anything missing after loading an older/partial file.</summary>
    public void Normalize()
    {
        foreach (HotkeyAction a in Enum.GetValues<HotkeyAction>())
            if (Hotkeys.All(h => h.Action != a))
                Hotkeys.Add(new HotkeyBinding { Action = a, Gesture = DefaultHotkeys.For(a) });

        if (Layouts.Count == 0 || Layouts.All(l => l.Name != HudPresets.CustomName))
            Layouts.Insert(0, HudPresets.CreateDefaultCustom());

        if (Alerts.Count == 0 && !AlertsInitialized)
        {
            foreach (var r in AlertRule.Defaults()) Alerts.Add(r);
        }
        AlertsInitialized = true;

        Performance.SensorIntervalMs = Math.Clamp(Performance.SensorIntervalMs, 250, 10000);
        General.HudRefreshMs = Math.Clamp(General.HudRefreshMs, 100, 5000);
        Appearance.Scale = Math.Clamp(Appearance.Scale, 0.5, 3.0);
        Appearance.Opacity = Math.Clamp(Appearance.Opacity, 0.1, 1.0);
        Appearance.BackgroundOpacity = Math.Clamp(Appearance.BackgroundOpacity, 0.0, 1.0);
    }

    public bool AlertsInitialized { get; set; }
}

public static class DefaultHotkeys
{
    public static string For(HotkeyAction a) => a switch
    {
        HotkeyAction.ToggleHud => "F9",
        HotkeyAction.CyclePreset => "F10",
        HotkeyAction.ToggleCompact => "F11",
        HotkeyAction.ToggleGraphs => "Ctrl+Alt+G",
        HotkeyAction.ToggleClickThrough => "Ctrl+Alt+L",
        HotkeyAction.ToggleSettings => "Ctrl+Alt+S",
        HotkeyAction.TogglePause => "Ctrl+Alt+P",
        HotkeyAction.ScreenshotHud => "Ctrl+Alt+C",
        HotkeyAction.ResetPosition => "Ctrl+Alt+R",
        HotkeyAction.ToggleRecording => "Ctrl+Alt+H",
        HotkeyAction.OpenEditor => "Ctrl+Alt+E",
        _ => "",
    };
}

public sealed class HotkeyBinding : Observable
{
    private HotkeyAction _action;
    private string _gesture = "";
    public HotkeyAction Action { get => _action; set => Set(ref _action, value); }
    /// <summary>e.g. "F9", "Ctrl+Shift+F12". Empty = unbound.</summary>
    public string Gesture { get => _gesture; set => Set(ref _gesture, value ?? ""); }
}

public sealed class GeneralSettings : Observable
{
    private bool _startWithWindows, _startElevated, _startMinimized = true, _showHudOnStartup = true;
    private string _language = "en";
    private int _hudRefreshMs = 500;
    private TemperatureUnit _tempUnit;
    private NetworkUnit _netUnit;
    private bool _firstRunDone;

    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    /// <summary>Use a highest-privilege scheduled task instead of the Run key (enables admin-only sensors without UAC prompts).</summary>
    public bool StartElevated { get => _startElevated; set => Set(ref _startElevated, value); }
    public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }
    public bool ShowHudOnStartup { get => _showHudOnStartup; set => Set(ref _showHudOnStartup, value); }
    public string Language { get => _language; set => Set(ref _language, value); }
    /// <summary>How often the HUD redraws values (ms).</summary>
    public int HudRefreshMs { get => _hudRefreshMs; set => Set(ref _hudRefreshMs, value); }
    public TemperatureUnit TemperatureUnit { get => _tempUnit; set => Set(ref _tempUnit, value); }
    public NetworkUnit NetworkUnit { get => _netUnit; set => Set(ref _netUnit, value); }
    public bool FirstRunDone { get => _firstRunDone; set => Set(ref _firstRunDone, value); }
}

public sealed class AppearanceSettings : Observable
{
    private string _theme = "Midnight";
    private string _bg = "#0A0D12", _panel = "#11161D", _accent = "#00D9FF", _success = "#42E88A",
        _warning = "#FFC857", _hot = "#FF8C42", _danger = "#FF4D6D", _cool = "#4CC9F0", _text = "#F2F5F7", _muted = "#7C8795";
    private double _bgOpacity = 0.80, _opacity = 1.0, _scale = 1.0, _fontScale = 1.0, _corner = 10, _anim = 1.0;
    private string _labelFont = "Segoe UI", _valueFont = "Bahnschrift";
    private bool _blur, _shadow = true, _border = true, _highContrast, _showIcons = true, _showLabels = true;
    private ColorVisionMode _cvm;

    public string ThemeName { get => _theme; set => Set(ref _theme, value); }
    public string Background { get => _bg; set => Set(ref _bg, value); }
    public string Panel { get => _panel; set => Set(ref _panel, value); }
    public string Accent { get => _accent; set => Set(ref _accent, value); }
    public string Success { get => _success; set => Set(ref _success, value); }
    public string Warning { get => _warning; set => Set(ref _warning, value); }
    public string Hot { get => _hot; set => Set(ref _hot, value); }
    public string Danger { get => _danger; set => Set(ref _danger, value); }
    public string Cool { get => _cool; set => Set(ref _cool, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
    public string Muted { get => _muted; set => Set(ref _muted, value); }

    /// <summary>Opacity of the HUD background panel only (text stays crisp).</summary>
    public double BackgroundOpacity { get => _bgOpacity; set => Set(ref _bgOpacity, value); }
    /// <summary>Opacity of the whole HUD window.</summary>
    public double Opacity { get => _opacity; set => Set(ref _opacity, value); }
    public double Scale { get => _scale; set => Set(ref _scale, value); }
    public double FontScale { get => _fontScale; set => Set(ref _fontScale, value); }
    public double CornerRadius { get => _corner; set => Set(ref _corner, value); }
    /// <summary>0 = no animations, 1 = normal, 2 = slow.</summary>
    public double AnimationSpeed { get => _anim; set => Set(ref _anim, value); }
    public string LabelFont { get => _labelFont; set => Set(ref _labelFont, value); }
    public string ValueFont { get => _valueFont; set => Set(ref _valueFont, value); }
    public bool Blur { get => _blur; set => Set(ref _blur, value); }
    public bool Shadow { get => _shadow; set => Set(ref _shadow, value); }
    public bool Border { get => _border; set => Set(ref _border, value); }
    public bool HighContrast { get => _highContrast; set => Set(ref _highContrast, value); }
    public bool ShowIcons { get => _showIcons; set => Set(ref _showIcons, value); }
    public bool ShowLabels { get => _showLabels; set => Set(ref _showLabels, value); }
    public ColorVisionMode ColorVision { get => _cvm; set => Set(ref _cvm, value); }

    public void ApplyTheme(ThemeDefinition t)
    {
        ThemeName = t.Name;
        Background = t.Background; Panel = t.Panel; Accent = t.Accent; Success = t.Success; Warning = t.Warning;
        Hot = t.Hot; Danger = t.Danger; Cool = t.Cool; Text = t.Text; Muted = t.Muted;
    }
}

public sealed class HudSettings : Observable
{
    private bool _enabled = true, _compact, _showGraphs = true, _clickThrough = true, _respectTaskbar = true, _hideFromCapture;
    private string _preset = HudPresets.GamingName, _monitor = "";
    private HudCorner _corner = HudCorner.TopLeft;
    private double _offX = 16, _offY = 16;
    private int _customX = 40, _customY = 40;
    private HudOrientation _orientation;
    private HudAlignment _alignment;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string ActivePreset { get => _preset; set => Set(ref _preset, value); }
    public HudCorner Corner { get => _corner; set => Set(ref _corner, value); }
    /// <summary>Margin from the anchored screen edge, in DIPs.</summary>
    public double OffsetX { get => _offX; set => Set(ref _offX, value); }
    public double OffsetY { get => _offY; set => Set(ref _offY, value); }
    /// <summary>Custom position in physical pixels relative to the monitor's top-left (when Corner = Custom).</summary>
    public int CustomX { get => _customX; set => Set(ref _customX, value); }
    public int CustomY { get => _customY; set => Set(ref _customY, value); }
    /// <summary>"" = primary monitor, "*active" = follow the foreground window, otherwise a device name like \\.\DISPLAY2.</summary>
    public string Monitor { get => _monitor; set => Set(ref _monitor, value); }
    public HudOrientation Orientation { get => _orientation; set => Set(ref _orientation, value); }
    public HudAlignment Alignment { get => _alignment; set => Set(ref _alignment, value); }
    public bool Compact { get => _compact; set => Set(ref _compact, value); }
    public bool ShowGraphs { get => _showGraphs; set => Set(ref _showGraphs, value); }
    /// <summary>Locked = mouse clicks pass through the HUD. Unlock to drag it.</summary>
    public bool ClickThrough { get => _clickThrough; set => Set(ref _clickThrough, value); }
    public bool RespectTaskbar { get => _respectTaskbar; set => Set(ref _respectTaskbar, value); }
    public bool HideFromCapture { get => _hideFromCapture; set => Set(ref _hideFromCapture, value); }
}

public sealed class PerformanceSettings : Observable
{
    private int _sensorMs = 1000, _graphSeconds = 60, _fpsWindow = 30;
    private bool _fpsEnabled = true, _adaptive = true, _ping = true;
    private double _cpuLimit = 1.5;
    private FpsTargetMode _fpsTarget = FpsTargetMode.ForegroundThenMostActive;
    private string _pingHost = "";

    public int SensorIntervalMs { get => _sensorMs; set => Set(ref _sensorMs, value); }
    /// <summary>Default graph window: 5, 10, 30, 60 or 300 seconds.</summary>
    public int GraphHistorySeconds { get => _graphSeconds; set => Set(ref _graphSeconds, value); }
    public bool FpsEnabled { get => _fpsEnabled; set => Set(ref _fpsEnabled, value); }
    /// <summary>Window used for average / 1% low / 0.1% low.</summary>
    public int FpsStatsWindowSeconds { get => _fpsWindow; set => Set(ref _fpsWindow, value); }
    public FpsTargetMode FpsTarget { get => _fpsTarget; set => Set(ref _fpsTarget, value); }
    /// <summary>If PerfHud itself exceeds this CPU % (of total), polling slows down automatically.</summary>
    public double CpuLimitPercent { get => _cpuLimit; set => Set(ref _cpuLimit, value); }
    public bool AdaptiveThrottle { get => _adaptive; set => Set(ref _adaptive, value); }
    public bool PingEnabled { get => _ping; set => Set(ref _ping, value); }
    /// <summary>"" = default gateway (stays on the local network).</summary>
    public string PingHost { get => _pingHost; set => Set(ref _pingHost, value); }
}

public sealed class BatterySettings : Observable
{
    private bool _enabled = true, _reduceAnim = true, _disableExpensive = true;
    private double _mult = 2.0;
    public bool OptimizationEnabled { get => _enabled; set => Set(ref _enabled, value); }
    public double IntervalMultiplier { get => _mult; set => Set(ref _mult, value); }
    public bool ReduceAnimations { get => _reduceAnim; set => Set(ref _reduceAnim, value); }
    public bool DisableExpensiveSensors { get => _disableExpensive; set => Set(ref _disableExpensive, value); }
}

public sealed class TempThreshold : Observable
{
    private double _warm, _hot, _critical;
    public TempThreshold() { }
    public TempThreshold(double warm, double hot, double critical) { _warm = warm; _hot = hot; _critical = critical; }
    public double Warm { get => _warm; set => Set(ref _warm, value); }
    public double Hot { get => _hot; set => Set(ref _hot, value); }
    public double Critical { get => _critical; set => Set(ref _critical, value); }
}

public sealed class ThresholdSettings : Observable
{
    public TempThreshold Cpu { get; set; } = new(70, 85, 95);
    public TempThreshold Gpu { get; set; } = new(70, 80, 88);
    public TempThreshold Storage { get; set; } = new(50, 60, 70);
    public TempThreshold Battery { get; set; } = new(40, 45, 55);
    public TempThreshold Other { get; set; } = new(60, 75, 90);

    private double _usageWarn = 75, _usageCrit = 92, _memWarn = 80, _memCrit = 92, _fpsWarn = 60, _fpsCrit = 30, _batWarn = 20, _batCrit = 10;
    public double UsageWarn { get => _usageWarn; set => Set(ref _usageWarn, value); }
    public double UsageCritical { get => _usageCrit; set => Set(ref _usageCrit, value); }
    public double MemoryWarn { get => _memWarn; set => Set(ref _memWarn, value); }
    public double MemoryCritical { get => _memCrit; set => Set(ref _memCrit, value); }
    public double FpsWarn { get => _fpsWarn; set => Set(ref _fpsWarn, value); }
    public double FpsCritical { get => _fpsCrit; set => Set(ref _fpsCrit, value); }
    public double BatteryWarn { get => _batWarn; set => Set(ref _batWarn, value); }
    public double BatteryCritical { get => _batCrit; set => Set(ref _batCrit, value); }
}

public sealed class SensorSettings : Observable
{
    private bool _lhm = true, _kernel, _noWake = true, _vendorWmi = true, _diskTemps = true;
    /// <summary>Use LibreHardwareMonitorLib for extended sensors (AMD GPUs, SMART temps when elevated).</summary>
    public bool UseLibreHardwareMonitor { get => _lhm; set => Set(ref _lhm, value); }
    /// <summary>Allow LibreHardwareMonitor's kernel driver (CPU MSR temps/power). Requires admin; some anti-cheats dislike it. Off by default.</summary>
    public bool AllowKernelDriver { get => _kernel; set => Set(ref _kernel, value); }
    /// <summary>Don't poll a sleeping discrete GPU (Optimus/hybrid laptops) — polling would wake it and drain the battery.</summary>
    public bool AvoidWakingDiscreteGpu { get => _noWake; set => Set(ref _noWake, value); }
    /// <summary>Use OEM WMI interfaces (e.g. Lenovo GameZone) for temperatures and performance mode when accessible.</summary>
    public bool UseVendorWmi { get => _vendorWmi; set => Set(ref _vendorWmi, value); }
    public bool DiskTemperatures { get => _diskTemps; set => Set(ref _diskTemps, value); }
}

public sealed class AppProfile : Observable
{
    private bool _enabled = true, _hide, _autoRecord;
    private string _exe = "", _preset = "";
    private ProfileTrigger _trigger;
    private HudCorner? _corner;
    private double? _opacity, _scale;
    private bool? _compact;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    /// <summary>Executable name, e.g. "Cyberpunk2077.exe" (case-insensitive).</summary>
    public string ExeName { get => _exe; set => Set(ref _exe, value?.Trim() ?? ""); }
    public ProfileTrigger Trigger { get => _trigger; set => Set(ref _trigger, value); }
    /// <summary>Layout/preset name; "" keeps the current one.</summary>
    public string Preset { get => _preset; set => Set(ref _preset, value ?? ""); }
    public bool Hide { get => _hide; set => Set(ref _hide, value); }
    public HudCorner? Corner { get => _corner; set => Set(ref _corner, value); }
    public double? Opacity { get => _opacity; set => Set(ref _opacity, value); }
    public double? Scale { get => _scale; set => Set(ref _scale, value); }
    public bool? Compact { get => _compact; set => Set(ref _compact, value); }
    public bool AutoRecord { get => _autoRecord; set => Set(ref _autoRecord, value); }

    public override string ToString() => string.IsNullOrEmpty(ExeName) ? "(new profile)" : ExeName;
}

public sealed class ProfileSettings : Observable
{
    private bool _enabled = true;
    private string _desktop = "";
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    /// <summary>What to do when the desktop is focused: "" = nothing, "(Hidden)" = hide HUD, otherwise a preset name.</summary>
    public string DesktopAction { get => _desktop; set => Set(ref _desktop, value ?? ""); }
    public ObservableList<AppProfile> Items { get; set; } = new();
}

public sealed class HistorySettings : Observable
{
    private AutoRecordMode _auto = AutoRecordMode.GamesDetected;
    private int _sample = 1, _retention = 90;
    public AutoRecordMode AutoRecord { get => _auto; set => Set(ref _auto, value); }
    public int SampleIntervalSeconds { get => _sample; set => Set(ref _sample, value); }
    public int RetentionDays { get => _retention; set => Set(ref _retention, value); }
}

public sealed class PrivacySettings : Observable
{
    private bool _showIp, _showProcess = true;
    /// <summary>Show the local IP address on the HUD. Off by default.</summary>
    public bool ShowLocalIp { get => _showIp; set => Set(ref _showIp, value); }
    /// <summary>Show the name of the app being measured next to FPS.</summary>
    public bool ShowProcessNames { get => _showProcess; set => Set(ref _showProcess, value); }
}

public sealed class AlertRule : Observable
{
    private bool _enabled = true, _onlyOnBattery;
    private string _metric = "", _name = "";
    private CompareOp _op;
    private double _threshold, _duration = 3, _cooldown = 120;
    private AlertActions _actions = AlertActions.Notification | AlertActions.HudWarning;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string MetricId { get => _metric; set => Set(ref _metric, value ?? ""); }
    public CompareOp Op { get => _op; set => Set(ref _op, value); }
    public double Threshold { get => _threshold; set => Set(ref _threshold, value); }
    public double DurationSeconds { get => _duration; set => Set(ref _duration, value); }
    public double CooldownSeconds { get => _cooldown; set => Set(ref _cooldown, value); }
    public bool OnlyOnBattery { get => _onlyOnBattery; set => Set(ref _onlyOnBattery, value); }
    public AlertActions Actions { get => _actions; set { if (Set(ref _actions, value)) { Raise(nameof(Notify)); Raise(nameof(Hud)); Raise(nameof(Sound)); } } }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Notify { get => Actions.HasFlag(AlertActions.Notification); set => Actions = value ? Actions | AlertActions.Notification : Actions & ~AlertActions.Notification; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Hud { get => Actions.HasFlag(AlertActions.HudWarning); set => Actions = value ? Actions | AlertActions.HudWarning : Actions & ~AlertActions.HudWarning; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Sound { get => Actions.HasFlag(AlertActions.Sound); set => Actions = value ? Actions | AlertActions.Sound : Actions & ~AlertActions.Sound; }

    public static IEnumerable<AlertRule> Defaults()
    {
        using var quiet = Observable.Quiet();
        return DefaultsCore().ToList();
    }

    private static IEnumerable<AlertRule> DefaultsCore() => new[]
    {
        new AlertRule { Name = "GPU hot", MetricId = "gpu.temp", Op = CompareOp.Above, Threshold = 85 },
        new AlertRule { Name = "CPU hot", MetricId = "cpu.temp", Op = CompareOp.Above, Threshold = 90 },
        new AlertRule { Name = "Battery low", MetricId = "bat.pct", Op = CompareOp.Below, Threshold = 20, OnlyOnBattery = true, DurationSeconds = 1, CooldownSeconds = 600 },
        new AlertRule { Name = "RAM almost full", MetricId = "ram.pct", Op = CompareOp.Above, Threshold = 90, DurationSeconds = 10 },
        new AlertRule { Name = "VRAM almost full", MetricId = "gpu.vram.pct", Op = CompareOp.Above, Threshold = 95, DurationSeconds = 10 },
        new AlertRule { Name = "Low disk space", MetricId = "disk.sys.free", Op = CompareOp.Below, Threshold = 10, DurationSeconds = 1, CooldownSeconds = 3600 },
        new AlertRule { Name = "FPS drop", MetricId = "fps.current", Op = CompareOp.Below, Threshold = 30, DurationSeconds = 5, Actions = AlertActions.HudWarning, Enabled = false },
    };
}
