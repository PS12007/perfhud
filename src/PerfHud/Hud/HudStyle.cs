using System.Windows;
using System.Windows.Media;
using PerfHud.Monitoring;
using PerfHud.Settings;

namespace PerfHud.Hud;

public static class ColorUtil
{
    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(hex.Trim()); }
        catch { return fallback; }
    }

    public static SolidColorBrush Brush(Color c, double alpha = 1)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public static SolidColorBrush Brush(string? hex, Color fallback, double alpha = 1) => Brush(Parse(hex, fallback), alpha);

    public static bool IsValidHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { ColorConverter.ConvertFromString(hex.Trim()); return true; } catch { return false; }
    }
}

/// <summary>Immutable snapshot of resolved brushes, fonts and metrics used to build HUD elements.</summary>
public sealed class HudStyle
{
    public required SolidColorBrush Background, PanelBorder, Text, Muted, Accent, Cool, Normal, Warm, Hot, Critical, Track, Grid, Panel;
    public required FontFamily LabelFont, ValueFont;
    public double LabelSize, ValueSize, BigSize, SmallSize, IconSize;
    public double CellPadX, CellPadY, PanelPadding;
    public bool ShowIcons, ShowLabels, Compact, HighContrast;
    public double AnimMs;
    public HudAlignment Align;
    public int GraphSeconds;
    public double CornerRadius;

    public PanelStyle PanelStyle;
    public PanelEdge PanelEdge;
    public double BorderWidth;
    public bool TextShadow, ShowUnits = true, ShowWarnGlyph = true, ShowTags = true;
    public BarStyle BarStyle;
    public double BarThickness = 4, GraphLineWidth = 1.4;
    public GraphStyle GraphStyle;
    public GaugeStyle GaugeStyle;
    public LabelCase LabelCase;
    public LabelPosition LabelPosition;
    public HeaderStyle HeaderStyle;
    public FontWeight ValueWeight = FontWeights.SemiBold, LabelWeight = FontWeights.SemiBold;

    public bool LightBackground => Background.Color.R * 0.299 + Background.Color.G * 0.587 + Background.Color.B * 0.114 > 150;

    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
        { "CPU", "GPU", "RAM", "VRAM", "FPS", "SSD", "OS", "BIOS", "HDR", "OEM", "IP", "C/T", "MEM", "AVG", "1%", "0.1%" };

    /// <summary>Applies the configured label case to a label/header text.</summary>
    public string Case(string s) => LabelCase switch
    {
        LabelCase.Upper => s.ToUpperInvariant(),
        LabelCase.Lower => s.ToLowerInvariant(),
        LabelCase.Title => string.Join(' ', s.Split(' ').Select(w =>
            w.Length == 0 || Acronyms.Contains(w) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant())),
        _ => s,
    };

    /// <summary>Copy with no panel behind the values (floating panels with the background turned off).</summary>
    public HudStyle WithoutPanel()
    {
        var c = (HudStyle)MemberwiseClone();
        var bg = Background.Color;
        c.Background = ColorUtil.Brush(Color.FromRgb(bg.R, bg.G, bg.B), 0.004); // ~0 keeps it hit-testable for dragging
        c.PanelBorder = ColorUtil.Brush(Colors.Transparent);
        c.PanelStyle = PanelStyle.Bare;
        c.PanelEdge = PanelEdge.None;
        return c;
    }

    public static FontWeight Weight(WeightOption w) => w switch
    {
        WeightOption.Light => FontWeights.Light,
        WeightOption.Regular => FontWeights.Normal,
        WeightOption.Medium => FontWeights.Medium,
        WeightOption.Bold => FontWeights.Bold,
        _ => FontWeights.SemiBold,
    };

    public SolidColorBrush ForSeverity(Severity s) => s switch
    {
        Severity.Unavailable => Muted,
        Severity.Cool => Cool,
        Severity.Normal => Normal,
        Severity.Warm => Warm,
        Severity.Hot => Hot,
        Severity.Critical => Critical,
        _ => Text,
    };

