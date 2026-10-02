using PerfHud.Sensors.Native;

namespace PerfHud.Monitoring.Monitors;

public sealed record GpuSnapshot(string Name, string Vendor, bool Discrete, double Usage, double VramUsed, double VramTotal, string State);

/// <summary>
/// GPU metrics. Sources, in order of preference:
///  • NVIDIA: NVML (usage, temp, power, clocks, VRAM, fan, throttle reasons) — skipped while the dGPU sleeps (D3) to avoid waking it.
///  • AMD: LibreHardwareMonitor (ADL) when enabled.
///  • Any vendor: Windows GPU perf counters (usage per engine, dedicated/shared memory) — same data as Task Manager.
/// </summary>
public sealed class GpuMonitor : MonitorBase
{
    public override string Name => "GPU";
    public override string[] Prefixes => new[] { "gpu.", "igpu." };
    public override bool IsExpensive => true; // the GPU Engine counter set is large

    private PdhQuery? _q;
    private PdhCounter? _engine, _dedicated, _shared;
    private readonly List<(string Name, double Value)> _buf = new();
    private readonly Dictionary<string, double> _engineSums = new();
    private readonly Dictionary<string, double> _luidUsage = new(StringComparer.OrdinalIgnoreCase);
    private DevicePowerState? _power;
    private Dxgi.Adapter? _primary, _integrated;
    private IntPtr _nvDevice;
    private bool _nvmlInit;
    private string? _nvmlError;
    private int _nvTempFailures;

    public override void Initialize(MonitorContext ctx)
    {
        var adapters = ctx.Sensors.Adapters;
        if (adapters.Count == 0) throw new MonitorUnavailableException("No hardware GPU adapters found");

        _primary = adapters.FirstOrDefault(a => a.IsNvidia || (a.IsAmd && a.DedicatedVideoMemory > 1UL << 30))
                   ?? adapters.OrderByDescending(a => a.DedicatedVideoMemory).First();
        _integrated = adapters.FirstOrDefault(a => a != _primary && (a.IsIntel || a.IsAmd));

        ctx.Store.SetText("gpu.name", Clean(_primary.Name));
        ctx.Store.SetText("igpu.name", _integrated != null ? Clean(_integrated.Name) : null);
        ctx.Store.SetText("sys.gpus", string.Join(" + ", adapters.Select(a => Clean(a.Name))));
        if (_primary.DedicatedVideoMemory > 0) ctx.Store.Set("gpu.vram.total", _primary.DedicatedVideoMemory);

        _q = new PdhQuery();
        _engine = _q.TryAdd(@"\GPU Engine(*)\Utilization Percentage");
        _dedicated = _q.TryAdd(@"\GPU Adapter Memory(*)\Dedicated Usage");
        _shared = _q.TryAdd(@"\GPU Adapter Memory(*)\Shared Usage");
        _q.Collect();

        try { _power = new DevicePowerState(); } catch { _power = null; }

        Detail = _primary.IsNvidia ? (Nvml.IsInstalled ? "NVML + perf counters" : "Perf counters (nvml.dll missing)")
               : _primary.IsAmd ? "Perf counters + LHM (AMD)" : "Perf counters";
    }

