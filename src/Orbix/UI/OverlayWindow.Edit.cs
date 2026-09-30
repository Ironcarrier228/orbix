using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Orbix.Core.Localization;
using Orbix.Core.Models;
using Orbix.Native;
using Orbix.Services;

namespace Orbix.UI;

/// <summary>Edit mode of the overlay: drag &amp; drop of files, context menus, add / rename / icon commands.</summary>
internal sealed partial class OverlayWindow
{
    private const int CmdRename = 1;
    private const int CmdChangeIcon = 2;
    private const int CmdResetIcon = 3;
    private const int CmdEditInSettings = 4;
    private const int CmdDelete = 5;
    private const int CmdAddFile = 10;
    private const int CmdAddFolder = 11;
    private const int CmdAddGroup = 12;
    private const int CmdAddLink = 13;
    private const int CmdAddCommand = 14;
    private const int CmdSystemBase = 100;

    private static readonly SystemActionKind[] AddableActions =
    {
        SystemActionKind.Lock,
        SystemActionKind.Sleep,
        SystemActionKind.Hibernate,
        SystemActionKind.SignOut,
        SystemActionKind.Restart,
        SystemActionKind.Shutdown,
        SystemActionKind.Screenshot,
        SystemActionKind.ShowDesktop,
        SystemActionKind.TaskManager,
        SystemActionKind.OrbixSettings,
    };

    private DispatcherTimer? _dragLeaveTimer;
    private bool _openedByDrag;

    private void InitializeEditSupport()
    {
        _menu.FocusRequested += () =>
        {
            Focus();
            System.Windows.Input.Keyboard.Focus(this);
        };
    }

