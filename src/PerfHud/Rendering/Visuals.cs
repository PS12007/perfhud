using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PerfHud.Rendering;

/// <summary>
/// Lightweight sparkline: draws a polyline + soft gradient fill directly in OnRender (no per-point visuals).
/// Data is decimated to one min/max pair per pixel column, so long windows stay cheap.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    private double[] _t = Array.Empty<double>(), _v = Array.Empty<double>();
    private int _n;
    private double _now, _window = 60, _fixedMax = double.NaN, _minMax = 1;
    private Pen? _pen;
    private Brush? _fill;
    private Pen? _gridPen;
    private Brush _line = Brushes.Cyan;
    private StreamGeometry? _lineGeo, _fillGeo;
    private double _shownMax;

    public double ShownMax => _shownMax;
    public Settings.GraphStyle Mode { get; set; }
    /// <summary>Draws the mid/baseline guides.</summary>
    public bool ShowGrid { get; set; } = true;

    public void SetStyle(Brush line, Brush grid, double thickness = 1.4)
    {
        _line = line;
        _pen = new Pen(line, thickness) { LineJoin = PenLineJoin.Round };
        _pen.Freeze();
        var c = (line as SolidColorBrush)?.Color ?? Colors.Cyan;
        Brush fill = Mode == Settings.GraphStyle.Columns
            ? new SolidColorBrush(Color.FromArgb((byte)(c.A * 0.85), c.R, c.G, c.B))
            : new LinearGradientBrush(Color.FromArgb(70, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90);
        fill.Freeze();
        _fill = fill;
        _gridPen = new Pen(grid, 1);
        _gridPen.Freeze();
    }

    /// <summary>Takes ownership of the arrays until the next call. <paramref name="fixedMax"/> = NaN for auto-scale.</summary>
    public void SetData(double[] t, double[] v, int n, double now, double windowSeconds, double fixedMax, double minAutoMax = 1)
    {
        _t = t; _v = v; _n = n; _now = now; _window = windowSeconds; _fixedMax = fixedMax; _minMax = minAutoMax;
        Rebuild();
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    private void Rebuild()
    {
        double w = ActualWidth, h = ActualHeight;
        _lineGeo = null; _fillGeo = null;
        if (w < 2 || h < 2 || _n < 2) return;

        double max = _fixedMax;
        if (double.IsNaN(max))
        {
            max = _minMax;
            for (int i = 0; i < _n; i++) if (!double.IsNaN(_v[i]) && _v[i] > max) max = _v[i];
            max *= 1.15;
        }
        _shownMax = max;
        double start = _now - _window;
        if (Mode == Settings.GraphStyle.Columns) { RebuildColumns(w, h, max, start); return; }
        int cols = Math.Max(2, (int)w);

        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            bool open = false;
            double firstX = 0, lastX = 0;
            int col = -1; double cmin = 0, cmax = 0; bool cHas = false;

            void Emit(int c, double vmin, double vmax)
            {
                double x = c / (double)(cols - 1) * w;
                double y1 = h - Math.Clamp(vmax / max, 0, 1) * (h - 1) - 0.5;
                double y2 = h - Math.Clamp(vmin / max, 0, 1) * (h - 1) - 0.5;
                if (!open)
                {
                    lc.BeginFigure(new Point(x, y1), false, false);
                    fc.BeginFigure(new Point(x, h), true, true);
                    fc.LineTo(new Point(x, y1), false, false);
                    open = true; firstX = x;
                }
                else
                {
                    lc.LineTo(new Point(x, y1), true, true);
                    fc.LineTo(new Point(x, y1), false, false);
                }
                if (Math.Abs(y2 - y1) > 0.5) { lc.LineTo(new Point(x, y2), true, true); fc.LineTo(new Point(x, y2), false, false); }
                lastX = x;
            }

            for (int i = 0; i < _n; i++)
            {
                double v = _v[i];
                if (double.IsNaN(v)) continue;
                int c = (int)((_t[i] - start) / _window * (cols - 1));
                if (c < 0) continue;
                if (c >= cols) c = cols - 1;
                if (c != col)
                {
                    if (cHas) Emit(col, cmin, cmax);
                    col = c; cmin = cmax = v; cHas = true;
                }
                else { if (v < cmin) cmin = v; if (v > cmax) cmax = v; }
            }
            if (cHas) Emit(col, cmin, cmax);
            if (open) fc.LineTo(new Point(lastX, h), false, false);
            _ = firstX;
        }
        line.Freeze(); fill.Freeze();
        _lineGeo = line; _fillGeo = Mode == Settings.GraphStyle.Line ? null : fill;
    }

    /// <summary>Bar-chart mode: the window is split into fixed-width buckets, each drawn as its peak value.</summary>
    private void RebuildColumns(double w, double h, double max, double start)
    {
        const double barW = 3, gap = 1;
        int buckets = Math.Max(2, (int)((w + gap) / (barW + gap)));
        var peak = new double[buckets];
        Array.Fill(peak, double.NaN);
        for (int i = 0; i < _n; i++)
        {
            double v = _v[i];
            if (double.IsNaN(v)) continue;
            int b = (int)((_t[i] - start) / _window * buckets);
            if (b < 0) continue;
            if (b >= buckets) b = buckets - 1;
            if (double.IsNaN(peak[b]) || v > peak[b]) peak[b] = v;
        }
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int b = 0; b < buckets; b++)
            {
                if (double.IsNaN(peak[b])) continue;
                double bh = Math.Max(1, Math.Clamp(peak[b] / max, 0, 1) * h);
                double x = b * (barW + gap);
                ctx.BeginFigure(new Point(x, h), true, true);
                ctx.LineTo(new Point(x, h - bh), false, false);
                ctx.LineTo(new Point(x + barW, h - bh), false, false);
                ctx.LineTo(new Point(x + barW, h), false, false);
            }
        }
        geo.Freeze();
        _fillGeo = geo;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (_gridPen != null && ShowGrid)
        {
            dc.DrawLine(_gridPen, new Point(0, Math.Round(h / 2) + 0.5), new Point(w, Math.Round(h / 2) + 0.5));
            dc.DrawLine(_gridPen, new Point(0, h - 0.5), new Point(w, h - 0.5));
        }
        if (_fillGeo != null && _fill != null) dc.DrawGeometry(_fill, null, _fillGeo);
        if (_lineGeo != null && _pen != null) dc.DrawGeometry(null, _pen, _lineGeo);
    }
}

