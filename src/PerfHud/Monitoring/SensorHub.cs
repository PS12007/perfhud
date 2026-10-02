using PerfHud.Sensors;
using PerfHud.Sensors.Native;

namespace PerfHud.Monitoring;

public sealed record SensorReading(string Name, string Source, TempCategory Category, double Value);

/// <summary>State shared between monitors (adapter list, power source, vendor sensor snapshots).</summary>
public sealed class SensorHub
{
    private List<Dxgi.Adapter>? _adapters;
    private readonly object _lock = new();

    public IReadOnlyList<Dxgi.Adapter> Adapters
    {
        get
        {
            lock (_lock)
            {
                if (_adapters == null)
                {
                    try { _adapters = Dxgi.EnumerateAdapters().Where(a => !a.IsSoftware).ToList(); }
                    catch { _adapters = new(); }
                }
                return _adapters;
            }
        }
    }

    public void InvalidateAdapters() { lock (_lock) _adapters = null; }

    /// <summary>Set by GpuMonitor when NVML provided the GPU temperature (so other sources don't override it).</summary>
    public volatile bool GpuTempFromVendorApi;

    /// <summary>Latest LibreHardwareMonitor snapshot (null if disabled/unavailable).</summary>
    public volatile LhmSnapshot? Lhm;

    /// <summary>Device name of the monitor the HUD is on (for "active monitor" info).</summary>
    public volatile string? HudMonitorDevice;

    public volatile bool OnBattery;
    public event Action<bool>? PowerSourceChanged;

    public void ReportPowerSource(bool onBattery)
    {
        if (OnBattery == onBattery) return;
        OnBattery = onBattery;
        PowerSourceChanged?.Invoke(onBattery);
    }
}
