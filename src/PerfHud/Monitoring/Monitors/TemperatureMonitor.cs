using PerfHud.Core;
using PerfHud.Sensors;

namespace PerfHud.Monitoring.Monitors;

/// <summary>
/// Aggregates every available temperature sensor into "temps.all" and resolves the canonical CPU/GPU temperatures.
/// Providers (each optional and failing independently): LibreHardwareMonitor, Lenovo GameZone WMI, ACPI thermal zones.
/// </summary>
public sealed class TemperatureMonitor : MonitorBase
{
    public override string Name => "Temperatures";
    public override string[] Prefixes => new[] { "cpu.temp" };
    public override MonitorLane Lane => MonitorLane.Slow;
    public override int GetIntervalMs(MonitorContext ctx) => Math.Max(1500, ctx.Settings.Performance.SensorIntervalMs * 2);

    private sealed class Slot<T> where T : class, IDisposable
    {
        public T? Instance;
        public string Status = "Not started";
        public long RetryAt;
        public int Failures;
    }

    private readonly Slot<LhmProvider> _lhm = new();
    private readonly Slot<LenovoGameZoneProvider> _lenovo = new();
    private readonly Slot<AcpiThermalProvider> _acpi = new();
    private bool _isLenovo;

    public string ProviderStatus => $"LHM: {_lhm.Status} · Lenovo: {_lenovo.Status} · ACPI: {_acpi.Status}";

    public override void Initialize(MonitorContext ctx)
    {
        var m = ctx.Store.GetText("sys.manufacturer") ?? Wmi.First("SELECT Manufacturer FROM Win32_ComputerSystem").Str("Manufacturer") ?? "";
        _isLenovo = m.Contains("LENOVO", StringComparison.OrdinalIgnoreCase);
    }

    public override void Update(MonitorContext ctx)
    {
        var store = ctx.Store;
        var s = ctx.Settings.Sensors;
        var hub = ctx.Sensors;
        var readings = new List<SensorReading>();
        bool skipExpensive = ctx.PowerSaving && ctx.Settings.Battery.DisableExpensiveSensors;

        // ── LibreHardwareMonitor ──
        LhmSnapshot? lhm = null;
        if (s.UseLibreHardwareMonitor && !skipExpensive)
        {
            bool hasAmd = hub.Adapters.Any(a => a.IsAmd);
            lhm = Use(_lhm, () => new LhmProvider(), (LhmProvider p, out string r) => p.TryOpen(s, hasAmd, out r), p => p.Read(), p => p.Status);
        }
        else Close(_lhm, s.UseLibreHardwareMonitor ? "Paused on battery" : "Disabled in settings");
        hub.Lhm = lhm;
        if (lhm != null) readings.AddRange(lhm.Temperatures.Where(r => r.Category != TempCategory.Storage));

        // ── Lenovo GameZone ──
        double? lenovoCpu = null, lenovoGpu = null;
        if (_isLenovo && s.UseVendorWmi)
        {
            var res = Use(_lenovo, () => new LenovoGameZoneProvider(), (LenovoGameZoneProvider p, out string r) => p.TryOpen(out r),
                p => new LenovoRead(p.CpuTemp(), hub.GpuTempFromVendorApi ? null : p.GpuTemp(), p.PerformanceMode()), p => p.Status);
            if (res != null)
            {
                lenovoCpu = res.Cpu;
                lenovoGpu = res.Gpu;
                store.SetText("sys.oemmode", res.Mode);
                if (lenovoCpu is double lc) readings.Add(new SensorReading("CPU (EC)", "Lenovo", TempCategory.Cpu, lc));
                if (lenovoGpu is double lg) readings.Add(new SensorReading("GPU (EC)", "Lenovo", TempCategory.Gpu, lg));
            }
        }
        if (store.GetText("sys.oemmode") == null)
            store.SetText("sys.oemmode", _isLenovo ? (Elevation.IsAdmin ? "N/A" : "Needs admin") : "N/A");

        // ── ACPI ──
        var acpi = Use(_acpi, () => new AcpiThermalProvider(), (AcpiThermalProvider p, out string r) => p.TryOpen(out r), p => p.Read().ToList(), p => p.Status);
        if (acpi != null) foreach (var (n, t) in acpi) readings.Add(new SensorReading(n, "ACPI", TempCategory.Other, t));

        // ── Canonical CPU temperature ──
        double? cpu = lhm?.CpuPackageTemp ?? lenovoCpu ?? acpi?.Select(a => (double?)a.Celsius).Max();
        store.Set("cpu.temp", cpu, Elevation.IsAdmin
            ? "No CPU temperature source (enable LibreHardwareMonitor kernel sensors in Settings → Sensors)"
            : "CPU temperature needs administrator rights on this laptop (Settings → Diagnostics → Restart as admin)");

        // ── GPU temperature fallbacks when NVML isn't providing it ──
        if (!hub.GpuTempFromVendorApi && store.GetText("gpu.state") != "Sleeping")
        {
            var g = lhm?.GpuTemp ?? lenovoGpu;
            if (g != null) store.Set("gpu.temp", g.Value);
        }
        if (lhm?.GpuHotspot is double hs) store.Set("gpu.hotspot", hs);

        // ── Everything else already published by other monitors ──
        void AddStore(string id, string name, TempCategory c, string src)
        {
            var v = store.Get(id);
            if (!double.IsNaN(v) && !readings.Any(r => r.Name == name)) readings.Insert(0, new SensorReading(name, src, c, v));
        }
        AddStore("bat.temp", "Battery", TempCategory.Battery, "Battery");
        AddStore("gpu.hotspot", "GPU Hotspot", TempCategory.Gpu, "GPU");
        if (store.GetObject<List<DriveSnapshot>>("disk.drives") is { } drives)
            foreach (var d in drives.Where(d => !double.IsNaN(d.Temp)).Reverse())
                readings.Insert(0, new SensorReading($"SSD {d.Letter}", "Storage", TempCategory.Storage, d.Temp));
        AddStore("gpu.temp", "GPU", TempCategory.Gpu, "GPU");
        if (cpu != null) readings.Insert(0, new SensorReading("CPU Package", lhm?.CpuPackageTemp != null ? "LHM" : lenovoCpu != null ? "Lenovo EC" : "ACPI", TempCategory.Cpu, cpu.Value));

        store.SetObject("temps.all", readings);
        Detail = ProviderStatus;
    }

