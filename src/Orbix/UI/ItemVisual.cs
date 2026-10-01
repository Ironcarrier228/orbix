using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Orbix.Core.Models;
using Orbix.Services;

namespace Orbix.UI;

/// <summary>
/// Visual of one menu element (or of the "+" slot in edit mode): shadow, disc, icon, group marker and
/// edit badges. Coordinates are relative to the centre of the stage; all elements are centred on (0, 0) of
/// <see cref="Root"/>, which is moved and scaled with render transforms (no layout passes while animating).
/// </summary>
internal sealed class ItemVisual
{
    private const double HoverZoom = 0.14;

    private readonly Palette _palette;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new();
    private readonly Canvas _iconHost = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    private readonly Ellipse _hoverDisc;
    private readonly Ellipse _hoverRing;
    private readonly Ellipse _expandedRing;
    private readonly Ellipse _armedRing;
    private readonly Ellipse _dim;
    private readonly Canvas _groupBadge = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    private readonly Canvas _deleteBadge = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    private readonly Canvas _missingBadge = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    private readonly Ellipse _editRing;

    public ItemVisual(RadialItem? item, double size, Palette palette, Animator animator)
    {
        Item = item;
        Size = size;
        _palette = palette;

        Root = new Canvas { Width = 0, Height = 0, IsHitTestVisible = false };
        Root.RenderTransform = new TransformGroup { Children = { _scale, _translate } };

        // soft shadow: a radial gradient is much cheaper than a blur effect and looks the same
        var shadow = new Ellipse
        {
            Width = size * 1.34,
            Height = size * 1.34,
            Fill = ShadowFor(palette),
            IsHitTestVisible = false,
        };
        Place(shadow, 0, size * 0.09);
        Root.Children.Add(shadow);

        Disc = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = palette.ItemFill,
            Stroke = palette.ItemBorder,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Place(Disc, 0, 0);
        Root.Children.Add(Disc);

        _hoverDisc = new Ellipse { Width = size, Height = size, Fill = palette.ItemFillHover, Opacity = 0, IsHitTestVisible = false };
        Place(_hoverDisc, 0, 0);
        Root.Children.Add(_hoverDisc);

        Place(_iconHost, 0, 0);
        Root.Children.Add(_iconHost);

        _dim = new Ellipse { Width = size, Height = size, Fill = Palette.Frozen(Color.FromArgb(0xA0, palette.GlassColor.R, palette.GlassColor.G, palette.GlassColor.B)), Opacity = 0, IsHitTestVisible = false };
        Place(_dim, 0, 0);
        Root.Children.Add(_dim);

        _expandedRing = new Ellipse { Width = size + 4, Height = size + 4, Stroke = palette.AccentBrush, StrokeThickness = 2.2, Opacity = 0, IsHitTestVisible = false };
        Place(_expandedRing, 0, 0);
        Root.Children.Add(_expandedRing);

        _hoverRing = new Ellipse { Width = size + 2, Height = size + 2, Stroke = palette.AccentBrush, StrokeThickness = 2, Opacity = 0, IsHitTestVisible = false };
        Place(_hoverRing, 0, 0);
        Root.Children.Add(_hoverRing);

        _armedRing = new Ellipse { Width = size + 4, Height = size + 4, Stroke = palette.Danger, StrokeThickness = 2.6, Opacity = 0, IsHitTestVisible = false };
        Place(_armedRing, 0, 0);
        Root.Children.Add(_armedRing);

        _editRing = new Ellipse
        {
            Width = size + 7,
            Height = size + 7,
            Stroke = palette.AccentBrush,
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 2.2, 2.6 },
            Opacity = 0,
            IsHitTestVisible = false,
        };
        Place(_editRing, 0, 0);
        Root.Children.Add(_editRing);

        BuildBadges();

