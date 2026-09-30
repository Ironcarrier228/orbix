using System.Text.RegularExpressions;
using Orbix.Core.Hotkeys;
using Orbix.Core.Models;

namespace Orbix.Core.Services;

/// <summary>
/// Repairs a configuration that was edited by hand or written by another version:
/// duplicated ids, missing profiles, invalid hotkeys and colours, absurdly deep trees.
/// Numeric ranges are already enforced by the setters of the settings classes.
/// </summary>
public static class ConfigNormalizer
{
    private static readonly Regex HexColor = new("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    public static bool IsValidColor(string? color) => color != null && HexColor.IsMatch(color);

    public static void Normalize(AppConfig config, string defaultLanguage = "en")
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Profiles.Count == 0)
        {
            config.Profiles.Add(DefaultConfigFactory.CreateDefaultProfile(defaultLanguage));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int number = 1;
        foreach (var profile in config.Profiles)
        {
            if (!seen.Add(profile.Id))
            {
                profile.Id = RadialItem.NewId();
                seen.Add(profile.Id);
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = "Profile " + number;
            }

            number++;
            NormalizeItems(profile.Items, seen, 1);
        }

        if (config.Profiles.All(p => p.Id != config.ActiveProfileId))
        {
            config.ActiveProfileId = config.Profiles[0].Id;
        }

        NormalizeTriggers(config.Triggers);

        if (!IsValidColor(config.Appearance.Accent))
        {
            config.Appearance.Accent = AppearanceSettings.DefaultAccent;
        }

        if (config.Orb.Color != null && !IsValidColor(config.Orb.Color))
        {
            config.Orb.Color = null;
        }

        var lang = config.General.Language;
        if (lang is not ("auto" or "ru" or "en"))
        {
            config.General.Language = "auto";
        }
    }

    private static void NormalizeTriggers(TriggerSettings triggers)
    {
        if (!HotkeyGesture.TryParse(triggers.Hotkey, out var open) || !open.IsValid)
        {
            triggers.Hotkey = TriggerSettings.DefaultHotkey;
            open = HotkeyGesture.Parse(TriggerSettings.DefaultHotkey);
        }

        if (!HotkeyGesture.TryParse(triggers.ToggleOrbHotkey, out var toggle) || !toggle.IsValid)
        {
            triggers.ToggleOrbHotkey = TriggerSettings.DefaultToggleOrbHotkey;
            toggle = HotkeyGesture.Parse(TriggerSettings.DefaultToggleOrbHotkey);
        }

        // Canonical spelling ("ctrl+alt+space" -> "Ctrl+Alt+Space").
        triggers.Hotkey = open.ToString();
        triggers.ToggleOrbHotkey = toggle.ToString();

        if (open == toggle)
        {
            triggers.ToggleOrbHotkeyEnabled = false;
        }
    }

    private static void NormalizeItems(ObservableList<RadialItem> items, HashSet<string> seen, int depth)
    {
        foreach (var item in items)
        {
            if (!seen.Add(item.Id))
            {
                item.Id = RadialItem.NewId();
                seen.Add(item.Id);
            }

            item.Name = (item.Name ?? string.Empty).Trim();
            if (item.Name.Length == 0)
            {
                item.Name = DeriveName(item);
            }

            if (item.Color != null && !IsValidColor(item.Color))
            {
                item.Color = null;
            }

            if (depth >= ItemTree.MaxSupportedDepth)
            {
                item.Children.Clear();
            }
            else if (item.Children.Count > 0)
            {
                NormalizeItems(item.Children, seen, depth + 1);
            }
        }
    }

    private static string DeriveName(RadialItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Target))
        {
            if (item.Kind == ItemKind.Url &&
                Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) &&
                !string.IsNullOrEmpty(uri.Host))
            {
                return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            }

            var target = item.Target!.TrimEnd('\\', '/');
            int slash = target.LastIndexOfAny(new[] { '\\', '/' });
            var file = slash >= 0 ? target[(slash + 1)..] : target;
            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return file;
        }

        return item.Kind == ItemKind.Group ? "Group" : "Item";
    }
}
