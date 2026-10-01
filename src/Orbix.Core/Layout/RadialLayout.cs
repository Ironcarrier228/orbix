namespace Orbix.Core.Layout;

/// <summary>
/// Description of one orbit to lay out.
/// <see cref="Count"/> is the number of slots (items plus optional "add" slot).
/// <see cref="ParentSlot"/> is the index - inside the PREVIOUS orbit - of the group that owns this orbit
/// (-1 for the first orbit).
/// </summary>
public readonly record struct RingSpec(int Count, int ParentSlot);

/// <summary>Tuning parameters of the layout. All distances are in device independent pixels.</summary>
public sealed record LayoutOptions
{
    /// <summary>Radius of the central orb.</summary>
    public double OrbRadius { get; init; } = 28;

    /// <summary>Diameter of an item.</summary>
    public double ItemSize { get; init; } = 52;

    /// <summary>Radius of the first orbit (distance from the centre to the centres of items).</summary>
    public double Radius { get; init; } = 128;

    /// <summary>Minimal clearance between two neighbouring items and between two orbits.</summary>
    public double Gap { get; init; } = 10;

    /// <summary>Angle of the first item of a full-circle orbit, degrees, clockwise from +X (-90 = top).</summary>
    public double StartAngleDeg { get; init; } = -90;

    /// <summary>How many orbits are shown at once; if a path is deeper, the inner orbits are hidden.</summary>
    public int MaxVisibleOrbits { get; init; } = 3;

    /// <summary>Upper limit of the distance from the centre to an item centre (window half size minus margins).</summary>
    public double MaxCenterRadius { get; init; } = double.PositiveInfinity;

    /// <summary>Items are never shrunk below this size.</summary>
    public double MinItemSize { get; init; } = 28;

    /// <summary>Maximal angular span of a child fan (degrees). Larger groups grow the orbit radius.</summary>
    public double MaxArcDeg { get; init; } = 300;
}

/// <summary>Position of a single slot. Coordinates are relative to the centre of the orb (Y grows downwards).</summary>
public readonly record struct SlotPosition(double AngleDeg, double X, double Y);

/// <summary>Laid out orbit.</summary>
public sealed class RingLayout
{
    public RingLayout(int level, int orbitIndex, double radius, double itemSize, bool isArc, double arcCenterDeg, SlotPosition[] slots)
    {
        Level = level;
        OrbitIndex = orbitIndex;
        Radius = radius;
        ItemSize = itemSize;
        IsArc = isArc;
        ArcCenterDeg = arcCenterDeg;
        Slots = slots;
    }

    /// <summary>Depth level in the tree (0 = root).</summary>
    public int Level { get; }

    /// <summary>Index of the orbit among the VISIBLE ones (0 = innermost).</summary>
    public int OrbitIndex { get; }

    /// <summary>Distance from the centre to the centres of the items.</summary>
    public double Radius { get; }

    /// <summary>Actual diameter of the items (may be shrunk when an orbit is crowded).</summary>
    public double ItemSize { get; }

    /// <summary>True for a fan around the parent group, false for a full circle.</summary>
    public bool IsArc { get; }

    /// <summary>For arcs: the angle of the parent group.</summary>
    public double ArcCenterDeg { get; }

    public SlotPosition[] Slots { get; }

    /// <summary>Distance from the centre to the outer edge of the items.</summary>
    public double OuterRadius => Radius + ItemSize / 2;
}

/// <summary>Result of <see cref="RadialLayout.Compute"/>.</summary>
public sealed class MenuLayout
{
    public MenuLayout(IReadOnlyList<RingLayout> rings, int firstVisibleLevel, double outerRadius)
    {
        Rings = rings;
        FirstVisibleLevel = firstVisibleLevel;
        OuterRadius = outerRadius;
    }

    public IReadOnlyList<RingLayout> Rings { get; }

    /// <summary>Level of the first visible ring (greater than zero when inner orbits are hidden).</summary>
    public int FirstVisibleLevel { get; }

    /// <summary>Distance from the centre to the farthest edge of any item.</summary>
    public double OuterRadius { get; }
}

