using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Orbix.Core.Models;
using Orbix.Native;
using Orbix.UI;

namespace Orbix.Services;

/// <summary>
/// Resolves dark / light theme and the accent colour (user choice or Windows) and publishes them:
/// <see cref="Palette"/> for the custom-drawn overlay and brush resources for the XAML of the settings window.
/// </summary>
internal sealed class ThemeService
{
    private AppearanceSettings? _settings;

    public bool IsDark { get; private set; } = true;

    public Color Accent { get; private set; } = (Color)ColorConverter.ConvertFromString(AppearanceSettings.DefaultAccent);

    public Palette Palette { get; private set; } = Palette.Create(true, (Color)ColorConverter.ConvertFromString(AppearanceSettings.DefaultAccent));

    public event EventHandler? Changed;

    public void Apply(AppearanceSettings settings)
    {
        _settings = settings;
        Refresh();
    }

    /// <summary>Re-reads the system theme (called on WM_SETTINGCHANGE "ImmersiveColorSet").</summary>
    public void Refresh()
    {
        if (_settings == null)
        {
            return;
        }

        bool dark = _settings.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDarkTheme(),
        };

        var accent = ParseColor(_settings.Accent, (Color)ColorConverter.ConvertFromString(AppearanceSettings.DefaultAccent));
        if (_settings.UseSystemAccent && TryGetSystemAccent(out var system))
        {
            accent = system;
        }

        if (dark == IsDark && accent == Accent && Application.Current != null && Application.Current.Resources.Contains("Bg"))
        {
            return;
        }

        IsDark = dark;
        Accent = accent;
        Palette = Palette.Create(dark, accent);
        PublishResources();
        ApplyNativeMenuTheme(dark);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static Color ParseColor(string? text, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                return (Color)ColorConverter.ConvertFromString(text);
            }
        }
        catch (FormatException)
        {
            // fall back below
        }

        return fallback;
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static bool TryGetSystemAccent(out Color color)
    {
        color = default;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", false);
            if (key?.GetValue("AccentColor") is int abgr)
            {
                color = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                return true;
            }
        }
        catch (Exception)
        {
            // ignore
        }

        return false;
    }

    /// <summary>Dark popup menus for the tray icon (undocumented uxtheme ordinals 135/136, harmless if missing).</summary>
    private static void ApplyNativeMenuTheme(bool dark)
    {
        try
        {
            Win32.SetPreferredAppMode(dark ? 2 : 3); // ForceDark / ForceLight
            Win32.FlushMenuThemes();
        }
        catch (Exception)
        {
            // Older Windows builds: the menu simply stays light.
        }
    }

    private void PublishResources()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        var p = Palette;
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            app.Resources[key] = brush;
            app.Resources[key + "Color"] = color;
        }

        Set("Bg", p.WindowBg);
        Set("Surface", p.Surface);
        Set("SurfaceHi", p.SurfaceHi);
        Set("Border", p.Line);
        Set("Text", p.TextColor);
        Set("Muted", p.MutedColor);
        Set("Accent", p.Accent);
        Set("AccentText", p.AccentText);
        Set("AccentSoft", Color.FromArgb(0x33, p.Accent.R, p.Accent.G, p.Accent.B));
        Set("AccentHover", p.AccentHover);
        Set("Danger", Color.FromRgb(0xE5, 0x48, 0x4D));
        Set("Ok", Color.FromRgb(0x30, 0xA4, 0x6C));
    }
}
