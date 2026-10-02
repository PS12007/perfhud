using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerfHud.Sensors.Native;

public enum PresentSource : byte { Dxgi = 0, D3D9 = 1, KernelPresent = 2, KernelHistory = 3, KernelHistoryDetailed = 4 }

/// <summary>
/// Real-time ETW session that listens to the same graphics-stack events PresentMon uses
/// (Microsoft-Windows-DXGI, -D3D9 and -DxgKrnl). This is purely passive observation of OS events:
/// nothing is injected into games, so it is safe with anti-cheat software.
/// Requires administrator rights OR membership in the "Performance Log Users" group.
/// </summary>
public sealed unsafe class EtwPresentSession : IDisposable
{
    public const string SessionName = "PerfHud-FrameTiming";

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    // Event ids (from the providers' manifests, as used by PresentMon)
    private const ushort DXGI_Present_Start = 42, DXGI_PresentMPO_Start = 55;
    private const ushort D3D9_Present_Start = 1;
    private const ushort DxgKrnl_Present_Info = 184, DxgKrnl_PresentHistoryDetailed_Start = 171, DxgKrnl_PresentHistory_Start = 215;

    public delegate void PresentCallback(uint pid, long fileTime, PresentSource source);

    private static volatile EtwPresentSession? _active;
    private readonly PresentCallback _callback;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    private ulong _session;
    private ulong _trace = InvalidHandle;
    private IntPtr _props;
    private IntPtr _logfile;
    private IntPtr _loggerNamePtr;
    private Thread? _thread;
    private const ulong InvalidHandle = 0xFFFFFFFFFFFFFFFF;

    public long EventsReceived;

    public EtwPresentSession(PresentCallback callback) => _callback = callback;

