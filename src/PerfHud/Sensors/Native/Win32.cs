using System.Runtime.InteropServices;
using System.Text;

namespace PerfHud.Sensors.Native;

/// <summary>Shared Win32 declarations used across PerfHud.</summary>
public static class Win32
{
    // ── Window styles ────────────────────────────────────
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000,
        WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_APPWINDOW = 0x40000;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("user32.dll")] public static extern bool MessageBeep(uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    // ── Monitors ─────────────────────────────────────────
    public const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr hMon, ref MONITORINFOEX info);
    public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public sealed record MonitorInfo(IntPtr Handle, string Device, RECT Bounds, RECT Work, bool Primary, double DpiScale);

    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = GetMonitor(h);
            if (mi != null) list.Add(mi);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static MonitorInfo? GetMonitor(IntPtr hMon)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfoW(hMon, ref info)) return null;
        double scale = 1;
        try { if (GetDpiForMonitor(hMon, 0, out var dx, out _) == 0) scale = dx / 96.0; } catch { }
        return new MonitorInfo(hMon, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & 1) != 0, scale);
    }

    // ── Processes ────────────────────────────────────────
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, int flags, StringBuilder name, ref int size);

    /// <summary>Executable name (e.g. "game.exe") for a pid, without needing admin. Null if inaccessible.</summary>
    public static string? GetProcessPath(uint pid)
    {
        if (pid == 0) return null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static string? GetProcessName(uint pid)
    {
        var p = GetProcessPath(pid);
        return p == null ? null : Path.GetFileName(p);
    }

    // ── Power ────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;       // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;        // 128 = no battery
        public byte BatteryLifePercent; // 255 unknown
        public byte SystemStatusFlag;   // 1 = battery saver on
        public int BatteryLifeTime;     // seconds, -1 unknown
        public int BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // ── DWM ─────────────────────────────────────────────
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public const int DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

    /// <summary>Dark/light caption plus explicit caption, text and border colors (COLORREF 0x00BBGGRR; colors need Windows 11).</summary>
    public static void StyleTitleBar(IntPtr hwnd, bool dark, int caption, int text, int border)
    {
        try
        {
            int on = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, 4);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, 4);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4);
        }
        catch { }
    }

    // Undocumented but stable since Windows 10: blur behind for layered windows.
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);

    public static bool SetBlurBehind(IntPtr hwnd, bool enabled)
    {
        try
        {
            var accent = new AccentPolicy { AccentState = enabled ? 3 : 0 }; // 3 = ACCENT_ENABLE_BLURBEHIND
            int size = Marshal.SizeOf(accent);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttribData { Attribute = 19, Data = ptr, SizeOfData = size }; // WCA_ACCENT_POLICY
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { return false; }
    }

    // ── WinEvent hook (foreground changes) ───────────────
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
