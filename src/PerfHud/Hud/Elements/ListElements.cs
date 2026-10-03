using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PerfHud.Monitoring;
using PerfHud.Monitoring.Monitors;
using PerfHud.Rendering;
using PerfHud.Settings;

namespace PerfHud.Hud.Elements;

/// <summary>Every available temperature sensor, color-coded with warning glyphs.</summary>
public sealed class SensorListElement : HudElement
{
    private readonly StackPanel _rows = new();
    private readonly List<(TextBlock name, TextBlock value)> _cells = new();
    private readonly TextBlock _empty;

    public SensorListElement(HudComponent c, HudStyle s) : base(c, s)
    {
        _empty = MakeLabel("No temperature sensors accessible");
        _empty.FontWeight = FontWeights.Normal;
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.MaxWidth = 260;
        var stack = new StackPanel();
        stack.Children.Add(_rows);
        stack.Children.Add(_empty);
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        var list = ctx.Store.GetObject<List<SensorReading>>("temps.all") ?? new();
        int max = Math.Max(1, C.RowSpan * 2);
        int n = Math.Min(list.Count, max);
        _empty.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (n == 0)
        {
            var reason = ctx.Store.GetReason("cpu.temp");
            SetText(_empty, reason ?? "No temperature sensors accessible");
        }

        while (_cells.Count < n)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0.5, 0, 0.5) };
            var v = MakeValueBlock(S.ValueSize * Fs * 0.82);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            v.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(v, Dock.Right);
            var name = MakeLabel("");
            name.FontWeight = FontWeights.Normal;
            name.Foreground = S.Text;
            row.Children.Add(v);
            row.Children.Add(name);
            _rows.Children.Add(row);
            _cells.Add((name, v));
        }
        while (_cells.Count > n)
        {
            _rows.Children.RemoveAt(_rows.Children.Count - 1);
            _cells.RemoveAt(_cells.Count - 1);
        }

        for (int i = 0; i < n; i++)
        {
            var r = list[i];
            var def = new MetricDefinition("temp", r.Name, r.Name, MetricKind.Temperature, "Temperature", Temp: r.Category);
            var sev = MetricRegistry.Evaluate(def, r.Value, ctx.Settings);
            var (val, unit) = MetricRegistry.Format(def, r.Value, ctx.Settings);
            SetText(_cells[i].name, r.Name);
            SetText(_cells[i].value, WarnGlyph(sev >= Severity.Hot) + val + (S.ShowUnits ? unit : ""));
            SetFg(_cells[i].value, S.ForSeverity(sev));
        }
    }
}

/// <summary>Per-drive usage, throughput and temperature.</summary>
public sealed class DriveListElement : HudElement
{
    private readonly StackPanel _rows = new();
    private readonly List<(TextBlock title, TextBlock space, BarFill bar, TextBlock detail)> _cells = new();

    public DriveListElement(HudComponent c, HudStyle s) : base(c, s) => Child = _rows;

