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

    public static Border Card(string? title, string? subtitle, params UIElement[] children)
    {
        var stack = new StackPanel();
        if (title != null)
        {
            var t = H2(title);
            t.FontSize = 14;
            stack.Children.Add(t);
            if (subtitle != null)
            {
                var m = Muted(subtitle);
                m.Margin = new Thickness(0, 2, 0, 4);
                stack.Children.Add(m);
            }
        }
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0 || title != null)
                stack.Children.Add(new Border { Height = 1, Background = Res("BorderBrush"), Margin = new Thickness(0, 10, 0, 10), Opacity = i == 0 ? 0 : 0.7 });
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
        left.Children.Add(new TextBlock { Text = label, Foreground = Res("TextBrush"), FontSize = 13 });
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

    public static Button Button(string text, Action click, string? styleKey = null, string? icon = null)
    {
        var b = new Button { Margin = new Thickness(0, 0, 8, 0) };
        if (styleKey != null) b.Style = StyleRes(styleKey);
        if (icon != null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var fg = styleKey == "AccentButton" ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 18, 26)) : Res("TextBrush");
            sp.Children.Add(Icons.Create(icon, fg, 14, 2.2));
            sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
        }
        else b.Content = text;
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
        var sw = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), BorderBrush = Res("BorderStrongBrush"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) };
        sw.SetBinding(Border.BackgroundProperty, new Binding(path) { Source = source, Converter = HexToBrush.Instance });
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(sw);
        sp.Children.Add(t);
        return Row(label, null, sp, 0);
    }

    public static Border Callout(string text, string kind = "info")
    {
        var brush = kind switch { "warn" => Res("WarningBrush"), "ok" => Res("SuccessBrush"), "danger" => Res("DangerBrush"), _ => Res("AccentBrush") };
        var sp = new DockPanel();
        var icon = Icons.Create(kind == "warn" || kind == "danger" ? "warn" : kind == "ok" ? "shield" : "info", brush, 16);
        icon.Margin = new Thickness(0, 1, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        sp.Children.Add(icon);
        sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextBrush"), FontSize = 12.5 });
        var c = ((SolidColorBrush)brush).Color;
        return new Border
        {
            Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(24, c.R, c.G, c.B)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
        };
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

