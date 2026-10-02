using PerfHud.Sensors;
using PerfHud.Sensors.Native;

namespace PerfHud.Monitoring.Monitors;

/// <summary>
/// CPU load (total + per logical processor, matching Task Manager's "% Processor Utility"),
/// effective clock, package power (RAPL via the Windows "Energy Meter" counters — no admin needed).
/// CPU temperature comes from <see cref="TemperatureMonitor"/>.
/// </summary>
public sealed class CpuMonitor : MonitorBase
{
    public override string Name => "CPU";
    public override string[] Prefixes => new[] { "cpu.usage", "cpu.clock", "cpu.power", "cpu.core." };

    private PdhQuery? _q;
    private PdhCounter? _util, _perf, _energy, _procs;
    private readonly List<(string Name, double Value)> _buf = new();
    private double _baseMhz;
    private double _peakMhz;
    private double[] _cores = Array.Empty<double>();

    public override void Initialize(MonitorContext ctx)
    {
        var store = ctx.Store;
        var cpu = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        if (cpu.Count > 0)
        {
            store.SetText("cpu.name", CleanName(cpu[0].Str("Name") ?? "Unknown CPU"));
            store.Set("cpu.cores", cpu.Sum(c => c.Num("NumberOfCores") ?? 0));
            store.Set("cpu.threads", cpu.Sum(c => c.Num("NumberOfLogicalProcessors") ?? 0));
            _baseMhz = cpu[0].Num("MaxClockSpeed") ?? 0;
            if (_baseMhz > 0) store.Set("cpu.clock.base", _baseMhz);
        }
        else
        {
            store.SetText("cpu.name", Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU");
            store.Set("cpu.threads", Environment.ProcessorCount);
        }

        _q = new PdhQuery();
        _util = _q.TryAdd(@"\Processor Information(*)\% Processor Utility") ?? _q.Add(@"\Processor Information(*)\% Processor Time");
        _perf = _q.TryAdd(@"\Processor Information(_Total)\% Processor Performance");
        _procs = _q.TryAdd(@"\System\Processes");
        _energy = PdhQuery.CounterExists(@"\Energy Meter(*)\Power") ? _q.TryAdd(@"\Energy Meter(*)\Power") : null;
        _q.Collect();
        Detail = _energy != null ? "PDH + RAPL energy meter" : "PDH (package power via LHM only)";
    }

    public override void Update(MonitorContext ctx)
    {
        var store = ctx.Store;
        if (_q == null || !_q.Collect()) throw new InvalidOperationException("PDH collect failed");

        // Load
        _util!.Values(_buf);
        double total = double.NaN;
        int maxIndex = -1;
        foreach (var (n, _) in _buf) if (TryCoreIndex(n, out var i)) maxIndex = Math.Max(maxIndex, i);
        if (_cores.Length != maxIndex + 1) _cores = new double[maxIndex + 1];
        foreach (var (n, v) in _buf)
        {
            if (n == "_Total") total = Math.Clamp(v, 0, 100);
            else if (TryCoreIndex(n, out var i)) _cores[i] = Math.Clamp(v, 0, 100);
        }
        if (double.IsNaN(total) && _cores.Length > 0) total = _cores.Average();
        store.Set("cpu.usage", total, "No CPU load counter");
        store.SetObject("cpu.cores", (double[])_cores.Clone());
        for (int i = 0; i < _cores.Length; i++) store.Set($"cpu.core.{i}", _cores[i]);

        // Effective clock = base clock × % Processor Performance (same method Task Manager uses for "Speed")
        double perf = _perf?.Value() ?? double.NaN;
        var lhm = ctx.Sensors.Lhm;
        if (!double.IsNaN(perf) && _baseMhz > 0)
        {
            var mhz = _baseMhz * perf / 100.0;
            store.Set("cpu.clock", mhz);
            _peakMhz = Math.Max(_peakMhz, mhz);
        }
        else if (lhm?.CpuMaxClockMHz is double lc)
        {
            store.Set("cpu.clock", lc);
            _peakMhz = Math.Max(_peakMhz, lc);
        }
        else store.SetUnavailable("cpu.clock", "No processor performance counter");
        if (_peakMhz > 0) store.Set("cpu.clock.peak", _peakMhz);

        // Package power
        double? watts = null;
        if (_energy != null)
        {
            _energy.Values(_buf);
            foreach (var (n, v) in _buf)
                if (n.Contains("pkg", StringComparison.OrdinalIgnoreCase) || n.Contains("package", StringComparison.OrdinalIgnoreCase) && !n.Contains("dram"))
                {
                    if (v > 0) watts = v / 1000.0; // counter is in milliwatts
                    break;
                }
        }
        watts ??= lhm?.CpuPackagePower;
        store.Set("cpu.power", watts, _energy == null
            ? "No RAPL energy counters; enable LibreHardwareMonitor kernel sensors (admin)"
            : "Energy meter returned no package reading");

        var procs = _procs?.Value() ?? double.NaN;
        if (!double.IsNaN(procs)) store.Set("cpu.procs", procs);
    }

    /// <summary>Instance names look like "0,7" (group,index). Returns the flat logical-processor index.</summary>
    private static bool TryCoreIndex(string name, out int index)
    {
        index = -1;
        var comma = name.IndexOf(',');
        if (comma < 0 || name.EndsWith("_Total")) return false;
        if (!int.TryParse(name.AsSpan(0, comma), out var group) || !int.TryParse(name.AsSpan(comma + 1), out var n)) return false;
        index = group * 64 + n;
        return index < 1024;
    }

    private static string CleanName(string n) =>
        System.Text.RegularExpressions.Regex.Replace(n.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "").Replace("  ", " "), @"\s+@.*$", "").Trim();

    protected override void DisposeResources()
    {
        _q?.Dispose();
        _q = null;
    }
}
