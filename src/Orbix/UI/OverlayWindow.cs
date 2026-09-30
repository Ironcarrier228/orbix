using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Orbix.Core.Layout;
using Orbix.Core.Models;
using Orbix.Core.Services;
using Orbix.Native;
using Orbix.Services;

namespace Orbix.UI;

internal enum OverlayState
{
    /// <summary>The orb is not shown (hidden by the user, full-screen application, desktop-only mode).</summary>
    Hidden,

    /// <summary>The small orb is on the screen.</summary>
    Orb,

    Opening,

    Open,

    Closing,
}

/// <summary>
/// The one window of the application that is always on the screen: a borderless, transparent, topmost tool window
/// (no taskbar button, no Alt+Tab entry). It is exactly as big as the orb while idle - so everything around the circle
/// is transparent and click-through - and grows into a square around the orb while the menu is open.
/// The orb stays in the centre of the primary monitor (or of the monitor with the cursor, if selected).
/// </summary>
internal sealed partial class OverlayWindow : Window
{
    private const double EdgeMargin = 14;

    private readonly AppConfig _config;
    private readonly ConfigService _configService;
    private readonly ThemeService _theme;
    private readonly IconService _icons;
    private readonly FullscreenWatcher _fullscreen;
    private readonly MouseHookService _mouseHook;
    private readonly Animator _animator = new();
    private readonly Canvas _stage;
    private readonly OrbVisual _orb;
    private readonly MenuView _menu;
    private readonly DispatcherTimer _desktopTimer;
    private readonly DispatcherTimer _monitorTimer;
    private readonly DispatcherTimer _trimTimer;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private OverlayState _state = OverlayState.Hidden;
    private MonitorGeometry _monitor = MonitorService.GetPrimary();
    private int _orbCenterX;
    private int _orbCenterY;
    private int _menuCenterX;
    private int _menuCenterY;
    private int _menuSizePx;
    private double _menuDiscRadiusPx;
    private IntPtr _previousForeground;
    private int _modalDepth;
    private int _closeToken;
    private bool _orbHover;
    private bool _orbPressed;
    private bool _captured;
    private OrbDrag? _orbDrag;
    private bool _firstShowDone;
    private bool _refreshPending;
    private string? _orbImageKey;
    private ImageSource? _orbImage;

    private sealed record OrbDrag(int CursorX, int CursorY, int CenterX, int CenterY, bool Moved);

    public OverlayWindow(AppConfig config, ConfigService configService, ThemeService theme, IconService icons, FullscreenWatcher fullscreen, MouseHookService mouseHook)
    {
        _config = config;
        _configService = configService;
        _theme = theme;
        _icons = icons;
        _fullscreen = fullscreen;
        _mouseHook = mouseHook;

        // A transparent, borderless, topmost tool window. Background = null: empty pixels are not painted and
        // therefore let the mouse through to the window below.
        Title = "OrbixOverlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = true;
        AllowDrop = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Left = -32000;
        Top = -32000;
        Width = 64;
        Height = 64;
        FocusVisualStyle = null;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // The stage is a zero-size canvas in the middle of the window: children are placed relative to the centre,
        // so the window can change its size without anything moving.
        _stage = new Canvas
        {
            Width = 0,
            Height = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = true,
        };
        var root = new Grid { Background = null };
        root.Children.Add(_stage);
        Content = root;

        _orb = new OrbVisual(_animator);
        _menu = new MenuView(_animator, _orb);
        _stage.Children.Add(_menu.BackdropLayer);
        _stage.Children.Add(_menu.GuideLayer);
        _stage.Children.Add(_menu.RingLayer);
        _stage.Children.Add(_orb.Root);
        _stage.Children.Add(_menu.OverlayLayer);

        _menu.ItemInvoked += item => ItemInvoked?.Invoke(item);
        _menu.CloseRequested += () => CloseMenu(true);
        _menu.ContextRequested += OnContextRequested;
        _menu.EditModeChanged += SetEditMode;

        _desktopTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _desktopTimer.Tick += (_, _) => UpdateOrbVisibility();
        _monitorTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _monitorTimer.Tick += (_, _) => FollowCursorMonitor();
        _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _trimTimer.Tick += (_, _) => TrimMemory();

        SourceInitialized += OnSourceInitialized;
        Deactivated += OnDeactivated;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonDown += OnPreviewLeftDown;
        PreviewMouseLeftButtonUp += OnPreviewLeftUp;
        PreviewMouseRightButtonUp += OnPreviewRightUp;
        MouseWheel += OnMouseWheel;
        MouseLeave += OnMouseLeaveWindow;
        MouseEnter += OnMouseEnterWindow;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(ApplyBounds));

