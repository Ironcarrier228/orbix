using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>
/// "Acrylic" for a layered window: a snapshot of the screen area behind the menu, blurred on a down-scaled copy.
/// DWM acrylic cannot be combined with per-pixel transparency, so the frosted glass is produced here.
/// Typical cost for a 600x600 px area: a few milliseconds (the copy is reduced 4x before blurring).
/// </summary>
internal static class BackdropService
{
    /// <summary>
    /// Captures the screen rectangle (physical pixels) and returns a blurred, slightly saturated bitmap
    /// (1/<paramref name="downscale"/> of the size; WPF stretches it smoothly). Null when the capture failed.
    /// </summary>
    public static BitmapSource? CaptureBlurred(int x, int y, int width, int height, int downscale = 4, int radius = 4, int passes = 3, double saturation = 1.3)
    {
        if (width < 8 || height < 8)
        {
            return null;
        }

        IntPtr screen = IntPtr.Zero;
        IntPtr memory = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            screen = Win32.GetDC(IntPtr.Zero);
            memory = Win32.CreateCompatibleDC(screen);
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            dib = Win32.CreateDIBSection(screen, ref info, Win32.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            previous = Win32.SelectObject(memory, dib);

            // SRCCOPY without CAPTUREBLT: layered windows (including our own overlay) are not part of the snapshot.
            if (!Win32.BitBlt(memory, 0, 0, width, height, screen, x, y, Win32.SRCCOPY))
            {
                return null;
            }

            var raw = new byte[width * height * 4];
            Marshal.Copy(bits, raw, 0, raw.Length);
            return Blur(raw, width, height, downscale, radius, passes, saturation);
        }
        catch (Exception ex)
        {
            Logger.Warn("Backdrop capture failed: " + ex.Message);
            return null;
        }
        finally
        {
            if (memory != IntPtr.Zero && previous != IntPtr.Zero)
            {
                Win32.SelectObject(memory, previous);
            }

            if (dib != IntPtr.Zero)
            {
                Win32.DeleteObject(dib);
            }

            if (memory != IntPtr.Zero)
            {
                Win32.DeleteDC(memory);
            }

            if (screen != IntPtr.Zero)
            {
                Win32.ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    /// <summary>Box-downscale, three-pass box blur (approximates a gaussian) and saturation boost. Pure function - unit-testable.</summary>
    internal static BitmapSource Blur(byte[] bgra, int width, int height, int downscale, int radius, int passes, double saturation)
    {
        int f = Math.Max(1, downscale);
        int sw = Math.Max(1, width / f);
        int sh = Math.Max(1, height / f);
        var small = new byte[sw * sh * 4];

        // --- box downscale
        for (int sy = 0; sy < sh; sy++)
        {
            for (int sx = 0; sx < sw; sx++)
            {
                int b = 0, g = 0, r = 0, n = 0;
                int y0 = sy * f, y1 = Math.Min(height, y0 + f);
                int x0 = sx * f, x1 = Math.Min(width, x0 + f);
                for (int yy = y0; yy < y1; yy++)
                {
                    int row = (yy * width + x0) * 4;
                    for (int xx = x0; xx < x1; xx++, row += 4)
                    {
                        b += bgra[row];
                        g += bgra[row + 1];
                        r += bgra[row + 2];
                        n++;
                    }
                }

                int o = (sy * sw + sx) * 4;
                n = Math.Max(1, n);
                small[o] = (byte)(b / n);
                small[o + 1] = (byte)(g / n);
                small[o + 2] = (byte)(r / n);
                small[o + 3] = 255;
            }
        }

        // --- blur
        var temp = new byte[small.Length];
        for (int pass = 0; pass < Math.Max(1, passes); pass++)
        {
            BoxBlur(small, temp, sw, sh, radius, horizontal: true);
            BoxBlur(temp, small, sw, sh, radius, horizontal: false);
        }

        // --- saturation
        if (Math.Abs(saturation - 1.0) > 0.01)
        {
            for (int i = 0; i < small.Length; i += 4)
            {
                double b = small[i], g = small[i + 1], r = small[i + 2];
                double gray = 0.114 * b + 0.587 * g + 0.299 * r;
                small[i] = Clamp(gray + (b - gray) * saturation);
                small[i + 1] = Clamp(gray + (g - gray) * saturation);
                small[i + 2] = Clamp(gray + (r - gray) * saturation);
            }
        }

        var bitmap = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgr32, null, small, sw * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>One box-blur pass with clamp-to-edge and a running sum (O(n)).</summary>
    private static void BoxBlur(byte[] src, byte[] dst, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w;
        int length = horizontal ? w : h;
        int step = horizontal ? 4 : w * 4;
        int lineStep = horizontal ? w * 4 : 4;
        int window = radius * 2 + 1;

        for (int line = 0; line < lines; line++)
        {
            int baseIndex = line * lineStep;
            for (int c = 0; c < 3; c++)
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int idx = Math.Clamp(k, 0, length - 1);
                    sum += src[baseIndex + idx * step + c];
                }

                for (int i = 0; i < length; i++)
                {
                    dst[baseIndex + i * step + c] = (byte)(sum / window);
                    int add = Math.Clamp(i + radius + 1, 0, length - 1);
                    int sub = Math.Clamp(i - radius, 0, length - 1);
                    sum += src[baseIndex + add * step + c] - src[baseIndex + sub * step + c];
                }
            }

            for (int i = 0; i < length; i++)
            {
                dst[baseIndex + i * step + 3] = 255;
            }
        }
    }
}
