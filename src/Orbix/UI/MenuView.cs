using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Orbix.Core.Layout;
using Orbix.Core.Localization;
using Orbix.Core.Models;
using Orbix.Core.Search;
using Orbix.Services;

namespace Orbix.UI;

/// <summary>Everything the menu needs to know about one opening (geometry, backdrop snapshot, layout options).</summary>
internal sealed class MenuSession
{
    /// <summary>Half of the window size in DIPs (the window is a centred square).</summary>
    public double HalfExtent { get; init; }

    /// <summary>DPI scale of the monitor (1.0 = 96 dpi) - used to size the icons of the shell.</summary>
    public double Scale { get; init; } = 1.0;

    public BitmapSource? Backdrop { get; init; }

    public LayoutOptions Options { get; init; } = new();

    public double OrbRadius { get; init; } = 28;
}

/// <summary>One orbit of the menu: its visuals, source list and layout.</summary>
internal sealed class RingView
{
    public int Level { get; init; }

    public string? ParentId { get; init; }

    public bool IsSearch { get; init; }

    public Canvas Layer { get; } = new() { Width = 0, Height = 0, IsHitTestVisible = false };

    public System.Windows.Shapes.Path Guide { get; } = new() { IsHitTestVisible = false, Opacity = 0 };

    public AnimatedDouble? GuideAlpha { get; set; }

    public List<ItemVisual> Visuals { get; set; } = new();

    public ItemVisual? AddSlot { get; set; }

    /// <summary>The list that is shown (null for the search results).</summary>
    public ObservableList<RadialItem>? Source { get; set; }

    public RingLayout? Layout { get; set; }

    public RadialItem? Parent { get; init; }

    public int ItemCount => Visuals.Count;
}

internal enum HitKind
{
    None,
    Disc,
    Orb,
    Item,
    AddSlot,
}

internal readonly record struct HitResult(HitKind Kind, RingView? Ring, int Index, ItemVisual? Visual, bool OnDelete)
{
    public static HitResult None { get; } = new(HitKind.None, null, -1, null, false);
}

/// <summary>A request to show the context menu of an item (or of the "+" slot when <see cref="Item"/> is null).</summary>
internal sealed record ContextRequest(RadialItem? Item, ObservableList<RadialItem> List, int Index, RadialItem? Parent);

/// <summary>
/// The radial menu itself: frosted-glass disc, orbits of items, navigation through groups, hover / keyboard /
/// wheel handling, type-to-search and the edit mode (drag to reorder, delete badge). It owns the visuals inside
/// the stage canvas of the overlay window and reports user intentions through events.
/// </summary>
internal sealed partial class MenuView
{
    private const double DiscPadding = 18;

    private readonly Animator _anim;
    private readonly OrbVisual _orb;
    private readonly List<RadialItem> _path = new();
    private readonly List<RingView> _rings = new();
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _armTimer;

    private AppConfig _config = null!;
    private Palette _palette = null!;
    private IconService _icons = null!;
    private MenuSession? _session;
    private MenuLayout? _layout;
    private bool _edit;
    private bool _open;
    private string _query = string.Empty;
    private SearchResult _search = SearchResult.Empty;
    private RadialItem? _armed;

    // backdrop
    private readonly Canvas _backdropRoot = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    private readonly ScaleTransform _backdropScale = new(1, 1);
    private readonly Ellipse _shadowDisc = new() { IsHitTestVisible = false };
    private readonly Image _glassImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly EllipseGeometry _glassClip = new();
    private readonly Ellipse _tintDisc = new() { IsHitTestVisible = true };
    private readonly Ellipse _sheenDisc = new() { IsHitTestVisible = false };
    private readonly Ellipse _rimDisc = new() { IsHitTestVisible = false };
    private readonly AnimatedDouble _aDisc;
    private readonly AnimatedDouble _aBackdrop;

    public MenuView(Animator animator, OrbVisual orb)
    {
        _anim = animator;
        _orb = orb;

        BackdropLayer = NewLayer();
        GuideLayer = NewLayer();
        RingLayer = NewLayer();
        OverlayLayer = NewLayer();

        BuildBackdrop();
        BuildOverlay();

        _aDisc = animator.Create(0, _ => UpdateDiscGeometry());
        _aBackdrop = animator.Create(0, v =>
        {
            _backdropRoot.Opacity = v;
            double s = 0.86 + 0.14 * v;
            _backdropScale.ScaleX = s;
            _backdropScale.ScaleY = s;
        });

        _hoverTimer = new DispatcherTimer(DispatcherPriority.Input);
        _hoverTimer.Tick += OnHoverTimer;
        _armTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _armTimer.Tick += (_, _) => Disarm();
    }

