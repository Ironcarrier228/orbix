namespace Orbix.Core.Models;

/// <summary>Allowed ranges of numeric settings (shared by the model validation and by the settings UI sliders).</summary>
public static class Limits
{
    public const double OrbSizeMin = 32, OrbSizeMax = 160, OrbSizeDefault = 56;
    public const double OpacityMin = 0.1, OpacityMax = 1.0;
    public const double RadiusMin = 90, RadiusMax = 360, RadiusDefault = 128;
    public const double ItemSizeMin = 36, ItemSizeMax = 96, ItemSizeDefault = 52;
    public const int HoverDelayMin = 0, HoverDelayMax = 1500, HoverDelayDefault = 280;
    public const double AnimationSpeedMin = 0.5, AnimationSpeedMax = 2.0;
    public const double BackdropOpacityMin = 0.2, BackdropOpacityMax = 0.95;
    public const int MaxOrbitsMin = 2, MaxOrbitsMax = 5;
    public const int DwellMin = 100, DwellMax = 2000;
    public const double OffsetMax = 8000;

    public static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value));
    }

    public static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
}

/// <summary>Application-wide options.</summary>
public sealed class GeneralSettings : ObservableObject
{
    private string _language = "auto";
    private bool _welcomeShown;

    /// <summary>"auto" (follow Windows), "ru" or "en".</summary>
    public string Language
    {
        get => _language;
        set => Set(ref _language, string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant());
    }

    /// <summary>The first-run balloon has been shown.</summary>
    public bool WelcomeShown
    {
        get => _welcomeShown;
        set => Set(ref _welcomeShown, value);
    }
}

/// <summary>Theme and accent colour.</summary>
public sealed class AppearanceSettings : ObservableObject
{
    public const string DefaultAccent = "#6C63FF";

    private ThemeMode _theme = ThemeMode.System;
    private string _accent = DefaultAccent;
    private bool _useSystemAccent;

    public ThemeMode Theme
    {
        get => _theme;
        set => Set(ref _theme, value);
    }

    /// <summary>Accent colour as #RRGGBB.</summary>
    public string Accent
    {
        get => _accent;
        set => Set(ref _accent, string.IsNullOrWhiteSpace(value) ? DefaultAccent : value.Trim());
    }

    /// <summary>Take the accent colour from Windows instead of <see cref="Accent"/>.</summary>
    public bool UseSystemAccent
    {
        get => _useSystemAccent;
        set => Set(ref _useSystemAccent, value);
    }
}

/// <summary>Look and behaviour of the always-on-top orb.</summary>
public sealed class OrbSettings : ObservableObject
{
    private double _size = Limits.OrbSizeDefault;
    private double _restingOpacity = 0.4;
    private double _hoverOpacity = 1.0;
    private bool _breathing = true;
    private string? _color;
    private string? _imagePath;
    private OrbVisibilityMode _visibility = OrbVisibilityMode.AutoHideFullscreen;
    private bool _hidden;
    private bool _allowMove;
    private double _offsetX;
    private double _offsetY;
    private MonitorMode _monitor = MonitorMode.Primary;

    /// <summary>Diameter of the orb in device independent pixels (~56 by default).</summary>
    public double Size
    {
        get => _size;
        set => Set(ref _size, Limits.Clamp(value, Limits.OrbSizeMin, Limits.OrbSizeMax, Limits.OrbSizeDefault));
    }

    /// <summary>Opacity while the pointer is not over the orb.</summary>
    public double RestingOpacity
    {
        get => _restingOpacity;
        set => Set(ref _restingOpacity, Limits.Clamp(value, Limits.OpacityMin, Limits.OpacityMax, 0.4));
    }

    /// <summary>Opacity while the pointer hovers the orb.</summary>
    public double HoverOpacity
    {
        get => _hoverOpacity;
        set => Set(ref _hoverOpacity, Limits.Clamp(value, Limits.OpacityMin, Limits.OpacityMax, 1.0));
    }

    /// <summary>Soft "breathing" glow (a light, low-frame-rate animation; can be switched off).</summary>
    public bool Breathing
    {
        get => _breathing;
        set => Set(ref _breathing, value);
    }

