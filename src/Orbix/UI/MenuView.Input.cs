using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbix.Core.Layout;
using Orbix.Core.Localization;
using Orbix.Core.Models;
using Orbix.Core.Search;
using Orbix.Services;

namespace Orbix.UI;

/// <summary>Pointer, keyboard, wheel, search, drag &amp; drop reordering of the menu.</summary>
internal sealed partial class MenuView
{
    private const double DragThreshold = 6;

    private HitResult _hot = HitResult.None;
    private HitResult _downHit = HitResult.None;
    private int _keyboardIndex = -1;
    private Point _lastPointer;
    private bool _hasPointer;

    // reorder by dragging (edit mode)
    private ItemVisual? _dragCandidate;
    private RingView? _dragCandidateRing;
    private Point _dragStart;
    private ItemVisual? _dragVisual;
    private RingView? _dragRing;
    private RingView? _dropRing;
    private int _dropIndex;
    private ItemVisual? _dropGroup;

    private (RadialItem Item, ObservableList<RadialItem> List, int Index)? _lastDeleted;

    // ---- hit testing -----------------------------------------------------------

    private IEnumerable<(RingView Ring, ItemVisual Visual, int Index)> AllVisuals()
    {
        foreach (var ring in _rings)
        {
            for (int i = 0; i < ring.Visuals.Count; i++)
            {
                yield return (ring, ring.Visuals[i], i);
            }

            if (ring.AddSlot != null)
            {
                yield return (ring, ring.AddSlot, ring.Visuals.Count);
            }
        }
    }

    /// <summary>What is under the point (stage coordinates: origin = centre of the orb, DIPs).</summary>
    public HitResult HitTest(Point p)
    {
        if (!_open || _session == null)
        {
            return HitResult.None;
        }

        // delete badges (edit mode) win over everything else
        if (_edit)
        {
            foreach (var (ring, visual, index) in AllVisuals())
            {
                if (visual.IsAddSlot || ReferenceEquals(visual, _dragVisual) || visual.Alpha.Value < 0.4)
                {
                    continue;
                }

                var (bx, by, br) = visual.DeleteBadgeGeometry;
                double dx = p.X - (visual.X.Value + bx);
                double dy = p.Y - (visual.Y.Value + by);
                if (dx * dx + dy * dy <= (br + 3) * (br + 3))
                {
                    return new HitResult(HitKind.Item, ring, index, visual, true);
                }
            }
        }

        HitResult best = HitResult.None;
        double bestDistance = double.MaxValue;
        foreach (var (ring, visual, index) in AllVisuals())
        {
            if (ReferenceEquals(visual, _dragVisual) || visual.Alpha.Value < 0.4 || visual.Alpha.Target < 0.4)
            {
                continue;
            }

            double dx = p.X - visual.X.Value;
            double dy = p.Y - visual.Y.Value;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= visual.Size / 2 + 3 && d < bestDistance)
            {
                bestDistance = d;
                best = new HitResult(visual.IsAddSlot ? HitKind.AddSlot : HitKind.Item, ring, index, visual, false);
            }
        }

        if (best.Kind != HitKind.None)
        {
            return best;
        }

        double fromCenter = Math.Sqrt(p.X * p.X + p.Y * p.Y);
        if (fromCenter <= _orb.Diameter / 2 + 2)
        {
            return new HitResult(HitKind.Orb, null, -1, null, false);
        }

        if (fromCenter <= Math.Max(_aDisc.Value, 1))
        {
            return new HitResult(HitKind.Disc, null, -1, null, false);
        }