    public static HudStyle From(AppSettings s, bool compact, bool reduceAnimations)
    {
        var a = s.Appearance;
        bool hc = a.HighContrast || SystemParameters.HighContrast;
        var text = ColorUtil.Parse(a.Text, Colors.White);
        var muted = ColorUtil.Parse(a.Muted, Colors.Gray);
        var bg = ColorUtil.Parse(a.Background, Colors.Black);
        var accent = ColorUtil.Parse(a.Accent, Colors.Cyan);
        Color cool = ColorUtil.Parse(a.Cool, accent), ok = ColorUtil.Parse(a.Success, Colors.LightGreen),
            warm = ColorUtil.Parse(a.Warning, Colors.Gold), hot = ColorUtil.Parse(a.Hot, Colors.Orange), crit = ColorUtil.Parse(a.Danger, Colors.Red);

        if (a.ColorVision == ColorVisionMode.ColorBlindSafe)
        {
            var p = ThemeDefinition.ColorBlindSafe;
            cool = ColorUtil.Parse(p.Cool, cool); ok = ColorUtil.Parse(p.Ok, ok); warm = ColorUtil.Parse(p.Warm, warm);
            hot = ColorUtil.Parse(p.Hot, hot); crit = ColorUtil.Parse(p.Critical, crit);
        }
        if (hc)
        {
            text = Colors.White; muted = Color.FromRgb(0xD0, 0xD0, 0xD0); bg = Colors.Black;
        }

        double fs = a.FontScale * (compact ? 0.92 : 1.0);
        var panelStyle = hc ? PanelStyle.Solid : a.PanelStyle;
        double bgAlpha = hc ? 1 : panelStyle switch { PanelStyle.Solid => a.BackgroundOpacity, _ => 0.004 }; // ~0 keeps the panel hit-testable for dragging
        Color borderColor = hc ? Colors.White
            : ColorUtil.IsValidHex(a.BorderColor) ? ColorUtil.Parse(a.BorderColor, text)
            : text;
        double borderAlpha = hc ? 0.9
            : !a.Border || panelStyle == PanelStyle.Bare ? 0
            : ColorUtil.IsValidHex(a.BorderColor) ? 1
            : panelStyle == PanelStyle.Outline ? 0.5 : 0.12;

        var textBrush = ColorUtil.Brush(text);
        return new HudStyle
        {
            Background = ColorUtil.Brush(bg, bgAlpha),
            Panel = ColorUtil.Brush(a.Panel, Colors.Black, hc ? 1 : 0.6),
            PanelBorder = ColorUtil.Brush(borderColor, borderAlpha),
            Text = textBrush,
            Muted = ColorUtil.Brush(muted),
            Accent = ColorUtil.Brush(accent),
            // Severity coloring off: calm states use the text color; warm and above still stand out.
            Cool = a.ColorBySeverity || hc ? ColorUtil.Brush(cool) : textBrush,
            Normal = a.ColorBySeverity || hc ? ColorUtil.Brush(ok) : textBrush,
            Warm = ColorUtil.Brush(warm),
            Hot = ColorUtil.Brush(hot),
            Critical = ColorUtil.Brush(crit),
            Track = ColorUtil.Brush(text, hc ? 0.35 : 0.12),
            Grid = ColorUtil.Brush(text, hc ? 0.3 : 0.08),
            LabelFont = new FontFamily(string.IsNullOrWhiteSpace(a.LabelFont) ? "Segoe UI" : a.LabelFont + ", Segoe UI"),
            ValueFont = new FontFamily(string.IsNullOrWhiteSpace(a.ValueFont) ? "Bahnschrift" : a.ValueFont + ", Bahnschrift, Segoe UI"),
            LabelSize = 10.5 * fs,
            ValueSize = 15 * fs,
            BigSize = 30 * fs,
            SmallSize = 9.5 * fs,
            IconSize = 12 * fs,
            CellPadX = Math.Clamp(a.ColumnSpacing, 0, 30) * (compact ? 0.7 : 1),
            CellPadY = Math.Clamp(a.RowSpacing, 0, 20) * (compact ? 0.4 : 1),
            PanelPadding = Math.Clamp(a.PanelPadding, 0, 40) * (compact ? 0.65 : 1),
            ShowIcons = a.ShowIcons,
            ShowLabels = a.ShowLabels,
            Compact = compact,
            HighContrast = hc,
            AnimMs = reduceAnimations ? 0 : 180 * a.AnimationSpeed,
            Align = s.Hud.Alignment,
            GraphSeconds = s.Performance.GraphHistorySeconds,
            CornerRadius = a.CornerRadius,
            PanelStyle = panelStyle,
            PanelEdge = hc ? PanelEdge.None : a.PanelEdge,
            BorderWidth = hc ? 2 : Math.Clamp(a.BorderWidth, 0, 6),
            TextShadow = a.TextShadow && !hc,
            ShowUnits = a.ShowUnits,
            ShowWarnGlyph = a.ShowWarningGlyph || hc,
            ShowTags = a.ShowWarningTags || hc,
            BarStyle = a.BarStyle,
            BarThickness = Math.Clamp(a.BarThickness, 1, 16),
            GraphStyle = a.GraphStyle,
            GraphLineWidth = Math.Clamp(a.GraphLineWidth, 0.5, 5),
            GaugeStyle = a.GaugeStyle,
            LabelCase = a.LabelCase,
            LabelPosition = a.LabelPosition,
            HeaderStyle = a.HeaderStyle,
            ValueWeight = Weight(a.ValueWeight),
            LabelWeight = Weight(a.LabelWeight),
        };
    }
}