    public override void Refresh(HudRenderContext ctx)
    {
        var drives = ctx.Store.GetObject<List<DriveSnapshot>>("disk.drives") ?? new();
        while (_cells.Count < drives.Count)
        {
            var box = new StackPanel { Margin = new Thickness(0, _cells.Count == 0 ? 0 : 5, 0, 0) };
            var head = new DockPanel();
            var space = MakeValueBlock(S.ValueSize * Fs * 0.78);
            space.HorizontalAlignment = HorizontalAlignment.Right;
            space.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(space, Dock.Right);
            var title = MakeLabel("", S.Text);
            head.Children.Add(space);
            head.Children.Add(title);
            box.Children.Add(head);
            var bar = MakeBar(Math.Max(2, S.BarThickness * 0.85), 120);
            bar.Margin = new Thickness(0, 3, 0, 2);
            box.Children.Add(bar);
            var detail = MakeLabel("");
            detail.FontWeight = FontWeights.Normal;
            detail.FontSize = S.SmallSize * Fs;
            box.Children.Add(detail);
            _rows.Children.Add(box);
            _cells.Add((title, space, bar, detail));
        }
        while (_cells.Count > drives.Count)
        {
            _rows.Children.RemoveAt(_rows.Children.Count - 1);
            _cells.RemoveAt(_cells.Count - 1);
        }

        var t = ctx.Settings.Thresholds;
        for (int i = 0; i < drives.Count; i++)
        {
            var d = drives[i];
            var (title, space, bar, detail) = _cells[i];
            SetText(title, $"{d.Letter} {d.Label}{(d.Model != null ? " · " + d.Model : "")}");
            double used = d.Total - d.Free, frac = d.Total > 0 ? used / d.Total : 0;
            SetText(space, $"{Gb(used)}/{Gb(d.Total)} GB");
            bar.Fill = frac * 100 >= t.MemoryCritical ? S.Critical : frac * 100 >= t.MemoryWarn ? S.Warm : S.Accent;
            bar.AnimateTo(frac, S.AnimMs);
            var (r, ru) = MetricRegistry.FormatBytesRate(d.Read);
            var (w, wu) = MetricRegistry.FormatBytesRate(d.Write);
            string temp = "";
            if (!double.IsNaN(d.Temp))
            {
                var def = new MetricDefinition("t", "", "", MetricKind.Temperature, "Storage", Temp: TempCategory.Storage);
                var (tv, tu) = MetricRegistry.Format(def, d.Temp, ctx.Settings);
                temp = $" · {(MetricRegistry.Evaluate(def, d.Temp, ctx.Settings) >= Severity.Hot ? "⚠ " : "")}{tv}{tu}";
            }
            SetText(detail, $"R {r} {ru} · W {w} {wu} · {d.Active:0}% busy · {Gb(d.Free)} GB free{temp}");
        }
    }

    private static string Gb(double bytes)
    {
        var g = bytes / (1024d * 1024 * 1024);
        return g >= 100 ? g.ToString("0") : g.ToString("0.0");
    }
}

/// <summary>Static text / section header (with optional subtitle metric on the right).</summary>
public sealed class HeaderElement : HudElement
{
    private readonly TextBlock? _subtitle;
    private readonly MetricDefinition? _subDef;

    public HeaderElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var brush = OverrideBrush ?? S.Accent;
        bool tape = S.HeaderStyle == HeaderStyle.Tape;
        // Tape headers print the title knocked out of a solid accent block.
        var bgc = S.Background.Color;
        var titleBrush = tape ? ColorUtil.Brush(Color.FromRgb(bgc.R, bgc.G, bgc.B)) : brush;
        var dock = new DockPanel { Margin = new Thickness(0, C.Row == 0 ? 0 : 5 * Fs, 0, 1) };
        _subDef = MetricRegistry.Get(C.SecondaryMetricId);
        if (_subDef != null)
        {
            _subtitle = MakeLabel("");
            _subtitle.FontWeight = FontWeights.Normal;
            _subtitle.HorizontalAlignment = HorizontalAlignment.Right;
            _subtitle.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(_subtitle, Dock.Right);
            dock.Children.Add(_subtitle);
        }

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        if (S.ShowIcons && C.ShowIcon && !string.IsNullOrEmpty(C.Icon))
        {
            var icon = Icons.Create(C.Icon, titleBrush, S.IconSize * Fs);
            icon.Margin = new Thickness(0, 0, 6, 0);
            head.Children.Add(icon);
        }
        var title = MakeLabel(string.IsNullOrEmpty(C.Text) ? "TEXT" : C.Text, titleBrush);
        title.FontWeight = FontWeights.Bold;
        title.FontSize = S.LabelSize * Fs * 1.02;
        head.Children.Add(title);
        if (tape)
        {
            dock.Children.Add(new Border
            {
                Background = brush, Child = head, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(5 * Fs, 0.5, 6 * Fs, 1), CornerRadius = new CornerRadius(Math.Min(2, S.CornerRadius)),
            });
        }
        else dock.Children.Add(head);

        var stack = new StackPanel();
        stack.Children.Add(dock);
        if (S.HeaderStyle == HeaderStyle.Rule)
            stack.Children.Add(new Border { Height = 1, Background = ColorUtil.Brush(((SolidColorBrush)brush).Color, 0.4), Margin = new Thickness(0, 2, 0, 1) });
        Child = stack;
    }

    public override void Refresh(HudRenderContext ctx)
    {
        if (_subtitle == null || _subDef == null) return;
        string text = _subDef.IsText ? ctx.Store.GetText(_subDef.Id) ?? "" : MetricRegistry.Format(_subDef, ctx.Store.Get(_subDef.Id), ctx.Settings) is var (v, u) ? $"{v} {u}" : "";
        SetText(_subtitle, text);
    }
}

