using System.Runtime.InteropServices;
using System.Text;

namespace PerfHud.Sensors.Native;

/// <summary>
/// NVIDIA Management Library (nvml.dll ships with the NVIDIA driver in System32). Works without admin.
/// </summary>
public static class Nvml
{
    public const int SUCCESS = 0;
    public const int NVML_TEMPERATURE_GPU = 0;
    public const int NVML_CLOCK_GRAPHICS = 0, NVML_CLOCK_SM = 1, NVML_CLOCK_MEM = 2;

    // Clock event / throttle reasons
    public const ulong GpuIdle = 0x1, AppClocksSetting = 0x2, SwPowerCap = 0x4, HwSlowdown = 0x8,
        SyncBoost = 0x10, SwThermalSlowdown = 0x20, HwThermalSlowdown = 0x40, HwPowerBrakeSlowdown = 0x80;

    [StructLayout(LayoutKind.Sequential)] public struct Utilization { public uint Gpu; public uint Memory; }
    [StructLayout(LayoutKind.Sequential)] public struct Memory { public ulong Total; public ulong Free; public ulong Used; }

    private static bool? _available;

    public static bool IsInstalled
    {
        get
        {
            _available ??= NativeLibrary.TryLoad("nvml.dll", typeof(Nvml).Assembly, DllImportSearchPath.System32, out _);
            return _available.Value;
        }
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] public static extern int Init();
    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")] public static extern int Shutdown();
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")] public static extern int DeviceGetCount(out uint count);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] public static extern int DeviceGetHandleByIndex(uint index, out IntPtr device);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")] private static extern int DeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] public static extern int DeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] public static extern int DeviceGetUtilizationRates(IntPtr device, out Utilization util);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] public static extern int DeviceGetMemoryInfo(IntPtr device, out Memory mem);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] public static extern int DeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")] public static extern int DeviceGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")] public static extern int DeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMaxClockInfo")] public static extern int DeviceGetMaxClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFanSpeed")] public static extern int DeviceGetFanSpeed(IntPtr device, out uint percent);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPerformanceState")] public static extern int DeviceGetPerformanceState(IntPtr device, out int pstate);
    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrentClocksThrottleReasons")] public static extern int DeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);

    public static string? GetName(IntPtr device)
    {
        var buf = new byte[96];
        if (DeviceGetName(device, buf, (uint)buf.Length) != SUCCESS) return null;
        int len = Array.IndexOf(buf, (byte)0);
        return Encoding.UTF8.GetString(buf, 0, len < 0 ? buf.Length : len);
    }
}
