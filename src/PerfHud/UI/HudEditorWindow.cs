using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PerfHud.Core;
using PerfHud.Hud;
using PerfHud.Hud.Elements;
using PerfHud.Monitoring;
using PerfHud.Rendering;
using PerfHud.Sensors.Native;
using PerfHud.Settings;
using PerfHud.UI.Controls;

namespace PerfHud.UI;

/// <summary>
/// Visual layout editor. Components live on a snapping grid and show live data.
/// Drag to move, drag the corner to resize, drop new components from the palette.
/// Keyboard: arrows move, Shift+arrows resize, Del delete, Ctrl+D duplicate, Ctrl+Z undo, PgUp/PgDn reorder.
/// </summary>
public sealed class HudEditorWindow : Window
{
    private const double CW = 176, CH = 50, Gap = 6;
    private const string DragFormat = "PerfHud.ComponentType";

    private readonly AppHost _app;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, AllowDrop = true, Focusable = true };
    private readonly ContentControl _props = new();
    private readonly ComboBox _layoutCombo = new() { Width = 200 };
    private readonly TextBox _nameBox = new() { Width = 180 };
    private readonly TextBlock _readOnlyNote = Ui.Muted("", 12);
    private readonly ListBox _metricList = new() { Height = 260 };
    private readonly TextBox _search = new();
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _redrawDebounce = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Stack<List<HudComponent>> _undo = new();
    private readonly List<(Border tile, HudElement? el, HudComponent c)> _tiles = new();
    private HudLayout _layout = null!;
    private bool _editable;
    private HudComponent? _selected;
    private HudRenderContext? _ctx;

    private AppSettings S => _app.Settings.Current;

    public HudEditorWindow(AppHost app)
    {
        _app = app;
        Title = "PerfHud — HUD Editor";
        Width = 1320; Height = 840; MinWidth = 980; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        UseLayoutRounding = true;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/PerfHud;component/Assets/app.ico"));
        Content = BuildShell();

        _canvas.Drop += OnDrop;
        _canvas.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        _canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == _canvas || e.OriginalSource is Rectangle) Select(null); _canvas.Focus(); };
        PreviewKeyDown += OnKey;
        _live.Tick += (_, _) => RefreshTiles();
        _redrawDebounce.Tick += (_, _) => { _redrawDebounce.Stop(); Redraw(); };
        _live.Start();
        Closed += (_, _) => { _live.Stop(); _app.Settings.SaveNow(); };
        Action themed = () => { UiTheme.StyleTitleBar(this); Content = BuildShellFresh(); };
        UiTheme.Changed += themed;
        Closed += (_, _) => UiTheme.Changed -= themed;

        LoadLayout(S.Hud.ActivePreset);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UiTheme.StyleTitleBar(this);
    }

    // ── Shell ────────────────────────────────────────────

    private UIElement BuildShell()
    {
        var root = new DockPanel();

        // Top bar
        var top = new WrapPanel { Margin = new Thickness(16, 14, 16, 10) };
        top.Children.Add(new TextBlock { Text = "Layout", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6), Foreground = Ui.Res("MutedBrush") });
        _layoutCombo.SelectionChanged += (_, _) => { if (_layoutCombo.SelectedItem is string n && n != _layout?.Name) LoadLayout(n); };
        _layoutCombo.Margin = new Thickness(0, 0, 10, 6);
        top.Children.Add(_layoutCombo);
        _nameBox.Margin = new Thickness(0, 0, 10, 6);
        _nameBox.LostFocus += (_, _) => Rename(_nameBox.Text);
        _nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(_nameBox.Text); };
        _nameBox.ToolTip = "Rename layout";
        top.Children.Add(_nameBox);
        foreach (var b in new[]
        {
            Ui.Button("New", NewLayout, null, "plus"),
            Ui.Button("Duplicate", Duplicate, null, "copy"),
            Ui.Button("Delete", DeleteLayout, "DangerButton", "trash"),
            Ui.Button("Undo", Undo, null, "history"),
            Ui.Button("Compact rows", () => { Mutate(); CompactRows(); }, null, "layers"),
            Ui.Button("Use on HUD", () => { S.Hud.ActivePreset = _layout.Name; S.Hud.Enabled = true; _app.Hud.ShowInfo($"Layout: {_layout.Name}"); }, "AccentButton", "eye"),
        })
        {
            b.Margin = new Thickness(0, 0, 8, 6);
            top.Children.Add(b);
        }
        _readOnlyNote.VerticalAlignment = VerticalAlignment.Center;
        _readOnlyNote.Margin = new Thickness(6, 0, 0, 6);
        top.Children.Add(_readOnlyNote);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // Status bar
        var help = Ui.Muted("Drag tiles to move · drag the corner handle to resize · drop components from the left · Arrows move · Shift+Arrows resize · Del delete · Ctrl+D duplicate · Ctrl+Z undo · PgUp/PgDn reorder", 11.5);
        help.Margin = new Thickness(16, 6, 16, 10);
        DockPanel.SetDock(help, Dock.Bottom);
        root.Children.Add(help);

        // Palette
        var left = new StackPanel { Margin = new Thickness(16, 0, 8, 0) };
        left.Children.Add(Ui.H2("Components"));
        left.Children.Add(Ui.Muted("Click to add, or drag onto the grid", 11.5));
        var pal = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 12) };
        foreach (var t in Enum.GetValues<ComponentType>())
        {
            var btn = new Button { Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 7, 8, 7), HorizontalContentAlignment = HorizontalAlignment.Left };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Icons.Create(TypeIcon(t), Ui.Res("MutedBrush"), 14));
            sp.Children.Add(new TextBlock { Text = TypeName(t), Margin = new Thickness(7, 0, 0, 0), FontSize = 12 });
            btn.Content = sp;
            btn.ToolTip = TypeHelp(t);
            var type = t;
            btn.Click += (_, _) => Add(type, 0, _layout.Rows);
            Point? down = null;
            btn.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(btn);
            btn.PreviewMouseMove += (_, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed || !_editable) return;
                var p = e.GetPosition(btn);
                if (Math.Abs(p.X - down.Value.X) + Math.Abs(p.Y - down.Value.Y) < 6) return;
                down = null;
                DragDrop.DoDragDrop(btn, new DataObject(DragFormat, type.ToString()), DragDropEffects.Copy);
            };
            pal.Children.Add(btn);
        }
        left.Children.Add(pal);
        left.Children.Add(Ui.H2("Metric for new components"));
        _search.Margin = new Thickness(0, 6, 0, 6);
        _search.TextChanged += (_, _) => FillMetricList();
        left.Children.Add(_search);
        _metricList.BorderThickness = new Thickness(1);
        _metricList.BorderBrush = Ui.Res("BorderBrush");
        FillMetricList();
        left.Children.Add(_metricList);
        var leftScroll = new ScrollViewer { Content = left, Width = 290, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(leftScroll, Dock.Left);
        root.Children.Add(leftScroll);

        // Properties
        var right = new ScrollViewer { Width = 340, Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 0, 16, 0) };
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);

        // Canvas
        var canvasHost = new Border
        {
            Background = Ui.Res("CanvasBrush"),
            BorderBrush = Ui.Res("BorderStrongBrush"), BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14) },
        };
        root.Children.Add(canvasHost);
        return root;
    }

    /// <summary>Rebuilds the window chrome after an app-theme change, reusing the long-lived controls.</summary>
    private UIElement BuildShellFresh()
    {
        foreach (FrameworkElement e in new FrameworkElement[] { _canvas, _props, _layoutCombo, _nameBox, _readOnlyNote, _metricList, _search })
        {
            switch (e.Parent)
            {
                case Panel p: p.Children.Remove(e); break;
                case ContentControl cc: cc.Content = null; break;
                case Decorator d: d.Child = null; break;
            }
        }
        var shell = BuildShell();
        Redraw();
        ShowProperties();
        return shell;
    }

    private void FillMetricList()
    {
        var q = _search.Text.Trim();
        var prev = (_metricList.SelectedItem as ListBoxItem)?.Tag as string;
        _metricList.Items.Clear();
        foreach (var d in MetricRegistry.All.Where(d => q.Length == 0 || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Group.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            var item = new ListBoxItem { Tag = d.Id, Padding = new Thickness(8, 4, 8, 4) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = d.Group, Foreground = Ui.Res("MutedBrush"), FontSize = 11, Width = 62 });
            sp.Children.Add(new TextBlock { Text = d.Name, FontSize = 12 });
            item.Content = sp;
            _metricList.Items.Add(item);
            if (d.Id == prev) _metricList.SelectedItem = item;
        }
    }

    private static string Graphable(string id) => MetricRegistry.Get(id) is { IsText: false } ? id : "cpu.usage";

    private string SelectedMetric(string fallback) => (_metricList.SelectedItem as ListBoxItem)?.Tag as string ?? fallback;

    // ── Layout management ────────────────────────────────

    public void LoadLayout(string name)
    {
        var editable = HudPresets.FindEditable(name, S);
        _editable = editable != null;
        _layout = editable ?? HudPresets.Resolve(name, S);
        _undo.Clear();
        _selected = null;
        RefreshLayoutCombo();
        _nameBox.Text = _layout.Name;
        _nameBox.IsEnabled = _editable;
        _readOnlyNote.Text = _editable ? "" : "Built-in layout (read-only) — Duplicate it to customize.";
        Redraw();
        ShowProperties();
    }

    private void RefreshLayoutCombo()
    {
        var names = HudPresets.AllNames(S);
        _layoutCombo.ItemsSource = names;
        _layoutCombo.SelectedItem = _layout?.Name;
    }

    private void NewLayout()
    {
        var l = new HudLayout { Name = UniqueName("Layout") };
        l.Components.Add(new HudComponent { Type = ComponentType.BigNumber, MetricId = "fps.current", SecondaryMetricId = "fps.app" });
        S.Layouts.Add(l);
        LoadLayout(l.Name);
    }

    private void Duplicate()
    {
        var copy = _layout.Clone(UniqueName(_layout.Name + " copy"));
        S.Layouts.Add(copy);
        LoadLayout(copy.Name);
    }

    private void DeleteLayout()
    {
        if (!_editable) return;
        if (MessageBox.Show(this, $"Delete layout '{_layout.Name}'?", "PerfHud", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var name = _layout.Name;
        S.Layouts.Remove(_layout);
        if (S.Hud.ActivePreset == name) S.Hud.ActivePreset = HudPresets.GamingName;
        foreach (var p in S.Profiles.Items.Where(p => p.Preset == name)) p.Preset = "";
        if (S.Layouts.Count == 0) S.Layouts.Add(HudPresets.CreateDefaultCustom());
        LoadLayout(HudPresets.GamingName);
    }

    private void Rename(string newName)
    {
        newName = newName.Trim();
        if (!_editable || newName.Length == 0 || newName == _layout.Name) return;
        if (HudPresets.AllNames(S).Contains(newName) || newName == HudPresets.HiddenToken)
        {
            _nameBox.Text = _layout.Name;
            MessageBox.Show(this, "A layout with that name already exists.", "PerfHud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var old = _layout.Name;
        _layout.Name = newName;
        if (S.Hud.ActivePreset == old) S.Hud.ActivePreset = newName;
        foreach (var p in S.Profiles.Items.Where(p => p.Preset == old)) p.Preset = newName;
        if (S.Profiles.DesktopAction == old) S.Profiles.DesktopAction = newName;
        RefreshLayoutCombo();
    }

    private string UniqueName(string baseName)
    {
        var names = HudPresets.AllNames(S);
        if (!names.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) if (!names.Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }

    // ── Mutations ────────────────────────────────────────

    /// <summary>Snapshot for undo; call before changing the layout.</summary>
    private void Mutate()
    {
        _undo.Push(_layout.Components.Select(c => { var x = c.Clone(); x.Id = c.Id; return x; }).ToList());
        if (_undo.Count > 100) { var keep = _undo.Take(100).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
    }

    private void Commit()
    {
        Observable.RaiseGlobal(_layout, nameof(HudLayout.Components));
        Redraw();
        ShowProperties();
    }

    private void Undo()
    {
        if (!_editable || _undo.Count == 0) return;
        var snap = _undo.Pop();
        var selId = _selected?.Id;
        _layout.Components = new ObservableList<HudComponent>(snap);
        _selected = _layout.Components.FirstOrDefault(c => c.Id == selId);
        Commit();
    }

    private void Add(ComponentType t, int col, int row)
    {
        if (!_editable) { _app.Hud.ShowInfo("Duplicate this built-in layout to edit it"); return; }
        Mutate();
        var c = new HudComponent { Type = t, Col = col, Row = row };
        switch (t)
        {
            case ComponentType.Number: c.MetricId = SelectedMetric("cpu.usage"); break;
            case ComponentType.Percentage: c.MetricId = SelectedMetric("cpu.usage"); break;
            case ComponentType.BigNumber: c.MetricId = SelectedMetric("fps.current"); if (c.MetricId == "fps.current") c.SecondaryMetricId = "fps.app"; break;
            case ComponentType.ProgressBar:
                c.MetricId = SelectedMetric("ram.used");
                if (MetricRegistry.Get(c.MetricId)?.MaxId is string mx) { c.SecondaryMetricId = mx; c.Ratio = true; }
                break;
            case ComponentType.Graph:
                c.MetricId = MetricRegistry.Get(SelectedMetric("cpu.usage"))?.Graphable == true ? SelectedMetric("cpu.usage") : "cpu.usage";
                c.ColSpan = 2; c.Height = 28; c.DetailOnly = true; break;
            case ComponentType.FrameTimeGraph: c.MetricId = "fps.frametime"; c.ColSpan = 2; c.Height = 30; c.DetailOnly = true; break;
            case ComponentType.Gauge: c.MetricId = SelectedMetric("cpu.usage"); c.RowSpan = 2; break;
            case ComponentType.Text: c.Text = "SECTION"; c.ColSpan = 2; c.Icon = "activity"; break;
            case ComponentType.Icon: c.Icon = MetricRegistry.Get(SelectedMetric(""))?.Icon ?? "cpu"; break;
            case ComponentType.Divider: c.ColSpan = 2; break;
            case ComponentType.Spacer: c.Height = 8; break;
            case ComponentType.CoreGrid: c.MetricId = "cpu.cores"; c.ColSpan = 2; c.Height = 26; c.DetailOnly = true; break;
            case ComponentType.SensorList: c.MetricId = "temps.all"; c.ColSpan = 2; c.RowSpan = 4; break;
            case ComponentType.DriveList: c.MetricId = "disk.drives"; c.ColSpan = 2; c.RowSpan = 3; break;
            case ComponentType.Trend: c.MetricId = Graphable(SelectedMetric("cpu.usage")); c.ColSpan = 2; break;
            case ComponentType.Stats: c.MetricId = Graphable(SelectedMetric("fps.current")); c.ColSpan = 2; break;
            case ComponentType.Template: c.Text = "CPU {cpu.usage} {cpu.temp}   GPU {gpu.usage} {gpu.temp}"; c.ColSpan = 2; break;
        }
        _layout.Components.Add(c);
        Resolve(c);
        _selected = c;
        Commit();
    }

    private void DuplicateSelected()
    {
        if (!_editable || _selected == null) return;
        Mutate();
        var c = _selected.Clone();
        c.Row = _selected.RowEnd;
        _layout.Components.Insert(_layout.Components.IndexOf(_selected) + 1, c);
        Resolve(c);
        _selected = c;
        Commit();
    }

    private void DeleteSelected()
    {
        if (!_editable || _selected == null) return;
        Mutate();
        _layout.Components.Remove(_selected);
        _selected = null;
        Commit();
    }

    private void Reorder(int delta)
    {
        if (!_editable || _selected == null) return;
        int i = _layout.Components.IndexOf(_selected), j = Math.Clamp(i + delta, 0, _layout.Components.Count - 1);
        if (i == j) return;
        Mutate();
        _layout.Components.Move(i, j);
        Commit();
    }

    private static bool Overlaps(HudComponent a, HudComponent b) =>
        a.Col < b.ColEnd && b.Col < a.ColEnd && a.Row < b.RowEnd && b.Row < a.RowEnd;

    /// <summary>Pushes anything overlapping <paramref name="anchor"/> downward (cascading), like a dashboard grid.</summary>
    private void Resolve(HudComponent anchor)
    {
        var queue = new Queue<HudComponent>();
        queue.Enqueue(anchor);
        int guard = 0;
        while (queue.Count > 0 && guard++ < 2000)
        {
            var a = queue.Dequeue();
            foreach (var b in _layout.Components.ToList())
            {
                if (ReferenceEquals(a, b) || !Overlaps(a, b)) continue;
                b.Row = a.RowEnd;
                queue.Enqueue(b);
            }
        }
    }

    private void CompactRows()
    {
        if (!_editable) return;
        int rows = _layout.Rows;
        for (int r = rows - 1; r >= 0; r--)
        {
            bool used = _layout.Components.Any(c => c.Row <= r && r < c.RowEnd);
            if (used) continue;
            foreach (var c in _layout.Components.Where(c => c.Row > r)) c.Row--;
        }
        Commit();
    }

    // ── Canvas ───────────────────────────────────────────

    private void RequestRedraw() { _redrawDebounce.Stop(); _redrawDebounce.Start(); }

    private void Redraw()
    {
        _canvas.Children.Clear();
        _tiles.Clear();
        int cols = Math.Max(_layout.Columns + 1, 4), rows = _layout.Rows + 3;
        _canvas.Width = cols * CW;
        _canvas.Height = rows * CH;

        var ink = ((SolidColorBrush)Ui.Res("TextBrush")).Color;
        var cellBrush = new SolidColorBrush(Color.FromArgb(10, ink.R, ink.G, ink.B));
        var cellStroke = new SolidColorBrush(Color.FromArgb(40, ink.R, ink.G, ink.B));
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var rect = new Rectangle { Width = CW - Gap, Height = CH - Gap, RadiusX = 1, RadiusY = 1, Fill = cellBrush, Stroke = cellStroke, StrokeDashArray = new DoubleCollection { 3, 3 } };
                Canvas.SetLeft(rect, c * CW); Canvas.SetTop(rect, r * CH);
                _canvas.Children.Add(rect);
            }

        var style = HudStyle.From(S, false, false);
        _ctx = new HudRenderContext { Store = _app.Store, Settings = S, Style = style, Fps = _app.Fps, Now = MetricSeries.Now };
        foreach (var comp in _layout.Components) AddTile(comp, style);
        RefreshTiles();
    }

    private void AddTile(HudComponent comp, HudStyle style)
    {
        HudElement? el = null;
        try { el = HudElementFactory.Create(comp, style); el.IsHitTestVisible = false; }
        catch (Exception ex) { Log.Once("editor-el", LogLevel.Warn, ex.Message); }

        bool sel = ReferenceEquals(comp, _selected);
        var grid = new Grid { ClipToBounds = true };
        if (el != null)
            grid.Children.Add(new Viewbox { Child = el, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        var badge = new TextBlock { Text = TypeName(comp.Type), FontSize = 9.5, Foreground = Ui.Res("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 6, 0), Opacity = 0.8 };
        grid.Children.Add(badge);

        var tile = new Border
        {
            Width = comp.ColSpan * CW - Gap,
            Height = comp.RowSpan * CH - Gap,
            CornerRadius = new CornerRadius(Math.Min(4, style.CornerRadius)),
            Background = ColorUtil.Brush(Color.FromRgb(style.Background.Color.R, style.Background.Color.G, style.Background.Color.B), 0.97),
            BorderBrush = sel ? Ui.Res("AccentBrush") : ColorUtil.Brush(style.Text.Color, 0.2),
            BorderThickness = new Thickness(sel ? 2 : 1),
            Child = grid,
            Cursor = _editable ? Cursors.SizeAll : Cursors.Arrow,
            ToolTip = $"{TypeName(comp.Type)} · {MetricRegistry.Get(comp.MetricId)?.Name ?? comp.Text}",
        };
        Canvas.SetLeft(tile, comp.Col * CW);
        Canvas.SetTop(tile, comp.Row * CH);
        Panel.SetZIndex(tile, sel ? 100 : 10);
        _canvas.Children.Add(tile);
        _tiles.Add((tile, el, comp));
        HookDrag(tile, comp);

        if (sel && _editable)
        {
            var handle = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, Template = HandleTemplate() };
            Canvas.SetLeft(handle, comp.Col * CW + tile.Width - 10);
            Canvas.SetTop(handle, comp.Row * CH + tile.Height - 10);
            Panel.SetZIndex(handle, 200);
            double accX = 0, accY = 0;
            int c0 = comp.ColSpan, r0 = comp.RowSpan;
            handle.DragStarted += (_, _) => { accX = accY = 0; c0 = comp.ColSpan; r0 = comp.RowSpan; };
            handle.DragDelta += (_, e) =>
            {
                accX += e.HorizontalChange; accY += e.VerticalChange;
                tile.Width = Math.Max(CW - Gap, c0 * CW - Gap + accX);
                tile.Height = Math.Max(CH - Gap, r0 * CH - Gap + accY);
                Canvas.SetLeft(handle, comp.Col * CW + tile.Width - 10);
                Canvas.SetTop(handle, comp.Row * CH + tile.Height - 10);
            };
            handle.DragCompleted += (_, _) =>
            {
                int cs = Math.Max(1, c0 + (int)Math.Round(accX / CW)), rs = Math.Max(1, r0 + (int)Math.Round(accY / CH));
                if (cs == comp.ColSpan && rs == comp.RowSpan) { Redraw(); return; }
                Mutate();
                comp.ColSpan = cs; comp.RowSpan = rs;
                Resolve(comp);
                Commit();
            };
            _canvas.Children.Add(handle);
        }
    }

    private static ControlTemplate HandleTemplate()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var f = new FrameworkElementFactory(typeof(Border));
        f.SetValue(Border.BackgroundProperty, Ui.Res("AccentBrush"));
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        f.SetValue(Border.BorderBrushProperty, Ui.Res("BgBrush"));
        f.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        t.VisualTree = f;
        return t;
    }

    private void HookDrag(Border tile, HudComponent comp)
    {
        Point start = default;
        bool dragging = false, wasSelected = false;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            wasSelected = ReferenceEquals(_selected, comp);
            if (!wasSelected)
            {
                _selected = comp;
                tile.BorderBrush = Ui.Res("AccentBrush");
                tile.BorderThickness = new Thickness(2);
                ShowProperties();
            }
            e.Handled = true;
            _canvas.Focus();
            if (!_editable) { if (!wasSelected) Redraw(); return; }
            start = e.GetPosition(_canvas);
            dragging = true;
            tile.CaptureMouse();
        };
        tile.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(_canvas);
            Canvas.SetLeft(tile, Math.Max(0, comp.Col * CW + (p.X - start.X)));
            Canvas.SetTop(tile, Math.Max(0, comp.Row * CH + (p.Y - start.Y)));
            tile.Opacity = 0.85;
        };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            tile.ReleaseMouseCapture();
            var p = e.GetPosition(_canvas);
            int dc = (int)Math.Round((p.X - start.X) / CW), dr = (int)Math.Round((p.Y - start.Y) / CH);
            if (dc == 0 && dr == 0) { Redraw(); return; } // also shows the resize handle on a fresh selection
            _ = wasSelected;
            Mutate();
            comp.Col = Math.Max(0, comp.Col + dc);
            comp.Row = Math.Max(0, comp.Row + dr);
            Resolve(comp);
            Commit();
        };
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not string s || !Enum.TryParse<ComponentType>(s, out var t)) return;
        var p = e.GetPosition(_canvas);
        Add(t, Math.Max(0, (int)(p.X / CW)), Math.Max(0, (int)(p.Y / CH)));
    }

    private void Select(HudComponent? c)
    {
        _selected = c;
        Redraw();
        ShowProperties();
    }

    private void RefreshTiles()
    {
        if (_ctx == null || !IsVisible) return;
        _ctx.Now = MetricSeries.Now;
        foreach (var (_, el, _) in _tiles)
        {
            try { el?.Refresh(_ctx); } catch { }
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox || e.OriginalSource is ComboBoxItem) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
        if (_selected == null || !_editable) return;
        var c = _selected;
        void Move(int dc, int dr)
        {
            Mutate();
            if (shift) { c.ColSpan = Math.Max(1, c.ColSpan + dc); c.RowSpan = Math.Max(1, c.RowSpan + dr); }
            else { c.Col = Math.Max(0, c.Col + dc); c.Row = Math.Max(0, c.Row + dr); }
            Resolve(c);
            Commit();
        }
        switch (e.Key)
        {
            case Key.Left: Move(-1, 0); break;
            case Key.Right: Move(1, 0); break;
            case Key.Up: Move(0, -1); break;
            case Key.Down: Move(0, 1); break;
            case Key.Delete: DeleteSelected(); break;
            case Key.D when ctrl: DuplicateSelected(); break;
            case Key.PageUp: Reorder(-1); break;
            case Key.PageDown: Reorder(1); break;
            case Key.Escape: Select(null); break;
            default: return;
        }
        e.Handled = true;
    }

    // ── Properties panel ─────────────────────────────────

    private void ShowProperties()
    {
        if (_selected == null)
        {
            var info = new StackPanel();
            info.Children.Add(Ui.H2("Properties"));
            info.Children.Add(Ui.Muted(_editable ? "Select a component to edit it, or add one from the palette." : "This built-in layout is read-only. Use Duplicate to create an editable copy.", 12));
            var stats = Ui.Muted($"{_layout.Components.Count} components · {_layout.Columns} columns × {_layout.Rows} rows", 11.5);
            stats.Margin = new Thickness(0, 10, 0, 0);
            info.Children.Add(stats);
            _props.Content = info;
            return;
        }

        var c = _selected;
        c.PropertyChanged -= OnSelectedChanged;
        c.PropertyChanged += OnSelectedChanged;
        var metricItems = new List<(string, string)> { ("", "(none)") };
        metricItems.AddRange(MetricRegistry.All.Select(d => (d.Id, $"{d.Group} · {d.Name}")));
        var dynamicDrives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => char.ToLowerInvariant(d.Name[0]));
        foreach (var l in dynamicDrives)
            foreach (var sfx in new[] { "free", "used", "pct", "read", "write", "active", "temp" })
            {
                var id = $"disk.{l}.{sfx}";
                metricItems.Add((id, $"Storage · {MetricRegistry.Get(id)!.Name}"));
            }
        var iconItems = new List<(string, string)> { ("", "(automatic)") };
        iconItems.AddRange(Icons.Names.Select(n => (n, n)));

        var panel = new StackPanel { IsEnabled = _editable };
        var head = Ui.H2($"{TypeName(c.Type)}");
        head.FontSize = 15;
        panel.Children.Add(head);
        panel.Children.Add(Ui.Muted(TypeHelp(c.Type), 11.5));

        UIElement R(UIElement e) { if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 8, 0, 0); return e; }
        Grid Small(Grid row) { if (row.Children.Count > 1 && row.Children[1] is FrameworkElement f && f is not CheckBox && f.Width > 150) f.Width = 170; return row; }

        panel.Children.Add(R(Small(Ui.ComboMap("Type", null, c, nameof(HudComponent.Type), Enum.GetValues<ComponentType>().Select(t => (t, TypeName(t))), 170))));
        bool usesMetric = c.Type is not (ComponentType.Text or ComponentType.Template or ComponentType.Divider or ComponentType.Spacer or ComponentType.Icon or ComponentType.CoreGrid or ComponentType.SensorList or ComponentType.DriveList or ComponentType.FrameTimeGraph);
        if (usesMetric)
        {
            panel.Children.Add(R(Small(Ui.ComboMap("Metric", null, c, nameof(HudComponent.MetricId), metricItems, 170))));
            panel.Children.Add(R(Small(Ui.ComboMap("Second value", "Shown next to the main value", c, nameof(HudComponent.SecondaryMetricId), metricItems, 170))));
            panel.Children.Add(R(Ui.Toggle("Show as ratio", "e.g. 11.2/32 GB", c, nameof(HudComponent.Ratio))));
        }
        if (c.Type == ComponentType.Template)
        {
            panel.Children.Add(R(Ui.Caption("Template")));
            var tpl = new TextBox { Margin = new Thickness(0, 4, 0, 4), FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], FontSize = 12, TextWrapping = TextWrapping.Wrap };
            var tb = Ui.Bind(c, nameof(HudComponent.Text));
            tb.UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus;
            tpl.SetBinding(TextBox.TextProperty, tb);
            tpl.KeyDown += (_, e) => { if (e.Key == Key.Enter) tpl.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); };
            panel.Children.Add(tpl);
            panel.Children.Add(Ui.Muted("Write any text; {metric.id} becomes its live value, {metric.id:v} the bare number. " +
                "e.g. CPU {cpu.usage} · {cpu.temp}   RAM {ram.used:v}/{ram.total}. Metric ids are listed under \"Metric\" on other components " +
                "(cpu.usage, cpu.temp, cpu.clock, gpu.usage, gpu.temp, gpu.vram.used, ram.used, ram.pct, fps.current, fps.low1, net.down, net.up, bat.pct, time.now …).", 11));
        }
        if (c.Type == ComponentType.Text)
        {
            panel.Children.Add(R(Small(Ui.Text("Text", null, c, nameof(HudComponent.Text), 170))));
            panel.Children.Add(R(Small(Ui.ComboMap("Subtitle metric", null, c, nameof(HudComponent.SecondaryMetricId), metricItems, 170))));
        }
        panel.Children.Add(R(Small(Ui.Text("Label", "Empty = automatic", c, nameof(HudComponent.Label), 170))));
        panel.Children.Add(R(Small(Ui.ComboMap("Icon", null, c, nameof(HudComponent.Icon), iconItems, 170))));

        var colorRow = Ui.Color("Color", c, nameof(HudComponent.Color));
        panel.Children.Add(R(colorRow));
        panel.Children.Add(Ui.Muted("Empty = automatic severity colors", 11));
        panel.Children.Add(R(Ui.Slider("Font size", null, c, nameof(HudComponent.FontScale), 0.6, 3, 0.05, "{0:0.00}×")));
        if (c.Type is ComponentType.Graph or ComponentType.Trend or ComponentType.FrameTimeGraph or ComponentType.Gauge or ComponentType.Spacer or ComponentType.CoreGrid or ComponentType.Icon)
            panel.Children.Add(R(Ui.Slider("Height", null, c, nameof(HudComponent.Height), 0, 160, 2, "{0:0} px")));
        panel.Children.Add(R(Ui.Slider("Min width", "0 = automatic", c, nameof(HudComponent.Width), 0, 400, 4, "{0:0} px")));
        if (c.IsGraph || c.Type is ComponentType.Trend or ComponentType.Stats)
            panel.Children.Add(R(Small(Ui.ComboMap("History window", null, c, nameof(HudComponent.GraphSeconds), new[] { (0, "Default"), (5, "5 s"), (10, "10 s"), (30, "30 s"), (60, "60 s"), (300, "5 min") }, 170))));

        panel.Children.Add(R(Ui.H2("Placement")));
        var place = new UniformGrid { Columns = 4, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (lbl, path) in new[] { ("Col", nameof(HudComponent.Col)), ("Row", nameof(HudComponent.Row)), ("Width", nameof(HudComponent.ColSpan)), ("Height", nameof(HudComponent.RowSpan)) })
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            sp.Children.Add(Ui.Muted(lbl, 11));
            var tb = new TextBox { TextAlignment = TextAlignment.Right };
            var b = Ui.Bind(c, path); b.UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus;
            tb.SetBinding(TextBox.TextProperty, b);
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); };
            sp.Children.Add(tb);
            place.Children.Add(sp);
        }
        panel.Children.Add(place);
        panel.Children.Add(R(Ui.Toggle("Hide in compact mode", null, c, nameof(HudComponent.DetailOnly))));
        panel.Children.Add(R(Ui.Toggle("Show label", null, c, nameof(HudComponent.ShowLabel))));
        panel.Children.Add(R(Ui.Toggle("Show icon", null, c, nameof(HudComponent.ShowIcon))));
        panel.Children.Add(R(Ui.Buttons(
            Ui.Button("Duplicate", DuplicateSelected, null, "copy"),
            Ui.Button("Delete", DeleteSelected, "DangerButton", "trash"),
            Ui.Button("", () => Reorder(-1), null, "arrowup"),
            Ui.Button("", () => Reorder(1), null, "arrowdown"))));
        _props.Content = panel;
    }

    private void OnSelectedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is HudComponent c && e.PropertyName is nameof(HudComponent.Col) or nameof(HudComponent.Row) or nameof(HudComponent.ColSpan) or nameof(HudComponent.RowSpan))
            Resolve(c);
        RequestRedraw();
        if (e.PropertyName == nameof(HudComponent.Type)) Dispatcher.BeginInvoke(ShowProperties);
    }

    // ── Names ────────────────────────────────────────────

    public static string TypeName(ComponentType t) => t switch
    {
        ComponentType.Number => "Number",
        ComponentType.BigNumber => "Big number",
        ComponentType.Percentage => "Percentage",
        ComponentType.ProgressBar => "Progress bar",
        ComponentType.Graph => "Graph",
        ComponentType.FrameTimeGraph => "Frame-time graph",
        ComponentType.Gauge => "Gauge",
        ComponentType.Text => "Text / header",
        ComponentType.Icon => "Icon",
        ComponentType.Divider => "Divider",
        ComponentType.Spacer => "Spacer",
        ComponentType.CoreGrid => "Per-core bars",
        ComponentType.SensorList => "Temperature list",
        ComponentType.DriveList => "Drive list",
        ComponentType.Trend => "Value + trend",
        ComponentType.Stats => "Min / avg / max",
        ComponentType.Template => "Custom text",
        _ => t.ToString(),
    };

    private static string TypeIcon(ComponentType t) => t switch
    {
        ComponentType.Number => "number",
        ComponentType.BigNumber => "fps",
        ComponentType.Percentage => "percent",
        ComponentType.ProgressBar => "bar",
        ComponentType.Graph => "graph",
        ComponentType.FrameTimeGraph => "frametime",
        ComponentType.Gauge => "gauge",
        ComponentType.Text => "text",
        ComponentType.Icon => "eye",
        ComponentType.Divider => "divider",
        ComponentType.Spacer => "spacer",
        ComponentType.CoreGrid => "cores",
        ComponentType.SensorList => "temp",
        ComponentType.DriveList => "disk",
        ComponentType.Trend => "activity",
        ComponentType.Stats => "sliders",
        ComponentType.Template => "text",
        _ => "activity",
    };

    private static string TypeHelp(ComponentType t) => t switch
    {
        ComponentType.Number => "Label + value (+ optional second value), color-coded by severity.",
        ComponentType.BigNumber => "Large hero value — ideal for FPS.",
        ComponentType.Percentage => "Value with a hairline bar underneath.",
        ComponentType.ProgressBar => "Label, value and a rounded fill bar.",
        ComponentType.Graph => "Real-time sparkline of the metric's history.",
        ComponentType.FrameTimeGraph => "Per-frame time of the measured app — spikes = stutter.",
        ComponentType.Gauge => "Arc gauge with the value in the center.",
        ComponentType.Text => "Static text or a section header with optional subtitle metric.",
        ComponentType.Icon => "A single icon.",
        ComponentType.Divider => "Thin horizontal line.",
        ComponentType.Spacer => "Empty space.",
        ComponentType.CoreGrid => "Load bars for every logical processor.",
        ComponentType.SensorList => "Every available temperature sensor.",
        ComponentType.DriveList => "Every drive: space, throughput, temperature.",
        ComponentType.Trend => "Value with a small inline sparkline beside it.",
        ComponentType.Stats => "Lowest, average and highest value over the history window.",
        ComponentType.Template => "Free text with live values: \"CPU {cpu.usage} · {cpu.temp}\".",
        _ => "",
    };
}
