using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PerfHud.Monitoring;
using PerfHud.Monitoring.Monitors;
using PerfHud.Settings;

namespace PerfHud.Hud.Elements;

public sealed class HudRenderContext
{
    public required MetricStore Store { get; init; }
    public required AppSettings Settings { get; init; }
    public required HudStyle Style { get; init; }
    public FpsMonitor? Fps { get; init; }
    public double Now { get; set; }
    public bool Paused { get; set; }
}

/// <summary>Base class for HUD cells. Builds its visual tree once; <see cref="Refresh"/> only updates changed text/brushes.</summary>
public abstract class HudElement : Border
{
    protected readonly HudComponent C;
    protected readonly HudStyle S;
    protected readonly MetricDefinition? Def;
    protected readonly MetricDefinition? Def2;

    protected HudElement(HudComponent c, HudStyle s)
    {
        C = c;
        S = s;
        Def = MetricRegistry.Get(c.MetricId);
        Def2 = MetricRegistry.Get(c.SecondaryMetricId);
        Padding = new Thickness(S.CellPadX, S.CellPadY, S.CellPadX, S.CellPadY);
        if (c.Width > 0) MinWidth = c.Width;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public HudComponent Component => C;

    public abstract void Refresh(HudRenderContext ctx);

    protected double Fs => C.FontScale;

    protected string LabelText => !string.IsNullOrEmpty(C.Label) ? C.Label : Def?.Short ?? C.Type.ToString().ToUpperInvariant();
    protected string? IconName => !string.IsNullOrEmpty(C.Icon) ? C.Icon : Def?.Icon;
    protected bool ShowIcon => S.ShowIcons && C.ShowIcon && !string.IsNullOrEmpty(IconName);
    protected bool ShowLabel => S.ShowLabels && C.ShowLabel;

    protected SolidColorBrush? OverrideBrush => string.IsNullOrEmpty(C.Color) ? null : ColorUtil.Brush(C.Color, Colors.White);

    protected TextBlock MakeLabel(string text, Brush? brush = null) => new()
    {
        Text = text,
        FontFamily = S.LabelFont,
        FontSize = S.LabelSize * Fs,
        FontWeight = FontWeights.SemiBold,
        Foreground = brush ?? S.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    protected TextBlock MakeValueBlock(double size) => new()
    {
        FontFamily = S.ValueFont,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Foreground = S.Text,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    protected FrameworkElement? MakeIcon(Brush brush, double sizeMul = 1) =>
        ShowIcon ? Rendering.Icons.Create(IconName, brush, S.IconSize * Fs * sizeMul, 2.1) : null;

    protected static void SetRun(Run r, string text) { if (r.Text != text) r.Text = text; }
    protected static void SetFg(System.Windows.Documents.TextElement e, Brush b) { if (!ReferenceEquals(e.Foreground, b)) e.Foreground = b; }
    protected static void SetFg(TextBlock e, Brush b) { if (!ReferenceEquals(e.Foreground, b)) e.Foreground = b; }
    protected static void SetText(TextBlock t, string s) { if (t.Text != s) t.Text = s; }

    protected HorizontalAlignment ValueAlign => S.Align switch
    {
        HudAlignment.Right => HorizontalAlignment.Right,
        HudAlignment.Center => HorizontalAlignment.Center,
        _ => HorizontalAlignment.Left,
    };

    /// <summary>Formatted primary value with severity, unit and optional secondary part.</summary>
    protected ValueParts Compose(HudRenderContext ctx)
    {
        var p = new ValueParts();
        if (Def == null) { p.Value = "?"; p.Brush = S.Muted; return p; }
        var store = ctx.Store;
        if (Def.IsText)
        {
            var t = store.GetText(Def.Id);
            p.Value = string.IsNullOrEmpty(t) ? MetricRegistry.NA : t;
            p.Brush = string.IsNullOrEmpty(t) ? S.Muted : (OverrideBrush ?? S.Text);
            p.IsText = true;
        }
        else
        {
            var v = store.Get(Def.Id);
            (p.Value, p.Unit) = MetricRegistry.Format(Def, v, ctx.Settings);
            p.Severity = MetricRegistry.Evaluate(Def, v, ctx.Settings);
            p.Brush = double.IsNaN(v) ? S.Muted : OverrideBrush ?? S.ForSeverity(p.Severity);
            p.Raw = v;
            p.Tag = MetricRegistry.WarningTag(Def, p.Severity, store);
            p.Warn = p.Severity >= Severity.Hot || (p.Severity == Severity.Warm && Def.Kind is MetricKind.Fps or MetricKind.BatteryPercent) || p.Tag == "THERMAL";
        }

        if (Def2 != null)
        {
            if (Def2.IsText)
            {
                var t2 = store.GetText(Def2.Id);
                if (!string.IsNullOrEmpty(t2)) { p.Second = $"• {t2}"; p.SecondBrush = S.Muted; }
            }
            else
            {
                var v2 = store.Get(Def2.Id);
                if (C.Ratio && !double.IsNaN(v2) && !double.IsNaN(p.Raw))
                {
                    var (sv, su) = MetricRegistry.Format(Def2, v2, ctx.Settings);
                    p.Value = $"{p.Value}/{sv}";
                    p.Unit = su;
                }
                else if (!C.Ratio)
                {
                    var (sv, su) = MetricRegistry.Format(Def2, v2, ctx.Settings);
                    var sev2 = MetricRegistry.Evaluate(Def2, v2, ctx.Settings);
                    p.Second = sv;
                    p.SecondUnit = su;
                    p.SecondBrush = double.IsNaN(v2) ? S.Muted : S.ForSeverity(sev2);
                    var tag2 = MetricRegistry.WarningTag(Def2, sev2, store);
                    if (p.Tag == null && tag2 != null) p.Tag = tag2;
                    if (sev2 >= Severity.Hot) p.Warn = true;
                }
            }
        }
        if (ctx.Paused && !p.IsText) p.Brush = S.Muted;
        return p;
    }

    protected sealed class ValueParts
    {
        public string Value = "", Unit = "";
        public string? Second, SecondUnit, Tag;
        public Brush Brush = Brushes.White;
        public Brush? SecondBrush;
        public Severity Severity;
        public double Raw = double.NaN;
        public bool Warn, IsText;
    }

    /// <summary>Small bordered pill (e.g. "THERMAL", "LOW") so warnings never rely on color alone.</summary>
    protected Border MakeTag(out TextBlock text)
    {
        text = new TextBlock { FontFamily = S.LabelFont, FontSize = S.SmallSize * Fs * 0.92, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        return new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
    }

    protected static void UpdateTag(Border tag, TextBlock text, string? value, Brush brush)
    {
        if (string.IsNullOrEmpty(value)) { if (tag.Visibility != Visibility.Collapsed) tag.Visibility = Visibility.Collapsed; return; }
        if (tag.Visibility != Visibility.Visible) tag.Visibility = Visibility.Visible;
        SetText(text, value);
        SetFg(text, brush);
        if (!ReferenceEquals(tag.BorderBrush, brush)) tag.BorderBrush = brush;
    }

    protected double GraphWindow => C.GraphSeconds > 0 ? C.GraphSeconds : S.GraphSeconds;
}
