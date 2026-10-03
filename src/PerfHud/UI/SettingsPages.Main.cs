using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PerfHud.Core;
using PerfHud.Hotkeys;
using PerfHud.Hud;
using PerfHud.Monitoring;
using PerfHud.Rendering;
using PerfHud.Sensors.Native;
using PerfHud.Settings;
using PerfHud.UI.Controls;

namespace PerfHud.UI;

public sealed partial class SettingsWindow
{
    public static readonly Dictionary<HotkeyAction, (string Name, string Desc)> ActionNames = new()
    {
        [HotkeyAction.ToggleHud] = ("Toggle HUD", "Show or hide the overlay"),
        [HotkeyAction.CyclePreset] = ("Cycle layout", "Minimal → Gaming → Full → your layouts"),
        [HotkeyAction.ToggleCompact] = ("Compact / detailed", "Hide graphs and detail rows"),
        [HotkeyAction.ToggleGraphs] = ("Toggle graphs", "Show or hide all graphs"),
        [HotkeyAction.ToggleClickThrough] = ("Lock / unlock HUD", "Unlocked = drag to move, Ctrl+scroll to scale"),
        [HotkeyAction.ToggleSettings] = ("Show / hide settings", "Open this window"),
        [HotkeyAction.TogglePause] = ("Pause / resume monitoring", "Stops all polling"),
        [HotkeyAction.ScreenshotHud] = ("Screenshot HUD", "Saves a PNG to Pictures\\PerfHud"),
        [HotkeyAction.ResetPosition] = ("Reset HUD position", "Back to the top-left corner"),
        [HotkeyAction.ToggleRecording] = ("Start / stop recording", "Record a performance session"),
        [HotkeyAction.OpenEditor] = ("Open HUD editor", "Drag-and-drop layout editor"),
    };

    // ── Overview ────────────────────────────────────────

