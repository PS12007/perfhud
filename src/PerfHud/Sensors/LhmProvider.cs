using LibreHardwareMonitor.Hardware;
using PerfHud.Core;
using PerfHud.Monitoring;
using PerfHud.Settings;

namespace PerfHud.Sensors;

public sealed class LhmSnapshot
{
    public List<SensorReading> Temperatures { get; } = new();
    public double? CpuPackageTemp, CpuPackagePower, CpuMaxClockMHz;
    public double? GpuTemp, GpuHotspot, GpuLoad, GpuPower, GpuCoreClock, GpuMemClock, GpuVramUsedMB, GpuVramTotalMB, GpuFanRpm;
    public string? GpuName;
}

/// <summary>
/// Optional extended sensors via LibreHardwareMonitorLib.
/// Groups that require a kernel driver (CPU/motherboard) are only enabled when the user opts in AND PerfHud is elevated.
/// NVIDIA GPUs are deliberately excluded here (NVML is used directly, and LHM's NVAPI path would wake a sleeping Optimus dGPU).
/// </summary>
public sealed class LhmProvider : IDisposable
{
    private Computer? _computer;
    public string Status { get; private set; } = "Not started";

    public bool TryOpen(SensorSettings s, bool hasAmdGpu, out string reason)
    {
        bool kernel = s.AllowKernelDriver && Elevation.IsAdmin;
        var c = new Computer
        {
            IsCpuEnabled = kernel,
            IsMotherboardEnabled = kernel,
            IsGpuEnabled = hasAmdGpu,
            IsStorageEnabled = Elevation.IsAdmin && s.DiskTemperatures,
            IsMemoryEnabled = false,
            IsNetworkEnabled = false,
            IsBatteryEnabled = false,
            IsControllerEnabled = false,
            IsPsuEnabled = false,
        };
        if (!c.IsCpuEnabled && !c.IsMotherboardEnabled && !c.IsGpuEnabled && !c.IsStorageEnabled)
        {
            reason = Elevation.IsAdmin
                ? "No LibreHardwareMonitor groups needed (enable the kernel driver option for CPU sensors)"
                : "LibreHardwareMonitor extended sensors need administrator rights on this machine";
            Status = reason;
            return false;
        }
        c.Open();
        _computer = c;
        var groups = new List<string>();
        if (c.IsCpuEnabled) groups.Add("CPU");
        if (c.IsMotherboardEnabled) groups.Add("Motherboard");
        if (c.IsGpuEnabled) groups.Add("AMD GPU");
        if (c.IsStorageEnabled) groups.Add("Storage SMART");
        Status = "Active: " + string.Join(", ", groups);
        reason = "";
        return true;
    }

    public LhmSnapshot Read()
    {
        var snap = new LhmSnapshot();
        if (_computer == null) return snap;
        foreach (var hw in _computer.Hardware)
        {
            try
            {
                hw.Update();
                foreach (var sub in hw.SubHardware) sub.Update();
                Collect(hw, snap);
                foreach (var sub in hw.SubHardware) Collect(sub, snap);
            }
            catch (Exception ex)
            {
                Log.Once($"lhm-{hw.Name}", LogLevel.Warn, $"LHM read failed for {hw.Name}: {ex.Message}");
            }
        }
        return snap;
    }

    private static void Collect(IHardware hw, LhmSnapshot snap)
    {
        var cat = hw.HardwareType switch
        {
            HardwareType.Cpu => TempCategory.Cpu,
            HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => TempCategory.Gpu,
            HardwareType.Storage => TempCategory.Storage,
            HardwareType.Battery => TempCategory.Battery,
            _ => TempCategory.Other,
        };
        bool isGpu = cat == TempCategory.Gpu;
        if (isGpu) snap.GpuName ??= hw.Name;

        foreach (var s in hw.Sensors)
        {
            if (s.Value is not float fv || float.IsNaN(fv)) continue;
            double v = fv;
            var n = s.Name;
            switch (s.SensorType)
            {
                case SensorType.Temperature:
                    if (v <= 0 || v > 150) break;
                    snap.Temperatures.Add(new SensorReading(cat == TempCategory.Storage ? $"{hw.Name}" : $"{ShortHw(hw)} {n}", "LHM", cat, v));
                    if (cat == TempCategory.Cpu && (n.Contains("Package") || n.Contains("Tctl") || n.Contains("Tdie"))) snap.CpuPackageTemp ??= v;
                    if (cat == TempCategory.Cpu && n == "Core Max") snap.CpuPackageTemp ??= v;
                    if (isGpu && n.Contains("Hot Spot")) snap.GpuHotspot = v;
                    else if (isGpu && n.Contains("Core")) snap.GpuTemp ??= v;
                    break;
                case SensorType.Power:
                    if (cat == TempCategory.Cpu && n.Contains("Package")) snap.CpuPackagePower = v;
                    if (isGpu && (n.Contains("Package") || n.Contains("Total") || n.Contains("Core"))) snap.GpuPower ??= v;
                    break;
                case SensorType.Clock:
                    if (cat == TempCategory.Cpu && n.StartsWith("Core")) snap.CpuMaxClockMHz = Math.Max(snap.CpuMaxClockMHz ?? 0, v);
                    if (isGpu && n.Contains("Core")) snap.GpuCoreClock = v;
                    if (isGpu && n.Contains("Memory")) snap.GpuMemClock = v;
                    break;
                case SensorType.Load:
                    if (isGpu && n.Contains("Core")) snap.GpuLoad = v;
                    break;
                case SensorType.SmallData:
                    if (isGpu && n.Contains("Memory Used")) snap.GpuVramUsedMB = v;
                    if (isGpu && n.Contains("Memory Total")) snap.GpuVramTotalMB = v;
                    break;
                case SensorType.Fan:
                    if (isGpu) snap.GpuFanRpm = v;
                    break;
            }
        }
    }

    private static string ShortHw(IHardware hw) => hw.HardwareType switch
    {
        HardwareType.Cpu => "CPU",
        HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => "GPU",
        HardwareType.Motherboard or HardwareType.SuperIO => "Board",
        HardwareType.EmbeddedController => "EC",
        _ => hw.Name,
    };

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
    }
}
