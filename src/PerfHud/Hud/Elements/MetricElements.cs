using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PerfHud.Monitoring;
using PerfHud.Rendering;
using PerfHud.Settings;

namespace PerfHud.Hud.Elements;

/// <summary>"CPU  38%  62°C" style row. Also used for Percentage (adds a hairline bar).</summary>
public sealed class NumberElement : HudElement
{
    private readonly Run _warn = new(), _value = new(), _unit = new(), _gap = new("  "), _second = new(), _secondUnit = new();
    private readonly TextBlock _valueBlock;
    private readonly Border _tag;
    private readonly TextBlock _tagText;
    private readonly BarFill? _bar;
    private readonly FrameworkElement? _icon;

    public NumberElement(HudComponent c, HudStyle s, bool percentBar) : base(c, s)
    {
        bool above = S.LabelPosition == LabelPosition.Above;
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _icon = MakeIcon(S.Muted);
        if (_icon != null) { _icon.Margin = new Thickness(0, 0, 5, 0); left.Children.Add(_icon); }
        if (ShowLabel)
        {
            var lbl = MakeLabel(LabelText);
            if (!above) lbl.MinWidth = (Def?.IsText == true ? 52 : 34) * Fs * S.LabelSize / 10.5;
            else lbl.FontSize = S.SmallSize * Fs;
            left.Children.Add(lbl);
        }

        bool text = Def?.IsText == true;
        _valueBlock = MakeValueBlock((text ? S.ValueSize * 0.82 : S.ValueSize * (above ? 1.15 : 1)) * Fs);
        if (text) { _valueBlock.FontFamily = S.LabelFont; _valueBlock.FontWeight = FontWeights.Normal; }
        _valueBlock.Margin = new Thickness(!above && left.Children.Count > 0 ? 8 : 0, 0, 0, 0);
        _valueBlock.HorizontalAlignment = ValueAlign;
        _warn.FontFamily = new FontFamily("Segoe UI Symbol");
        foreach (var u in new[] { _unit, _secondUnit })
        {
            u.FontFamily = S.LabelFont; u.FontSize = S.SmallSize * Fs; u.Foreground = S.Muted; u.FontWeight = FontWeights.Normal;
        }
        _valueBlock.Inlines.AddRange(new Inline[] { _warn, _value, _unit, _gap, _second, _secondUnit });

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = ValueAlign };
        right.Children.Add(_valueBlock);
        _tag = MakeTag(out _tagText);
        right.Children.Add(_tag);

        UIElement row;
        if (above)
        {
            left.HorizontalAlignment = ValueAlign;
            var st = new StackPanel();
            if (left.Children.Count > 0) st.Children.Add(left);
            right.Margin = new Thickness(0, -1, 0, 0);
            st.Children.Add(right);
            row = st;
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(left);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            row = grid;
        }

        if (percentBar)
        {
            var stack = new StackPanel();
            stack.Children.Add(row);
            _bar = MakeBar(Math.Max(2, S.BarThickness * 0.6), 0);
            _bar.Margin = new Thickness(0, 3, 0, 1);
            stack.Children.Add(_bar);
            Child = stack;
        }
        else Child = row;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        SetRun(_warn, WarnGlyph(p.Warn));
        SetFg(_warn, p.Brush);
        SetRun(_value, p.Value);
        SetFg(_value, p.Brush);
        SetRun(_unit, Def?.Kind == MetricKind.Fps ? "" : UnitText(p.Unit)); // the label already says FPS
        bool hasSecond = !string.IsNullOrEmpty(p.Second);
        SetRun(_gap, hasSecond ? "  " : "");
        SetRun(_second, p.Second ?? "");
        if (p.SecondBrush != null) SetFg(_second, p.SecondBrush);
        SetRun(_secondUnit, UnitText(p.SecondUnit));
        UpdateTag(_tag, _tagText, p.Tag, p.Brush);
        if (_bar != null)
        {
            _bar.Fill = p.Brush;
            _bar.AnimateTo(p.Raw / (Def?.Max is double m && !double.IsNaN(m) ? m : 100), S.AnimMs);
        }
    }
}

