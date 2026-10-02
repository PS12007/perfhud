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
    public double CellPadX, CellPadY;
    public bool ShowIcons, ShowLabels, Compact, HighContrast;
    public double AnimMs;
    public HudAlignment Align;
    public int GraphSeconds;
    public double CornerRadius;

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
        return new HudStyle
        {
            Background = ColorUtil.Brush(bg, hc ? 1 : a.BackgroundOpacity),
            Panel = ColorUtil.Brush(a.Panel, Colors.Black, hc ? 1 : 0.6),
            PanelBorder = ColorUtil.Brush(hc ? Colors.White : Color.FromRgb(255, 255, 255), hc ? 0.9 : (a.Border ? 0.09 : 0)),
            Text = ColorUtil.Brush(text),
            Muted = ColorUtil.Brush(muted),
            Accent = ColorUtil.Brush(accent),
            Cool = ColorUtil.Brush(cool),
            Normal = ColorUtil.Brush(ok),
            Warm = ColorUtil.Brush(warm),
            Hot = ColorUtil.Brush(hot),
            Critical = ColorUtil.Brush(crit),
            Track = ColorUtil.Brush(Colors.White, hc ? 0.35 : 0.08),
            Grid = ColorUtil.Brush(Colors.White, hc ? 0.3 : 0.06),
            LabelFont = new FontFamily(string.IsNullOrWhiteSpace(a.LabelFont) ? "Segoe UI" : a.LabelFont + ", Segoe UI"),
            ValueFont = new FontFamily(string.IsNullOrWhiteSpace(a.ValueFont) ? "Bahnschrift" : a.ValueFont + ", Bahnschrift, Segoe UI"),
            LabelSize = 10.5 * fs,
            ValueSize = 15 * fs,
            BigSize = 30 * fs,
            SmallSize = 9.5 * fs,
            IconSize = 12 * fs,
            CellPadX = compact ? 5 : 7,
            CellPadY = compact ? 1 : 2.5,
            ShowIcons = a.ShowIcons,
            ShowLabels = a.ShowLabels,
            Compact = compact,
            HighContrast = hc,
            AnimMs = reduceAnimations ? 0 : 180 * a.AnimationSpeed,
            Align = s.Hud.Alignment,
            GraphSeconds = s.Performance.GraphHistorySeconds,
            CornerRadius = a.CornerRadius,
        };
    }
}
