// Win32 helpers of the end-to-end test (compiled by Add-Type in ci/e2e.ps1).
// Everything the test does to the application is done the way a user does it: real (synthesized) keyboard and mouse input.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace E2E
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public INPUTUNION u; }

    public static class Native
    {
        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 2, KEYEVENTF_EXTENDEDKEY = 1;
        const uint MOUSEEVENTF_MOVE = 1, MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4, MOUSEEVENTF_RIGHTDOWN = 8,
                   MOUSEEVENTF_RIGHTUP = 16, MOUSEEVENTF_WHEEL = 0x800;

        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_APPWINDOW = 0x40000,
                          WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;

        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

        public static int ScreenWidth { get { return GetSystemMetrics(0); } }
        public static int ScreenHeight { get { return GetSystemMetrics(1); } }
        public static int SystemDpi { get { try { return (int)GetDpiForSystem(); } catch (Exception) { return 96; } } }

        // ---- windows ----------------------------------------------------------------

        public static IntPtr FindWindow(int pid, string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate (IntPtr hwnd, IntPtr l)
            {
                int p;
                GetWindowThreadProcessId(hwnd, out p);
                if (pid != 0 && p != pid) return true;
                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                if (sb.ToString() == title) { found = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Titles and classes of all visible top-level windows of a process (for diagnostics).</summary>
        public static string[] ListWindows(int pid)
        {
            var list = new List<string>();
            EnumWindows(delegate (IntPtr hwnd, IntPtr l)
            {
                int p;
                GetWindowThreadProcessId(hwnd, out p);
                if (p != pid) return true;
                var t = new StringBuilder(256); GetWindowText(hwnd, t, 256);
                var c = new StringBuilder(256); GetClassName(hwnd, c, 256);
                RECT r; GetWindowRect(hwnd, out r);
                list.Add(string.Format("0x{0:X} class='{1}' title='{2}' visible={3} rect={4},{5} {6}x{7}", hwnd.ToInt64(), c, t, IsWindowVisible(hwnd), r.Left, r.Top, r.Width, r.Height));
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        public static RECT GetRect(IntPtr hwnd) { RECT r; GetWindowRect(hwnd, out r); return r; }

        /// <summary>Forces a window rectangle (the CI helper form sometimes comes up zero-sized).</summary>
        public static void ForceRect(IntPtr hwnd, int x, int y, int w, int h) { MoveWindow(hwnd, x, y, w, h, true); ShowWindow(hwnd, 5 /*SW_SHOW*/); }

        public static void Show(IntPtr hwnd) { ShowWindow(hwnd, 5 /*SW_SHOW*/); }

        public static string TitleOf(IntPtr hwnd)
        {
            var t = new StringBuilder(256); GetWindowText(hwnd, t, 256);
            return t.ToString();
        }

        /// <summary>All top-level window handles of a process.</summary>
        public static IntPtr[] WindowHandles(int pid)
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate (IntPtr hwnd, IntPtr l)
            {
                int p; GetWindowThreadProcessId(hwnd, out p);
                if (p == pid) list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        public static string CursorPos() { POINT p; GetCursorPos(out p); return p.X + "," + p.Y; }
        public static bool IsVisible(IntPtr hwnd) { return IsWindowVisible(hwnd); }
        public static long ExStyle(IntPtr hwnd) { return GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64(); }
        public static IntPtr Foreground() { return GetForegroundWindow(); }

        /// <summary>Top-level window under a screen point (root ancestor), or zero.</summary>
        public static IntPtr RootWindowAt(int x, int y)
        {
            var h = WindowFromPoint(new POINT { X = x, Y = y });
            return h == IntPtr.Zero ? IntPtr.Zero : GetAncestor(h, 2 /* GA_ROOT */);
        }

        public static string DescribeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(none)";
            var c = new StringBuilder(256); GetClassName(hwnd, c, 256);
            var t = new StringBuilder(256); GetWindowText(hwnd, t, 256);
            int pid; GetWindowThreadProcessId(hwnd, out pid);
            return string.Format("class='{0}' title='{1}' pid={2}", c, t, pid);
        }

        public static void Activate(IntPtr hwnd)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);          // a tap of Alt lifts the foreground lock
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
        }

        public static bool SetWallpaper(string path)
        {
            return SystemParametersInfo(0x14 /* SPI_SETDESKWALLPAPER */, 0, path, 3 /* update ini + broadcast */);
        }

        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
        [DllImport("msimg32.dll")] static extern bool AlphaBlend(IntPtr dst, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, BLENDFUNCTION blend);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public int biSize; public int biWidth; public int biHeight; public short biPlanes; public short biBitCount;
            public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }

        /// <summary>
        /// Captures a screen region including layered (per-pixel alpha) windows: first the DWM-composed desktop
        /// (plain SRCCOPY - layered windows are excluded from it), then every visible layered window in the region
        /// is printed with PrintWindow and alpha-blended on top. System.Drawing's CopyFromScreen cannot do this
        /// (CAPTUREBLT only paints part of the frame on this OS build).
        /// Returns an HBITMAP (Image.FromHbitmap, then DeleteObject) or zero.
        /// </summary>
        public static IntPtr CaptureScreen(int x, int y, int w, int h)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
            IntPtr old = SelectObject(mem, bmp);
            bool ok = BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020u); // SRCCOPY
            if (!ok)
            {
                SelectObject(mem, old); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); DeleteObject(bmp);
                return IntPtr.Zero;
            }

            var layered = LayeredWindowsIn(x, y, w, h);
            foreach (var hwnd in layered)
            {
                RECT r; GetWindowRect(hwnd, out r);
                int rw = r.Width, rh = r.Height;
                if (rw <= 0 || rh <= 0 || rw > 4096 || rh > 4096) continue;
                IntPtr wdc = CreateCompatibleDC(screen);
                var bi = new BITMAPINFO
                {
                    biSize = 40, biWidth = rw, biHeight = -rh, biPlanes = 1, biBitCount = 32, biCompression = 0,
                };
                IntPtr bits;
                IntPtr wbmp = CreateDIBSection(wdc, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (wbmp == IntPtr.Zero || bits == IntPtr.Zero) { DeleteDC(wdc); continue; }
                IntPtr wold = SelectObject(wdc, wbmp);
                if (!PrintWindow(hwnd, wdc, 2 /*PW_RENDERFULLCONTENT*/))
                {
                    SelectObject(wdc, wold); DeleteObject(wbmp); DeleteDC(wdc);
                    continue;
                }

                var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /*AC_SRC_ALPHA*/ };
                AlphaBlend(mem, r.Left - x, r.Top - y, rw, rh, wdc, 0, 0, rw, rh, bf);
                SelectObject(wdc, wold); DeleteObject(wbmp); DeleteDC(wdc);
            }

            SelectObject(mem, old);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            return bmp;
        }

        private static List<IntPtr> LayeredWindowsIn(int x, int y, int w, int h)
        {
            var found = new List<IntPtr>();
            EnumWindows(delegate (IntPtr hwnd, IntPtr l)
            {
                if (!IsWindowVisible(hwnd)) return true;
                long ex = GetWindowLongPtr(hwnd, -20 /*GWL_EXSTYLE*/).ToInt64();
                if ((ex & 0x00080000 /*WS_EX_LAYERED*/) == 0) return true;
                RECT r; GetWindowRect(hwnd, out r);
                if (r.Right <= x || r.Left >= x + w || r.Bottom <= y || r.Top >= y + h) return true;
                found.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Minimizes every visible application window (the runner agent keeps a console on the desktop).</summary>
        public static string[] MinimizeAll(int exceptPid)
        {
            var done = new List<string>();
            EnumWindows(delegate (IntPtr hwnd, IntPtr l)
            {
                if (!IsWindowVisible(hwnd)) return true;
                int p;
                GetWindowThreadProcessId(hwnd, out p);
                if (p == exceptPid) return true;
                var c = new StringBuilder(256); GetClassName(hwnd, c, 256);
                string cls = c.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                var t = new StringBuilder(256); GetWindowText(hwnd, t, 256);
                if (t.Length == 0) return true;
                RECT r; GetWindowRect(hwnd, out r);
                if (r.Width < 50 || r.Height < 50) return true;
                if (ShowWindow(hwnd, 6 /* SW_MINIMIZE */)) done.Add(cls + " '" + t + "'");
                return true;
            }, IntPtr.Zero);
            return done.ToArray();
        }

        // ---- keyboard ---------------------------------------------------------------

        static INPUT Key(ushort vk, bool up)
        {
            uint flags = up ? KEYEVENTF_KEYUP : 0;
            if (vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || vk == 0x2E || vk == 0x24 || vk == 0x23) flags |= KEYEVENTF_EXTENDEDKEY;
            var i = new INPUT { type = INPUT_KEYBOARD };
            i.u.ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags };
            return i;
        }

        static void Send(params INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length) throw new InvalidOperationException("SendInput sent " + sent + " of " + inputs.Length + ", error " + Marshal.GetLastWin32Error());
        }

        /// <summary>Presses the keys in order, then releases them in reverse order (a chord such as Ctrl+Alt+Space).</summary>
        public static void Chord(params int[] vks)
        {
            foreach (var vk in vks) { Send(Key((ushort)vk, false)); Thread.Sleep(15); }
            Thread.Sleep(30);
            for (int k = vks.Length - 1; k >= 0; k--) { Send(Key((ushort)vks[k], true)); Thread.Sleep(15); }
        }

        // ---- mouse ------------------------------------------------------------------

        static INPUT Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0)
        {
            var i = new INPUT { type = INPUT_MOUSE };
            i.u.mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, mouseData = data };
            return i;
        }

        /// <summary>Puts the cursor at the point; the 1 px nudge produces a real WM_MOUSEMOVE for the window below.</summary>
        public static void MoveTo(int x, int y)
        {
            SetCursorPos(x - 1, y);
            Thread.Sleep(10);
            Send(Mouse(MOUSEEVENTF_MOVE, 1, 0));
            Thread.Sleep(25);
        }

        public static void Click(int x, int y, bool right = false, int holdMs = 40)
        {
            MoveTo(x, y);
            Thread.Sleep(60);
            Send(Mouse(right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN));
            Thread.Sleep(holdMs);
            Send(Mouse(right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP));
        }

        public static void Wheel(int delta) { Send(Mouse(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta))); }

        // ---- measurements -----------------------------------------------------------

        /// <summary>
        /// Presses a chord (the last key is pressed with the stopwatch running) and returns the milliseconds until the window
        /// is wider than <paramref name="widerThan"/> pixels (the overlay grows from the orb to the menu square), or -1 after
        /// the timeout. The keys are released afterwards.
        /// </summary>
        public static double MeasureHotkey(IntPtr hwnd, int widerThan, int timeoutMs, params int[] vks)
        {
            for (int k = 0; k < vks.Length - 1; k++) { Send(Key((ushort)vks[k], false)); Thread.Sleep(15); }
            var sw = Stopwatch.StartNew();
            Send(Key((ushort)vks[vks.Length - 1], false));
            double result = -1;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                RECT r; GetWindowRect(hwnd, out r);
                if (IsWindowVisible(hwnd) && r.Width > widerThan) { result = sw.Elapsed.TotalMilliseconds; break; }
                Thread.Sleep(1);
            }
            Thread.Sleep(20);
            for (int k = vks.Length - 1; k >= 0; k--) { Send(Key((ushort)vks[k], true)); Thread.Sleep(15); }
            return result;
        }
    }
}
