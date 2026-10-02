using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PerfHud.Sensors.Native;

/// <summary>
/// Storage queries through IOCTL_STORAGE_QUERY_PROPERTY with zero access rights (no admin).
/// NVMe drives on Windows 10+ usually report temperature this way.
/// </summary>
public static class StorageDevice
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x560000;
    private const int StorageDeviceProperty = 0, StorageDeviceTemperatureProperty = 52;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY { public int PropertyId; public int QueryType; public int Additional; }

    /// <summary>Physical disk number backing a drive letter (first extent), or -1.</summary>
    public static int GetDiskNumber(char driveLetter)
    {
        using var h = Open($@"\\.\{driveLetter}:");
        if (h.IsInvalid) return -1;
        var buf = new byte[256];
        if (!DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, buf, buf.Length, out _, IntPtr.Zero)) return -1;
        int extents = BitConverter.ToInt32(buf, 0);
        return extents > 0 ? BitConverter.ToInt32(buf, 8) : -1;
    }

    /// <summary>Drive temperature in °C, or null if not reported.</summary>
    public static double? GetTemperature(int diskNumber)
    {
        using var h = Open($@"\\.\PhysicalDrive{diskNumber}");
        if (h.IsInvalid) return null;
        var q = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceTemperatureProperty, QueryType = 0 };
        var buf = new byte[512];
        if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, ref q, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), buf, buf.Length, out var ret, IntPtr.Zero) || ret < 28)
            return null;
        ushort count = BitConverter.ToUInt16(buf, 12);
        if (count == 0) return null;
        short t = BitConverter.ToInt16(buf, 26); // TemperatureInfo[0].Temperature (°C)
        return t is > -40 and < 150 ? t : null;
    }

    /// <summary>Product id / model string of a physical disk.</summary>
    public static string? GetModel(int diskNumber)
    {
        using var h = Open($@"\\.\PhysicalDrive{diskNumber}");
        if (h.IsInvalid) return null;
        var q = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty, QueryType = 0 };
        var buf = new byte[1024];
        if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, ref q, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), buf, buf.Length, out var ret, IntPtr.Zero) || ret < 32)
            return null;
        int productOffset = BitConverter.ToInt32(buf, 16);
        if (productOffset <= 0 || productOffset >= ret) return null;
        int end = Array.IndexOf(buf, (byte)0, productOffset);
        if (end < 0) end = (int)ret;
        return System.Text.Encoding.ASCII.GetString(buf, productOffset, end - productOffset).Trim();
    }

    private static SafeFileHandle Open(string path) =>
        CreateFileW(path, 0, 3 /*FILE_SHARE_READ|WRITE*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out uint returned, IntPtr ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref STORAGE_PROPERTY_QUERY inBuf, int inSize, byte[] outBuf, int outSize, out uint returned, IntPtr ov);
}