/// <summary>Large hero number (e.g. "144 FPS").</summary>
public sealed class BigNumberElement : HudElement
{
    private readonly Run _warn = new(), _value = new(), _unit = new();
    private readonly TextBlock _sub;
    private readonly Border _tag;
    private readonly TextBlock _tagText;

    public BigNumberElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var stack = new StackPanel();
        var head = new DockPanel { LastChildFill = true };
        var icon = MakeIcon(S.Accent, 0.95);
        if (icon != null) { icon.Margin = new Thickness(0, 0, 5, 0); head.Children.Add(icon); }
        if (ShowLabel) head.Children.Add(MakeLabel(LabelText));
        _sub = MakeLabel("");
        _sub.FontWeight = FontWeights.Normal;
        _sub.HorizontalAlignment = HorizontalAlignment.Right;
        _sub.Margin = new Thickness(10, 0, 0, 0);
        _sub.MaxWidth = 140 * Fs;
        DockPanel.SetDock(_sub, Dock.Right);
        head.Children.Insert(0, _sub);
        stack.Children.Add(head);

        var vb = MakeValueBlock(S.BigSize * Fs);
        vb.Margin = new Thickness(0, -2, 0, -3);
        vb.HorizontalAlignment = ValueAlign;
        _warn.FontFamily = new FontFamily("Segoe UI Symbol"); _warn.FontSize = S.BigSize * Fs * 0.6;
        _unit.FontFamily = S.LabelFont; _unit.FontSize = S.LabelSize * Fs * 1.05; _unit.Foreground = S.Muted; _unit.FontWeight = S.LabelWeight;
        vb.Inlines.AddRange(new Inline[] { _warn, _value, _unit });
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = ValueAlign };
        line.Children.Add(vb);
        _tag = MakeTag(out _tagText);
        line.Children.Add(_tag);
        stack.Children.Add(line);
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        bool na = double.IsNaN(p.Raw) && !p.IsText;
        SetRun(_warn, WarnGlyph(p.Warn));
        SetFg(_warn, p.Brush);
        SetRun(_value, na ? "—" : p.Value);
        SetFg(_value, na ? S.Muted : (Def?.Kind == MetricKind.Fps && p.Severity == Severity.Normal ? (OverrideBrush ?? S.Text) : p.Brush));
        SetRun(_unit, na || !S.ShowUnits || C.HideUnit ? "" : " " + S.Case(p.Unit));
        string sub = "";
        if (p.Second?.TrimStart('•', ' ') is { Length: > 0 } sec) sub = sec;
        else if (na && Def?.Group == "FPS")
        {
            var reason = ctx.Store.GetReason(Def.Id) ?? "";
            sub = reason.Contains("Performance Log Users") || reason.Contains("admin") ? "needs permission · see Settings"
                : reason.Contains("disabled", StringComparison.OrdinalIgnoreCase) ? "disabled"
                : "no app rendering";
        }
        SetText(_sub, sub);
        UpdateTag(_tag, _tagText, p.Tag, p.Brush);
    }
}

/// <summary>Label + value on one line, bar underneath.</summary>
public sealed class ProgressBarElement : HudElement
{
    private readonly Run _value = new(), _unit = new();
    private readonly BarFill _bar;

