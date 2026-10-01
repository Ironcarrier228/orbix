using System.Windows.Media;

namespace Orbix.UI;

/// <summary>
/// Colours of the current theme (dark / light) and accent. Frozen brushes, created once per theme change.
/// The overlay is custom-drawn and takes its colours from here; the settings window uses the same values
/// through the brush resources published by <c>ThemeService</c>.
/// </summary>
internal sealed class Palette
{
    private Palette(bool dark, Color accent)
    {
        IsDark = dark;
        Accent = accent;
        AccentHover = Blend(accent, Colors.White, dark ? 0.18 : 0.0);
        if (!dark)
        {
            AccentHover = Blend(accent, Colors.Black, 0.12);
        }

        AccentText = Luminance(accent) > 0.62 ? Color.FromRgb(0x14, 0x14, 0x1A) : Colors.White;

        if (dark)
        {
            WindowBg = Color.FromRgb(0x1A, 0x1A, 0x23);
            Surface = Color.FromRgb(0x24, 0x24, 0x30);
            SurfaceHi = Color.FromRgb(0x30, 0x30, 0x40);
            Line = Color.FromRgb(0x3C, 0x3C, 0x4E);
            TextColor = Color.FromRgb(0xF3, 0xF3, 0xF8);
            MutedColor = Color.FromRgb(0xA4, 0xA4, 0xB8);
            GlassColor = Color.FromRgb(0x14, 0x14, 0x1C);
            ItemFillColor = Color.FromArgb(0xEB, 0x2B, 0x2B, 0x3A);
            ItemFillHoverColor = Color.FromArgb(0xF5, 0x3A, 0x3A, 0x50);
            ItemBorderColor = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
            PanelColor = Color.FromArgb(0xF4, 0x1E, 0x1E, 0x28);
        }
        else
        {
            WindowBg = Color.FromRgb(0xF3, 0xF3, 0xF8);
            Surface = Color.FromRgb(0xFF, 0xFF, 0xFF);
            SurfaceHi = Color.FromRgb(0xEA, 0xEA, 0xF3);
            Line = Color.FromRgb(0xD6, 0xD6, 0xE2);
            TextColor = Color.FromRgb(0x1C, 0x1C, 0x24);
            MutedColor = Color.FromRgb(0x66, 0x66, 0x78);
            GlassColor = Color.FromRgb(0xF2, 0xF2, 0xF9);
            ItemFillColor = Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF);
            ItemFillHoverColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            ItemBorderColor = Color.FromArgb(0x24, 0x00, 0x00, 0x00);
            PanelColor = Color.FromArgb(0xF7, 0xFA, 0xFA, 0xFD);
        }

        ItemFill = Frozen(ItemFillColor);
        ItemFillHover = Frozen(ItemFillHoverColor);
        ItemBorder = Frozen(ItemBorderColor);
        TextBrush = Frozen(TextColor);
        MutedBrush = Frozen(MutedColor);
        PanelBrush = Frozen(PanelColor);
        PanelBorder = Frozen(Color.FromArgb(dark ? (byte)0x30 : (byte)0x28, dark ? (byte)0xFF : (byte)0x00, dark ? (byte)0xFF : (byte)0x00, dark ? (byte)0xFF : (byte)0x00));
        AccentBrush = Frozen(accent);
        AccentSoft = Frozen(Color.FromArgb(0x55, accent.R, accent.G, accent.B));
        AccentText_ = Frozen(AccentText);
        Danger = Frozen(Color.FromRgb(0xE5, 0x48, 0x4D));
        Hit = Frozen(Color.FromArgb(0x01, 0x00, 0x00, 0x00));
        Glyph = Frozen(dark ? Color.FromRgb(0xE8, 0xE8, 0xF2) : Color.FromRgb(0x2A, 0x2A, 0x36));
        Shadow = Frozen(Color.FromArgb(dark ? (byte)0x90 : (byte)0x55, 0, 0, 0));
    }

    public bool IsDark { get; }

    public Color Accent { get; }

    public Color AccentHover { get; }

    public Color AccentText { get; }

    public Color WindowBg { get; }

    public Color Surface { get; }

    public Color SurfaceHi { get; }

    public Color Line { get; }

    public Color TextColor { get; }

    public Color MutedColor { get; }

    /// <summary>Base colour of the frosted-glass tint (alpha is applied from the settings).</summary>
    public Color GlassColor { get; }

    public Color ItemFillColor { get; }

    public Color ItemFillHoverColor { get; }

    public Color ItemBorderColor { get; }

    public Color PanelColor { get; }

    public Brush ItemFill { get; }

    public Brush ItemFillHover { get; }

    public Brush ItemBorder { get; }

    public Brush TextBrush { get; }

    public Brush MutedBrush { get; }

    public Brush PanelBrush { get; }

    public Brush PanelBorder { get; }

    public Brush AccentBrush { get; }

    public Brush AccentSoft { get; }

    // ReSharper disable once InconsistentNaming
    public Brush AccentText_ { get; }

    public Brush Danger { get; }

    /// <summary>Practically invisible fill (alpha 1/255) that still makes a region hit-testable in a layered window.</summary>
    public Brush Hit { get; }

    /// <summary>Colour of the monochrome vector glyphs.</summary>
    public Brush Glyph { get; }

    public Brush Shadow { get; }

    public static Palette Create(bool dark, Color accent) => new(dark, accent);

    public Brush GlassTint(double opacity)
    {
        byte a = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        return Frozen(Color.FromArgb(a, GlassColor.R, GlassColor.G, GlassColor.B));
    }

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}
