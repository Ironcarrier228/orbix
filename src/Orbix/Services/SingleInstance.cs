using System.Runtime.InteropServices;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>
/// Single-instance guard. The first process owns a named mutex; later processes forward their command
/// line (WM_COPYDATA to the hidden message window of the first one) and exit - "a second start activates the first".
/// </summary>
internal static class SingleInstance
{
    /// <summary>Marker of our WM_COPYDATA messages (ignore foreign ones).</summary>
    public const long CopyDataMarker = 0x0B1B0001;

    public const string CommandOpen = "open";
    public const string CommandSettings = "settings";
    public const string CommandExit = "exit";
    public const string CommandToggleOrb = "toggle-orb";

    public static Mutex? TryAcquire(string dataDirectory)
    {
        // The mutex name depends on the data directory, so the E2E tests (own ORBIX_DATA_DIR) never meet the real instance.
        uint hash = 2166136261;
        foreach (char c in dataDirectory.ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619;
        }

        var mutex = new Mutex(false, @"Local\Orbix.SingleInstance." + hash.ToString("X8"));
        try
        {
            if (mutex.WaitOne(0, false))
            {
                return mutex;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed: we own the mutex now.
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Sends a command to the running instance. Returns false if it could not be reached.</summary>
    public static bool SendCommand(string command, int waitMs = 4000)
    {
        var deadline = Environment.TickCount64 + waitMs;
        while (true)
        {
            var hwnd = Win32.FindWindowEx(Win32.HWND_MESSAGE, IntPtr.Zero, null, MessageWindow.Title);
            if (hwnd != IntPtr.Zero)
            {
                var text = command + "\0";
                var ptr = Marshal.StringToHGlobalUni(text);
                try
                {
                    var data = new COPYDATASTRUCT
                    {
                        dwData = new IntPtr(CopyDataMarker),
                        cbData = text.Length * 2,
                        lpData = ptr,
                    };
                    Win32.SendMessage(hwnd, Win32.WM_COPYDATA, IntPtr.Zero, ref data);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            if (Environment.TickCount64 > deadline)
            {
                return false;
            }

            Thread.Sleep(100);
        }
    }

    /// <summary>Decodes a WM_COPYDATA message; null when it is not ours.</summary>
    public static string? TryRead(IntPtr lParam)
    {
        try
        {
            var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (data.dwData.ToInt64() != CopyDataMarker || data.cbData <= 0 || data.cbData > 4096)
            {
                return null;
            }

            return Marshal.PtrToStringUni(data.lpData, data.cbData / 2)?.TrimEnd('\0');
        }
        catch (Exception)
        {
            return null;
        }
    }
}
