using Orbix.Native;

namespace Orbix.Services;

/// <summary>An entry of a native popup menu. A negative id is a separator.</summary>
internal sealed record TrayEntry(int Id, string Text, bool Checked = false, bool Enabled = true, IReadOnlyList<TrayEntry>? Children = null)
{
    public static TrayEntry Separator { get; } = new(-1, string.Empty);

    public bool IsSeparator => Id < 0;
}

/// <summary>
/// Native Win32 popup menu (TrackPopupMenuEx). It opens instantly, costs no WPF window and matches the system look;
/// it is used for the tray icon and for the context menus of the edit mode.
/// </summary>
internal static class NativeMenu
{
    /// <summary>Shows the menu at the screen position and returns the chosen id (0 = dismissed).</summary>
    public static int Show(IntPtr owner, IReadOnlyList<TrayEntry> entries, int x, int y)
    {
        IntPtr menu = Win32.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            Populate(menu, entries);

            // Required so that the menu closes when the user clicks elsewhere.
            Win32.SetForegroundWindow(owner);
            int command = Win32.TrackPopupMenuEx(
                menu,
                Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN,
                x,
                y,
                owner,
                IntPtr.Zero);
            Win32.PostMessage(owner, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return command;
        }
        finally
        {
            Win32.DestroyMenu(menu); // also destroys the sub menus
        }
    }

    private static void Populate(IntPtr menu, IReadOnlyList<TrayEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
                continue;
            }

            uint flags = Win32.MF_STRING;
            if (entry.Checked)
            {
                flags |= Win32.MF_CHECKED;
            }

            if (!entry.Enabled)
            {
                flags |= Win32.MF_GRAYED;
            }

            if (entry.Children is { Count: > 0 })
            {
                IntPtr sub = Win32.CreatePopupMenu();
                Populate(sub, entry.Children);
                Win32.AppendMenu(menu, flags | Win32.MF_POPUP, new UIntPtr((ulong)sub.ToInt64()), entry.Text);
            }
            else
            {
                Win32.AppendMenu(menu, flags, new UIntPtr((uint)entry.Id), entry.Text);
            }
        }
    }
}
