using System.Runtime.InteropServices;

namespace PerfHud.Sensors.Native;

internal static class SetupApi
{
    public const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    public const uint SPDRP_HARDWAREID = 0x1, SPDRP_FRIENDLYNAME = 0xC, SPDRP_DEVICEDESC = 0x0;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid iface, uint index, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint required, IntPtr devInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint prop, out uint type, byte[]? buffer, uint size, out uint required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[]? buffer, uint size, out uint required, uint flags);

    [DllImport("setupapi.dll")]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    public static string? GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA dev, uint prop)
    {
        var buf = new byte[2048];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref dev, prop, out _, buf, (uint)buf.Length, out var req)) return null;
        // REG_SZ or REG_MULTI_SZ: first string is enough for our needs (we search hardware ids with Contains below)
        return System.Text.Encoding.Unicode.GetString(buf, 0, (int)Math.Min(req, (uint)buf.Length)).Replace('\0', '\n').Trim('\n');
    }
}

/// <summary>
/// Reads the current device power state (D0 = on, D3 = off) of display adapters through SetupAPI.
/// This does not touch the GPU itself, so it can be called without waking a sleeping hybrid-graphics dGPU.
/// </summary>
public sealed class DevicePowerState : IDisposable
{
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static SetupApi.DEVPROPKEY PowerDataKey = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 32 };

    private IntPtr _set = SetupApi.INVALID_HANDLE_VALUE;
    private readonly List<(SetupApi.SP_DEVINFO_DATA dev, string hwid, string name)> _devices = new();

    public DevicePowerState()
    {
        var g = DisplayClass;
        _set = SetupApi.SetupDiGetClassDevsW(ref g, null, IntPtr.Zero, SetupApi.DIGCF_PRESENT);
        if (_set == SetupApi.INVALID_HANDLE_VALUE) return;
        for (uint i = 0; ; i++)
        {
            var d = new SetupApi.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>() };
            if (!SetupApi.SetupDiEnumDeviceInfo(_set, i, ref d)) break;
            var hw = SetupApi.GetStringProperty(_set, ref d, SetupApi.SPDRP_HARDWAREID) ?? "";
            var name = SetupApi.GetStringProperty(_set, ref d, SetupApi.SPDRP_FRIENDLYNAME)
                       ?? SetupApi.GetStringProperty(_set, ref d, SetupApi.SPDRP_DEVICEDESC) ?? "";
            _devices.Add((d, hw, name));
        }
    }

    public IEnumerable<(string HardwareId, string Name)> Devices => _devices.Select(d => (d.hwid, d.name));

    /// <summary>Returns 1..4 for D0..D3 of the first display device whose hardware id contains <paramref name="vendorTag"/> (e.g. "VEN_10DE"), or 0 if unknown.</summary>
    public int GetPowerState(string vendorTag)
    {
        for (int i = 0; i < _devices.Count; i++)
        {
            if (!_devices[i].hwid.Contains(vendorTag, StringComparison.OrdinalIgnoreCase)) continue;
            var dev = _devices[i].dev;
            var buf = new byte[64];
            if (SetupApi.SetupDiGetDevicePropertyW(_set, ref dev, ref PowerDataKey, out _, buf, (uint)buf.Length, out _, 0))
                return BitConverter.ToInt32(buf, 4); // CM_POWER_DATA.PD_MostRecentPowerState
        }
        return 0;
    }

    public string? GetName(string vendorTag) =>
        _devices.FirstOrDefault(d => d.hwid.Contains(vendorTag, StringComparison.OrdinalIgnoreCase)).name;

    public void Dispose()
    {
        if (_set != SetupApi.INVALID_HANDLE_VALUE) { SetupApi.SetupDiDestroyDeviceInfoList(_set); _set = SetupApi.INVALID_HANDLE_VALUE; }
    }
}
