using PerfHud.Sensors.Native;

namespace PerfHud.Monitoring.Monitors;

/// <summary>
/// Battery: Windows power status + direct battery IOCTLs (capacity, voltage, rate, cycles, health, temperature if exposed).
/// Also publishes AC/battery state, which drives battery-saving mode.
/// </summary>
public sealed class BatteryMonitor : MonitorBase
{
    public override string Name => "Battery";
    public override string[] Prefixes => new[] { "bat." };
    // Battery firmware (ACPI/EC) answers slowly (~50 ms per query on many laptops), and values change slowly anyway.
    // AC plug/unplug is picked up instantly via SystemEvents (AppHost kicks the monitors).
    public override int GetIntervalMs(MonitorContext ctx) => Math.Max(3000, ctx.Settings.Performance.SensorIntervalMs * 3);

    private List<BatteryDevice> _devices = new();
    private BatteryDevice.BatteryInformation? _info;
    private long _lastInfo;
    private double _smoothedPowerW = double.NaN;
    private long _nextTempQuery;
    private double? _lastTemp;

    public override void Initialize(MonitorContext ctx)
    {
        Win32.GetSystemPowerStatus(out var ps);
        _devices = BatteryDevice.Enumerate();
        if ((ps.BatteryFlag & 128) != 0 && _devices.Count == 0)
        {
            ctx.Sensors.ReportPowerSource(false);
            throw new MonitorUnavailableException("No battery detected (desktop or battery removed)");
        }
        Detail = _devices.Count > 0 ? $"{_devices.Count} battery device(s) via IOCTL" : "Windows power status only";
        _lastInfo = 0;
    }

    public override void Update(MonitorContext ctx)
    {
        var s = ctx.Store;
        if (!Win32.GetSystemPowerStatus(out var ps)) throw new InvalidOperationException("GetSystemPowerStatus failed");

        bool ac = ps.ACLineStatus == 1;
        ctx.Sensors.ReportPowerSource(!ac);
        s.SetText("bat.source", ac ? "AC" : "Battery");

        var dev = _devices.FirstOrDefault();
        long now = Environment.TickCount64;
        if (dev != null && (_info == null || now - _lastInfo > 60_000))
        {
            _info = dev.QueryInformation();
            _lastInfo = now;
        }
        var st = dev?.QueryStatus();

        double pct = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : double.NaN;
        bool charging = false, discharging = !ac;
        double rateW = double.NaN, volts = double.NaN;

        if (st is { } b)
        {
            charging = (b.PowerState & BatteryDevice.BATTERY_CHARGING) != 0;
            discharging = (b.PowerState & BatteryDevice.BATTERY_DISCHARGING) != 0;
            if (b.Voltage != BatteryDevice.UNKNOWN && b.Voltage > 0) volts = b.Voltage / 1000.0;
            if (b.Rate != BatteryDevice.UNKNOWN_RATE) rateW = b.Rate / 1000.0;

            if (_info is { } inf && !inf.IsRelative)
            {
                if (inf.FullChargedCapacity > 0 && b.Capacity != BatteryDevice.UNKNOWN)
                    pct = Math.Clamp(b.Capacity * 100.0 / inf.FullChargedCapacity, 0, 100);
                s.Set("bat.remaining", b.Capacity != BatteryDevice.UNKNOWN ? b.Capacity / 1000.0 : null, "Not reported");
                s.Set("bat.full", inf.FullChargedCapacity > 0 ? inf.FullChargedCapacity / 1000.0 : null, "Not reported");
                s.Set("bat.design", inf.DesignedCapacity > 0 ? inf.DesignedCapacity / 1000.0 : null, "Not reported");
                s.Set("bat.health", inf.DesignedCapacity > 0 && inf.FullChargedCapacity > 0
                    ? Math.Min(100, inf.FullChargedCapacity * 100.0 / inf.DesignedCapacity) : null, "Design capacity not reported");
            }
            else
            {
                const string rel = "Battery reports relative capacity only";
                s.SetUnavailable("bat.remaining", rel); s.SetUnavailable("bat.full", rel); s.SetUnavailable("bat.design", rel); s.SetUnavailable("bat.health", rel);
            }
            s.Set("bat.cycles", _info is { CycleCount: > 0 } i2 ? i2.CycleCount : null, "Cycle count not reported by firmware");
            if (now >= _nextTempQuery)
            {
                _lastTemp = dev!.QueryTemperature();
                // Most firmware doesn't implement battery temperature; don't keep asking every poll.
                _nextTempQuery = now + (_lastTemp.HasValue ? 15_000 : 300_000);
            }
            s.Set("bat.temp", _lastTemp, "Battery firmware doesn't report temperature");
        }
        else
        {
            const string na = "Battery device not accessible";
            foreach (var id in new[] { "bat.remaining", "bat.full", "bat.design", "bat.health", "bat.cycles", "bat.temp" }) s.SetUnavailable(id, na);
        }

        s.Set("bat.pct", double.IsNaN(pct) ? null : pct, "Charge level unknown");
        s.Set("bat.voltage", double.IsNaN(volts) ? null : volts, "Voltage not reported");

        // Power flow: positive = charging, negative = discharging
        if (!double.IsNaN(rateW))
        {
            _smoothedPowerW = double.IsNaN(_smoothedPowerW) ? rateW : _smoothedPowerW * 0.6 + rateW * 0.4;
            s.Set("bat.power", discharging ? Math.Abs(_smoothedPowerW) : 0);
            s.Set("bat.rate", charging ? Math.Abs(_smoothedPowerW) : 0);
            s.Set("bat.current", !double.IsNaN(volts) && volts > 0 ? Math.Abs(rateW) / volts : null, "Voltage unknown");
        }
        else
        {
            s.SetUnavailable("bat.power", "Discharge rate not reported");
            s.SetUnavailable("bat.rate", "Charge rate not reported");
            s.SetUnavailable("bat.current", "Rate not reported");
        }

        // Time remaining / to full
        double secs = double.NaN;
        string state;
        if (charging)
        {
            state = "Charging";
            if (_info is { IsRelative: false } inf && st is { } b2 && rateW > 0.5 && b2.Capacity != BatteryDevice.UNKNOWN)
                secs = (inf.FullChargedCapacity - b2.Capacity) / 1000.0 / Math.Abs(_smoothedPowerW) * 3600;
        }
        else if (discharging)
        {
            state = ps.SystemStatusFlag == 1 ? "Battery saver" : "Discharging";
            if (ps.BatteryLifeTime > 0) secs = ps.BatteryLifeTime;
            else if (st is { } b3 && Math.Abs(_smoothedPowerW) > 0.5 && b3.Capacity != BatteryDevice.UNKNOWN && _info is { IsRelative: false })
                secs = b3.Capacity / 1000.0 / Math.Abs(_smoothedPowerW) * 3600;
        }
        else state = pct >= 99 ? "Full" : "Plugged in, not charging";

        s.SetText("bat.state", state);
        s.SetText("bat.saver", ps.SystemStatusFlag == 1 ? "on" : "off");
        s.Set("bat.time", double.IsNaN(secs) ? null : secs, charging ? "Calculating…" : ac ? "On AC power" : "Calculating…");
    }

    protected override void DisposeResources()
    {
        foreach (var d in _devices) d.Dispose();
        _devices.Clear();
        _info = null;
        _smoothedPowerW = double.NaN;
        _nextTempQuery = 0;
    }
}