    public ProgressBarElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var stack = new StackPanel();
        var head = new DockPanel();
        var icon = MakeIcon(S.Muted);
        if (icon != null) { icon.Margin = new Thickness(0, 0, 5, 0); head.Children.Add(icon); }
        if (ShowLabel) head.Children.Add(MakeLabel(LabelText));
        var vb = MakeValueBlock(S.ValueSize * Fs * 0.86);
        vb.HorizontalAlignment = HorizontalAlignment.Right;
        vb.Margin = new Thickness(12, 0, 0, 0);
        _unit.FontFamily = S.LabelFont; _unit.FontSize = S.SmallSize * Fs; _unit.Foreground = S.Muted; _unit.FontWeight = FontWeights.Normal;
        vb.Inlines.AddRange(new Inline[] { _value, _unit });
        head.Children.Add(vb);
        stack.Children.Add(head);
        _bar = MakeBar(Math.Max(2, S.BarThickness * Fs));
        _bar.Margin = new Thickness(0, 4, 0, 2);
        stack.Children.Add(_bar);
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        SetRun(_value, p.Value);
        SetFg(_value, p.Brush);
        SetRun(_unit, UnitText(p.Unit));

        double frac = double.NaN;
        if (Def != null && !double.IsNaN(p.Raw))
        {
            if (!double.IsNaN(Def.Max)) frac = p.Raw / Def.Max;
            else if (Def.MaxId != null) frac = p.Raw / ctx.Store.Get(Def.MaxId);
            else if (Def2 != null) frac = p.Raw / ctx.Store.Get(Def2.Id);
        }
        // Color bars by fill level (usage semantics) when no explicit severity applies.
        var brush = p.Severity is Severity.Neutral && !double.IsNaN(frac)
            ? (frac >= ctx.Settings.Thresholds.MemoryCritical / 100 ? S.Critical : frac >= ctx.Settings.Thresholds.MemoryWarn / 100 ? S.Warm : OverrideBrush ?? S.Accent)
            : p.Brush;
        _bar.Fill = brush;
        if (p.Severity is Severity.Neutral) SetFg(_value, ReferenceEquals(brush, S.Accent) ? S.Text : brush);
        _bar.AnimateTo(frac, S.AnimMs);
    }
}

/// <summary>Arc / ring / half-dial gauge with the value in the middle.</summary>
public sealed class GaugeElement : HudElement
{
    private readonly GaugeArc _arc;
    private readonly TextBlock _value, _second;

    public GaugeElement(HudComponent c, HudStyle s) : base(c, s)
    {
        bool half = S.GaugeStyle == GaugeStyle.Half;
        double size = Math.Max(40, (C.Height > 0 ? C.Height : 58) * Fs);
        var grid = new Grid { Width = size, Height = half ? size * 0.62 : size, HorizontalAlignment = HorizontalAlignment.Center };
        _arc = new GaugeArc
        {
            Track = S.Track, Thickness = Math.Max(3, size / 11), Kind = S.GaugeStyle,
            RoundCaps = S.BarStyle == BarStyle.Rounded,
        };
        grid.Children.Add(_arc);
        var mid = new StackPanel
        {
            VerticalAlignment = half ? VerticalAlignment.Bottom : VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _value = MakeValueBlock(size * 0.26);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        _second = MakeLabel("");
        _second.FontSize = S.SmallSize * Fs;
        _second.HorizontalAlignment = HorizontalAlignment.Center;
        _second.Margin = new Thickness(0, -2, 0, 0);
        mid.Children.Add(_value);
        if (!half) mid.Children.Add(_second);
        grid.Children.Add(mid);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(grid);
        if (half) { _second.Margin = new Thickness(0, 0, 0, 0); stack.Children.Add(_second); }
        if (ShowLabel)
        {
            var lbl = MakeLabel(LabelText);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.Margin = new Thickness(0, half ? 0 : -6, 0, 0);
            stack.Children.Add(lbl);
        }
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        bool na = double.IsNaN(p.Raw);
        SetText(_value, na ? "—" : p.Value);
        SetFg(_value, na ? S.Muted : S.Text);
        SetText(_second, p.Second != null ? $"{p.Second}{UnitText(p.SecondUnit).Trim()}" : S.ShowUnits ? p.Unit : "");
        if (p.SecondBrush != null) SetFg(_second, p.SecondBrush);
        _arc.Fill = p.Brush is SolidColorBrush b && ReferenceEquals(b, S.Normal) ? (OverrideBrush ?? S.Accent) : p.Brush;
        double max = Def?.Max is double m && !double.IsNaN(m) ? m : Def?.MaxId != null ? ctx.Store.Get(Def.MaxId) : 100;
        _arc.AnimateTo(p.Raw / max, S.AnimMs);
    }
}

/// <summary>Label + value with a tiny inline sparkline on the right (e.g. "CPU 38% ▁▂▅▃").</summary>
public sealed class TrendElement : HudElement
{
    private readonly Run _warn = new(), _value = new(), _unit = new();
    private readonly Sparkline _spark;
    private double[] _t = new double[64], _v = new double[64];
    private Brush? _lastLine;