    public void Start()
    {
        if (_active != null) throw new InvalidOperationException("Only one frame-timing session can be active");

        uint status = StartSession();
        if (status == 183) // ERROR_ALREADY_EXISTS: stale session from a previous crash
        {
            StopByName();
            status = StartSession();
        }
        if (status == 5) throw new UnauthorizedAccessException("Starting an ETW session requires administrator rights or membership in 'Performance Log Users'");
        if (status != 0) throw new Win32Exception((int)status, $"StartTrace failed ({status})");

        try
        {
            Enable(DxgiProvider, 4, ulong.MaxValue, DXGI_Present_Start, DXGI_PresentMPO_Start);
            Enable(D3D9Provider, 4, ulong.MaxValue, D3D9_Present_Start);
            // Kernel present events cover OpenGL / Vulkan apps that don't go through DXGI. Base keyword only.
            Enable(DxgKrnlProvider, 4, 0x1, DxgKrnl_Present_Info, DxgKrnl_PresentHistoryDetailed_Start, DxgKrnl_PresentHistory_Start);
        }
        catch
        {
            Stop();
            throw;
        }

        // Open the real-time consumer
        _logfile = Marshal.AllocHGlobal(LogfileSize);
        new Span<byte>((void*)_logfile, LogfileSize).Clear();
        _loggerNamePtr = Marshal.StringToHGlobalUni(SessionName);
        *(IntPtr*)(_logfile + 8) = _loggerNamePtr;                                          // LoggerName
        *(uint*)(_logfile + 28) = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD; // ProcessTraceMode
        *(IntPtr*)(_logfile + 424) = (IntPtr)(delegate* unmanaged<EventRecord*, void>)&OnEventRecord; // EventRecordCallback

        _active = this;
        _trace = OpenTraceW(_logfile);
        if (_trace == InvalidHandle)
        {
            int err = Marshal.GetLastWin32Error();
            _active = null;
            Stop();
            throw new Win32Exception(err, "OpenTrace failed");
        }

        _thread = new Thread(() =>
        {
            var h = _trace;
            ProcessTrace(&h, 1, IntPtr.Zero, IntPtr.Zero); // blocks until the session stops
        })
        { IsBackground = true, Name = "PerfHud.ETW", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private uint StartSession()
    {
        FreeProps();
        _props = AllocProps();
        return StartTraceW(out _session, SessionName, _props);
    }

    private void Enable(Guid provider, byte level, ulong anyKeyword, params ushort[] ids)
    {
        // Kernel-side event-id filter so we only receive the handful of events we need (keeps overhead tiny).
        int filterSize = 4 + 2 * ids.Length;
        var filter = Marshal.AllocHGlobal(filterSize);
        try
        {
            byte* f = (byte*)filter;
            f[0] = 1; // FilterIn = TRUE
            f[1] = 0;
            *(ushort*)(f + 2) = (ushort)ids.Length;
            for (int i = 0; i < ids.Length; i++) *(ushort*)(f + 4 + 2 * i) = ids[i];

            var desc = new EVENT_FILTER_DESCRIPTOR { Ptr = (ulong)filter, Size = (uint)filterSize, Type = EVENT_FILTER_TYPE_EVENT_ID };
            var p = new ENABLE_TRACE_PARAMETERS { Version = 2, EnableFilterDesc = (IntPtr)(&desc), FilterDescCount = 1 };
            uint st = EnableTraceEx2(_session, ref provider, 1 /*ENABLE*/, level, anyKeyword, 0, 0, ref p);
            if (st != 0)
            {
                // Retry without the filter (older builds may reject it).
                var p2 = new ENABLE_TRACE_PARAMETERS { Version = 2 };
                st = EnableTraceEx2(_session, ref provider, 1, level, anyKeyword, 0, 0, ref p2);
            }
            if (st == 5) throw new UnauthorizedAccessException("Not allowed to enable graphics trace providers");
            if (st != 0) throw new Win32Exception((int)st, $"EnableTraceEx2 failed for {provider}");
        }
        finally { Marshal.FreeHGlobal(filter); }
    }

    [UnmanagedCallersOnly]
    private static void OnEventRecord(EventRecord* rec)
    {
        var self = _active;
        if (self == null) return;
        try
        {
            ref var h = ref rec->Header;
            uint pid = h.ProcessId;
            if (pid == self._ownPid || pid == 0) return;
            PresentSource src;
            if (h.ProviderId == DxgiProvider)
            {
                if (h.Id != DXGI_Present_Start && h.Id != DXGI_PresentMPO_Start) return;
                src = PresentSource.Dxgi;
            }
            else if (h.ProviderId == D3D9Provider)
            {
                if (h.Id != D3D9_Present_Start) return;
                src = PresentSource.D3D9;
            }
            else if (h.ProviderId == DxgKrnlProvider)
            {
                src = h.Id switch
                {
                    DxgKrnl_Present_Info => PresentSource.KernelPresent,
                    DxgKrnl_PresentHistory_Start => PresentSource.KernelHistory,
                    DxgKrnl_PresentHistoryDetailed_Start => PresentSource.KernelHistoryDetailed,
                    _ => (PresentSource)255,
                };
                if ((byte)src == 255) return;
            }
            else return;

            Interlocked.Increment(ref self.EventsReceived);
            self._callback(pid, h.TimeStamp, src);
        }
        catch { /* never throw across the native boundary */ }
    }

    public void Stop()
    {
        _active = null;
        if (_session != 0 && _props != IntPtr.Zero)
        {
            ControlTraceW(_session, null, _props, EVENT_TRACE_CONTROL_STOP);
            _session = 0;
        }
        if (_trace != InvalidHandle)
        {
            CloseTrace(_trace);
            _trace = InvalidHandle;
        }
        _thread?.Join(2000);
        _thread = null;
        if (_logfile != IntPtr.Zero) { Marshal.FreeHGlobal(_logfile); _logfile = IntPtr.Zero; }
        if (_loggerNamePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_loggerNamePtr); _loggerNamePtr = IntPtr.Zero; }
        FreeProps();
    }

    private static void StopByName()
    {
        var p = AllocProps();
        try { ControlTraceW(0, SessionName, p, EVENT_TRACE_CONTROL_STOP); }
        finally { Marshal.FreeHGlobal(p); }
    }

    private void FreeProps()
    {
        if (_props != IntPtr.Zero) { Marshal.FreeHGlobal(_props); _props = IntPtr.Zero; }
    }

    public void Dispose() => Stop();

    // ── EVENT_TRACE_PROPERTIES (built by offset; layout documented in evntrace.h) ──
    private const int PropsBaseSize = 120;
    private const int PropsTotalSize = PropsBaseSize + 2048 * 2;

    private static IntPtr AllocProps()
    {
        var p = Marshal.AllocHGlobal(PropsTotalSize);
        new Span<byte>((void*)p, PropsTotalSize).Clear();
        byte* b = (byte*)p;
        *(uint*)(b + 0) = PropsTotalSize;          // Wnode.BufferSize
        *(uint*)(b + 40) = 1;                      // Wnode.ClientContext = QPC
        *(uint*)(b + 44) = 0x00020000;             // Wnode.Flags = WNODE_FLAG_TRACED_GUID
        *(uint*)(b + 48) = 32;                     // BufferSize (KB)
        *(uint*)(b + 52) = 4;                      // MinimumBuffers
        *(uint*)(b + 56) = 32;                     // MaximumBuffers
        *(uint*)(b + 64) = 0x00000100;             // LogFileMode = EVENT_TRACE_REAL_TIME_MODE
        *(uint*)(b + 68) = 1;                      // FlushTimer (s) — keeps latency ~1s
        *(uint*)(b + 112) = 0;                     // LogFileNameOffset
        *(uint*)(b + 116) = PropsBaseSize;         // LoggerNameOffset
        return p;
    }

    // ── EVENT_TRACE_LOGFILEW: 448 bytes on x64 ──
    private const int LogfileSize = 512;
    private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100, PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    private const uint EVENT_TRACE_CONTROL_STOP = 1;
    private const uint EVENT_FILTER_TYPE_EVENT_ID = 0x80000200;

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_FILTER_DESCRIPTOR { public ulong Ptr; public uint Size; public uint Type; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ENABLE_TRACE_PARAMETERS
    {
        public uint Version;
        public uint EnableProperty;
        public uint ControlFlags;
        public Guid SourceId;
        public IntPtr EnableFilterDesc;
        public uint FilterDescCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventHeader
    {
        public ushort Size, HeaderType, Flags, EventProperty;
        public uint ThreadId, ProcessId;
        public long TimeStamp;
        public Guid ProviderId;
        public ushort Id;
        public byte Version, Channel, Level, Opcode;
        public ushort Task;
        public ulong Keyword;
        public ulong ProcessorTime;
        public Guid ActivityId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventRecord
    {
        public EventHeader Header;
        public uint BufferContext;
        public ushort ExtendedDataCount, UserDataLength;
        public IntPtr ExtendedData, UserData, UserContext;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint StartTraceW(out ulong handle, string name, IntPtr props);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(ulong handle, string? name, IntPtr props, uint code);

    [DllImport("advapi32.dll")]
    private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level,
        ulong matchAny, ulong matchAll, uint timeout, ref ENABLE_TRACE_PARAMETERS p);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern ulong OpenTraceW(IntPtr logfile);

    [DllImport("advapi32.dll")]
    private static extern uint ProcessTrace(ulong* handles, uint count, IntPtr start, IntPtr end);

    [DllImport("advapi32.dll")]
    private static extern uint CloseTrace(ulong handle);
}
