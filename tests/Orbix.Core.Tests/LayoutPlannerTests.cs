using Orbix.Core.Layout;
using Orbix.Core.Models;
using Orbix.Core.Services;
using Xunit;

namespace Orbix.Core.Tests;

public class LayoutPlannerTests
{
    private static RadialItem Leaf(string name) => new() { Name = name, Kind = ItemKind.App, Target = name + ".exe" };

    private static RadialItem Group(string name, params RadialItem[] children)
    {
        var g = new RadialItem { Name = name, Kind = ItemKind.Group };
        foreach (var c in children) g.Children.Add(c);
        return g;
    }

    [Fact]
    public void BuildSpecs_ForRoot_HasSingleRing()
    {
        var root = new ObservableList<RadialItem> { Leaf("a"), Leaf("b"), Leaf("c") };
        var specs = LayoutPlanner.BuildSpecs(root, Array.Empty<RadialItem>());
        Assert.Single(specs);
        Assert.Equal(3, specs[0].Count);
        Assert.Equal(-1, specs[0].ParentSlot);
    }

    [Fact]
    public void BuildSpecs_ForPath_UsesParentSlotsAndExtraSlots()
    {
        var inner = Group("inner", Leaf("x"), Leaf("y"));
        var outer = Group("outer", Leaf("p"), inner, Leaf("q"));
        var root = new ObservableList<RadialItem> { Leaf("a"), Leaf("b"), outer };

        var specs = LayoutPlanner.BuildSpecs(root, new[] { outer, inner }, extraSlotsOnLast: 1);

        Assert.Equal(3, specs.Count);
        Assert.Equal(new RingSpec(3, -1), specs[0]);
        Assert.Equal(new RingSpec(3, 2), specs[1]);   // children of "outer", owned by slot 2 of the root
        Assert.Equal(new RingSpec(3, 1), specs[2]);   // 2 children + "+" slot, owned by slot 1 of "outer"
    }

    [Fact]
    public void ComputeMaxOuterRadius_CoversTheDeepestGroup()
    {
        var deep = Group("deep", Leaf("1"), Leaf("2"), Leaf("3"));
        var mid = Group("mid", Leaf("m"), deep);
        var root = new ObservableList<RadialItem> { Leaf("a"), Leaf("b"), mid };
        var options = new LayoutOptions();

        double flat = RadialLayout.Compute(LayoutPlanner.BuildSpecs(root, Array.Empty<RadialItem>()), options).OuterRadius;
        double max = LayoutPlanner.ComputeMaxOuterRadius(root, options, 0);

        Assert.True(max > flat);
        double expected = RadialLayout.Compute(LayoutPlanner.BuildSpecs(root, new[] { mid, deep }), options).OuterRadius;
        Assert.Equal(expected, max, 6);
    }

    [Fact]
    public void ComputeMaxOuterRadius_OfDefaultProfile_FitsInsideACommonScreen()
    {
        var profile = DefaultConfigFactory.CreateDefaultProfile("en");
        double max = LayoutPlanner.ComputeMaxOuterRadius(profile.Items, new LayoutOptions(), 1);
        Assert.InRange(max, 100, 384); // half of a 768px-high screen
    }

    [Fact]
    public void NearestSlot_FindsTheClosestAngle()
    {
        var layout = RadialLayout.Compute(new[] { new RingSpec(4, -1) }, new LayoutOptions());
        var ring = layout.Rings[0];
        Assert.Equal(0, LayoutPlanner.NearestSlot(ring, -80));
        Assert.Equal(1, LayoutPlanner.NearestSlot(ring, 10));
        Assert.Equal(2, LayoutPlanner.NearestSlot(ring, 100));
        Assert.Equal(3, LayoutPlanner.NearestSlot(ring, 170));
        Assert.Equal(-1, LayoutPlanner.NearestSlot(RadialLayout.Compute(new[] { new RingSpec(0, -1) }, new LayoutOptions()).Rings[0], 0));
    }
}
