using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using PerfHud.Core;
using PerfHud.Hud;
using PerfHud.Monitoring;
using PerfHud.Notifications;
using PerfHud.Profiles;
using PerfHud.Settings;
using PerfHud.UI.Controls;

namespace PerfHud.UI;

public sealed partial class SettingsWindow
{
    // ── Profiles ────────────────────────────────────────

    private UIElement PageProfiles()
    {
        var ps = S.Profiles;
        var root = new StackPanel();
        var presetChoices = new List<(string, string)> { ("", "Keep current layout"), (HudPresets.HiddenToken, "Hide the HUD") };
        presetChoices.AddRange(HudPresets.AllNames(S).Select(n => (n, n)));
        var desktopChoices = new List<(string, string)> { ("", "Do nothing"), (HudPresets.HiddenToken, "Hide the HUD") };
        desktopChoices.AddRange(HudPresets.AllNames(S).Select(n => (n, $"Switch to {n}")));

        var status = Ui.Muted("", 12.5);
        Live(() =>
        {
            var m = _app.Profiles.Current;
            status.Text = $"Foreground app: {m.ForegroundExe ?? "—"}   ·   Active profile: {(m.Profile != null ? m.Profile.ExeName : m.Desktop ? "Desktop" : "none (default settings)")}";
        });

        root.Children.Add(Ui.Card("Automatic switching", "When a listed app starts (or is focused), its profile is applied. When it closes, the HUD returns to your normal settings.",
            Ui.Toggle("Enable app profiles", null, ps, nameof(ProfileSettings.Enabled)),
            Ui.ComboMap("When the desktop is focused", null, ps, nameof(ProfileSettings.DesktopAction), desktopChoices),
            status));

        // Add
        var pick = new ComboBox { IsEditable = true, Width = 300, ItemsSource = ProfileManager.RunningAppExes() };
        pick.Text = "";
        var add = Ui.Button("Add profile", () =>
        {
            var exe = pick.Text.Trim();
            if (exe.Length == 0) return;
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            ps.Items.Add(new AppProfile { ExeName = exe, Preset = HudPresets.GamingName });
            Show(_pages.First(p => p.Key == "Profiles"));
        }, "AccentButton", "plus");
        add.Margin = new Thickness(8, 0, 0, 0);
        var addRow = new StackPanel { Orientation = Orientation.Horizontal };
        addRow.Children.Add(pick);
        addRow.Children.Add(add);
        root.Children.Add(Ui.Card("Add a profile", "Pick a running app or type an executable name (e.g. Cyberpunk2077.exe).", addRow));

        var cornerChoices = new List<(HudCorner?, string)> { (null, "Default") };
        cornerChoices.AddRange(Enum.GetValues<HudCorner>().Where(c => c != HudCorner.Custom).Select(c => ((HudCorner?)c, Split(c.ToString()))));
        var compactChoices = new List<(bool?, string)> { (null, "Default"), (true, "Compact"), (false, "Detailed") };

        foreach (var p in ps.Items.ToList())
        {
            var prof = p;
            var exeBox = new TextBox();
            exeBox.SetBinding(TextBox.TextProperty, Ui.Bind(prof, nameof(AppProfile.ExeName)));
            var remove = Ui.Button("Remove", () => { ps.Items.Remove(prof); Show(_pages.First(x => x.Key == "Profiles")); }, "DangerButton", "trash");
            root.Children.Add(Ui.Card(prof.ExeName, null,
                Ui.Toggle("Enabled", null, prof, nameof(AppProfile.Enabled)),
                Ui.Row("Executable", null, exeBox),
                Ui.ComboMap("Activate", null, prof, nameof(AppProfile.Trigger), new[] { (ProfileTrigger.WhileFocused, "While the app is focused"), (ProfileTrigger.WhileRunning, "While the app is running") }),
                Ui.ComboMap("Layout", null, prof, nameof(AppProfile.Preset), presetChoices),
                Ui.ComboMap("Position", null, prof, nameof(AppProfile.Corner), cornerChoices),
                Ui.ComboMap("Mode", null, prof, nameof(AppProfile.Compact), compactChoices),
                Ui.Text("Opacity override", "0.2 – 1, empty = default", prof, nameof(AppProfile.Opacity), 110, NullableDoubleConverter.Instance),
                Ui.Text("Scale override", "0.5 – 3, empty = default", prof, nameof(AppProfile.Scale), 110, NullableDoubleConverter.Instance),
                Ui.Toggle("Record a session automatically", null, prof, nameof(AppProfile.AutoRecord)),
                Ui.Buttons(remove)));
        }
        if (ps.Items.Count == 0) root.Children.Add(Ui.Muted("No profiles yet."));
        return root;
    }

