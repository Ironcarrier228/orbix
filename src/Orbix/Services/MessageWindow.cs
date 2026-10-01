using System.Windows.Interop;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>
/// Hidden message-only window. It is the single receiver of everything that does not belong to a visible window:
/// WM_HOTKEY, tray icon callbacks, WM_COPYDATA from a second instance, display / theme change notifications.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    /// <summary>Title used by a second instance to find this window (FindWindowEx on HWND_MESSAGE).</summary>
    public const string Title = "Orbix.MessageWindow";

    private readonly HwndSource _source;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters(Title)
        {
            ParentWindow = Win32.HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
    }

    public IntPtr Handle => _source.Handle;

    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);

    public void RemoveHook(HwndSourceHook hook) => _source.RemoveHook(hook);

    public void Dispose() => _source.Dispose();
}