    private UIElement PageHome()
    {
        var root = new StackPanel();
        string Gesture(HotkeyAction a) => S.Hotkeys.FirstOrDefault(h => h.Action == a)?.Gesture is { Length: > 0 } g ? g : "unbound";
        if (_welcome)
            root.Children.Add(Ui.Callout($"Welcome to PerfHud! The HUD is now on screen. Press {Gesture(HotkeyAction.ToggleHud)} to toggle it, " +
                $"{Gesture(HotkeyAction.CyclePreset)} to cycle layouts and {Gesture(HotkeyAction.ToggleCompact)} for compact mode. PerfHud lives in the system tray — " +
                "everything here applies instantly and is saved automatically.", "ok"));

        // Readout grid: hairline-ruled cells, mono caption, big number. Reads like an instrument panel, not a dashboard of cards.
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 30) };
        (TextBlock v, TextBlock d) Tile(string icon, string title)
        {
            _ = icon;
            var big = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("TextBrush"), FontFamily = new FontFamily("Bahnschrift, Segoe UI"), Margin = new Thickness(0, 4, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            var det = Ui.Muted("", 11.5);
            det.TextWrapping = TextWrapping.NoWrap;
            det.TextTrimming = TextTrimming.CharacterEllipsis;
            var head = Ui.Caption(title);
            head.Foreground = Ui.Res("MutedBrush");
            head.FontWeight = FontWeights.Normal;
            int i = tiles.Children.Count;
            var b = new Border
            {
                BorderBrush = Ui.Res("BorderStrongBrush"), BorderThickness = new Thickness(i % 3 == 0 ? 0 : 1, 1, 0, i >= 3 ? 1 : 0),
                Padding = new Thickness(i % 3 == 0 ? 0 : 16, 12, 12, 14), Child = Ui.Stack(head, big, det),
            };
            tiles.Children.Add(b);
            return (big, det);
        }
        var cpu = Tile("cpu", "CPU");
        var gpu = Tile("gpu", "GPU");
        var ram = Tile("ram", "MEMORY");
        var bat = Tile("battery", "BATTERY");
        var fps = Tile("fps", "FRAME RATE");
        var self = Tile("activity", "PERFHUD OVERHEAD");
        root.Children.Add(tiles);

        var st = _app.Store;
        string F(string id) { var d = MetricRegistry.Get(id)!; var (v, u) = MetricRegistry.Format(d, st.Get(id), S); return v == MetricRegistry.NA ? v : (u is "%" or "°C" or "°F" ? v + u : $"{v} {u}"); }
        Live(() =>
        {
            cpu.v.Text = $"{F("cpu.usage")}  ·  {F("cpu.temp")}";
            cpu.d.Text = $"{st.GetText("cpu.name")} · {F("cpu.clock")} · {F("cpu.power")}";
            gpu.v.Text = $"{F("gpu.usage")}  ·  {F("gpu.temp")}";
            gpu.d.Text = $"{st.GetText("gpu.name")} · {st.GetText("gpu.state")} · VRAM {F("gpu.vram.used")}";
            ram.v.Text = F("ram.pct");
            ram.d.Text = $"{F("ram.used")} of {F("ram.total")} · {st.GetText("ram.type")}";
            bat.v.Text = F("bat.pct");
            bat.d.Text = $"{st.GetText("bat.state")} · {F("bat.power")} drain · health {F("bat.health")}";
            fps.v.Text = st.Has("fps.current") ? $"{st.Get("fps.current"):0} FPS" : "—";
            var fpsStatus = _app.Monitoring.Statuses.FirstOrDefault(s => s.Name == "FPS");
            fps.d.Text = st.Has("fps.current") ? $"{st.GetText("fps.app")} · 1% low {F("fps.low1")}" : fpsStatus?.State == MonitorState.Unavailable ? fpsStatus.Message ?? "" : "Waiting for an app to render frames";
            self.v.Text = $"{F("app.cpu")} CPU";
            self.d.Text = $"{F("app.mem")} RAM · {(_app.Monitoring.Context.PowerSaving ? "battery mode" : "normal mode")}";
        });

        // FPS capture
        var fpsCard = new StackPanel();
        var fpsText = Ui.Muted("", 12.5);
        fpsCard.Children.Add(fpsText);
        var fpsButtons = Ui.Buttons(
            Ui.Button("Enable FPS without admin (one-time)", EnableFpsWithoutAdmin, "AccentButton", "shield"),
            Ui.Button("Restart as administrator", () => { if (Elevation.RestartElevated()) Application.Current.Shutdown(); }, null, "settings"));
        fpsButtons.Margin = new Thickness(0, 10, 0, 0);
        fpsCard.Children.Add(fpsButtons);
        Live(() =>
        {
            var s = _app.Monitoring.Statuses.FirstOrDefault(x => x.Name == "FPS");
            bool ok = s?.State is MonitorState.Ok or MonitorState.Degraded;
            fpsText.Text = ok
                ? $"Frame timing is active (ETW present events, no injection — safe with anti-cheat). {_app.Fps.EventsReceived:N0} present events processed."
                : $"{s?.Message ?? "Starting…"}\n\nFPS is measured from Windows graphics events (like PresentMon). Windows only allows that for administrators " +
                  "or members of the built-in 'Performance Log Users' group. Joining that group once (UAC prompt, then sign out/in) lets PerfHud show FPS without running as admin.";
            fpsButtons.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        });
        root.Children.Add(Ui.Card("FPS capture", null, fpsCard));

        var access = Ui.Muted("", 12.5);
        Live(() =>
        {
            access.Text = Elevation.IsAdmin
                ? "Running as administrator: vendor sensors (e.g. Lenovo GameZone CPU temperature) and SMART drive temperatures are available."
                : $"Running as a normal user (recommended). CPU temperature: {(st.Has("cpu.temp") ? "available" : st.GetReason("cpu.temp") ?? "checking…")}. " +
                  "Enable 'Start elevated' under General to get admin-only sensors at every logon without UAC prompts.";
        });
        root.Children.Add(Ui.Card("Sensor access", null, access));

        root.Children.Add(Ui.Card("Quick actions", null, Ui.Buttons(
            Ui.Button("Toggle HUD", _app.Hud.Toggle, null, "eye"),
            Ui.Button("Next layout", _app.Hud.CyclePreset, null, "layers"),
            Ui.Button("Unlock / lock HUD", _app.Hud.ToggleClickThrough, null, "unlock"),
            Ui.Button("Open HUD editor", () => _app.OpenEditor(), null, "grid"),
            Ui.Button("Screenshot HUD", () => _app.Hud.Screenshot(), null, "copy"))));
        return root;
    }

    private void EnableFpsWithoutAdmin()
    {
        if (Elevation.AddCurrentUserToPerformanceLogUsers())
            MessageBox.Show(this, "Done. Sign out of Windows and back in once — after that PerfHud can show FPS without administrator rights.", "PerfHud", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(this, "The change wasn't applied (UAC was cancelled or the account can't be modified). You can also run PerfHud as administrator.", "PerfHud", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── General ─────────────────────────────────────────

    private UIElement PageGeneral()
    {
        var g = S.General;
        return Ui.Stack(
            Ui.Card("Startup", "Uses the per-user Run key — no services are installed.",
                Ui.Toggle("Start with Windows", null, g, nameof(GeneralSettings.StartWithWindows)),
                Ui.Toggle("Start elevated", "Registers a logon task with highest privileges (one UAC prompt now) so admin-only sensors work without prompts later", g, nameof(GeneralSettings.StartElevated)),
                Ui.Toggle("Start minimized", "Don't open this window when PerfHud starts", g, nameof(GeneralSettings.StartMinimized)),
                Ui.Toggle("Show HUD on startup", "Launch the overlay automatically", g, nameof(GeneralSettings.ShowHudOnStartup))),
            Ui.Card("Updates & units", null,
                Ui.Slider("HUD refresh interval", "How often values on the HUD update", g, nameof(GeneralSettings.HudRefreshMs), 100, 2000, 50, "{0:0} ms"),
                Ui.ComboMap("Temperature unit", null, g, nameof(GeneralSettings.TemperatureUnit), new[] { (TemperatureUnit.Celsius, "Celsius (°C)"), (TemperatureUnit.Fahrenheit, "Fahrenheit (°F)") }),
                Ui.ComboMap("Network speed unit", null, g, nameof(GeneralSettings.NetworkUnit), new[] { (NetworkUnit.Bytes, "Bytes (MB/s)"), (NetworkUnit.Bits, "Bits (Mb/s)") }),
                Ui.ComboMap("Language", "More languages can be added via the string catalog", g, nameof(GeneralSettings.Language), new[] { ("en", "English") })));
    }

    // ── Hotkeys ─────────────────────────────────────────

    private UIElement PageHotkeys()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.Callout("Hotkeys use the Windows RegisterHotKey API (no keyboard hooks), so they're anti-cheat safe. " +
            "If a combination is already taken by another app it's shown below and the rest keep working. Note: global F-keys are " +
            "unavailable to other apps while PerfHud runs (e.g. F11 browser full-screen) — rebind them here if needed."));
        var rows = new List<UIElement>();
        foreach (var b in S.Hotkeys.OrderBy(h => h.Action))
        {
            var (name, desc) = ActionNames.GetValueOrDefault(b.Action, (b.Action.ToString(), ""));
            var box = new HotkeyBox(b, _app.Hotkeys);
            var status = new TextBlock { FontSize = 11.5, Width = 210, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var reset = Ui.Button("Default", () => b.Gesture = DefaultHotkeys.For(b.Action), "GhostButton");
            reset.Margin = new Thickness(6, 0, 0, 0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(box); sp.Children.Add(reset); sp.Children.Add(status);
            Live(() =>
            {
                var (st, detail) = _app.Hotkeys.GetStatus(b.Action);
                status.Text = st switch { HotkeyState.Registered => "✓ Active", HotkeyState.Unbound => "Not set", _ => "⚠ " + detail };
                status.ToolTip = detail;
                status.Foreground = st switch { HotkeyState.Registered => Ui.Res("SuccessBrush"), HotkeyState.Unbound => Ui.Res("MutedBrush"), _ => Ui.Res("WarningBrush") };
            });
            rows.Add(Ui.Row(name, desc, sp, 0));
        }
        root.Children.Add(Ui.Card("Shortcuts", "Click a box and press the new combination. Backspace clears.", rows.ToArray()));
        root.Children.Add(Ui.Buttons(Ui.Button("Reset all to defaults", () => { foreach (var b in S.Hotkeys) b.Gesture = DefaultHotkeys.For(b.Action); }, null, "history")));
        return root;
    }

    // ── HUD ─────────────────────────────────────────────

    private UIElement PageHud()
    {
        var h = S.Hud;
        var monitors = new List<(string, string)> { ("", "Primary monitor"), ("*active", "Follow the active window") };
        int i = 1;
        foreach (var m in Win32.GetMonitors()) monitors.Add((m.Device, $"Monitor {i++}: {m.Bounds.Width}×{m.Bounds.Height}{(m.Primary ? " (primary)" : "")}"));

        var presetCombo = new ComboBox { ItemsSource = HudPresets.AllNames(S), Width = 200 };
        presetCombo.SetBinding(Selector.SelectedItemProperty, Ui.Bind(h, nameof(HudSettings.ActivePreset)));
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
        presetRow.Children.Add(presetCombo);
        var edit = Ui.Button("Edit", () => _app.OpenEditor(h.ActivePreset), null, "grid");
        edit.Margin = new Thickness(8, 0, 0, 0);
        presetRow.Children.Add(edit);

        return Ui.Stack(
            Ui.Card("Layout", "Minimal, Gaming and Full are built in. Duplicate one in the editor to customize it.",
                Ui.Toggle("HUD enabled", null, h, nameof(HudSettings.Enabled)),
                Ui.Row("Active layout", null, presetRow, 0),
                Ui.Toggle("Compact mode", "Hides graphs and detail rows, tighter spacing", h, nameof(HudSettings.Compact)),
                Ui.Toggle("Show graphs", null, h, nameof(HudSettings.ShowGraphs)),
                Ui.ComboMap("Orientation", "Horizontal turns any layout into a strip", h, nameof(HudSettings.Orientation), new[] { (HudOrientation.Vertical, "Vertical"), (HudOrientation.Horizontal, "Horizontal") }),
                Ui.ComboMap("Value alignment", null, h, nameof(HudSettings.Alignment), new[] { (HudAlignment.Left, "Left"), (HudAlignment.Center, "Center"), (HudAlignment.Right, "Right") })),
            Ui.Card("Position", "Unlock the HUD to drag it anywhere; Ctrl+scroll over it to scale.",
                Ui.ComboMap("Anchor", null, h, nameof(HudSettings.Corner), Enum.GetValues<HudCorner>().Select(c => (c, Split(c.ToString())))),
                Ui.Slider("Horizontal margin", null, h, nameof(HudSettings.OffsetX), 0, 400, 1, "{0:0} px"),
                Ui.Slider("Vertical margin", null, h, nameof(HudSettings.OffsetY), 0, 400, 1, "{0:0} px"),
                Ui.ComboMap("Monitor", null, h, nameof(HudSettings.Monitor), monitors, 300),
                Ui.Toggle("Avoid the taskbar", "Use the work area instead of the full screen", h, nameof(HudSettings.RespectTaskbar)),
                Ui.Buttons(Ui.Button("Reset position", _app.Hud.ResetPosition, null, "history"))),
            Ui.Card("Behavior", null,
                Ui.Toggle("Locked (click-through)", "Mouse clicks pass through the HUD to the game. Turn off to move it.", h, nameof(HudSettings.ClickThrough)),
                Ui.Toggle("Hide from screenshots & recordings", "Excludes the overlay from capture (OBS, Game Bar, screenshots)", h, nameof(HudSettings.HideFromCapture))));
    }

    private static string Split(string s) => System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])([A-Z])", " $1");

    // ── Metric picker ───────────────────────────────────

    private UIElement PageMetrics()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.Callout("Tick the metrics you want and press Apply — PerfHud builds the 'Custom' layout from them. " +
            "For full control over placement, sizes and component types, use the HUD editor."));

        var custom = HudPresets.FindEditable(HudPresets.CustomName, S);
        var current = custom?.Components.Select(c => c.MetricId).ToHashSet() ?? new HashSet<string>();
        var checks = new List<(CheckBox cb, MetricDefinition d)>();
        bool graphs = true;

        var st = _app.Store;
        foreach (var group in MetricRegistry.All.GroupBy(d => d.Group))
        {
            var wrap = new UniformGrid { Columns = 2 };
            foreach (var d in group)
            {
                var cb = new CheckBox { Style = Ui.StyleRes("BoxCheck"), IsChecked = current.Contains(d.Id), Margin = new Thickness(0, 3, 12, 3) };
                var val = Ui.Muted("", 11);
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(new TextBlock { Text = d.Name, FontSize = 12.5 });
                val.Margin = new Thickness(8, 0, 0, 0);
                label.Children.Add(val);
                cb.Content = label;
                var def = d;
                Live(() =>
                {
                    if (def.IsText) { val.Text = st.GetText(def.Id) ?? MetricRegistry.NA; return; }
                    var (v, u) = MetricRegistry.Format(def, st.Get(def.Id), S);
                    val.Text = v == MetricRegistry.NA ? v : $"{v}{(u is "%" or "°C" ? "" : " ")}{u}";
                });
                wrap.Children.Add(cb);
                checks.Add((cb, d));
            }
            root.Children.Add(Ui.Card(group.Key, null, wrap));
        }

        var gToggle = new CheckBox { IsChecked = graphs, Content = "Add a graph for each graphable metric" };
        root.Children.Add(Ui.Buttons(
            gToggle,
            Ui.Button("Apply to Custom layout", () =>
            {
                var selected = checks.Where(c => c.cb.IsChecked == true).Select(c => c.d).ToList();
                var layout = HudPresets.FindEditable(HudPresets.CustomName, S);
                if (layout == null) { layout = new HudLayout { Name = HudPresets.CustomName }; S.Layouts.Insert(0, layout); }
                var comps = new List<HudComponent>();
                int row = 0, col = 0;
                foreach (var d in selected)
                {
                    bool wide = d.IsText;
                    if (wide && col == 1) { row++; col = 0; }
                    comps.Add(new HudComponent { Type = ComponentType.Number, MetricId = d.Id, Col = col, Row = row, ColSpan = wide ? 2 : 1 });
                    if (wide || col == 1) { row++; col = 0; } else col = 1;
                    if (gToggle.IsChecked == true && d.Graphable)
                    {
                        if (col == 1) { row++; col = 0; }
                        comps.Add(new HudComponent { Type = ComponentType.Graph, MetricId = d.Id, Col = 0, Row = row++, ColSpan = 2, DetailOnly = true, Height = 24 });
                    }
                }
                layout.Components = new ObservableList<HudComponent>(comps);
                Observable.RaiseGlobal(layout, nameof(HudLayout.Components));
                S.Hud.ActivePreset = HudPresets.CustomName;
                _app.Hud.ShowInfo("Custom layout updated");
            }, "AccentButton", "layers")));
        return root;
    }
}
