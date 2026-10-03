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
    private readonly TextBlock _pageNo = Ui.Caption("");
    /// <summary>Non-scrolling column to the right of the page (e.g. the live HUD preview).</summary>
    private readonly ContentControl _aside = new() { Focusable = false, Visibility = Visibility.Collapsed };
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
        Width = 1200; Height = 760; MinWidth = 820; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/PerfHud;component/Assets/app.ico"));

        _pages = new()
        {
            new("Home", "Overview", "Live status of sensors, FPS capture and PerfHud itself", "activity", PageHome),
            new("Appearance", "HUD look", "Shape, type, colors, bars and graphs — previewed live", "palette", PageAppearance),
            new("HUD", "HUD layout", "Which layout, where it sits and how it behaves", "layers", PageHud),
            new("General", "General", "Startup, refresh rate and units", "settings", PageGeneral),
            new("Theme", "App theme", "How this window looks — the HUD has its own colors", "palette", PageTheme),
            new("Hotkeys", "Hotkeys", "Work everywhere, including over games", "keyboard", PageHotkeys),
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
        Closed += (_, _) => { _live.Stop(); _pageCleanup?.Invoke(); _app.Settings.SaveNow(); };
        _app.Settings.Changed += OnSettingsReplaced;
        Closed += (_, _) => _app.Settings.Changed -= OnSettingsReplaced;
        UiTheme.Changed += OnThemeChanged;
        Closed += (_, _) => UiTheme.Changed -= OnThemeChanged;

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
        UiTheme.StyleTitleBar(this);
    }

    private void OnThemeChanged()
    {
        UiTheme.StyleTitleBar(this);
        // Shell chrome follows DynamicResources; page content is rebuilt so code-made brushes refresh too.
        if (_nav.SelectedItem is ListBoxItem { Tag: PageDef p }) Show(p);
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
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sidebar: wordmark, numbered index, two actions. No icons — the numbers do the wayfinding.
        var side = new DockPanel();
        side.SetResourceReference(BackgroundProperty, "SidebarBrush");
        var sideFrame = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
        sideFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var brand = new StackPanel { Margin = new Thickness(22, 24, 16, 22) };
        var mark = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 9, Height = 9, Margin = new Thickness(0, 3, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        mark.Children.Add(dot);
        var word = new TextBlock { Text = "PerfHud", FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)Application.Current.Resources["UiDisplayFont"] };
        word.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        mark.Children.Add(word);
        brand.Children.Add(mark);
        var tag = Ui.Caption($"overlay · v{typeof(AppHost).Assembly.GetName().Version?.ToString(2)}");
        tag.FontWeight = FontWeights.Normal;
        tag.FontSize = 10.5;
        tag.Margin = new Thickness(17, 2, 0, 0);
        tag.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        brand.Children.Add(tag);
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);

        var foot = new StackPanel { Margin = new Thickness(18, 10, 18, 18) };
        var editor = Ui.Button("Open HUD editor", () => _app.OpenEditor(), "AccentButton");
        editor.Margin = new Thickness(0, 0, 0, 6);
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        var history = Ui.Button("Session history", () => _app.OpenHistory());
        history.Margin = new Thickness(0);
        history.HorizontalContentAlignment = HorizontalAlignment.Center;
        foot.Children.Add(editor);
        foot.Children.Add(history);
        DockPanel.SetDock(foot, Dock.Bottom);
        side.Children.Add(foot);

        _nav.Margin = new Thickness(10, 0, 10, 0);
        KeyboardNavigation.SetDirectionalNavigation(_nav, KeyboardNavigationMode.Cycle);
        int n = 1;
        foreach (var p in _pages)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = $"{n++:00}", FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], FontSize = 11, FontWeight = FontWeights.Normal, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            var t = new TextBlock { Text = p.Title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var item = new ListBoxItem { Content = g, Tag = p };
            System.Windows.Automation.AutomationProperties.SetName(item, p.Title);
            _nav.Items.Add(item);
        }
        side.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        root.Children.Add(sideFrame);

        // Content
        var main = new DockPanel { Margin = new Thickness(44, 30, 20, 0) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 14, 26) };
        _pageNo.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        head.Children.Add(_pageNo);
        head.Children.Add(_title);
        _subtitle.Margin = new Thickness(0, 2, 0, 0);
        head.Children.Add(_subtitle);
        DockPanel.SetDock(head, Dock.Top);
        main.Children.Add(head);
        DockPanel.SetDock(_aside, Dock.Right);
        _aside.Margin = new Thickness(8, 0, 24, 24);
        main.Children.Add(_aside);
        _content.Margin = new Thickness(0, 0, 18, 30);
        _content.MaxWidth = 780;
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
        _pageCleanup?.Invoke();
        _pageCleanup = null;
        _aside.Content = null;
        _aside.Visibility = Visibility.Collapsed;
        _pageNo.Text = $"{_pages.IndexOf(p) + 1:00} / {_pages.Count:00}";
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

    private Action? _pageCleanup;

    /// <summary>Runs when the user leaves the current page (unsubscribe handlers etc.).</summary>
    private void OnLeave(Action a) => _pageCleanup += a;

    private void Aside(UIElement e)
    {
        _aside.Content = e;
        _aside.Visibility = Visibility.Visible;
    }
}
