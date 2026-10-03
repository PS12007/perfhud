using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PerfHud.Hud;
using PerfHud.Rendering;

namespace PerfHud.UI.Controls;

/// <summary>
/// Small declarative builder for settings pages. Every control binds two-way to a model property,
/// so changes apply live (and are persisted by the settings service).
/// </summary>
public static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    public static Style StyleRes(string key) => (Style)Application.Current.Resources[key];

    public static TextBlock H1(string text) => new() { Text = text, Style = StyleRes("H1") };
    public static TextBlock H2(string text) => new() { Text = text, Style = StyleRes("H2") };
    public static TextBlock Muted(string text, double size = 12) => new() { Text = text, Style = StyleRes("Muted"), FontSize = size };

    /// <summary>Mono caption, e.g. section titles and small field headings.</summary>
    public static TextBlock Caption(string text) => new() { Text = text.ToUpperInvariant(), Style = StyleRes("Caption") };

    /// <summary>A section: ink rule on top, mono caption, then rows separated by hairlines.</summary>
    public static Border Card(string? title, string? subtitle, params UIElement[] children)
    {
        var stack = new StackPanel();
        if (title != null)
        {
            stack.Children.Add(Caption(title));
            if (subtitle != null)
            {
                var m = Muted(subtitle);
                m.Margin = new Thickness(0, 3, 0, 2);
                m.MaxWidth = 620;
                m.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(m);
            }
        }
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0 || title != null)
                stack.Children.Add(new Border { Height = 1, Background = Res("BorderBrush"), Margin = new Thickness(0, 9, 0, 9), Opacity = i == 0 ? 0 : 1 });
            stack.Children.Add(children[i]);
        }
        return new Border { Style = StyleRes("Card"), Child = stack };
    }

    /// <summary>Label (+ description) on the left, control on the right.</summary>
    public static Grid Row(string label, string? description, UIElement control, double controlWidth = 260)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        var lt = new TextBlock { Text = label, FontSize = 13 };
        lt.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        left.Children.Add(lt);
        if (!string.IsNullOrEmpty(description)) left.Children.Add(Muted(description, 11.5));
        g.Children.Add(left);
        if (control is FrameworkElement fe && controlWidth > 0 && fe is not CheckBox) fe.Width = controlWidth;
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement f2) f2.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(control);
        AutomationName(control, label);
        return g;
    }

    private static void AutomationName(UIElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);

    public static Binding Bind(object source, string path, IValueConverter? conv = null) =>
        new(path) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, Converter = conv };

    public static Grid Toggle(string label, string? description, object source, string path)
    {
        var cb = new CheckBox();
        cb.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Bind(source, path));
        return Row(label, description, cb, 0);
    }

    public static Grid Slider(string label, string? description, object source, string path, double min, double max, double step, string format)
    {
        var s = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, Width = 190 };
        s.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, Bind(source, path));
        var val = new TextBlock { Foreground = Res("MutedBrush"), Width = 58, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], FontSize = 12 };
        val.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = source, StringFormat = format });
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(s);
        sp.Children.Add(val);
        return Row(label, description, sp, 0);
    }

    public static Grid Combo(string label, string? description, object source, string path, System.Collections.IEnumerable items, double width = 260, string? display = null)
    {
        var c = new ComboBox { ItemsSource = items };
        if (display != null) c.DisplayMemberPath = display;
        c.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, Bind(source, path));
        return Row(label, description, c, width);
    }

    /// <summary>ComboBox showing friendly names for values (e.g. enum â†’ text).</summary>
    public static Grid ComboMap<T>(string label, string? description, object source, string path, IEnumerable<(T Value, string Text)> items, double width = 260)
    {
        var list = items.Select(i => new KeyValuePair<T, string>(i.Value, i.Text)).ToList();
        var c = new ComboBox { ItemsSource = list, DisplayMemberPath = "Value", SelectedValuePath = "Key" };
        c.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, Bind(source, path));
        return Row(label, description, c, width);
    }

    public static Grid Text(string label, string? description, object source, string path, double width = 260, IValueConverter? conv = null)
    {
        var t = new TextBox();
        var b = Bind(source, path, conv);
        b.UpdateSourceTrigger = conv == null ? UpdateSourceTrigger.PropertyChanged : UpdateSourceTrigger.LostFocus;
        t.SetBinding(TextBox.TextProperty, b);
        return Row(label, description, t, width);
    }

    public static Grid Number(string label, string? description, object source, string path, double width = 110, string format = "0.##")
    {
        var t = new TextBox { TextAlignment = TextAlignment.Right };
        var b = Bind(source, path);
        b.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
        b.StringFormat = format;
        t.SetBinding(TextBox.TextProperty, b);
        t.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) t.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); };
        return Row(label, description, t, width);
    }

    /// <summary>
    /// Text buttons stay text-only (cleaner); the icon is used only when there's no text (icon buttons like delete / move).
    /// Content is always a StackPanel whose [1] child is the label, so callers can relabel it.
    /// </summary>
    public static Button Button(string text, Action click, string? styleKey = null, string? icon = null)
    {
        var b = new Button { Margin = new Thickness(0, 0, 8, 0) };
        if (styleKey != null) b.Style = StyleRes(styleKey);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        bool iconOnly = string.IsNullOrEmpty(text) && icon != null;
        if (iconOnly)
        {
            var path = Icons.Create(icon, Res("TextBrush"), 14, 2);
            if (path is Viewbox { Child: Canvas cv } && cv.Children.Count > 0 && cv.Children[0] is System.Windows.Shapes.Path p)
                p.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, styleKey == "AccentButton" ? "OnAccentBrush" : styleKey == "DangerButton" ? "DangerBrush" : "TextBrush");
            sp.Children.Add(path);
            b.Padding = new Thickness(7, 5, 7, 5);
        }
        else sp.Children.Add(new Border { Width = 0 });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible });
        b.Content = sp;
        b.Click += (_, _) => click();
        return b;
    }

    public static WrapPanel Buttons(params UIElement[] buttons)
    {
        var w = new WrapPanel();
        foreach (var b in buttons) { if (b is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 6); w.Children.Add(b); }
        return w;
    }

    public static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    /// <summary>Hex color field with live swatch.</summary>
    public static Grid Color(string label, object source, string path)
    {
        var t = new TextBox { Width = 110, FontFamily = (FontFamily)Application.Current.Resources["MonoFont"] };
        t.SetBinding(TextBox.TextProperty, Bind(source, path));
        var sw = new Border { Width = 26, Height = 26, BorderBrush = Res("TextBrush"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) };
        sw.SetBinding(Border.BackgroundProperty, new Binding(path) { Source = source, Converter = HexToBrush.Instance });
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(sw);
        sp.Children.Add(t);
        return Row(label, null, sp, 0);
    }

    /// <summary>A note set off by a colored bar in the margin — no box, no icon.</summary>
    public static Border Callout(string text, string kind = "info")
    {
        var brush = kind switch { "warn" => Res("WarningBrush"), "ok" => Res("SuccessBrush"), "danger" => Res("DangerBrush"), _ => Res("AccentBrush") };
        return new Border
        {
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextBrush"), FontSize = 12.5, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left },
            Padding = new Thickness(12, 2, 0, 2), Margin = new Thickness(0, 0, 0, 22),
            BorderBrush = brush, BorderThickness = new Thickness(3, 0, 0, 0),
        };
    }

    /// <summary>Row of selectable chips (one per option) bound to a property — a flatter alternative to a combo box for 2–5 choices.</summary>
    public static Grid Segmented<T>(string label, string? description, object source, string path, IEnumerable<(T Value, string Text)> items)
    {
        var prop = source.GetType().GetProperty(path)!;
        var wrap = new WrapPanel();
        var buttons = new List<(Button b, T v)>();
        void Sync()
        {
            var cur = prop.GetValue(source);
            foreach (var (b, v) in buttons)
            {
                bool on = Equals(cur, v);
                b.SetResourceReference(Control.BackgroundProperty, on ? "TextBrush" : "InputBrush");
                b.SetResourceReference(Control.ForegroundProperty, on ? "BgBrush" : "TextBrush");
                b.SetResourceReference(Control.BorderBrushProperty, on ? "TextBrush" : "BorderStrongBrush");
            }
        }
        foreach (var (value, text) in items)
        {
            var b = new Button { Style = StyleRes("ChipButton"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, -1, 0), Content = new TextBlock { Text = text, FontSize = 12 } };
            var v = value;
            b.Click += (_, _) => { prop.SetValue(source, v); Sync(); };
            buttons.Add((b, v));
            wrap.Children.Add(b);
        }
        if (source is System.ComponentModel.INotifyPropertyChanged npc)
        {
            System.ComponentModel.PropertyChangedEventHandler h = (_, e) => { if (e.PropertyName == path) Sync(); };
            npc.PropertyChanged += h;
            wrap.Unloaded += (_, _) => npc.PropertyChanged -= h;
            wrap.Loaded += (_, _) => { npc.PropertyChanged -= h; npc.PropertyChanged += h; Sync(); };
        }
        Sync();
        return Row(label, description, wrap, 0);
    }
}

public sealed class HexToBrush : IValueConverter
{
    public static readonly HexToBrush Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ColorUtil.IsValidHex(value as string) ? ColorUtil.Brush(value as string, Colors.Transparent) : Brushes.Transparent;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>double? â‡„ text ("" = not overridden).</summary>
public sealed class NullableDoubleConverter : IValueConverter
{
    public static readonly NullableDoubleConverter Instance = new();
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value is double d ? d.ToString("0.##", CultureInfo.InvariantCulture) : "";
    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : Binding.DoNothing;
    }
}

