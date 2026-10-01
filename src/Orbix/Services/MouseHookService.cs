using System.Runtime.InteropServices;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>
/// Low-level mouse hook shared by the optional triggers (middle click, hot corner) and by the open menu
/// ("click outside closes it"). It is installed only while somebody needs it, so an idle Orbix never
/// sits in the global input path.
/// </summary>
internal sealed unsafe class MouseHookService : IDisposable
{
    private readonly HookProc _proc;
    private readonly HashSet<string> _users = new();
    private IntPtr _hook;
    private bool _swallowLeftUp;
    private bool _swallowRightUp;
    private bool _swallowMiddleUp;

    public MouseHookService()
    {
        _proc = Callback;
    }

    private readonly List<Func<HookButton, int, int, bool>> _downHandlers = new();

    /// <summary>
    /// Adds a handler for "a button went down". It returns true to swallow the click
    /// (the matching "up" is swallowed too).
    /// </summary>
    public void AddButtonHandler(Func<HookButton, int, int, bool> handler) => _downHandlers.Add(handler);

    public void RemoveButtonHandler(Func<HookButton, int, int, bool> handler) => _downHandlers.Remove(handler);

    private bool RaiseDown(HookButton button, int x, int y)
    {
        bool swallow = false;
        for (int i = 0; i < _downHandlers.Count; i++)
        {
            swallow |= _downHandlers[i](button, x, y);
        }

        return swallow;
    }

    /// <summary>Pointer moved (only raised when <see cref="WantMoves"/> is true).</summary>
    public Action<int, int>? Moved { get; set; }

    public bool WantMoves { get; set; }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Installs the hook for a named user (idempotent per user).</summary>
    public void Acquire(string user)
    {
        _users.Add(user);
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Logger.Warn("SetWindowsHookEx(WH_MOUSE_LL) failed: " + Marshal.GetLastWin32Error());
        }
    }

    public void Release(string user)
    {
        _users.Remove(user);
        if (_users.Count == 0 && _hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        _users.Clear();
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = (MSLLHOOKSTRUCT*)lParam;
                int x = data->pt.X;
                int y = data->pt.Y;
                switch ((int)wParam)
                {
                    case Win32.WM_MOUSEMOVE:
                        if (WantMoves)
                        {
                            Moved?.Invoke(x, y);
                        }

                        break;
                    case Win32.WM_LBUTTONDOWN:
                        if (RaiseDown(HookButton.Left, x, y))
                        {
                            _swallowLeftUp = true;
                            return (IntPtr)1;
                        }

                        break;
                    case Win32.WM_RBUTTONDOWN:
                        if (RaiseDown(HookButton.Right, x, y))
                        {
                            _swallowRightUp = true;
                            return (IntPtr)1;
                        }

                        break;
                    case Win32.WM_MBUTTONDOWN:
                        if (RaiseDown(HookButton.Middle, x, y))
                        {
                            _swallowMiddleUp = true;
                            return (IntPtr)1;
                        }

                        break;
                    case Win32.WM_LBUTTONUP when _swallowLeftUp:
                        _swallowLeftUp = false;
                        return (IntPtr)1;
                    case Win32.WM_RBUTTONUP when _swallowRightUp:
                        _swallowRightUp = false;
                        return (IntPtr)1;
                    case Win32.WM_MBUTTONUP when _swallowMiddleUp:
                        _swallowMiddleUp = false;
                        return (IntPtr)1;
                }
            }
            catch (Exception)
            {
                // A hook procedure must never throw.
            }
        }

        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

internal enum HookButton
{
    Left,
    Right,
    Middle,
}
