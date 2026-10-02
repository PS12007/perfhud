using System.Runtime.InteropServices;

namespace PerfHud.Sensors.Native;

/// <summary>Minimal wrapper over the Performance Data Helper API (no admin required).</summary>
public sealed class PdhQuery : IDisposable
{
    private IntPtr _query;
    private readonly List<PdhCounter> _counters = new();

    public PdhQuery()
    {
        Check(PdhApi.PdhOpenQueryW(null, IntPtr.Zero, out _query), "PdhOpenQuery");
    }

    /// <summary>Adds an English (locale-independent) counter path, e.g. @"\Processor Information(*)\% Processor Utility".</summary>
    public PdhCounter Add(string path)
    {
        Check(PdhApi.PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var h), $"PdhAddEnglishCounter {path}");
        var c = new PdhCounter(h, path);
        _counters.Add(c);
        return c;
    }

    public PdhCounter? TryAdd(string path)
    {
        if (PdhApi.PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var h) != 0) return null;
        var c = new PdhCounter(h, path);
        _counters.Add(c);
        return c;
    }

    public bool Collect() => PdhApi.PdhCollectQueryData(_query) == 0;

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { PdhApi.PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }

    internal static void Check(uint status, string what)
    {
        if (status != 0) throw new InvalidOperationException($"{what} failed (0x{status:X8})");
    }

    /// <summary>True if the counter set exists on this machine (cheap check before building queries).</summary>
    public static bool CounterExists(string path)
    {
        try
        {
            using var q = new PdhQuery();
            return q.TryAdd(path) != null;
        }
        catch { return false; }
    }
}

public sealed class PdhCounter
{
    private readonly IntPtr _h;
    private IntPtr _buf;
    private uint _bufSize;
    public string Path { get; }

    internal PdhCounter(IntPtr h, string path) { _h = h; Path = path; }

    /// <summary>Single-instance value, or NaN if invalid (e.g. first sample of a rate counter).</summary>
    public double Value()
    {
        var st = PdhApi.PdhGetFormattedCounterValue(_h, PdhApi.PDH_FMT_DOUBLE | PdhApi.PDH_FMT_NOCAP100, out _, out var v);
        if (st != 0 || (v.CStatus != 0 && v.CStatus != 1)) return double.NaN;
        return v.doubleValue;
    }

    /// <summary>Wildcard-instance values. Reuses the provided list to limit allocations.</summary>
    public int Values(List<(string Name, double Value)> into)
    {
        into.Clear();
        uint size = _bufSize;
        uint st = PdhApi.PdhGetFormattedCounterArrayW(_h, PdhApi.PDH_FMT_DOUBLE | PdhApi.PDH_FMT_NOCAP100, ref size, out uint count, _buf);
        if (st == PdhApi.PDH_MORE_DATA)
        {
            if (_buf != IntPtr.Zero) Marshal.FreeHGlobal(_buf);
            _bufSize = size + 4096;
            _buf = Marshal.AllocHGlobal((int)_bufSize);
            size = _bufSize;
            st = PdhApi.PdhGetFormattedCounterArrayW(_h, PdhApi.PDH_FMT_DOUBLE | PdhApi.PDH_FMT_NOCAP100, ref size, out count, _buf);
        }
        if (st != 0) return 0;
        int itemSize = Marshal.SizeOf<PdhApi.PDH_FMT_COUNTERVALUE_ITEM_W>();
        for (int i = 0; i < count; i++)
        {
            var item = Marshal.PtrToStructure<PdhApi.PDH_FMT_COUNTERVALUE_ITEM_W>(_buf + i * itemSize);
            if (item.FmtValue.CStatus != 0 && item.FmtValue.CStatus != 1) continue;
            into.Add((Marshal.PtrToStringUni(item.szName) ?? "", item.FmtValue.doubleValue));
        }
        return into.Count;
    }

    ~PdhCounter()
    {
        if (_buf != IntPtr.Zero) Marshal.FreeHGlobal(_buf);
    }
}

internal static class PdhApi
{
    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_FMT_NOCAP100 = 0x00008000;
    public const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double doubleValue; // union; 8-byte aligned at offset 8
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM_W
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);
}
