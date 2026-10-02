using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using PerfHud.Core;
using PerfHud.Rendering;
using PerfHud.Sensors.Native;
using PerfHud.Storage;
using PerfHud.UI.Controls;

namespace PerfHud.UI;

/// <summary>Browse recorded sessions: summary, charts, CSV/JSON export.</summary>
public sealed class HistoryWindow : Window
{
    private readonly AppHost _app;
    private readonly ListBox _list = new();
    private readonly ContentControl _detail = new();
    private readonly Button _recordButton;

    public HistoryWindow(AppHost app)
    {
        _app = app;
        Title = "PerfHud — Session history";
        Width = 1120; Height = 760; MinWidth = 820; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Res("BgBrush");
        Foreground = Ui.Res("TextBrush");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/PerfHud;component/Assets/app.ico"));

        var root = new DockPanel { Margin = new Thickness(18) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var title = Ui.H1("Session history");
        top.Children.Add(title);
        _recordButton = Ui.Button("", () => { _app.ToggleRecording(); UpdateRecordButton(); Dispatcher.BeginInvoke(Reload, System.Windows.Threading.DispatcherPriority.Background); }, "AccentButton", "record");
        var buttons = Ui.Buttons(_recordButton, Ui.Button("Refresh", Reload, null, "history"), Ui.Button("Open folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.SessionDir}\"") { UseShellExecute = true }), null, "disk"));
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Insert(0, buttons);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var left = new Border { Style = Ui.StyleRes("Card"), Width = 330, Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 0), Child = _list };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        _list.SelectionChanged += (_, _) => ShowSession((_list.SelectedItem as ListBoxItem)?.Tag as SessionSummary);
        _app.Recorder.SessionSaved += OnSaved;
        Closed += (_, _) => _app.Recorder.SessionSaved -= OnSaved;
        UpdateRecordButton();
        Reload();
    }

    private void OnSaved(SessionSummary s) => Dispatcher.BeginInvoke(() => { Reload(); UpdateRecordButton(); });

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void UpdateRecordButton()
    {
        if (_recordButton.Content is StackPanel sp && sp.Children[1] is TextBlock t)
            t.Text = _app.Recorder.IsRecording ? "Stop recording" : "Start recording";
    }

    private void Reload()
    {
        var sessions = SessionStore.List();
        _list.Items.Clear();
        foreach (var s in sessions)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(Ui.Muted($"{s.Start:ddd d MMM, HH:mm} · {s.DurationText} · {s.Trigger}", 11.5));
            if (s.AvgFps is double f) sp.Children.Add(new TextBlock { Text = $"{f:0} FPS avg · 1% low {s.Low1Fps:0}", Foreground = Ui.Res("AccentBrush"), FontSize = 11.5 });
            _list.Items.Add(new ListBoxItem { Content = sp, Tag = s });
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else _detail.Content = Ui.Card("No sessions yet", null,
            Ui.Muted("Sessions are recorded automatically when a full-screen game is detected (configurable in Settings → History), " +
                     "or manually with the Start recording button / hotkey."));
    }

    private void ShowSession(SessionSummary? s)
    {
        if (s == null) return;
        var root = new StackPanel();
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(Ui.H1(s.Name));
        head.Children.Add(Ui.Muted($"{s.Start:dddd d MMMM yyyy, HH:mm} – {s.End:HH:mm} · {s.App ?? "no app"} · {s.Samples} samples · {s.Frames:N0} frames", 12.5));
        root.Children.Add(head);

        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 6) };
        void Stat(string label, string value, string? brushKey = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Foreground = Ui.Res("MutedBrush"), FontSize = 11, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Bahnschrift, Segoe UI"), Foreground = Ui.Res(brushKey ?? "TextBrush"), Margin = new Thickness(0, 3, 0, 0) });
            grid.Children.Add(new Border { Style = Ui.StyleRes("Card"), Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 10, 14, 10), Child = sp });
        }
        static string N(double? v, string fmt, string unit = "") => v is double d ? d.ToString(fmt) + unit : "N/A";
        Stat("Duration", s.DurationText);
        Stat("Avg FPS", N(s.AvgFps, "0"), "AccentBrush");
        Stat("1% low", N(s.Low1Fps, "0"));
        Stat("0.1% low", N(s.Low01Fps, "0"));
        Stat("Avg GPU", N(s.AvgGpu, "0", "%"));
        Stat("Avg CPU", N(s.AvgCpu, "0", "%"));
        Stat("Max GPU temp", N(s.MaxGpuTemp, "0", "°C"), s.MaxGpuTemp >= _app.Settings.Current.Thresholds.Gpu.Hot ? "WarningBrush" : null);
        Stat("Max CPU temp", N(s.MaxCpuTemp, "0", "°C"), s.MaxCpuTemp >= _app.Settings.Current.Thresholds.Cpu.Hot ? "WarningBrush" : null);
        Stat("Battery used", N(s.BatteryUsed, "0.#", "%"));
        Stat("Avg battery drain", N(s.AvgBatteryDrainW, "0.0", " W"));
        Stat("Peak RAM", N(s.MaxRamPct, "0", "%"));
        Stat("Peak VRAM", N(s.MaxVramGb, "0.0", " GB"));
        root.Children.Add(grid);

        // Charts
        var samples = SessionStore.LoadSamples(s.Id);
        var charts = new StackPanel();
        foreach (var (col, label, max) in new (string, string, double)[]
                 {
                     ("fps", "FPS", double.NaN), ("frametime_ms", "Frame time (ms)", double.NaN), ("cpu_pct", "CPU %", 100), ("gpu_pct", "GPU %", 100),
                     ("cpu_temp_c", "CPU °C", double.NaN), ("gpu_temp_c", "GPU °C", double.NaN), ("ram_pct", "RAM %", 100), ("battery_pct", "Battery %", 100),
                     ("battery_drain_w", "Battery drain W", double.NaN), ("net_down_kbps", "Download KB/s", double.NaN),
                 })
        {
            if (!samples.Values.TryGetValue(col, out var vals) || vals.All(double.IsNaN)) continue;
            var t = samples.Time.ToArray();
            var v = vals.ToArray();
            var valid = v.Where(x => !double.IsNaN(x)).ToList();
            var spark = new Sparkline { Height = 46, Margin = new Thickness(0, 4, 0, 0) };
            spark.SetStyle(Ui.Res("AccentBrush"), new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 1.5);
            double window = Math.Max(1, t.Length > 0 ? t[^1] : 1);
            spark.Loaded += (_, _) => spark.SetData(t, v, t.Length, window, window, max);
            var head2 = new DockPanel();
            var stats = Ui.Muted($"min {valid.Min():0.#} · avg {valid.Average():0.#} · max {valid.Max():0.#}", 11.5);
            DockPanel.SetDock(stats, Dock.Right);
            head2.Children.Add(stats);
            head2.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            charts.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 12), Child = Ui.Stack(head2, spark) });
        }
        if (charts.Children.Count > 0) root.Children.Add(Ui.Card("Timeline", null, charts));

        root.Children.Add(Ui.Buttons(
            Ui.Button("Export CSV", () => Export(s, "csv"), "AccentButton", "copy"),
            Ui.Button("Export JSON", () => Export(s, "json"), null, "copy"),
            Ui.Button("Delete", () =>
            {
                if (MessageBox.Show(this, $"Delete session '{s.Name}'?", "PerfHud", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                SessionStore.Delete(s.Id);
                Reload();
            }, "DangerButton", "trash")));
        _detail.Content = root;
    }

    private void Export(SessionSummary s, string kind)
    {
        var d = new SaveFileDialog
        {
            FileName = $"perfhud-{s.Id}.{kind}",
            Filter = kind == "csv" ? "CSV|*.csv" : "JSON|*.json",
        };
        if (d.ShowDialog(this) != true) return;
        try
        {
            if (kind == "csv") SessionStore.ExportCsv(s, d.FileName);
            else SessionStore.ExportJson(s, d.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "PerfHud", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
