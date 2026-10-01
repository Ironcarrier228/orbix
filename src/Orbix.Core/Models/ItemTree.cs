namespace Orbix.Core.Models;

/// <summary>Helpers for working with the tree of <see cref="RadialItem"/>.</summary>
public static class ItemTree
{
    /// <summary>Safety limit of nesting (the UI handles unlimited depth, this only protects from corrupted files).</summary>
    public const int MaxSupportedDepth = 12;

    /// <summary>Depth-first, pre-order enumeration of all items.</summary>
    public static IEnumerable<RadialItem> Walk(IEnumerable<RadialItem> roots)
    {
        var stack = new Stack<IEnumerator<RadialItem>>();
        stack.Push(roots.GetEnumerator());
        try
        {
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop().Dispose();
                    continue;
                }

                var item = top.Current;
                yield return item;
                if (item.Children.Count > 0)
                {
                    stack.Push(item.Children.GetEnumerator());
                }
            }
        }
        finally
        {
            while (stack.Count > 0)
            {
                stack.Pop().Dispose();
            }
        }
    }

    public static RadialItem? Find(IEnumerable<RadialItem> roots, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return Walk(roots).FirstOrDefault(i => i.Id == id);
    }

    /// <summary>Total number of items in the tree.</summary>
    public static int Count(IEnumerable<RadialItem> roots) => Walk(roots).Count();

    /// <summary>Depth of the tree: 0 for an empty list, 1 for a flat list, 2 when groups have children...</summary>
    public static int MaxDepth(IEnumerable<RadialItem> roots)
    {
        int best = 0;
        foreach (var item in roots)
        {
            best = Math.Max(best, 1 + (item.Children.Count > 0 ? MaxDepth(item.Children) : 0));
        }

        return best;
    }

    /// <summary>
    /// Finds the list that contains <paramref name="item"/> and the owning group
    /// (null parent = the item is in the root list).
    /// </summary>
    public static bool TryFindOwner(
        ObservableList<RadialItem> root,
        RadialItem item,
        out ObservableList<RadialItem> owner,
        out RadialItem? parent)
    {
        if (root.Contains(item))
        {
            owner = root;
            parent = null;
            return true;
        }

        foreach (var node in Walk(root))
        {
            if (node.Children.Contains(item))
            {
                owner = node.Children;
                parent = node;
                return true;
            }
        }

        owner = root;
        parent = null;
        return false;
    }

    /// <summary>True when <paramref name="list"/> is the child list of <paramref name="item"/> or of its descendants.</summary>
    public static bool ListIsInside(RadialItem item, IList<RadialItem> list)
    {
        if (ReferenceEquals(item.Children, list))
        {
            return true;
        }

        return Walk(item.Children).Any(n => ReferenceEquals(n.Children, list));
    }

    /// <summary>
    /// Moves <paramref name="item"/> to <paramref name="index"/> of <paramref name="target"/>.
    /// Returns false when the move is impossible (unknown item, or moving a group into itself).
    /// </summary>
    public static bool Move(ObservableList<RadialItem> root, RadialItem item, ObservableList<RadialItem> target, int index)
    {
        if (!TryFindOwner(root, item, out var source, out _))
        {
            return false;
        }

        if (ListIsInside(item, target))
        {
            return false;
        }

        int oldIndex = source.IndexOf(item);
        if (ReferenceEquals(source, target))
        {
            int newIndex = Math.Clamp(index, 0, source.Count - 1);
            if (newIndex != oldIndex)
            {
                source.Move(oldIndex, newIndex);
            }

            return true;
        }

        source.RemoveAt(oldIndex);
        target.Insert(Math.Clamp(index, 0, target.Count), item);
        return true;
    }

    /// <summary>Removes the item from wherever it is in the tree.</summary>
    public static bool Remove(ObservableList<RadialItem> root, RadialItem item)
    {
        if (!TryFindOwner(root, item, out var owner, out _))
        {
            return false;
        }

        owner.Remove(item);
        return true;
    }

    /// <summary>
    /// Returns the path of groups from the root to <paramref name="target"/> (the target itself excluded),
    /// or null when the item is not part of the tree.
    /// </summary>
    public static List<RadialItem>? PathTo(IEnumerable<RadialItem> roots, RadialItem target)
    {
        var path = new List<RadialItem>();
        return Search(roots);

        List<RadialItem>? Search(IEnumerable<RadialItem> level)
        {
            foreach (var node in level)
            {
                if (ReferenceEquals(node, target))
                {
                    return new List<RadialItem>(path);
                }

                if (node.Children.Count > 0)
                {
                    path.Add(node);
                    var found = Search(node.Children);
                    path.RemoveAt(path.Count - 1);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }
}
