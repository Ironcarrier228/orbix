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

/// <summary>Creates, shows and disposes the settings window (it is created lazily and thrown away when closed).</summary>
internal sealed class SettingsController : IDisposable
{
    private readonly ConfigService _config;

    public SettingsController(ConfigService config, ThemeService theme, IconService icons, LaunchService launcher, string dataDirectory)
    {
        _config = config;
    }

    public event Action? ConfigChanged;

    public void Show(SettingsPage page, RadialItem? item)
    {
        Logger.Info("Settings requested: " + page);
        ConfigChanged?.Invoke();
    }

    public void HotkeyStateChanged(HotkeyService hotkeys)
    {
    }

    public void Dispose()
    {
    }
}
