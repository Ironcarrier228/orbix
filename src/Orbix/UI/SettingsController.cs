using System.Windows;
using Orbix.Core.Models;
using Orbix.Core.Services;
using Orbix.Services;

namespace Orbix.UI;

internal enum SettingsPage
{
    General,
    Orb,
    Menu,
    Items,
    Appearance,
    About,
}

/// <summary>
/// Creates, shows and disposes the settings window. The window is built lazily on first request and
/// thrown away when closed, so an idle Orbix keeps no settings UI in memory.
/// </summary>
internal sealed class SettingsController : IDisposable
{
    private readonly ConfigService _config;
    private readonly ThemeService _theme;
    private readonly IconService _icons;
    private readonly string _dataDirectory;
    private SettingsWindow? _window;
    private HotkeyService? _hotkeys;

    public SettingsController(ConfigService config, ThemeService theme, IconService icons, LaunchService launcher, string dataDirectory)
    {
        _config = config;
        _theme = theme;
        _icons = icons;
        _dataDirectory = dataDirectory;
    }

    /// <summary>Raised after any committed change: the host re-applies geometry, theme and hotkeys.</summary>
    public event Action? ConfigChanged;

    public void Show(SettingsPage page, RadialItem? item)
    {
        if (_window == null)
        {
            _window = new SettingsWindow(_config, _theme, _icons, _dataDirectory, () => ConfigChanged?.Invoke());
            if (_hotkeys != null)
            {
                _window.SetHotkeyService(_hotkeys);
            }

            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.SelectPage(page);
        if (item != null)
        {
            _window.SelectItem(item);
        }
    }

    public void HotkeyStateChanged(HotkeyService hotkeys)
    {
        _hotkeys = hotkeys;
        _window?.SetHotkeyService(hotkeys);
    }

    public void Dispose()
    {
        if (_window != null)
        {
            _window.Closed -= (_, _) => _window = null;
            _window.Close();
            _window = null;
        }
    }
}
