using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PerfHud.Core;
using PerfHud.Sensors.Native;

namespace PerfHud.SystemTray;

/// <summary>
/// Notification-area icon via Shell_NotifyIcon, with a themed WPF context menu.
/// Re-adds itself if Explorer restarts (TaskbarCreated).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_APP_TRAY = 0x8000 + 0x51;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, IntPtr[]? large, IntPtr[] small, uint count);

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tip = "PerfHud";
    private bool _added;

    public Func<ContextMenu>? MenuFactory { get; set; }
    public event Action? DoubleClicked;
    public event Action? Clicked;

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _taskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
        var small = new IntPtr[1];
        if (ExtractIconExW(AppPaths.ExePath, 0, null, small, 1) > 0) _icon = small[0];
        _window.AddHandler(OnMessage);
        Add();
    }

    private NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref d);
        if (!_added) Log.Warn("Could not add the tray icon (Explorer not ready?) — will retry when the taskbar appears");
    }

    public void SetTooltip(string text)
    {
        text = text.Length > 127 ? text[..127] : text;
        if (text == _tip) return;
        _tip = text;
        if (!_added) return;
        var d = Data(NIF_TIP);
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    }

    /// <summary>Native balloon/toast (used only for important one-off messages; alerts use PerfHud's own subtle toasts).</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        var d = Data(NIF_INFO);
        d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        d.szInfo = text.Length > 255 ? text[..255] : text;
        d.dwInfoFlags = 1; // NIIF_INFO
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    }

    private bool OnMessage(int msg, IntPtr wp, IntPtr lp)
    {
        if (msg == _taskbarCreated) { Add(); return true; }
        if (msg != WM_APP_TRAY) return false;
        int ev = (int)lp & 0xFFFF;
        switch (ev)
        {
            case WM_LBUTTONDBLCLK: DoubleClicked?.Invoke(); break;
            case WM_LBUTTONUP: Clicked?.Invoke(); break;
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowMenu();
                break;
        }
        return true;
    }

    private void ShowMenu()
    {
        var menu = MenuFactory?.Invoke();
        if (menu == null) return;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        // Make the menu's popup the foreground window so it closes when the user clicks elsewhere.
        if (PresentationSource.FromVisual(menu) is HwndSource src) Win32.SetForegroundWindow(src.Handle);
        menu.Focus();
    }

    public void Dispose()
    {
        if (_added)
        {
            var d = Data(0);
            Shell_NotifyIconW(NIM_DELETE, ref d);
            _added = false;
        }
        if (_icon != IntPtr.Zero) { Win32.DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }
}