    // ---- drag & drop ---------------------------------------------------------------

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _dragLeaveTimer?.Stop();
        if (!DropImport.CanImport(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!IsMenuOpen)
        {
            // Dragging something onto the orb reveals the menu, so the user can choose the place.
            _openedByDrag = true;
            OpenMenu(true);
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!DropImport.CanImport(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (IsMenuOpen)
        {
            _menu.DragOverFiles(e.GetPosition(_stage));
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (!_openedByDrag)
        {
            return;
        }

        // the drag left the window without a drop: close the menu that the drag opened
        _dragLeaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _dragLeaveTimer.Tick -= OnDragLeaveTimer;
        _dragLeaveTimer.Tick += OnDragLeaveTimer;
        _dragLeaveTimer.Stop();
        _dragLeaveTimer.Start();
    }

    private void OnDragLeaveTimer(object? sender, EventArgs e)
    {
        _dragLeaveTimer?.Stop();
        if (_openedByDrag && IsMenuOpen)
        {
            _openedByDrag = false;
            CloseMenu(false);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dragLeaveTimer?.Stop();
        _openedByDrag = false;
        e.Handled = true;

        var items = DropImport.FromData(e.Data);
        if (items.Count == 0 || !IsMenuOpen)
        {
            if (items.Count == 0 && IsMenuOpen)
            {
                _menu.ShowToast(Loc.T("Menu.DropNothing"), true);
            }

            return;
        }

        _menu.ResolveDrop(e.GetPosition(_stage), out var list, out int index, out var group);
        InsertItems(list, index, items);
        _menu.ShowToast(group != null
            ? Loc.T("Menu.AddedTo", items.Count, group.Name)
            : Loc.T("Menu.Added", items.Count));
    }

    private void InsertItems(ObservableList<RadialItem> list, int index, IReadOnlyList<RadialItem> items)
    {
        index = Math.Clamp(index, 0, list.Count);
        foreach (var item in items)
        {
            list.Insert(index++, item);
        }

        _icons.Preload(items, _config.Menu.ItemSize * 0.62 * _monitor.Scale);
        _menu.ItemsAdded();
    }

    // ---- context menus ---------------------------------------------------------------

    private void OnContextRequested(ContextRequest request)
    {
        var entries = request.Item == null ? BuildAddEntries() : BuildItemEntries(request.Item);

        int command;
        using (BeginModal())
        {
            Win32.GetCursorPos(out var pt);
            command = NativeMenu.Show(_hwnd, entries, pt.X, pt.Y);
        }

        if (command <= 0)
        {
            Activate();
            return;
        }

        try
        {
            ExecuteContextCommand(command, request);
        }
        catch (Exception ex)
        {
            Logger.Error("Context command failed", ex);
            _menu.ShowToast(ex.Message, true);
        }

        Activate();
    }

    private static List<TrayEntry> BuildItemEntries(RadialItem item)
    {
        var entries = new List<TrayEntry>
        {
            new(CmdRename, Loc.T("Ctx.Rename")),
            new(CmdChangeIcon, Loc.T("Ctx.ChangeIcon")),
            new(CmdResetIcon, Loc.T("Ctx.ResetIcon"), false, !string.IsNullOrEmpty(item.IconPath)),
            new(CmdEditInSettings, Loc.T("Ctx.EditInSettings")),
            TrayEntry.Separator,
            new(CmdDelete, Loc.T("Ctx.Delete")),
        };
        return entries;
    }

    private static List<TrayEntry> BuildAddEntries()
    {
        var system = new List<TrayEntry>();
        for (int i = 0; i < AddableActions.Length; i++)
        {
            system.Add(new TrayEntry(CmdSystemBase + i, SystemActionName(AddableActions[i])));
        }

        return new List<TrayEntry>
        {
            new(CmdAddFile, Loc.T("Ctx.AddFile")),
            new(CmdAddFolder, Loc.T("Ctx.AddFolder")),
            new(CmdAddGroup, Loc.T("Ctx.AddGroup")),
            new(CmdAddLink, Loc.T("Ctx.AddLink")),
            new(CmdAddCommand, Loc.T("Ctx.AddCommand")),
            new(0, Loc.T("Ctx.AddSystem"), false, true, system),
        };
    }

    /// <summary>Localized name of a system action (also used as the default item name).</summary>
    internal static string SystemActionName(SystemActionKind action) => action switch
    {
        SystemActionKind.Lock => Loc.T("Default.Lock"),
        SystemActionKind.Sleep => Loc.T("Default.Sleep"),
        SystemActionKind.Hibernate => Loc.T("Action.Hibernate"),
        SystemActionKind.SignOut => Loc.T("Default.SignOut"),
        SystemActionKind.Restart => Loc.T("Default.Restart"),
        SystemActionKind.Shutdown => Loc.T("Default.Shutdown"),
        SystemActionKind.Screenshot => Loc.T("Default.Screenshot"),
        SystemActionKind.ShowDesktop => Loc.T("Default.ShowDesktop"),
        SystemActionKind.TaskManager => Loc.T("Default.TaskManager"),
        SystemActionKind.OrbixSettings => Loc.T("Action.OrbixSettings"),
        _ => action.ToString(),
    };

    private void ExecuteContextCommand(int command, ContextRequest request)
    {
        var item = request.Item;

        if (command >= CmdSystemBase && command < CmdSystemBase + AddableActions.Length)
        {
            var action = AddableActions[command - CmdSystemBase];
            var created = new RadialItem { Name = SystemActionName(action), Kind = ItemKind.System, SystemAction = action };
            InsertItems(request.List, request.Index, new[] { created });
            return;
        }

        switch (command)
        {
            case CmdRename when item != null:
                _menu.BeginRename(item);
                break;
            case CmdChangeIcon when item != null:
                ChangeIcon(item);
                break;
            case CmdResetIcon when item != null:
                item.IconPath = null;
                _icons.Invalidate(item);
                _menu.Refresh();
                break;
            case CmdEditInSettings:
                SettingsRequested?.Invoke(item);
                break;
            case CmdDelete when item != null:
                _menu.DeleteItem(item);
                break;
            case CmdAddFile:
                AddFiles(request);
                break;
            case CmdAddFolder:
                AddFolder(request);
                break;
            case CmdAddGroup:
                var group = new RadialItem { Name = Loc.T("Menu.NewGroup"), Kind = ItemKind.Group };
                InsertItems(request.List, request.Index, new[] { group });
                _menu.BeginRename(group);
                break;
            case CmdAddLink:
                var link = new RadialItem { Name = Loc.T("Menu.NewLink"), Kind = ItemKind.Url, Target = "https://" };
                InsertItems(request.List, request.Index, new[] { link });
                SettingsRequested?.Invoke(link);
                break;
            case CmdAddCommand:
                var cmd = new RadialItem { Name = Loc.T("Menu.NewCommand"), Kind = ItemKind.Command, Target = string.Empty };
                InsertItems(request.List, request.Index, new[] { cmd });
                SettingsRequested?.Invoke(cmd);
                break;
        }
    }

    private void ChangeIcon(RadialItem item)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Dialog.ChooseIcon"),
            Filter = Loc.T("Dialog.ImageFilter"),
            CheckFileExists = true,
        };

        bool ok;
        using (BeginModal())
        {
            ok = dialog.ShowDialog(this) == true;
        }

        if (!ok)
        {
            return;
        }

        try
        {
            string extension = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
            if (extension is ".exe" or ".dll")
            {
                // keep the reference to the resource instead of copying a binary
                item.IconPath = dialog.FileName + ",0";
            }
            else
            {
                item.IconPath = _configService.ImportIconFile(dialog.FileName, item.Name);
            }

            _icons.Invalidate(item);
            _menu.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Error("Changing the icon failed", ex);
            _menu.ShowToast(ex.Message, true);
        }
    }

    private void AddFiles(ContextRequest request)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Dialog.ChooseFile"),
            Filter = Loc.T("Dialog.AppFilter"),
            Multiselect = true,
            CheckFileExists = true,
            DereferenceLinks = false,
        };

        bool ok;
        using (BeginModal())
        {
            ok = dialog.ShowDialog(this) == true;
        }

        if (!ok)
        {
            return;
        }

        var items = dialog.FileNames.Select(DropImport.FromPath).Where(i => i != null).Cast<RadialItem>().ToList();
        if (items.Count > 0)
        {
            InsertItems(request.List, request.Index, items);
        }
    }

    private void AddFolder(ContextRequest request)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Dialog.ChooseFolder") };

        bool ok;
        using (BeginModal())
        {
            ok = dialog.ShowDialog(this) == true;
        }

        if (!ok)
        {
            return;
        }

        var item = DropImport.FromPath(dialog.FolderName);
        if (item != null)
        {
            InsertItems(request.List, request.Index, new[] { item });
        }
    }
}
