using System.Runtime.InteropServices;
using PerfHud.Sensors;

namespace PerfHud.Monitoring.Monitors;

public sealed class MemoryMonitor : MonitorBase
{
    public override string Name => "Memory";
    public override string[] Prefixes => new[] { "ram." };

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("psapi.dll")] private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION p, uint size);

    public override void Initialize(MonitorContext ctx)
    {
        var dimms = Wmi.Query("SELECT Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        if (dimms.Count == 0) { ctx.Store.SetUnavailable("ram.speed", "Memory modules not reported by firmware"); return; }
        var speed = dimms.Max(d => d.Num("ConfiguredClockSpeed") ?? d.Num("Speed") ?? 0);
        ctx.Store.Set("ram.speed", speed > 0 ? speed : null, "Speed not reported by firmware");
        var type = (int)(dimms[0].Num("SMBIOSMemoryType") ?? 0) switch
        {
            20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3",
            30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "RAM",
        };
        var totalGb = Math.Round(dimms.Sum(d => d.Num("Capacity") ?? 0) / (1024d * 1024 * 1024));
        ctx.Store.SetText("ram.type", $"{totalGb:0} GB {type}{(speed > 0 ? $"-{speed:0}" : "")} ({dimms.Count}×{totalGb / dimms.Count:0} GB)");
    }

    public override void Update(MonitorContext ctx)
    {
        var s = ctx.Store;
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) throw new InvalidOperationException("GlobalMemoryStatusEx failed");
        double used = m.ullTotalPhys - m.ullAvailPhys;
        s.Set("ram.total", m.ullTotalPhys);
        s.Set("ram.avail", m.ullAvailPhys);
        s.Set("ram.used", used);
        s.Set("ram.pct", used / m.ullTotalPhys * 100);

        if (GetPerformanceInfo(out var p, (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>()))
        {
            double page = p.PageSize;
            s.Set("ram.commit", p.CommitTotal * page);
            s.Set("ram.commit.limit", p.CommitLimit * page);
        }
    }
}
