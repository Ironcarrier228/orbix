using System.Text.Json;
using Orbix.Core.Models;
using Orbix.Core.Services;
using Xunit;

namespace Orbix.Core.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orbix-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private ConfigService NewService() => new(_dir);

    [Fact]
    public void FirstRun_CreatesDefaultsAndWritesTheFile()
    {
        using var service = NewService();
        var config = service.Load("en");

        Assert.True(service.CreatedNew);
        Assert.True(File.Exists(service.ConfigPath));
        Assert.Single(config.Profiles);
        Assert.Equal(config.Profiles[0].Id, config.ActiveProfileId);
        Assert.Equal(8, config.ActiveProfile.Items.Count);
        Assert.Equal("Ctrl+Alt+Space", config.Triggers.Hotkey);
        Assert.Equal(56, config.Orb.Size);
        Assert.Equal(0.4, config.Orb.RestingOpacity);
        Assert.Equal(OrbVisibilityMode.AutoHideFullscreen, config.Orb.Visibility);
    }

    [Fact]
    public void DefaultProfile_HasThreeLevelsOfNesting()
    {
        var profile = DefaultConfigFactory.CreateDefaultProfile("ru");
        Assert.Equal(3, ItemTree.MaxDepth(profile.Items));
        Assert.Equal("Проводник", profile.Items[0].Name);
    }

    [Fact]
    public void Config_RoundTripsThroughJson()
    {
        using (var service = NewService())
        {
            var config = service.Load("en");
            config.Orb.Size = 80;
            config.Orb.Color = "#FF8800";
            config.Menu.Radius = 200;
            config.Triggers.Hotkey = "Ctrl+Shift+F1";
            config.Appearance.Theme = ThemeMode.Dark;
            var profile = config.ActiveProfile;
            profile.Items[0].Arguments = "--flag \"a b\"";
            profile.Items[0].RunAsAdmin = true;
            service.SaveNow();
        }

        using var second = NewService();
        var loaded = second.Load("en");

        Assert.False(second.CreatedNew);
        Assert.Null(second.LoadWarning);
        Assert.Equal(80, loaded.Orb.Size);
        Assert.Equal("#FF8800", loaded.Orb.Color);
        Assert.Equal(200, loaded.Menu.Radius);
        Assert.Equal("Ctrl+Shift+F1", loaded.Triggers.Hotkey);
        Assert.Equal(ThemeMode.Dark, loaded.Appearance.Theme);
        Assert.Equal("--flag \"a b\"", loaded.ActiveProfile.Items[0].Arguments);
        Assert.True(loaded.ActiveProfile.Items[0].RunAsAdmin);
        Assert.Equal(3, ItemTree.MaxDepth(loaded.ActiveProfile.Items));
    }

    [Fact]
    public void Json_UsesCamelCaseNamesAndStringEnums()
    {
        using var service = NewService();
        service.Load("en");
        var json = File.ReadAllText(service.ConfigPath);

        Assert.Contains("\"orb\"", json);
        Assert.Contains("\"restingOpacity\"", json);
        Assert.Contains("\"visibility\": \"autoHideFullscreen\"", json);
        Assert.Contains("\"kind\": \"group\"", json);
        Assert.DoesNotContain("null", json); // nulls are not written
    }

    [Fact]
    public void AutoSave_WritesAfterChange_WhenFlushed()
    {
        using var service = NewService();
        var config = service.Load("en");
        var before = File.ReadAllText(service.ConfigPath);

        config.Orb.Size = 99;      // bubbling Changed -> RequestSave
        service.Flush();

        var after = File.ReadAllText(service.ConfigPath);
        Assert.NotEqual(before, after);
        Assert.Contains("\"size\": 99", after);
    }

    [Fact]
    public void AutoSave_FiresByItselfAfterTheDebounceDelay()
    {
        using var service = NewService();
        var config = service.Load("en");
        using var saved = new ManualResetEventSlim();
        service.Saved += (_, _) => saved.Set();

        config.ActiveProfile.Items.Add(new RadialItem { Name = "new one", Kind = ItemKind.Url, Target = "https://example.com" });

        Assert.True(saved.Wait(TimeSpan.FromSeconds(5)), "debounced save did not happen");
        Assert.Contains("new one", File.ReadAllText(service.ConfigPath));
    }

    [Fact]
    public void NestedChanges_BubbleUpToTheRoot()
    {
        var config = DefaultConfigFactory.Create("en");
        int changes = 0;
        config.Changed += (_, _) => changes++;

        var power = config.ActiveProfile.Items.Last().Children.Last();
        power.Children[0].Name = "renamed";                       // 3rd level property
        Assert.True(changes > 0);

        changes = 0;
        power.Children.Add(new RadialItem { Name = "x" });          // 3rd level collection
        Assert.True(changes > 0);

        changes = 0;
        config.Menu.Radius = 150;                                 // settings section
        Assert.True(changes > 0);

        changes = 0;
        var removed = power.Children[0];
        power.Children.Remove(removed);
        changes = 0;
        removed.Name = "detached";                                // removed items no longer notify
        Assert.Equal(0, changes);
    }

    [Fact]
    public void CorruptedFile_IsMovedAside_AndBackupIsRestored()
    {
        using (var service = NewService())
        {
            var config = service.Load("en");
            config.Orb.Size = 70;
            service.SaveNow();          // config.json (size 70)
            config.Orb.Size = 71;
            service.SaveNow();          // config.json (71), config.json.bak (70)
        }

        File.WriteAllText(Path.Combine(_dir, "config.json"), "{ this is not json ");

        using var second = NewService();
        var loaded = second.Load("en");

        Assert.NotNull(second.LoadWarning);
        Assert.Equal(70, loaded.Orb.Size);                           // from the backup
        Assert.Single(Directory.GetFiles(_dir, "config.corrupted-*.json"));
    }

    [Fact]
    public void CorruptedFile_WithoutBackup_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), "\0\0\0 garbage");

        using var service = NewService();
        var config = service.Load("ru");

        Assert.NotNull(service.LoadWarning);
        Assert.Equal(8, config.ActiveProfile.Items.Count);
        Assert.Equal("Проводник", config.ActiveProfile.Items[0].Name);
    }

    [Fact]
    public void HandEditedValues_AreClampedAndRepaired()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """
        {
          "futureField": 123,
          "orb": { "size": 5000, "restingOpacity": 7, "visibility": "nonsense", "color": "red" },
          "menu": { "radius": -10, "itemSize": "64", "maxVisibleOrbits": 99 },
          "triggers": { "hotkey": "banana", "toggleOrbHotkey": "banana" },
          "appearance": { "accent": "oops" },
          "general": { "language": "klingon" },
          "profiles": [
            { "id": "p1", "name": "", "items": [
                { "id": "dup", "name": "  A  ", "kind": "app", "target": "C:\\Tools\\tool.exe" },
                { "id": "dup", "name": "", "kind": "app", "target": "C:\\Tools\\other.exe" },
                { "id": "g", "name": "G", "kind": "group", "children": [ { "name": "", "kind": "url", "target": "https://x.y" } ] }
            ] },
            { "id": "p1", "name": "Second", "items": [] }
          ],
          "activeProfileId": "missing"
        }
        """);

        using var service = NewService();
        var c = service.Load("en");

        Assert.Null(service.LoadWarning);
        Assert.Equal(Limits.OrbSizeMax, c.Orb.Size);
        Assert.Equal(1.0, c.Orb.RestingOpacity);
        Assert.Null(c.Orb.Color);
        Assert.Equal(Limits.RadiusMin, c.Menu.Radius);
        Assert.Equal(64, c.Menu.ItemSize);
        Assert.Equal(Limits.MaxOrbitsMax, c.Menu.MaxVisibleOrbits);
        Assert.Equal("Ctrl+Alt+Space", c.Triggers.Hotkey);
        Assert.Equal("Ctrl+Alt+O", c.Triggers.ToggleOrbHotkey);
        Assert.Equal(AppearanceSettings.DefaultAccent, c.Appearance.Accent);
        Assert.Equal("auto", c.General.Language);

        Assert.Equal(2, c.Profiles.Count);
        Assert.NotEqual(c.Profiles[0].Id, c.Profiles[1].Id);
        Assert.Equal(c.Profiles[0].Id, c.ActiveProfileId);
        Assert.Equal("Profile 1", c.Profiles[0].Name);

        var items = c.Profiles[0].Items;
        Assert.Equal("A", items[0].Name);
        Assert.NotEqual(items[0].Id, items[1].Id);
        Assert.Equal("other", items[1].Name);                   // derived from the target
        Assert.Equal("x.y", items[2].Children[0].Name);         // derived from the URL host... or target
    }

    [Fact]
    public void Normalizer_EqualHotkeys_DisableTheSecondOne()
    {
        var config = DefaultConfigFactory.Create("en");
        config.Triggers.ToggleOrbHotkey = config.Triggers.Hotkey;
        ConfigNormalizer.Normalize(config);
        Assert.False(config.Triggers.ToggleOrbHotkeyEnabled);
    }

    [Fact]
    public void ExportImport_RestoresSettingsProfilesAndCustomIcons()
    {
        string exportPath = Path.Combine(_dir, "export", "backup.json");
        string iconRelative;

        using (var source = NewService())
        {
            var config = source.Load("en");
            config.Orb.Size = 90;
            config.Menu.ItemSize = 70;
            var second = new MenuProfile { Name = "Work" };
            second.Items.Add(new RadialItem { Name = "Site", Kind = ItemKind.Url, Target = "https://example.org" });
            config.Profiles.Add(second);

            var picture = Path.Combine(_dir, "picture.png");
            File.WriteAllBytes(picture, new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 });
            iconRelative = source.ImportIconFile(picture, "site");
            second.Items[0].IconPath = iconRelative;

            source.Export(exportPath);
        }

        var exported = File.ReadAllText(exportPath);
        Assert.Contains("embeddedIcons", exported);

        var otherDir = Path.Combine(_dir, "other-machine");
        using var target = new ConfigService(otherDir);
        var before = target.Load("en");
        Assert.Equal(56, before.Orb.Size);

        var result = target.Import(exportPath);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.IconsRestored);
        Assert.Equal(90, target.Config.Orb.Size);
        Assert.Equal(70, target.Config.Menu.ItemSize);
        Assert.Equal(2, target.Config.Profiles.Count);
        Assert.Equal("Work", target.Config.Profiles[1].Name);
        Assert.True(File.Exists(Path.Combine(otherDir, iconRelative.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(target.ConfigPath + ".before-import"));

        // the imported configuration is persisted
        using var reloaded = new ConfigService(otherDir);
        Assert.Equal(90, reloaded.Load("en").Orb.Size);
    }

    [Fact]
    public void Import_OfGarbage_FailsAndKeepsTheCurrentConfig()
    {
        using var service = NewService();
        var config = service.Load("en");
        config.Orb.Size = 66;

        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "[1, 2, 3]");
        var result = service.Import(bad);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(66, service.Config.Orb.Size);

        var missing = service.Import(Path.Combine(_dir, "does-not-exist.json"));
        Assert.False(missing.Success);
    }

    [Fact]
    public void Import_IgnoresUnsafeEmbeddedIconNames()
    {
        using var service = NewService();
        service.Load("en");

        var evil = Path.Combine(_dir, "evil.json");
        var config = DefaultConfigFactory.Create("en");
        var node = JsonSerializer.SerializeToNode(config, ConfigService.JsonOptions)!.AsObject();
        node["embeddedIcons"] = new System.Text.Json.Nodes.JsonObject
        {
            ["../../evil.png"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            ["payload.exe"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            ["fine.png"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        };
        File.WriteAllText(evil, node.ToJsonString());

        var result = service.Import(evil);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.IconsRestored);
        Assert.True(File.Exists(Path.Combine(service.IconsDirectory, "fine.png")));
        Assert.False(File.Exists(Path.Combine(_dir, "..", "evil.png")));
        Assert.False(File.Exists(Path.Combine(service.IconsDirectory, "payload.exe")));
    }

    [Fact]
    public void ApplyFrom_KeepsSectionIdentity()
    {
        var target = DefaultConfigFactory.Create("en");
        var orbBefore = target.Orb;
        var source = DefaultConfigFactory.Create("en");
        source.Orb.Size = 100;

        target.ApplyFrom(source);

        Assert.Same(orbBefore, target.Orb);
        Assert.Equal(100, target.Orb.Size);
        Assert.Single(target.Profiles);
    }

    [Fact]
    public void ActiveProfile_FallsBackToTheFirstOne()
    {
        var config = new AppConfig();
        config.Profiles.Add(new MenuProfile { Name = "A" });
        config.Profiles.Add(new MenuProfile { Name = "B" });
        config.ActiveProfileId = "unknown";
        Assert.Equal("A", config.ActiveProfile.Name);

        config.ActiveProfileId = config.Profiles[1].Id;
        Assert.Equal("B", config.ActiveProfile.Name);
    }

    [Fact]
    public void SettingValidation_RejectsNaNAndInfinity()
    {
        var orb = new OrbSettings();
        orb.Size = double.NaN;
        Assert.Equal(Limits.OrbSizeDefault, orb.Size);
        orb.RestingOpacity = double.PositiveInfinity;
        Assert.Equal(0.4, orb.RestingOpacity);
    }
}
