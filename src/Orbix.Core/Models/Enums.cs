namespace Orbix.Core.Models;

/// <summary>What a menu element does when it is activated.</summary>
public enum ItemKind
{
    /// <summary>An executable, shortcut (.lnk), folder or any file - opened through the Windows shell.</summary>
    App,

    /// <summary>A folder of the radial menu: opens a nested orbit with <see cref="RadialItem.Children"/>.</summary>
    Group,

    /// <summary>A URL (http, https, mailto, ms-settings: ...), opened with the default handler.</summary>
    Url,

    /// <summary>A command line executed through cmd.exe.</summary>
    Command,

    /// <summary>A built-in system function (lock, sleep, shutdown, screenshot...).</summary>
    System,
}

/// <summary>Built-in system functions available for <see cref="ItemKind.System"/>.</summary>
public enum SystemActionKind
{
    None,
    Lock,
    Sleep,
    Hibernate,
    SignOut,
    Restart,
    Shutdown,
    Screenshot,
    ShowDesktop,
    TaskManager,
    OrbixSettings,
}

/// <summary>When the always-on-top orb is visible.</summary>
public enum OrbVisibilityMode
{
    /// <summary>Always visible (even on top of full-screen apps).</summary>
    Always,

    /// <summary>Only while the desktop is in front (no application window is active).</summary>
    DesktopOnly,

    /// <summary>Visible everywhere except above full-screen applications and games.</summary>
    AutoHideFullscreen,
}

/// <summary>Which monitor hosts the orb and the menu.</summary>
public enum MonitorMode
{
    Primary,
    Cursor,
}

public enum ThemeMode
{
    System,
    Dark,
    Light,
}

public enum SubmenuOpenMode
{
    /// <summary>Hover a group (after a short delay) or click it.</summary>
    Hover,

    /// <summary>Only a click opens a group.</summary>
    Click,
}

public enum HotCorner
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public enum MiddleClickMode
{
    Off,

    /// <summary>Middle click on the empty desktop opens the menu (other apps are not affected).</summary>
    Desktop,

    /// <summary>Middle click anywhere opens the menu (the click is swallowed).</summary>
    Everywhere,
}
