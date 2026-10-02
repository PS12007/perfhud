using System.Windows.Input;
using PerfHud.Core;
using PerfHud.Sensors.Native;
using PerfHud.Settings;
using PerfHud.SystemTray;

namespace PerfHud.Hotkeys;

public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public bool IsEmpty => Key == Key.None;

    public static bool TryParse(string? text, out HotkeyGesture g)
    {
        g = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": case "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse(raw, true, out key))
                    {
                        if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                        else return false;
                    }
                    break;
            }
        }
        if (key == Key.None) return false;
        g = new HotkeyGesture(mods, key);
        return true;
    }

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key is >= Key.D0 and <= Key.D9 ? ((int)(Key - Key.D0)).ToString() : Key.ToString());
        return string.Join("+", parts);
    }
}

public enum HotkeyState { Unbound, Registered, Conflict, Duplicate, Invalid }

/// <summary>
/// Registers system-wide hotkeys with RegisterHotKey (no keyboard hooks, so nothing for anti-cheat to object to).
/// Conflicts (key already taken by another app) are reported per binding instead of failing.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private readonly Dictionary<HotkeyAction, (HotkeyState State, string Detail)> _status = new();
    private int _nextId = 0xB000;
    private bool _suspended;

    public event Action<HotkeyAction>? Pressed;
    public event Action? StatusChanged;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.AddHandler((msg, wp, _) =>
        {
            if (msg != Win32.WM_HOTKEY || _suspended) return false;
            if (_registered.TryGetValue((int)wp, out var action)) Pressed?.Invoke(action);
            return true;
        });
    }

    public (HotkeyState State, string Detail) GetStatus(HotkeyAction a) => _status.TryGetValue(a, out var s) ? s : (HotkeyState.Unbound, "");

    public void Apply(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();
        _status.Clear();
        var seen = new Dictionary<HotkeyGesture, HotkeyAction>();
        foreach (var b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Gesture)) { _status[b.Action] = (HotkeyState.Unbound, "Not set"); continue; }
            if (!HotkeyGesture.TryParse(b.Gesture, out var g)) { _status[b.Action] = (HotkeyState.Invalid, $"Can't parse '{b.Gesture}'"); continue; }
            if (seen.TryGetValue(g, out var other)) { _status[b.Action] = (HotkeyState.Duplicate, $"Also assigned to {other}"); continue; }
            seen[g] = b.Action;

            uint mods = Win32.MOD_NOREPEAT;
            if (g.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Win32.MOD_CONTROL;
            if (g.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Win32.MOD_ALT;
            if (g.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Win32.MOD_SHIFT;
            if (g.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Win32.MOD_WIN;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(g.Key);
            int id = _nextId++;
            if (Win32.RegisterHotKey(_window.Handle, id, mods, vk))
            {
                _registered[id] = b.Action;
                _status[b.Action] = (HotkeyState.Registered, g.ToString());
            }
            else
            {
                _status[b.Action] = (HotkeyState.Conflict, $"{g} is already used by another application");
                Log.Warn($"Hotkey {g} for {b.Action} could not be registered (in use by another app)");
            }
        }
        StatusChanged?.Invoke();
    }

    /// <summary>Temporarily ignore hotkeys (while the user is recording a new key combination).</summary>
    public void Suspend(bool suspended) => _suspended = suspended;

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) Win32.UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
    }

    public void Dispose() => UnregisterAll();
}