    public override void Update(MonitorContext ctx)
    {
        var store = ctx.Store;
        var s = ctx.Settings.Sensors;
        if (_q == null || _primary == null) return;
        _q.Collect();

        // ── Usage from GPU Engine counters (Task Manager method: max over engines of per-engine sums) ──
        _luidUsage.Clear();
        if (_engine != null && _engine.Values(_buf) > 0)
        {
            _engineSums.Clear();
            foreach (var (n, v) in _buf)
            {
                int l = n.IndexOf("luid_", StringComparison.Ordinal);
                int e = n.IndexOf("_eng_", StringComparison.Ordinal);
                int et = n.IndexOf("_engtype", StringComparison.Ordinal);
                if (l < 0 || e < 0) continue;
                var key = string.Concat(n.AsSpan(l, (e - l)), et > e ? n.AsSpan(e, et - e) : n.AsSpan(e));
                _engineSums[key] = _engineSums.GetValueOrDefault(key) + v;
            }
            foreach (var (key, v) in _engineSums)
            {
                int p = key.IndexOf("_phys", StringComparison.Ordinal);
                var luid = p > 0 ? key[..p] : key;
                _luidUsage[luid] = Math.Max(_luidUsage.GetValueOrDefault(luid), Math.Min(100, v));
            }
        }
        double pdhPrimary = _luidUsage.TryGetValue(_primary.PdhLuid, out var pu) ? pu : (_engine != null ? 0 : double.NaN);
        double dedicatedUsed = SumFor(_dedicated, _primary.PdhLuid);

        // ── Discrete GPU power state ──
        int dstate = 0;
        if (_power != null && (_primary.IsNvidia || _primary.IsAmd))
            dstate = _power.GetPowerState(_primary.VendorTag);
        bool sleeping = dstate >= 3 && s.AvoidWakingDiscreteGpu;
        store.SetText("gpu.state", dstate switch { 1 => "Active", >= 3 => "Sleeping", _ => "Active" });

        bool haveVendor = false;
        if (_primary.IsNvidia && Nvml.IsInstalled && !sleeping)
            haveVendor = ReadNvml(store);
        else if (_primary.IsNvidia && sleeping)
        {
            ShutdownNvml();
            ctx.Sensors.GpuTempFromVendorApi = false;
            const string why = "dGPU is asleep (not polled, to save battery)";
            store.Set("gpu.usage", 0);
            store.SetUnavailable("gpu.temp", why);
            store.SetUnavailable("gpu.power", why);
            store.SetUnavailable("gpu.clock", why);
            store.SetUnavailable("gpu.memclock", why);
            store.SetUnavailable("gpu.fan", why);
            store.SetUnavailable("gpu.hotspot", why);
            store.SetText("gpu.throttle", null);
            store.SetText("gpu.pstate", "D3");
            haveVendor = true;
            if (!double.IsNaN(dedicatedUsed)) SetVram(store, dedicatedUsed, _primary.DedicatedVideoMemory);
        }
        else if (_primary.IsAmd && ctx.Sensors.Lhm is { } lhm && lhm.GpuLoad != null)
        {
            store.Set("gpu.usage", lhm.GpuLoad.Value);
            store.Set("gpu.temp", lhm.GpuTemp, "No AMD temperature sensor");
            store.Set("gpu.hotspot", lhm.GpuHotspot, "No hotspot sensor");
            store.Set("gpu.power", lhm.GpuPower, "No power sensor");
            store.Set("gpu.clock", lhm.GpuCoreClock, "No clock sensor");
            store.Set("gpu.memclock", lhm.GpuMemClock, "No memory clock sensor");
            if (lhm.GpuVramUsedMB is double mu && lhm.GpuVramTotalMB is double mt)
                SetVram(store, mu * 1024 * 1024, mt * 1024 * 1024);
            ctx.Sensors.GpuTempFromVendorApi = lhm.GpuTemp != null;
            haveVendor = true;
        }

        if (!haveVendor)
        {
            // Generic path (Intel Arc / AMD without LHM / NVIDIA without NVML)
            store.Set("gpu.usage", double.IsNaN(pdhPrimary) ? null : pdhPrimary, "GPU perf counters unavailable");
            if (!double.IsNaN(dedicatedUsed)) SetVram(store, dedicatedUsed, _primary.DedicatedVideoMemory);
            var why = _primary.IsNvidia ? (_nvmlError ?? "NVIDIA driver NVML not found") : "Not exposed by this GPU's driver without extended sensors";
            if (!ctx.Sensors.GpuTempFromVendorApi && ctx.Sensors.Lhm?.GpuTemp == null) store.SetUnavailable("gpu.temp", why);
            store.SetUnavailable("gpu.power", why);
            store.SetUnavailable("gpu.clock", why);
            store.SetUnavailable("gpu.memclock", why);
            store.SetUnavailable("gpu.fan", why);
        }

        // ── Integrated GPU ──
        if (_integrated != null)
        {
            store.Set("igpu.usage", _luidUsage.TryGetValue(_integrated.PdhLuid, out var iu) ? iu : 0);
            var sh = SumFor(_shared, _integrated.PdhLuid);
            store.Set("igpu.mem", double.IsNaN(sh) ? null : sh, "No shared memory counter");
        }
        else
        {
            store.SetUnavailable("igpu.usage", "No integrated GPU");
            store.SetUnavailable("igpu.mem", "No integrated GPU");
        }

        var list = new List<GpuSnapshot>
        {
            new(Clean(_primary.Name), Vendor(_primary), true, store.Get("gpu.usage"), store.Get("gpu.vram.used"), store.Get("gpu.vram.total"), store.GetText("gpu.state") ?? ""),
        };
        if (_integrated != null)
            list.Add(new(Clean(_integrated.Name), Vendor(_integrated), false, store.Get("igpu.usage"), store.Get("igpu.mem"), double.NaN, "Active"));
        store.SetObject("gpu.list", list);
    }