    // layers, bottom to top (the window puts the orb between the rings and the overlay)
    public Canvas BackdropLayer { get; }

    public Canvas GuideLayer { get; }

    public Canvas RingLayer { get; }

    public Canvas OverlayLayer { get; }

    public bool IsOpen => _open;

    public bool IsEdit => _edit;

    public int Depth => _path.Count;

    public bool SearchActive => _search.IsActive;

    /// <summary>Cursor the window should show for the current pointer position.</summary>
    public Cursor CurrentCursor { get; private set; } = Cursors.Arrow;

    // ---- events -----------------------------------------------------------------

    /// <summary>A leaf item was activated (click / Enter / digit).</summary>
    public event Action<RadialItem>? ItemInvoked;

    /// <summary>The menu wants to close (orb click at the top level, Esc, right click).</summary>
    public event Action? CloseRequested;

    /// <summary>The context menu of an item or of the "+" slot is requested (edit mode).</summary>
    public event Action<ContextRequest>? ContextRequested;

    /// <summary>The user toggled the edit mode with the keyboard.</summary>
    public event Action<bool>? EditModeChanged;

    // ---- lifecycle --------------------------------------------------------------

    /// <summary>Builds the menu and plays the appear animation.</summary>
    public void Show(MenuSession session, AppConfig config, Palette palette, IconService icons, bool edit)
    {
        _session = session;
        _config = config;
        _palette = palette;
        _icons = icons;
        _edit = edit;
        _open = true;
        _path.Clear();
        _query = string.Empty;
        _search = SearchResult.Empty;
        _armed = null;
        _hot = HitResult.None;
        _keyboardIndex = -1;
        ClearDrag();

        ApplyAnimationSettings();
        ConfigureBackdrop();
        ConfigureOverlay();
        RemoveAllRings();

        _orb.SetGlyph("close", palette, true);
        Rebuild(animate: true, opening: true);
        UpdateEditBanner();

        double target = DiscRadius();
        _aDisc.Snap(_orb.Diameter / 2 + 6);
        _aDisc.Go(target, 200, 0, Easing.OutCubic);
        _aBackdrop.Snap(0);
        _aBackdrop.Go(1, 170, 0, Easing.OutCubic);
    }

    /// <summary>Plays the disappear animation, then raises <paramref name="done"/>.</summary>
    public void Hide(Action done)
    {
        if (!_open)
        {
            done();
            return;
        }

        _open = false;
        _hoverTimer.Stop();
        _armTimer.Stop();
        HideLabel();
        HideToast();
        ClearDrag();
        _hot = HitResult.None;
        _orb.Pop.Go(1, 90);
        _orb.SetGlyph(null, _palette, true);

        // icons collapse into the orb
        foreach (var ring in _rings)
        {
            int i = 0;
            foreach (var visual in ring.Visuals.Concat(ring.AddSlot != null ? new[] { ring.AddSlot } : Array.Empty<ItemVisual>()))
            {
                double delay = Math.Min(i * 4, 30);
                visual.X.Go(0, 120, delay, Easing.InCubic);
                visual.Y.Go(0, 120, delay, Easing.InCubic);
                visual.Spawn.Go(0.3, 120, delay, Easing.InCubic);
                visual.Alpha.Go(0, 110, delay, Easing.Linear);
                i++;
            }

            ring.GuideAlpha?.Go(0, 100);
        }

        _aDisc.Go(_orb.Diameter / 2 + 6, 130, 0, Easing.InCubic);
        _aBackdrop.Go(0, 130, 0, Easing.InCubic, () =>
        {
            RemoveAllRings();
            done();
        });
    }

    /// <summary>Immediately removes everything (used when the menu is closed without animation).</summary>
    public void Reset()
    {
        _open = false;
        _hoverTimer.Stop();
        _armTimer.Stop();
        HideLabel();
        HideToast();
        ClearDrag();
        _anim.CancelAll();
        _aBackdrop.Snap(0);
        _aDisc.Snap(0);
        RemoveAllRings();
        _hot = HitResult.None;
        _orb.SetGlyph(null, _palette ?? Palette.Create(true, Colors.DodgerBlue), false);
    }

