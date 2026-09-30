using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Orbix.Core.Localization;
using Orbix.Core.Models;
using Orbix.Services;

namespace Orbix.UI.Settings;

/// <summary>
/// Edits one <see cref="RadialItem"/> in place: name, kind, target, arguments, run-as-admin, icon.
/// Every change is written straight into the item and reported through <see cref="Changed"/>.
/// </summary>
internal partial class ItemEditorPanel : UserControl
{
    private readonly IconService _icons;
    private RadialItem? _item;
    private bool _loading;

    /// <summary>Raised after every committed edit (the window saves the config there).</summary>
    public event Action<RadialItem>? Changed;

    public ItemEditorPanel(IconService icons)
    {
        _icons = icons;
        InitializeComponent();

        KindCombo.Items.Add(new ComboValue(ItemKind.App, Loc.T("Settings.Kind.App")));
        KindCombo.Items.Add(new ComboValue(ItemKind.Group, Loc.T("Settings.Kind.Group")));
        KindCombo.Items.Add(new ComboValue(ItemKind.Url, Loc.T("Settings.Kind.Url")));
        KindCombo.Items.Add(new ComboValue(ItemKind.Command, Loc.T("Settings.Kind.Command")));
        KindCombo.Items.Add(new ComboValue(ItemKind.System, Loc.T("Settings.Kind.System")));

        foreach (var action in new[]
                 {
                     SystemActionKind.Lock, SystemActionKind.Sleep, SystemActionKind.Hibernate, SystemActionKind.SignOut,
                     SystemActionKind.Restart, SystemActionKind.Shutdown, SystemActionKind.Screenshot,
                     SystemActionKind.ShowDesktop, SystemActionKind.TaskManager,
                 })
        {
            SystemCombo.Items.Add(new ComboValue(action, Loc.T(SystemActionNameKey(action))));
        }

        NameText.TextChanged += (_, _) => Commit(item => item.Name = NameText.Text);
        TargetText.TextChanged += (_, _) => Commit(item => item.Target = TargetText.Text.Trim());
        ArgsText.TextChanged += (_, _) => Commit(item => item.Arguments = ArgsText.Text);
        RunAsCheck.Checked += (_, _) => Commit(item => item.RunAsAdmin = true);
        RunAsCheck.Unchecked += (_, _) => Commit(item => item.RunAsAdmin = false);
        KindCombo.SelectionChanged += (_, _) => Commit(item =>
        {
            if (KindCombo.SelectedItem is ComboValue v && v.Value is ItemKind kind && item.Kind != kind)
            {
                item.Kind = kind;
                UpdateVisibility();
            }
        });
        SystemCombo.SelectionChanged += (_, _) => Commit(item =>
        {
            if (SystemCombo.SelectedItem is ComboValue v && v.Value is SystemActionKind action)
            {
                item.SystemAction = action;
            }
        });
        BrowseButton.Click += (_, _) => BrowseTarget();
        IconChangeButton.Click += (_, _) => BrowseIcon();
        IconResetButton.Click += (_, _) => Commit(item =>
        {
            item.IconPath = string.Empty;
            _icons.Invalidate(item);
            UpdateIcon();
        });
    }

    /// <summary>Rebuilds the localized combo labels, keeping the current selection.</summary>
    public void RefreshLocalized()
    {
        var kind = _item?.Kind;
        var action = _item?.SystemAction;
        KindCombo.Items.Clear();
        KindCombo.Items.Add(new ComboValue(ItemKind.App, Loc.T("Settings.Kind.App")));
        KindCombo.Items.Add(new ComboValue(ItemKind.Group, Loc.T("Settings.Kind.Group")));
        KindCombo.Items.Add(new ComboValue(ItemKind.Url, Loc.T("Settings.Kind.Url")));
        KindCombo.Items.Add(new ComboValue(ItemKind.Command, Loc.T("Settings.Kind.Command")));
        KindCombo.Items.Add(new ComboValue(ItemKind.System, Loc.T("Settings.Kind.System")));
        SystemCombo.Items.Clear();
        foreach (var a in new[]
                 {
                     SystemActionKind.Lock, SystemActionKind.Sleep, SystemActionKind.Hibernate, SystemActionKind.SignOut,
                     SystemActionKind.Restart, SystemActionKind.Shutdown, SystemActionKind.Screenshot,
                     SystemActionKind.ShowDesktop, SystemActionKind.TaskManager,
                 })
        {
            SystemCombo.Items.Add(new ComboValue(a, Loc.T(SystemActionNameKey(a))));
        }

        if (kind != null)
        {
            SelectCombo(KindCombo, kind);
        }

        if (action != null)
        {
            SelectCombo(SystemCombo, action);
        }
    }

