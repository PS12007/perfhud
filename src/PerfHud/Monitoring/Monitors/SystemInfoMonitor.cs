using System.Diagnostics;
using Microsoft.Win32;
using PerfHud.Sensors;
using PerfHud.Sensors.Native;

namespace PerfHud.Monitoring.Monitors;

/// <summary>Laptop identity (mostly static) plus display, power plan and power mode (refreshed periodically).</summary>
public sealed class SystemInfoMonitor : MonitorBase
{
    public override string Name => "System Info";
    public override string[] Prefixes => Array.Empty<string>();
    public override MonitorLane Lane => MonitorLane.Slow;
    public override int GetIntervalMs(MonitorContext ctx) => 15_000;

    private volatile bool _displayDirty = true;
    private long _lastDisplay;

    public void MarkDisplayChanged() => _displayDirty = true;

    public override void Initialize(MonitorContext ctx)
    {
        var s = ctx.Store;
        var cs = Wmi.First("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
        var prod = Wmi.First("SELECT Version, Name FROM Win32_ComputerSystemProduct");
        var bios = Wmi.First("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
        var board = Wmi.First("SELECT Manufacturer, Product FROM Win32_BaseBoard");
        var os = Wmi.First("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");

        var maker = cs.Str("Manufacturer");
        s.SetText("sys.manufacturer", maker == null ? "Unknown" : Title(maker));
        // Lenovo stores the marketing name ("Legion 5 15IRX10") in ComputerSystemProduct.Version and the MTM in Model.
        var model = cs.Str("Model");
        var friendly = prod.Str("Version");
        s.SetText("sys.model", friendly != null && friendly.Length > 3 && !friendly.StartsWith("0") ? $"{friendly} ({model})" : model ?? "Unknown");
        s.SetText("sys.bios", bios.Str("SMBIOSBIOSVersion") ?? "Not accessible");
        s.SetText("sys.board", board == null ? "Not accessible" : $"{Title(board.Str("Manufacturer") ?? "")} {board.Str("Product")}".Trim());

        var caption = (os.Str("Caption") ?? "Windows").Replace("Microsoft ", "");
        string? display = null;
        try { display = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null) as string; } catch { }
        string? ubr = null;
        try { ubr = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", null)?.ToString(); } catch { }
        s.SetText("sys.os", caption);
        s.SetText("sys.osver", $"{display ?? ""} (build {os.Str("BuildNumber")}{(ubr != null ? "." + ubr : "")})".Trim());
    }

    public override void Update(MonitorContext ctx)
    {
        var s = ctx.Store;
        long now = Environment.TickCount64;

        if (_displayDirty || now - _lastDisplay > 60_000)
        {
            _displayDirty = false;
            _lastDisplay = now;
            var displays = DisplayConfig.Query();
            var dev = ctx.Sensors.HudMonitorDevice;
            var d = displays.FirstOrDefault(x => dev != null && x.GdiName.Equals(dev, StringComparison.OrdinalIgnoreCase))
                    ?? displays.FirstOrDefault(x => x.Internal) ?? displays.FirstOrDefault();
            if (d != null)
            {
                s.SetText("sys.display", $"{d.Width}×{d.Height}");
                s.Set("sys.refresh", Math.Round(d.RefreshHz));
                s.SetText("sys.monitor", displays.Count > 1 ? $"{d.FriendlyName} (+{displays.Count - 1})" : d.FriendlyName);
                s.SetText("sys.hdr", d.HdrEnabled ? $"On ({d.BitsPerColor}-bit)" : d.HdrSupported ? "Supported, off" : "Not supported");
            }
            s.SetObject("sys.displays", displays);
        }

        s.SetText("sys.powerplan", PowerPlan.ActivePlanName() ?? "Unknown");
        var mode = PowerPlan.PowerMode(); // battery-saver flag comes from the battery monitor (avoids another slow EC query)
        s.SetText("sys.powermode", s.GetText("bat.saver") == "on" ? "Battery saver" : mode ?? "N/A");
        s.Set("sys.uptime", Environment.TickCount64 / 1000.0);
    }

    private static string Title(string s) => s.Length > 1 && s == s.ToUpperInvariant() ? s[0] + s[1..].ToLowerInvariant() : s;
}

/// <summary>PerfHud's own CPU/RAM overhead; drives adaptive self-throttling.</summary>
public sealed class SelfMonitor : MonitorBase
{
    public override string Name => "PerfHud overhead";
    public override string[] Prefixes => new[] { "app." };
    public override int GetIntervalMs(MonitorContext ctx) => 2000;

    private readonly Process _self = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private long _lastTick;
    private readonly Action<double> _setThrottle;
    private double _factor = 1;

    public SelfMonitor(Action<double> setThrottle) => _setThrottle = setThrottle;

    public override void Update(MonitorContext ctx)
    {
        _self.Refresh();
        var cpu = _self.TotalProcessorTime;
        long now = Environment.TickCount64;
        if (_lastTick != 0)
        {
            double pct = (cpu - _lastCpu).TotalMilliseconds / (now - _lastTick) / Environment.ProcessorCount * 100;
            ctx.Store.Set("app.cpu", pct);

            var p = ctx.Settings.Performance;
            if (p.AdaptiveThrottle && p.CpuLimitPercent > 0)
            {
                if (pct > p.CpuLimitPercent) _factor = Math.Min(4, _factor * 1.5);
                else if (pct < p.CpuLimitPercent * 0.5) _factor = Math.Max(1, _factor / 1.25);
            }
            else _factor = 1;
            _setThrottle(_factor);
        }
        _lastCpu = cpu;
        _lastTick = now;
        ctx.Store.Set("app.mem", _self.PrivateMemorySize64);
    }

    public double ThrottleFactor => _factor;
}
