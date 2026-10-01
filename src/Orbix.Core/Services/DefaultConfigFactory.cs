using Orbix.Core.Localization;
using Orbix.Core.Models;

namespace Orbix.Core.Services;

/// <summary>Creates the configuration used on the first start: a useful set of items that exists on any Windows.</summary>
public static class DefaultConfigFactory
{
    public static AppConfig Create(string language = "en")
    {
        var config = new AppConfig();
        config.General.Language = "auto";
        var profile = CreateDefaultProfile(language);
        config.Profiles.Add(profile);
        config.ActiveProfileId = profile.Id;
        return config;
    }

    /// <summary>
    /// Root level: 8 items. "System" is a group with a nested "Power" group, which demonstrates three levels of orbits.
    /// </summary>
    public static MenuProfile CreateDefaultProfile(string language)
    {
        string T(string key) => Loc.In(language, key);

        var profile = new MenuProfile { Name = T("Default.Profile") };
        var items = profile.Items;

        items.Add(App(T("Default.Explorer"), "explorer.exe"));
        items.Add(App(T("Default.Notepad"), "notepad.exe"));
        items.Add(App(T("Default.Calculator"), "calc.exe"));
        items.Add(App(T("Default.Terminal"), "cmd.exe"));
        items.Add(App(T("Default.Paint"), "mspaint.exe"));
        items.Add(new RadialItem { Name = T("Default.Settings"), Kind = ItemKind.Url, Target = "ms-settings:" });
        items.Add(Sys(T("Default.TaskManager"), SystemActionKind.TaskManager));

        var power = new RadialItem { Name = T("Default.Power"), Kind = ItemKind.Group };
        power.Children.Add(Sys(T("Default.Sleep"), SystemActionKind.Sleep));
        power.Children.Add(Sys(T("Default.Restart"), SystemActionKind.Restart));
        power.Children.Add(Sys(T("Default.Shutdown"), SystemActionKind.Shutdown));
        power.Children.Add(Sys(T("Default.SignOut"), SystemActionKind.SignOut));

        var system = new RadialItem { Name = T("Default.System"), Kind = ItemKind.Group };
        system.Children.Add(Sys(T("Default.Lock"), SystemActionKind.Lock));
        system.Children.Add(Sys(T("Default.Screenshot"), SystemActionKind.Screenshot));
        system.Children.Add(Sys(T("Default.ShowDesktop"), SystemActionKind.ShowDesktop));
        system.Children.Add(power);
        items.Add(system);

        return profile;
    }

    private static RadialItem App(string name, string target) =>
        new() { Name = name, Kind = ItemKind.App, Target = target };

    private static RadialItem Sys(string name, SystemActionKind action) =>
        new() { Name = name, Kind = ItemKind.System, SystemAction = action };
}