    /// <summary>Shows the editor for an item (null hides it).</summary>
    public void Load(RadialItem? item)
    {
        _item = item;
        Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
        if (item == null)
        {
            return;
        }

        _loading = true;
        NameText.Text = item.Name;
        TargetText.Text = item.Target;
        ArgsText.Text = item.Arguments;
        RunAsCheck.IsChecked = item.RunAsAdmin;
        SelectCombo(KindCombo, item.Kind);
        SelectCombo(SystemCombo, item.SystemAction);
        _loading = false;
        UpdateVisibility();
        UpdateIcon();
    }

    private void Commit(Action<RadialItem> change)
    {
        if (_loading || _item == null)
        {
            return;
        }

        change(_item);
        Changed?.Invoke(_item);
    }

    private void UpdateVisibility()
    {
        if (_item == null)
        {
            return;
        }

        var kind = _item.Kind;
        TargetRow.Visibility = kind is ItemKind.App or ItemKind.Url ? Visibility.Visible : Visibility.Collapsed;
        ArgsRow.Visibility = kind is ItemKind.App or ItemKind.Command ? Visibility.Visible : Visibility.Collapsed;
        SystemRow.Visibility = kind == ItemKind.System ? Visibility.Visible : Visibility.Collapsed;
        RunAsRow.Visibility = kind == ItemKind.App ? Visibility.Visible : Visibility.Collapsed;
        IconRow.Visibility = kind == ItemKind.Group ? Visibility.Collapsed : Visibility.Visible;
        GroupHint.Visibility = kind == ItemKind.Group ? Visibility.Visible : Visibility.Collapsed;
        BrowseButton.Visibility = kind == ItemKind.App ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateIcon()
    {
        if (_item == null)
        {
            return;
        }

        var item = _item;
        var result = _icons.Get(item, 32, r => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(_item, item))
            {
                ApplyIcon(r, item);
            }
        })));
        ApplyIcon(result, item);
    }

    private void ApplyIcon(IconResult result, RadialItem item)
    {
        IconPreview.Source = result.Image ?? Glyphs.LetterTile(item.Name, false);
    }

    private void BrowseTarget()
    {
        if (_item == null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Dialog.ChooseFile"),
            Filter = Loc.T("Dialog.AppFilter"),
            CheckFileExists = false,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            TargetText.Text = dialog.FileName;
        }
    }

    private void BrowseIcon()
    {
        if (_item == null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Dialog.ChooseIcon"),
            Filter = Loc.T("Dialog.ImageFilter"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            var item = _item;
            item.IconPath = dialog.FileName;
            _icons.Invalidate(item);
            UpdateIcon();
            Changed?.Invoke(item);
        }
    }

    // ---- combo helpers (value + localized label) ----

    private sealed record ComboValue(object Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static void SelectCombo(ComboBox combo, object value)
    {
        foreach (var entry in combo.Items)
        {
            if (entry is ComboValue v && Equals(v.Value, value))
            {
                combo.SelectedItem = v;
                return;
            }
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    internal static string SystemActionNameKey(SystemActionKind action) => action switch
    {
        SystemActionKind.Lock => "Default.Lock",
        SystemActionKind.Sleep => "Default.Sleep",
        SystemActionKind.Hibernate => "Action.Hibernate",
        SystemActionKind.SignOut => "Default.SignOut",
        SystemActionKind.Restart => "Default.Restart",
        SystemActionKind.Shutdown => "Default.Shutdown",
        SystemActionKind.Screenshot => "Default.Screenshot",
        SystemActionKind.ShowDesktop => "Default.ShowDesktop",
        SystemActionKind.TaskManager => "Default.TaskManager",
        _ => "Default.System",
    };
}
