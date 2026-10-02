namespace PerfHud.Monitoring;

/// <summary>Fixed-capacity ring buffer of (time, value) samples. Thread-safe for one writer / many readers.</summary>
public sealed class MetricSeries
{
    private readonly double[] _t;
    private readonly double[] _v;
    private int _head;   // next write index
    private int _count;
    private readonly object _lock = new();

    public MetricSeries(int capacity = 1024)
    {
        _t = new double[capacity];
        _v = new double[capacity];
    }

    public int Capacity => _t.Length;

    /// <summary>Monotonic seconds used as the time axis for all series.</summary>
    public static double Now => Environment.TickCount64 / 1000.0;

    public void Add(double value) => Add(Now, value);

    public void Add(double time, double value)
    {
        lock (_lock)
        {
            _t[_head] = time;
            _v[_head] = value;
            _head = (_head + 1) % _t.Length;
            if (_count < _t.Length) _count++;
        }
    }

    public void Clear() { lock (_lock) { _head = 0; _count = 0; } }

    /// <summary>Copies samples newer than <paramref name="since"/> into the provided buffers. Returns count.</summary>
    public int CopySince(double since, ref double[] times, ref double[] values)
    {
        lock (_lock)
        {
            if (times.Length < _count) { times = new double[_t.Length]; values = new double[_t.Length]; }
            int n = 0;
            int start = (_head - _count + _t.Length) % _t.Length;
            for (int i = 0; i < _count; i++)
            {
                int idx = (start + i) % _t.Length;
                if (_t[idx] < since) continue;
                times[n] = _t[idx];
                values[n] = _v[idx];
                n++;
            }
            return n;
        }
    }

    public (double min, double max, double avg, int n) Stats(double since)
    {
        lock (_lock)
        {
            double min = double.MaxValue, max = double.MinValue, sum = 0; int n = 0;
            int start = (_head - _count + _t.Length) % _t.Length;
            for (int i = 0; i < _count; i++)
            {
                int idx = (start + i) % _t.Length;
                if (_t[idx] < since || double.IsNaN(_v[idx])) continue;
                var v = _v[idx];
                if (v < min) min = v; if (v > max) max = v; sum += v; n++;
            }
            return n == 0 ? (double.NaN, double.NaN, double.NaN, 0) : (min, max, sum / n, n);
        }
    }
}
