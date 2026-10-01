using Orbix.Core.Layout;
using Xunit;

namespace Orbix.Core.Tests;

public class RadialLayoutTests
{
    private static readonly LayoutOptions Defaults = new();

    private static double Distance(SlotPosition a, SlotPosition b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void EmptyMenu_DoesNotThrowAndReportsSomeExtent()
    {
        var layout = RadialLayout.Compute(Array.Empty<RingSpec>(), Defaults);
        Assert.Empty(layout.Rings);
        Assert.True(layout.OuterRadius > 0);
    }

    [Fact]
    public void FirstRing_IsEvenlySpread_AndStartsAtTop()
    {
        var layout = RadialLayout.Compute(new[] { new RingSpec(8, -1) }, Defaults);
        var ring = Assert.Single(layout.Rings);

        Assert.False(ring.IsArc);
        Assert.Equal(8, ring.Slots.Length);
        Assert.Equal(128, ring.Radius, 6);

        // first slot is at the top: x == 0, y == -radius
        Assert.Equal(0, ring.Slots[0].X, 6);
        Assert.Equal(-128, ring.Slots[0].Y, 6);

        for (int i = 0; i < 8; i++)
        {
            double expected = RadialLayout.NormalizeDeg(-90 + i * 45);
            Assert.Equal(expected, ring.Slots[i].AngleDeg, 6);
            Assert.Equal(128, Math.Sqrt(ring.Slots[i].X * ring.Slots[i].X + ring.Slots[i].Y * ring.Slots[i].Y), 6);
        }
    }

    [Fact]
    public void SingleItem_IsPlacedAtStartAngle()
    {
        var layout = RadialLayout.Compute(new[] { new RingSpec(1, -1) }, Defaults with { StartAngleDeg = 0 });
        var slot = layout.Rings[0].Slots[0];
        Assert.Equal(128, slot.X, 6);
        Assert.Equal(0, slot.Y, 6);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(40)]
    public void CrowdedRing_GrowsRadius_SoItemsNeverOverlap(int count)
    {
        var layout = RadialLayout.Compute(new[] { new RingSpec(count, -1) }, Defaults);
        var ring = layout.Rings[0];

        Assert.Equal(count, ring.Slots.Length);
        Assert.True(ring.Radius >= Defaults.Radius - 1e-9);
        Assert.Equal(Defaults.ItemSize, ring.ItemSize, 6);

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                Assert.True(Distance(ring.Slots[i], ring.Slots[j]) >= ring.ItemSize + Defaults.Gap - 1e-6,
                    $"slots {i} and {j} overlap for count {count}");
            }
        }
    }

    [Fact]
    public void ChildRing_IsAFanCenteredOnTheParent_AndOutsideTheParentRing()
    {
        // level 0: 8 items, the group at slot 2 (angle 0 = right) is expanded, it has 5 children
        var layout = RadialLayout.Compute(new[] { new RingSpec(8, -1), new RingSpec(5, 2) }, Defaults);
        Assert.Equal(2, layout.Rings.Count);

        var parent = layout.Rings[0];
        var child = layout.Rings[1];
        double parentAngle = parent.Slots[2].AngleDeg;

        Assert.True(child.IsArc);
        Assert.Equal(parentAngle, child.ArcCenterDeg, 6);
        Assert.True(child.Radius >= parent.Radius + (parent.ItemSize + child.ItemSize) / 2 + Defaults.Gap - 1e-6);

        // symmetric around the parent's angle
        for (int i = 0; i < child.Slots.Length; i++)
        {
            double left = RadialLayout.NormalizeDeg(child.Slots[i].AngleDeg - parentAngle);
            double right = RadialLayout.NormalizeDeg(child.Slots[child.Slots.Length - 1 - i].AngleDeg - parentAngle);
            Assert.Equal(-right, left, 6);
        }

        // the middle child is exactly behind the parent
        Assert.Equal(parentAngle, child.Slots[2].AngleDeg, 6);

        // no overlaps between children
        for (int i = 0; i < child.Slots.Length; i++)
        {
            for (int j = i + 1; j < child.Slots.Length; j++)
            {
                Assert.True(Distance(child.Slots[i], child.Slots[j]) >= child.ItemSize + Defaults.Gap - 1e-6);
            }
        }
    }

    [Fact]
    public void SingleChild_IsPlacedStraightBehindTheParent()
    {
        var layout = RadialLayout.Compute(new[] { new RingSpec(4, -1), new RingSpec(1, 1) }, Defaults);
        Assert.Equal(layout.Rings[0].Slots[1].AngleDeg, layout.Rings[1].Slots[0].AngleDeg, 6);
    }

    [Fact]
    public void ThreeLevels_AreAllVisibleAndProperlySeparated()
    {
        var specs = new[] { new RingSpec(8, -1), new RingSpec(4, 7), new RingSpec(3, 2) };
        var layout = RadialLayout.Compute(specs, Defaults);

        Assert.Equal(3, layout.Rings.Count);
        Assert.Equal(0, layout.FirstVisibleLevel);
        for (int i = 1; i < layout.Rings.Count; i++)
        {
            var a = layout.Rings[i - 1];
            var b = layout.Rings[i];
            Assert.True(b.Radius - a.Radius >= (a.ItemSize + b.ItemSize) / 2 + Defaults.Gap - 1e-6);
            Assert.Equal(i, b.OrbitIndex);
            Assert.Equal(i, b.Level);
        }

        Assert.Equal(layout.Rings[2].OuterRadius, layout.OuterRadius, 6);
    }

    [Fact]
    public void DeeperThanVisibleOrbits_HidesInnerOrbits_AndStartsAFreshCircle()
    {
        var specs = new[] { new RingSpec(8, -1), new RingSpec(4, 7), new RingSpec(3, 2), new RingSpec(5, 1), new RingSpec(2, 0) };
        var layout = RadialLayout.Compute(specs, Defaults with { MaxVisibleOrbits = 3 });

        Assert.Equal(3, layout.Rings.Count);
        Assert.Equal(2, layout.FirstVisibleLevel);
        Assert.Equal(2, layout.Rings[0].Level);
        Assert.False(layout.Rings[0].IsArc);
        Assert.Equal(3, layout.Rings[0].Slots.Length);
        Assert.True(layout.Rings[1].IsArc);
        Assert.Equal(0, layout.Rings[0].OrbitIndex);
    }

    [Fact]
    public void MaxCenterRadius_IsRespected_ByShrinkingItems()
    {
        var options = Defaults with { MaxCenterRadius = 200 };
        var layout = RadialLayout.Compute(new[] { new RingSpec(40, -1), new RingSpec(30, 3) }, options);

        foreach (var ring in layout.Rings)
        {
            Assert.True(ring.Radius <= 200 + 1e-9);
            Assert.True(ring.ItemSize >= options.MinItemSize - 1e-9);
            Assert.True(ring.ItemSize <= options.ItemSize + 1e-9);
            foreach (var slot in ring.Slots)
            {
                Assert.True(Math.Sqrt(slot.X * slot.X + slot.Y * slot.Y) <= 200 + 1e-6);
            }
        }
    }

    [Fact]
    public void LargeGroup_FansOutWithinMaxArc()
    {
        var layout = RadialLayout.Compute(new[] { new RingSpec(6, -1), new RingSpec(20, 0) }, Defaults);
        var child = layout.Rings[1];
        double first = RadialLayout.NormalizeDeg(child.Slots[0].AngleDeg - child.ArcCenterDeg);
        double last = RadialLayout.NormalizeDeg(child.Slots[^1].AngleDeg - child.ArcCenterDeg);
        Assert.True(Math.Abs(last - first) <= Defaults.MaxArcDeg + 1e-6);
        for (int i = 0; i < child.Slots.Length; i++)
        {
            for (int j = i + 1; j < child.Slots.Length; j++)
            {
                Assert.True(Distance(child.Slots[i], child.Slots[j]) >= child.ItemSize + Defaults.Gap - 1e-6);
            }
        }
    }

    [Fact]
    public void InvalidParentSlot_FallsBackToFullCircle()
    {
        var layout = RadialLayout.Compute(new[] { new RingSpec(4, -1), new RingSpec(3, 99) }, Defaults);
        Assert.False(layout.Rings[1].IsArc);
        Assert.True(layout.Rings[1].Radius > layout.Rings[0].Radius);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 350, 20)]
    [InlineData(-170, 170, 20)]
    [InlineData(90, -90, 180)]
    public void AngleDistance_HandlesWrapAround(double a, double b, double expected) =>
        Assert.Equal(expected, RadialLayout.AngleDistance(a, b), 6);

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(540, 180)]
    [InlineData(-180, 180)]
    public void NormalizeDeg_ReturnsHalfOpenRange(double input, double expected) =>
        Assert.Equal(expected, RadialLayout.NormalizeDeg(input), 6);
}