        // animated properties
        X = animator.Create(0, _ => ApplyTransform());
        Y = animator.Create(0, _ => ApplyTransform());
        Spawn = animator.Create(1, _ => ApplyTransform());
        Alpha = animator.Create(1, v => Root.Opacity = v);
        Hover = animator.Create(0, v =>
        {
            _hoverDisc.Opacity = v;
            _hoverRing.Opacity = v;
            ApplyTransform();
        });
        Expanded = animator.Create(0, v => _expandedRing.Opacity = v);
        Armed = animator.Create(0, v => _armedRing.Opacity = v);
        Dimmed = animator.Create(0, v => _dim.Opacity = v);
        EditAmount = animator.Create(0, v =>
        {
            _editRing.Opacity = v * 0.85;
            _deleteBadge.Opacity = v;
        });
    }

    public RadialItem? Item { get; }

    public bool IsAddSlot => Item == null;

    public double Size { get; }

    public Canvas Root { get; }

    public Ellipse Disc { get; }

    public AnimatedDouble X { get; }

    public AnimatedDouble Y { get; }

    /// <summary>Spawn / despawn scale (0.3 .. 1).</summary>
    public AnimatedDouble Spawn { get; }

    public AnimatedDouble Alpha { get; }

    public AnimatedDouble Hover { get; }

    public AnimatedDouble Expanded { get; }

    public AnimatedDouble Armed { get; }

    public AnimatedDouble Dimmed { get; }

    public AnimatedDouble EditAmount { get; }

    /// <summary>Slot position the visual is heading to (stage coordinates).</summary>
    public double SlotX { get; set; }

    public double SlotY { get; set; }

    public double SlotAngle { get; set; }

    /// <summary>Distance of the delete badge centre from the visual centre.</summary>
    public (double X, double Y, double Radius) DeleteBadgeGeometry => (Size * 0.37, -Size * 0.37, Math.Max(8, Size * 0.17));

    public void SetEditMode(bool edit, bool animate)
    {
        bool showDelete = edit && !IsAddSlot;
        _deleteBadge.Visibility = showDelete ? Visibility.Visible : Visibility.Collapsed;
        if (animate)
        {
            EditAmount.Go(edit ? 1 : 0, 160);
        }
        else
        {
            EditAmount.Snap(edit ? 1 : 0);
        }
    }

    public void SetExpanded(bool expanded) => Expanded.Go(expanded ? 1 : 0, 120);

    public void SetArmed(bool armed) => Armed.Go(armed ? 1 : 0, 120);

    public void SetDimmed(bool dimmed) => Dimmed.Go(dimmed ? 1 : 0, 120);

    public void SetHover(bool hover) => Hover.Go(hover ? 1 : 0, 110);

    public void ShowGroupMarker(bool show) => _groupBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Puts the icon into the disc: built-in glyph, bitmap or placeholder.</summary>
    public void SetIcon(IconResult icon)
    {
        _iconHost.Children.Clear();

        if (IsAddSlot)
        {
            var plus = Glyphs.CreatePath("plus", _palette.AccentBrush, Size * 0.46, 2.0);
            Place(plus, 0, 0);
            _iconHost.Children.Add(plus);
            return;
        }

        string? glyph = icon.GlyphName;
        if (glyph == null && icon.Image == null && Item != null)
        {
            glyph = Glyphs.NameFor(Item);
        }

        if (glyph != null)
        {
            double glyphSize = Size * (Item is { Kind: ItemKind.Group } ? 0.5 : 0.48);
            Brush brush = Item is { Kind: ItemKind.Group } ? _palette.AccentBrush : _palette.Glyph;
            if (!string.IsNullOrEmpty(Item?.Color) && ConfigNormalizerColor(Item!.Color!, out var custom))
            {
                brush = Palette.Frozen(custom);
            }

            var path = Glyphs.CreatePath(glyph, brush, glyphSize, 1.9);
            Place(path, 0, 0);
            _iconHost.Children.Add(path);
        }
        else if (icon.Image != null)
        {
            double imageSize = Size * 0.62;
            var image = new Image
            {
                Source = icon.Image,
                Width = imageSize,
                Height = imageSize,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Place(image, 0, 0);
            _iconHost.Children.Add(image);
        }

        _missingBadge.Visibility = icon.TargetMissing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Immediate position (no animation).</summary>
    public void SnapTo(double x, double y)
    {
        X.Snap(x);
        Y.Snap(y);
    }

    private static bool ConfigNormalizerColor(string text, out Color color)
    {
        color = default;
        if (!Orbix.Core.Services.ConfigNormalizer.IsValidColor(text))
        {
            return false;
        }

        color = ThemeService.ParseColor(text, Colors.White);
        return true;
    }

    private void ApplyTransform()
    {
        double s = Spawn.Value * (1 + HoverZoom * Hover.Value);
        _scale.ScaleX = s;
        _scale.ScaleY = s;
        _translate.X = X.Value;
        _translate.Y = Y.Value;
    }

    private void BuildBadges()
    {
        double s = Size;

        // group marker: a small accent circle with a chevron, at the lower right
        double gr = Math.Max(7, s * 0.16);
        var groupCircle = new Ellipse { Width = gr * 2, Height = gr * 2, Fill = _palette.AccentBrush, IsHitTestVisible = false };
        Place(groupCircle, 0, 0);
        _groupBadge.Children.Add(groupCircle);
        var chevron = Glyphs.CreatePath("chevron", Palette.Frozen(_palette.AccentText), gr * 1.25, 2.6);
        Place(chevron, 0.4, 0);
        _groupBadge.Children.Add(chevron);
        Place(_groupBadge, s * 0.36, s * 0.36);
        _groupBadge.Visibility = Visibility.Collapsed;
        Root.Children.Add(_groupBadge);

        // delete badge (edit mode)
        var (bx, by, br) = DeleteBadgeGeometry;
        var deleteCircle = new Ellipse
        {
            Width = br * 2,
            Height = br * 2,
            Fill = _palette.Danger,
            Stroke = Brushes.White,
            StrokeThickness = 1.4,
            IsHitTestVisible = false,
        };
        Place(deleteCircle, 0, 0);
        _deleteBadge.Children.Add(deleteCircle);
        var cross = Glyphs.CreatePath("close", Brushes.White, br * 1.3, 2.8);
        Place(cross, 0, 0);
        _deleteBadge.Children.Add(cross);
        Place(_deleteBadge, bx, by);
        _deleteBadge.Visibility = Visibility.Collapsed;
        _deleteBadge.Opacity = 0;
        Root.Children.Add(_deleteBadge);

        // "target not found" marker: red dot with an exclamation mark, upper left
        double mr = Math.Max(6, s * 0.13);
        var missing = new Ellipse { Width = mr * 2, Height = mr * 2, Fill = _palette.Danger, IsHitTestVisible = false };
        Place(missing, 0, 0);
        _missingBadge.Children.Add(missing);
        var bang = Glyphs.CreatePath("warning", Brushes.White, mr * 1.25, 2.2);
        Place(bang, 0, 0.3);
        _missingBadge.Children.Add(bang);
        Place(_missingBadge, -s * 0.36, -s * 0.36);
        _missingBadge.Visibility = Visibility.Collapsed;
        Root.Children.Add(_missingBadge);
    }

    private static Brush ShadowFor(Palette palette)
    {
        byte peak = palette.IsDark ? (byte)0x70 : (byte)0x40;
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(peak, 0, 0, 0), 0.0),
                new GradientStop(Color.FromArgb((byte)(peak * 0.55), 0, 0, 0), 0.62),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0),
            },
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Centres an element on the given point of its parent canvas.</summary>
    internal static void Place(FrameworkElement element, double cx, double cy)
    {
        double w = double.IsNaN(element.Width) ? 0 : element.Width;
        double h = double.IsNaN(element.Height) ? 0 : element.Height;
        Canvas.SetLeft(element, cx - w / 2);
        Canvas.SetTop(element, cy - h / 2);
    }
}
