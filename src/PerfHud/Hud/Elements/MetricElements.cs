using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PerfHud.Monitoring;
using PerfHud.Rendering;

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
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _icon = MakeIcon(S.Muted);
        if (_icon != null) { _icon.Margin = new Thickness(0, 0, 5, 0); left.Children.Add(_icon); }
        if (ShowLabel)
        {
            var lbl = MakeLabel(LabelText);
            lbl.MinWidth = (Def?.IsText == true ? 52 : 34) * Fs * S.LabelSize / 10.5;
            left.Children.Add(lbl);
        }
        grid.Children.Add(left);

        bool text = Def?.IsText == true;
        _valueBlock = MakeValueBlock((text ? S.ValueSize * 0.82 : S.ValueSize) * Fs);
        if (text) { _valueBlock.FontFamily = S.LabelFont; _valueBlock.FontWeight = FontWeights.Normal; }
        _valueBlock.Margin = new Thickness(left.Children.Count > 0 ? 8 : 0, 0, 0, 0);
        _valueBlock.HorizontalAlignment = ValueAlign;
        _warn.FontFamily = new FontFamily("Segoe UI Symbol");
        _unit.FontFamily = S.LabelFont; _unit.FontSize = S.SmallSize * Fs; _unit.Foreground = S.Muted; _unit.FontWeight = FontWeights.Normal;
        _secondUnit.FontFamily = S.LabelFont; _secondUnit.FontSize = S.SmallSize * Fs; _secondUnit.Foreground = S.Muted; _secondUnit.FontWeight = FontWeights.Normal;
        _valueBlock.Inlines.AddRange(new Inline[] { _warn, _value, _unit, _gap, _second, _secondUnit });

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = ValueAlign };
        right.Children.Add(_valueBlock);
        _tag = MakeTag(out _tagText);
        right.Children.Add(_tag);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        if (percentBar)
        {
            var stack = new StackPanel();
            stack.Children.Add(grid);
            _bar = new BarFill { Height = 2.5, Margin = new Thickness(0, 3, 0, 1), Track = S.Track };
            stack.Children.Add(_bar);
            Child = stack;
        }
        else Child = grid;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        SetRun(_warn, p.Warn ? "⚠ " : "");
        SetFg(_warn, p.Brush);
        SetRun(_value, p.Value);
        SetFg(_value, p.Brush);
        var unit = Def?.Kind == MetricKind.Fps ? "" : p.Unit; // the label already says FPS
        SetRun(_unit, unit.Length > 0 && unit != "%" && unit != "°C" && unit != "°F" ? " " + unit : unit);
        bool hasSecond = !string.IsNullOrEmpty(p.Second);
        SetRun(_gap, hasSecond ? "  " : "");
        SetRun(_second, p.Second ?? "");
        if (p.SecondBrush != null) SetFg(_second, p.SecondBrush);
        var su = p.SecondUnit ?? "";
        SetRun(_secondUnit, su.Length > 0 && su != "%" && su != "°C" && su != "°F" ? " " + su : su);
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
        _unit.FontFamily = S.LabelFont; _unit.FontSize = S.LabelSize * Fs * 1.05; _unit.Foreground = S.Muted; _unit.FontWeight = FontWeights.SemiBold;
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
        SetRun(_warn, p.Warn ? "⚠ " : "");
        SetFg(_warn, p.Brush);
        SetRun(_value, na ? "—" : p.Value);
        SetFg(_value, na ? S.Muted : (Def?.Kind == MetricKind.Fps && p.Severity == Severity.Normal ? (OverrideBrush ?? S.Text) : p.Brush));
        SetRun(_unit, na ? "" : " " + p.Unit);
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

/// <summary>Label + value on one line, rounded bar underneath.</summary>
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
        _bar = new BarFill { Height = Math.Max(3, 4.5 * Fs), Margin = new Thickness(0, 4, 0, 2), Track = S.Track, MinWidth = 90 };
        stack.Children.Add(_bar);
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        SetRun(_value, p.Value);
        SetFg(_value, p.Brush);
        SetRun(_unit, p.Unit.Length > 0 && p.Unit != "%" ? " " + p.Unit : p.Unit);

        double frac = double.NaN;
        if (Def != null && !double.IsNaN(p.Raw))
        {
            if (!double.IsNaN(Def.Max)) frac = p.Raw / Def.Max;
            else if (Def.MaxId != null) frac = p.Raw / ctx.Store.Get(Def.MaxId);
            else if (Def2 != null) frac = p.Raw / ctx.Store.Get(Def2.Id);
        }
        // Color bars by fill level (usage semantics) when no explicit severity applies.
        var brush = p.Severity is Monitoring.Severity.Neutral && !double.IsNaN(frac)
            ? (frac >= ctx.Settings.Thresholds.MemoryCritical / 100 ? S.Critical : frac >= ctx.Settings.Thresholds.MemoryWarn / 100 ? S.Warm : OverrideBrush ?? S.Accent)
            : p.Brush;
        _bar.Fill = brush;
        if (p.Severity is Monitoring.Severity.Neutral) SetFg(_value, ReferenceEquals(brush, S.Accent) ? S.Text : brush);
        _bar.AnimateTo(frac, S.AnimMs);
    }
}

/// <summary>Arc gauge with the value in the middle.</summary>
public sealed class GaugeElement : HudElement
{
    private readonly GaugeArc _arc;
    private readonly TextBlock _value, _second;

    public GaugeElement(HudComponent c, HudStyle s) : base(c, s)
    {
        double size = Math.Max(40, (C.Height > 0 ? C.Height : 58) * Fs);
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        _arc = new GaugeArc { Track = S.Track, Thickness = Math.Max(3, size / 11) };
        grid.Children.Add(_arc);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _value = MakeValueBlock(size * 0.26);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        _second = MakeLabel("");
        _second.FontSize = S.SmallSize * Fs;
        _second.HorizontalAlignment = HorizontalAlignment.Center;
        _second.Margin = new Thickness(0, -2, 0, 0);
        mid.Children.Add(_value);
        mid.Children.Add(_second);
        grid.Children.Add(mid);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(grid);
        if (ShowLabel)
        {
            var lbl = MakeLabel(LabelText);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.Margin = new Thickness(0, -6, 0, 0);
            stack.Children.Add(lbl);
        }
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var p = Compose(ctx);
        bool na = double.IsNaN(p.Raw);
        SetText(_value, na ? "—" : p.Value + (p.Unit == "%" ? "" : ""));
        SetFg(_value, na ? S.Muted : S.Text);
        SetText(_second, p.Second != null ? $"{p.Second}{p.SecondUnit}" : p.Unit == "%" ? "%" : p.Unit);
        if (p.SecondBrush != null) SetFg(_second, p.SecondBrush);
        _arc.Fill = p.Brush is SolidColorBrush b && ReferenceEquals(b, S.Normal) ? (OverrideBrush ?? S.Accent) : p.Brush;
        double max = Def?.Max is double m && !double.IsNaN(m) ? m : Def?.MaxId != null ? ctx.Store.Get(Def.MaxId) : 100;
        _arc.AnimateTo(p.Raw / max, S.AnimMs);
    }
}