    public void SetEditMode(bool edit)
    {
        if (_edit == edit)
        {
            return;
        }

        _edit = edit;
        ClearDrag();
        foreach (var ring in _rings)
        {
            foreach (var visual in ring.Visuals)
            {
                visual.SetEditMode(edit, true);
            }
        }

        Rebuild(animate: true);
        UpdateEditBanner();
    }

    /// <summary>Re-reads the tree after it was changed from outside (rename, icon change, add, delete).</summary>
    public void Refresh()
    {
        if (!_open)
        {
            return;
        }

        foreach (var ring in _rings)
        {
            foreach (var visual in ring.Visuals)
            {
                if (visual.Item != null)
                {
                    _icons.Invalidate(visual.Item);
                }
            }
        }

        // a full rebuild of the visuals keeps the code simple: names, icons and colours may all have changed
        RemoveAllRings();
        Rebuild(animate: true);
    }

    // ---- layout / visuals -------------------------------------------------------

    private static Canvas NewLayer() => new() { Width = 0, Height = 0, IsHitTestVisible = false };

    private void ApplyAnimationSettings()
    {
        var menu = _config.Menu;
        _anim.Enabled = menu.Animations;
        _anim.Speed = menu.AnimationSpeed;
    }

    private MenuProfile Profile => _config.ActiveProfile;

    private ObservableList<RadialItem> ListAtLevel(int level) => level == 0 ? Profile.Items : _path[level - 1].Children;

    /// <summary>Drops path entries that no longer exist (an item was deleted or moved).</summary>
    private void ValidatePath()
    {
        ObservableList<RadialItem> list = Profile.Items;
        for (int i = 0; i < _path.Count; i++)
        {
            if (!list.Contains(_path[i]) || !_path[i].IsGroup)
            {
                _path.RemoveRange(i, _path.Count - i);
                break;
            }

            list = _path[i].Children;
        }
    }

    private void Rebuild(bool animate, bool opening = false)
    {
        if (_session == null)
        {
            return;
        }

        ValidatePath();
        MenuLayout layout;
        if (_search.IsActive)
        {
            int count = Math.Min(_search.Matches.Count, 12);
            var options = _session.Options with
            {
                MaxCenterRadius = Math.Min(_session.Options.MaxCenterRadius, _session.HalfExtent - 14 - _session.Options.ItemSize / 2),
            };
            layout = RadialLayout.Compute(new[] { new RingSpec(count, -1) }, options);
        }
        else
        {
            var specs = LayoutPlanner.BuildSpecs(Profile.Items, _path, _edit ? 1 : 0);
            layout = RadialLayout.Compute(specs, _session.Options);
        }

        _layout = layout;
        if (Logger.DebugEnabled)
        {
            // slot centres relative to the centre of the orb, in DIPs (used by the end-to-end test to click on the items)
            Logger.Debug("Layout " + string.Join(" | ", layout.Rings.Select(r =>
                $"L{r.Level} r={r.Radius:0.#} s={r.ItemSize:0.#} [" + string.Join(";", r.Slots.Select(slot => $"{slot.X:0.#},{slot.Y:0.#}")) + "]")));
        }

        var wanted = new List<RingView>();
        foreach (var ringLayout in layout.Rings)
        {
            int level = ringLayout.Level;
            bool search = _search.IsActive;
            string? parentId = search ? "search" : level == 0 ? null : _path[level - 1].Id;
            var ring = _rings.FirstOrDefault(r => r.Level == level && r.ParentId == parentId && r.IsSearch == search);
            bool isNew = ring == null;
            ring ??= CreateRing(level, parentId, search);

            // where new icons appear from: the orb for the first ring, the owning group for deeper rings
            double fromX = 0, fromY = 0;
            if (!search && level > 0 && FindVisual(_path[level - 1]) is { } parentVisual)
            {
                fromX = parentVisual.X.Value;
                fromY = parentVisual.Y.Value;
            }

            UpdateRing(ring, ringLayout, animate, isNew, fromX, fromY, opening);
            wanted.Add(ring);
        }

        foreach (var old in _rings.Where(r => !wanted.Contains(r)).ToList())
        {
            RemoveRing(old, animate);
        }

        _rings.Clear();
        _rings.AddRange(wanted);

        UpdateExpandedStates();
        UpdateOrbGlyph();

        double target = DiscRadius();
        if (!opening)
        {
            _aDisc.Go(target, 200, 0, Easing.OutCubic);
        }

        RefreshHighlightAfterRebuild();
    }

