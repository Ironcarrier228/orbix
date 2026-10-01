using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Orbix.Core.Layout;
using Orbix.Core.Localization;
using Orbix.Core.Models;

namespace Orbix.UI;

/// <summary>Tooltip, search pill, edit banner and toast of the menu.</summary>
internal sealed partial class MenuView
{
    private static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");

    private readonly Border _label = new();
    private readonly TextBlock _labelName = new();
    private readonly TextBlock _labelSub = new();
    private readonly Border _searchPill = new();
    private readonly TextBlock _searchText = new();
    private readonly Canvas _searchIcon = new() { Width = 16, Height = 16 };
    private readonly Border _editPill = new();
    private readonly TextBlock _editText = new();
    private readonly Border _toast = new();
    private readonly TextBlock _toastText = new();
    private DispatcherTimer? _toastTimer;
    private AnimatedDouble? _aLabel;
    private AnimatedDouble? _aToast;
    private bool _toastError;

    private void BuildOverlay()
    {
        _aLabel = _anim.Create(0, v => _label.Opacity = v);
        _aToast = _anim.Create(0, v => _toast.Opacity = v);

        // tooltip
        var stack = new StackPanel();
        _labelName.FontFamily = UiFont;
        _labelName.FontSize = 13;
        _labelName.FontWeight = FontWeights.SemiBold;
        _labelName.TextTrimming = TextTrimming.CharacterEllipsis;
        _labelName.MaxWidth = 250;
        _labelSub.FontFamily = UiFont;
        _labelSub.FontSize = 11;
        _labelSub.TextTrimming = TextTrimming.CharacterEllipsis;
        _labelSub.MaxWidth = 250;
        stack.Children.Add(_labelName);
        stack.Children.Add(_labelSub);
        _label.Child = stack;
        _label.CornerRadius = new CornerRadius(9);
        _label.Padding = new Thickness(11, 6, 11, 6);
        _label.BorderThickness = new Thickness(1);
        _label.Visibility = Visibility.Collapsed;
        _label.IsHitTestVisible = false;
        _label.Opacity = 0;

        // search pill
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
        _searchIcon.VerticalAlignment = VerticalAlignment.Center;
        _searchIcon.Margin = new Thickness(0, 0, 7, 0);
        _searchText.FontFamily = UiFont;
        _searchText.FontSize = 14;
        _searchText.VerticalAlignment = VerticalAlignment.Center;
        searchRow.Children.Add(_searchIcon);
        searchRow.Children.Add(_searchText);
        _searchPill.Child = searchRow;
        _searchPill.CornerRadius = new CornerRadius(16);
        _searchPill.Padding = new Thickness(12, 6, 14, 6);
        _searchPill.BorderThickness = new Thickness(1);
        _searchPill.Visibility = Visibility.Collapsed;
        _searchPill.IsHitTestVisible = false;

        // edit banner
        _editText.FontFamily = UiFont;
        _editText.FontSize = 12;
        _editPill.Child = _editText;
        _editPill.CornerRadius = new CornerRadius(14);
        _editPill.Padding = new Thickness(12, 5, 12, 5);
        _editPill.BorderThickness = new Thickness(1);
        _editPill.Visibility = Visibility.Collapsed;
        _editPill.IsHitTestVisible = false;

        // toast
        _toastText.FontFamily = UiFont;
        _toastText.FontSize = 13;
        _toastText.TextWrapping = TextWrapping.Wrap;
        _toastText.MaxWidth = 320;
        _toast.Child = _toastText;
        _toast.CornerRadius = new CornerRadius(12);
        _toast.Padding = new Thickness(14, 8, 14, 8);
        _toast.BorderThickness = new Thickness(1);
        _toast.Visibility = Visibility.Collapsed;
        _toast.IsHitTestVisible = false;
        _toast.Opacity = 0;

        OverlayLayer.Children.Add(_label);
        OverlayLayer.Children.Add(_searchPill);
        OverlayLayer.Children.Add(_editPill);
        OverlayLayer.Children.Add(_toast);
    }

    /// <summary>Applies the colours of the current palette (called on every opening).</summary>
    private void ConfigureOverlay()
    {
        var p = _palette;
        _label.Background = p.PanelBrush;
        _label.BorderBrush = p.PanelBorder;
        _labelName.Foreground = p.TextBrush;
        _labelSub.Foreground = p.MutedBrush;

        _searchPill.Background = p.PanelBrush;
        _searchPill.BorderBrush = p.AccentBrush;
        _searchText.Foreground = p.TextBrush;
        _searchIcon.Children.Clear();
        var icon = Glyphs.CreatePath("search", p.AccentBrush, 16, 2.2);
        ItemVisual.Place(icon, 8, 8);
        _searchIcon.Children.Add(icon);

        _editPill.Background = p.PanelBrush;
        _editPill.BorderBrush = p.PanelBorder;
        _editText.Foreground = p.MutedBrush;
        _editText.Text = Loc.T("Menu.EditBanner");

        _toast.Background = p.PanelBrush;
        _toast.BorderBrush = p.PanelBorder;
        _toastText.Foreground = p.TextBrush;
    }

    private double Half => _session?.HalfExtent ?? 300;

    private static void PlaceCentered(FrameworkElement element, double centerX, double centerY, double maxWidth)
    {
        element.Measure(new Size(maxWidth, double.PositiveInfinity));
        var size = element.DesiredSize;
        Canvas.SetLeft(element, centerX - size.Width / 2);
        Canvas.SetTop(element, centerY - size.Height / 2);
    }

    // ---- tooltip ---------------------------------------------------------------

