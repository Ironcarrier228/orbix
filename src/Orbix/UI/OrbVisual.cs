using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Orbix.UI;

/// <summary>
/// The orb: a glossy sphere in the accent colour (or a custom picture clipped to a circle). While the menu is open
/// it becomes the central button and shows a "close" or "back" glyph. The "breathing" glow is a slow opacity
/// animation limited to 20 fps; it exists only while the option is on, otherwise the orb costs nothing.
/// </summary>
internal sealed class OrbVisual
{
    private readonly Animator _animator;
    private readonly Ellipse _body = new() { IsHitTestVisible = true };
    private readonly Ellipse _glow = new() { IsHitTestVisible = false };
    private readonly Ellipse _rim = new() { IsHitTestVisible = false };
    private readonly Ellipse _specular = new() { IsHitTestVisible = false };
    private readonly Canvas _glyphHost = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ScaleTransform _glowScale = new(1, 1);
    private string? _glyph;
    private bool _breathing;

    public OrbVisual(Animator animator)
    {
        _animator = animator;
        Root = new Canvas { Width = 0, Height = 0, IsHitTestVisible = true };
        Root.RenderTransform = _scale;

        Root.Children.Add(_body);
        Root.Children.Add(_glow);
        Root.Children.Add(_specular);
        Root.Children.Add(_rim);
        Root.Children.Add(_glyphHost);

        _glow.RenderTransform = _glowScale;
        _glow.RenderTransformOrigin = new Point(0.5, 0.5);

        Opacity = animator.Create(0.4, v => Root.Opacity = v);
        Pop = animator.Create(1, v =>
        {
            _scale.ScaleX = v;
            _scale.ScaleY = v;
        });
        GlyphAlpha = animator.Create(0, v => _glyphHost.Opacity = v);
        _glyphHost.Opacity = 0;
    }

    public Canvas Root { get; }

    public double Diameter { get; private set; }

    /// <summary>Opacity of the whole orb (resting / hover / menu).</summary>
    public AnimatedDouble Opacity { get; }

    /// <summary>Hover "pop" scale.</summary>
    public AnimatedDouble Pop { get; }

    public AnimatedDouble GlyphAlpha { get; }

    /// <summary>Builds the look of the orb. <paramref name="customImage"/> replaces the sphere gradient.</summary>
    public void Configure(double diameter, Palette palette, Color? color, ImageSource? customImage)
    {
        Diameter = diameter;
        var accent = color ?? palette.Accent;

        foreach (var ellipse in new[] { _body, _glow, _rim })
        {
            ellipse.Width = diameter;
            ellipse.Height = diameter;
            ItemVisual.Place(ellipse, 0, 0);
        }

        // sphere
        if (customImage != null)
        {
            var brush = new ImageBrush(customImage) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            _body.Fill = brush;
        }
        else
        {
            var sphere = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.36, 0.28),
                Center = new Point(0.44, 0.40),
                RadiusX = 0.78,
                RadiusY = 0.78,
            };
            sphere.GradientStops.Add(new GradientStop(Palette.Blend(accent, Colors.White, 0.42), 0.0));
            sphere.GradientStops.Add(new GradientStop(accent, 0.52));
            sphere.GradientStops.Add(new GradientStop(Palette.Blend(accent, Colors.Black, 0.38), 1.0));
            sphere.Freeze();
            _body.Fill = sphere;
        }

        // breathing glow: transparent in the middle, bright at the edge
        var glow = new RadialGradientBrush { RadiusX = 0.5, RadiusY = 0.5 };
        var glowColor = Palette.Blend(accent, Colors.White, 0.35);
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, glowColor.R, glowColor.G, glowColor.B), 0.0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, glowColor.R, glowColor.G, glowColor.B), 0.55));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, glowColor.R, glowColor.G, glowColor.B), 1.0));
        glow.Freeze();
        _glow.Fill = glow;

        // rim light
        var rimBrush = new LinearGradientBrush(
            Color.FromArgb(0x88, 255, 255, 255),
            Color.FromArgb(0x10, 255, 255, 255),
            new Point(0.2, 0),
            new Point(0.8, 1));
        rimBrush.Freeze();
        _rim.Stroke = rimBrush;
        _rim.StrokeThickness = Math.Max(1, diameter / 40);
        _rim.Width = diameter - _rim.StrokeThickness;
        _rim.Height = diameter - _rim.StrokeThickness;
        ItemVisual.Place(_rim, 0, 0);

        // specular highlight
        _specular.Width = diameter * 0.46;
        _specular.Height = diameter * 0.26;
        var spec = new RadialGradientBrush();
        spec.GradientStops.Add(new GradientStop(Color.FromArgb(0xA0, 255, 255, 255), 0.0));
        spec.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 1.0));
        spec.Freeze();
        _specular.Fill = spec;
        ItemVisual.Place(_specular, -diameter * 0.12, -diameter * 0.26);
        _specular.Visibility = customImage != null ? Visibility.Collapsed : Visibility.Visible;

        SetGlyph(_glyph, palette, false);
        ApplyBreathing();
    }

    public void SetBreathing(bool enabled)
    {
        _breathing = enabled;
        ApplyBreathing();
    }

    /// <summary>Shows a glyph above the sphere ("close", "back") or nothing (null).</summary>
    public void SetGlyph(string? name, Palette palette, bool animate)
    {
        if (name != _glyph || _glyphHost.Children.Count == 0)
        {
            _glyph = name;
            _glyphHost.Children.Clear();
            if (name != null)
            {
                double size = Math.Max(14, Diameter * 0.42);
                var shadow = Glyphs.CreatePath(name, Palette.Frozen(Color.FromArgb(0x60, 0, 0, 0)), size, 3.4);
                ItemVisual.Place(shadow, 0, 1);
                _glyphHost.Children.Add(shadow);
                var path = Glyphs.CreatePath(name, Brushes.White, size, 2.6);
                ItemVisual.Place(path, 0, 0);
                _glyphHost.Children.Add(path);
            }
        }

        if (animate)
        {
            GlyphAlpha.Go(name != null ? 1 : 0, 140);
        }
        else
        {
            GlyphAlpha.Snap(name != null ? 1 : 0);
        }
    }

    private void ApplyBreathing()
    {
        if (_breathing && Diameter > 0)
        {
            var opacity = new DoubleAnimation(0.05, 0.85, TimeSpan.FromMilliseconds(2300))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(opacity, 20);
            _glow.BeginAnimation(UIElement.OpacityProperty, opacity);

            var pulse = new DoubleAnimation(0.93, 1.0, TimeSpan.FromMilliseconds(2300))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(pulse, 20);
            _glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            _glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        else
        {
            _glow.BeginAnimation(UIElement.OpacityProperty, null);
            _glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _glow.Opacity = 0.0;
            _glowScale.ScaleX = 1;
            _glowScale.ScaleY = 1;
        }
    }
}