    private double DiscRadius()
    {
        double outer = _layout?.OuterRadius ?? (_session!.Options.Radius + _session.Options.ItemSize / 2);
        double max = _session!.HalfExtent - 2;
        return Math.Min(max, outer + DiscPadding);
    }

    private RingView CreateRing(int level, string? parentId, bool search)
    {
        var ring = new RingView
        {
            Level = level,
            ParentId = parentId,
            IsSearch = search,
            Source = search ? null : ListAtLevel(level),
            Parent = search || level == 0 ? null : _path[level - 1],
        };
        ring.GuideAlpha = _anim.Create(0, v => ring.Guide.Opacity = v);
        RingLayer.Children.Add(ring.Layer);
        GuideLayer.Children.Add(ring.Guide);
        return ring;
    }

    private void RemoveRing(RingView ring, bool animate)
    {
        // the icons fly back into their group, then the layers are removed
        double toX = 0, toY = 0;
        if (!ring.IsSearch && ring.Level > 0 && ring.Parent != null && FindVisual(ring.Parent) is { } parentVisual)
        {
            toX = parentVisual.X.Value;
            toY = parentVisual.Y.Value;
        }

        if (!animate || !_anim.Enabled)
        {
            RingLayer.Children.Remove(ring.Layer);
            GuideLayer.Children.Remove(ring.Guide);
            return;
        }

        var all = ring.Visuals.ToList();
        if (ring.AddSlot != null)
        {
            all.Add(ring.AddSlot);
        }

        int i = 0;
        foreach (var visual in all)
        {
            double delay = Math.Min(i * 5, 40);
            visual.X.Go(toX, 140, delay, Easing.InCubic);
            visual.Y.Go(toY, 140, delay, Easing.InCubic);
            visual.Spawn.Go(0.3, 140, delay, Easing.InCubic);
            visual.Alpha.Go(0, 120, delay, Easing.Linear);
            i++;
        }

        ring.GuideAlpha?.Go(0, 100);

        // a timer built from an animated value: removes the layers when the icons have gone
        var timer = _anim.Create(0);
        timer.Go(1, 200, 0, Easing.Linear, () =>
        {
            RingLayer.Children.Remove(ring.Layer);
            GuideLayer.Children.Remove(ring.Guide);
        });
    }

    private void RemoveAllRings()
    {
        foreach (var ring in _rings)
        {
            RingLayer.Children.Remove(ring.Layer);
            GuideLayer.Children.Remove(ring.Guide);
        }

        _rings.Clear();
        _layout = null;
    }

