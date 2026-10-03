using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PerfHud.Core;
using PerfHud.Hud.Elements;
using PerfHud.Monitoring;
using PerfHud.Sensors.Native;
using PerfHud.Settings;

namespace PerfHud.Hud;

public sealed class HudBuildSpec
{
    public required HudLayout Layout { get; init; }
    public required HudStyle Style { get; init; }
    public bool Transpose { get; init; }
    public bool Compact { get; init; }
    public bool ShowGraphs { get; init; }
    public double Opacity { get; init; } = 1;
    public double Scale { get; init; } = 1;
    public bool Blur { get; init; }
    public bool Shadow { get; init; }
    public HudCorner Corner { get; init; }
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public int CustomX { get; init; }
    public int CustomY { get; init; }
    public string Monitor { get; init; } = "";
    public bool RespectTaskbar { get; init; }
    public bool HideFromCapture { get; init; }
    public bool ClickThrough { get; init; }
}

/// <summary>
/// The overlay: borderless, transparent, always-on-top, non-activating, optionally click-through.
/// Works over windowed and borderless-fullscreen games. (Exclusive fullscreen bypasses the desktop compositor,
/// so no non-injected overlay can draw over it — see README.)
/// </summary>
public sealed class HudWindow : Window
{
    private readonly Grid _root = new();
    private readonly Border _panel = new();
    private readonly DockPanel _dock = new();
    private readonly Border _banner = new();
    private readonly TextBlock _bannerText = new();
    private readonly StackPanel _badges = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ContentControl _host = new();
    private readonly List<HudElement> _elements = new();
    private readonly DispatcherTimer _bannerTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IntPtr _hwnd;
    private HudBuildSpec? _spec;
    private bool _wantVisible;

    /// <summary>Raised after the user drags the HUD (unlocked mode): physical px relative to monitor, monitor device.</summary>
    public event Action<int, int, string>? UserMoved;
    /// <summary>Raised on Ctrl+wheel while unlocked: requested scale delta.</summary>
    public event Action<double>? UserScaled;

