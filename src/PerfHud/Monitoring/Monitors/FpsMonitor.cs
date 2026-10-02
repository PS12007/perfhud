using PerfHud.Core;
using PerfHud.Sensors.Native;
using PerfHud.Settings;

namespace PerfHud.Monitoring.Monitors;

/// <summary>
/// Frame-rate statistics for the foreground (or most active) application, from passive ETW present events.
/// Current FPS, average, 1% / 0.1% lows (average of the slowest 1% / 0.1% frames), frame time and a per-frame history.
/// </summary>
public sealed class FpsMonitor : MonitorBase
{
    public override string Name => "FPS";
    public override string[] Prefixes => new[] { "fps." };
    public override int GetIntervalMs(MonitorContext ctx) => 250;

    private const int SourceCount = 5;
    private const long TicksPerMs = 10_000; // ETW timestamps are FILETIME (100 ns)

    private sealed class FrameRing
    {
        public readonly long[] Time;
        public readonly float[] Ms;
        public int Head, Count;
        public long LastTs;
        public FrameRing(int cap) { Time = new long[cap]; Ms = new float[cap]; }
        public void Add(long ts)
        {
            if (LastTs != 0)
            {
                long d = ts - LastTs;
                if (d > 0 && d < 5000 * TicksPerMs)
                {
                    Time[Head] = ts;
                    Ms[Head] = (float)(d / (double)TicksPerMs);
                    Head = (Head + 1) % Time.Length;
                    if (Count < Time.Length) Count++;
                }
            }
            LastTs = ts;
        }
    }

