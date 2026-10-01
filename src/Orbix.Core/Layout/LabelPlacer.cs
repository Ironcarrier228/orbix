namespace Orbix.Core.Layout;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Distance from a point to the rectangle (0 when inside).</summary>
    public double DistanceTo(double px, double py)
    {
        double dx = Math.Max(Math.Max(X - px, 0), px - Right);
        double dy = Math.Max(Math.Max(Y - py, 0), py - Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Chooses where to show the tooltip of a hovered item: next to the item, outside of the ring when possible,
/// never overlapping the item and never leaving the window.
/// Coordinates are relative to the centre of the window.
/// </summary>
public static class LabelPlacer
{
    public static RectD Place(
        double itemX,
        double itemY,
        double itemRadius,
        double angleDeg,
        double labelWidth,
        double labelHeight,
        double halfWidth,
        double halfHeight,
        double gap = 8)
    {
        double a = RadialLayout.DegToRad(angleDeg);
        double dx = Math.Cos(a);
        double dy = Math.Sin(a);

        // Preferred side = the dominant direction of the item from the centre.
        var sides = Math.Abs(dx) >= Math.Abs(dy)
            ? new[] { dx >= 0 ? Side.Right : Side.Left, dx >= 0 ? Side.Left : Side.Right, dy >= 0 ? Side.Bottom : Side.Top, dy >= 0 ? Side.Top : Side.Bottom }
            : new[] { dy >= 0 ? Side.Bottom : Side.Top, dy >= 0 ? Side.Top : Side.Bottom, dx >= 0 ? Side.Right : Side.Left, dx >= 0 ? Side.Left : Side.Right };

        RectD? fallback = null;
        foreach (var side in sides)
        {
            var rect = Candidate(side, itemX, itemY, itemRadius, labelWidth, labelHeight, gap);
            rect = Clamp(rect, halfWidth, halfHeight);

            bool overlapsItem = rect.DistanceTo(itemX, itemY) < itemRadius + 2;
            if (!overlapsItem)
            {
                return rect;
            }

            fallback ??= rect;
        }

        return fallback ?? new RectD(itemX - labelWidth / 2, itemY + itemRadius + gap, labelWidth, labelHeight);
    }

    private enum Side
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    private static RectD Candidate(Side side, double x, double y, double r, double w, double h, double gap)
    {
        return side switch
        {
            Side.Right => new RectD(x + r + gap, y - h / 2, w, h),
            Side.Left => new RectD(x - r - gap - w, y - h / 2, w, h),
            Side.Top => new RectD(x - w / 2, y - r - gap - h, w, h),
            _ => new RectD(x - w / 2, y + r + gap, w, h),
        };
    }

    private static RectD Clamp(RectD rect, double halfWidth, double halfHeight)
    {
        double x = Math.Min(Math.Max(rect.X, -halfWidth), Math.Max(-halfWidth, halfWidth - rect.Width));
        double y = Math.Min(Math.Max(rect.Y, -halfHeight), Math.Max(-halfHeight, halfHeight - rect.Height));
        return new RectD(x, y, rect.Width, rect.Height);
    }
}
