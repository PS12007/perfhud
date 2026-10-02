using System.Windows.Interop;

namespace PerfHud.SystemTray;

/// <summary>Hidden top-level window that receives hotkey, tray and broadcast messages (e.g. TaskbarCreated).</summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<Func<int, IntPtr, IntPtr, bool>> _handlers = new();

    public IntPtr Handle => _source.Handle;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("PerfHud.MessageWindow")
        {
            Width = 0, Height = 0, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = 0x80,               // WS_EX_TOOLWINDOW
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public void AddHandler(Func<int, IntPtr, IntPtr, bool> handler) => _handlers.Add(handler);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        foreach (var h in _handlers)
        {
            try { if (h(msg, wParam, lParam)) { handled = true; break; } }
            catch (Exception ex) { Core.Log.Error("Message handler failed", ex); }
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
