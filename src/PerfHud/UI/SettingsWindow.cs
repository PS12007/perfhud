using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PerfHud.Core;
using PerfHud.Rendering;
using PerfHud.Sensors.Native;
using PerfHud.Settings;
using PerfHud.UI.Controls;

namespace PerfHud.UI;

/// <summary>Settings application: sidebar navigation + live-applied pages (see SettingsPages.cs).</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly AppHost _app;
    private readonly ListBox _nav = new();
    private readonly ContentControl _content = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly TextBlock _title = Ui.H1("");
    private readonly TextBlock _subtitle = Ui.Muted("", 13);
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Action> _liveActions = new();
    private readonly ScaleTransform _uiScale = new(1, 1);
    private bool _welcome;

    private sealed record PageDef(string Key, string Title, string Subtitle, string Icon, Func<UIElement> Build);
    private readonly List<PageDef> _pages;

    private AppSettings S => _app.Settings.Current;

    public SettingsWindow(AppHost app)
    {
        _app = app;
        Title = "PerfHud Settings";
        Width = 1040; Height = 720; MinWidth = 820; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Res("BgBrush");
        Foreground = Ui.Res("TextBrush");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/PerfHud;component/Assets/app.ico"));

        _pages = new()
        {
            new("Home", "Overview", "Live status of sensors, FPS capture and PerfHud itself", "activity", PageHome),
            new("General", "General", "Startup, refresh rate and units", "settings", PageGeneral),
            new("Hotkeys", "Global hotkeys", "Work everywhere, including over games", "keyboard", PageHotkeys),
            new("Appearance", "Appearance", "Theme, colors, transparency, typography", "palette", PageAppearance),
            new("HUD", "HUD", "Layout, position, orientation and behavior", "layers", PageHud),
            new("Metrics", "Metric picker", "Choose exactly which metrics the Custom layout shows", "list", PageMetrics),
            new("Profiles", "App profiles", "Switch HUD layout automatically per game or app", "game", PageProfiles),
            new("Alerts", "Alerts", "Subtle warnings when something needs attention", "bell", PageAlerts),
            new("History", "History", "Record sessions and review performance later", "history", PageHistory),
            new("Performance", "Performance", "Polling, FPS capture and PerfHud's own footprint", "sliders", PagePerformance),
            new("Battery", "Battery mode", "Lower overhead automatically when unplugged", "battery", PageBattery),
            new("Sensors", "Temperatures & sensors", "Thresholds, color coding and sensor sources", "temp", PageSensors),
            new("Diagnostics", "Diagnostics", "Monitor status, logs, system information", "shield", PageDiagnostics),
            new("About", "About & privacy", "Version, licensing and what data stays local", "info", PageAbout),
        };

        Content = BuildShell();
        _nav.SelectionChanged += (_, _) => { if (_nav.SelectedItem is ListBoxItem { Tag: PageDef p }) Show(p); };
        _nav.SelectedIndex = 0;

        _live.Tick += (_, _) => { foreach (var a in _liveActions.ToList()) { try { a(); } catch (Exception ex) { Log.Once("live-ui", LogLevel.Warn, ex.Message); } } };
        _live.Start();
        Closed += (_, _) => { _live.Stop(); _app.Settings.SaveNow(); };
        _app.Settings.Changed += OnSettingsReplaced;
        Closed += (_, _) => _app.Settings.Changed -= OnSettingsReplaced;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key is Key.OemPlus or Key.Add)) SetUiScale(_uiScale.ScaleX + 0.1);
            if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key is Key.OemMinus or Key.Subtract)) SetUiScale(_uiScale.ScaleX - 0.1);
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.D0 or Key.NumPad0) SetUiScale(1);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void SetUiScale(double s)
    {
        s = Math.Clamp(Math.Round(s, 1), 0.8, 1.8);
        _uiScale.ScaleX = _uiScale.ScaleY = s;
    }

    private void OnSettingsReplaced(object sender, string? prop)
    {
        // Import / reset swaps the whole settings object: rebuild the visible page so bindings point at the new one.
        if (sender is AppSettings) Dispatcher.BeginInvoke(() => { if (_nav.SelectedItem is ListBoxItem { Tag: PageDef p }) Show(p); });
    }

    public void Navigate(string key)
    {
        _welcome = key == "Welcome";
        if (_welcome) key = "Home";
        foreach (ListBoxItem item in _nav.Items)
            if (item.Tag is PageDef p && p.Key == key) { _nav.SelectedItem = item; Show(p); }
    }

    private UIElement BuildShell()
    {
        var root = new Grid { LayoutTransform = _uiScale };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sidebar
        var side = new DockPanel { Background = Ui.Res("SidebarBrush") };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 20, 16, 18) };
        var logo = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush(Color.FromRgb(0x16, 0x1D, 0x27), Color.FromRgb(0x0A, 0x0D, 0x12), 45),
            BorderBrush = Ui.Res("AccentDimBrush"), BorderThickness = new Thickness(1),
            Child = Icons.Create("frametime", Ui.Res("AccentBrush"), 18, 2.4),
        };
        brand.Children.Add(logo);
        var bt = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = "PerfHud", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("TextBrush") });
        bt.Children.Add(new TextBlock { Text = "Performance overlay", FontSize = 11, Foreground = Ui.Res("MutedBrush") });
        brand.Children.Add(bt);
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);

        var foot = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };
        foot.Children.Add(Ui.Buttons(
            Ui.Button("HUD Editor", () => _app.OpenEditor(), "AccentButton", "grid"),
            Ui.Button("History", () => _app.OpenHistory(), null, "history")));
        DockPanel.SetDock(foot, Dock.Bottom);
        side.Children.Add(foot);

        _nav.Margin = new Thickness(12, 0, 12, 0);
        KeyboardNavigation.SetDirectionalNavigation(_nav, KeyboardNavigationMode.Cycle);
        foreach (var p in _pages)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Icons.Create(p.Icon, Ui.Res("MutedBrush"), 16, 2));
            sp.Children.Add(new TextBlock { Text = p.Title, Margin = new Thickness(11, 0, 0, 0), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = sp, Tag = p };
            System.Windows.Automation.AutomationProperties.SetName(item, p.Title);
            _nav.Items.Add(item);
        }
        side.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        root.Children.Add(side);

        // Content
        var main = new DockPanel { Margin = new Thickness(34, 26, 20, 0) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 14, 18) };
        head.Children.Add(_title);
        head.Children.Add(_subtitle);
        DockPanel.SetDock(head, Dock.Top);
        main.Children.Add(head);
        _content.Margin = new Thickness(0, 0, 14, 30);
        _content.MaxWidth = 860;
        _content.HorizontalAlignment = HorizontalAlignment.Left;
        _scroll.Content = _content;
        main.Children.Add(_scroll);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        return root;
    }

    private void Show(PageDef p)
    {
        _liveActions.Clear();
        _title.Text = p.Title;
        _subtitle.Text = p.Subtitle;
        try { _content.Content = p.Build(); }
        catch (Exception ex)
        {
            Log.Error($"Settings page {p.Key} failed", ex);
            _content.Content = Ui.Callout($"This page failed to load: {ex.Message}", "danger");
        }
        _scroll.ScrollToTop();
        foreach (var a in _liveActions) { try { a(); } catch { } }
    }

    /// <summary>Registers an action run every second while the page is visible (live values).</summary>
    private void Live(Action a) => _liveActions.Add(a);
}
