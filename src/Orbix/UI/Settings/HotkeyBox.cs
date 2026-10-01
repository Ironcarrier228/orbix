using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Orbix.Core.Hotkeys;
using Orbix.Core.Localization;

namespace Orbix.UI.Settings;

/// <summary>
/// A read-only textbox that records a key combination instead of text (the global-hotkey editor).
/// Backspace / Delete clear it. The value is a <see cref="HotkeyGesture"/> (empty = "none").
/// </summary>
internal sealed class HotkeyBox : System.Windows.Controls.TextBox
{
    private HotkeyGesture _gesture = HotkeyGesture.None;

    public event Action<HotkeyGesture>? GestureChanged;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Style = (Style)Application.Current.FindResource("StTextBox");
        MinWidth = 170;
        MaxWidth = 240;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            RefreshText();
        }
    }

    private void RefreshText()
    {
        Text = _gesture.IsEmpty ? string.Empty : _gesture.ToString();
        ToolTip = _gesture.IsEmpty ? Loc.T("Settings.HotkeyPlaceholder") : null;
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        SelectAll();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // modifiers alone are not a gesture yet
        }

        if (key is Key.Back or Key.Delete or Key.Escape)
        {
            Commit(HotkeyGesture.None);
            return;
        }

        var mods = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= KeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= KeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= KeyModifiers.Win;

        Commit(new HotkeyGesture(mods, KeyInterop.VirtualKeyFromKey(key)));
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e) => e.Handled = true;

    private void Commit(HotkeyGesture gesture)
    {
        _gesture = gesture;
        RefreshText();
        GestureChanged?.Invoke(gesture);
    }
}
