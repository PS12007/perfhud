using System.Collections.Concurrent;

namespace PerfHud.Monitoring;

/// <summary>
/// Central, thread-safe store of the latest metric values. Monitors write; the HUD, alerts and recorder read.
/// A missing/NaN value means "not available" and is displayed as N/A, never faked.
/// </summary>
public sealed class MetricStore
{
    private sealed class Cell
    {
        public double Value = double.NaN;
        public string? Text;
        public object? Obj;
        public string? Reason;
        public long Ticks;
    }

    private readonly ConcurrentDictionary<string, Cell> _cells = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MetricSeries> _series = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>History capacity sized for 5 min at 4 Hz.</summary>
    public const int SeriesCapacity = 1280;

    public MetricStore()
    {
        foreach (var d in MetricRegistry.All)
            if (d.Graphable) _series[d.Id] = new MetricSeries(SeriesCapacity);
    }

    private Cell CellFor(string id) => _cells.GetOrAdd(id, _ => new Cell());

    public void Set(string id, double value)
    {
        var c = CellFor(id);
        c.Value = value;
        c.Reason = null;
        c.Ticks = Environment.TickCount64;
        if (_series.TryGetValue(id, out var s)) s.Add(value);
    }

    /// <summary>Sets a value, or marks it unavailable if null.</summary>
    public void Set(string id, double? value, string reasonIfNull)
    {
        if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)) Set(id, value.Value);
        else SetUnavailable(id, reasonIfNull);
    }

    public void SetText(string id, string? text)
    {
        var c = CellFor(id);
        c.Text = text;
        if (text != null) c.Reason = null;
        c.Ticks = Environment.TickCount64;
    }

    public void SetObject(string id, object? obj)
    {
        var c = CellFor(id);
        c.Obj = obj;
        c.Ticks = Environment.TickCount64;
    }

    public void SetUnavailable(string id, string reason)
    {
        var c = CellFor(id);
        c.Value = double.NaN;
        c.Reason = reason;
        c.Ticks = Environment.TickCount64;
        if (_series.TryGetValue(id, out var s)) s.Add(double.NaN);
    }

    public double Get(string id) => _cells.TryGetValue(id, out var c) ? c.Value : double.NaN;
    public bool Has(string id) => !double.IsNaN(Get(id));
    public string? GetText(string id) => _cells.TryGetValue(id, out var c) ? c.Text : null;
    public T? GetObject<T>(string id) where T : class => _cells.TryGetValue(id, out var c) ? c.Obj as T : null;
    public string? GetReason(string id) => _cells.TryGetValue(id, out var c) ? c.Reason : null;

    /// <summary>Age of the last write in ms (long.MaxValue if never written).</summary>
    public long AgeMs(string id) => _cells.TryGetValue(id, out var c) ? Environment.TickCount64 - c.Ticks : long.MaxValue;

    public MetricSeries? Series(string id) => _series.TryGetValue(id, out var s) ? s : null;

    /// <summary>Ensures a history series exists for dynamic metrics (e.g. per-drive) that a graph wants to show.</summary>
    public MetricSeries EnsureSeries(string id) => _series.GetOrAdd(id, _ => new MetricSeries(SeriesCapacity));

    public IEnumerable<string> Keys => _cells.Keys;

    /// <summary>Marks every metric of a monitor stale (used when a monitor fails or is paused).</summary>
    public void InvalidatePrefix(string prefix, string reason)
    {
        // Metrics that never had a value still get the reason, so the UI can explain *why* they're N/A.
        foreach (var d in MetricRegistry.All)
            if (!d.IsText && d.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var c = CellFor(d.Id);
                if (double.IsNaN(c.Value)) c.Reason = reason;
            }
        foreach (var kv in _cells)
            if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !double.IsNaN(kv.Value.Value))
            {
                kv.Value.Value = double.NaN;
                kv.Value.Reason = reason;
            }
    }
}
