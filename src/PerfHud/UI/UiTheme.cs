using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PerfHud.Sensors.Native;

namespace PerfHud.UI;

/// <summary>
/// Palettes for the app's own windows (settings, editor, history, tray menu). Theme.xaml references these brushes with
/// DynamicResource, so <see cref="Apply"/> restyles templates live; windows rebuild their code-made parts on <see cref="Changed"/>.
/// </summary>
public static class UiTheme
{
    public sealed record Palette(string Name, string Description, bool Dark,
        string Bg, string Sidebar, string Card, string Input, string Popup, string Hover, string Pressed,
        string Border, string BorderStrong, string Text, string Muted, string Accent, string OnAccent,
        string Success, string Warning, string Danger, string Canvas);

    public static readonly Palette[] All =
    {
        new("Paper", "Warm off-white, ink text, signal orange", false,
            "#F2EFE8", "#E9E5DB", "#FAF8F3", "#FFFFFF", "#FBFAF7", "#E3DED2", "#D8D2C4",
            "#DAD4C7", "#B9B2A3", "#1E1C18", "#7A7469", "#E2531F", "#FFFFFF",
            "#3C7D3A", "#B07A00", "#C0272D", "#E6E1D6"),
        new("Ink", "Bright white, black ink, red pen", false,
            "#F7F6F2", "#EDEBE5", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#E7E4DC", "#DAD6CC",
            "#DFDBD2", "#BDB8AC", "#111111", "#6F6B63", "#B3261E", "#FFFFFF",
            "#2F6F3A", "#9A6B00", "#B3261E", "#EAE7E0"),
        new("Carbon", "Warm charcoal with an ember accent", true,
            "#191816", "#141311", "#201E1B", "#24221F", "#22201D", "#2B2925", "#34312C",
            "#34312C", "#4A4640", "#EDE9E0", "#8E887D", "#F0652F", "#140C08",
            "#8DBF6A", "#E2B14A", "#E5534B", "#121110"),
        new("Moss", "Deep olive with a lime accent", true,
            "#161A14", "#12150F", "#1D221A", "#20261C", "#1E231B", "#283022", "#303A29",
            "#2E3628", "#455040", "#E6EADB", "#889079", "#C9DA5A", "#161A0C",
            "#9CCB6B", "#E0B64E", "#E06A55", "#0F120D"),
        new("Linen", "Soft sand, walnut brown, olive green", false,
            "#EEE8DC", "#E4DCCC", "#F7F3EA", "#FFFDF8", "#F9F6EF", "#DFD6C4", "#D2C7B2",
            "#D6CCB9", "#B3A68E", "#2A231A", "#7D715F", "#8A5A2B", "#FFFFFF",
            "#5E7D3A", "#A87400", "#A8322A", "#E3DCCD"),
        new("Sage", "Pale grey-green with a deep teal pen", false,
            "#ECEFEA", "#E1E6DF", "#F6F8F4", "#FFFFFF", "#F8FAF6", "#DAE0D6", "#CDD5C8",
            "#D2D9CD", "#AEB8A8", "#1B211C", "#6E786E", "#2F6B5E", "#FFFFFF",
            "#3E7D3A", "#A07400", "#B3392E", "#E0E5DC"),
        new("Slate", "Cool blue-grey with a parchment accent", true,
            "#16191C", "#121417", "#1C2024", "#202529", "#1E2226", "#272C31", "#30363C",
            "#2E343A", "#454D55", "#E6E9EC", "#858E97", "#E8D9B0", "#1A1710",
            "#9BC28A", "#E2B45A", "#E05F55", "#101214"),
        new("Plum", "Dusky aubergine with a rose accent", true,
            "#1A141C", "#151017", "#211A24", "#251D28", "#231B26", "#2E2531", "#382D3B",
            "#352B38", "#4D4150", "#EFE6EE", "#928393", "#D88FB0", "#1E0F16",
            "#A8C98A", "#E6BE6A", "#E8606A", "#130E15"),
    };

    public static Palette Current { get; private set; } = All[0];

    /// <summary>Raised after the palette changed (windows rebuild code-created content).</summary>
    public static event Action? Changed;

    public static Palette Find(string? name) => All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static void Apply(string? name)
    {
        var p = Find(name);
        bool changed = !ReferenceEquals(p, Current);
        Current = p;
        var r = Application.Current.Resources;
        void B(string key, string hex, byte alpha = 255)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            r[key] = b;
        }
        B("BgBrush", p.Bg); B("SidebarBrush", p.Sidebar); B("CardBrush", p.Card); B("InputBrush", p.Input); B("PopupBrush", p.Popup);
        B("HoverBrush", p.Hover); B("PressedBrush", p.Pressed); B("BorderBrush", p.Border); B("BorderStrongBrush", p.BorderStrong);
        B("TextBrush", p.Text); B("MutedBrush", p.Muted); B("AccentBrush", p.Accent); B("OnAccentBrush", p.OnAccent);
        B("AccentSoftBrush", p.Accent, 0x22); B("SuccessBrush", p.Success); B("WarningBrush", p.Warning); B("DangerBrush", p.Danger);
        B("CanvasBrush", p.Canvas); B("GridLineBrush", p.Text, 0x26);
        if (changed) Changed?.Invoke();
    }

    /// <summary>Colors the native title bar to match the palette (Windows 11; dark/light flag on Windows 10).</summary>
    public static void StyleTitleBar(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        var bg = (Color)ColorConverter.ConvertFromString(Current.Bg);
        var fg = (Color)ColorConverter.ConvertFromString(Current.Text);
        var border = (Color)ColorConverter.ConvertFromString(Current.BorderStrong);
        Win32.StyleTitleBar(h, Current.Dark, Ref(bg), Ref(fg), Ref(border));
    }

    private static int Ref(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