public sealed class IconElement : HudElement
{
    public IconElement(HudComponent c, HudStyle s) : base(c, s)
    {
        var size = (C.Height > 0 ? C.Height : 20) * Fs;
        Child = Icons.Create(string.IsNullOrEmpty(C.Icon) ? Def?.Icon ?? "activity" : C.Icon, OverrideBrush ?? S.Accent, size);
        HorizontalAlignment = HorizontalAlignment.Center;
    }
    public override void Refresh(HudRenderContext ctx) { }
}

public sealed class DividerElement : HudElement
{
    public DividerElement(HudComponent c, HudStyle s) : base(c, s)
    {
        Child = new Border { Height = 1, Background = OverrideBrush ?? S.Grid, Margin = new Thickness(0, 3 * Fs, 0, 3 * Fs) };
    }
    public override void Refresh(HudRenderContext ctx) { }
}

public sealed class SpacerElement : HudElement
{
    public SpacerElement(HudComponent c, HudStyle s) : base(c, s)
    {
        Height = (C.Height > 0 ? C.Height : 8) * Fs;
        MinWidth = C.Width > 0 ? C.Width : 4;
    }
    public override void Refresh(HudRenderContext ctx) { }
}

public static class HudElementFactory
{
    public static HudElement Create(HudComponent c, HudStyle s) => c.Type switch
    {
        ComponentType.Number => new NumberElement(c, s, false),
        ComponentType.Percentage => new NumberElement(c, s, true),
        ComponentType.BigNumber => new BigNumberElement(c, s),
        ComponentType.ProgressBar => new ProgressBarElement(c, s),
        ComponentType.Graph => new GraphElement(c, s, false),
        ComponentType.FrameTimeGraph => new GraphElement(c, s, true),
        ComponentType.Gauge => new GaugeElement(c, s),
        ComponentType.CoreGrid => new CoreGridElement(c, s),
        ComponentType.SensorList => new SensorListElement(c, s),
        ComponentType.DriveList => new DriveListElement(c, s),
        ComponentType.Text => new HeaderElement(c, s),
        ComponentType.Icon => new IconElement(c, s),
        ComponentType.Divider => new DividerElement(c, s),
        ComponentType.Spacer => new SpacerElement(c, s),
        ComponentType.Trend => new TrendElement(c, s),
        ComponentType.Stats => new StatsElement(c, s),
        ComponentType.Template => new TemplateElement(c, s),
        _ => new HeaderElement(c, s),
    };

    /// <summary>Lays components out in a Grid. Horizontal orientation transposes rows and columns.</summary>
    public static Grid BuildGrid(IEnumerable<HudComponent> components, HudStyle style, bool transpose, List<HudElement> created)
    {
        var grid = new Grid();
        var items = components.ToList();
        int cols = items.Count == 0 ? 1 : items.Max(c => transpose ? c.RowEnd : c.ColEnd);
        int rows = items.Count == 0 ? 1 : items.Max(c => transpose ? c.ColEnd : c.RowEnd);
        for (int i = 0; i < cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var c in items)
        {
            HudElement e;
            try { e = Create(c, style); }
            catch (Exception ex)
            {
                Core.Log.Once($"element-{c.Type}", Core.LogLevel.Warn, $"Could not create HUD element {c.Type}/{c.MetricId}: {ex.Message}");
                continue;
            }
            Grid.SetColumn(e, transpose ? c.Row : c.Col);
            Grid.SetRow(e, transpose ? c.Col : c.Row);
            Grid.SetColumnSpan(e, transpose ? c.RowSpan : c.ColSpan);
            Grid.SetRowSpan(e, transpose ? c.ColSpan : c.RowSpan);
            if (c.Type is ComponentType.SensorList or ComponentType.DriveList) e.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(e);
            created.Add(e);
        }
        return grid;
    }
}