    private void UpdateRing(RingView ring, RingLayout layout, bool animate, bool isNew, double fromX, double fromY, bool opening)
    {
        IReadOnlyList<RadialItem> items = ring.IsSearch
            ? _search.Matches.Take(layout.Slots.Length).ToList()
            : ring.Source!.ToList();

        // only items that really have a slot (the layout may have fewer slots than items in a search)
        int itemCount = Math.Min(items.Count, _edit && !ring.IsSearch ? layout.Slots.Length - 1 : layout.Slots.Length);

        var existing = new Dictionary<string, ItemVisual>();
        foreach (var visual in ring.Visuals)
        {
            if (visual.Item != null)
            {
                existing[visual.Item.Id] = visual;
            }
        }

        var next = new List<ItemVisual>(itemCount);
        double size = layout.ItemSize;

        for (int i = 0; i < itemCount; i++)
        {
            var item = items[i];
            var slot = layout.Slots[i];
            bool spawned = false;

            if (existing.TryGetValue(item.Id, out var visual))
            {
                existing.Remove(item.Id);
                if (Math.Abs(visual.Size - size) > 0.75)
                {
                    // the layout shrank / grew the icons: rebuild the visual at its current place
                    double cx = visual.X.Value, cy = visual.Y.Value;
                    ring.Layer.Children.Remove(visual.Root);
                    visual = CreateVisual(item, size, ring);
                    visual.SnapTo(cx, cy);
                    ring.Layer.Children.Add(visual.Root);
                }
            }
            else
            {
                visual = CreateVisual(item, size, ring);
                visual.SnapTo(fromX, fromY);
                visual.Spawn.Snap(0.3);
                visual.Alpha.Snap(0);
                ring.Layer.Children.Add(visual.Root);
                spawned = true;
            }

            visual.SlotX = slot.X;
            visual.SlotY = slot.Y;
            visual.SlotAngle = slot.AngleDeg;

            double delay = (isNew || spawned) ? Math.Min(i * (opening ? 12 : 14), 110) : 0;
            double duration = (isNew || spawned) ? 210 : 190;
            var ease = spawned ? (Func<double, double>)Easing.OutBack : Easing.OutCubic;
            visual.X.Go(slot.X, duration, delay, ease);
            visual.Y.Go(slot.Y, duration, delay, ease);
            visual.Spawn.Go(1, duration, delay, Easing.OutCubic);
            visual.Alpha.Go(1, 150, delay, Easing.Linear);
            next.Add(visual);
        }

        // items that disappeared from the list: shrink and remove
        foreach (var gone in existing.Values)
        {
            var layer = ring.Layer;
            gone.Spawn.Go(0.4, 140);
            gone.Alpha.Go(0, 130, 0, Easing.Linear, () => layer.Children.Remove(gone.Root));
        }

        ring.Visuals = next;

        // the "+" slot of the edit mode
        if (_edit && !ring.IsSearch && layout.Slots.Length > itemCount)
        {
            var slot = layout.Slots[itemCount];
            if (ring.AddSlot == null || Math.Abs(ring.AddSlot.Size - size) > 0.75)
            {
                if (ring.AddSlot != null)
                {
                    ring.Layer.Children.Remove(ring.AddSlot.Root);
                }

                var add = new ItemVisual(null, size, _palette, _anim);
                add.SetIcon(default);
                add.SetEditMode(true, false);
                add.SnapTo(fromX, fromY);
                add.Spawn.Snap(0.3);
                add.Alpha.Snap(0);
                ring.Layer.Children.Add(add.Root);
                ring.AddSlot = add;
            }

            var slotVisual = ring.AddSlot;
            slotVisual.SlotX = slot.X;
            slotVisual.SlotY = slot.Y;
            slotVisual.SlotAngle = slot.AngleDeg;
            double delay = isNew ? Math.Min(itemCount * 14, 120) : 0;
            slotVisual.X.Go(slot.X, 210, delay, Easing.OutBack);
            slotVisual.Y.Go(slot.Y, 210, delay, Easing.OutBack);
            slotVisual.Spawn.Go(1, 210, delay, Easing.OutCubic);
            slotVisual.Alpha.Go(0.9, 150, delay, Easing.Linear);
        }
        else if (ring.AddSlot != null)
        {
            var remove = ring.AddSlot;
            var layer = ring.Layer;
            ring.AddSlot = null;
            remove.Alpha.Go(0, 120, 0, Easing.Linear, () => layer.Children.Remove(remove.Root));
        }

        UpdateGuide(ring, layout);
        ring.Layout = layout;
    }

    private ItemVisual CreateVisual(RadialItem item, double size, RingView ring)
    {
        var visual = new ItemVisual(item, size, _palette, _anim);
        visual.ShowGroupMarker(item.IsGroup);
        visual.SetEditMode(_edit, false);

        double px = size * 0.62 * _session!.Scale;
        var icon = _icons.Get(item, Math.Max(px, 24), loaded => visual.SetIcon(loaded));
        visual.SetIcon(icon);
        return visual;
    }

    private void UpdateGuide(RingView ring, RingLayout layout)
    {
        if (!_config.Menu.ShowOrbitRings || layout.Slots.Length == 0)
        {
            ring.Guide.Data = null;
            return;
        }

        double r = layout.Radius;
        Geometry geometry;
        if (!layout.IsArc || layout.Slots.Length < 2)
        {
            geometry = layout.IsArc && layout.Slots.Length == 1
                ? ArcGeometry(r, layout.Slots[0].AngleDeg - 14, layout.Slots[0].AngleDeg + 14)
                : new EllipseGeometry(new Point(0, 0), r, r);
        }
        else
        {
            double center = layout.ArcCenterDeg;
            double first = RadialLayout.NormalizeDeg(layout.Slots[0].AngleDeg - center);
            double last = RadialLayout.NormalizeDeg(layout.Slots[^1].AngleDeg - center);
            double pad = RadialLayout.RadToDeg(layout.ItemSize / 2 / Math.Max(r, 1)) + 5;
            geometry = ArcGeometry(r, center + first - pad, center + last + pad);
        }

        geometry.Freeze();
        ring.Guide.Data = geometry;
        ring.Guide.Stroke = _palette.IsDark
            ? Palette.Frozen(Color.FromArgb(0x26, 255, 255, 255))
            : Palette.Frozen(Color.FromArgb(0x22, 0, 0, 0));
        ring.Guide.StrokeThickness = 1.2;
        ring.GuideAlpha?.Go(1, 220, 60);
    }