    public TrendElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var dock = new DockPanel();
        double h = Math.Max(10, (C.Height > 0 ? C.Height : 16) * Fs);
        _spark = new Sparkline { Width = Math.Max(30, 64 * Fs), Height = h, Mode = S.GraphStyle, ShowGrid = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _spark.SetStyle(OverrideBrush ?? S.Accent, S.Grid, S.GraphLineWidth);
        DockPanel.SetDock(_spark, Dock.Right);
        dock.Children.Add(_spark);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = MakeIcon(S.Muted);
        if (icon != null) { icon.Margin = new Thickness(0, 0, 5, 0); left.Children.Add(icon); }
        if (ShowLabel)
        {
            var lbl = MakeLabel(LabelText);
            lbl.MinWidth = 34 * Fs * S.LabelSize / 10.5;
            lbl.Margin = new Thickness(0, 0, 8, 0);
            left.Children.Add(lbl);
        }
        var vb = MakeValueBlock(S.ValueSize * Fs);
        _warn.FontFamily = new FontFamily("Segoe UI Symbol");
        _unit.FontFamily = S.LabelFont; _unit.FontSize = S.SmallSize * Fs; _unit.Foreground = S.Muted; _unit.FontWeight = FontWeights.Normal;
        vb.Inlines.AddRange(new Inline[] { _warn, _value, _unit });
        left.Children.Add(vb);
        dock.Children.Add(left);
        Child = dock;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        SetRun(_warn, WarnGlyph(p.Warn));
        SetFg(_warn, p.Brush);
        SetRun(_value, p.Value);
        SetFg(_value, p.Brush);
        SetRun(_unit, Def?.Kind == MetricKind.Fps ? "" : UnitText(p.Unit));

        var line = OverrideBrush ?? (p.Severity >= Severity.Warm ? p.Brush : S.Accent);
        if (!ReferenceEquals(line, _lastLine)) { _spark.SetStyle(line, S.Grid, S.GraphLineWidth); _lastLine = line; }
        if (Def == null || Def.IsText) return;
        var series = ctx.Store.Series(Def.Id) ?? ctx.Store.EnsureSeries(Def.Id);
        int n = series?.CopySince(ctx.Now - GraphWindow, ref _t, ref _v) ?? 0;
        _spark.SetData(_t, _v, n, ctx.Now, GraphWindow, Def.Kind is MetricKind.Percent or MetricKind.BatteryPercent ? 100 : double.NaN);
    }
}

/// <summary>Min / average / max of a metric over the graph window.</summary>
public sealed class StatsElement : HudElement
{
    private readonly TextBlock _min, _avg, _max;
    private double[] _t = new double[64], _v = new double[64];