    private bool ReadNvml(MetricStore store)
    {
        if (!_nvmlInit)
        {
            int r = Nvml.Init();
            if (r != Nvml.SUCCESS) { _nvmlError = $"NVML init failed ({r})"; return false; }
            _nvmlInit = true;
            if (Nvml.DeviceGetCount(out var cnt) != Nvml.SUCCESS || cnt == 0) { _nvmlError = "NVML reports no devices"; ShutdownNvml(); return false; }
            Nvml.DeviceGetHandleByIndex(0, out _nvDevice);
            var name = Nvml.GetName(_nvDevice);
            if (name != null) store.SetText("gpu.name", Clean(name));
            _nvmlError = null;
        }
        var d = _nvDevice;

        if (Nvml.DeviceGetUtilizationRates(d, out var u) == Nvml.SUCCESS) store.Set("gpu.usage", u.Gpu);
        else throw new InvalidOperationException("NVML utilization query failed (driver reset?)");

        if (Nvml.DeviceGetTemperature(d, Nvml.NVML_TEMPERATURE_GPU, out var t) == Nvml.SUCCESS && t > 0)
        {
            store.Set("gpu.temp", t);
            _nvTempFailures = 0;
        }
        else if (++_nvTempFailures > 2) store.SetUnavailable("gpu.temp", "NVML temperature not available");

        store.SetUnavailable("gpu.hotspot", "Hotspot isn't exposed by NVML on this GPU");
        if (Nvml.DeviceGetMemoryInfo(d, out var m) == Nvml.SUCCESS) SetVram(store, m.Used, m.Total);
        store.Set("gpu.power", Nvml.DeviceGetPowerUsage(d, out var mw) == Nvml.SUCCESS ? mw / 1000.0 : null, "Power reading not supported");
        store.Set("gpu.power.limit", Nvml.DeviceGetEnforcedPowerLimit(d, out var lim) == Nvml.SUCCESS ? lim / 1000.0 : null, "No power limit reported");
        store.Set("gpu.clock", Nvml.DeviceGetClockInfo(d, Nvml.NVML_CLOCK_GRAPHICS, out var gc) == Nvml.SUCCESS ? gc : null, "No clock reading");
        store.Set("gpu.memclock", Nvml.DeviceGetClockInfo(d, Nvml.NVML_CLOCK_MEM, out var mc) == Nvml.SUCCESS ? mc : null, "No memory clock reading");
        store.Set("gpu.fan", Nvml.DeviceGetFanSpeed(d, out var fan) == Nvml.SUCCESS ? fan : null, "Fan is controlled by the laptop EC (not reported by the GPU)");
        store.SetText("gpu.pstate", Nvml.DeviceGetPerformanceState(d, out var ps) == Nvml.SUCCESS && ps >= 0 && ps < 32 ? $"P{ps}" : null);

        string? throttle = null;
        if (Nvml.DeviceGetCurrentClocksThrottleReasons(d, out var reasons) == Nvml.SUCCESS)
        {
            if ((reasons & (Nvml.HwThermalSlowdown | Nvml.SwThermalSlowdown)) != 0) throttle = "THERMAL";
            else if ((reasons & (Nvml.HwSlowdown | Nvml.HwPowerBrakeSlowdown)) != 0) throttle = "SLOWDOWN";
        }
        store.SetText("gpu.throttle", throttle);
        return true;
    }

    private static void SetVram(MetricStore store, double used, double total)
    {
        store.Set("gpu.vram.used", used);
        if (total > 0)
        {
            store.Set("gpu.vram.total", total);
            store.Set("gpu.vram.pct", used / total * 100);
        }
    }

    private double SumFor(PdhCounter? c, string luid)
    {
        if (c == null || c.Values(_buf) == 0) return double.NaN;
        double sum = 0; bool any = false;
        foreach (var (n, v) in _buf)
            if (n.Contains(luid, StringComparison.OrdinalIgnoreCase)) { sum += v; any = true; }
        return any ? sum : double.NaN;
    }

    private void ShutdownNvml()
    {
        if (_nvmlInit) { try { Nvml.Shutdown(); } catch { } }
        _nvmlInit = false;
        _nvDevice = IntPtr.Zero;
    }

    private static string Vendor(Dxgi.Adapter a) => a.IsNvidia ? "NVIDIA" : a.IsAmd ? "AMD" : a.IsIntel ? "Intel" : $"0x{a.VendorId:X4}";

    public static string Clean(string n) => n.Replace("(R)", "").Replace("(TM)", "").Replace("NVIDIA GeForce", "GeForce").Replace("  ", " ").Trim();

    protected override void DisposeResources()
    {
        ShutdownNvml();
        _q?.Dispose(); _q = null;
        _power?.Dispose(); _power = null;
    }
}
