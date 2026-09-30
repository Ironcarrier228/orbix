using Orbix.Core.Layout;
using Xunit;

namespace Orbix.Core.Tests;

public class LabelPlacerTests
{
    [Theory]
    [InlineData(0, -128, -90)]
    [InlineData(128, 0, 0)]
    [InlineData(0, 128, 90)]
    [InlineData(-128, 0, 180)]
    [InlineData(90, -90, -45)]
    [InlineData(200, 100, 26)]
    public void Label_StaysInsideWindow_AndNeverCoversItsItem(double x, double y, double angle)
    {
        const double half = 220;
        var rect = LabelPlacer.Place(x, y, 26, angle, 140, 26, half, half);

        Assert.True(rect.X >= -half - 1e-6 && rect.Right <= half + 1e-6, "horizontally inside");
        Assert.True(rect.Y >= -half - 1e-6 && rect.Bottom <= half + 1e-6, "vertically inside");
        Assert.True(rect.DistanceTo(x, y) >= 26, "does not overlap the item");
    }

    [Fact]
    public void Label_PrefersTheOutwardSide()
    {
        var rect = LabelPlacer.Place(0, -100, 26, -90, 100, 24, 300, 300);
        Assert.True(rect.Bottom < -100, "a label for a top item is shown above it");

        rect = LabelPlacer.Place(100, 0, 26, 0, 100, 24, 300, 300);
        Assert.True(rect.X > 100, "a label for a right item is shown to its right");
    }

    [Fact]
    public void Label_FlipsWhenThereIsNoRoomOutside()
    {
        // window is barely bigger than the ring: a label for the top item cannot go above it
        var rect = LabelPlacer.Place(0, -100, 26, -90, 100, 24, 300, 130);
        Assert.True(rect.DistanceTo(0, -100) >= 26);
        Assert.True(rect.Y >= -130 - 1e-6);
    }
}