    /// <summary>Orb colour (#RRGGBB). Null = accent colour of the theme.</summary>
    public string? Color
    {
        get => _color;
        set => Set(ref _color, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Optional custom picture of the orb (clipped to a circle).</summary>
    public string? ImagePath
    {
        get => _imagePath;
        set => Set(ref _imagePath, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public OrbVisibilityMode Visibility
    {
        get => _visibility;
        set => Set(ref _visibility, value);
    }

    /// <summary>The user hid the orb (tray / hotkey). The hotkey still opens the menu.</summary>
    public bool Hidden
    {
        get => _hidden;
        set => Set(ref _hidden, value);
    }

    /// <summary>Allow dragging the orb in edit mode. By default the orb is strictly in the screen centre.</summary>
    public bool AllowMove
    {
        get => _allowMove;
        set => Set(ref _allowMove, value);
    }

    /// <summary>Horizontal shift from the screen centre (DIPs). Zero unless <see cref="AllowMove"/> was used.</summary>
    public double OffsetX
    {
        get => _offsetX;
        set => Set(ref _offsetX, Limits.Clamp(value, -Limits.OffsetMax, Limits.OffsetMax, 0));
    }

    /// <summary>Vertical shift from the screen centre (DIPs).</summary>
    public double OffsetY
    {
        get => _offsetY;
        set => Set(ref _offsetY, Limits.Clamp(value, -Limits.OffsetMax, Limits.OffsetMax, 0));
    }

    /// <summary>Primary monitor (default) or the monitor where the cursor is.</summary>
    public MonitorMode Monitor
    {
        get => _monitor;
        set => Set(ref _monitor, value);
    }
}

/// <summary>Geometry, animation and behaviour of the radial menu.</summary>
public sealed class MenuSettings : ObservableObject
{
    private double _radius = Limits.RadiusDefault;
    private double _itemSize = Limits.ItemSizeDefault;
    private SubmenuOpenMode _submenuOpen = SubmenuOpenMode.Hover;
    private int _hoverDelayMs = Limits.HoverDelayDefault;
    private bool _closeAfterLaunch = true;
    private bool _animations = true;
    private double _animationSpeed = 1.0;
    private bool _blurBackground = true;
    private double _backdropOpacity = 0.62;
    private bool _showTooltips = true;
    private bool _keyboardControl = true;
    private bool _wheelControl = true;
    private bool _showOrbitRings = true;
    private double _startAngle = -90;
    private int _maxVisibleOrbits = 3;
    private bool _confirmDangerous = true;

    /// <summary>Distance from the orb centre to the centres of first-level icons (DIPs).</summary>
    public double Radius
    {
        get => _radius;
        set => Set(ref _radius, Limits.Clamp(value, Limits.RadiusMin, Limits.RadiusMax, Limits.RadiusDefault));
    }

    /// <summary>Diameter of menu icons (DIPs).</summary>
    public double ItemSize
    {
        get => _itemSize;
        set => Set(ref _itemSize, Limits.Clamp(value, Limits.ItemSizeMin, Limits.ItemSizeMax, Limits.ItemSizeDefault));
    }

    public SubmenuOpenMode SubmenuOpen
    {
        get => _submenuOpen;
        set => Set(ref _submenuOpen, value);
    }

    /// <summary>Hover time before a group opens (only for <see cref="SubmenuOpenMode.Hover"/>).</summary>
    public int HoverDelayMs
    {
        get => _hoverDelayMs;
        set => Set(ref _hoverDelayMs, Limits.Clamp(value, Limits.HoverDelayMin, Limits.HoverDelayMax));
    }

    public bool CloseAfterLaunch
    {
        get => _closeAfterLaunch;
        set => Set(ref _closeAfterLaunch, value);
    }

    /// <summary>Fan-out / fade animations. Off = the menu appears instantly.</summary>
    public bool Animations
    {
        get => _animations;
        set => Set(ref _animations, value);
    }

    /// <summary>Speed multiplier of the animations (1 = default, 2 = twice as fast).</summary>
    public double AnimationSpeed
    {
        get => _animationSpeed;
        set => Set(ref _animationSpeed, Limits.Clamp(value, Limits.AnimationSpeedMin, Limits.AnimationSpeedMax, 1.0));
    }

    /// <summary>Frosted-glass (blurred desktop snapshot) disc behind the icons.</summary>
    public bool BlurBackground
    {
        get => _blurBackground;
        set => Set(ref _blurBackground, value);
    }

    /// <summary>Opacity of the tinted glass disc.</summary>
    public double BackdropOpacity
    {
        get => _backdropOpacity;
        set => Set(ref _backdropOpacity, Limits.Clamp(value, Limits.BackdropOpacityMin, Limits.BackdropOpacityMax, 0.62));
    }

    public bool ShowTooltips
    {
        get => _showTooltips;
        set => Set(ref _showTooltips, value);
    }

    /// <summary>Arrows / digits / Enter / type-to-search.</summary>
    public bool KeyboardControl
    {
        get => _keyboardControl;
        set => Set(ref _keyboardControl, value);
    }

    /// <summary>Mouse wheel cycles the highlighted item.</summary>
    public bool WheelControl
    {
        get => _wheelControl;
        set => Set(ref _wheelControl, value);
    }

    /// <summary>Thin guide circles along the orbits.</summary>
    public bool ShowOrbitRings
    {
        get => _showOrbitRings;
        set => Set(ref _showOrbitRings, value);
    }

    /// <summary>Angle of the first item in degrees (-90 = top, clockwise).</summary>
    public double StartAngle
    {
        get => _startAngle;
        set => Set(ref _startAngle, Limits.Clamp(value, -180, 180, -90));
    }

    /// <summary>How many orbits are visible simultaneously; deeper levels replace the inner ones.</summary>
    public int MaxVisibleOrbits
    {
        get => _maxVisibleOrbits;
        set => Set(ref _maxVisibleOrbits, Limits.Clamp(value, Limits.MaxOrbitsMin, Limits.MaxOrbitsMax));
    }

    /// <summary>Shutdown / restart / sign-out need a second click to confirm.</summary>
    public bool ConfirmDangerous
    {
        get => _confirmDangerous;
        set => Set(ref _confirmDangerous, value);
    }
}

/// <summary>Ways to summon the menu.</summary>
public sealed class TriggerSettings : ObservableObject
{
    public const string DefaultHotkey = "Ctrl+Alt+Space";
    public const string DefaultToggleOrbHotkey = "Ctrl+Alt+O";

    private bool _hotkeyEnabled = true;
    private string _hotkey = DefaultHotkey;
    private bool _toggleOrbHotkeyEnabled = true;
    private string _toggleOrbHotkey = DefaultToggleOrbHotkey;
    private MiddleClickMode _middleClick = MiddleClickMode.Off;
    private HotCorner _corner = HotCorner.None;
    private int _cornerDwellMs = 350;
    private bool _suspendInFullscreen = true;

    public bool HotkeyEnabled
    {
        get => _hotkeyEnabled;
        set => Set(ref _hotkeyEnabled, value);
    }

    /// <summary>Global hotkey that opens / closes the menu, e.g. "Ctrl+Alt+Space".</summary>
    public string Hotkey
    {
        get => _hotkey;
        set => Set(ref _hotkey, string.IsNullOrWhiteSpace(value) ? DefaultHotkey : value.Trim());
    }

    public bool ToggleOrbHotkeyEnabled
    {
        get => _toggleOrbHotkeyEnabled;
        set => Set(ref _toggleOrbHotkeyEnabled, value);
    }

    /// <summary>Global hotkey that shows / hides the orb.</summary>
    public string ToggleOrbHotkey
    {
        get => _toggleOrbHotkey;
        set => Set(ref _toggleOrbHotkey, string.IsNullOrWhiteSpace(value) ? DefaultToggleOrbHotkey : value.Trim());
    }

    public MiddleClickMode MiddleClick
    {
        get => _middleClick;
        set => Set(ref _middleClick, value);
    }

    /// <summary>Opens the menu when the cursor rests in a screen corner.</summary>
    public HotCorner Corner
    {
        get => _corner;
        set => Set(ref _corner, value);
    }

    public int CornerDwellMs
    {
        get => _cornerDwellMs;
        set => Set(ref _cornerDwellMs, Limits.Clamp(value, Limits.DwellMin, Limits.DwellMax));
    }

    /// <summary>Do not intercept hotkeys / mouse triggers while a full-screen application is active (games).</summary>
    public bool SuspendInFullscreen
    {
        get => _suspendInFullscreen;
        set => Set(ref _suspendInFullscreen, value);
    }
}
