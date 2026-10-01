using Orbix.Native;

namespace Orbix.Services;

/// <summary>Integer rectangle in physical pixels.</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>A monitor: handle, bounds in physical pixels and its DPI scale (1.0 = 96 dpi).</summary>
internal sealed record MonitorGeometry(IntPtr Handle, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary)
{
    public int CenterX => Bounds.Left + Bounds.Width / 2;

    public int CenterY => Bounds.Top + Bounds.Height / 2;
}

/// <summary>Monitor helpers (primary monitor, monitor under the cursor, DPI).</summary>
internal static class MonitorService
{
    public static MonitorGeometry GetPrimary() => FromHandle(Win32.MonitorFromPoint(new POINT(0, 0), Win32.MONITOR_DEFAULTTOPRIMARY));

    public static MonitorGeometry GetUnderCursor()
    {
        if (!Win32.GetCursorPos(out var pt))
        {
            return GetPrimary();
        }

        return FromHandle(Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST));
    }

    public static MonitorGeometry GetForWindow(IntPtr hwnd) =>
        FromHandle(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST));

    public static int Count()
    {
        int count = 0;
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            count++;
            return true;
        }, IntPtr.Zero);
        return Math.Max(1, count);
    }

    public static MonitorGeometry FromHandle(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !Win32.GetMonitorInfo(monitor, ref info))
        {
            // Extremely unlikely (no monitor); fall back to a sane default so nothing crashes.
            int w = Math.Max(800, Win32.GetSystemMetrics(0));
            int h = Math.Max(600, Win32.GetSystemMetrics(1));
            var rect = new PixelRect(0, 0, w, h);
            return new MonitorGeometry(monitor, rect, rect, 1.0, true);
        }

        double scale = 1.0;
        try
        {
            if (Win32.GetDpiForMonitor(monitor, 0, out uint dx, out _) == 0 && dx > 0)
            {
                scale = dx / 96.0;
            }
        }
        catch (Exception)
        {
            // shcore is missing on very old systems - keep 1.0
        }

        return new MonitorGeometry(
            monitor,
            new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
            new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
            scale,
            (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0);
    }
}
