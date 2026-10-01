using System.Runtime.InteropServices;
using System.Windows.Interop;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>
/// Notification-area icon (Shell_NotifyIcon, callback version 4) with a native popup menu.
/// A native menu is used on purpose: it opens instantly, costs no extra WPF window and follows the system look.
/// The icon is restored automatically when Explorer restarts (TaskbarCreated).
/// </summary>
internal sealed class TrayService : IDisposable
{
    private const uint CallbackMessage = Win32.WM_APP + 1;
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private readonly HwndSourceHook _hook;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tip = "Orbix";
    private bool _added;
    private long _lastLeftTick;
    private long _lastRightTick;

    public TrayService(MessageWindow window, IntPtr icon)
    {
        _window = window;
        _icon = icon;
        _taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
        _hook = WndProc;
        window.AddHook(_hook);
    }

    /// <summary>Primary action (left click).</summary>
    public event Action? LeftClick;

    /// <summary>Builds the entries right before the menu is shown.</summary>
    public Func<IReadOnlyList<TrayEntry>>? BuildMenu { get; set; }

    public event Action<int>? CommandSelected;

    public void Show()
    {
        if (_added)
        {
            return;
        }

        var data = NewData();
        data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = Truncate(_tip, 120);
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
        if (_added)
        {
            data.uTimeoutOrVersion = Win32.NOTIFYICON_VERSION_4;
            Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
        }
        else
        {
            Logger.Warn("Shell_NotifyIcon(NIM_ADD) failed.");
        }
    }

    public void SetTip(string tip)
    {
        _tip = tip;
        if (!_added)
        {
            return;
        }

        var data = NewData();
        data.uFlags = Win32.NIF_TIP;
        data.szTip = Truncate(tip, 120);
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_added)
        {
            return;
        }

        var data = NewData();
        data.uFlags = Win32.NIF_INFO;
        data.szInfoTitle = Truncate(title, 60);
        data.szInfo = Truncate(text, 250);
        data.dwInfoFlags = Win32.NIIF_INFO | Win32.NIIF_NOSOUND;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            _added = false;
        }

        _window.RemoveHook(_hook);
        if (_icon != IntPtr.Zero)
        {
            Win32.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = IconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            handled = true;
            uint ev = (uint)(lParam.ToInt64() & 0xFFFF);
            long now = Environment.TickCount64;
            switch (ev)
            {
                case Win32.WM_LBUTTONUP:
                case Win32.NIN_SELECT:
                case Win32.NIN_KEYSELECT:
                    if (now - _lastLeftTick > 300)
                    {
                        _lastLeftTick = now;
                        LeftClick?.Invoke();
                    }

                    break;
                case Win32.WM_RBUTTONUP:
                case Win32.WM_CONTEXTMENU:
                    if (now - _lastRightTick > 300)
                    {
                        _lastRightTick = now;
                        ShowContextMenu();
                    }

                    break;
            }
        }
        else if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
        {
            // Explorer was restarted: the icon has to be added again.
            _added = false;
            Show();
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        IReadOnlyList<TrayEntry>? entries;
        try
        {
            entries = BuildMenu?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error("Building the tray menu failed", ex);
            return;
        }

        if (entries == null || entries.Count == 0)
        {
            return;
        }

        Win32.GetCursorPos(out var pt);
        int command = NativeMenu.Show(_window.Handle, entries, pt.X, pt.Y);
        if (command > 0)
        {
            CommandSelected?.Invoke(command);
        }
    }
}
