using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PerfHud.Core;
using PerfHud.Hud;
using PerfHud.Hud.Elements;
using PerfHud.Monitoring;
using PerfHud.Settings;
using PerfHud.UI.Controls;

namespace PerfHud.UI;

public sealed partial class SettingsWindow
{
    // ── HUD look ────────────────────────────────────────

    private UIElement PageAppearance()
    {
        var a = S.Appearance;
        Aside(BuildPreview());

        // Look presets: a grid of named cards; the active one is outlined in ink.
        var looks = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        var lookCards = new List<(Button b, HudLook l)>();
        void SyncLooks()
        {
            foreach (var (b, l) in lookCards)
            {
                bool on = l.Name == a.Look;
                b.SetResourceReference(Control.BorderBrushProperty, on ? "TextBrush" : "BorderBrush");
                b.BorderThickness = new Thickness(on ? 2 : 1);
            }
        }
        foreach (var look in HudLooks.All)
        {
            var l = look;
            var title = new TextBlock { Text = l.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)Application.Current.Resources["UiDisplayFont"] };
            var desc = Ui.Muted(l.Description, 11.5);
            var b = new Button { Style = Ui.StyleRes("ChipButton"), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 8, 8), Content = Ui.Stack(title, desc) };
            b.SetResourceReference(Control.BackgroundProperty, "CardBrush");
            b.Click += (_, _) => { HudLooks.Apply(l, a); SyncLooks(); };
            lookCards.Add((b, l));
            looks.Children.Add(b);
        }
        SyncLooks();

        // Color themes: each swatch is a tiny HUD sample in that palette.
        var themes = new WrapPanel();
        foreach (var t in ThemeDefinition.BuiltIn)
        {
            var theme = t;
            var sample = new TextBlock { FontFamily = new FontFamily("Bahnschrift"), FontSize = 15, FontWeight = FontWeights.SemiBold };
            sample.Inlines.Add(new System.Windows.Documents.Run("62") { Foreground = ColorUtil.Brush(t.Text, Colors.White) });
            sample.Inlines.Add(new System.Windows.Documents.Run("°") { Foreground = ColorUtil.Brush(t.Muted, Colors.Gray) });
            sample.Inlines.Add(new System.Windows.Documents.Run(" 88") { Foreground = ColorUtil.Brush(t.Warning, Colors.Gold) });
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            foreach (var c in new[] { t.Accent, t.Success, t.Warning, t.Danger })
                strip.Children.Add(new Border { Width = 9, Height = 4, Margin = new Thickness(0, 0, 2, 0), Background = ColorUtil.Brush(c, Colors.Gray) });
            var swatch = new Border
            {
                Width = 74, Padding = new Thickness(8, 6, 8, 7), Background = ColorUtil.Brush(t.Background, Colors.Black),
                BorderBrush = ColorUtil.Brush(t.Accent, Colors.Gray), BorderThickness = new Thickness(3, 0, 0, 0),
                Child = Ui.Stack(sample, strip),
            };
            var name = new TextBlock { Text = t.Name, FontSize = 11.5, Margin = new Thickness(1, 4, 0, 0) };
            var b = new Button { Style = Ui.StyleRes("ChipButton"), BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 10), Content = Ui.Stack(swatch, name), ToolTip = $"Apply the {t.Name} colors" };
            b.Click += (_, _) => a.ApplyTheme(theme);
            themes.Children.Add(b);
        }

        var colorRows = new StackPanel { Visibility = Visibility.Collapsed };
        foreach (var (label, path) in new[]
        {
            ("Accent", nameof(AppearanceSettings.Accent)), ("Background", nameof(AppearanceSettings.Background)), ("Text", nameof(AppearanceSettings.Text)),
            ("Muted text", nameof(AppearanceSettings.Muted)), ("Cool", nameof(AppearanceSettings.Cool)), ("Normal", nameof(AppearanceSettings.Success)),
            ("Warm", nameof(AppearanceSettings.Warning)), ("Hot", nameof(AppearanceSettings.Hot)), ("Critical", nameof(AppearanceSettings.Danger)),
        })
        {
            var row = Ui.Color(label, a, path);
            row.Margin = new Thickness(0, 4, 0, 4);
            colorRows.Children.Add(row);
        }
        var toggleColors = Ui.Button("Edit individual colors", () => { }, "GhostButton");
        toggleColors.Padding = new Thickness(0, 4, 0, 4);
        toggleColors.HorizontalAlignment = HorizontalAlignment.Left;
        toggleColors.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        toggleColors.Click += (_, _) =>
        {
            colorRows.Visibility = colorRows.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            ((TextBlock)((StackPanel)toggleColors.Content).Children[1]).Text = colorRows.Visibility == Visibility.Visible ? "Hide individual colors" : "Edit individual colors";
        };

        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToList();
        ComboBox FontCombo(string path)
        {
            var c = new ComboBox { ItemsSource = fonts, IsEditable = true, Width = 230 };
            c.SetBinding(ComboBox.TextProperty, Ui.Bind(a, path));
            return c;
        }
        var weights = new[] { (WeightOption.Light, "Light"), (WeightOption.Regular, "Regular"), (WeightOption.Medium, "Medium"), (WeightOption.SemiBold, "Semi"), (WeightOption.Bold, "Bold") };

        return Ui.Stack(
            Ui.Card("Look", "A look sets shape and typography in one click. Colors stay as they are, and everything below can still be tweaked.", looks),
            Ui.Card("Colors", "Pick a palette for the HUD.", themes, Ui.Stack(toggleColors, colorRows)),
            Ui.Card("Panel", null,
                Ui.Segmented("Panel", "None = just the numbers, floating over the game", a, nameof(AppearanceSettings.PanelStyle), new[] { (PanelStyle.Solid, "Solid"), (PanelStyle.Outline, "Outline"), (PanelStyle.Bare, "None") }),
                Ui.Segmented("Accent edge", null, a, nameof(AppearanceSettings.PanelEdge), new[] { (PanelEdge.None, "None"), (PanelEdge.Left, "Left"), (PanelEdge.Top, "Top") }),
                Ui.Slider("Background opacity", "Solid panel only — text stays crisp", a, nameof(AppearanceSettings.BackgroundOpacity), 0, 1, 0.01, "{0:P0}"),
                Ui.Slider("Corner radius", null, a, nameof(AppearanceSettings.CornerRadius), 0, 20, 1, "{0:0} px"),
                Ui.Toggle("Frame", null, a, nameof(AppearanceSettings.Border)),
                Ui.Slider("Frame width", null, a, nameof(AppearanceSettings.BorderWidth), 0.5, 4, 0.5, "{0:0.0} px"),
                Ui.Text("Frame color", "Empty = automatic, or #RRGGBB", a, nameof(AppearanceSettings.BorderColor), 120),
                Ui.Slider("Padding", null, a, nameof(AppearanceSettings.PanelPadding), 0, 24, 1, "{0:0} px"),
                Ui.Toggle("Text halo", "Soft outline behind text — makes a panel-less HUD readable over bright scenes", a, nameof(AppearanceSettings.TextShadow)),
                Ui.Toggle("Drop shadow", "Solid panel only", a, nameof(AppearanceSettings.Shadow)),
                Ui.Toggle("Background blur (experimental)", "Frosted glass via the Windows compositor; on some builds it fills the square bounds", a, nameof(AppearanceSettings.Blur))),
            Ui.Card("Type", null,
                Ui.Row("Label font", null, FontCombo(nameof(AppearanceSettings.LabelFont)), 0),
                Ui.Row("Value font", "Tabular digits keep numbers from jumping: Bahnschrift, Cascadia Mono, Consolas", FontCombo(nameof(AppearanceSettings.ValueFont)), 0),
                Ui.Segmented("Label weight", null, a, nameof(AppearanceSettings.LabelWeight), weights),
                Ui.Segmented("Value weight", null, a, nameof(AppearanceSettings.ValueWeight), weights),
                Ui.Segmented("Label case", null, a, nameof(AppearanceSettings.LabelCase), new[] { (LabelCase.AsTyped, "As typed"), (LabelCase.Upper, "UPPER"), (LabelCase.Title, "Title"), (LabelCase.Lower, "lower") }),
                Ui.Segmented("Label position", null, a, nameof(AppearanceSettings.LabelPosition), new[] { (LabelPosition.Left, "Beside value"), (LabelPosition.Above, "Above value") }),
                Ui.Slider("Text size", null, a, nameof(AppearanceSettings.FontScale), 0.7, 1.8, 0.05, "{0:0.00}×"),
                Ui.Toggle("Labels", null, a, nameof(AppearanceSettings.ShowLabels)),
                Ui.Toggle("Units", "°C, GB, MB/s …", a, nameof(AppearanceSettings.ShowUnits)),
                Ui.Toggle("Icons", null, a, nameof(AppearanceSettings.ShowIcons))),
            Ui.Card("Size & spacing", null,
                Ui.Slider("Scale", "Ctrl + scroll over the unlocked HUD does the same", a, nameof(AppearanceSettings.Scale), 0.6, 2.5, 0.05, "{0:0.00}×"),
                Ui.Slider("Row spacing", null, a, nameof(AppearanceSettings.RowSpacing), 0, 12, 0.5, "{0:0.0} px"),
                Ui.Slider("Column spacing", null, a, nameof(AppearanceSettings.ColumnSpacing), 0, 24, 1, "{0:0} px"),
                Ui.Slider("HUD opacity", "Fades the whole overlay, text included", a, nameof(AppearanceSettings.Opacity), 0.2, 1, 0.01, "{0:P0}")),
            Ui.Card("Bars, graphs & gauges", null,
                Ui.Segmented("Bars", null, a, nameof(AppearanceSettings.BarStyle), new[] { (BarStyle.Segmented, "Segments"), (BarStyle.Square, "Square"), (BarStyle.Rounded, "Rounded"), (BarStyle.Line, "Line") }),
                Ui.Slider("Bar thickness", null, a, nameof(AppearanceSettings.BarThickness), 1, 12, 0.5, "{0:0.0} px"),
                Ui.Segmented("Graphs", null, a, nameof(AppearanceSettings.GraphStyle), new[] { (GraphStyle.Area, "Area"), (GraphStyle.Line, "Line"), (GraphStyle.Columns, "Columns") }),
                Ui.Slider("Graph line", null, a, nameof(AppearanceSettings.GraphLineWidth), 0.5, 4, 0.1, "{0:0.0} px"),
                Ui.Segmented("Gauges", null, a, nameof(AppearanceSettings.GaugeStyle), new[] { (GaugeStyle.Half, "Half dial"), (GaugeStyle.Arc, "Arc"), (GaugeStyle.Ring, "Ring") }),
                Ui.Segmented("Section headers", null, a, nameof(AppearanceSettings.HeaderStyle), new[] { (HeaderStyle.Tape, "Tape"), (HeaderStyle.Rule, "Underline"), (HeaderStyle.Plain, "Plain") }),
                Ui.Slider("Animation speed", "0 turns animations off", a, nameof(AppearanceSettings.AnimationSpeed), 0, 2, 0.1, "{0:0.0}×")),
            Ui.Card("Warnings & accessibility", null,
                Ui.Toggle("Color values by state", "Off = calm values use the text color; only warm, hot and critical are colored", a, nameof(AppearanceSettings.ColorBySeverity)),
                Ui.Toggle("Warning symbol", "⚠ before values that are hot or critical", a, nameof(AppearanceSettings.ShowWarningGlyph)),
                Ui.Toggle("Warning tags", "Small HOT / LOW / THERMAL labels next to values", a, nameof(AppearanceSettings.ShowWarningTags)),
                Ui.Toggle("High contrast", "Opaque black panel, white text, heavier frame — also follows Windows high-contrast mode (and forces the warning cues on)", a, nameof(AppearanceSettings.HighContrast)),
                Ui.ComboMap("Color vision", null, a, nameof(AppearanceSettings.ColorVision), new[] { (ColorVisionMode.Standard, "Standard"), (ColorVisionMode.ColorBlindSafe, "Color-blind safe (Okabe–Ito)") })));
    }

    /// <summary>Live HUD sample over a stand-in "game scene", rebuilt whenever an appearance setting changes.</summary>
    private UIElement BuildPreview()
    {
        var panel = new Border();
        var dock = new DockPanel();
        var host = new ContentControl { Focusable = false };
        dock.Children.Add(host);
        panel.Child = dock;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        panel.VerticalAlignment = VerticalAlignment.Top;

        HudLayout sample;
        using (Observable.Quiet())
        {
            sample = new HudLayout { Name = "preview" };
            void Add(ComponentType t, string m, int col, int row, int span = 2, string sec = "", string label = "", bool ratio = false, double h = 0, string text = "")
                => sample.Components.Add(new HudComponent { Type = t, MetricId = m, Col = col, Row = row, ColSpan = span, SecondaryMetricId = sec, Label = label, Ratio = ratio, Height = h, Text = text });
            Add(ComponentType.Text, "", 0, 0, text: "System", sec: "time.now");
            Add(ComponentType.Number, "cpu.usage", 0, 1, sec: "cpu.temp", label: "CPU");
            Add(ComponentType.Number, "gpu.usage", 0, 2, sec: "gpu.temp", label: "GPU");
            Add(ComponentType.ProgressBar, "ram.used", 0, 3, sec: "ram.total", label: "RAM", ratio: true);
            Add(ComponentType.Graph, "cpu.usage", 0, 4, label: "CPU load", h: 26);
            Add(ComponentType.Gauge, "cpu.usage", 0, 5, 1, label: "CPU", h: 52);
            Add(ComponentType.Gauge, "ram.pct", 1, 5, 1, label: "RAM", h: 52);
        }

        var elements = new List<HudElement>();
        HudRenderContext? ctx = null;
        void Rebuild()
        {
            var st = HudStyle.From(S, false, false);
            elements.Clear();
            var grid = HudElementFactory.BuildGrid(sample.Components, st, false, elements);
            grid.LayoutTransform = new ScaleTransform(Math.Clamp(S.Appearance.Scale, 0.6, 1.4), Math.Clamp(S.Appearance.Scale, 0.6, 1.4));
            host.Content = grid;
            HudChrome.Apply(panel, dock, host, st, S.Appearance.Shadow);
            panel.Opacity = S.Appearance.Opacity;
            ctx = new HudRenderContext { Store = _app.Store, Settings = S, Style = st, Fps = _app.Fps, Now = MetricSeries.Now };
            Refresh();
        }
        void Refresh()
        {
            if (ctx == null) return;
            ctx.Now = MetricSeries.Now;
            foreach (var e in elements) { try { e.Refresh(ctx); } catch { } }
        }

        bool pending = false;
        PropertyChangedEventHandler onChange = (_, _) =>
        {
            if (pending) return;
            pending = true;
            Dispatcher.BeginInvoke(() => { pending = false; Rebuild(); }, System.Windows.Threading.DispatcherPriority.Background);
        };
        S.Appearance.PropertyChanged += onChange;
        var appearance = S.Appearance;
        OnLeave(() => appearance.PropertyChanged -= onChange);
        Live(Refresh);
        Rebuild();

        // Stand-in for a game frame so transparency, halo and frame are judged in context.
        var scene = new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0x2E, 0x3B, 0x33), 0), new GradientStop(Color.FromRgb(0x6B, 0x5D, 0x45), 0.45),
                new GradientStop(Color.FromRgb(0xC9, 0xB8, 0x92), 0.7), new GradientStop(Color.FromRgb(0x3A, 0x34, 0x2E), 1),
            }, 60),
            Padding = new Thickness(18),
            MinHeight = 420,
            Child = panel,
        };
        var caption = Ui.Caption("Live preview");
        caption.Margin = new Thickness(0, 0, 0, 8);
        var note = Ui.Muted("Over a stand-in game frame. Changes apply to the real HUD instantly too.", 11.5);
        note.Margin = new Thickness(0, 8, 0, 0);
        var col = Ui.Stack(caption, scene, note);
        ((FrameworkElement)col).Width = 300;
        return col;
    }

    // ── App theme ───────────────────────────────────────

    private UIElement PageTheme()
    {
        var a = S.Appearance;
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var p in UiTheme.All)
        {
            var pal = p;
            Brush B(string hex) => ColorUtil.Brush(hex, Colors.Gray);
            // Miniature of this window in that palette: sidebar with an index, a title, a rule, a toggle.
            var mini = new Grid { Height = 92 };
            mini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            mini.ColumnDefinitions.Add(new ColumnDefinition());
            var side = new StackPanel { Background = B(p.Sidebar) };
            side.Children.Add(new Border { Width = 6, Height = 6, Background = B(p.Accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(7, 9, 0, 8) });
            for (int i = 0; i < 4; i++) side.Children.Add(new Border { Height = 3, Width = i == 1 ? 26 : 20, Background = B(i == 1 ? p.Text : p.Muted), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(7, 0, 0, 6), Opacity = i == 1 ? 1 : 0.6 });
            mini.Children.Add(side);
            var body = new StackPanel { Background = B(p.Bg) };
            body.Children.Add(new TextBlock { Text = "Aa", FontFamily = new FontFamily("Bahnschrift"), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = B(p.Text), Margin = new Thickness(10, 6, 0, 2) });
            body.Children.Add(new Border { Height = 1.5, Background = B(p.Text), Margin = new Thickness(10, 2, 10, 6) });
            var row = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
            var tog = new Border { Width = 22, Height = 11, Background = B(p.Text), Padding = new Thickness(1.5), Child = new Border { Width = 8, Background = B(p.Accent), HorizontalAlignment = HorizontalAlignment.Right } };
            DockPanel.SetDock(tog, Dock.Right);
            row.Children.Add(tog);
            row.Children.Add(new Border { Height = 3, Width = 50, Background = B(p.Muted), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(row);
            Grid.SetColumn(body, 1);
            mini.Children.Add(body);

            var title = new TextBlock { Text = p.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), FontFamily = (FontFamily)Application.Current.Resources["UiDisplayFont"] };
            var desc = Ui.Muted(p.Description, 11.5);
            bool on = p.Name == UiTheme.Current.Name;
            var b = new Button
            {
                Style = Ui.StyleRes("ChipButton"), Padding = new Thickness(8), Margin = new Thickness(0, 0, 12, 12),
                BorderThickness = new Thickness(on ? 2 : 1), Content = Ui.Stack(new Border { BorderBrush = B(p.Border), BorderThickness = new Thickness(1), Child = mini }, title, desc),
            };
            b.SetResourceReference(Control.BorderBrushProperty, on ? "TextBrush" : "BorderBrush");
            b.Click += (_, _) => a.AppTheme = pal.Name;
            grid.Children.Add(b);
        }
        return Ui.Stack(
            Ui.Card("Palette", "Applies to the settings, editor and history windows and the tray menu. The overlay's colors live under HUD look.", grid),
            Ui.Card("Window", null,
                Ui.Muted("Ctrl + / Ctrl − zoom this window, Ctrl 0 resets. Tab and the arrow keys move between controls.")));
    }
}
