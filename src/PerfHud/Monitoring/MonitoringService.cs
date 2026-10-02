using PerfHud.Core;

namespace PerfHud.Monitoring;

public sealed class MonitorStatus
{
    public required string Name { get; init; }
    public MonitorState State { get; set; } = MonitorState.Starting;
    public string? Message { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime LastSuccess { get; set; }
    public double LastDurationMs { get; set; }
}

/// <summary>
/// Schedules monitors on two background lanes. Handles fault isolation, exponential backoff,
/// pause/resume, battery-saving interval scaling, adaptive self-throttling and full resets (resume from sleep).
/// </summary>
public sealed class MonitoringService : IDisposable
{
    private sealed class Entry
    {
        public required IMonitor Monitor { get; init; }
        public required MonitorStatus Status { get; init; }
        public long NextDue;
        public bool Initialized;
        public bool ResetRequested;
    }

    private readonly List<Entry> _entries = new();
    private readonly MonitorContext _ctx;
    private readonly List<Thread> _threads = new();
    private readonly AutoResetEvent[] _wake = { new(false), new(false) };
    private volatile bool _running;
    private volatile bool _paused;

    /// <summary>Extra interval multiplier applied when PerfHud's own CPU use exceeds the configured limit (1..4).</summary>
    public double ThrottleFactor { get; set; } = 1.0;

    public MonitorContext Context => _ctx;
    public bool IsPaused => _paused;
    public event Action<bool>? PausedChanged;

    public MonitoringService(MonitorContext ctx) => _ctx = ctx;

    public void Add(IMonitor m) => _entries.Add(new Entry { Monitor = m, Status = new MonitorStatus { Name = m.Name } });

    public IReadOnlyList<MonitorStatus> Statuses => _entries.Select(e => e.Status).ToList();
    public IEnumerable<IMonitor> Monitors => _entries.Select(e => e.Monitor);
    public T? Get<T>() where T : class, IMonitor => _entries.Select(e => e.Monitor).OfType<T>().FirstOrDefault();

    public void Start()
    {
        _running = true;
        foreach (var lane in new[] { MonitorLane.Fast, MonitorLane.Slow })
        {
            var t = new Thread(() => Loop(lane))
            {
                IsBackground = true,
                Name = $"PerfHud.Monitor.{lane}",
                Priority = ThreadPriority.BelowNormal,
            };
            _threads.Add(t);
            t.Start();
        }
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        foreach (var e in _entries) e.Status.State = paused ? MonitorState.Paused : MonitorState.Starting;
        Log.Info(paused ? "Monitoring paused" : "Monitoring resumed");
        WakeAll();
        PausedChanged?.Invoke(paused);
    }

    /// <summary>Forces every monitor to release and re-acquire its native resources (sleep/wake, GPU driver reset, display change).</summary>
    public void RequestResetAll(string reason)
    {
        Log.Info($"Resetting all monitors: {reason}");
        lock (_entries) foreach (var e in _entries) { e.ResetRequested = true; e.NextDue = 0; }
        WakeAll();
    }

    public void RequestReset<T>() where T : IMonitor
    {
        lock (_entries) foreach (var e in _entries.Where(e => e.Monitor is T)) { e.ResetRequested = true; e.NextDue = 0; }
        WakeAll();
    }

    /// <summary>Runs one monitor as soon as possible (e.g. battery after an AC plug event).</summary>
    public void Kick<T>() where T : IMonitor
    {
        lock (_entries) foreach (var e in _entries.Where(e => e.Monitor is T)) e.NextDue = 0;
        WakeAll();
    }

    /// <summary>Run everything again immediately (e.g. after settings change).</summary>
    public void Kick()
    {
        lock (_entries) foreach (var e in _entries) e.NextDue = 0;
        WakeAll();
    }

    private void WakeAll() { foreach (var w in _wake) w.Set(); }

