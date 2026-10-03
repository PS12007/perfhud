using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PerfHud.Hud;
using PerfHud.Monitoring;
using PerfHud.Rendering;
using PerfHud.Sensors.Native;

namespace PerfHud.Notifications;

/// <summary>Small, non-activating, click-through notification in the corner of the primary screen. Stacks up to 3.</summary>
public sealed class ToastWindow : Window
{
    private static readonly List<ToastWindow> Open = new();
    private readonly DispatcherTimer _timer;

    public ToastWindow(string title, string message, Severity sev, HudStyle style)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;

        var accent = style.ForSeverity(sev);
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Icons.Create(sev >= Severity.Warm ? "warn" : "bell", accent, 15));
        head.Children.Add(new TextBlock { Text = title, Margin = new Thickness(7, 0, 0, 0), Foreground = style.Text, FontFamily = style.LabelFont, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var body = new TextBlock { Text = message, Margin = new Thickness(22, 3, 0, 0), Foreground = style.Muted, FontFamily = style.LabelFont, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        Content = new Border
        {
            Child = stack,
            Background = ColorUtil.Brush(Color.FromRgb(style.Background.Color.R, style.Background.Color.G, style.Background.Color.B), 0.96),
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(Math.Min(8, style.CornerRadius)),
            Padding = new Thickness(12, 9, 14, 10),
            Margin = new Thickness(10),
            MinWidth = 260,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5 },
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _timer.Tick += (_, _) => { _timer.Stop(); FadeOut(); };
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            var ex = (long)Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE) | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLongPtr(h, Win32.GWL_EXSTYLE, (IntPtr)ex);
        };
        Loaded += (_, _) => { Layout(); BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))); _timer.Start(); };
    }

    public static void Show(string title, string message, Severity sev, HudStyle style)
    {
        while (Open.Count >= 3) Open[0].Close();
        var t = new ToastWindow(title, message, sev, style);
        Open.Add(t);
        t.Closed += (_, _) => { Open.Remove(t); foreach (var o in Open) o.Layout(); };
        t.Show();
    }

    private void Layout()
    {
        var wa = SystemParameters.WorkArea;
        double y = wa.Bottom;
        foreach (var t in Open.AsEnumerable().Reverse())
        {
            y -= t.ActualHeight;
            t.Left = wa.Right - t.ActualWidth;
            t.Top = y;
        }
    }

    private void FadeOut()
    {
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        a.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, a);
    }
}
