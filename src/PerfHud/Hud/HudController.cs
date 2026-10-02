using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PerfHud.Core;
using PerfHud.Hud.Elements;
using PerfHud.Monitoring;
using PerfHud.Monitoring.Monitors;
using PerfHud.Notifications;
using PerfHud.Profiles;
using PerfHud.Settings;

namespace PerfHud.Hud;

/// <summary>
/// Owns the overlay: resolves the effective state (settings + active app profile), rebuilds on changes,
/// refreshes values on a timer and implements every HUD action used by hotkeys and the tray.
/// </summary>
public sealed class HudController : IDisposable
{
    private readonly SettingsService _svc;
    private readonly MetricStore _store;
    private readonly MonitoringService _mon;
    private readonly SensorHub _hub;
    private readonly FpsMonitor? _fps;
    private readonly HudWindow _win = new();
    private readonly DispatcherTimer _refresh = new(DispatcherPriority.Render);
    private readonly DispatcherTimer _rebuild = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
    private HudRenderContext? _ctx;
    private HudStyle? _style;
    private ProfileMatch _profile = new(null, false, null);
    private bool _userOverride;          // user forced the HUD visible while a profile hides it
    private bool _ignoreProfilePreset;   // user picked a preset manually while a profile is active
    private bool _recording;
    private long _lastActiveMonitorCheck;

    public event Action? StateChanged;

    private AppSettings S => _svc.Current;

    public HudController(SettingsService svc, MetricStore store, MonitoringService mon, SensorHub hub, FpsMonitor? fps)
    {
        _svc = svc; _store = store; _mon = mon; _hub = hub; _fps = fps;
        _win.UserMoved += (x, y, dev) =>
        {
            S.Hud.Monitor = dev;
            S.Hud.CustomX = x;
            S.Hud.CustomY = y;
            S.Hud.Corner = HudCorner.Custom;
        };
        _win.UserScaled += d => S.Appearance.Scale = Math.Clamp(Math.Round(S.Appearance.Scale + d, 2), 0.5, 3);
        _rebuild.Tick += (_, _) => { _rebuild.Stop(); Rebuild(); };
        _refresh.Tick += (_, _) => Tick();
        _svc.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(RequestRebuild);
        _mon.PausedChanged += _ => Application.Current?.Dispatcher.BeginInvoke(() => { _win.UpdateBadges(_mon.IsPaused, _recording); StateChanged?.Invoke(); });
    }

    public HudWindow Window => _win;

    public void Start(bool showOnStartup)
    {
        new WindowInteropHelper(_win).EnsureHandle();
        if (!showOnStartup) S.Hud.Enabled = false;
        Rebuild();
    }

    public void RequestRebuild() { _rebuild.Stop(); _rebuild.Start(); }

    // ── Effective state ──────────────────────────────────

    public string EffectivePreset
    {
        get
        {
            if (!_ignoreProfilePreset)
            {
                if (_profile.Profile is { Preset.Length: > 0 } p && p.Preset != HudPresets.HiddenToken) return p.Preset;
                if (_profile.Desktop && S.Profiles.DesktopAction is { Length: > 0 } d && d != HudPresets.HiddenToken) return d;
            }
            return S.Hud.ActivePreset;
        }
    }

    private bool ProfileHides => _profile.Profile?.Hide == true || _profile.Profile?.Preset == HudPresets.HiddenToken
                                 || (_profile.Desktop && S.Profiles.DesktopAction == HudPresets.HiddenToken);

    public bool IsHudVisible => S.Hud.Enabled && (!ProfileHides || _userOverride);
    public bool IsPowerSaving => _mon.Context.PowerSaving;
    public ProfileMatch ActiveProfile => _profile;

    private void Rebuild()
    {
        var p = _profile.Profile;
        bool compact = p?.Compact ?? S.Hud.Compact;
        bool reduceAnim = _mon.Context.PowerSaving && S.Battery.ReduceAnimations;
        _style = HudStyle.From(S, compact, reduceAnim);
        var spec = new HudBuildSpec
        {
            Layout = HudPresets.Resolve(EffectivePreset, S),
            Style = _style,
            Transpose = S.Hud.Orientation == HudOrientation.Horizontal,
            Compact = compact,
            ShowGraphs = S.Hud.ShowGraphs,
            Opacity = p?.Opacity ?? S.Appearance.Opacity,
            Scale = p?.Scale ?? S.Appearance.Scale,
            Blur = S.Appearance.Blur,
            Shadow = S.Appearance.Shadow,
            Corner = p?.Corner ?? S.Hud.Corner,
            OffsetX = S.Hud.OffsetX,
            OffsetY = S.Hud.OffsetY,
            CustomX = S.Hud.CustomX,
            CustomY = S.Hud.CustomY,
            Monitor = S.Hud.Monitor,
            RespectTaskbar = S.Hud.RespectTaskbar,
            HideFromCapture = S.Hud.HideFromCapture,
            ClickThrough = S.Hud.ClickThrough,
        };
        try { _win.Build(spec); }
        catch (Exception ex) { Log.Error("HUD build failed", ex); }
        _ctx = new HudRenderContext { Store = _store, Settings = S, Style = _style, Fps = _fps };
        _win.UpdateBadges(_mon.IsPaused, _recording);

        double mult = _mon.Context.PowerSaving ? Math.Max(1, S.Battery.IntervalMultiplier) : 1;
        _refresh.Interval = TimeSpan.FromMilliseconds(Math.Clamp(S.General.HudRefreshMs, 100, 5000) * mult);
        ApplyVisibility();
        Tick();
        StateChanged?.Invoke();
    }