    private void Loop(MonitorLane lane)
    {
        var mine = _entries.Where(e => e.Monitor.Lane == lane).ToList();
        while (_running)
        {
            long now = Environment.TickCount64;
            long nextWake = now + 1000;

            if (!_paused)
            {
                foreach (var e in mine)
                {
                    if (!_running) break;
                    if (e.ResetRequested) DoReset(e);
                    if (now >= e.NextDue) Run(e);
                    nextWake = Math.Min(nextWake, e.NextDue);
                }
            }

            int wait = (int)Math.Clamp(nextWake - Environment.TickCount64, 15, 1000);
            _wake[(int)lane].WaitOne(wait);
        }
    }

    private void DoReset(Entry e)
    {
        e.ResetRequested = false;
        try { e.Monitor.Reset(); } catch (Exception ex) { Log.Once($"reset-{e.Monitor.Name}", LogLevel.Warn, $"{e.Monitor.Name} reset error: {ex.Message}"); }
        e.Initialized = false;
        e.Status.ConsecutiveFailures = 0;
    }

    private void Run(Entry e)
    {
        var m = e.Monitor;
        var st = e.Status;
        var settings = _ctx.Settings;
        int interval = Math.Max(100, m.GetIntervalMs(_ctx));
        double scale = ThrottleFactor;
        if (_ctx.PowerSaving)
        {
            scale *= Math.Max(1, settings.Battery.IntervalMultiplier);
            if (m.IsExpensive && settings.Battery.DisableExpensiveSensors) scale *= 4;
        }
        long start = Environment.TickCount64;

        try
        {
            if (!e.Initialized)
            {
                m.Initialize(_ctx);
                e.Initialized = true;
                Log.Info($"Monitor '{m.Name}' initialized{(m.Detail != null ? $" ({m.Detail})" : "")}");
            }
            m.Update(_ctx);

            if (st.ConsecutiveFailures > 0)
                Log.Info($"Monitor '{m.Name}' recovered after {st.ConsecutiveFailures} failure(s)");
            st.ConsecutiveFailures = 0;
            st.State = m.Detail?.StartsWith("Partial", StringComparison.OrdinalIgnoreCase) == true ? MonitorState.Degraded : MonitorState.Ok;
            st.Message = m.Detail;
            st.LastSuccess = DateTime.Now;
            e.NextDue = start + (long)(interval * scale);
        }
        catch (MonitorUnavailableException ex)
        {
            if (st.State != MonitorState.Unavailable || st.Message != ex.Message)
                Log.Info($"Monitor '{m.Name}' unavailable: {ex.Message}");
            st.State = MonitorState.Unavailable;
            st.Message = ex.Message;
            foreach (var p in m.Prefixes) _ctx.Store.InvalidatePrefix(p, ex.Message);
            try { m.Reset(); } catch { }
            e.Initialized = false;
            e.NextDue = start + 60_000; // retry slowly: hardware may appear later (dock, driver install, elevation)
        }
        catch (Exception ex)
        {
            st.ConsecutiveFailures++;
            st.State = MonitorState.Failing;
            st.Message = ex.Message;
            Log.Once($"monitor-{m.Name}-{ex.GetType().Name}", LogLevel.Warn, $"Monitor '{m.Name}' failed ({st.ConsecutiveFailures}): {ex.GetType().Name}: {ex.Message}");

            if (st.ConsecutiveFailures >= 3)
            {
                foreach (var p in m.Prefixes) _ctx.Store.InvalidatePrefix(p, "Sensor error — retrying");
                try { m.Reset(); } catch { }
                e.Initialized = false;
            }
            double backoff = Math.Min(60_000, interval * Math.Pow(2, Math.Min(st.ConsecutiveFailures, 6)));
            e.NextDue = start + (long)backoff;
        }
        finally
        {
            st.LastDurationMs = Environment.TickCount64 - start;
        }
    }

    public void Dispose()
    {
        _running = false;
        WakeAll();
        foreach (var t in _threads) t.Join(1500);
        foreach (var e in _entries)
        {
            try { e.Monitor.Dispose(); } catch { }
        }
    }
}
