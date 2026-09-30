using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Orbix.Core.Hotkeys;
using Orbix.Core.Localization;
using Orbix.Core.Models;
using Orbix.Core.Services;
using Orbix.Services;

namespace Orbix.UI.Settings;

/// <summary>
/// The settings window: six pages (general, orb, menu, items, appearance, about). Controls edit the
/// live <see cref="AppConfig"/>; every change is autosaved through <see cref="ConfigService"/> and
/// reported to the host so the overlay re-reads its geometry and colours.
/// </summary>
internal partial class SettingsWindow : Window
{
    private static readonly (string Hex, string Name)[] AccentPresets =
    {
        ("#6C63FF", "Iris"), ("#0EA5E9", "Sky"), ("#22C55E", "Green"),
        ("#F59E0B", "Amber"), ("#FF6B6B", "Coral"), ("#EC4899", "Pink"),
    };

    private readonly ConfigService _config;
    private readonly ThemeService _theme;
    private readonly IconService _icons;
    private readonly string _dataDirectory;
    private readonly Action _onChanged;
    private readonly ItemEditorPanel _editor;
    private readonly HotkeyBox _hotOpen = new();
    private readonly HotkeyBox _hotToggle = new();
    private HotkeyService? _hotkeys;
    private bool _loading;

    public SettingsWindow(ConfigService config, ThemeService theme, IconService icons, string dataDirectory, Action onChanged)
    {
        _config = config;
        _theme = theme;
        _icons = icons;
        _dataDirectory = dataDirectory;
        _onChanged = onChanged;
        InitializeComponent();

        _editor = new ItemEditorPanel(icons);
        _editor.Changed += item =>
        {
            _icons.Invalidate(item);
            RefreshItemList();
            Save();
        };
        EditorHost.Content = _editor;
        HotOpenHost.Content = _hotOpen;
        HotToggleHost.Content = _hotToggle;

        BuildCombos();
        BuildAccentSwatches();
        WireEvents();
        LoadAll();

        VersionText.Text = "Orbix " + FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? string.Empty).FileVersion;
        DataDirText.Text = dataDirectory;
    }

    /// <summary>Called by the controller when the global hotkey registration state changes.</summary>
    public void SetHotkeyService(HotkeyService hotkeys)
    {
        _hotkeys = hotkeys;
        hotkeys.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateHotkeyStates));
        UpdateHotkeyStates();
    }

    public void SelectPage(SettingsPage page)
    {
        Nav.SelectedIndex = (int)page;
        ShowPage(Nav.SelectedIndex);
    }

    /// <summary>Highlights an item on the Items page and loads it into the editor.</summary>
    public void SelectItem(RadialItem item)
    {
        SelectPage(SettingsPage.Items);
        if (!ItemsList.Items.Contains(item))
        {
            RefreshItemList();
        }

        ItemsList.SelectedItem = item;
        _editor.Load(item);
    }

    // ---- construction ----

    private void BuildCombos()
    {
        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(new ComboValue("ru", "Русский"));
        LanguageCombo.Items.Add(new ComboValue("en", "English"));

        VisibilityCombo.Items.Clear();
        VisibilityCombo.Items.Add(new ComboValue(OrbVisibilityMode.Always, Loc.T("Settings.Visibility.Always")));
        VisibilityCombo.Items.Add(new ComboValue(OrbVisibilityMode.DesktopOnly, Loc.T("Settings.Visibility.DesktopOnly")));
        VisibilityCombo.Items.Add(new ComboValue(OrbVisibilityMode.AutoHideFullscreen, Loc.T("Settings.Visibility.AutoHide")));

        MonitorCombo.Items.Clear();
        MonitorCombo.Items.Add(new ComboValue(MonitorMode.Primary, Loc.T("Settings.Monitor.Primary")));
        MonitorCombo.Items.Add(new ComboValue(MonitorMode.Cursor, Loc.T("Settings.Monitor.Cursor")));

        SubmenuCombo.Items.Clear();
        SubmenuCombo.Items.Add(new ComboValue(SubmenuOpenMode.Hover, Loc.T("Settings.SubmenuOpen.Hover")));
        SubmenuCombo.Items.Add(new ComboValue(SubmenuOpenMode.Click, Loc.T("Settings.SubmenuOpen.Click")));

        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add(new ComboValue(ThemeMode.Dark, Loc.T("Settings.Theme.Dark")));
        ThemeCombo.Items.Add(new ComboValue(ThemeMode.Light, Loc.T("Settings.Theme.Light")));
        ThemeCombo.Items.Add(new ComboValue(ThemeMode.System, Loc.T("Settings.Theme.System")));
    }

    private void BuildAccentSwatches()
    {
        AccentSwatches.Children.Clear();
        foreach (var (hex, name) in AccentPresets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = name,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                SystemAccentCheck.IsChecked = false;
                AccentText.Text = hex;
            };
            AccentSwatches.Children.Add(swatch);
        }
    }

    private void WireEvents()
    {
        Nav.SelectionChanged += (_, _) => ShowPage(Nav.SelectedIndex);

        LanguageCombo.SelectionChanged += (_, _) => Commit(c =>
        {
            var code = ComboValueOf(LanguageCombo) as string ?? "ru";
            if (c.General.Language != code)
            {
                c.General.Language = code;
                Loc.SetLanguage(code);
                LocBinder.Refresh();
                RebuildLocalized();
            }
        });
        AutostartCheck.Checked += (_, _) => AutostartService.SetEnabled(true);
        AutostartCheck.Unchecked += (_, _) => AutostartService.SetEnabled(false);

        HotkeyEnabledCheck.Checked += (_, _) => Commit(c => c.Triggers.HotkeyEnabled = true);
        HotkeyEnabledCheck.Unchecked += (_, _) => Commit(c => c.Triggers.HotkeyEnabled = false);
        _hotOpen.GestureChanged += g => Commit(c => c.Triggers.Hotkey = g.IsEmpty ? string.Empty : g.ToString());
        _hotToggle.GestureChanged += g => Commit(c => c.Triggers.ToggleOrbHotkey = g.IsEmpty ? string.Empty : g.ToString());

        ExportButton.Click += (_, _) => Export();
        ImportButton.Click += (_, _) => Import();
        OpenDirButton.Click += (_, _) => OpenDataDirectory();

        OrbSizeSlider.ValueChanged += (_, e) => Commit(c => c.Orb.Size = e.NewValue, refreshLabels: true);
        OrbOpacitySlider.ValueChanged += (_, e) => Commit(c => c.Orb.RestingOpacity = e.NewValue / 100.0, refreshLabels: true);
        BreathingCheck.Checked += (_, _) => Commit(c => c.Orb.Breathing = true);
        BreathingCheck.Unchecked += (_, _) => Commit(c => c.Orb.Breathing = false);
        VisibilityCombo.SelectionChanged += (_, _) => Commit(c => c.Orb.Visibility = (OrbVisibilityMode)(ComboValueOf(VisibilityCombo) ?? OrbVisibilityMode.Always));
        MonitorCombo.SelectionChanged += (_, _) => Commit(c => c.Orb.Monitor = (MonitorMode)(ComboValueOf(MonitorCombo) ?? MonitorMode.Primary));
        AllowMoveCheck.Checked += (_, _) => Commit(c => c.Orb.AllowMove = true);
        AllowMoveCheck.Unchecked += (_, _) => Commit(c => c.Orb.AllowMove = false);
        OrbColorText.TextChanged += (_, _) => Commit(c => c.Orb.Color = string.IsNullOrWhiteSpace(OrbColorText.Text) ? null : OrbColorText.Text.Trim());
        OrbColorClear.Click += (_, _) => OrbColorText.Text = string.Empty;
        OrbImageText.TextChanged += (_, _) => Commit(c => c.Orb.ImagePath = string.IsNullOrWhiteSpace(OrbImageText.Text) ? null : OrbImageText.Text.Trim());
        OrbImageBrowse.Click += (_, _) => Browse(Loc.T("Dialog.ImageFilter"), path => OrbImageText.Text = path);
        OrbImageClear.Click += (_, _) => OrbImageText.Text = string.Empty;

        RadiusSlider.ValueChanged += (_, e) => Commit(c => c.Menu.Radius = e.NewValue, refreshLabels: true);
        ItemSizeSlider.ValueChanged += (_, e) => Commit(c => c.Menu.ItemSize = e.NewValue, refreshLabels: true);
        HoverDelaySlider.ValueChanged += (_, e) => Commit(c => c.Menu.HoverDelayMs = (int)e.NewValue, refreshLabels: true);
        SubmenuCombo.SelectionChanged += (_, _) => Commit(c => c.Menu.SubmenuOpen = (SubmenuOpenMode)(ComboValueOf(SubmenuCombo) ?? SubmenuOpenMode.Hover));
        WheelCheck.Checked += (_, _) => Commit(c => c.Menu.WheelControl = true);
        WheelCheck.Unchecked += (_, _) => Commit(c => c.Menu.WheelControl = false);
        KeyboardCheck.Checked += (_, _) => Commit(c => c.Menu.KeyboardControl = true);
        KeyboardCheck.Unchecked += (_, _) => Commit(c => c.Menu.KeyboardControl = false);
        TooltipsCheck.Checked += (_, _) => Commit(c => c.Menu.ShowTooltips = true);
        TooltipsCheck.Unchecked += (_, _) => Commit(c => c.Menu.ShowTooltips = false);
        RingsCheck.Checked += (_, _) => Commit(c => c.Menu.ShowOrbitRings = true);
        RingsCheck.Unchecked += (_, _) => Commit(c => c.Menu.ShowOrbitRings = false);
        ConfirmCheck.Checked += (_, _) => Commit(c => c.Menu.ConfirmDangerous = true);
        ConfirmCheck.Unchecked += (_, _) => Commit(c => c.Menu.ConfirmDangerous = false);
        AnimationsCheck.Checked += (_, _) => Commit(c => c.Menu.Animations = true);
        AnimationsCheck.Unchecked += (_, _) => Commit(c => c.Menu.Animations = false);
        BlurCheck.Checked += (_, _) => Commit(c => c.Menu.BlurBackground = true);
        BlurCheck.Unchecked += (_, _) => Commit(c => c.Menu.BlurBackground = false);
        BlurOpacitySlider.ValueChanged += (_, e) => Commit(c => c.Menu.BackdropOpacity = e.NewValue / 100.0, refreshLabels: true);

        ItemsList.SelectionChanged += (_, _) => _editor.Load(SelectedItem);
        AddAppButton.Click += (_, _) => AddItem(ItemKind.App);
        AddGroupButton.Click += (_, _) => AddItem(ItemKind.Group);
        AddLinkButton.Click += (_, _) => AddItem(ItemKind.Url);
        AddCommandButton.Click += (_, _) => AddItem(ItemKind.Command);
        AddSystemButton.Click += (_, _) => AddItem(ItemKind.System);
        MoveUpButton.Click += (_, _) => MoveSelected(-1);
        MoveDownButton.Click += (_, _) => MoveSelected(+1);
        DeleteButton.Click += (_, _) => DeleteSelected();

        ThemeCombo.SelectionChanged += (_, _) => Commit(c => c.Appearance.Theme = (ThemeMode)(ComboValueOf(ThemeCombo) ?? ThemeMode.System));
        SystemAccentCheck.Checked += (_, _) => Commit(c => c.Appearance.UseSystemAccent = true);
        SystemAccentCheck.Unchecked += (_, _) => Commit(c => c.Appearance.UseSystemAccent = false);
        AccentText.TextChanged += (_, _) => Commit(c =>
        {
            if (!string.IsNullOrWhiteSpace(AccentText.Text))
            {
                c.Appearance.Accent = AccentText.Text.Trim();
            }
        });
    }

    // ---- load / refresh ----

    private void LoadAll()
    {
        _loading = true;
        var c = _config.Config;

        SelectCombo(LanguageCombo, c.General.Language);
        AutostartCheck.IsChecked = AutostartService.IsEnabled();
        HotkeyEnabledCheck.IsChecked = c.Triggers.HotkeyEnabled;
        _hotOpen.Gesture = HotkeyGesture.TryParse(c.Triggers.Hotkey, out var g1) ? g1 : HotkeyGesture.None;
        _hotToggle.Gesture = HotkeyGesture.TryParse(c.Triggers.ToggleOrbHotkey, out var g2) ? g2 : HotkeyGesture.None;

        OrbSizeSlider.Value = c.Orb.Size;
        OrbOpacitySlider.Value = c.Orb.RestingOpacity * 100;
        BreathingCheck.IsChecked = c.Orb.Breathing;
        SelectCombo(VisibilityCombo, c.Orb.Visibility);
        SelectCombo(MonitorCombo, c.Orb.Monitor);
        AllowMoveCheck.IsChecked = c.Orb.AllowMove;
        OrbColorText.Text = c.Orb.Color ?? string.Empty;
        OrbImageText.Text = c.Orb.ImagePath ?? string.Empty;

        RadiusSlider.Value = c.Menu.Radius;
        ItemSizeSlider.Value = c.Menu.ItemSize;
        HoverDelaySlider.Value = c.Menu.HoverDelayMs;
        SelectCombo(SubmenuCombo, c.Menu.SubmenuOpen);
        WheelCheck.IsChecked = c.Menu.WheelControl;
        KeyboardCheck.IsChecked = c.Menu.KeyboardControl;
        TooltipsCheck.IsChecked = c.Menu.ShowTooltips;
        RingsCheck.IsChecked = c.Menu.ShowOrbitRings;
        ConfirmCheck.IsChecked = c.Menu.ConfirmDangerous;
        AnimationsCheck.IsChecked = c.Menu.Animations;
        BlurCheck.IsChecked = c.Menu.BlurBackground;
        BlurOpacitySlider.Value = c.Menu.BackdropOpacity * 100;

        SelectCombo(ThemeCombo, c.Appearance.Theme);
        SystemAccentCheck.IsChecked = c.Appearance.UseSystemAccent;
        AccentText.Text = c.Appearance.Accent;

        RefreshItemList();
        RefreshLabels();
        _loading = false;
        UpdateHotkeyStates();
    }

    /// <summary>Rebuilds the localized combo labels and the item list after a language switch.</summary>
    private void RebuildLocalized()
    {
        var c = _config.Config;
        BuildCombos();
        SelectCombo(LanguageCombo, c.General.Language);
        SelectCombo(VisibilityCombo, c.Orb.Visibility);
        SelectCombo(MonitorCombo, c.Orb.Monitor);
        SelectCombo(SubmenuCombo, c.Menu.SubmenuOpen);
        SelectCombo(ThemeCombo, c.Appearance.Theme);
        _editor.RefreshLocalized();
        RefreshItemList();
    }

    private void RefreshLabels()
    {
        OrbSizeVal.Text = ((int)OrbSizeSlider.Value).ToString();
        OrbOpacityVal.Text = ((int)OrbOpacitySlider.Value).ToString() + "%";
        RadiusVal.Text = ((int)RadiusSlider.Value).ToString();
        ItemSizeVal.Text = ((int)ItemSizeSlider.Value).ToString();
        HoverDelayVal.Text = ((int)HoverDelaySlider.Value).ToString() + " ms";
        BlurOpacityVal.Text = ((int)BlurOpacitySlider.Value).ToString() + "%";
    }

    private void ShowPage(int index)
    {
        FrameworkElement[] pages = { PageGeneral, PageOrb, PageMenu, PageItems, PageAppearance, PageAbout };
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---- items page ----

    private RadialItem? SelectedItem => ItemsList.SelectedItem as RadialItem;

    private void RefreshItemList()
    {
        var selected = SelectedItem;
        ItemsList.Items.Clear();
        foreach (var item in _config.Config.ActiveProfile.Items)
        {
            ItemsList.Items.Add(item);
        }

        if (selected != null && ItemsList.Items.Contains(selected))
        {
            ItemsList.SelectedItem = selected;
        }
    }

    private void AddItem(ItemKind kind)
    {
        var item = new RadialItem
        {
            Kind = kind,
            Name = kind switch
            {
                ItemKind.Group => Loc.T("Menu.NewGroup"),
                ItemKind.Url => Loc.T("Menu.NewLink"),
                ItemKind.Command => Loc.T("Menu.NewCommand"),
                ItemKind.System => Loc.T("Default.Lock"),
                _ => Loc.T("Menu.NewItem"),
            },
        };
        if (kind == ItemKind.System)
        {
            item.SystemAction = SystemActionKind.Lock;
        }

        _config.Config.ActiveProfile.Items.Add(item);
        RefreshItemList();
        ItemsList.SelectedItem = item;
        _editor.Load(item);
        Save();
    }

    private void MoveSelected(int delta)
    {
        var list = _config.Config.ActiveProfile.Items;
        var index = list.IndexOf(SelectedItem!);
        if (SelectedItem == null || index < 0)
        {
            return;
        }

        var target = index + delta;
        if (target < 0 || target >= list.Count)
        {
            return;
        }

        var item = list[index];
        list.RemoveAt(index);
        list.Insert(target, item);
        RefreshItemList();
        ItemsList.SelectedItem = item;
        Save();
    }

    private void DeleteSelected()
    {
        var item = SelectedItem;
        if (item == null)
        {
            return;
        }

        _config.Config.ActiveProfile.Items.Remove(item);
        _editor.Load(null);
        RefreshItemList();
        Save();
    }

    // ---- config io ----

    private void Commit(Action<AppConfig> change, bool refreshLabels = false)
    {
        if (_loading)
        {
            return;
        }

        change(_config.Config);
        if (refreshLabels)
        {
            RefreshLabels();
        }

        Save();
    }

    private void Save()
    {
        _config.RequestSave();
        _onChanged();
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Settings.Export"),
            Filter = "JSON|*.json",
            FileName = "orbix-config.json",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _config.Export(dialog.FileName);
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Settings.Import"),
            Filter = "JSON|*.json",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = _config.Import(dialog.FileName);
        MessageBox.Show(this,
            result.Success ? Loc.T("Settings.ImportOk") : Loc.T("Settings.ImportFail", result.Error ?? "?"),
            Loc.T("Settings.Title"),
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (result.Success)
        {
            LoadAll();
            Save();
        }
    }

    private void OpenDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _dataDirectory) { UseShellExecute = false });
        }
        catch (Exception e)
        {
            Logger.Warn("Cannot open the data directory: " + e.Message);
        }
    }

    private void Browse(string filter, Action<string> apply)
    {
        var dialog = new OpenFileDialog { Title = Loc.T("Dialog.ChooseFile"), Filter = filter, CheckFileExists = false };
        if (dialog.ShowDialog(this) == true)
        {
            apply(dialog.FileName);
        }
    }

    private void UpdateHotkeyStates()
    {
        if (_hotkeys == null)
        {
            return;
        }

        HotOpenState.Text = HotkeyStateText(_hotkeys.OpenMenuState);
        HotToggleState.Text = HotkeyStateText(_hotkeys.ToggleOrbState);
    }

    private string HotkeyStateText(HotkeyState state) => state switch
    {
        HotkeyState.Registered => Loc.T("Settings.HotkeyState.Registered"),
        HotkeyState.Conflict => Loc.T("Settings.HotkeyState.Conflict"),
        HotkeyState.Invalid => Loc.T("Settings.HotkeyState.Invalid"),
        HotkeyState.Suspended => Loc.T("Settings.HotkeyState.Suspended"),
        _ => Loc.T("Settings.HotkeyState.Disabled"),
    };

    // ---- combo helpers ----

    private sealed record ComboValue(object Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static object? ComboValueOf(ComboBox combo) => (combo.SelectedItem as ComboValue)?.Value;

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
    }
}
