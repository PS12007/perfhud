using System.Diagnostics;
using System.Windows.Threading;
using PerfHud.Core;
using PerfHud.Sensors.Native;
using PerfHud.Settings;

namespace PerfHud.Profiles;

public sealed record ProfileMatch(AppProfile? Profile, bool Desktop, string? ForegroundExe);

/// <summary>
/// Switches HUD profiles automatically. Foreground changes are event-driven (SetWinEventHook, no polling);
/// "while running" profiles use a light process scan every 4 s, and only when such profiles exist.
/// When no profile matches, the HUD returns to the user's normal settings.
/// </summary>
public sealed class ProfileManager : IDisposable
{
    private readonly SettingsService _settings;
    private readonly Win32.WinEventProc _proc;
    private IntPtr _hook;
    private readonly DispatcherTimer _scan;
    private ProfileMatch _current = new(null, false, null);
    private string? _fgExe;
    private bool _fgDesktop;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public event Action<ProfileMatch>? Changed;
    public ProfileMatch Current => _current;
    public string? ForegroundExe => _fgExe;

    public ProfileManager(SettingsService settings)
    {
        _settings = settings;
        _proc = OnForeground; // keep the delegate alive
        _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        _scan = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        _scan.Tick += (_, _) => Evaluate();
        _scan.Start();
        ReadForeground(Win32.GetForegroundWindow());
        Evaluate();
    }

    private void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObj, int idChild, uint thread, uint time)
    {
        ReadForeground(hwnd);
        Evaluate();
    }

    private void ReadForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownPid) return; // our own settings window shouldn't switch profiles
        var cls = Win32.GetClassName(hwnd);
        _fgDesktop = hwnd == Win32.GetShellWindow() || cls is "Progman" or "WorkerW" or "Shell_TrayWnd";
        _fgExe = Win32.GetProcessName(pid);
    }

    public void Evaluate()
    {
        var ps = _settings.Current.Profiles;
        AppProfile? match = null;
        bool desktop = false;
        if (ps.Enabled)
        {
            match = ps.Items.FirstOrDefault(p => p.Enabled && p.Trigger == ProfileTrigger.WhileFocused && Matches(p, _fgExe));
            if (match == null)
            {
                var running = ps.Items.Where(p => p.Enabled && p.Trigger == ProfileTrigger.WhileRunning && !string.IsNullOrWhiteSpace(p.ExeName)).ToList();
                foreach (var p in running)
                {
                    if (IsRunning(p.ExeName)) { match = p; break; }
                }
            }
            if (match == null && _fgDesktop && !string.IsNullOrEmpty(ps.DesktopAction)) desktop = true;
        }

        var next = new ProfileMatch(match, desktop, _fgExe);
        if (!ReferenceEquals(next.Profile, _current.Profile) || next.Desktop != _current.Desktop)
        {
            _current = next;
            Log.Info(match != null ? $"Profile activated: {match.ExeName}" : desktop ? "Desktop profile active" : "Profile cleared (back to defaults)");
            Changed?.Invoke(next);
        }
        else _current = next;
    }

    private static bool Matches(AppProfile p, string? exe) =>
        exe != null && !string.IsNullOrWhiteSpace(p.ExeName) &&
        (exe.Equals(p.ExeName, StringComparison.OrdinalIgnoreCase) ||
         exe.Equals(p.ExeName + ".exe", StringComparison.OrdinalIgnoreCase));

    private static bool IsRunning(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        var procs = Process.GetProcessesByName(name);
        bool any = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return any;
    }

    /// <summary>Running applications with visible windows (for the "pick an app" helper in settings).</summary>
    public static List<string> RunningAppExes()
    {
        var list = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != IntPtr.Zero) list.Add(p.ProcessName + ".exe"); }
            catch { }
            finally { p.Dispose(); }
        }
        list.Remove("PerfHud.exe");
        return list.ToList();
    }

    public void Dispose()
    {
        _scan.Stop();
        if (_hook != IntPtr.Zero) { Win32.UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
    }
}
