using System.Windows.Interop;
using Orbix.Core.Hotkeys;
using Orbix.Core.Models;
using Orbix.Native;

namespace Orbix.Services;

internal enum HotkeyAction
{
    OpenMenu = 1,
    ToggleOrb = 2,
}

internal enum HotkeyState
{
    /// <summary>Switched off in the settings.</summary>
    Disabled,

    /// <summary>Registered with RegisterHotKey.</summary>
    Registered,

    /// <summary>Another application already owns this combination.</summary>
    Conflict,

    /// <summary>The text cannot be parsed or has no modifier.</summary>
    Invalid,

    /// <summary>Temporarily released (a full-screen application is active).</summary>
    Suspended,
}

/// <summary>
/// Global hotkeys through the Win32 <c>RegisterHotKey</c> API. WM_HOTKEY arrives in the hidden message window.
/// The combinations are released while a full-screen application is active, so that games receive their keys.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly MessageWindow _window;
    private readonly HwndSourceHook _hook;
    private readonly HashSet<int> _registered = new();
    private TriggerSettings? _settings;
    private bool _suspended;

    public HotkeyService(MessageWindow window)
    {
        _window = window;
        _hook = WndProc;
        window.AddHook(_hook);
    }

    public event Action<HotkeyAction>? Pressed;

    public event EventHandler? StateChanged;

    public HotkeyState OpenMenuState { get; private set; } = HotkeyState.Disabled;

    public HotkeyState ToggleOrbState { get; private set; } = HotkeyState.Disabled;

    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value)
            {
                return;
            }

            _suspended = value;
            Reapply();
        }
    }

    /// <summary>(Re-)registers the hotkeys according to the settings.</summary>
    public void Apply(TriggerSettings settings)
    {
        _settings = settings;
        Reapply();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.RemoveHook(_hook);
    }

    private void Reapply()
    {
        UnregisterAll();
        if (_settings == null)
        {
            return;
        }

        OpenMenuState = Register(HotkeyAction.OpenMenu, _settings.HotkeyEnabled, _settings.Hotkey);
        ToggleOrbState = Register(HotkeyAction.ToggleOrb, _settings.ToggleOrbHotkeyEnabled, _settings.ToggleOrbHotkey);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private HotkeyState Register(HotkeyAction action, bool enabled, string text)
    {
        if (!enabled)
        {
            return HotkeyState.Disabled;
        }

        if (!HotkeyGesture.TryParse(text, out var gesture) || !gesture.IsValid)
        {
            Logger.Warn($"Hotkey '{text}' is invalid.");
            return HotkeyState.Invalid;
        }

        if (_suspended)
        {
            return HotkeyState.Suspended;
        }

        if (Win32.RegisterHotKey(_window.Handle, (int)action, (uint)gesture.Modifiers | Win32.MOD_NOREPEAT, (uint)gesture.VirtualKey))
        {
            _registered.Add((int)action);
            return HotkeyState.Registered;
        }

        Logger.Warn($"RegisterHotKey({gesture}) failed, Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()} (taken by another application?).");
        return HotkeyState.Conflict;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered)
        {
            Win32.UnregisterHotKey(_window.Handle, id);
        }

        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY)
        {
            handled = true;
            int id = wParam.ToInt32();
            if (Enum.IsDefined(typeof(HotkeyAction), id))
            {
                Pressed?.Invoke((HotkeyAction)id);
            }
        }

        return IntPtr.Zero;
    }
}
