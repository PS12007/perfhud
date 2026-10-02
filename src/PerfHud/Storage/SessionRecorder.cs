using System.Globalization;
using System.Text;
using System.Text.Json;
using PerfHud.Core;
using PerfHud.Monitoring;
using PerfHud.Settings;

namespace PerfHud.Storage;

public sealed class SessionSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? App { get; set; }
    public string Trigger { get; set; } = "Manual";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public double DurationSeconds { get; set; }
    public int Samples { get; set; }
    public long Frames { get; set; }
    public double? AvgFps { get; set; }
    public double? Low1Fps { get; set; }
    public double? Low01Fps { get; set; }
    public double? AvgCpu { get; set; }
    public double? MaxCpu { get; set; }
    public double? AvgGpu { get; set; }
    public double? MaxGpu { get; set; }
    public double? AvgCpuTemp { get; set; }
    public double? MaxCpuTemp { get; set; }
    public double? AvgGpuTemp { get; set; }
    public double? MaxGpuTemp { get; set; }
    public double? AvgRamPct { get; set; }
    public double? MaxRamPct { get; set; }
    public double? MaxVramGb { get; set; }
    public double? AvgCpuPower { get; set; }
    public double? AvgGpuPower { get; set; }
    public double? AvgBatteryDrainW { get; set; }
    public double? BatteryStart { get; set; }
    public double? BatteryEnd { get; set; }
    public double? BatteryUsed { get; set; }

    public string DurationText => MetricRegistry.FormatDuration(DurationSeconds);
    public string Title => $"{Name} · {Start:MMM d, HH:mm}";
}

/// <summary>Frame-time histogram (0.05 ms bins up to 500 ms) for exact session-wide average and 1%/0.1% lows.</summary>
public sealed class FrameHistogram
{
    private const double BinMs = 0.05;
    private readonly int[] _bins = new int[10_000];
    private long _count;
    private double _sum;
    private readonly object _lock = new();

    public void Add(float ms)
    {
        int i = (int)(ms / BinMs);
        if (i >= _bins.Length) i = _bins.Length - 1;
        lock (_lock) { _bins[i]++; _count++; _sum += ms; }
    }

    public long Count { get { lock (_lock) return _count; } }

    public (double avgFps, double low1, double low01) Compute()
    {
        lock (_lock)
        {
            if (_count < 10) return (double.NaN, double.NaN, double.NaN);
            return (1000.0 * _count / _sum, 1000.0 / TailAvg(Math.Max(1, _count / 100)), _count >= 1000 ? 1000.0 / TailAvg(Math.Max(1, _count / 1000)) : double.NaN);
        }
    }

    private double TailAvg(long k)
    {
        long need = k; double sum = 0;
        for (int i = _bins.Length - 1; i >= 0 && need > 0; i--)
        {
            if (_bins[i] == 0) continue;
            long take = Math.Min(need, _bins[i]);
            sum += take * (i + 0.5) * BinMs;
            need -= take;
        }
        return sum / k;
    }
}

/// <summary>Records metric samples to CSV while a session is active, and writes a JSON summary when it ends.</summary>
public sealed class SessionRecorder : IDisposable
{
    private static readonly (string Col, string Id, double Div)[] Columns =
    {
        ("fps", "fps.current", 1), ("frametime_ms", "fps.frametime", 1), ("fps_1pct_low", "fps.low1", 1),
        ("cpu_pct", "cpu.usage", 1), ("cpu_temp_c", "cpu.temp", 1), ("cpu_power_w", "cpu.power", 1), ("cpu_clock_mhz", "cpu.clock", 1),
        ("gpu_pct", "gpu.usage", 1), ("gpu_temp_c", "gpu.temp", 1), ("gpu_power_w", "gpu.power", 1), ("gpu_clock_mhz", "gpu.clock", 1),
        ("vram_gb", "gpu.vram.used", 1073741824), ("ram_pct", "ram.pct", 1), ("ram_gb", "ram.used", 1073741824),
        ("battery_pct", "bat.pct", 1), ("battery_drain_w", "bat.power", 1),
        ("net_down_kbps", "net.down", 1024), ("net_up_kbps", "net.up", 1024),
        ("disk_read_mbps", "disk.read", 1048576), ("disk_write_mbps", "disk.write", 1048576),
    };

    public static IReadOnlyList<string> ColumnNames => Columns.Select(c => c.Col).ToList();

    private readonly MetricStore _store;
    private readonly Func<AppSettings> _settings;
    private Timer? _timer;
    private StreamWriter? _csv;
    private SessionSummary? _current;
    private FrameHistogram? _hist;
    private readonly Dictionary<string, (double sum, double max, int n)> _acc = new();
    private readonly object _lock = new();
    private DateTime _start;

    public bool IsRecording => _current != null;
    public SessionSummary? Current => _current;
    public string? Trigger => _current?.Trigger;
    public event Action<bool>? RecordingChanged;
    public event Action<SessionSummary>? SessionSaved;