    private void ShowLabel(string title, string? sub, double x, double y, double itemRadius, double angleDeg)
    {
        if (!_config.Menu.ShowTooltips || _aLabel == null)
        {
            return;
        }

        _labelName.Text = title;
        _labelSub.Text = sub ?? string.Empty;
        _labelSub.Visibility = string.IsNullOrWhiteSpace(sub) ? Visibility.Collapsed : Visibility.Visible;
        _label.Visibility = Visibility.Visible;
        _label.Measure(new Size(300, double.PositiveInfinity));
        var size = _label.DesiredSize;

        double half = Half;
        var rect = LabelPlacer.Place(x, y, itemRadius, angleDeg, size.Width, size.Height, half, half);
        Canvas.SetLeft(_label, rect.X);
        Canvas.SetTop(_label, rect.Y);
        _aLabel.Go(1, 90);
    }

    private void HideLabel()
    {
        if (_aLabel == null)
        {
            return;
        }

        _aLabel.Go(0, 60, 0, Easing.Linear, () =>
        {
            if (_aLabel.Target == 0)
            {
                _label.Visibility = Visibility.Collapsed;
            }
        });
    }

    // ---- search pill / edit banner ----------------------------------------------

    private void UpdateSearchPill()
    {
        if (!_search.IsActive)
        {
            _searchPill.Visibility = Visibility.Collapsed;
            UpdateEditBanner();
            return;
        }

        bool none = !_search.HasMatches;
        _searchText.Text = none ? _query + "  ·  " + Loc.T("Menu.SearchNone") : _query;
        _searchPill.BorderBrush = none ? _palette.Danger : _palette.AccentBrush;
        _searchPill.Visibility = Visibility.Visible;
        PlaceCentered(_searchPill, 0, -Half + 24, 420);
        _editPill.Visibility = Visibility.Collapsed;
    }

    private void UpdateEditBanner()
    {
        if (_search.IsActive)
        {
            return;
        }

        _editPill.Visibility = _edit ? Visibility.Visible : Visibility.Collapsed;
        if (_edit)
        {
            PlaceCentered(_editPill, 0, -Half + 22, 520);
        }
    }

    // ---- toast -----------------------------------------------------------------

    public void ShowToast(string text, bool error = false)
    {
        if (_aToast == null)
        {
            return;
        }

        _toastError = error;
        _toastText.Text = text;
        _toast.Background = error ? _palette.Danger : _palette.PanelBrush;
        _toastText.Foreground = error ? Brushes.White : _palette.TextBrush;
        _toast.BorderBrush = error ? _palette.Danger : _palette.PanelBorder;
        _toast.Visibility = Visibility.Visible;
        PlaceCentered(_toast, 0, Half - 30, 340);
        _aToast.Go(1, 120);

        _toastTimer ??= new DispatcherTimer();
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(error ? 3200 : 2400);
        _toastTimer.Tick -= OnToastTimer;
        _toastTimer.Tick += OnToastTimer;
        _toastTimer.Start();
    }

    private void OnToastTimer(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        HideToast();
    }

    private void HideToast()
    {
        if (_aToast == null)
        {
            return;
        }

        _toastTimer?.Stop();
        _aToast.Go(0, 120, 0, Easing.Linear, () =>
        {
            if (_aToast.Target == 0)
            {
                _toast.Visibility = Visibility.Collapsed;
            }
        });
    }

    internal bool ToastIsError => _toastError;

    // ---- inline rename (edit mode) ---------------------------------------------

    private TextBox? _renameBox;
    private RadialItem? _renaming;

    public bool IsRenaming => _renameBox != null && _renaming != null;

    /// <summary>The window should take the keyboard focus back (after the rename box closed).</summary>
    public event Action? FocusRequested;

    /// <summary>Shows a text box under the item; Enter confirms, Esc cancels.</summary>
    public void BeginRename(RadialItem item)
    {
        var visual = FindVisual(item);
        if (visual == null || !_open)
        {
            return;
        }

        EndRename(false);
        _renaming = item;
        var box = new TextBox
        {
            Text = item.Name,
            MinWidth = 150,
            MaxWidth = 280,
            FontFamily = UiFont,
            FontSize = 13,
            Padding = new Thickness(8, 5, 8, 5),
            Background = _palette.PanelBrush,
            Foreground = _palette.TextBrush,
            BorderBrush = _palette.AccentBrush,
            BorderThickness = new Thickness(1.6),
            CaretBrush = _palette.TextBrush,
            SelectionBrush = _palette.AccentSoft,
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                EndRename(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                EndRename(false);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (IsRenaming)
            {
                EndRename(true);
            }
        };

        box.Measure(new Size(300, double.PositiveInfinity));
        var size = box.DesiredSize;
        double x = visual.X.Value - size.Width / 2;
        double y = visual.Y.Value + visual.Size / 2 + 8;
        double half = Half;
        x = Math.Clamp(x, -half + 4, Math.Max(-half + 4, half - size.Width - 4));
        y = Math.Clamp(y, -half + 4, Math.Max(-half + 4, half - size.Height - 4));
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        _renameBox = box;
        OverlayLayer.Children.Add(box);
        box.Focus();
        Keyboard.Focus(box);
        box.SelectAll();
    }

    private void EndRename(bool commit)
    {
        var box = _renameBox;
        var item = _renaming;
        _renameBox = null;
        _renaming = null;
        if (box == null)
        {
            return;
        }

        OverlayLayer.Children.Remove(box);
        if (commit && item != null)
        {
            var text = box.Text.Trim();
            if (text.Length > 0 && text != item.Name)
            {
                item.Name = text;
                _icons.Invalidate(item);
                Rebuild(animate: false);
            }
        }

        FocusRequested?.Invoke();
    }
}
