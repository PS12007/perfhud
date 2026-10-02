using System.Runtime.InteropServices;

namespace PerfHud.Sensors.Native;

/// <summary>Enumerates GPUs via DXGI (name, vendor, LUID, dedicated VRAM). Enumeration doesn't power up a sleeping dGPU.</summary>
public static unsafe class Dxgi
{
    public sealed record Adapter(string Name, uint VendorId, uint DeviceId, uint LuidLow, int LuidHigh,
        ulong DedicatedVideoMemory, ulong SharedSystemMemory, bool IsSoftware)
    {
        /// <summary>LUID in the format used by "GPU Engine" / "GPU Adapter Memory" counter instance names.</summary>
        public string PdhLuid => $"luid_0x{(uint)LuidHigh:X8}_0x{LuidLow:X8}";
        public bool IsNvidia => VendorId == 0x10DE;
        public bool IsAmd => VendorId == 0x1002;
        public bool IsIntel => VendorId == 0x8086;
        public string VendorTag => $"VEN_{VendorId:X4}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static List<Adapter> EnumerateAdapters()
    {
        var list = new List<Adapter>();
        var iid = IID_IDXGIFactory1;
        if (CreateDXGIFactory1(ref iid, out var factory) < 0 || factory == IntPtr.Zero) return list;
        try
        {
            var fvt = *(IntPtr**)factory;
            // IDXGIFactory1::EnumAdapters1 is vtable slot 12
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)fvt[12];
            for (uint i = 0; i < 16; i++)
            {
                IntPtr adapter;
                if (enumAdapters1(factory, i, &adapter) < 0 || adapter == IntPtr.Zero) break;
                try
                {
                    var avt = *(IntPtr**)adapter;
                    // IDXGIAdapter1::GetDesc1 is vtable slot 10
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)avt[10];
                    int size = Marshal.SizeOf<DXGI_ADAPTER_DESC1>();
                    var buf = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (getDesc1(adapter, buf) >= 0)
                        {
                            var d = Marshal.PtrToStructure<DXGI_ADAPTER_DESC1>(buf);
                            bool sw = (d.Flags & 2) != 0 || d.VendorId == 0x1414; // DXGI_ADAPTER_FLAG_SOFTWARE / Microsoft Basic Render
                            list.Add(new Adapter(d.Description, d.VendorId, d.DeviceId, d.LuidLow, d.LuidHigh,
                                d.DedicatedVideoMemory, d.SharedSystemMemory, sw));
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return list;
    }
}