/// <summary>
/// Pure maths of the radial menu.
///
/// * The first visible orbit is a full circle: items are spread evenly starting at <see cref="LayoutOptions.StartAngleDeg"/>.
/// * Every next orbit belongs to the expanded group of the previous one: its items are placed on a bigger circle,
///   as a fan centred on the angle of that group ("the second orbit opens around the group").
/// * Crowded orbits first grow their radius; if the available space is exhausted the items are shrunk.
/// </summary>
public static class RadialLayout
{
    public static MenuLayout Compute(IReadOnlyList<RingSpec> specs, LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(options);

        int maxOrbits = Math.Max(1, options.MaxVisibleOrbits);
        int skip = Math.Max(0, specs.Count - maxOrbits);

        var rings = new List<RingLayout>(specs.Count - skip);
        double prevRadius = 0;
        double prevSize = options.ItemSize;
        double parentAngle = double.NaN;
        double outer = options.OrbRadius;

        for (int j = 0; j < specs.Count - skip; j++)
        {
            var spec = specs[skip + j];
            int n = Math.Max(0, spec.Count);
            double size = options.ItemSize;
            bool arc = j > 0 && !double.IsNaN(parentAngle);
            double radius;
            SlotPosition[] slots;

            if (!arc)
            {
                radius = j == 0
                    ? Math.Max(options.Radius, options.OrbRadius + size / 2 + options.Gap)
                    : prevRadius + (prevSize + size) / 2 + options.Gap;

                if (n >= 2)
                {
                    radius = Math.Max(radius, (size + options.Gap) / (2 * Math.Sin(Math.PI / n)));
                }

                if (radius > options.MaxCenterRadius)
                {
                    radius = Math.Max(options.MaxCenterRadius, 1);
                    if (n >= 2)
                    {
                        double chord = 2 * radius * Math.Sin(Math.PI / n);
                        size = Math.Clamp(chord - options.Gap, options.MinItemSize, options.ItemSize);
                    }
                }

                slots = new SlotPosition[n];
                double step = n > 0 ? 360.0 / n : 0;
                for (int i = 0; i < n; i++)
                {
                    slots[i] = Polar(options.StartAngleDeg + i * step, radius);
                }

                rings.Add(new RingLayout(skip + j, j, radius, size, false, double.NaN, slots));
            }
            else
            {
                radius = prevRadius + (prevSize + size) / 2 + options.Gap;
                double maxSpan = DegToRad(Math.Clamp(options.MaxArcDeg, 30, 350));

                if (n >= 2)
                {
                    double minStep = StepFor(size, options.Gap, radius);
                    if ((n - 1) * minStep > maxSpan)
                    {
                        // Too many children for a fan: move the orbit outwards.
                        double s = Math.Sin(maxSpan / (2 * (n - 1)));
                        radius = Math.Max(radius, (size + options.Gap) / (2 * s));
                    }
                }

                if (radius > options.MaxCenterRadius)
                {
                    radius = Math.Max(options.MaxCenterRadius, 1);
                }

                if (n >= 2)
                {
                    // Shrink items if even the clamped radius cannot host the fan.
                    double stepNow = StepFor(size, options.Gap, radius);
                    if ((n - 1) * stepNow > maxSpan)
                    {
                        double chord = 2 * radius * Math.Sin(maxSpan / (2 * (n - 1)));
                        size = Math.Clamp(chord - options.Gap, options.MinItemSize, options.ItemSize);
                    }
                }

                slots = new SlotPosition[n];
                if (n == 1)
                {
                    slots[0] = Polar(parentAngle, radius);
                }
                else if (n > 1)
                {
                    double stepRad = Math.Min(StepFor(size, options.Gap, radius), maxSpan / (n - 1));
                    double stepDeg = RadToDeg(stepRad);
                    for (int i = 0; i < n; i++)
                    {
                        slots[i] = Polar(parentAngle + (i - (n - 1) / 2.0) * stepDeg, radius);
                    }
                }

                rings.Add(new RingLayout(skip + j, j, radius, size, true, parentAngle, slots));
            }

            outer = Math.Max(outer, radius + size / 2);
            prevRadius = radius;
            prevSize = size;

            // Angle of the group that owns the next ring.
            parentAngle = double.NaN;
            if (skip + j + 1 < specs.Count)
            {
                int parentSlot = specs[skip + j + 1].ParentSlot;
                if (parentSlot >= 0 && parentSlot < slots.Length)
                {
                    parentAngle = slots[parentSlot].AngleDeg;
                }
            }
        }

        if (rings.Count == 0)
        {
            outer = Math.Max(options.Radius + options.ItemSize / 2, options.OrbRadius);
        }

        return new MenuLayout(rings, skip, outer);
    }

    /// <summary>Angle (radians) between two neighbours on a circle of the given radius so that their chord is size + gap.</summary>
    private static double StepFor(double size, double gap, double radius)
    {
        double ratio = (size + gap) / (2 * Math.Max(radius, 1));
        return 2 * Math.Asin(Math.Min(1.0, ratio));
    }

    private static SlotPosition Polar(double angleDeg, double radius)
    {
        double a = DegToRad(angleDeg);
        return new SlotPosition(NormalizeDeg(angleDeg), radius * Math.Cos(a), radius * Math.Sin(a));
    }

    public static double DegToRad(double deg) => deg * Math.PI / 180.0;

    public static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Normalizes an angle to the range (-180, 180].</summary>
    public static double NormalizeDeg(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg <= -180.0) deg += 360.0;
        return deg;
    }

    /// <summary>Smallest absolute difference between two angles (degrees, 0..180).</summary>
    public static double AngleDistance(double a, double b) => Math.Abs(NormalizeDeg(a - b));
}