    public StatsElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 0) };
        var icon = MakeIcon(S.Muted);
        if (icon != null) { icon.Margin = new Thickness(0, 0, 5, 0); head.Children.Add(icon); }
        if (ShowLabel) head.Children.Add(MakeLabel(LabelText));
        grid.Children.Add(head);

        TextBlock Cell(int col, string caption)
        {
            var sp = new StackPanel { Margin = new Thickness(col == 1 ? 0 : 8, 0, 0, 0), HorizontalAlignment = ValueAlign };
            var cap = MakeLabel(caption);
            cap.FontSize = S.SmallSize * Fs * 0.9;
            sp.Children.Add(cap);
            var v = MakeValueBlock(S.ValueSize * Fs * 0.85);
            sp.Children.Add(v);
            Grid.SetColumn(sp, col);
            grid.Children.Add(sp);
            return v;
        }
        _min = Cell(1, "min");
        _avg = Cell(2, "avg");
        _max = Cell(3, "max");
        Child = grid;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        if (Def == null || Def.IsText) return;
        var series = ctx.Store.Series(Def.Id) ?? ctx.Store.EnsureSeries(Def.Id);
        int n = series?.CopySince(ctx.Now - GraphWindow, ref _t, ref _v) ?? 0;
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            double v = _v[i];
            if (double.IsNaN(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
            sum += v; k++;
        }
        void Show(TextBlock tb, double v)
        {
            if (k == 0) { SetText(tb, MetricRegistry.NA); SetFg(tb, S.Muted); return; }
            var (val, unit) = Fmt(Def, v, ctx.Settings);
            SetText(tb, val + UnitText(unit));
            SetFg(tb, OverrideBrush ?? S.ForSeverity(MetricRegistry.Evaluate(Def, v, ctx.Settings)));
        }
        Show(_min, min);
        Show(_avg, k > 0 ? sum / k : double.NaN);
        Show(_max, max);
    }
}

/// <summary>
/// Free-form line: "CPU {cpu.usage} · {cpu.temp}   GPU {gpu.usage}". Each {metric.id} is replaced with its live value
/// (colored by severity); {metric.id:v} shows the bare number without its unit.
/// </summary>
public sealed class TemplateElement : HudElement
{
    private static readonly Regex Token = new(@"\{([a-z0-9_.]+)(?::(v))?\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly List<(Run run, MetricDefinition def, bool bare)> _tokens = new();

    public TemplateElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var tb = MakeValueBlock(S.ValueSize * Fs * 0.9);
        tb.HorizontalAlignment = ValueAlign;
        tb.TextTrimming = TextTrimming.None;
        var text = string.IsNullOrEmpty(C.Text) ? "CPU {cpu.usage}  GPU {gpu.usage}" : C.Text;
        int pos = 0;
        foreach (Match m in Token.Matches(text))
        {
            if (m.Index > pos) tb.Inlines.Add(StaticRun(text[pos..m.Index]));
            var def = MetricRegistry.Get(m.Groups[1].Value);
            if (def == null) tb.Inlines.Add(StaticRun(m.Value));
            else
            {
                var r = new Run();
                _tokens.Add((r, def, m.Groups[2].Success));
                tb.Inlines.Add(r);
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) tb.Inlines.Add(StaticRun(text[pos..]));
        Child = tb;
    }

    private Run StaticRun(string s) => new(s)
    {
        Foreground = OverrideBrush ?? S.Muted,
        FontFamily = S.LabelFont,
        FontWeight = S.LabelWeight,
        FontSize = S.LabelSize * Fs * 1.05,
    };

    public override void Refresh(HudRenderContext ctx)
    {
        foreach (var (run, def, bare) in _tokens)
        {
            if (def.IsText)
            {
                SetRun(run, ctx.Store.GetText(def.Id) is { Length: > 0 } t ? t : MetricRegistry.NA);
                SetFg(run, S.Text);
                continue;
            }
            var v = ctx.Store.Get(def.Id);
            var (val, unit) = Fmt(def, v, ctx.Settings);
            SetRun(run, bare || double.IsNaN(v) ? val : val + UnitText(unit));
            // Calm values read as plain text inside a sentence; only warm and above get colored.
            var sev = MetricRegistry.Evaluate(def, v, ctx.Settings);
            SetFg(run, double.IsNaN(v) ? S.Muted : sev >= Severity.Warm ? S.ForSeverity(sev) : S.Text);
        }
    }
}