    private sealed class ProcFrames
    {
        public readonly FrameRing?[] Rings = new FrameRing?[SourceCount];
        public readonly long[] LastEvent = new long[SourceCount];
        public long LastAny;
        public string? Name;
    }

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm.exe", "PerfHud.exe", "csrss.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe",
        "TextInputHost.exe", "SearchHost.exe", "explorer.exe", "LockApp.exe", "SystemSettings.exe", "ApplicationFrameHost.exe",
    };

    private readonly object _lock = new();
    private readonly Dictionary<uint, ProcFrames> _procs = new();
    private EtwPresentSession? _session;
    private uint _targetPid;
    private int _targetSource = -1;
    private long _lastCleanup;
    private float[] _sortBuf = new float[4096];

    /// <summary>Optional sink for session recording: receives each new frame time (ms) of the measured app.</summary>
    public Action<float>? FrameSink { get; set; }
    private long _sinkCursor;

    public uint TargetPid => _targetPid;
    public string? TargetName { get; private set; }
    public bool IsRunning => _session != null;

    public override void Initialize(MonitorContext ctx)
    {
        if (!ctx.Settings.Performance.FpsEnabled) throw new MonitorUnavailableException("FPS counter disabled in settings");
        var session = new EtwPresentSession(OnPresent);
        try { session.Start(); }
        catch (UnauthorizedAccessException)
        {
            session.Dispose();
            throw new MonitorUnavailableException(Elevation.IsPerformanceLogUser()
                ? "Sign out and back in to activate 'Performance Log Users' membership"
                : "FPS needs admin rights or 'Performance Log Users' membership (Settings → Performance → Enable FPS)");
        }
        catch (Exception ex)
        {
            session.Dispose();
            throw new MonitorUnavailableException($"Frame timing unavailable: {ex.Message}");
        }
        _session = session;
        Detail = "ETW (DXGI / D3D9 / DxgKrnl present events)";
    }

    private void OnPresent(uint pid, long ts, PresentSource src)
    {
        lock (_lock)
        {
            if (!_procs.TryGetValue(pid, out var p))
            {
                if (_procs.Count > 256) return;
                p = new ProcFrames();
                _procs[pid] = p;
            }
            int i = (int)src;
            (p.Rings[i] ??= new FrameRing(16384)).Add(ts);
            long now = Environment.TickCount64;
            p.LastEvent[i] = now;
            p.LastAny = now;
        }
    }

    public override void Update(MonitorContext ctx)
    {
        var store = ctx.Store;
        var settings = ctx.Settings;
        if (!settings.Performance.FpsEnabled) throw new MonitorUnavailableException("FPS counter disabled in settings");

        long now = Environment.TickCount64;
        long nowFt = DateTime.UtcNow.ToFileTimeUtc();

        // Pick the target process
        Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out var fgPid);
        lock (_lock)
        {
            if (now - _lastCleanup > 10_000)
            {
                _lastCleanup = now;
                foreach (var k in _procs.Where(kv => now - kv.Value.LastAny > 30_000).Select(kv => kv.Key).ToList()) _procs.Remove(k);
            }

            uint target = 0;
            if (_procs.TryGetValue(fgPid, out var fg) && now - fg.LastAny < 3000 && !IsIgnored(fgPid, fg))
                target = fgPid;
            else if (settings.Performance.FpsTarget == FpsTargetMode.ForegroundThenMostActive)
            {
                int best = 0;
                foreach (var (pid, p) in _procs)
                {
                    if (now - p.LastAny > 2000 || IsIgnored(pid, p)) continue;
                    int cnt = RecentCount(p, nowFt, 1000);
                    if (cnt > best) { best = cnt; target = pid; }
                }
                if (best < 5) target = 0; // a couple of stray presents isn't an app rendering
            }

            if (target != _targetPid) { _targetPid = target; _sinkCursor = 0; }
            if (target == 0 || !_procs.TryGetValue(target, out var tp))
            {
                _targetSource = -1;
                TargetName = null;
                const string why = "No app is rendering frames right now";
                foreach (var id in new[] { "fps.current", "fps.avg", "fps.low1", "fps.low01", "fps.frametime" }) store.SetUnavailable(id, why);
                store.SetText("fps.app", null);
                store.SetText("fps.api", null);
                return;
            }

            _targetSource = PickSource(tp, now, nowFt);
            var ring = _targetSource >= 0 ? tp.Rings[_targetSource] : null;
            TargetName = tp.Name;
            store.SetText("fps.app", settings.Privacy.ShowProcessNames ? Path.GetFileNameWithoutExtension(tp.Name) : "App");
            store.SetText("fps.api", _targetSource switch { 0 => "DXGI", 1 => "D3D9", 2 or 3 or 4 => "Kernel", _ => null });
            if (ring == null) return;

            // Current FPS / frame time over the last second (ETW buffers add ~1 s latency; use the newest second we have)
            long newest = ring.Time[(ring.Head - 1 + ring.Time.Length) % ring.Time.Length];
            bool stale = nowFt - newest > 2_500 * TicksPerMs; // app stopped presenting (minimized / paused)
            Stats(ring, newest - 1000 * TicksPerMs, out int n1, out double sum1, sortSlowest: false, out _, out _);
            if (stale || n1 == 0)
            {
                store.Set("fps.current", 0);
                store.SetUnavailable("fps.frametime", "App is not presenting frames");
            }
            else
            {
                double ft = sum1 / n1;
                store.Set("fps.frametime", ft);
                store.Set("fps.current", 1000.0 / ft);
            }

            // Window stats
            int window = Math.Clamp(settings.Performance.FpsStatsWindowSeconds, 5, 600);
            Stats(ring, newest - window * 1000L * TicksPerMs, out int n, out double sum, sortSlowest: true, out double low1, out double low01);
            if (n > 10)
            {
                store.Set("fps.avg", 1000.0 * n / sum);
                store.Set("fps.low1", low1 > 0 ? 1000.0 / low1 : double.NaN);
                if (n >= 1000) store.Set("fps.low01", 1000.0 / low01);
                else store.SetUnavailable("fps.low01", "Need ≥1000 frames");
            }

            // Feed the session recorder
            if (FrameSink != null)
            {
                int start = (ring.Head - ring.Count + ring.Time.Length) % ring.Time.Length;
                for (int i = 0; i < ring.Count; i++)
                {
                    int idx = (start + i) % ring.Time.Length;
                    if (ring.Time[idx] <= _sinkCursor) continue;
                    FrameSink(ring.Ms[idx]);
                }
                _sinkCursor = newest;
            }
        }
    }

    private bool IsIgnored(uint pid, ProcFrames p)
    {
        p.Name ??= Win32.GetProcessName(pid) ?? $"PID {pid}";
        return Ignored.Contains(p.Name);
    }

    private static int RecentCount(ProcFrames p, long nowFt, int ms)
    {
        int best = 0;
        for (int s = 0; s < SourceCount; s++)
        {
            var r = p.Rings[s];
            if (r == null) continue;
            long since = r.LastTs - ms * TicksPerMs;
            int c = 0;
            for (int i = 0; i < r.Count && c < 2000; i++)
            {
                int idx = (r.Head - 1 - i + r.Time.Length * 2) % r.Time.Length;
                if (r.Time[idx] < since) break;
                c++;
            }
            best = Math.Max(best, c);
        }
        return best;
    }

    /// <summary>DXGI > D3D9 > kernel events. Among kernel sources pick the one with the fewest (non-zero) events,
    /// since some present paths emit more than one kernel event per frame.</summary>
    private static int PickSource(ProcFrames p, long now, long nowFt)
    {
        for (int s = 0; s <= 1; s++)
            if (p.Rings[s] != null && now - p.LastEvent[s] < 3000) return s;
        int best = -1, bestCount = int.MaxValue;
        for (int s = 2; s < SourceCount; s++)
        {
            if (p.Rings[s] == null || now - p.LastEvent[s] > 3000) continue;
            var single = new ProcFrames();
            single.Rings[s] = p.Rings[s];
            int c = RecentCount(single, nowFt, 1000);
            if (c > 0 && c < bestCount) { bestCount = c; best = s; }
        }
        return best;
    }

    private void Stats(FrameRing r, long since, out int n, out double sum, bool sortSlowest, out double low1, out double low01)
    {
        n = 0; sum = 0; low1 = 0; low01 = 0;
        if (sortSlowest && _sortBuf.Length < r.Count) _sortBuf = new float[r.Time.Length];
        for (int i = 0; i < r.Count; i++)
        {
            int idx = (r.Head - 1 - i + r.Time.Length * 2) % r.Time.Length;
            if (r.Time[idx] < since) break;
            var ms = r.Ms[idx];
            sum += ms;
            if (sortSlowest) _sortBuf[n] = ms;
            n++;
        }
        if (!sortSlowest || n == 0) return;
        var span = _sortBuf.AsSpan(0, n);
        span.Sort();
        low1 = AvgTail(span, Math.Max(1, n / 100));
        low01 = AvgTail(span, Math.Max(1, n / 1000));
    }

    private static double AvgTail(Span<float> sorted, int k)
    {
        double s = 0;
        for (int i = sorted.Length - k; i < sorted.Length; i++) s += sorted[i];
        return s / k;
    }

    /// <summary>Copies the measured app's recent frame times for the frame-time graph. Times are in <see cref="MetricSeries.Now"/> seconds.</summary>
    public int CopyFrameTimes(double windowSeconds, ref double[] times, ref double[] values)
    {
        lock (_lock)
        {
            if (_targetPid == 0 || _targetSource < 0 || !_procs.TryGetValue(_targetPid, out var p) || p.Rings[_targetSource] is not { } r) return 0;
            long nowFt = DateTime.UtcNow.ToFileTimeUtc();
            double nowSec = MetricSeries.Now;
            long since = nowFt - (long)(windowSeconds * 1000 * TicksPerMs);
            if (times.Length < r.Count) { times = new double[r.Time.Length]; values = new double[r.Time.Length]; }
            int n = 0;
            int start = (r.Head - r.Count + r.Time.Length) % r.Time.Length;
            for (int i = 0; i < r.Count; i++)
            {
                int idx = (start + i) % r.Time.Length;
                if (r.Time[idx] < since) continue;
                times[n] = nowSec - (nowFt - r.Time[idx]) / 1e7;
                values[n] = r.Ms[idx];
                n++;
            }
            return n;
        }
    }

    public long EventsReceived => _session?.EventsReceived ?? 0;

    protected override void DisposeResources()
    {
        _session?.Dispose();
        _session = null;
        lock (_lock) _procs.Clear();
        _targetPid = 0;
    }
}