    private sealed record LenovoRead(double? Cpu, double? Gpu, string? Mode);

    private delegate bool TryOpen<T>(T provider, out string reason);

    /// <summary>Lazily creates a provider, retries failures with backoff, and isolates its exceptions.</summary>
    private static TResult? Use<T, TResult>(Slot<T> slot, Func<T> create, TryOpen<T> open, Func<T, TResult> read, Func<T, string> status)
        where T : class, IDisposable where TResult : class
    {
        long now = Environment.TickCount64;
        if (slot.Instance == null)
        {
            if (now < slot.RetryAt) return default;
            T? p = null;
            try
            {
                p = create();
                if (!open(p, out var reason))
                {
                    p.Dispose();
                    slot.Status = reason;
                    slot.RetryAt = now + 5 * 60_000; // capability won't change often; re-check every 5 min
                    return default;
                }
                slot.Instance = p;
                slot.Status = status(p);
                Log.Info($"Sensor provider {typeof(T).Name}: {slot.Status}");
            }
            catch (Exception ex)
            {
                p?.Dispose();
                slot.Status = $"Error: {ex.Message}";
                slot.RetryAt = now + 60_000;
                Log.Once($"provider-{typeof(T).Name}", LogLevel.Warn, $"{typeof(T).Name} failed to open: {ex.Message}");
                return default;
            }
        }
        try
        {
            var r = read(slot.Instance);
            slot.Failures = 0;
            return r;
        }
        catch (Exception ex)
        {
            if (++slot.Failures >= 3)
            {
                slot.Instance.Dispose();
                slot.Instance = null;
                slot.RetryAt = now + 30_000;
                slot.Status = $"Error: {ex.Message}";
            }
            Log.Once($"provider-read-{typeof(T).Name}", LogLevel.Warn, $"{typeof(T).Name} read failed: {ex.Message}");
            return default;
        }
    }

    private static void Close<T>(Slot<T> slot, string status) where T : class, IDisposable
    {
        slot.Instance?.Dispose();
        slot.Instance = null;
        slot.Status = status;
        slot.RetryAt = 0;
    }

    protected override void DisposeResources()
    {
        Close(_lhm, "Stopped");
        Close(_lenovo, "Stopped");
        Close(_acpi, "Stopped");
    }
}