    // ── Alerts ──────────────────────────────────────────

    private UIElement PageAlerts()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.Callout("Alerts fire when a value stays past its threshold for the given time, then wait for the cooldown. " +
            "Choose any mix of a small corner notification, a warning line on the HUD, and a sound."));
        var metrics = MetricRegistry.All.Where(d => !d.IsText).Select(d => new KeyValuePair<string, string>(d.Id, $"{d.Group} · {d.Name}")).ToList();

        foreach (var r in S.Alerts.ToList())
        {
            var rule = r;
            var name = new TextBox { Width = 220 };
            name.SetBinding(TextBox.TextProperty, Ui.Bind(rule, nameof(AlertRule.Name)));
            var en = new CheckBox();
            en.SetBinding(ToggleButton.IsCheckedProperty, Ui.Bind(rule, nameof(AlertRule.Enabled)));
            var del = Ui.Button("", () => { S.Alerts.Remove(rule); Show(_pages.First(x => x.Key == "Alerts")); }, "GhostButton", "trash");
            del.ToolTip = "Delete alert";
            var head = new DockPanel();
            DockPanel.SetDock(del, Dock.Right); head.Children.Add(del);
            DockPanel.SetDock(en, Dock.Right); en.Margin = new Thickness(0, 0, 8, 0); head.Children.Add(en);
            head.Children.Add(name);

            var metric = new ComboBox { ItemsSource = metrics, DisplayMemberPath = "Value", SelectedValuePath = "Key", Width = 250 };
            metric.SetBinding(Selector.SelectedValueProperty, Ui.Bind(rule, nameof(AlertRule.MetricId)));
            var op = new ComboBox { ItemsSource = new[] { new KeyValuePair<CompareOp, string>(CompareOp.Above, "above"), new(CompareOp.Below, "below") }, DisplayMemberPath = "Value", SelectedValuePath = "Key", Width = 90, Margin = new Thickness(8, 0, 0, 0) };
            op.SetBinding(Selector.SelectedValueProperty, Ui.Bind(rule, nameof(AlertRule.Op)));
            TextBox Num(string path, double w)
            {
                var t = new TextBox { Width = w, Margin = new Thickness(8, 0, 0, 0), TextAlignment = TextAlignment.Right };
                var b = Ui.Bind(rule, path); b.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
                t.SetBinding(TextBox.TextProperty, b);
                return t;
            }
            var unit = Ui.Muted("", 12); unit.Margin = new Thickness(6, 0, 0, 0); unit.VerticalAlignment = VerticalAlignment.Center; unit.Width = 36;
            void UpdateUnit() => unit.Text = AlertEngine.ThresholdUnit(MetricRegistry.Get(rule.MetricId));
            UpdateUnit();
            metric.SelectionChanged += (_, _) => UpdateUnit();
            var cond = new WrapPanel();
            cond.Children.Add(metric); cond.Children.Add(op); cond.Children.Add(Num(nameof(AlertRule.Threshold), 70)); cond.Children.Add(unit);
            cond.Children.Add(new TextBlock { Text = "for", Foreground = Ui.Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            cond.Children.Add(Num(nameof(AlertRule.DurationSeconds), 50));
            cond.Children.Add(new TextBlock { Text = "s · cooldown", Foreground = Ui.Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            cond.Children.Add(Num(nameof(AlertRule.CooldownSeconds), 60));
            cond.Children.Add(new TextBlock { Text = "s", Foreground = Ui.Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });

            CheckBox Box(string text, string path)
            {
                var c = new CheckBox { Style = Ui.StyleRes("BoxCheck"), Content = text, Margin = new Thickness(0, 0, 18, 0) };
                c.SetBinding(ToggleButton.IsCheckedProperty, Ui.Bind(rule, path));
                return c;
            }
            var acts = new WrapPanel();
            acts.Children.Add(Box("Notification", nameof(AlertRule.Notify)));
            acts.Children.Add(Box("HUD warning", nameof(AlertRule.Hud)));
            acts.Children.Add(Box("Sound", nameof(AlertRule.Sound)));
            acts.Children.Add(Box("Only on battery", nameof(AlertRule.OnlyOnBattery)));

            root.Children.Add(Ui.Card(null, null, head, cond, acts));
        }

        root.Children.Add(Ui.Buttons(
            Ui.Button("Add alert", () => { S.Alerts.Add(new AlertRule { Name = "New alert", MetricId = "cpu.temp", Threshold = 90 }); Show(_pages.First(x => x.Key == "Alerts")); }, "AccentButton", "plus"),
            Ui.Button("Test notification", () => { ToastWindow.Show("Test alert", "This is how alerts look.", Severity.Warm, _app.Hud.CurrentStyle); _app.Hud.ShowInfo("This is a HUD warning line"); }, null, "bell"),
            Ui.Button("Restore defaults", () =>
            {
                S.Alerts.Clear();
                foreach (var r in AlertRule.Defaults()) S.Alerts.Add(r);
                Show(_pages.First(x => x.Key == "Alerts"));
            }, null, "history")));
        return root;
    }

    // ── History ─────────────────────────────────────────

    private UIElement PageHistory()
    {
        var h = S.History;
        var status = Ui.Muted("", 12.5);
        var toggle = Ui.Button("", _app.ToggleRecording, "AccentButton", "record");
        Live(() =>
        {
            status.Text = _app.Recorder.IsRecording
                ? $"Recording \"{_app.Recorder.Current?.Name}\" ({_app.Recorder.Trigger}) · {_app.Recorder.Current?.Samples} samples"
                : "Not recording.";
            ((TextBlock)((StackPanel)toggle.Content).Children[1]).Text = _app.Recorder.IsRecording ? "Stop recording" : "Start recording";
        });
        return Ui.Stack(
            Ui.Card("Recording", "Sessions capture FPS (with exact 1% / 0.1% lows from every frame), CPU, GPU, temperatures, RAM, VRAM, battery and network.",
                Ui.ComboMap("Record automatically", null, h, nameof(HistorySettings.AutoRecord), new[]
                {
                    (AutoRecordMode.Off, "Off (manual only)"),
                    (AutoRecordMode.ProfilesOnly, "For profiles with auto-record"),
                    (AutoRecordMode.GamesDetected, "When a full-screen game is detected"),
                }, 300),
                Ui.Slider("Sample interval", null, h, nameof(HistorySettings.SampleIntervalSeconds), 1, 10, 1, "{0:0} s"),
                Ui.Slider("Keep sessions for", null, h, nameof(HistorySettings.RetentionDays), 7, 365, 1, "{0:0} days"),
                status,
                Ui.Buttons(toggle, Ui.Button("Open history", () => _app.OpenHistory(), null, "history"),
                    Ui.Button("Open sessions folder", () => OpenFolder(AppPaths.SessionDir), null, "disk"))));
    }

    // ── Performance ─────────────────────────────────────

    private UIElement PagePerformance()
    {
        var p = S.Performance;
        var overhead = Ui.Muted("", 12.5);
        Live(() =>
        {
            var st = _app.Store;
            overhead.Text = $"PerfHud is using {st.Get("app.cpu"):0.00}% CPU and {st.Get("app.mem") / 1048576:0} MB RAM. " +
                            $"Throttle factor ×{_app.Monitoring.ThrottleFactor:0.0}{(_app.Monitoring.Context.PowerSaving ? " · battery mode active" : "")}.";
        });
        var fpsStatus = Ui.Muted("", 12.5);
        Live(() =>
        {
            var s = _app.Monitoring.Statuses.FirstOrDefault(x => x.Name == "FPS");
            fpsStatus.Text = $"Status: {s?.State} — {s?.Message}";
        });
        return Ui.Stack(
            Ui.Card("Polling", "Lower polling = less CPU. Each sensor runs isolated; a failing one never blocks the others.",
                Ui.Slider("Sensor polling interval", null, p, nameof(PerformanceSettings.SensorIntervalMs), 250, 5000, 250, "{0:0} ms"),
                Ui.ComboMap("Default graph history", null, p, nameof(PerformanceSettings.GraphHistorySeconds), new[] { (5, "5 seconds"), (10, "10 seconds"), (30, "30 seconds"), (60, "60 seconds"), (300, "5 minutes") }),
                Ui.Toggle("Latency probe", "Pings your router (default gateway) — nothing leaves your network unless you set a host", p, nameof(PerformanceSettings.PingEnabled)),
                Ui.Text("Ping host", "Empty = default gateway", p, nameof(PerformanceSettings.PingHost), 200)),
            Ui.Card("FPS sampling", null,
                Ui.Toggle("FPS counter", "Passive ETW frame timing (PresentMon technique) — nothing is injected into games", p, nameof(PerformanceSettings.FpsEnabled)),
                Ui.ComboMap("Statistics window", "Window for average, 1% low and 0.1% low", p, nameof(PerformanceSettings.FpsStatsWindowSeconds), new[] { (10, "10 seconds"), (30, "30 seconds"), (60, "1 minute"), (120, "2 minutes"), (300, "5 minutes") }),
                Ui.ComboMap("Measure", null, p, nameof(PerformanceSettings.FpsTarget), new[] { (FpsTargetMode.ForegroundApp, "Only the focused app"), (FpsTargetMode.ForegroundThenMostActive, "Focused app, else the busiest renderer") }, 300),
                fpsStatus,
                Ui.Buttons(Ui.Button("Enable FPS without admin (one-time)", EnableFpsWithoutAdmin, null, "shield"))),
            Ui.Card("PerfHud footprint", null,
                Ui.Slider("CPU usage limit", "If PerfHud uses more than this (% of total CPU), it slows its own polling", p, nameof(PerformanceSettings.CpuLimitPercent), 0.25, 5, 0.25, "{0:0.00} %"),
                Ui.Toggle("Adaptive throttling", null, p, nameof(PerformanceSettings.AdaptiveThrottle)),
                overhead));
    }

    // ── Battery ─────────────────────────────────────────

    private UIElement PageBattery()
    {
        var b = S.Battery;
        var impact = Ui.Muted("", 12.5);
        Live(() =>
        {
            var st = _app.Store;
            double appCpu = st.Get("app.cpu"), pkg = st.Get("cpu.power"), drain = st.Get("bat.power");
            string est = !double.IsNaN(appCpu) && !double.IsNaN(pkg) ? $"≈ {appCpu / 100 * pkg * Environment.ProcessorCount / Math.Max(1, Environment.ProcessorCount):0.00} W" : "n/a";
            var src = st.GetText("bat.source") ?? "?";
            double mult = Math.Max(1, b.IntervalMultiplier);
            impact.Text = $"Power source: {src}. Battery optimization is {(_app.Monitoring.Context.PowerSaving ? "ACTIVE" : "inactive")}.\n" +
                          $"PerfHud currently uses {appCpu:0.00}% CPU ({est} of the {pkg:0.0} W CPU package)." +
                          (double.IsNaN(drain) || drain <= 0 ? "" : $" The laptop is drawing {drain:0.0} W from the battery.") +
                          $"\nWith optimization on, polling and HUD redraws run {mult:0.#}× less often (≈{(1 - 1 / mult) * 100:0}% less PerfHud overhead)" +
                          (b.DisableExpensiveSensors ? ", and expensive sensors (GPU engine counters, latency probe, LibreHardwareMonitor, SMART) slow down 4× more." : ".");
        });
        return Ui.Stack(
            Ui.Card("Battery optimization", "Applied automatically when you unplug, removed when you plug back in.",
                Ui.Toggle("Enable battery optimization", null, b, nameof(BatterySettings.OptimizationEnabled)),
                Ui.Slider("Slow polling by", null, b, nameof(BatterySettings.IntervalMultiplier), 1, 4, 0.5, "{0:0.0}×"),
                Ui.Toggle("Reduce animations", null, b, nameof(BatterySettings.ReduceAnimations)),
                Ui.Toggle("Throttle expensive sensors", null, b, nameof(BatterySettings.DisableExpensiveSensors))),
            Ui.Card("Estimated impact", null, impact),
            Ui.Card("Hybrid graphics", null,
                Ui.Toggle("Don't wake a sleeping discrete GPU", "On Optimus / hybrid laptops the dGPU powers off when idle; polling it would wake it and cost several watts", S.Sensors, nameof(SensorSettings.AvoidWakingDiscreteGpu))));
    }

    // ── Sensors & temperatures ──────────────────────────

    private UIElement PageSensors()
    {
        var t = S.Thresholds;
        UIElement Temp(string label, TempThreshold th)
        {
            TextBox Box(string path)
            {
                var tb = new TextBox { Width = 64, Margin = new Thickness(6, 0, 0, 0), TextAlignment = TextAlignment.Right };
                var bind = Ui.Bind(th, path); bind.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
                tb.SetBinding(TextBox.TextProperty, bind);
                return tb;
            }
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            void L(string s, string brushKey) => sp.Children.Add(new TextBlock { Text = s, Foreground = Ui.Res(brushKey), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontSize = 12 });
            L("Warm", "WarningBrush"); sp.Children.Add(Box(nameof(TempThreshold.Warm)));
            L("Hot", "WarningBrush"); sp.Children.Add(Box(nameof(TempThreshold.Hot)));
            L("Critical", "DangerBrush"); sp.Children.Add(Box(nameof(TempThreshold.Critical)));
            return Ui.Row(label, "°C", sp, 0);
        }

        var list = new StackPanel();
        var providers = Ui.Muted("", 12);
        Live(() =>
        {
            list.Children.Clear();
            var temps = _app.Store.GetObject<List<SensorReading>>("temps.all") ?? new();
            var style = _app.Hud.CurrentStyle;
            foreach (var r in temps)
            {
                var def = new MetricDefinition("t", r.Name, r.Name, MetricKind.Temperature, "Temperature", Temp: r.Category);
                var sev = MetricRegistry.Evaluate(def, r.Value, S);
                var (v, u) = MetricRegistry.Format(def, r.Value, S);
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var val = new TextBlock { Text = (sev >= Severity.Hot ? "⚠ " : "") + v + u + "  " + sev, Foreground = style.ForSeverity(sev), FontWeight = FontWeights.SemiBold, Width = 150, TextAlignment = TextAlignment.Right };
                DockPanel.SetDock(val, Dock.Right);
                row.Children.Add(val);
                row.Children.Add(new TextBlock { Text = $"{r.Name}  ", Foreground = Ui.Res("TextBrush") });
                row.Children.Add(new TextBlock { Text = r.Source, Foreground = Ui.Res("MutedBrush"), FontSize = 11 });
                list.Children.Add(row);
            }
            if (temps.Count == 0) list.Children.Add(Ui.Muted(_app.Store.GetReason("cpu.temp") ?? "No temperature sensors are accessible."));
            providers.Text = _app.Temps.ProviderStatus;
        });

        return Ui.Stack(
            Ui.Card("All temperature sensors", "Live. Color = severity; the text label is shown too so color is never the only cue.", list, providers),
            Ui.Card("Temperature thresholds", "Cool (blue) below warm − 20 · normal (green) · warm (yellow) · hot (orange) · critical (red)",
                Temp("CPU", t.Cpu), Temp("GPU", t.Gpu), Temp("SSD / storage", t.Storage), Temp("Battery", t.Battery), Temp("Other sensors", t.Other)),
            Ui.Card("Load thresholds", null,
                Ui.Number("Usage warning (%)", null, t, nameof(ThresholdSettings.UsageWarn)),
                Ui.Number("Usage critical (%)", null, t, nameof(ThresholdSettings.UsageCritical)),
                Ui.Number("Memory warning (%)", null, t, nameof(ThresholdSettings.MemoryWarn)),
                Ui.Number("Memory critical (%)", null, t, nameof(ThresholdSettings.MemoryCritical)),
                Ui.Number("FPS warning below", null, t, nameof(ThresholdSettings.FpsWarn)),
                Ui.Number("FPS critical below", null, t, nameof(ThresholdSettings.FpsCritical)),
                Ui.Number("Battery warning at (%)", null, t, nameof(ThresholdSettings.BatteryWarn)),
                Ui.Number("Battery critical at (%)", null, t, nameof(ThresholdSettings.BatteryCritical))),
            Ui.Card("Sensor sources", null,
                Ui.Toggle("LibreHardwareMonitor", "Extended sensors: AMD GPUs, SATA SMART temperatures (admin)", S.Sensors, nameof(SensorSettings.UseLibreHardwareMonitor)),
                Ui.Toggle("Allow kernel driver (CPU sensors)", "Needs admin + the PawnIO driver. Gives per-core temps/power. Some anti-cheats block games while such drivers are loaded — off by default.", S.Sensors, nameof(SensorSettings.AllowKernelDriver)),
                Ui.Toggle("OEM sensors (WMI)", "e.g. Lenovo GameZone CPU/GPU temperature & performance mode (admin)", S.Sensors, nameof(SensorSettings.UseVendorWmi)),
                Ui.Toggle("Drive temperatures", null, S.Sensors, nameof(SensorSettings.DiskTemperatures))));
    }

    // ── Diagnostics ─────────────────────────────────────

    private UIElement PageDiagnostics()
    {
        var table = new Grid();
        foreach (var w in new[] { 160.0, 110, 0, 70 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        Live(() =>
        {
            table.Children.Clear();
            table.RowDefinitions.Clear();
            int r = 0;
            void Cell(string text, int col, Brush? b = null, bool bold = false)
            {
                var tb = new TextBlock { Text = text, Foreground = b ?? Ui.Res("TextBrush"), FontSize = 12, Margin = new Thickness(0, 3, 10, 3), TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
                Grid.SetRow(tb, r); Grid.SetColumn(tb, col);
                table.Children.Add(tb);
            }
            table.RowDefinitions.Add(new RowDefinition());
            Cell("Monitor", 0, Ui.Res("MutedBrush"), true); Cell("State", 1, Ui.Res("MutedBrush"), true); Cell("Detail", 2, Ui.Res("MutedBrush"), true); Cell("Cost", 3, Ui.Res("MutedBrush"), true);
            foreach (var s in _app.Monitoring.Statuses)
            {
                r++;
                table.RowDefinitions.Add(new RowDefinition());
                var b = s.State switch
                {
                    MonitorState.Ok => Ui.Res("SuccessBrush"),
                    MonitorState.Degraded or MonitorState.Paused => Ui.Res("WarningBrush"),
                    MonitorState.Failing => Ui.Res("DangerBrush"),
                    _ => Ui.Res("MutedBrush"),
                };
                Cell(s.Name, 0);
                Cell("● " + s.State, 1, b);
                Cell(s.Message ?? "", 2, Ui.Res("MutedBrush"));
                Cell($"{s.LastDurationMs:0} ms", 3, Ui.Res("MutedBrush"));
            }
        });

        var p = S.Privacy;
        return Ui.Stack(
            Ui.Card("Monitors", "Each monitor fails independently and retries with backoff. Unavailable sensors show N/A on the HUD.", table),
            Ui.Card("Tools", null, Ui.Buttons(
                Ui.Button("Open logs", () => OpenFolder(AppPaths.LogDir), null, "list"),
                Ui.Button("Open config folder", () => OpenFolder(AppPaths.ConfigDir), null, "settings"),
                Ui.Button("Copy system information", () =>
                {
                    Clipboard.SetText(SystemInfoReport.Build(_app));
                    _app.Hud.ShowInfo("System information copied");
                }, "AccentButton", "copy"),
                Ui.Button("Restart as administrator", () => { if (Elevation.RestartElevated()) Application.Current.Shutdown(); }, null, "shield"),
                Ui.Button("Reset monitors", () => _app.Monitoring.RequestResetAll("user request"), null, "history"))),
            Ui.Card("Settings file", AppPaths.SettingsFile, Ui.Buttons(
                Ui.Button("Export…", ExportSettings, null, "copy"),
                Ui.Button("Import…", ImportSettings, null, "plus"),
                Ui.Button("Reset everything", () =>
                {
                    if (MessageBox.Show(this, "Reset all PerfHud settings to defaults?", "PerfHud", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        _app.Settings.ResetToDefaults();
                }, "DangerButton", "trash"))),
            Ui.Card("Privacy", "Nothing is ever sent anywhere. These only affect what the HUD displays.",
                Ui.Toggle("Show local IP address", null, p, nameof(PrivacySettings.ShowLocalIp)),
                Ui.Toggle("Show the measured app's name", null, p, nameof(PrivacySettings.ShowProcessNames))));
    }

    private void ExportSettings()
    {
        var d = new SaveFileDialog { FileName = "perfhud-settings.json", Filter = "JSON|*.json" };
        if (d.ShowDialog(this) == true) File.WriteAllText(d.FileName, _app.Settings.Export());
    }

    private void ImportSettings()
    {
        var d = new OpenFileDialog { Filter = "JSON|*.json" };
        if (d.ShowDialog(this) != true) return;
        if (!_app.Settings.Import(File.ReadAllText(d.FileName), out var err))
            MessageBox.Show(this, $"Import failed: {err}", "PerfHud", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Could not open {path}: {ex.Message}"); }
    }

    // ── About ───────────────────────────────────────────

    private UIElement PageAbout()
    {
        var v = typeof(AppHost).Assembly.GetName().Version;
        return Ui.Stack(
            Ui.Card($"PerfHud {v?.ToString(3)}", "A lightweight, highly customizable performance overlay for Windows laptops.",
                Ui.Muted($"Running as {(Elevation.IsAdmin ? "administrator" : "standard user")} · .NET {Environment.Version} · {Environment.OSVersion.VersionString}"),
                Ui.Buttons(Ui.Button("Project page", () => Process.Start(new ProcessStartInfo("https://github.com/PS12007/perfhud") { UseShellExecute = true }), null, "info"))),
            Ui.Card("Privacy", null,
                Ui.Muted("• All monitoring data stays on this PC. There is no telemetry, analytics, update check or crash upload.\n" +
                         "• The only network activity is the optional latency probe to your own router (default gateway), or a host you choose.\n" +
                         "• Hardware info, IP addresses, app names and performance data are never transmitted.\n" +
                         $"• Settings: {AppPaths.ConfigDir}\n• Logs and sessions: {AppPaths.DataDir}")),
            Ui.Card("Safety", null,
                Ui.Muted("• FPS is read from Windows' own graphics event tracing (the PresentMon technique). Nothing is injected into games.\n" +
                         "• Hotkeys use RegisterHotKey (no global keyboard hooks).\n" +
                         "• System power settings are only read, never changed.")),
            Ui.Card("Open source", null,
                Ui.Muted("PerfHud is MIT licensed. It uses LibreHardwareMonitorLib (MPL-2.0) for optional extended sensors and NVIDIA's NVML (part of the NVIDIA driver).")));
    }
}
