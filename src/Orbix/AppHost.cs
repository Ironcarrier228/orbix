using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Orbix.Core.Localization;
using Orbix.Core.Models;
using Orbix.Core.Services;
using Orbix.Native;
using Orbix.Services;
using Orbix.UI;

namespace Orbix;

/// <summary>
/// Composition root: creates the services, connects them to each other and to the overlay, reacts to changes of the
/// configuration. Everything here runs on the UI thread.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private const int CmdToggleMenu = 1;
    private const int CmdToggleOrb = 2;
    private const int CmdEdit = 3;
    private const int CmdSettings = 4;
    private const int CmdAutostart = 5;
    private const int CmdExit = 9;
    private const int CmdProfileBase = 100;

    private readonly string[] _args;
    private readonly string _dataDirectory;
    private readonly DispatcherTimer _cornerTimer;
    private readonly DispatcherTimer _applyTimer;
    private readonly HwndSourceHook _messageHook;
    private readonly Func<HookButton, int, int, bool> _mouseHandler;

    private ConfigService _config = null!;
    private ThemeService _theme = null!;
    private MessageWindow _messages = null!;
    private HotkeyService _hotkeys = null!;
    private TrayService _tray = null!;
    private FullscreenWatcher _fullscreen = null!;
    private MouseHookService _mouse = null!;
    private IconService _icons = null!;
    private LaunchService _launcher = null!;
    private OverlayWindow _overlay = null!;
    private SettingsController _settings = null!;
    private bool _cornerInside;
    private bool _cornerFired;
    private PixelRect _primaryBounds;
    private bool _disposed;
    private readonly HashSet<string> _dirtyAreas = new();

    public AppHost(string[] args, string dataDirectory)
    {
        _args = args;
        _dataDirectory = dataDirectory;
        _messageHook = MessageHook;
        _mouseHandler = OnMouseDown;
        _cornerTimer = new DispatcherTimer();
        _cornerTimer.Tick += OnCornerTimer;
        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _applyTimer.Tick += (_, _) => ApplyPendingChanges();
    }

    AppConfig Cfg => _config.Config;

    public void Start()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        string systemLanguage = SystemLanguage();

        // configuration (never throws: corrupted files fall back to a backup or defaults)
        _config = new ConfigService(_dataDirectory, new DispatcherSynchronizationContext(dispatcher));
        _config.Load(systemLanguage);
        _config.SaveFailed += (_, ex) => Logger.Warn("Saving the configuration failed: " + ex.Message);
        if (_config.LoadWarning != null)
        {
            Logger.Warn(_config.LoadWarning);
        }

        Loc.SetLanguage(Loc.Resolve(Cfg.General.Language, systemLanguage));

        _theme = new ThemeService();
        _theme.Apply(Cfg.Appearance);

        _messages = new MessageWindow();
        _messages.AddHook(_messageHook);

        _icons = new IconService(_dataDirectory, dispatcher);
        _launcher = new LaunchService();
        _fullscreen = new FullscreenWatcher();
        _mouse = new MouseHookService();
        _primaryBounds = MonitorService.GetPrimary().Bounds;

        _overlay = new OverlayWindow(Cfg, _config, _theme, _icons, _fullscreen, _mouse);
        _overlay.ItemInvoked += OnItemInvoked;
        _overlay.SettingsRequested += item => OpenSettings(SettingsPage.Items, item);
        _overlay.OrbContextRequested += ShowOrbContextMenu;

        _settings = new SettingsController(_config, _theme, _icons, _launcher, _dataDirectory);
        _settings.ConfigChanged += () => _overlay.RefreshAll();

        _hotkeys = new HotkeyService(_messages);
        _hotkeys.Pressed += OnHotkey;
        _hotkeys.StateChanged += (_, _) => _settings.HotkeyStateChanged(_hotkeys);

        _fullscreen.Changed += OnFullscreenChanged;
        _fullscreen.Start();

        _tray = new TrayService(_messages, AppIcon.CreateTrayIcon());
        _tray.BuildMenu = BuildTrayMenu;
        _tray.CommandSelected += OnTrayCommand;
        _tray.LeftClick += () => _overlay.ToggleMenu();
        _tray.Show();
        UpdateTrayTip();

        _overlay.Start();
        _hotkeys.Apply(Cfg.Triggers);
        UpdateMouseTriggers();

        // warm the icon cache in the background: the first opening already shows real icons
        _icons.Preload(Cfg.ActiveProfile.Items, IconPixels());

        SubscribeToConfig();

        if (!Cfg.General.WelcomeShown)
        {
            Cfg.General.WelcomeShown = true;
            _tray.ShowBalloon(Loc.T("Tray.WelcomeTitle"), Loc.T("Tray.WelcomeText", Cfg.Triggers.Hotkey));
        }

        if (_args.Contains("--settings"))
        {
            OpenSettings(SettingsPage.General, null);
        }
        else if (_args.Contains("--open"))
        {
            _overlay.OpenMenu();
        }

        if (_hotkeys.OpenMenuState == HotkeyState.Conflict)
        {
            _tray.ShowBalloon("Orbix", Loc.T("Tray.Conflict", Cfg.Triggers.Hotkey));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _config?.Flush();
            _settings?.Dispose();
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _fullscreen?.Dispose();
            _mouse?.Dispose();
            _icons?.Dispose();
            _messages?.Dispose();
            _config?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn("Shutdown: " + ex.Message);
        }
    }

    /// <summary>Size in pixels of the icons on the primary monitor (used to warm the cache).</summary>
    private double IconPixels() => Cfg.Menu.ItemSize * 0.62 * MonitorService.GetPrimary().Scale;

    // ---- commands ------------------------------------------------------------------

    public void HandleCommand(string command)
    {
        switch (command)
        {
            case SingleInstance.CommandSettings:
                OpenSettings(SettingsPage.General, null);
                break;
            case SingleInstance.CommandExit:
                Exit();
                break;
            case SingleInstance.CommandToggleOrb:
                Cfg.Orb.Hidden = !Cfg.Orb.Hidden;
                break;
            default:
                // "a second start activates the first one"
                _overlay.OpenMenu();
                break;
        }
    }

    public void Exit()
    {
        Logger.Info("Exit requested.");
        Application.Current.Shutdown();
    }

    private void OpenSettings(SettingsPage page, RadialItem? item)
    {
        if (_overlay.IsMenuOpen)
        {
            _overlay.CloseMenu(false);
        }

        _settings.Show(page, item);
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.OpenMenu:
                _overlay.ToggleMenu();
                break;
            case HotkeyAction.ToggleOrb:
                Cfg.Orb.Hidden = !Cfg.Orb.Hidden;
                break;
        }
    }

    private async void OnItemInvoked(RadialItem item)
    {
        try
        {
            if (item.Kind == ItemKind.System && item.SystemAction == SystemActionKind.OrbixSettings)
            {
                OpenSettings(SettingsPage.General, null);
                return;
            }

            bool stayOpen = !Cfg.Menu.CloseAfterLaunch && !LaunchService.IsDangerous(item);
            if (!stayOpen)
            {
                _overlay.CloseMenu(false);
            }

            var result = await _launcher.LaunchAsync(item);
            if (!result.Success && !result.Cancelled)
            {
                string message = Loc.T(result.ErrorKey ?? "Launch.Failed", result.Detail ?? item.Name);
                if (_overlay.IsMenuOpen)
                {
                    _overlay.ShowToast(message, true);
                }
                else
                {
                    _tray.ShowBalloon("Orbix", message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Launching an item failed", ex);
        }
    }

    // ---- tray ----------------------------------------------------------------------

    private IReadOnlyList<TrayEntry> BuildTrayMenu()
    {
        var entries = new List<TrayEntry>
        {
            new(CmdToggleMenu, Loc.T(_overlay.IsMenuOpen ? "Tray.Close" : "Tray.Open")),
            new(CmdToggleOrb, Loc.T(Cfg.Orb.Hidden ? "Tray.ShowOrb" : "Tray.HideOrb")),
            new(CmdEdit, Loc.T("Tray.Edit")),
        };

        if (Cfg.Profiles.Count > 1)
        {
            var profiles = new List<TrayEntry>();
            for (int i = 0; i < Cfg.Profiles.Count; i++)
            {
                profiles.Add(new TrayEntry(CmdProfileBase + i, Cfg.Profiles[i].Name, Cfg.Profiles[i].Id == Cfg.ActiveProfile.Id));
            }

            entries.Add(new TrayEntry(0, Loc.T("Tray.Profile"), false, true, profiles));
        }

        entries.Add(TrayEntry.Separator);
        entries.Add(new TrayEntry(CmdSettings, Loc.T("Tray.Settings")));
        entries.Add(TrayEntry.Separator);
        entries.Add(new TrayEntry(CmdAutostart, Loc.T("Tray.Autostart"), AutostartService.IsEnabled()));
        entries.Add(TrayEntry.Separator);
        entries.Add(new TrayEntry(CmdExit, Loc.T("Tray.Exit")));
        return entries;
    }

    private void ShowOrbContextMenu()
    {
        Win32.GetCursorPos(out var pt);
        int command = NativeMenu.Show(_messages.Handle, BuildTrayMenu(), pt.X, pt.Y);
        if (command > 0)
        {
            OnTrayCommand(command);
        }
    }

    private void OnTrayCommand(int command)
    {
        switch (command)
        {
            case CmdToggleMenu:
                _overlay.ToggleMenu();
                break;
            case CmdToggleOrb:
                Cfg.Orb.Hidden = !Cfg.Orb.Hidden;
                break;
            case CmdEdit:
                _overlay.OpenMenu(true);
                break;
            case CmdSettings:
                OpenSettings(SettingsPage.General, null);
                break;
            case CmdAutostart:
                AutostartService.SetEnabled(!AutostartService.IsEnabled());
                break;
            case CmdExit:
                Exit();
                break;
            default:
                int index = command - CmdProfileBase;
                if (index >= 0 && index < Cfg.Profiles.Count)
                {
                    Cfg.ActiveProfileId = Cfg.Profiles[index].Id;
                    _icons.Preload(Cfg.ActiveProfile.Items, IconPixels());
                }

                break;
        }
    }

    private void UpdateTrayTip()
    {
        string tip = Cfg.Triggers.HotkeyEnabled ? $"Orbix - {Cfg.Triggers.Hotkey}" : "Orbix";
        _tray.SetTip(tip);
    }

    // ---- configuration changes -------------------------------------------------------

    private void SubscribeToConfig()
    {
        Cfg.General.PropertyChanged += (_, e) => Dirty("general");
        Cfg.Appearance.PropertyChanged += (_, e) => Dirty("appearance");
        Cfg.Orb.PropertyChanged += (_, e) => Dirty("orb");
        Cfg.Menu.PropertyChanged += (_, e) => Dirty("menu");
        Cfg.Triggers.PropertyChanged += (_, e) => Dirty("triggers");
        Cfg.PropertyChanged += (_, e) =>
        {
            // a new configuration was imported or the profile switched
            if (e.PropertyName is nameof(AppConfig.ActiveProfileId) or nameof(AppConfig.Profiles))
            {
                Dirty("profiles");
            }
        };
        Cfg.Profiles.Changed += (_, _) => Dirty("profiles");
    }

    private void Dirty(string area)
    {
        _dirtyAreas.Add(area);
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    /// <summary>Applies the changes of the last 60 ms in one go (a slider produces many notifications).</summary>
    private void ApplyPendingChanges()
    {
        _applyTimer.Stop();
        var areas = _dirtyAreas.ToList();
        _dirtyAreas.Clear();

        try
        {
            if (areas.Contains("general"))
            {
                Loc.SetLanguage(Loc.Resolve(Cfg.General.Language, SystemLanguage()));
                UpdateTrayTip();
            }

            if (areas.Contains("appearance"))
            {
                _theme.Apply(Cfg.Appearance);
            }

            if (areas.Contains("triggers"))
            {
                _hotkeys.Apply(Cfg.Triggers);
                UpdateMouseTriggers();
                UpdateTrayTip();
                _hotkeys.Suspended = Cfg.Triggers.SuspendInFullscreen && _fullscreen.State.FullscreenActive;
            }

            if (areas.Contains("orb") || areas.Contains("menu") || areas.Contains("appearance"))
            {
                _overlay.RefreshAll();
            }

            if (areas.Contains("profiles"))
            {
                _icons.Preload(Cfg.ActiveProfile.Items, IconPixels());
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Applying the configuration failed", ex);
        }
    }

    // ---- screen state --------------------------------------------------------------

    private void OnFullscreenChanged(object? sender, EventArgs e)
    {
        _hotkeys.Suspended = Cfg.Triggers.SuspendInFullscreen && _fullscreen.State.FullscreenActive;
        _overlay.UpdateOrbVisibility();
    }

    // ---- optional mouse triggers -------------------------------------------------------

    private void UpdateMouseTriggers()
    {
        var t = Cfg.Triggers;
        bool middle = t.MiddleClick != MiddleClickMode.Off;
        bool corner = t.Corner != HotCorner.None;

        _mouse.RemoveButtonHandler(_mouseHandler);
        _mouse.Moved = null;
        _mouse.WantMoves = false;
        _cornerTimer.Stop();
        _cornerInside = false;
        _cornerFired = false;

        if (middle || corner)
        {
            _primaryBounds = MonitorService.GetPrimary().Bounds;
            _mouse.AddButtonHandler(_mouseHandler);
            _mouse.WantMoves = corner;
            if (corner)
            {
                _mouse.Moved = OnMouseMoved;
                _cornerTimer.Interval = TimeSpan.FromMilliseconds(t.CornerDwellMs);
            }

            _mouse.Acquire("triggers");
        }
        else
        {
            _mouse.Release("triggers");
        }
    }

    private bool TriggersSuspended => Cfg.Triggers.SuspendInFullscreen && _fullscreen.State.FullscreenActive;

    private bool OnMouseDown(HookButton button, int x, int y)
    {
        var mode = Cfg.Triggers.MiddleClick;
        if (button != HookButton.Middle || mode == MiddleClickMode.Off || TriggersSuspended || _overlay.IsMenuOpen)
        {
            return false;
        }

        if (mode == MiddleClickMode.Desktop && !FullscreenWatcher.IsPointOverDesktop(x, y))
        {
            return false;
        }

        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => _overlay.OpenMenu()));
        return true; // swallow: the click opened the menu
    }

    private void OnMouseMoved(int x, int y)
    {
        var corner = Cfg.Triggers.Corner;
        var b = _primaryBounds;
        const int zone = 3;
        bool inside = corner switch
        {
            HotCorner.TopLeft => x <= b.Left + zone && y <= b.Top + zone && x >= b.Left && y >= b.Top,
            HotCorner.TopRight => x >= b.Right - 1 - zone && y <= b.Top + zone && x < b.Right && y >= b.Top,
            HotCorner.BottomLeft => x <= b.Left + zone && y >= b.Bottom - 1 - zone && x >= b.Left && y < b.Bottom,
            HotCorner.BottomRight => x >= b.Right - 1 - zone && y >= b.Bottom - 1 - zone && x < b.Right && y < b.Bottom,
            _ => false,
        };

        if (inside == _cornerInside)
        {
            return;
        }

        _cornerInside = inside;
        if (inside)
        {
            if (!_cornerFired && !TriggersSuspended && !_overlay.IsMenuOpen)
            {
                _cornerTimer.Stop();
                _cornerTimer.Start();
            }
        }
        else
        {
            _cornerTimer.Stop();
            _cornerFired = false;
        }
    }

    private void OnCornerTimer(object? sender, EventArgs e)
    {
        _cornerTimer.Stop();
        if (_cornerInside && !TriggersSuspended && !_overlay.IsMenuOpen)
        {
            _cornerFired = true;
            _overlay.OpenMenu();
        }
    }

    // ---- messages of the hidden window -----------------------------------------------

    private IntPtr MessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_COPYDATA:
                var command = SingleInstance.TryRead(lParam);
                if (command != null)
                {
                    handled = true;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => HandleCommand(command)));
                    return (IntPtr)1;
                }

                break;
            case Win32.WM_SETTINGCHANGE:
                if (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        _theme.Refresh();
                        _overlay.RefreshAll();
                    }));
                }

                break;
            case Win32.WM_DISPLAYCHANGE:
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _primaryBounds = MonitorService.GetPrimary().Bounds;
                    _overlay.RefreshAll();
                }));
                break;
            case Win32.WM_QUERYENDSESSION:
            case Win32.WM_ENDSESSION:
                _config.Flush();
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>"ru" when the Windows display language is Russian, otherwise "en".</summary>
    internal static string SystemLanguage()
    {
        try
        {
            int primary = Win32.GetUserDefaultUILanguage() & 0x3FF;
            return primary == 0x19 ? "ru" : "en";
        }
        catch (Exception)
        {
            return "en";
        }
    }
}