    private void ApplyVisibility()
    {
        double fade = _style?.AnimMs ?? 0;
        if (IsHudVisible)
        {
            _win.ShowHud(fade * 0.8);
            _refresh.Start();
        }
        else
        {
            _win.HideHud(fade * 0.8);
            _refresh.Stop();
        }
    }

    private void Tick()
    {
        if (_ctx == null) return;
        _ctx.Now = MetricSeries.Now;
        _ctx.Paused = _mon.IsPaused;
        _store.SetText("time.now", DateTime.Now.ToString("HH:mm"));
        if (_win.IsShownToUser) _win.RefreshValues(_ctx);
        _hub.HudMonitorDevice = _win.CurrentMonitor?.Device;

        if (S.Hud.Monitor == "*active" && Environment.TickCount64 - _lastActiveMonitorCheck > 1500)
        {
            _lastActiveMonitorCheck = Environment.TickCount64;
            _win.Reposition();
        }
    }

    // ── Actions ──────────────────────────────────────────

    public void Toggle()
    {
        if (IsHudVisible) { S.Hud.Enabled = false; _userOverride = false; }
        else { _userOverride = ProfileHides; S.Hud.Enabled = true; }
        RequestRebuild();
    }

    public void SetVisible(bool visible)
    {
        if (visible != IsHudVisible) Toggle();
    }

    public void CyclePreset()
    {
        var names = HudPresets.AllNames(S);
        int i = names.IndexOf(EffectivePreset);
        var next = names[(i + 1) % names.Count];
        SetPreset(next);
        _win.ShowBanner($"Layout: {next}", Severity.Neutral, TimeSpan.FromSeconds(1.6));
    }

    public void SetPreset(string name)
    {
        _ignoreProfilePreset = _profile.Profile != null || _profile.Desktop;
        S.Hud.ActivePreset = name;
        if (!S.Hud.Enabled) S.Hud.Enabled = true;
    }

    public void ToggleCompact() => S.Hud.Compact = !S.Hud.Compact;
    public void ToggleGraphs() => S.Hud.ShowGraphs = !S.Hud.ShowGraphs;

    public void ToggleClickThrough()
    {
        S.Hud.ClickThrough = !S.Hud.ClickThrough;
        if (!S.Hud.ClickThrough && !IsHudVisible) S.Hud.Enabled = true;
    }

    public void ResetPosition()
    {
        S.Hud.Corner = HudCorner.TopLeft;
        S.Hud.OffsetX = 16;
        S.Hud.OffsetY = 16;
        S.Hud.Monitor = "";
        S.Appearance.Scale = 1;
        _win.ShowBanner("Position reset", Severity.Neutral, TimeSpan.FromSeconds(1.5));
    }

    public string? Screenshot()
    {
        if (!_win.IsShownToUser) return null;
        var path = _win.SaveScreenshot();
        if (path != null) _win.ShowBanner("Screenshot saved to Pictures\\PerfHud", Severity.Neutral, TimeSpan.FromSeconds(2));
        return path;
    }

    public void SetRecording(bool on)
    {
        _recording = on;
        _win.UpdateBadges(_mon.IsPaused, on);
        StateChanged?.Invoke();
    }

    public void ShowAlert(AlertEvent e)
    {
        if (!e.Rule.Actions.HasFlag(AlertActions.HudWarning)) return;
        _win.ShowBanner(e.Message, e.Severity);
    }

    public void ShowInfo(string text) => _win.ShowBanner(text, Severity.Neutral, TimeSpan.FromSeconds(3));

    public HudStyle CurrentStyle => _style ?? HudStyle.From(S, false, false);

    public void ApplyProfile(ProfileMatch m)
    {
        _profile = m;
        _userOverride = false;
        _ignoreProfilePreset = false;
        RequestRebuild();
    }

    public void OnDisplayChanged() => Application.Current?.Dispatcher.BeginInvoke(() => { _win.Reposition(); RequestRebuild(); });

    public void Dispose()
    {
        _refresh.Stop();
        _rebuild.Stop();
        _win.Close();
    }
}
