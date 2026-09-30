using System.Text.Json.Serialization;

namespace Orbix.Core.Models;

/// <summary>
/// Root of the configuration that is persisted as JSON
/// (<c>%APPDATA%\RadialLauncher\config.json</c>).
/// </summary>
public sealed class AppConfig : ObservableObject
{
    public const int CurrentVersion = 1;

    private GeneralSettings _general = new();
    private AppearanceSettings _appearance = new();
    private OrbSettings _orb = new();
    private MenuSettings _menu = new();
    private TriggerSettings _triggers = new();
    private string? _activeProfileId;
    private ObservableList<MenuProfile> _profiles = new();

    public AppConfig()
    {
        Attach(_general);
        Attach(_appearance);
        Attach(_orb);
        Attach(_menu);
        Attach(_triggers);
        Attach(_profiles);
    }

    /// <summary>Schema version of the file (for future migrations).</summary>
    public int Version { get; set; } = CurrentVersion;

    public GeneralSettings General
    {
        get => _general;
        set
        {
            var next = value ?? new GeneralSettings();
            if (ReferenceEquals(next, _general)) return;
            Detach(_general);
            _general = next;
            Attach(_general);
            Raise();
        }
    }

    public AppearanceSettings Appearance
    {
        get => _appearance;
        set
        {
            var next = value ?? new AppearanceSettings();
            if (ReferenceEquals(next, _appearance)) return;
            Detach(_appearance);
            _appearance = next;
            Attach(_appearance);
            Raise();
        }
    }

    public OrbSettings Orb
    {
        get => _orb;
        set
        {
            var next = value ?? new OrbSettings();
            if (ReferenceEquals(next, _orb)) return;
            Detach(_orb);
            _orb = next;
            Attach(_orb);
            Raise();
        }
    }

    public MenuSettings Menu
    {
        get => _menu;
        set
        {
            var next = value ?? new MenuSettings();
            if (ReferenceEquals(next, _menu)) return;
            Detach(_menu);
            _menu = next;
            Attach(_menu);
            Raise();
        }
    }

    public TriggerSettings Triggers
    {
        get => _triggers;
        set
        {
            var next = value ?? new TriggerSettings();
            if (ReferenceEquals(next, _triggers)) return;
            Detach(_triggers);
            _triggers = next;
            Attach(_triggers);
            Raise();
        }
    }

    /// <summary>Id of the profile that is shown by the menu.</summary>
    public string? ActiveProfileId
    {
        get => _activeProfileId;
        set
        {
            if (Set(ref _activeProfileId, value))
            {
                RaiseComputed(nameof(ActiveProfile));
            }
        }
    }

    public ObservableList<MenuProfile> Profiles
    {
        get => _profiles;
        set
        {
            var next = value ?? new ObservableList<MenuProfile>();
            if (ReferenceEquals(next, _profiles)) return;
            Detach(_profiles);
            _profiles = next;
            Attach(_profiles);
            Raise();
        }
    }

    /// <summary>The profile selected by <see cref="ActiveProfileId"/> (the first one when the id is unknown).</summary>
    [JsonIgnore]
    public MenuProfile ActiveProfile
    {
        get
        {
            foreach (var profile in _profiles)
            {
                if (profile.Id == _activeProfileId)
                {
                    return profile;
                }
            }

            if (_profiles.Count == 0)
            {
                _profiles.Add(new MenuProfile { Name = "Default" });
            }

            return _profiles[0];
        }
    }

    /// <summary>
    /// Replaces the content of this instance with the content of <paramref name="source"/>
    /// while keeping the identity of the settings sections (so that subscribers stay valid).
    /// Used by "import".
    /// </summary>
    public void ApplyFrom(AppConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);

        CopyProperties(source.General, General);
        CopyProperties(source.Appearance, Appearance);
        CopyProperties(source.Orb, Orb);
        CopyProperties(source.Menu, Menu);
        CopyProperties(source.Triggers, Triggers);

        Version = source.Version;
        Profiles.Clear();
        foreach (var profile in source.Profiles.ToList())
        {
            source.Profiles.Remove(profile);
            Profiles.Add(profile);
        }

        ActiveProfileId = source.ActiveProfileId;
        RaiseComputed(nameof(ActiveProfile));
    }

    /// <summary>Copies all public read/write properties of a simple settings object.</summary>
    private static void CopyProperties<T>(T from, T to) where T : class
    {
        foreach (var property in typeof(T).GetProperties())
        {
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(to, property.GetValue(from));
            }
        }
    }
}
