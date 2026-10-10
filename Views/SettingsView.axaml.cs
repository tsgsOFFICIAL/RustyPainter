using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RustyPainter.Painting;
using RustyPainter.ViewModels;

namespace RustyPainter.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // Tunnelling, so the key is used up here and does not also press a button or move the focus.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel { CapturingAction: not null } settings)
            return;

        // Holding Ctrl, Alt or Shift on its own is the start of a combination, not a choice yet.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
            return;

        e.Handled = true;
        settings.CaptureKey(ToVirtualKey(e.Key), ToModifiers(e.KeyModifiers));
    }

    private void OnCaptureLostFocus(object? sender, RoutedEventArgs e)
        => (DataContext as SettingsViewModel)?.CancelCapture();

    private static uint ToModifiers(KeyModifiers modifiers)
    {
        uint result = 0;

        if (modifiers.HasFlag(KeyModifiers.Control)) result |= Hotkey.Ctrl;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= Hotkey.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= Hotkey.Shift;

        return result;
    }

    /// <summary>The Windows virtual key code for a key, or 0 for one that has none that can be used.</summary>
    public static uint ToVirtualKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return (uint)('A' + (key - Key.A));

        if (key is >= Key.D0 and <= Key.D9)
            return (uint)('0' + (key - Key.D0));

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return (uint)('0' + (key - Key.NumPad0));

        if (key is >= Key.F1 and <= Key.F24)
            return (uint)(0x70 + (key - Key.F1));

        return key switch
        {
            Key.Space => 0x20,
            Key.Escape => 0x1B,
            Key.Enter => 0x0D,
            Key.Tab => 0x09,
            Key.Back => 0x08,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Up => 0x26,
            Key.Down => 0x28,
            Key.Left => 0x25,
            Key.Right => 0x27,
            Key.Pause => 0x13,
            _ => 0u
        };
    }
}
