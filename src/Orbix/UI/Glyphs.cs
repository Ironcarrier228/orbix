using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Orbix.Core.Models;

namespace Orbix.UI;

/// <summary>
/// Built-in vector icons (24x24 grid, drawn with a round pen). They do not depend on any installed icon font
/// and stay sharp at every DPI. Also generates the letter tiles used as placeholder for unknown applications.
/// </summary>
internal static class Glyphs
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["lock"] = "M6,11 H18 V20 H6 Z M8.5,11 V8 A3.5,3.5 0 0 1 15.5,8 V11 M12,14.5 V16.5",
        ["sleep"] = "M19.5,14.5 A8,8 0 1 1 9.5,4.5 A6.5,6.5 0 0 0 19.5,14.5 Z",
        ["restart"] = "M19.5,12 A7.5,7.5 0 1 1 17.3,6.7 M19.5,4.5 V8.8 H15.2",
        ["power"] = "M12,3.5 V11.5 M7,6.8 A7.5,7.5 0 1 0 17,6.8",
        ["signout"] = "M10,4.5 H5.5 V19.5 H10 M9.5,12 H19.5 M16,8.5 L19.5,12 L16,15.5",
        ["screenshot"] = "M3.5,8 H7 L8.5,5.8 H15.5 L17,8 H20.5 V18.5 H3.5 Z M12,10.3 A3.3,3.3 0 1 0 12.01,10.3",
        ["desktop"] = "M3.5,5 H20.5 V15.5 H3.5 Z M9,19.5 H15 M12,15.5 V19.5",
        ["taskman"] = "M4,4 V20 H20 M8.5,16 V12 M12.5,16 V8 M16.5,16 V11",
        ["settings"] = "M12,8.6 A3.4,3.4 0 1 0 12.01,8.6 M12,2.8 V5.4 M12,18.6 V21.2 M2.8,12 H5.4 M18.6,12 H21.2 M5.5,5.5 L7.4,7.4 M16.6,16.6 L18.5,18.5 M5.5,18.5 L7.4,16.6 M16.6,7.4 L18.5,5.5",
        ["folder"] = "M3.5,7 A1.8,1.8 0 0 1 5.3,5.2 H9.5 L11.5,7.4 H18.7 A1.8,1.8 0 0 1 20.5,9.2 V17.7 A1.8,1.8 0 0 1 18.7,19.5 H5.3 A1.8,1.8 0 0 1 3.5,17.7 Z",
        ["plus"] = "M12,5 V19 M5,12 H19",
        ["back"] = "M14.5,5 L7.5,12 L14.5,19",
        ["close"] = "M6,6 L18,18 M18,6 L6,18",
        ["pencil"] = "M4.5,19.5 L5.6,15.2 L15.8,5 L19,8.2 L8.8,18.4 Z M13.6,7.2 L16.8,10.4",
        ["trash"] = "M4.5,7 H19.5 M9,7 V4.8 H15 V7 M6.5,7 L7.5,19.5 H16.5 L17.5,7 M10.3,11 V16 M13.7,11 V16",
        ["globe"] = "M3.5,12 A8.5,8.5 0 1 0 20.5,12 A8.5,8.5 0 1 0 3.5,12 M3.5,12 H20.5 M12,3.5 C15.5,7 15.5,17 12,20.5 C8.5,17 8.5,7 12,3.5",
        ["terminal"] = "M3.5,5 H20.5 V19 H3.5 Z M7,9.8 L10,12.3 L7,14.8 M12,15 H17",
        ["app"] = "M5,5 H19 V19 H5 Z M5,9 H19",
        ["chevron"] = "M9.5,5.5 L16,12 L9.5,18.5",
        ["check"] = "M5,12.5 L10,17.5 L19,7",
        ["search"] = "M10.5,4 A6.5,6.5 0 1 0 10.51,4 M15.2,15.2 L20,20",
        ["hibernate"] = "M12,4 V14 M8,10.5 L12,14.5 L16,10.5 M5.5,19 H18.5",
        ["warning"] = "M12,4.5 L21,19.5 H3 Z M12,10 V14.2 M12,16.6 V16.7",
        ["drag"] = "M9,6 V6.1 M15,6 V6.1 M9,12 V12.1 M15,12 V12.1 M9,18 V18.1 M15,18 V18.1",
        ["image"] = "M4,5.5 H20 V18.5 H4 Z M4,16 L9,11 L13,15 L15.5,12.5 L20,17",
        ["file"] = "M6.5,3.5 H13.5 L18,8 V20.5 H6.5 Z M13.5,3.5 V8 H18",
        ["link"] = "M10,14 A3.5,3.5 0 0 0 15,14 L18,11 A3.5,3.5 0 0 0 13,6 L12,7 M14,10 A3.5,3.5 0 0 0 9,10 L6,13 A3.5,3.5 0 0 0 11,18 L12,17",
    };

    private static readonly Dictionary<string, Geometry> GeometryCache = new();
    private static readonly Color[] TileColors =
    {
        Color.FromRgb(0x5B, 0x8D, 0xEF), Color.FromRgb(0x9B, 0x6B, 0xE8), Color.FromRgb(0xE0, 0x6C, 0x9F), Color.FromRgb(0xEF, 0x8A, 0x4B),
        Color.FromRgb(0x3F, 0xB9, 0x8F), Color.FromRgb(0x4F, 0xB3, 0xD9), Color.FromRgb(0xD9, 0x5B, 0x5B), Color.FromRgb(0x8A, 0x9B, 0x3F),
    };

    public static Geometry Geo(string name)
    {
        lock (GeometryCache)
        {
            if (!GeometryCache.TryGetValue(name, out var geometry))
            {
                geometry = System.Windows.Media.Geometry.Parse(Paths.TryGetValue(name, out var data) ? data : Paths["app"]);
                geometry.Freeze();
                GeometryCache[name] = geometry;
            }

            return geometry;
        }
    }

    /// <summary>A path ready to be placed in the visual tree: 24x24 layout box, rendered scaled to <paramref name="size"/> around its centre.</summary>
    public static System.Windows.Shapes.Path CreatePath(string name, Brush stroke, double size, double thickness = 1.8)
    {
        return new System.Windows.Shapes.Path
        {
            Data = Geo(name),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
            Stretch = Stretch.None,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(size / 24.0, size / 24.0),
            IsHitTestVisible = false,
        };
    }

    /// <summary>Vector image of a glyph (for Image.Source).</summary>
    public static DrawingImage Vector(string name, Brush stroke, double thickness = 1.8)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var pen = new Pen(stroke, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        group.Children.Add(new GeometryDrawing(null, pen, Geo(name)));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Name of the built-in glyph of an item that has no shell icon.</summary>
    public static string NameFor(RadialItem item)
    {
        switch (item.Kind)
        {
            case ItemKind.Group:
                return "folder";
            case ItemKind.Url:
                return item.Target != null && item.Target.StartsWith("ms-settings", StringComparison.OrdinalIgnoreCase) ? "settings" : "globe";
            case ItemKind.Command:
                return "terminal";
            case ItemKind.System:
                return item.SystemAction switch
                {
                    SystemActionKind.Lock => "lock",
                    SystemActionKind.Sleep => "sleep",
                    SystemActionKind.Hibernate => "hibernate",
                    SystemActionKind.SignOut => "signout",
                    SystemActionKind.Restart => "restart",
                    SystemActionKind.Shutdown => "power",
                    SystemActionKind.Screenshot => "screenshot",
                    SystemActionKind.ShowDesktop => "desktop",
                    SystemActionKind.TaskManager => "taskman",
                    SystemActionKind.OrbixSettings => "settings",
                    _ => "power",
                };
            default:
                return "app";
        }
    }

    /// <summary>Colourful rounded tile with the first letter of the name: the placeholder for a missing / unreadable icon.</summary>
    public static DrawingImage LetterTile(string name, bool missing)
    {
        string letter = string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
        int hash = 17;
        foreach (char c in name ?? string.Empty)
        {
            hash = unchecked(hash * 31 + c);
        }

        var color = TileColors[(hash & 0x7FFFFFFF) % TileColors.Length];
        if (missing)
        {
            color = Palette.Blend(color, Color.FromRgb(0x80, 0x80, 0x88), 0.65);
        }

        var group = new DrawingGroup();
        var fill = new LinearGradientBrush(
            Palette.Blend(color, Colors.White, 0.22),
            Palette.Blend(color, Colors.Black, 0.18),
            new Point(0, 0),
            new Point(1, 1));
        fill.Freeze();
        group.Children.Add(new GeometryDrawing(fill, null, new RectangleGeometry(new Rect(1.5, 1.5, 21, 21), 5.5, 5.5)));

        var text = new FormattedText(
            letter,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            13.5,
            Brushes.White,
            1.0);
        var geometry = text.BuildGeometry(new Point(12 - text.Width / 2, 12.3 - text.Height / 2));
        geometry.Freeze();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, geometry));

        if (missing)
        {
            var pen = new Pen(Brushes.White, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), null, new EllipseGeometry(new Point(18.5, 18.5), 5, 5)));
            group.Children.Add(new GeometryDrawing(null, pen, System.Windows.Media.Geometry.Parse("M18.5,16.2 V18.9 M18.5,20.7 V20.8")));
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
