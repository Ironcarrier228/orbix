using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>Creates the HICON of the notification-area icon.</summary>
internal static class AppIcon
{
    /// <summary>Takes the icon embedded in the executable; draws the orb if that is impossible (e.g. "dotnet Orbix.dll").</summary>
    public static IntPtr CreateTrayIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && Path.GetFileNameWithoutExtension(exe).Equals("Orbix", StringComparison.OrdinalIgnoreCase))
            {
                var small = new IntPtr[1];
                if (Win32.ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                {
                    return small[0];
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("ExtractIconEx failed: " + ex.Message);
        }

        return Draw(32);
    }

    private static IntPtr Draw(int size)
    {
        try
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var center = new Point(size / 2.0, size / 2.0);
                var sphere = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.36, 0.28),
                    Center = new Point(0.44, 0.40),
                    RadiusX = 0.8,
                    RadiusY = 0.8,
                };
                sphere.GradientStops.Add(new GradientStop(Color.FromRgb(0xC4, 0xBE, 0xFF), 0));
                sphere.GradientStops.Add(new GradientStop(Color.FromRgb(0x6C, 0x63, 0xFF), 0.55));
                sphere.GradientStops.Add(new GradientStop(Color.FromRgb(0x40, 0x34, 0xD6), 1));
                dc.DrawEllipse(sphere, null, center, size * 0.42, size * 0.42);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var straight = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[size * size * 4];
            straight.CopyPixels(pixels, size * 4, 0);

            IntPtr color;
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                color = Win32.CreateBitmap(size, size, 1, 32, pin.AddrOfPinnedObject());
            }
            finally
            {
                pin.Free();
            }

            IntPtr mask = Win32.CreateBitmap(size, size, 1, 1, IntPtr.Zero);
            var info = new ICONINFO { fIcon = 1, hbmColor = color, hbmMask = mask };
            IntPtr icon = Win32.CreateIconIndirect(ref info);
            Win32.DeleteObject(color);
            Win32.DeleteObject(mask);
            return icon;
        }
        catch (Exception ex)
        {
            Logger.Warn("Drawing the tray icon failed: " + ex.Message);
            return IntPtr.Zero;
        }
    }
}
