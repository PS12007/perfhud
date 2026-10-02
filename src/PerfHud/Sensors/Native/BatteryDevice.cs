using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PerfHud.Sensors.Native;

/// <summary>
/// Direct battery queries via IOCTL_BATTERY_* (the same interface powercfg /batteryreport uses). No admin required.
/// Gives design/full capacity, voltage, charge/discharge rate, cycle count and (if the firmware exposes it) temperature.
/// </summary>
public sealed class BatteryDevice : IDisposable
{
    private static readonly Guid BatteryClass = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    private const uint IOCTL_BATTERY_QUERY_TAG = 0x294040, IOCTL_BATTERY_QUERY_INFORMATION = 0x294044, IOCTL_BATTERY_QUERY_STATUS = 0x29404C;
    public const uint BATTERY_POWER_ON_LINE = 1, BATTERY_DISCHARGING = 2, BATTERY_CHARGING = 4, BATTERY_CRITICAL = 8;
    public const uint UNKNOWN = 0xFFFFFFFF;
    public const int UNKNOWN_RATE = unchecked((int)0x80000000);

    private SafeFileHandle? _h;
    private uint _tag;
    public string DevicePath { get; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BatteryInformation
    {
        public uint Capabilities;
        public byte Technology;
        public byte R0, R1, R2;
        public uint Chemistry;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1, DefaultAlert2, CriticalBias;
        public uint CycleCount;
        public bool IsRelative => (Capabilities & 0x40000000) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BatteryStatus { public uint PowerState; public uint Capacity; public uint Voltage; public int Rate; }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryInfoArgs { public uint Tag; public int Level; public int AtRate; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaitStatus { public uint Tag; public uint Timeout; public uint PowerState; public uint Low; public uint High; }

    private BatteryDevice(string path) { DevicePath = path; }

    public static List<BatteryDevice> Enumerate()
    {
        var result = new List<BatteryDevice>();
        var g = BatteryClass;
        var set = SetupApi.SetupDiGetClassDevsW(ref g, null, IntPtr.Zero, SetupApi.DIGCF_PRESENT | SetupApi.DIGCF_DEVICEINTERFACE);
        if (set == SetupApi.INVALID_HANDLE_VALUE) return result;
        try
        {
            for (uint i = 0; i < 8; i++)
            {
                var data = new SetupApi.SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SetupApi.SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data)) break;
                SetupApi.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint req, IntPtr.Zero);
                if (req == 0) continue;
                var buf = Marshal.AllocHGlobal((int)req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                    if (!SetupApi.SetupDiGetDeviceInterfaceDetailW(set, ref data, buf, req, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(buf + 4);
                    if (!string.IsNullOrEmpty(path)) result.Add(new BatteryDevice(path));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private bool EnsureOpen()
    {
        if (_h is { IsInvalid: false, IsClosed: false } && _tag != 0) return true;
        _h?.Dispose();
        _h = CreateFileW(DevicePath, 0xC0000000 /*GENERIC_READ|WRITE*/, 3, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0x80, IntPtr.Zero);
        if (_h.IsInvalid) return false;
        uint wait = 0;
        if (!DeviceIoControl(_h, IOCTL_BATTERY_QUERY_TAG, ref wait, 4, out _tag, 4, out _, IntPtr.Zero) || _tag == 0)
        {
            _h.Dispose();
            return false;
        }
        return true;
    }

    private void Invalidate() { _h?.Dispose(); _h = null; _tag = 0; }

    public BatteryInformation? QueryInformation()
    {
        if (!EnsureOpen()) return null;
        var q = new QueryInfoArgs { Tag = _tag, Level = 0 };
        if (DeviceIoControl(_h!, IOCTL_BATTERY_QUERY_INFORMATION, ref q, Marshal.SizeOf<QueryInfoArgs>(), out BatteryInformation info, Marshal.SizeOf<BatteryInformation>(), out _, IntPtr.Zero))
            return info;
        Invalidate();
        return null;
    }

    public BatteryStatus? QueryStatus()
    {
        if (!EnsureOpen()) return null;
        var w = new WaitStatus { Tag = _tag };
        if (DeviceIoControl(_h!, IOCTL_BATTERY_QUERY_STATUS, ref w, Marshal.SizeOf<WaitStatus>(), out BatteryStatus st, Marshal.SizeOf<BatteryStatus>(), out _, IntPtr.Zero))
            return st;
        Invalidate();
        return null;
    }

    /// <summary>Battery temperature in °C, or null if the firmware doesn't report it (common).</summary>
    public double? QueryTemperature()
    {
        if (!EnsureOpen()) return null;
        var q = new QueryInfoArgs { Tag = _tag, Level = 2 };
        if (DeviceIoControl(_h!, IOCTL_BATTERY_QUERY_INFORMATION, ref q, Marshal.SizeOf<QueryInfoArgs>(), out uint tenthsKelvin, 4, out _, IntPtr.Zero)
            && tenthsKelvin > 2000 && tenthsKelvin < 4000)
            return tenthsKelvin / 10.0 - 273.15;
        return null;
    }

    /// <summary>Firmware-estimated seconds remaining at current rate (only meaningful while discharging).</summary>
    public uint? QueryEstimatedTime()
    {
        if (!EnsureOpen()) return null;
        var q = new QueryInfoArgs { Tag = _tag, Level = 3 };
        if (DeviceIoControl(_h!, IOCTL_BATTERY_QUERY_INFORMATION, ref q, Marshal.SizeOf<QueryInfoArgs>(), out uint secs, 4, out _, IntPtr.Zero) && secs != UNKNOWN)
            return secs;
        return null;
    }

    public string? QueryString(int level)
    {
        if (!EnsureOpen()) return null;
        var q = new QueryInfoArgs { Tag = _tag, Level = level };
        var buf = new char[128];
        unsafe
        {
            fixed (char* p = buf)
            {
                if (DeviceIoControlRaw(_h!, IOCTL_BATTERY_QUERY_INFORMATION, ref q, Marshal.SizeOf<QueryInfoArgs>(), (IntPtr)p, buf.Length * 2, out var ret, IntPtr.Zero))
                    return new string(p, 0, Math.Max(0, (int)ret / 2)).TrimEnd('\0');
            }
        }
        return null;
    }

    public void Dispose() => Invalidate();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref uint inBuf, int inSize, out uint outBuf, int outSize, out uint returned, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref QueryInfoArgs inBuf, int inSize, out BatteryInformation outBuf, int outSize, out uint returned, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref QueryInfoArgs inBuf, int inSize, out uint outBuf, int outSize, out uint returned, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref WaitStatus inBuf, int inSize, out BatteryStatus outBuf, int outSize, out uint returned, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    private static extern bool DeviceIoControlRaw(SafeFileHandle h, uint code, ref QueryInfoArgs inBuf, int inSize, IntPtr outBuf, int outSize, out uint returned, IntPtr ov);
}