    private static Geometry ArcGeometry(double radius, double startDeg, double endDeg)
    {
        static Point At(double r, double deg) => new(r * Math.Cos(RadialLayout.DegToRad(deg)), r * Math.Sin(RadialLayout.DegToRad(deg)));

        var figure = new PathFigure { StartPoint = At(radius, startDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(radius, endDeg), new Size(radius, radius), 0, endDeg - startDeg > 180, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    private ItemVisual? FindVisual(RadialItem item)
    {
        foreach (var ring in _rings)
        {
            foreach (var visual in ring.Visuals)
            {
                if (ReferenceEquals(visual.Item, item))
                {
                    return visual;
                }
            }
        }

        return null;
    }

    private void UpdateExpandedStates()
    {
        foreach (var ring in _rings)
        {
            foreach (var visual in ring.Visuals)
            {
                bool expanded = visual.Item != null && visual.Item.IsGroup && _path.Contains(visual.Item);
                visual.SetExpanded(expanded);
                visual.SetArmed(ReferenceEquals(visual.Item, _armed));
            }
        }
    }

    private void UpdateOrbGlyph()
    {
        string glyph = _path.Count > 0 || _search.IsActive ? "back" : "close";
        _orb.SetGlyph(glyph, _palette, false);
    }

    // ---- backdrop ---------------------------------------------------------------

    private void BuildBackdrop()
    {
        _backdropRoot.RenderTransform = _backdropScale;
        _backdropRoot.Opacity = 0;
        _glassImage.Clip = _glassClip;
        RenderOptions.SetBitmapScalingMode(_glassImage, BitmapScalingMode.HighQuality);

        _backdropRoot.Children.Add(_shadowDisc);
        _backdropRoot.Children.Add(_glassImage);
        _backdropRoot.Children.Add(_tintDisc);
        _backdropRoot.Children.Add(_sheenDisc);
        _backdropRoot.Children.Add(_rimDisc);
        BackdropLayer.Children.Add(_backdropRoot);
    }

    private void ConfigureBackdrop()
    {
        var menu = _config.Menu;
        var session = _session!;
        double half = session.HalfExtent;

        _glassImage.Width = half * 2;
        _glassImage.Height = half * 2;
        Canvas.SetLeft(_glassImage, -half);
        Canvas.SetTop(_glassImage, -half);
        _glassClip.Center = new Point(half, half);

        bool glass = menu.BlurBackground && session.Backdrop != null;
        _glassImage.Source = glass ? session.Backdrop : null;
        _glassImage.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;

        // with a real blurred snapshot the tint can be lighter; without it the disc must stay readable
        double opacity = glass ? menu.BackdropOpacity : Math.Max(menu.BackdropOpacity, 0.55);
        _tintDisc.Fill = _palette.GlassTint(opacity);

        _sheenDisc.Fill = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.0),
            GradientOrigin = new Point(0.5, 0.0),
            RadiusX = 0.9,
            RadiusY = 0.9,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(_palette.IsDark ? (byte)0x1C : (byte)0x55, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
            },
        };

        _rimDisc.Stroke = _palette.IsDark
            ? Palette.Frozen(Color.FromArgb(0x38, 255, 255, 255))
            : Palette.Frozen(Color.FromArgb(0x30, 0, 0, 0));
        _rimDisc.StrokeThickness = 1.2;

        var shadow = new RadialGradientBrush();
        byte peak = _palette.IsDark ? (byte)0x88 : (byte)0x48;
        shadow.GradientStops.Add(new GradientStop(Color.FromArgb(peak, 0, 0, 0), 0.70));
        shadow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak * 0.45), 0, 0, 0), 0.86));
        shadow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
        shadow.Freeze();
        _shadowDisc.Fill = shadow;
    }

    private void UpdateDiscGeometry()
    {
        double r = Math.Max(1, _aDisc.Value);
        foreach (var ellipse in new[] { _tintDisc, _sheenDisc, _rimDisc })
        {
            ellipse.Width = r * 2;
            ellipse.Height = r * 2;
            ItemVisual.Place(ellipse, 0, 0);
        }

        double shadowR = r + 26;
        _shadowDisc.Width = shadowR * 2;
        _shadowDisc.Height = shadowR * 2;
        ItemVisual.Place(_shadowDisc, 0, 5);

        _glassClip.RadiusX = r;
        _glassClip.RadiusY = r;
    }
}