        _mouseHook.AddButtonHandler(OnGlobalMouseDown);
        InitializeEditSupport();

        // create the window handle now (SourceInitialized positions the window), show it later
        new WindowInteropHelper(this).EnsureHandle();
    }

    // ---- public API ----------------------------------------------------------------

    /// <summary>A leaf item was activated; the host launches it.</summary>
    public event Action<RadialItem>? ItemInvoked;

    /// <summary>The user asked to edit an item in the settings window.</summary>
    public event Action<RadialItem?>? SettingsRequested;

    /// <summary>Right click on the idle orb: the host shows the same menu as the tray icon.</summary>
    public event Action? OrbContextRequested;

    /// <summary>The menu finished closing (used to trim memory).</summary>
    public event Action? MenuClosed;

    public bool IsMenuOpen => _state is OverlayState.Open or OverlayState.Opening;

    public bool IsOrbVisible => _state == OverlayState.Orb;

    public OverlayState State => _state;

    public IntPtr Handle => _hwnd;

    /// <summary>Applies the settings and shows / hides the orb. Called at start and after every settings change.</summary>
    public void Start()
    {
        RefreshAll();
    }

    /// <summary>Re-reads every setting that influences the orb and the geometry.</summary>
    public void RefreshAll()
    {
        if (_state is OverlayState.Open or OverlayState.Opening or OverlayState.Closing)
        {
            // applied when the menu has closed (changing geometry under an open menu would make it jump)
            _refreshPending = true;
            return;
        }

        _refreshPending = false;
        ConfigureOrb();
        _monitor = ChooseMonitor();
        ComputeOrbCenter();
        _animator.Enabled = _config.Menu.Animations;
        _animator.Speed = _config.Menu.AnimationSpeed;

        bool cursorMode = _config.Orb.Monitor == MonitorMode.Cursor && MonitorService.Count() > 1;
        _monitorTimer.IsEnabled = cursorMode;
        _desktopTimer.IsEnabled = _config.Orb.Visibility == OrbVisibilityMode.DesktopOnly;

        if (_state is OverlayState.Orb or OverlayState.Hidden)
        {
            ApplyBounds();
            UpdateOrbVisibility();
        }
    }

    public void ToggleMenu(bool edit = false)
    {
        if (IsMenuOpen)
        {
            CloseMenu(true);
        }
        else
        {
            OpenMenu(edit);
        }
    }

    /// <summary>Re-evaluates whether the orb should be visible (visibility modes, full-screen applications).</summary>
    public void UpdateOrbVisibility()
    {
        if (_state is OverlayState.Opening or OverlayState.Open or OverlayState.Closing)
        {
            return;
        }

        bool show = ShouldShowOrb();
        if (show && _state == OverlayState.Hidden)
        {
            _state = OverlayState.Orb;
            ApplyBounds();
            ShowOverlay();
        }
        else if (!show && _state == OverlayState.Orb)
        {
            _state = OverlayState.Hidden;
            HideOverlay();
        }

        UpdateBreathing();
    }

    public void ShowToast(string text, bool error = false) => _menu.ShowToast(text, error);

    /// <summary>Suppresses "close on deactivation" while a dialog or native menu of ours is open.</summary>
    public IDisposable BeginModal()
    {
        _modalDepth++;
        return new ModalScope(this);
    }

    private sealed class ModalScope : IDisposable
    {
        private OverlayWindow? _owner;

        public ModalScope(OverlayWindow owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner != null)
            {
                _owner._modalDepth = Math.Max(0, _owner._modalDepth - 1);
                _owner = null;
            }
        }
    }

    // ---- geometry ------------------------------------------------------------------

    private MonitorGeometry ChooseMonitor() =>
        _config.Orb.Monitor == MonitorMode.Cursor ? MonitorService.GetUnderCursor() : MonitorService.GetPrimary();

    private void ComputeOrbCenter()
    {
        _orbCenterX = _monitor.CenterX + (int)Math.Round(_config.Orb.OffsetX * _monitor.Scale);
        _orbCenterY = _monitor.CenterY + (int)Math.Round(_config.Orb.OffsetY * _monitor.Scale);
    }

    private static int EvenCeil(double value)
    {
        int v = (int)Math.Ceiling(value);
        return v % 2 == 0 ? v : v + 1;
    }

    private int OrbWindowPx() => EvenCeil(_config.Orb.Size * _monitor.Scale) + 2;

    /// <summary>Positions and sizes the window for the current state.</summary>
    private void ApplyBounds()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_state is OverlayState.Open or OverlayState.Opening or OverlayState.Closing && _menuSizePx > 0)
        {
            SetBounds(_menuCenterX, _menuCenterY, _menuSizePx);
        }
        else
        {
            SetBounds(_orbCenterX, _orbCenterY, OrbWindowPx());
        }
    }

    private void SetBounds(int centerX, int centerY, int sizePx)
    {
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, centerX - sizePx / 2, centerY - sizePx / 2, sizePx, sizePx, Win32.SWP_NOACTIVATE);
    }

    private void ShowOverlay()
    {
        if (!_firstShowDone)
        {
            _firstShowDone = true;
            Show();
        }
        else
        {
            Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);
        }

        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void HideOverlay()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        }
    }

    private bool ShouldShowOrb()
    {
        var orb = _config.Orb;
        if (orb.Hidden)
        {
            return false;
        }

        switch (orb.Visibility)
        {
            case OrbVisibilityMode.AutoHideFullscreen:
                var screen = _fullscreen.State;
                return !(screen.FullscreenActive && screen.FullscreenMonitor == _monitor.Handle);
            case OrbVisibilityMode.DesktopOnly:
                return FullscreenWatcher.IsPointOverDesktop(_orbCenterX, _orbCenterY);
            default:
                return true;
        }
    }

    private void FollowCursorMonitor()
    {
        if (_state != OverlayState.Orb && _state != OverlayState.Hidden)
        {
            return;
        }

        var target = MonitorService.GetUnderCursor();
        if (target.Handle != _monitor.Handle)
        {
            _monitor = target;
            ComputeOrbCenter();
            ApplyBounds();
            UpdateOrbVisibility();
        }
    }

    // ---- orb look ------------------------------------------------------------------

    private void ConfigureOrb()
    {
        var orb = _config.Orb;
        var palette = _theme.Palette;

        Color? color = string.IsNullOrWhiteSpace(orb.Color) ? null : ThemeService.ParseColor(orb.Color, palette.Accent);
        _orb.Configure(orb.Size, palette, color, LoadOrbImage(orb.ImagePath));
        _orbHover = false;
        _orb.Pop.Snap(1);
        _orb.Opacity.Snap(_state is OverlayState.Open or OverlayState.Opening ? 1.0 : orb.RestingOpacity);
    }

    private ImageSource? LoadOrbImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _orbImageKey = null;
            _orbImage = null;
            return null;
        }

        string key = path + "|" + _config.Orb.Size;
        if (key == _orbImageKey)
        {
            return _orbImage;
        }

        _orbImageKey = key;
        _orbImage = _icons.LoadPicture(path, (int)Math.Ceiling(_config.Orb.Size * 2 * _monitor.Scale));
        return _orbImage;
    }

    private void UpdateBreathing()
    {
        bool run = _config.Orb.Breathing && _state == OverlayState.Orb;
        _orb.SetBreathing(run);
    }

    private void UpdateOrbOpacity()
    {
        if (_state is OverlayState.Open or OverlayState.Opening)
        {
            return;
        }

        _orb.Opacity.Go(_orbHover ? _config.Orb.HoverOpacity : _config.Orb.RestingOpacity, 140);
        _orb.Pop.Go(_orbHover ? 1.06 : 1.0, 120);
    }

    // ---- window plumbing -------------------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Not in Alt+Tab / taskbar, never takes the focus while it is only an orb.
        long ex = Win32.GetExStyle(_hwnd);
        Win32.SetExStyle(_hwnd, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        _monitor = ChooseMonitor();
        ComputeOrbCenter();
        SetBounds(_orbCenterX, _orbCenterY, OrbWindowPx());
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEACTIVATE when _state is OverlayState.Orb or OverlayState.Hidden:
                handled = true;
                return (IntPtr)Win32.MA_NOACTIVATE;
            case Win32.WM_DPICHANGED:
            case Win32.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    if (_state is OverlayState.Orb or OverlayState.Hidden)
                    {
                        _monitor = ChooseMonitor();
                        ComputeOrbCenter();
                    }

                    ApplyBounds();
                }));
                break;
        }

        return IntPtr.Zero;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_state is OverlayState.Open or OverlayState.Opening && _modalDepth == 0)
        {
            CloseMenu(false);
        }
    }

    /// <summary>Clicks outside of the glass disc close the menu (the click itself still reaches the other window).</summary>
    private bool OnGlobalMouseDown(HookButton button, int x, int y)
    {
        if (_state != OverlayState.Open || _modalDepth > 0)
        {
            return false;
        }

        double dx = x - _menuCenterX;
        double dy = y - _menuCenterY;
        if (Math.Sqrt(dx * dx + dy * dy) > _menuDiscRadiusPx + 4)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                if (_state == OverlayState.Open && _modalDepth == 0)
                {
                    CloseMenu(false);
                }
            }));
        }

        return false;
    }

    // ---- open / close --------------------------------------------------------------

    /// <summary>Opens the menu around the orb. Typical time to the first frame: a few milliseconds plus the backdrop capture.</summary>
    public void OpenMenu(bool edit = false)
    {
        if (IsMenuOpen)
        {
            if (edit != _menu.IsEdit)
            {
                SetEditMode(edit);
            }

            return;
        }

        var clock = Stopwatch.StartNew();
        _closeToken++;
        _trimTimer.Stop();

        var foreground = Win32.GetForegroundWindow();
        _previousForeground = foreground != _hwnd ? foreground : IntPtr.Zero;

        _monitor = ChooseMonitor();
        ComputeOrbCenter();

        var session = BuildSession(out int sizePx, out int centerX, out int centerY);
        _menuCenterX = centerX;
        _menuCenterY = centerY;
        _menuSizePx = sizePx;
        _menuDiscRadiusPx = (session.HalfExtent - 4) * _monitor.Scale;

        // The frosted glass: a blurred snapshot of the screen behind the menu (taken before the window grows).
        System.Windows.Media.Imaging.BitmapSource? backdrop = null;
        if (_config.Menu.BlurBackground)
        {
            backdrop = BackdropService.CaptureBlurred(centerX - sizePx / 2, centerY - sizePx / 2, sizePx, sizePx);
        }

        _state = OverlayState.Opening;
        _desktopTimer.Stop();
        _monitorTimer.Stop();
        _orb.SetBreathing(false);

        SetBounds(centerX, centerY, sizePx);
        if (!_firstShowDone || !IsVisible)
        {
            ShowOverlay();
        }
        else
        {
            Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);
        }

        _orb.Opacity.Go(1, 120);
        _orb.Pop.Snap(1);

        var openSession = new MenuSession
        {
            HalfExtent = session.HalfExtent,
            Scale = session.Scale,
            Options = session.Options,
            OrbRadius = session.OrbRadius,
            Backdrop = backdrop,
        };
        _menu.Show(openSession, _config, _theme.Palette, _icons, edit);
        _state = OverlayState.Open;

        BecomeActive();
        _mouseHook.Acquire("menu");

        // latency to the first rendered frame
        void OnFirstFrame(object? s, EventArgs a)
        {
            CompositionTarget.Rendering -= OnFirstFrame;
            Logger.Info($"Menu opened: {clock.ElapsedMilliseconds} ms to the first frame (window {sizePx}px, backdrop {(backdrop != null ? "yes" : "no")}).");
        }

        CompositionTarget.Rendering += OnFirstFrame;
    }

    public void CloseMenu(bool restoreFocus)
    {
        if (!IsMenuOpen)
        {
            return;
        }

        _state = OverlayState.Closing;
        _mouseHook.Release("menu");
        int token = ++_closeToken;
        _menu.Hide(() =>
        {
            if (token == _closeToken)
            {
                FinishClose(restoreFocus);
            }
        });
    }

    private void FinishClose(bool restoreFocus)
    {
        _menu.Reset();

        // back to an inactive, click-through-outside-the-circle orb
        long ex = Win32.GetExStyle(_hwnd);
        Win32.SetExStyle(_hwnd, ex | Win32.WS_EX_NOACTIVATE);

        _state = OverlayState.Hidden;
        _orbHover = false;
        _monitor = ChooseMonitor();
        ComputeOrbCenter();
        _orb.Opacity.Snap(_config.Orb.RestingOpacity);
        _orb.Pop.Snap(1);
        ApplyBounds();

        bool show = ShouldShowOrb();
        if (show)
        {
            _state = OverlayState.Orb;
            ShowOverlay();
        }
        else
        {
            HideOverlay();
        }

        if (_config.Orb.Visibility == OrbVisibilityMode.DesktopOnly)
        {
            _desktopTimer.Start();
        }

        if (_config.Orb.Monitor == MonitorMode.Cursor && MonitorService.Count() > 1)
        {
            _monitorTimer.Start();
        }

        UpdateBreathing();
        if (_refreshPending)
        {
            RefreshAll();
        }

        if (restoreFocus && _previousForeground != IntPtr.Zero && Win32.IsWindow(_previousForeground) && Win32.GetForegroundWindow() == _hwnd)
        {
            Win32.SetForegroundWindow(_previousForeground);
        }

        _previousForeground = IntPtr.Zero;
        _trimTimer.Stop();
        _trimTimer.Start();
        MenuClosed?.Invoke();
    }

    private MenuSession BuildSession(out int sizePx, out int centerX, out int centerY)
    {
        var menu = _config.Menu;
        double scale = _monitor.Scale;
        double orbRadius = _config.Orb.Size / 2;
        double gap = Math.Max(8, menu.ItemSize * 0.2);
        var bounds = _monitor.Bounds;

        // the biggest square that fits on the monitor
        double monitorHalf = Math.Min(bounds.Width, bounds.Height) / 2.0 / scale;
        var options = new LayoutOptions
        {
            OrbRadius = orbRadius,
            ItemSize = menu.ItemSize,
            Radius = menu.Radius,
            Gap = gap,
            StartAngleDeg = menu.StartAngle,
            MaxVisibleOrbits = menu.MaxVisibleOrbits,
            MaxCenterRadius = Math.Max(orbRadius + menu.ItemSize / 2 + gap + 6, monitorHalf - EdgeMargin - menu.ItemSize / 2),
            MinItemSize = Math.Max(28, menu.ItemSize * 0.55),
            MaxArcDeg = 300,
        };

        double outer = LayoutPlanner.ComputeMaxOuterRadius(_config.ActiveProfile.Items, options, 1);
        double half = Math.Min(outer + EdgeMargin + 4, monitorHalf);
        sizePx = EvenCeil(half * 2 * scale);

        // If the orb was moved close to an edge, the menu is shifted so that it stays on the monitor.
        int halfPx = sizePx / 2;
        centerX = _orbCenterX;
        centerY = _orbCenterY;
        if (bounds.Width >= sizePx)
        {
            centerX = Math.Clamp(centerX, bounds.Left + halfPx, bounds.Right - halfPx);
        }

        if (bounds.Height >= sizePx)
        {
            centerY = Math.Clamp(centerY, bounds.Top + halfPx, bounds.Bottom - halfPx);
        }

        return new MenuSession
        {
            HalfExtent = sizePx / 2.0 / scale,
            Scale = scale,
            Options = options,
            OrbRadius = orbRadius,
        };
    }

    /// <summary>Makes the window the foreground window so that the keyboard works (Esc, arrows, search).</summary>
    private void BecomeActive()
    {
        long ex = Win32.GetExStyle(_hwnd);
        Win32.SetExStyle(_hwnd, ex & ~Win32.WS_EX_NOACTIVATE);

        Activate();
        if (Win32.GetForegroundWindow() != _hwnd)
        {
            // The foreground lock may refuse: attach to the input queue of the current foreground thread.
            var foreground = Win32.GetForegroundWindow();
            uint foregroundThread = foreground == IntPtr.Zero ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
            uint thisThread = Win32.GetCurrentThreadId();
            if (foregroundThread != 0 && foregroundThread != thisThread && Win32.AttachThreadInput(thisThread, foregroundThread, true))
            {
                Win32.SetForegroundWindow(_hwnd);
                Win32.AttachThreadInput(thisThread, foregroundThread, false);
            }
            else
            {
                Win32.SetForegroundWindow(_hwnd);
            }
        }

        Focus();
        Keyboard.Focus(this);
    }

    private void SetEditMode(bool edit)
    {
        if (!IsMenuOpen)
        {
            return;
        }

        _menu.SetEditMode(edit);
    }

    private void TrimMemory()
    {
        _trimTimer.Stop();
        if (_state is OverlayState.Orb or OverlayState.Hidden)
        {
            // The menu is closed: give memory back (working set of a tray application should stay small).
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            try
            {
                Win32.EmptyWorkingSet(Win32.GetCurrentProcess());
            }
            catch (Exception)
            {
                // not critical
            }
        }
    }

    // ---- mouse ---------------------------------------------------------------------

    private Point StagePoint(MouseEventArgs e) => e.GetPosition(_stage);

    private void OnMouseEnterWindow(object sender, MouseEventArgs e)
    {
        if (_state == OverlayState.Orb && !_orbHover)
        {
            _orbHover = true;
            UpdateOrbOpacity();
        }
    }

    private void OnMouseLeaveWindow(object sender, MouseEventArgs e)
    {
        if (_state == OverlayState.Orb)
        {
            _orbHover = false;
            _orbPressed = false;
            UpdateOrbOpacity();
        }
        else if (IsMenuOpen && !_captured)
        {
            _menu.PointerLeft();
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_orbDrag != null)
        {
            DragOrb();
            return;
        }

        if (_state == OverlayState.Orb)
        {
            if (!_orbHover)
            {
                _orbHover = true;
                UpdateOrbOpacity();
            }

            return;
        }

        if (IsMenuOpen)
        {
            _menu.PointerMoved(StagePoint(e));
            Cursor = _menu.CurrentCursor;
        }
    }

    private void OnPreviewLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_state == OverlayState.Orb)
        {
            _orbPressed = true;
            e.Handled = true;
            return;
        }

        if (!IsMenuOpen)
        {
            return;
        }

        var p = StagePoint(e);
        var hit = _menu.HitTest(p);
        if (hit.Kind == HitKind.Orb && _menu.IsEdit && _config.Orb.AllowMove)
        {
            if (e.ClickCount == 2)
            {
                ResetOrbPosition();
                e.Handled = true;
                return;
            }

            Win32.GetCursorPos(out var cursor);
            _orbDrag = new OrbDrag(cursor.X, cursor.Y, _menuCenterX, _menuCenterY, false);
            CaptureMouse();
            _captured = true;
            e.Handled = true;
            return;
        }

        if (_menu.PointerDown(p, MouseButton.Left))
        {
            CaptureMouse();
            _captured = true;
        }

        e.Handled = true;
    }

    private void OnPreviewLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_state == OverlayState.Orb)
        {
            bool click = _orbPressed;
            _orbPressed = false;
            e.Handled = true;
            if (click)
            {
                OpenMenu();
            }

            return;
        }

        if (_captured)
        {
            ReleaseMouseCapture();
            _captured = false;
        }

        if (_orbDrag != null)
        {
            FinishOrbDrag();
            e.Handled = true;
            return;
        }

        if (IsMenuOpen)
        {
            _menu.PointerUp(StagePoint(e), MouseButton.Left);
            Cursor = _menu.CurrentCursor;
            e.Handled = true;
        }
    }

    private void OnPreviewRightUp(object sender, MouseButtonEventArgs e)
    {
        if (_state == OverlayState.Orb)
        {
            e.Handled = true;
            OrbContextRequested?.Invoke();
            return;
        }

        if (IsMenuOpen)
        {
            _menu.PointerUp(StagePoint(e), MouseButton.Right);
            e.Handled = true;
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsMenuOpen)
        {
            _menu.Wheel(e.Delta);
            e.Handled = true;
        }
    }

    // ---- keyboard ------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsMenuOpen || _menu.IsRenaming)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_menu.KeyDown(key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (IsMenuOpen && !_menu.IsRenaming)
        {
            _menu.TextInput(e.Text);
            e.Handled = true;
        }
    }

    // ---- moving the orb (edit mode) -----------------------------------------------

    private void DragOrb()
    {
        if (_orbDrag == null)
        {
            return;
        }

        Win32.GetCursorPos(out var cursor);
        int dx = cursor.X - _orbDrag.CursorX;
        int dy = cursor.Y - _orbDrag.CursorY;
        if (!_orbDrag.Moved && Math.Abs(dx) + Math.Abs(dy) < 4)
        {
            return;
        }

        _orbDrag = _orbDrag with { Moved = true };
        var bounds = _monitor.Bounds;
        int half = _menuSizePx / 2;
        _menuCenterX = Math.Clamp(_orbDrag.CenterX + dx, bounds.Left + half, Math.Max(bounds.Left + half, bounds.Right - half));
        _menuCenterY = Math.Clamp(_orbDrag.CenterY + dy, bounds.Top + half, Math.Max(bounds.Top + half, bounds.Bottom - half));
        SetBounds(_menuCenterX, _menuCenterY, _menuSizePx);
    }

    private void FinishOrbDrag()
    {
        var drag = _orbDrag;
        _orbDrag = null;
        if (drag is { Moved: true })
        {
            // store the new position as an offset from the monitor centre (DIPs), the orb remains "centred by default"
            _config.Orb.OffsetX = Math.Round((_menuCenterX - _monitor.CenterX) / _monitor.Scale);
            _config.Orb.OffsetY = Math.Round((_menuCenterY - _monitor.CenterY) / _monitor.Scale);
            ComputeOrbCenter();
        }
    }

    private void ResetOrbPosition()
    {
        _config.Orb.OffsetX = 0;
        _config.Orb.OffsetY = 0;
        ComputeOrbCenter();
        _menuCenterX = _orbCenterX;
        _menuCenterY = _orbCenterY;
        SetBounds(_menuCenterX, _menuCenterY, _menuSizePx);
        _menu.ShowToast(Orbix.Core.Localization.Loc.T("Menu.OrbCentered"));
    }
}