    public HudWindow()
    {
        Title = "PerfHud Overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        Left = -10000; Top = -10000;

        _bannerText.FontWeight = FontWeights.SemiBold;
        _bannerText.TextWrapping = TextWrapping.Wrap;
        _bannerText.MaxWidth = 340;
        _banner.Child = _bannerText;
        _banner.Visibility = Visibility.Collapsed;
        _banner.CornerRadius = new CornerRadius(5);
        _banner.Padding = new Thickness(8, 4, 8, 4);
        _banner.Margin = new Thickness(4, 2, 4, 6);
        _banner.BorderThickness = new Thickness(3, 0, 0, 0);
        DockPanel.SetDock(_banner, Dock.Top);
        DockPanel.SetDock(_badges, Dock.Top);
        _dock.Children.Add(_badges);
        _dock.Children.Add(_banner);
        _dock.Children.Add(_host);
        _panel.Child = _dock;
        _root.Children.Add(_panel);
        Content = _root;

        _bannerTimer.Tick += (_, _) => { _bannerTimer.Stop(); _banner.Visibility = Visibility.Collapsed; };
        _topmostTimer.Tick += (_, _) => AssertTopmost();

        SizeChanged += (_, _) => { UpdateRegion(); Reposition(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);
        MouseLeftButtonDown += OnMouseDown;
        PreviewMouseWheel += OnWheel;
    }

    public IntPtr Handle => _hwnd;
    public bool IsShownToUser => _wantVisible;
    public IReadOnlyList<HudElement> Elements => _elements;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED | Win32.WS_EX_TOPMOST;
        ex &= ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)ex);
        if (_spec != null) ApplyWindowFlags(_spec);
    }

    public void Build(HudBuildSpec spec)
    {
        _spec = spec;
        var st = spec.Style;
        _elements.Clear();

        var comps = spec.Layout.Components.Where(c =>
            !(spec.Compact && c.DetailOnly) &&
            !(!spec.ShowGraphs && (c.IsGraph || c.Type == ComponentType.CoreGrid))).ToList();
        if (spec.Compact) comps = Compactify(comps);

        var grid = HudElementFactory.BuildGrid(comps, st, spec.Transpose, _elements);
        if (comps.Count == 0)
        {
            grid.Children.Add(new TextBlock { Text = "Empty layout — open the HUD editor", Foreground = st.Muted, FontFamily = st.LabelFont, Margin = new Thickness(6) });
        }
        grid.LayoutTransform = Math.Abs(spec.Scale - 1) > 0.001 ? new ScaleTransform(spec.Scale, spec.Scale) : null;
        _host.Content = grid;

        HudChrome.Apply(_panel, _dock, _host, st, spec.Shadow && !spec.Blur);

        _bannerText.FontFamily = st.LabelFont;
        _bannerText.FontSize = st.LabelSize * 1.05;
        _root.Opacity = _wantVisible ? 1 : _root.Opacity;
        Opacity = Math.Clamp(spec.Opacity, 0.1, 1);

        if (_hwnd != IntPtr.Zero) ApplyWindowFlags(spec);
        UpdateRegion();
        Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Loaded);
    }

    /// <summary>In compact mode, packs the remaining components so hidden ones don't leave gaps.</summary>
    private static List<HudComponent> Compactify(List<HudComponent> comps)
    {
        var rows = comps.Select(c => c.Row).Distinct().OrderBy(r => r).ToList();
        var map = rows.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => x.i);
        return comps.Select(c =>
        {
            var copy = c.Clone();
            copy.Row = map[c.Row];
            return copy;
        }).ToList();
    }

    private void ApplyWindowFlags(HudBuildSpec spec)
    {
        SetClickThrough(spec.ClickThrough);
        Win32.SetBlurBehind(_hwnd, spec.Blur && !spec.Style.HighContrast);
        Win32.SetWindowDisplayAffinity(_hwnd, spec.HideFromCapture ? Win32.WDA_EXCLUDEFROMCAPTURE : Win32.WDA_NONE);
        UpdateBadges(_paused, _recording);
    }

    public void SetClickThrough(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        var ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        ex = on ? ex | Win32.WS_EX_TRANSPARENT : ex & ~Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)ex);
        Cursor = on ? null : Cursors.SizeAll;
        UpdateBadges(_paused, _recording);
    }

    private bool _paused, _recording;

    public void UpdateBadges(bool paused, bool recording)
    {
        _paused = paused; _recording = recording;
        _badges.Children.Clear();
        if (_spec == null) return;
        var st = _spec.Style;
        void Badge(string text, Brush b)
        {
            _badges.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3), BorderBrush = b, BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(4, 0, 0, 4),
                Child = new TextBlock { Text = text, Foreground = b, FontFamily = st.LabelFont, FontSize = st.SmallSize, FontWeight = FontWeights.Bold },
            });
        }
        if (recording) Badge("● REC", st.Critical);
        if (paused) Badge("❚❚ PAUSED", st.Warm);
        if (!_spec.ClickThrough) Badge("UNLOCKED · drag to move · Ctrl+scroll to scale", st.Accent);
        _badges.Visibility = _badges.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void RefreshValues(HudRenderContext ctx)
    {
        foreach (var e in _elements)
        {
            try { e.Refresh(ctx); }
            catch (Exception ex) { Log.Once($"refresh-{e.Component.Type}-{e.Component.MetricId}", LogLevel.Warn, $"HUD element refresh failed: {ex.Message}"); }
        }
    }

    public void ShowBanner(string text, Severity sev, TimeSpan? duration = null)
    {
        if (_spec == null) return;
        var b = _spec.Style.ForSeverity(sev);
        _bannerText.Text = (sev >= Severity.Warm ? "⚠ " : "") + text;
        _bannerText.Foreground = sev >= Severity.Warm ? b : _spec.Style.Text;
        _banner.BorderBrush = b;
        _banner.Background = ColorUtil.Brush(((SolidColorBrush)b).Color, 0.12);
        _banner.Visibility = Visibility.Visible;
        _bannerTimer.Stop();
        _bannerTimer.Interval = duration ?? TimeSpan.FromSeconds(6);
        _bannerTimer.Start();
    }

    // ── Visibility with fade ─────────────────────────────

    public void ShowHud(double fadeMs)
    {
        _wantVisible = true;
        if (!IsVisible) { _root.Opacity = fadeMs > 0 ? 0 : 1; Show(); }
        AssertTopmost();
        _topmostTimer.Start();
        Fade(1, fadeMs, null);
    }

    public void HideHud(double fadeMs)
    {
        _wantVisible = false;
        _topmostTimer.Stop();
        if (!IsVisible) return;
        Fade(0, fadeMs, () => { if (!_wantVisible) Hide(); });
    }

    private void Fade(double to, double ms, Action? done)
    {
        if (ms <= 0)
        {
            _root.BeginAnimation(OpacityProperty, null);
            _root.Opacity = to;
            done?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() };
        if (done != null) a.Completed += (_, _) => done();
        _root.BeginAnimation(OpacityProperty, a);
    }

    private void AssertTopmost()
    {
        if (_hwnd != IntPtr.Zero && _wantVisible)
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    // ── Positioning (physical pixels, per-monitor DPI aware) ──

    public Win32.MonitorInfo? CurrentMonitor { get; private set; }

    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _spec == null) return;
        var mon = ResolveMonitor(_spec.Monitor);
        if (mon == null) return;
        CurrentMonitor = mon;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Ceiling(ActualWidth * dpi), h = (int)Math.Ceiling(ActualHeight * dpi);
        var area = _spec.RespectTaskbar ? mon.Work : mon.Bounds;
        double scale = mon.DpiScale;
        int ox = (int)(_spec.OffsetX * scale) - (int)(_panel.Margin.Left * dpi), oy = (int)(_spec.OffsetY * scale) - (int)(_panel.Margin.Top * dpi);
        int x, y;
        switch (_spec.Corner)
        {
            case HudCorner.Custom:
                x = mon.Bounds.Left + _spec.CustomX;
                y = mon.Bounds.Top + _spec.CustomY;
                break;
            default:
                var name = _spec.Corner.ToString();
                x = name.EndsWith("Left") ? area.Left + ox : name.EndsWith("Right") ? area.Right - w - ox : area.Left + (area.Width - w) / 2;
                y = name.StartsWith("Top") ? area.Top + oy : name.StartsWith("Bottom") ? area.Bottom - h - oy : area.Top + (area.Height - h) / 2;
                break;
        }
        // Keep it on-screen (monitor changes / resolution changes)
        x = Math.Clamp(x, mon.Bounds.Left - w / 2, Math.Max(mon.Bounds.Left, mon.Bounds.Right - w / 2));
        y = Math.Clamp(y, mon.Bounds.Top, Math.Max(mon.Bounds.Top, mon.Bounds.Bottom - Math.Min(h, 40)));
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    private static Win32.MonitorInfo? ResolveMonitor(string device)
    {
        var all = Win32.GetMonitors();
        if (all.Count == 0) return null;
        if (device == "*active")
        {
            var fg = Win32.GetForegroundWindow();
            if (fg != IntPtr.Zero) return Win32.GetMonitor(Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST)) ?? all[0];
        }
        return all.FirstOrDefault(m => !string.IsNullOrEmpty(device) && m.Device.Equals(device, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(m => m.Primary) ?? all[0];
    }

    private void UpdateRegion()
    {
        if (_hwnd == IntPtr.Zero || _spec == null) return;
        if (_spec.Blur && !_spec.Style.HighContrast)
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int w = (int)Math.Ceiling(ActualWidth * dpi), h = (int)Math.Ceiling(ActualHeight * dpi);
            int r = (int)Math.Round(_spec.Style.CornerRadius * 2 * dpi);
            if (w > 0 && h > 0) Win32.SetWindowRgn(_hwnd, Win32.CreateRoundRectRgn(0, 0, w + 1, h + 1, r, r), true);
        }
        else Win32.SetWindowRgn(_hwnd, IntPtr.Zero, true);
    }

    // ── Unlocked interaction ─────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_spec == null || _spec.ClickThrough || e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        if (Win32.GetWindowRect(_hwnd, out var r))
        {
            var mon = Win32.GetMonitor(Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST));
            if (mon != null) UserMoved?.Invoke(r.Left - mon.Bounds.Left, r.Top - mon.Bounds.Top, mon.Device);
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_spec == null || _spec.ClickThrough || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        UserScaled?.Invoke(e.Delta > 0 ? 0.05 : -0.05);
        e.Handled = true;
    }

    // ── Screenshot ───────────────────────────────────────

    public string? SaveScreenshot()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(_root.ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(_root.ActualHeight * dpi.DpiScaleY);
            if (w <= 0 || h <= 0) return null;
            var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var prev = _root.Opacity;
            _root.Opacity = 1;
            rtb.Render(_root);
            _root.Opacity = prev;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            var path = Path.Combine(AppPaths.ScreenshotDir, $"hud-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            using (var f = File.Create(path)) enc.Save(f);
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("HUD screenshot failed", ex);
            return null;
        }
    }
}