    /// <summary>Frame sink to attach to the FPS monitor while recording.</summary>
    public Action<float>? FrameSink => _hist == null ? null : _hist.Add;

    public SessionRecorder(MetricStore store, Func<AppSettings> settings)
    {
        _store = store;
        _settings = settings;
    }

    public void Start(string name, string? app, string trigger)
    {
        lock (_lock)
        {
            if (_current != null) return;
            _start = DateTime.Now;
            var id = $"{_start:yyyyMMdd-HHmmss}";
            _current = new SessionSummary { Id = id, Name = name, App = app, Trigger = trigger, Start = _start, BatteryStart = Nz(_store.Get("bat.pct")) };
            _hist = new FrameHistogram();
            _acc.Clear();
            try
            {
                _csv = new StreamWriter(Path.Combine(AppPaths.SessionDir, id + ".csv"), false, new UTF8Encoding(false));
                _csv.WriteLine("t_seconds,timestamp," + string.Join(",", Columns.Select(c => c.Col)));
            }
            catch (Exception ex)
            {
                Log.Error("Could not create session file", ex);
                _current = null;
                return;
            }
            int interval = Math.Clamp(_settings().History.SampleIntervalSeconds, 1, 60) * 1000;
            _timer = new Timer(_ => Sample(), null, interval, interval);
        }
        Log.Info($"Session recording started: {name} ({trigger})");
        RecordingChanged?.Invoke(true);
    }

    private void Sample()
    {
        lock (_lock)
        {
            if (_current == null || _csv == null) return;
            var sb = new StringBuilder();
            sb.Append(((DateTime.Now - _start).TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
              .Append(DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
            foreach (var (col, id, div) in Columns)
            {
                var v = _store.Get(id);
                sb.Append(',');
                if (double.IsNaN(v)) continue;
                v /= div;
                sb.Append(v.ToString("0.##", CultureInfo.InvariantCulture));
                var a = _acc.GetValueOrDefault(col, (0, double.MinValue, 0));
                _acc[col] = (a.sum + v, Math.Max(a.max, v), a.n + 1);
            }
            try { _csv.WriteLine(sb.ToString()); _csv.Flush(); }
            catch (Exception ex) { Log.Once("session-write", LogLevel.Error, $"Session write failed: {ex.Message}"); }
            _current.Samples++;
        }
    }

    public SessionSummary? Stop()
    {
        SessionSummary? s;
        lock (_lock)
        {
            if (_current == null) return null;
            _timer?.Dispose(); _timer = null;
            s = _current;
            s.End = DateTime.Now;
            s.DurationSeconds = (s.End - s.Start).TotalSeconds;
            if (_hist != null)
            {
                var (avg, l1, l01) = _hist.Compute();
                s.Frames = _hist.Count;
                s.AvgFps = Nz(avg); s.Low1Fps = Nz(l1); s.Low01Fps = Nz(l01);
            }
            double? Avg(string c) => _acc.TryGetValue(c, out var a) && a.n > 0 ? Math.Round(a.sum / a.n, 1) : null;
            double? Max(string c) => _acc.TryGetValue(c, out var a) && a.n > 0 ? Math.Round(a.max, 1) : null;
            s.AvgCpu = Avg("cpu_pct"); s.MaxCpu = Max("cpu_pct");
            s.AvgGpu = Avg("gpu_pct"); s.MaxGpu = Max("gpu_pct");
            s.AvgCpuTemp = Avg("cpu_temp_c"); s.MaxCpuTemp = Max("cpu_temp_c");
            s.AvgGpuTemp = Avg("gpu_temp_c"); s.MaxGpuTemp = Max("gpu_temp_c");
            s.AvgRamPct = Avg("ram_pct"); s.MaxRamPct = Max("ram_pct");
            s.MaxVramGb = Max("vram_gb");
            s.AvgCpuPower = Avg("cpu_power_w"); s.AvgGpuPower = Avg("gpu_power_w");
            s.AvgBatteryDrainW = Avg("battery_drain_w");
            s.BatteryEnd = Nz(_store.Get("bat.pct"));
            if (s.BatteryStart is double b0 && s.BatteryEnd is double b1) s.BatteryUsed = Math.Round(Math.Max(0, b0 - b1), 1);
            try
            {
                _csv?.Dispose();
                File.WriteAllText(Path.Combine(AppPaths.SessionDir, s.Id + ".json"), JsonSerializer.Serialize(s, SessionStore.Json));
            }
            catch (Exception ex) { Log.Error("Could not save session summary", ex); }
            _csv = null;
            _current = null;
            _hist = null;
        }
        Log.Info($"Session saved: {s.Name}, {s.DurationText}");
        RecordingChanged?.Invoke(false);
        if (s.DurationSeconds >= 10) SessionSaved?.Invoke(s);
        else SessionStore.Delete(s.Id); // discard accidental blips
        return s;
    }

    private static double? Nz(double v) => double.IsNaN(v) ? null : Math.Round(v, 1);

    public void Dispose() => Stop();
}