        return HitResult.None;
    }

    private static bool SameTarget(HitResult a, HitResult b) =>
        a.Kind == b.Kind && ReferenceEquals(a.Visual, b.Visual) && a.OnDelete == b.OnDelete;

    // ---- highlight (hover / keyboard) ------------------------------------------

    private void SetHot(HitResult hit)
    {
        if (SameTarget(_hot, hit))
        {
            return;
        }

        _hot.Visual?.SetHover(false);
        if (_hot.Kind == HitKind.Orb)
        {
            _orb.Pop.Go(1, 100);
        }

        _hot = hit;
        if (Logger.DebugEnabled)
        {
            Logger.Debug($"Hot {hit.Kind}" + (hit.Ring != null ? $" r{hit.Ring.Level}" : string.Empty) +
                         $" i{hit.Index}" + (hit.Visual?.Item != null ? $" '{hit.Visual.Item.Name}'" : string.Empty) +
                         $" ptr {_lastPointer.X:F0},{_lastPointer.Y:F0}");
        }

        if (hit.Visual != null && !hit.OnDelete)
        {
            hit.Visual.SetHover(true);
        }

        if (hit.Kind == HitKind.Orb)
        {
            _orb.Pop.Go(1.07, 100);
        }

        UpdateLabelForHot();
        UpdateCursor();
        ScheduleHoverOpen();
    }

    private void UpdateCursor()
    {
        bool clickable = _hot.Kind is HitKind.Item or HitKind.AddSlot or HitKind.Orb;
        CurrentCursor = _dragVisual != null ? Cursors.SizeAll : clickable ? Cursors.Hand : Cursors.Arrow;
    }

    private void UpdateLabelForHot()
    {
        if (_dragVisual != null)
        {
            HideLabel();
            return;
        }

        switch (_hot.Kind)
        {
            case HitKind.Item when _hot.Visual?.Item is { } item && _hot.Visual != null:
                var v = _hot.Visual;
                if (_hot.OnDelete)
                {
                    ShowLabel(Loc.T("Menu.Delete", item.Name), null, v.X.Value, v.Y.Value, v.Size / 2, v.SlotAngle);
                    break;
                }

                ShowLabel(item.Name, DescribeTarget(item), v.X.Value, v.Y.Value, v.Size / 2 * 1.14, v.SlotAngle);
                break;
            case HitKind.AddSlot when _hot.Visual != null:
                ShowLabel(Loc.T("Menu.Add"), null, _hot.Visual.X.Value, _hot.Visual.Y.Value, _hot.Visual.Size / 2, _hot.Visual.SlotAngle);
                break;
            case HitKind.Orb:
                ShowLabel(Loc.T(_path.Count > 0 || _search.IsActive ? "Menu.Back" : "Menu.Close"), null, 0, 0, _orb.Diameter / 2, 90);
                break;
            default:
                HideLabel();
                break;
        }
    }

    private static string? DescribeTarget(RadialItem item)
    {
        switch (item.Kind)
        {
            case ItemKind.Group:
                return Loc.T("Menu.GroupCount", item.Children.Count);
            case ItemKind.System:
                return null;
            default:
                var target = item.Target;
                if (string.IsNullOrWhiteSpace(target))
                {
                    return null;
                }

                return target.Length > 52 ? "…" + target[^51..] : target;
        }
    }

    private void RefreshHighlightAfterRebuild()
    {
        if (_hot.Visual != null && !AllVisuals().Any(v => ReferenceEquals(v.Visual, _hot.Visual)))
        {
            _hot = HitResult.None;
            HideLabel();
        }

        if (_hasPointer && _dragVisual == null)
        {
            SetHot(HitTest(_lastPointer));
        }
    }

    // ---- hover-open of groups --------------------------------------------------

    private void ScheduleHoverOpen()
    {
        _hoverTimer.Stop();
        if (_config.Menu.SubmenuOpen != SubmenuOpenMode.Hover || _dragVisual != null || _search.IsActive)
        {
            return;
        }

        if (_hot.Kind != HitKind.Item || _hot.OnDelete || _hot.Visual?.Item is not { IsGroup: true } group || _hot.Ring == null)
        {
            return;
        }

        if (IsExpanded(_hot.Ring, group))
        {
            return;
        }

        int delay = _config.Menu.HoverDelayMs;
        if (delay <= 0)
        {
            ExpandGroup(_hot.Ring.Level, group);
            return;
        }

        _hoverTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _hoverTimer.Start();
    }

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        Logger.Debug($"HoverOpen tick hot={_hot.Kind} ring={(_hot.Ring != null ? _hot.Ring.Level.ToString() : "-")} group={_hot.Visual?.Item?.IsGroup == true}");
        if (_open && _hot.Kind == HitKind.Item && !_hot.OnDelete && _hot.Visual?.Item is { IsGroup: true } group && _hot.Ring != null && !IsExpanded(_hot.Ring, group))
        {
            ExpandGroup(_hot.Ring.Level, group);
        }
    }

    private bool IsExpanded(RingView ring, RadialItem group) =>
        !ring.IsSearch && _path.Count > ring.Level && ReferenceEquals(_path[ring.Level], group);

    // ---- navigation ------------------------------------------------------------

    private void ExpandGroup(int level, RadialItem group)
    {
        Logger.Debug($"Expand level={level} '{group.Name}' path={_path.Count}");
        if (level > _path.Count)
        {
            return;
        }

        _path.RemoveRange(level, _path.Count - level);
        _path.Add(group);
        _keyboardIndex = -1;
        Rebuild(animate: true);
    }

    private void CollapseTo(int pathLength)
    {
        if (pathLength >= _path.Count)
        {
            return;
        }

        _path.RemoveRange(pathLength, _path.Count - pathLength);
        _keyboardIndex = -1;
        Rebuild(animate: true);
    }

    private void ToggleGroup(RingView ring, RadialItem group)
    {
        if (ring.IsSearch)
        {
            OpenGroupFromSearch(group);
            return;
        }

        if (IsExpanded(ring, group))
        {
            CollapseTo(ring.Level);
        }
        else
        {
            ExpandGroup(ring.Level, group);
        }
    }

    private void OpenGroupFromSearch(RadialItem group)
    {
        var chain = ItemTree.PathTo(Profile.Items, group);
        _query = string.Empty;
        _search = SearchResult.Empty;
        _path.Clear();
        if (chain != null)
        {
            _path.AddRange(chain);
            _path.Add(group);
        }

        UpdateSearchPill();
        Rebuild(animate: true);
    }

    /// <summary>The orb / right click: one level back, finally close.</summary>
    public void GoBackOrClose()
    {
        if (_search.IsActive)
        {
            ClearSearch();
        }
        else if (_path.Count > 0)
        {
            CollapseTo(_path.Count - 1);
        }
        else
        {
            CloseRequested?.Invoke();
        }
    }

    // ---- activation ------------------------------------------------------------

    private void InvokeItem(RadialItem item)
    {
        if (LaunchService.IsDangerous(item) && _config.Menu.ConfirmDangerous && !ReferenceEquals(_armed, item))
        {
            Arm(item);
            return;
        }

        Disarm();
        ItemInvoked?.Invoke(item);
    }

    private void Arm(RadialItem item)
    {
        _armed = item;
        UpdateExpandedStates();
        ShowToast(Loc.T("Menu.ConfirmAgain", item.Name));
        _armTimer.Stop();
        _armTimer.Start();
    }

    private void Disarm()
    {
        _armTimer.Stop();
        if (_armed == null)
        {
            return;
        }

        _armed = null;
        UpdateExpandedStates();
        HideToast();
    }

    private void HandleClick(HitResult hit)
    {
        Logger.Debug($"Click {hit.Kind}" + (hit.Ring != null ? $" r{hit.Ring.Level}" : string.Empty) + $" i{hit.Index}");
        switch (hit.Kind)
        {
            case HitKind.Orb:
                GoBackOrClose();
                break;
            case HitKind.AddSlot when hit.Ring?.Source != null:
                ContextRequested?.Invoke(new ContextRequest(null, hit.Ring.Source, hit.Ring.ItemCount, hit.Ring.Parent));
                break;
            case HitKind.Item when hit.Visual?.Item is { } item && hit.Ring != null:
                if (hit.OnDelete)
                {
                    DeleteItem(item);
                }
                else if (item.IsGroup)
                {
                    ToggleGroup(hit.Ring, item);
                }
                else if (_edit && hit.Ring.Source != null)
                {
                    ContextRequested?.Invoke(new ContextRequest(item, hit.Ring.Source, hit.Index, hit.Ring.Parent));
                }
                else
                {
                    InvokeItem(item);
                }

                break;
        }
    }

    private void HandleRightClick(HitResult hit)
    {
        if (_edit)
        {
            if (hit.Kind == HitKind.Item && !hit.OnDelete && hit.Visual?.Item is { } item && hit.Ring?.Source != null)
            {
                ContextRequested?.Invoke(new ContextRequest(item, hit.Ring.Source, hit.Index, hit.Ring.Parent));
            }
            else if (hit.Kind == HitKind.AddSlot && hit.Ring?.Source != null)
            {
                ContextRequested?.Invoke(new ContextRequest(null, hit.Ring.Source, hit.Ring.ItemCount, hit.Ring.Parent));
            }
            else if (hit.Kind is HitKind.Orb or HitKind.Disc or HitKind.None)
            {
                GoBackOrClose();
            }

            return;
        }

        GoBackOrClose();
    }

    public void DeleteItem(RadialItem item)
    {
        if (!ItemTree.TryFindOwner(Profile.Items, item, out var owner, out _))
        {
            return;
        }

        int index = owner.IndexOf(item);
        owner.Remove(item);
        _icons.Invalidate(item);
        _lastDeleted = (item, owner, index);
        _hot = HitResult.None;
        Rebuild(animate: true);
        ShowToast(Loc.T("Menu.Deleted", item.Name));
    }

    private void UndoDelete()
    {
        if (_lastDeleted is not { } entry)
        {
            return;
        }

        _lastDeleted = null;
        entry.List.Insert(Math.Clamp(entry.Index, 0, entry.List.Count), entry.Item);
        Rebuild(animate: true);
    }

    // ---- pointer API -----------------------------------------------------------

    public void PointerMoved(Point p)
    {
        if (!_open)
        {
            return;
        }

        _lastPointer = p;
        _hasPointer = true;

        if (_dragCandidate != null && _dragVisual == null && Distance(p, _dragStart) > DragThreshold)
        {
            BeginDrag();
        }

        if (_dragVisual != null)
        {
            UpdateDrag(p);
            return;
        }

        _keyboardIndex = -1;
        SetHot(HitTest(p));
    }

    public void PointerLeft()
    {
        _hasPointer = false;
        if (_dragVisual == null)
        {
            SetHot(HitResult.None);
        }
    }

    /// <summary>Returns true when the window should capture the mouse (a drag may start).</summary>
    public bool PointerDown(Point p, MouseButton button)
    {
        if (!_open)
        {
            return false;
        }

        _lastPointer = p;
        _hasPointer = true;
        var hit = HitTest(p);
        _downHit = hit;

        if (button == MouseButton.Left && _edit && hit.Kind == HitKind.Item && !hit.OnDelete && hit.Visual != null)
        {
            _dragCandidate = hit.Visual;
            _dragCandidateRing = hit.Ring;
            _dragStart = p;
            return true;
        }

        return false;
    }

    public void PointerUp(Point p, MouseButton button)
    {
        if (!_open)
        {
            return;
        }

        var up = HitTest(p);

        if (button == MouseButton.Left)
        {
            if (_dragVisual != null)
            {
                CommitDrag(p);
                return;
            }

            _dragCandidate = null;
            _dragCandidateRing = null;
            var down = _downHit;
            _downHit = HitResult.None;
            if (SameTarget(down, up))
            {
                HandleClick(up);
            }
        }
        else if (button == MouseButton.Right)
        {
            HandleRightClick(up);
        }
    }

    public void Wheel(int delta)
    {
        if (!_open || !_config.Menu.WheelControl || delta == 0)
        {
            return;
        }

        MoveKeyboard(delta > 0 ? -1 : 1);
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ---- keyboard --------------------------------------------------------------

    private RingView? DeepestRing => _rings.Count == 0 ? null : _rings[^1];

    private void MoveKeyboard(int step)
    {
        var ring = DeepestRing;
        if (ring == null || ring.Visuals.Count == 0)
        {
            return;
        }

        int n = ring.Visuals.Count;
        if (_keyboardIndex < 0 || _keyboardIndex >= n)
        {
            _keyboardIndex = step > 0 ? 0 : n - 1;
        }
        else
        {
            _keyboardIndex = (_keyboardIndex + step + n) % n;
        }

        SetHot(new HitResult(HitKind.Item, ring, _keyboardIndex, ring.Visuals[_keyboardIndex], false));
    }

    private void ActivateByIndex(int index)
    {
        var ring = DeepestRing;
        if (ring == null || index < 0 || index >= ring.Visuals.Count)
        {
            return;
        }

        HandleClick(new HitResult(HitKind.Item, ring, index, ring.Visuals[index], false));
    }

    private void ActivateHot()
    {
        if (_hot.Kind == HitKind.Item && !_hot.OnDelete)
        {
            HandleClick(_hot);
            return;
        }

        if (_search.IsActive && _search.HasMatches)
        {
            var ring = DeepestRing;
            if (ring != null && ring.Visuals.Count > 0)
            {
                HandleClick(new HitResult(HitKind.Item, ring, 0, ring.Visuals[0], false));
            }
        }
    }

    /// <summary>Returns true when the key was used.</summary>
    public bool KeyDown(Key key, ModifierKeys modifiers)
    {
        if (!_open || IsRenaming)
        {
            return false;
        }

        bool ctrl = (modifiers & ModifierKeys.Control) != 0;

        if (key == Key.Escape)
        {
            if (_search.IsActive)
            {
                ClearSearch();
            }
            else
            {
                CloseRequested?.Invoke();
            }

            return true;
        }

        if (ctrl && key == Key.E)
        {
            EditModeChanged?.Invoke(!_edit);
            return true;
        }

        if (ctrl && key == Key.Z && _edit)
        {
            UndoDelete();
            return true;
        }

        if (!_config.Menu.KeyboardControl)
        {
            return false;
        }

        switch (key)
        {
            case Key.Left:
            case Key.Up:
                MoveKeyboard(-1);
                return true;
            case Key.Right:
            case Key.Down:
                MoveKeyboard(1);
                return true;
            case Key.Tab:
                MoveKeyboard((modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                return true;
            case Key.Enter:
                ActivateHot();
                return true;
            case Key.Back:
                if (_query.Length > 0)
                {
                    SetQuery(_query[..^1]);
                }
                else
                {
                    GoBackOrClose();
                }

                return true;
            case Key.Delete when _edit && _hot.Kind == HitKind.Item && _hot.Visual?.Item is { } item:
                DeleteItem(item);
                return true;
            case Key.Home:
                _keyboardIndex = -1;
                MoveKeyboard(1);
                return true;
            case Key.End:
                _keyboardIndex = -1;
                MoveKeyboard(-1);
                return true;
        }

        if (_query.Length == 0 && !ctrl)
        {
            int digit = key switch
            {
                >= Key.D1 and <= Key.D9 => key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
                _ => -1,
            };
            if (digit >= 0)
            {
                ActivateByIndex(digit);
                return true;
            }
        }

        return false;
    }

    /// <summary>Text typed while the menu is open starts the search.</summary>
    public void TextInput(string text)
    {
        if (!_open || IsRenaming || !_config.Menu.KeyboardControl || string.IsNullOrEmpty(text))
        {
            return;
        }

        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray());
        if (clean.Length == 0 || (_query.Length == 0 && char.IsWhiteSpace(clean[0])))
        {
            return;
        }

        SetQuery(_query + clean);
    }

    private void SetQuery(string query)
    {
        _query = query;
        _search = ItemSearch.Run(Profile.Items, query);
        _keyboardIndex = -1;
        _hot.Visual?.SetHover(false);
        _hot = HitResult.None;
        Disarm();
        Rebuild(animate: true);
        UpdateSearchPill();
    }

    private void ClearSearch()
    {
        _query = string.Empty;
        _search = SearchResult.Empty;
        _keyboardIndex = -1;
        Rebuild(animate: true);
        UpdateSearchPill();
    }

    // ---- drag to reorder (edit mode) --------------------------------------------

    private void ClearDrag()
    {
        _dragCandidate = null;
        _dragCandidateRing = null;
        _dragVisual = null;
        _dragRing = null;
        _dropRing = null;
        _dropGroup = null;
        _dropIndex = -1;
    }

    private void BeginDrag()
    {
        _dragVisual = _dragCandidate;
        _dragRing = _dragCandidateRing;
        _dragCandidate = null;
        if (_dragVisual == null)
        {
            return;
        }

        _hoverTimer.Stop();
        HideLabel();
        Panel.SetZIndex(_dragVisual.Root, 100);
        _dragVisual.SetHover(true);
        UpdateCursor();
    }

    private void UpdateDrag(Point p)
    {
        var dragged = _dragVisual;
        if (dragged == null)
        {
            return;
        }

        dragged.X.Snap(p.X);
        dragged.Y.Snap(p.Y);

        // dropping onto another group moves the item into it
        ItemVisual? group = null;
        foreach (var (_, visual, _) in AllVisuals())
        {
            if (ReferenceEquals(visual, dragged) || visual.Item is not { IsGroup: true })
            {
                continue;
            }

            double dx = p.X - visual.X.Value, dy = p.Y - visual.Y.Value;
            if (Math.Sqrt(dx * dx + dy * dy) < visual.Size * 0.42)
            {
                group = visual;
                break;
            }
        }

        if (!ReferenceEquals(group, _dropGroup))
        {
            _dropGroup?.SetHover(false);
            _dropGroup = group;
            _dropGroup?.SetHover(true);
        }

        if (group != null)
        {
            _dropRing = null;
            return;
        }

        // otherwise: the orbit closest to the pointer and the slot closest to its angle
        double radius = Math.Sqrt(p.X * p.X + p.Y * p.Y);
        RingView? target = null;
        double best = double.MaxValue;
        foreach (var ring in _rings)
        {
            if (ring.Layout == null || ring.Source == null)
            {
                continue;
            }

            double d = Math.Abs(ring.Layout.Radius - radius);
            if (d < best)
            {
                best = d;
                target = ring;
            }
        }

        if (target?.Layout == null)
        {
            return;
        }

        double angle = RadialLayout.RadToDeg(Math.Atan2(p.Y, p.X));
        int slot = LayoutPlanner.NearestSlot(target.Layout, angle);
        bool sameRing = ReferenceEquals(target, _dragRing);
        int max = sameRing ? target.ItemCount - 1 : target.ItemCount;
        int index = Math.Clamp(slot, 0, Math.Max(0, max));

        if (!ReferenceEquals(target, _dropRing) || index != _dropIndex)
        {
            _dropRing = target;
            _dropIndex = index;
            PreviewReorder(target, dragged, index);
        }
    }

    /// <summary>Moves the neighbours aside so the user sees where the item will land (same orbit only).</summary>
    private void PreviewReorder(RingView ring, ItemVisual dragged, int index)
    {
        if (ring.Layout == null || !ReferenceEquals(ring, _dragRing))
        {
            return;
        }

        var order = ring.Visuals.Where(v => !ReferenceEquals(v, dragged)).ToList();
        order.Insert(Math.Clamp(index, 0, order.Count), dragged);
        for (int i = 0; i < order.Count && i < ring.Layout.Slots.Length; i++)
        {
            var visual = order[i];
            if (ReferenceEquals(visual, dragged))
            {
                continue;
            }

            var slot = ring.Layout.Slots[i];
            visual.X.Go(slot.X, 140);
            visual.Y.Go(slot.Y, 140);
            visual.SlotX = slot.X;
            visual.SlotY = slot.Y;
            visual.SlotAngle = slot.AngleDeg;
        }
    }

    private void CommitDrag(Point p)
    {
        var dragged = _dragVisual;
        var item = dragged?.Item;
        var group = _dropGroup?.Item;
        var dropRing = _dropRing;
        int index = _dropIndex;

        _dropGroup?.SetHover(false);
        if (dragged != null)
        {
            Panel.SetZIndex(dragged.Root, 0);
            dragged.SetHover(false);
        }

        ClearDrag();
        UpdateCursor();

        if (item == null)
        {
            return;
        }

        bool moved = false;
        if (group != null)
        {
            moved = ItemTree.Move(Profile.Items, item, group.Children, group.Children.Count);
            if (moved)
            {
                ShowToast(Loc.T("Menu.MovedInto", item.Name, group.Name));
            }
        }
        else if (dropRing?.Source != null && index >= 0)
        {
            moved = ItemTree.Move(Profile.Items, item, dropRing.Source, index);
        }

        Rebuild(animate: true);
        if (!moved && dragged != null)
        {
            // nothing changed: the icon returns to its place (Rebuild animates it from the pointer position)
            dragged.X.Go(dragged.SlotX, 160);
            dragged.Y.Go(dragged.SlotY, 160);
        }

        _hot = HitResult.None;
        if (_hasPointer)
        {
            SetHot(HitTest(p));
        }
    }

    // ---- drag & drop of files onto the menu --------------------------------------

    /// <summary>Finds the list and position where dropped items should be inserted.</summary>
    public bool ResolveDrop(Point p, out ObservableList<RadialItem> list, out int index, out RadialItem? group)
    {
        list = Profile.Items;
        index = list.Count;
        group = null;
        if (!_open)
        {
            return false;
        }

        foreach (var (ring, visual, i) in AllVisuals())
        {
            if (visual.Item is { IsGroup: true } g)
            {
                double dx = p.X - visual.X.Value, dy = p.Y - visual.Y.Value;
                if (Math.Sqrt(dx * dx + dy * dy) < visual.Size * 0.5)
                {
                    list = g.Children;
                    index = g.Children.Count;
                    group = g;
                    return true;
                }
            }
        }

        // the orbit closest to the pointer
        double radius = Math.Sqrt(p.X * p.X + p.Y * p.Y);
        RingView? target = null;
        double best = double.MaxValue;
        foreach (var ring in _rings)
        {
            if (ring.Layout == null || ring.Source == null)
            {
                continue;
            }

            double d = Math.Abs(ring.Layout.Radius - radius);
            if (d < best)
            {
                best = d;
                target = ring;
            }
        }

        if (target?.Source == null || target.Layout == null)
        {
            // search results or an empty menu: append to the current level
            var level = DeepestRing?.Source ?? Profile.Items;
            list = level;
            index = level.Count;
            return true;
        }

        double angle = RadialLayout.RadToDeg(Math.Atan2(p.Y, p.X));
        int slot = LayoutPlanner.NearestSlot(target.Layout, angle);
        list = target.Source;
        index = Math.Clamp(slot < 0 ? target.ItemCount : slot, 0, target.ItemCount);
        return true;
    }

    /// <summary>Highlights the group under the pointer while files are dragged over the menu.</summary>
    public void DragOverFiles(Point p)
    {
        if (!_open)
        {
            return;
        }

        _lastPointer = p;
        _hasPointer = true;
        var hit = HitTest(p);
        SetHot(hit.Kind == HitKind.Item && hit.Visual?.Item is { IsGroup: true } ? hit : HitResult.None);
    }

    public void ItemsAdded()
    {
        Rebuild(animate: true);
    }
}
