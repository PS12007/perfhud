using System.IO;
using PerfHud.Sensors.Native;

namespace PerfHud.Monitoring.Monitors;

public sealed record DriveSnapshot(string Letter, string Label, string? Model, double Total, double Free,
    double Read, double Write, double Active, double Temp, bool IsSystem);

/// <summary>Per-volume space, throughput and activity (perf counters), plus drive temperatures (storage IOCTL, no admin).</summary>
public sealed class DiskMonitor : MonitorBase
{
    public override string Name => "Storage";
    public override string[] Prefixes => new[] { "disk." };

    private PdhQuery? _q;
    private PdhCounter? _read, _write, _idle;
    private readonly List<(string Name, double Value)> _buf = new();
    private readonly Dictionary<string, (double r, double w, double a)> _io = new(StringComparer.OrdinalIgnoreCase);
    private List<DriveInfo> _drives = new();
    private long _lastDriveScan;
    private long _lastTempScan;
    private readonly Dictionary<char, int> _diskNumbers = new();
    private readonly Dictionary<char, string?> _models = new();
    private readonly Dictionary<int, double> _temps = new();
    private readonly char _sysDrive = char.ToUpperInvariant((Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "C")[0]);

    public override void Initialize(MonitorContext ctx)
    {
        _q = new PdhQuery();
        _read = _q.TryAdd(@"\LogicalDisk(*)\Disk Read Bytes/sec");
        _write = _q.TryAdd(@"\LogicalDisk(*)\Disk Write Bytes/sec");
        _idle = _q.TryAdd(@"\LogicalDisk(*)\% Idle Time");
        _q.Collect();
        _lastDriveScan = 0;
        _lastTempScan = 0;
        Detail = "PDH + storage IOCTL";
    }

    public override void Update(MonitorContext ctx)
    {
        var store = ctx.Store;
        long now = Environment.TickCount64;

        if (now - _lastDriveScan > 15_000)
        {
            _drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && SafeReady(d)).ToList();
            _lastDriveScan = now;
            foreach (var d in _drives)
            {
                var l = d.Name[0];
                if (!_diskNumbers.ContainsKey(l))
                {
                    int n = StorageDevice.GetDiskNumber(l);
                    _diskNumbers[l] = n;
                    _models[l] = n >= 0 ? StorageDevice.GetModel(n) : null;
                }
            }
        }

        _q?.Collect();
        _io.Clear();
        Read(_read, 0); Read(_write, 1); Read(_idle, 2);

        // Temperatures (slow; every 10 s)
        bool tempsWanted = ctx.Settings.Sensors.DiskTemperatures && !(ctx.PowerSaving && ctx.Settings.Battery.DisableExpensiveSensors);
        if (tempsWanted && now - _lastTempScan > 10_000)
        {
            _lastTempScan = now;
            foreach (var n in _diskNumbers.Values.Where(n => n >= 0).Distinct())
            {
                var t = StorageDevice.GetTemperature(n);
                if (t.HasValue) _temps[n] = t.Value; else _temps.Remove(n);
            }
            // Fill gaps from LibreHardwareMonitor SMART data (elevated only)
            if (_temps.Count == 0 && ctx.Sensors.Lhm is { } lhm)
            {
                var st = lhm.Temperatures.Where(r => r.Category == TempCategory.Storage).ToList();
                for (int i = 0; i < st.Count; i++) _temps[1000 + i] = st[i].Value;
            }
        }

        var snaps = new List<DriveSnapshot>();
        double totalR = 0, totalW = 0, maxActive = 0;
        foreach (var d in _drives)
        {
            var l = d.Name[0];
            double total, free;
            try { total = d.TotalSize; free = d.AvailableFreeSpace; }
            catch { continue; }
            var key = $"{l}:";
            _io.TryGetValue(key, out var io);
            double active = Math.Clamp(100 - io.a, 0, 100);
            int dn = _diskNumbers.GetValueOrDefault(l, -1);
            double temp = dn >= 0 && _temps.TryGetValue(dn, out var tv) ? tv : double.NaN;

            var ll = char.ToLowerInvariant(l);
            store.Set($"disk.{ll}.total", total);
            store.Set($"disk.{ll}.free", free);
            store.Set($"disk.{ll}.used", total - free);
            store.Set($"disk.{ll}.pct", total > 0 ? (total - free) / total * 100 : 0);
            store.Set($"disk.{ll}.read", io.r);
            store.Set($"disk.{ll}.write", io.w);
            store.Set($"disk.{ll}.active", active);
            store.Set($"disk.{ll}.temp", double.IsNaN(temp) ? null : temp, "Drive doesn't report temperature");

            totalR += io.r; totalW += io.w; maxActive = Math.Max(maxActive, active);
            string label;
            try { label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel; } catch { label = "Disk"; }
            snaps.Add(new DriveSnapshot($"{l}:", label, _models.GetValueOrDefault(l), total, free, io.r, io.w, active, temp, l == _sysDrive));

            if (l == _sysDrive)
            {
                store.Set("disk.sys.free", free);
                store.Set("disk.sys.total", total);
                store.Set("disk.sys.pct", total > 0 ? (total - free) / total * 100 : 0);
            }
        }

        store.Set("disk.read", totalR);
        store.Set("disk.write", totalW);
        store.Set("disk.active", maxActive);
        var hottest = _temps.Count > 0 ? _temps.Values.Max() : double.NaN;
        store.Set("disk.temp", double.IsNaN(hottest) ? null : hottest,
            tempsWanted ? "Drives don't report temperature (SATA needs admin)" : "Disabled (battery saver or settings)");
        store.SetObject("disk.drives", snaps);
    }

    private void Read(PdhCounter? c, int field)
    {
        if (c == null) return;
        c.Values(_buf);
        foreach (var (n, v) in _buf)
        {
            if (n.Length != 2 || n[1] != ':') continue;
            var cur = _io.GetValueOrDefault(n, (0, 0, 100));
            _io[n] = field switch { 0 => (v, cur.w, cur.a), 1 => (cur.r, v, cur.a), _ => (cur.r, cur.w, Math.Min(100, v)) };
        }
    }

    private static bool SafeReady(DriveInfo d) { try { return d.IsReady; } catch { return false; } }

    protected override void DisposeResources()
    {
        _q?.Dispose(); _q = null;
        _diskNumbers.Clear();
    }
}
