using System.Windows.Threading;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>What the watcher currently sees.</summary>
internal readonly record struct ScreenState(bool FullscreenActive, IntPtr FullscreenMonitor);

/// <summary>
/// Detects full-screen applications and games (to hide the orb / release the hotkeys) and answers
/// "is the desktop visible at this point?" (visibility mode "only over the desktop").
///
/// A window counts as full-screen when it covers its whole monitor and is not an ordinary maximized window
/// (those have a caption), or when the shell reports D3D full-screen / presentation mode.
/// Cost: one foreground WinEvent hook plus a 1 s timer - a few P/Invoke calls per second.
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    private readonly DispatcherTimer _timer;
    private readonly WinEventProc _callback;
    private IntPtr _hook;
    private ScreenState _state;

    public FullscreenWatcher()
    {
        _callback = OnWinEvent;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000) };
        _timer.Tick += (_, _) => Evaluate();
    }

    public event EventHandler? Changed;

    public ScreenState State => _state;

    public void Start()
    {
        if (_hook == IntPtr.Zero)
        {
            _hook = Win32.SetWinEventHook(
                Win32.EVENT_SYSTEM_FOREGROUND,
                Win32.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _callback,
                0,
                0,
                Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        }

        _timer.Start();
        Evaluate();
    }

    public void Stop()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();

    /// <summary>Re-evaluates immediately and raises <see cref="Changed"/> when something changed.</summary>
    public void Evaluate()
    {
        ScreenState next;
        try
        {
            bool full = IsFullscreenWindow(Win32.GetForegroundWindow(), out var monitor);
            next = new ScreenState(full, full ? monitor : IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.Warn("Fullscreen detection failed: " + ex.Message);
            next = default;
        }

        if (next != _state)
        {
            _state = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) => Evaluate();

    /// <summary>True when <paramref name="hwnd"/> is a full-screen window (game, video, presentation).</summary>
    public static bool IsFullscreenWindow(IntPtr hwnd, out IntPtr monitor)
    {
        monitor = IntPtr.Zero;
        if (hwnd == IntPtr.Zero || !Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd))
        {
            return false;
        }

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId || hwnd == Win32.GetShellWindow() || hwnd == Win32.GetDesktopWindow())
        {
            return false;
        }

        var cls = Win32.GetWindowClass(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        if (!Win32.GetWindowRect(hwnd, out var r))
        {
            return false;
        }

        monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !Win32.GetMonitorInfo(monitor, ref mi))
        {
            return false;
        }

        bool covers = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
                      r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        if (covers)
        {
            bool captioned = (Win32.GetStyle(hwnd) & Win32.WS_CAPTION) == Win32.WS_CAPTION;
            // An ordinary maximized window (taskbar auto-hide makes it cover the whole monitor) is not full-screen.
            return !(Win32.IsZoomed(hwnd) && captioned);
        }

        // Exclusive D3D full-screen and presentation mode: ask the shell.
        if (Win32.SHQueryUserNotificationState(out int state) == 0 &&
            (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when nothing but the desktop is visible at the given screen point (no application window covers it).
    /// Windows of this process, cloaked / minimized windows and click-through overlays are ignored.
    /// </summary>
    public static bool IsPointOverDesktop(int x, int y)
    {
        bool result = true;
        bool decided = false;
        uint self = (uint)Environment.ProcessId;

        Win32.EnumWindows((hwnd, _) =>
        {
            if (decided)
            {
                return false;
            }

            if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd))
            {
                return true;
            }

            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self)
            {
                return true;
            }

            long ex = Win32.GetExStyle(hwnd);
            if ((ex & Win32.WS_EX_TRANSPARENT) != 0 && (ex & Win32.WS_EX_LAYERED) != 0)
            {
                return true; // click-through overlay
            }

            if (!Win32.GetWindowRect(hwnd, out var r) || x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom)
            {
                return true;
            }

            if (Win32.IsCloaked(hwnd))
            {
                return true;
            }

            var cls = Win32.GetWindowClass(hwnd);
            if (cls is "Progman" or "WorkerW")
            {
                result = true;
                decided = true;
                return false;
            }

            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland")
            {
                return true; // taskbar / tray flyouts do not count as "an application"
            }

            result = false;
            decided = true;
            return false;
        }, IntPtr.Zero);

        return result;
    }
}
