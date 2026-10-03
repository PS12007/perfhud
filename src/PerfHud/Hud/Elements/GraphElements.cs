using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PerfHud.Monitoring;
using PerfHud.Rendering;

namespace PerfHud.Hud.Elements;

/// <summary>Label + current value, sparkline of the metric's history underneath.</summary>
public sealed class GraphElement : HudElement
{
    private readonly Sparkline _spark;
    private readonly Run _value = new(), _unit = new();
    private double[] _t = new double[64], _v = new double[64];
    private Brush? _lastLine;
    private readonly bool _frameTimes;

    public GraphElement(HudComponent c, HudStyle s, bool frameTimes) : base(c, s)
    {
        _frameTimes = frameTimes;
        var stack = new StackPanel();
        var head = new DockPanel();
        var icon = MakeIcon(S.Muted);
        if (icon != null) { icon.Margin = new Thickness(0, 0, 5, 0); head.Children.Add(icon); }
        if (ShowLabel) head.Children.Add(MakeLabel(frameTimes && string.IsNullOrEmpty(C.Label) ? "FRAME TIME" : LabelText));
        var vb = MakeValueBlock(S.LabelSize * Fs * 1.1);
        vb.HorizontalAlignment = HorizontalAlignment.Right;
        vb.Margin = new Thickness(10, 0, 0, 0);
        _unit.FontFamily = S.LabelFont; _unit.Foreground = S.Muted; _unit.FontSize = S.SmallSize * Fs; _unit.FontWeight = FontWeights.Normal;
        vb.Inlines.AddRange(new Inline[] { _value, _unit });
        head.Children.Add(vb);
        stack.Children.Add(head);

        _spark = new Sparkline { Height = Math.Max(12, (C.Height > 0 ? C.Height : 28) * Fs), Margin = new Thickness(0, 3, 0, 1), MinWidth = 120, Mode = S.GraphStyle };
        _spark.SetStyle(OverrideBrush ?? S.Accent, S.Grid, S.GraphLineWidth);
        stack.Children.Add(_spark);
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        SetRun(_value, p.Value);
        SetFg(_value, p.Brush);
        SetRun(_unit, UnitText(p.Unit));

        var line = OverrideBrush ?? (Def?.Kind is MetricKind.Temperature or MetricKind.Fps or MetricKind.FrameTime && p.Severity >= Severity.Warm ? p.Brush : S.Accent);
        if (!ReferenceEquals(line, _lastLine)) { _spark.SetStyle(line, S.Grid, S.GraphLineWidth); _lastLine = line; }

        double window = GraphWindow;
        int n;
        if (_frameTimes)
        {
            n = ctx.Fps?.CopyFrameTimes(window, ref _t, ref _v) ?? 0;
            _spark.SetData(_t, _v, n, ctx.Now, window, double.NaN, 1000.0 / 60);
            return;
        }
        var series = Def != null ? ctx.Store.Series(Def.Id) ?? ctx.Store.EnsureSeries(Def.Id) : null;
        n = series?.CopySince(ctx.Now - window, ref _t, ref _v) ?? 0;
        double fixedMax = Def?.Kind is MetricKind.Percent or MetricKind.BatteryPercent ? 100 : double.NaN;
        double minAuto = Def?.Kind switch
        {
            MetricKind.Temperature => 60,
            MetricKind.DataRate => 128 * 1024,
            MetricKind.Fps => 60,
            MetricKind.Power => 10,
            _ => 1,
        };
        _spark.SetData(_t, _v, n, ctx.Now, window, fixedMax, minAuto);
    }
}

/// <summary>Per-logical-processor load bars.</summary>
public sealed class CoreGridElement : HudElement
{
    private readonly CoreBars _bars;
    private readonly TextBlock _summary;
    private HudRenderContext? _ctx;

    public CoreGridElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var stack = new StackPanel();
        var head = new DockPanel();
        var icon = Icons.Create("cores", S.Muted, S.IconSize * Fs);
        if (S.ShowIcons && C.ShowIcon) { icon.Margin = new Thickness(0, 0, 5, 0); head.Children.Add(icon); }
        if (ShowLabel) head.Children.Add(MakeLabel(string.IsNullOrEmpty(C.Label) ? "THREADS" : C.Label));
        _summary = MakeLabel("");
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        _summary.FontWeight = FontWeights.Normal;
        head.Children.Add(_summary);
        stack.Children.Add(head);
        _bars = new CoreBars { Height = Math.Max(10, (C.Height > 0 ? C.Height : 26) * Fs), Margin = new Thickness(0, 4, 0, 1), MinWidth = 120, Track = S.Track, Radius = S.BarStyle == Settings.BarStyle.Rounded ? 1.5 : 0 };
        _bars.BrushFor = v =>
        {
            var t = _ctx?.Settings.Thresholds;
            if (t == null) return S.Accent;
            return v >= t.UsageCritical ? S.Critical : v >= t.UsageWarn ? S.Warm : OverrideBrush ?? S.Accent;
        };
        stack.Children.Add(_bars);
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        _ctx = ctx;
        var cores = ctx.Store.GetObject<double[]>("cpu.cores");
        if (cores == null || cores.Length == 0) { SetText(_summary, MetricRegistry.NA); return; }
        _bars.SetValues(cores);
        int busy = cores.Count(v => v >= 50);
        SetText(_summary, $"{cores.Length} threads · {busy} busy · max {cores.Max():0}%");
    }
}
