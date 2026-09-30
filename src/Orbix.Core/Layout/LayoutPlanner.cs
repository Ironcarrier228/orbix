using Orbix.Core.Models;

namespace Orbix.Core.Layout;

/// <summary>Connects the item tree with the pure maths of <see cref="RadialLayout"/>.</summary>
public static class LayoutPlanner
{
    /// <summary>
    /// Builds one <see cref="RingSpec"/> per visible level for a path of expanded groups.
    /// <paramref name="path"/>[i] is the group expanded on level i (its children form level i + 1).
    /// <paramref name="extraSlotsOnLast"/> reserves slots (for the "+" button of the edit mode) on the last ring.
    /// </summary>
    public static List<RingSpec> BuildSpecs(IReadOnlyList<RadialItem> root, IReadOnlyList<RadialItem> path, int extraSlotsOnLast = 0)
    {
        var specs = new List<RingSpec>(path.Count + 1);
        IReadOnlyList<RadialItem> level = root;
        int parentSlot = -1;

        for (int i = 0; i <= path.Count; i++)
        {
            bool last = i == path.Count;
            specs.Add(new RingSpec(level.Count + (last ? Math.Max(0, extraSlotsOnLast) : 0), parentSlot));
            if (!last)
            {
                var group = path[i];
                parentSlot = IndexOf(level, group);
                level = group.Children;
            }
        }

        return specs;
    }

    /// <summary>
    /// Largest distance from the centre reached by any item for ANY expanded path of the tree.
    /// The overlay window is sized with this value, so that it never has to be resized while the menu is open.
    /// </summary>
    public static double ComputeMaxOuterRadius(IReadOnlyList<RadialItem> root, LayoutOptions options, int extraSlots)
    {
        double best = RadialLayout.Compute(BuildSpecs(root, Array.Empty<RadialItem>(), extraSlots), options).OuterRadius;
        var path = new List<RadialItem>();
        Visit(root, 0);
        return best;

        void Visit(IReadOnlyList<RadialItem> level, int depth)
        {
            if (depth >= ItemTree.MaxSupportedDepth)
            {
                return;
            }

            foreach (var item in level)
            {
                if (!item.IsGroup)
                {
                    continue;
                }

                path.Add(item);
                var layout = RadialLayout.Compute(BuildSpecs(root, path, extraSlots), options);
                best = Math.Max(best, layout.OuterRadius);
                Visit(item.Children, depth + 1);
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    /// <summary>Index of the slot whose angle is closest to <paramref name="angleDeg"/>; -1 for an empty ring.</summary>
    public static int NearestSlot(RingLayout ring, double angleDeg)
    {
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < ring.Slots.Length; i++)
        {
            double d = RadialLayout.AngleDistance(ring.Slots[i].AngleDeg, angleDeg);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    private static int IndexOf(IReadOnlyList<RadialItem> list, RadialItem item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
