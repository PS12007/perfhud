using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PerfHud.Hotkeys;
using PerfHud.Settings;

namespace PerfHud.UI.Controls;

/// <summary>Focus it and press a key combination to record it. Esc cancels, Backspace/Delete clears.</summary>
public sealed class HotkeyBox : Border
{
    private readonly HotkeyBinding _binding;
    private readonly HotkeyManager _mgr;
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private bool _recording;

    public HotkeyBox(HotkeyBinding binding, HotkeyManager mgr)
    {
        _binding = binding;
        _mgr = mgr;
        Focusable = true;
        Cursor = Cursors.Hand;
        Child = _text;
        Padding = new Thickness(10, 6, 10, 6);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Background = Ui.Res("InputBrush");
        BorderBrush = Ui.Res("BorderBrush");
        FocusVisualStyle = null;
        Width = 190;
        ToolTip = "Click, then press the new key combination. Esc cancels, Backspace clears.";
        System.Windows.Automation.AutomationProperties.SetName(this, $"Hotkey for {binding.Action}");
        MouseLeftButtonDown += (_, _) => Focus();
        GotKeyboardFocus += (_, _) => Begin();
        LostKeyboardFocus += (_, _) => End();
        PreviewKeyDown += OnKey;
        binding.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Begin()
    {
        _recording = true;
        _mgr.Suspend(true);
        BorderBrush = Ui.Res("AccentBrush");
        _text.Text = "Press keys…";
        _text.Foreground = Ui.Res("AccentBrush");
    }

    private void End()
    {
        _recording = false;
        _mgr.Suspend(false);
        BorderBrush = Ui.Res("BorderBrush");
        Render();
    }

    private void Render()
    {
        if (_recording) return;
        _text.Text = string.IsNullOrEmpty(_binding.Gesture) ? "Not set" : _binding.Gesture;
        _text.Foreground = string.IsNullOrEmpty(_binding.Gesture) ? Ui.Res("MutedBrush") : Ui.Res("TextBrush");
        _text.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { Keyboard.ClearFocus(); MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
        if (key is Key.Back or Key.Delete) { _binding.Gesture = ""; MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
        {
            if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = false; }
            return;
        }
        var g = new HotkeyGesture(Keyboard.Modifiers, key);
        _binding.Gesture = g.ToString();
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }
}