/// <summary>Rounded progress bar with an animatable fill fraction.</summary>
public sealed class BarFill : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(BarFill), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Track { get; set; } = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    public Brush Fill { get; set; } = Brushes.Cyan;
    public Settings.BarStyle Kind { get; set; }

    public void AnimateTo(double v, double ms)
    {
        v = double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
        if (ms <= 0) { BeginAnimation(ValueProperty, null); Value = v; return; }
        BeginAnimation(ValueProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() }, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        switch (Kind)
        {
            case Settings.BarStyle.Segmented:
            {
                double gap = Math.Max(1, h * 0.35), segW = Math.Max(2, h * 0.9);
                int n = Math.Max(4, (int)((w + gap) / (segW + gap)));
                segW = (w - gap * (n - 1)) / n;
                int lit = (int)Math.Round(Value * n);
                if (Value > 0.001 && lit == 0) lit = 1;
                for (int i = 0; i < n; i++)
                    dc.DrawRectangle(i < lit ? Fill : Track, null, new Rect(i * (segW + gap), 0, segW, h));
                break;
            }
            case Settings.BarStyle.Line:
            {
                // Hairline track with a thicker fill riding on it.
                double th = Math.Max(1, Math.Round(h * 0.3));
                dc.DrawRectangle(Track, null, new Rect(0, h - th, w, th));
                double fw = w * Value;
                if (fw > 0) dc.DrawRectangle(Fill, null, new Rect(0, 0, fw, h));
                break;
            }
            default:
            {
                double r = Kind == Settings.BarStyle.Rounded ? h / 2 : 0;
                dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);
                double fw = Math.Max(Value > 0.001 ? (r > 0 ? h : 1) : 0, w * Value);
                if (fw > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, fw, h), r, r);
                break;
            }
        }
    }
}

/// <summary>270° arc gauge.</summary>
public sealed class GaugeArc : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(GaugeArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Track { get; set; } = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    public Brush Fill { get; set; } = Brushes.Cyan;
    public double Thickness { get; set; } = 5;
    public Settings.GaugeStyle Kind { get; set; }
    public bool RoundCaps { get; set; } = true;

    public void AnimateTo(double v, double ms)
    {
        v = double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
        if (ms <= 0) { BeginAnimation(ValueProperty, null); Value = v; return; }
        BeginAnimation(ValueProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() }, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var (start, sweep) = Kind switch
        {
            Settings.GaugeStyle.Ring => (-90.0, 360.0),
            Settings.GaugeStyle.Half => (180.0, 180.0),
            _ => (135.0, 270.0),
        };
        // A half gauge sits on the element's bottom edge and can use the full width.
        double size = Kind == Settings.GaugeStyle.Half ? Math.Min(w, h * 2) : Math.Min(w, h);
        if (size < 4) return;
        var c = Kind == Settings.GaugeStyle.Half ? new Point(w / 2, h - Thickness / 2) : new Point(w / 2, h / 2);
        double r = size / 2 - Thickness / 2 - 0.5;
        var cap = RoundCaps ? PenLineCap.Round : PenLineCap.Flat;
        var track = new Pen(Track, Thickness) { StartLineCap = cap, EndLineCap = cap };
        var fill = new Pen(Fill, Thickness) { StartLineCap = cap, EndLineCap = cap };
        dc.DrawGeometry(null, track, Arc(c, r, start, sweep));
        if (Value > 0.005) dc.DrawGeometry(null, fill, Arc(c, r, start, sweep * Value));
    }

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        sweepDeg = Math.Min(sweepDeg, 359.9);
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}

/// <summary>Per-core load mini bars.</summary>
public sealed class CoreBars : FrameworkElement
{
    private double[] _values = Array.Empty<double>();
    public Func<double, Brush>? BrushFor { get; set; }
    public Brush Track { get; set; } = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
    public double Radius { get; set; } = 1;

    public void SetValues(double[] v) { _values = v; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        int n = _values.Length;
        if (n == 0) return;
        double w = ActualWidth, h = ActualHeight;
        double gap = n > 24 ? 1.5 : 2;
        double bw = Math.Max(1.5, (w - gap * (n - 1)) / n);
        for (int i = 0; i < n; i++)
        {
            double x = i * (bw + gap);
            dc.DrawRoundedRectangle(Track, null, new Rect(x, 0, bw, h), Radius, Radius);
            double v = Math.Clamp(_values[i] / 100, 0, 1);
            double bh = Math.Max(1, v * h);
            dc.DrawRoundedRectangle(BrushFor?.Invoke(_values[i]) ?? Brushes.Cyan, null, new Rect(x, h - bh, bw, bh), Radius, Radius);
        }
    }
}
