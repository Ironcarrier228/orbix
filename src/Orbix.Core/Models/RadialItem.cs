using System.Text.Json.Serialization;

namespace Orbix.Core.Models;

/// <summary>
/// A single element of the radial menu. Any element of kind <see cref="ItemKind.Group"/> can contain
/// nested elements (unlimited depth, the UI is tuned for at least three levels).
/// </summary>
public sealed class RadialItem : ObservableObject
{
    private string _id = NewId();
    private string _name = string.Empty;
    private ItemKind _kind = ItemKind.App;
    private string? _target;
    private string? _arguments;
    private string? _workingDirectory;
    private bool _runAsAdmin;
    private string? _iconPath;
    private string? _color;
    private SystemActionKind _systemAction = SystemActionKind.None;
    private ObservableList<RadialItem> _children = new();

    public RadialItem()
    {
        Attach(_children);
    }

    /// <summary>Stable identifier (used by the UI to match visuals with models).</summary>
    public string Id
    {
        get => _id;
        set => Set(ref _id, string.IsNullOrWhiteSpace(value) ? NewId() : value);
    }

    /// <summary>Caption shown in the tooltip and used by search.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    public ItemKind Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                RaiseComputed(nameof(IsGroup));
            }
        }
    }

    /// <summary>
    /// Path of a file/folder/shortcut (<see cref="ItemKind.App"/>), URL (<see cref="ItemKind.Url"/>)
    /// or command line (<see cref="ItemKind.Command"/>).
    /// </summary>
    public string? Target
    {
        get => _target;
        set => Set(ref _target, value);
    }

    /// <summary>Command-line arguments passed to the application.</summary>
    public string? Arguments
    {
        get => _arguments;
        set => Set(ref _arguments, value);
    }

    /// <summary>Working directory. Empty = directory of the executable.</summary>
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set => Set(ref _workingDirectory, value);
    }

    /// <summary>Start elevated (UAC prompt, verb "runas").</summary>
    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set => Set(ref _runAsAdmin, value);
    }

    /// <summary>
    /// Custom icon: path to an image (png/jpg/ico/bmp), or "file.dll,index" to take an icon resource.
    /// A path relative to the data directory (icons\xxx.png) is resolved by the icon service.
    /// Null = take the icon from the shell (for files) or use the built-in glyph.
    /// </summary>
    public string? IconPath
    {
        get => _iconPath;
        set => Set(ref _iconPath, value);
    }

    /// <summary>Optional tint (#RRGGBB) of the built-in glyph / placeholder.</summary>
    public string? Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    /// <summary>Function for <see cref="ItemKind.System"/>.</summary>
    public SystemActionKind SystemAction
    {
        get => _systemAction;
        set => Set(ref _systemAction, value);
    }

    /// <summary>Nested elements of a group.</summary>
    public ObservableList<RadialItem> Children
    {
        get => _children;
        set
        {
            var next = value ?? new ObservableList<RadialItem>();
            if (ReferenceEquals(next, _children))
            {
                return;
            }

            Detach(_children);
            _children = next;
            Attach(_children);
            Raise();
        }
    }

    [JsonIgnore]
    public bool IsGroup => _kind == ItemKind.Group;

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Deep copy. By default all identifiers are regenerated.</summary>
    public RadialItem Clone(bool newIds = true)
    {
        var copy = new RadialItem
        {
            Id = newIds ? NewId() : Id,
            Name = Name,
            Kind = Kind,
            Target = Target,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            RunAsAdmin = RunAsAdmin,
            IconPath = IconPath,
            Color = Color,
            SystemAction = SystemAction,
        };

        foreach (var child in Children)
        {
            copy.Children.Add(child.Clone(newIds));
        }

        return copy;
    }

    public override string ToString() => $"{Kind}: {Name}";
}
