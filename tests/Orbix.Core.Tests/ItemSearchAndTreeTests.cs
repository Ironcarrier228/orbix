using Orbix.Core.Models;
using Orbix.Core.Search;
using Xunit;

namespace Orbix.Core.Tests;

public class ItemSearchTests
{
    private static RadialItem App(string name, string? target = null) => new() { Name = name, Kind = ItemKind.App, Target = target };

    private static ObservableList<RadialItem> Sample(out RadialItem deepLeaf, out RadialItem inner, out RadialItem outer)
    {
        deepLeaf = App("Visual Studio Code", @"C:\Program Files\Microsoft VS Code\Code.exe");
        inner = new RadialItem { Name = "Dev", Kind = ItemKind.Group };
        inner.Children.Add(deepLeaf);
        inner.Children.Add(App("Git Bash"));
        outer = new RadialItem { Name = "Tools", Kind = ItemKind.Group };
        outer.Children.Add(inner);
        return new ObservableList<RadialItem>
        {
            App("Notepad", "notepad.exe"),
            App("Калькулятор", "calc.exe"),
            outer,
        };
    }

    [Fact]
    public void EmptyQuery_ProducesInactiveResult()
    {
        var items = Sample(out _, out _, out _);
        var result = ItemSearch.Run(items, "   ");
        Assert.False(result.IsActive);
        Assert.False(result.HasMatches);
    }

    [Fact]
    public void Finds_ByPrefix_ByWordStart_AndBySubstring_InRankOrder()
    {
        var items = new ObservableList<RadialItem> { App("Some Studio"), App("Studio One"), App("Xstudio") };
        var result = ItemSearch.Run(items, "stu");
        Assert.Equal(new[] { "Studio One", "Some Studio", "Xstudio" }, result.Matches.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Finds_NestedItems_AndReportsAncestorGroups()
    {
        var items = Sample(out var deep, out var inner, out var outer);
        var result = ItemSearch.Run(items, "code");

        Assert.Contains(deep.Id, result.MatchIds);
        Assert.Contains(inner.Id, result.AncestorIds);
        Assert.Contains(outer.Id, result.AncestorIds);
        Assert.DoesNotContain(items[0].Id, result.AncestorIds);
    }

    [Fact]
    public void IsCaseInsensitive_AlsoForCyrillic()
    {
        var items = Sample(out _, out _, out _);
        Assert.Single(ItemSearch.Run(items, "КАЛЬК").Matches);
    }

    [Fact]
    public void WrongKeyboardLayout_IsUnderstood()
    {
        var items = Sample(out _, out _, out _);

        // "rfkm" typed on the English layout == "калькулятор" prefix on the Russian one
        Assert.Equal("Калькулятор", ItemSearch.Run(items, "rfkm").Matches.Single().Name);
        // and the other way round: "ьщеу" is "more" typed on RU layout; "тщеузфв" is "notepad"
        Assert.Equal("Notepad", ItemSearch.Run(items, "тщеузфв").Matches.Single().Name);
    }

    [Fact]
    public void SwapKeyboardLayout_ReturnsSameInstanceWhenNothingToConvert()
    {
        const string digits = "12345";
        Assert.Same(digits, ItemSearch.SwapKeyboardLayout(digits));
        Assert.Equal("привет", ItemSearch.SwapKeyboardLayout("ghbdtn"));
        Assert.Equal("ghbdtn", ItemSearch.SwapKeyboardLayout("привет"));
    }

    [Fact]
    public void Finds_ByTargetFileName()
    {
        var items = new ObservableList<RadialItem> { App("Editor", @"C:\tools\sublime_text.exe") };
        Assert.Single(ItemSearch.Run(items, "sublime").Matches);
    }
}

public class ItemTreeTests
{
    private static RadialItem Leaf(string n) => new() { Name = n, Kind = ItemKind.App, Target = n };

    private static RadialItem Group(string n, params RadialItem[] kids)
    {
        var g = new RadialItem { Name = n, Kind = ItemKind.Group };
        foreach (var k in kids) g.Children.Add(k);
        return g;
    }

    [Fact]
    public void Walk_IsDepthFirstPreOrder()
    {
        var root = new ObservableList<RadialItem> { Leaf("a"), Group("g", Leaf("g1"), Group("h", Leaf("h1"))), Leaf("b") };
        Assert.Equal(new[] { "a", "g", "g1", "h", "h1", "b" }, ItemTree.Walk(root).Select(i => i.Name).ToArray());
        Assert.Equal(6, ItemTree.Count(root));
        Assert.Equal(3, ItemTree.MaxDepth(root));
    }

    [Fact]
    public void Move_ReordersInsideTheSameList()
    {
        var a = Leaf("a"); var b = Leaf("b"); var c = Leaf("c");
        var root = new ObservableList<RadialItem> { a, b, c };

        Assert.True(ItemTree.Move(root, a, root, 2));
        Assert.Equal(new[] { "b", "c", "a" }, root.Select(i => i.Name).ToArray());

        Assert.True(ItemTree.Move(root, a, root, 0));
        Assert.Equal(new[] { "a", "b", "c" }, root.Select(i => i.Name).ToArray());

        Assert.True(ItemTree.Move(root, c, root, 99)); // clamped
        Assert.Equal("c", root[^1].Name);
    }

    [Fact]
    public void Move_IntoAGroup_AndOut()
    {
        var x = Leaf("x");
        var g = Group("g");
        var root = new ObservableList<RadialItem> { x, g };

        Assert.True(ItemTree.Move(root, x, g.Children, 0));
        Assert.Single(root);
        Assert.Single(g.Children);

        Assert.True(ItemTree.Move(root, x, root, 0));
        Assert.Equal(2, root.Count);
        Assert.Empty(g.Children);
    }

    [Fact]
    public void Move_ForbidsMovingAGroupIntoItself()
    {
        var inner = Group("inner");
        var outer = Group("outer", inner);
        var root = new ObservableList<RadialItem> { outer };

        Assert.False(ItemTree.Move(root, outer, outer.Children, 0));
        Assert.False(ItemTree.Move(root, outer, inner.Children, 0));
        Assert.Single(root);
        Assert.Same(inner, outer.Children[0]);
    }

    [Fact]
    public void Remove_And_PathTo_WorkAtAnyDepth()
    {
        var leaf = Leaf("leaf");
        var inner = Group("inner", leaf);
        var outer = Group("outer", inner);
        var root = new ObservableList<RadialItem> { Leaf("a"), outer };

        var path = ItemTree.PathTo(root, leaf);
        Assert.NotNull(path);
        Assert.Equal(new[] { "outer", "inner" }, path!.Select(p => p.Name).ToArray());
        Assert.Empty(ItemTree.PathTo(root, outer)!);
        Assert.Null(ItemTree.PathTo(root, Leaf("stranger")));

        Assert.True(ItemTree.Remove(root, leaf));
        Assert.Empty(inner.Children);
        Assert.False(ItemTree.Remove(root, leaf));
    }

    [Fact]
    public void Clone_CopiesDeeplyWithNewIds()
    {
        var g = Group("g", Leaf("a"), Group("h", Leaf("b")));
        var copy = g.Clone();

        Assert.NotEqual(g.Id, copy.Id);
        Assert.Equal(3, ItemTree.Count(copy.Children));
        Assert.NotEqual(g.Children[0].Id, copy.Children[0].Id);
        Assert.Equal("b", copy.Children[1].Children[0].Name);

        var same = g.Clone(newIds: false);
        Assert.Equal(g.Id, same.Id);
    }
}
