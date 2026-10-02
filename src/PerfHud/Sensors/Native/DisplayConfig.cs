using System.Runtime.InteropServices;

namespace PerfHud.Sensors.Native;

/// <summary>Active display info (resolution, refresh rate, monitor name, HDR) via QueryDisplayConfig. No admin needed.</summary>
public static class DisplayConfig
{
    public sealed record DisplayInfo(string GdiName, string FriendlyName, int Width, int Height, double RefreshHz,
        bool HdrSupported, bool HdrEnabled, uint BitsPerColor, bool Internal);

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2, GET_ADVANCED_COLOR_INFO = 9;

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
        public uint scaling; public uint refreshNum; public uint refreshDen; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO { public PATH_SOURCE_INFO source; public PATH_TARGET_INFO target; public uint flags; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct MODE_INFO
    {
        [FieldOffset(0)] public uint infoType; // 1 = source, 2 = target
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public uint sourceWidth;
        [FieldOffset(20)] public uint sourceHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SOURCE_NAME
    {
        public HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_NAME
    {
        public HEADER header;
        public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADVANCED_COLOR_INFO { public HEADER header; public uint value; public int colorEncoding; public uint bitsPerColorChannel; }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME req);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME req);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO req);

    public static List<DisplayInfo> Query()
    {
        var list = new List<DisplayInfo>();
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return list;
        var paths = new PATH_INFO[np];
        var modes = new MODE_INFO[nm];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;

        for (int i = 0; i < np; i++)
        {
            var p = paths[i];
            var src = new SOURCE_NAME { header = new HEADER { type = GET_SOURCE_NAME, size = (uint)Marshal.SizeOf<SOURCE_NAME>(), adapterId = p.source.adapterId, id = p.source.id } };
            DisplayConfigGetDeviceInfo(ref src);
            var tgt = new TARGET_NAME { header = new HEADER { type = GET_TARGET_NAME, size = (uint)Marshal.SizeOf<TARGET_NAME>(), adapterId = p.target.adapterId, id = p.target.id } };
            DisplayConfigGetDeviceInfo(ref tgt);
            var col = new ADVANCED_COLOR_INFO { header = new HEADER { type = GET_ADVANCED_COLOR_INFO, size = (uint)Marshal.SizeOf<ADVANCED_COLOR_INFO>(), adapterId = p.target.adapterId, id = p.target.id } };
            bool colorOk = DisplayConfigGetDeviceInfo(ref col) == 0;

            int w = 0, h = 0;
            if (p.source.modeInfoIdx < nm && modes[p.source.modeInfoIdx].infoType == 1)
            {
                w = (int)modes[p.source.modeInfoIdx].sourceWidth;
                h = (int)modes[p.source.modeInfoIdx].sourceHeight;
            }
            double hz = p.target.refreshDen == 0 ? 0 : (double)p.target.refreshNum / p.target.refreshDen;
            // outputTechnology: 0x80000000 = internal (laptop panel), 11 = embedded DisplayPort, 13 = UDI embedded
            bool internalPanel = p.target.outputTechnology is 0x80000000 or 11 or 13;
            var friendly = string.IsNullOrWhiteSpace(tgt.monitorFriendlyDeviceName) ? (internalPanel ? "Built-in display" : "Display") : tgt.monitorFriendlyDeviceName;

            list.Add(new DisplayInfo(src.viewGdiDeviceName ?? "", friendly, w, h, Math.Round(hz, 2),
                colorOk && (col.value & 1) != 0, colorOk && (col.value & 2) != 0, colorOk ? col.bitsPerColorChannel : 0, internalPanel));
        }
        return list;
    }
}
