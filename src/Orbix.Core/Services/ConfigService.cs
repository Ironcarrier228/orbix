using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Orbix.Core.Models;

namespace Orbix.Core.Services;

/// <summary>Outcome of <see cref="ConfigService.Import"/>.</summary>
public sealed record ImportResult(bool Success, string? Error, int IconsRestored)
{
    public static ImportResult Ok(int icons) => new(true, null, icons);

    public static ImportResult Fail(string error) => new(false, error, 0);
}

/// <summary>
/// Loads and stores the configuration as JSON (<c>%APPDATA%\RadialLauncher\config.json</c>).
///
/// * Never throws on load: a missing file creates defaults, a corrupted one is moved aside and replaced
///   by the last good backup (or by defaults).
/// * Auto-save: every change of the model graph schedules a debounced atomic write
///   (temp file + <see cref="File.Replace(string,string,string?,bool)"/>), keeping the previous version as config.json.bak.
/// * Export / import of the whole configuration, including custom icons (embedded as base64).
/// </summary>
public sealed class ConfigService : IDisposable
{
    public const int DebounceMs = 500;

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly Regex SafeIconName = new(@"^[A-Za-z0-9_\-\. ]{1,100}\.(png|jpg|jpeg|ico|bmp|gif)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private readonly object _gate = new();
    private readonly SynchronizationContext? _sync;
    private readonly Timer _timer;
    private string _defaultLanguage = "en";
    private bool _dirty;
    private bool _disposed;

    /// <param name="directory">Data directory (config.json, icons, log).</param>
    /// <param name="synchronizationContext">
    /// Context used to serialize the model on the thread that owns it (the UI thread). Null = run on the timer thread.
    /// </param>
    public ConfigService(string directory, SynchronizationContext? synchronizationContext = null)
    {
        DirectoryPath = directory;
        _sync = synchronizationContext;
        _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        Config = new AppConfig();
    }

    public string DirectoryPath { get; }

    public string ConfigPath => DataPaths.ConfigFile(DirectoryPath);

    public string BackupPath => ConfigPath + ".bak";

    public string IconsDirectory => DataPaths.IconsDirectory(DirectoryPath);

    public AppConfig Config { get; private set; }

    /// <summary>True when the file did not exist and defaults were created.</summary>
    public bool CreatedNew { get; private set; }

    /// <summary>Set when the file could not be read (description of what happened).</summary>
    public string? LoadWarning { get; private set; }

    public event EventHandler? Saved;

    public event EventHandler<Exception>? SaveFailed;

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new TolerantEnumConverterFactory());
        return options;
    }

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    public static AppConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? throw new JsonException("The configuration is empty.");

    /// <summary>Loads the configuration. Never throws.</summary>
    public AppConfig Load(string defaultLanguage = "en")
    {
        _defaultLanguage = defaultLanguage;
        AppConfig? loaded = null;
        LoadWarning = null;
        CreatedNew = false;

        try
        {
            Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception ex)
        {
            LoadWarning = "Cannot create data directory: " + ex.Message;
        }

        if (File.Exists(ConfigPath))
        {
            try
            {
                loaded = Deserialize(File.ReadAllText(ConfigPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                LoadWarning = "config.json is corrupted: " + ex.Message;
                MoveCorruptedAside();
                loaded = TryLoadBackup();
            }
        }
        else
        {
            CreatedNew = true;
        }

        loaded ??= DefaultConfigFactory.Create(defaultLanguage);
        ConfigNormalizer.Normalize(loaded, defaultLanguage);

        Config.Changed -= OnConfigChanged;
        Config = loaded;
        Config.Changed += OnConfigChanged;

        if (CreatedNew || LoadWarning != null)
        {
            SaveNow();
        }

        return Config;
    }

    /// <summary>Schedules a debounced save (called automatically on every change).</summary>
    public void RequestSave()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _dirty = true;
            _timer.Change(DebounceMs, Timeout.Infinite);
        }
    }

    /// <summary>Writes pending changes immediately (on exit).</summary>
    public void Flush()
    {
        bool dirty;
        lock (_gate)
        {
            dirty = _dirty;
        }

        if (dirty)
        {
            SaveNow();
        }
    }

    /// <summary>Atomically writes the configuration. Returns false (and raises <see cref="SaveFailed"/>) on error.</summary>
    public bool SaveNow()
    {
        lock (_gate)
        {
            _dirty = false;
            if (!_disposed)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var json = Serialize(Config);
            var temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, json, Utf8NoBom);

            if (File.Exists(ConfigPath))
            {
                File.Replace(temp, ConfigPath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, ConfigPath);
            }

            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            // Try again a bit later (antivirus, locked file...).
            lock (_gate)
            {
                if (!_disposed)
                {
                    _dirty = true;
                    _timer.Change(3000, Timeout.Infinite);
                }
            }

            SaveFailed?.Invoke(this, ex);
            return false;
        }
    }

    /// <summary>Exports the whole configuration (with custom icons embedded) to a JSON file.</summary>
    public void Export(string path)
    {
        var root = JsonSerializer.SerializeToNode(Config, JsonOptions)?.AsObject()
                   ?? throw new InvalidOperationException("Cannot serialize the configuration.");

        var icons = new JsonObject();
        void Embed(string? iconPath)
        {
            if (string.IsNullOrEmpty(iconPath) || !TryGetManagedIconFile(iconPath, out var file, out var name))
            {
                return;
            }

            try
            {
                if (File.Exists(file) && new FileInfo(file).Length <= 2 * 1024 * 1024)
                {
                    icons[name] = Convert.ToBase64String(File.ReadAllBytes(file));
                }
            }
            catch (IOException)
            {
                // A missing/locked icon must not break the export.
            }
        }

        Embed(Config.Orb.ImagePath);
        foreach (var profile in Config.Profiles)
        {
            foreach (var item in ItemTree.Walk(profile.Items))
            {
                Embed(item.IconPath);
            }
        }

        if (icons.Count > 0)
        {
            root["embeddedIcons"] = icons;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, root.ToJsonString(JsonOptions), Utf8NoBom);
    }

    /// <summary>
    /// Replaces the current configuration with the content of a file produced by <see cref="Export"/>.
    /// The previous configuration is kept as config.json.before-import.
    /// </summary>
    public ImportResult Import(string path)
    {
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var node = JsonNode.Parse(text, null, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? throw new JsonException("The file does not contain a JSON object.");

            int restored = 0;
            if (node["embeddedIcons"] is JsonObject icons)
            {
                foreach (var pair in icons)
                {
                    // Only plain file names are accepted: no separators, no "..", only image extensions.
                    if (!SafeIconName.IsMatch(pair.Key) || pair.Key.Contains("..") || pair.Value == null)
                    {
                        continue; // never write outside of the icons directory
                    }

                    var bytes = Convert.FromBase64String(pair.Value.GetValue<string>());
                    Directory.CreateDirectory(IconsDirectory);
                    File.WriteAllBytes(Path.Combine(IconsDirectory, pair.Key), bytes);
                    restored++;
                }

                node.Remove("embeddedIcons");
            }

            var imported = node.Deserialize<AppConfig>(JsonOptions) ?? throw new JsonException("The configuration is empty.");
            ConfigNormalizer.Normalize(imported, _defaultLanguage);

            try
            {
                if (File.Exists(ConfigPath))
                {
                    File.Copy(ConfigPath, ConfigPath + ".before-import", true);
                }
            }
            catch (IOException)
            {
                // Not critical.
            }

            Config.ApplyFrom(imported);
            ConfigNormalizer.Normalize(Config, _defaultLanguage);
            SaveNow();
            return ImportResult.Ok(restored);
        }
        catch (Exception ex)
        {
            return ImportResult.Fail(ex.Message);
        }
    }

    /// <summary>Copies a user picture into the managed icons folder and returns the relative path to store in the config.</summary>
    public string ImportIconFile(string sourcePath, string preferredName)
    {
        Directory.CreateDirectory(IconsDirectory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".ico" or ".bmp" or ".gif"))
        {
            extension = ".png";
        }

        var safe = Regex.Replace(preferredName, @"[^A-Za-z0-9_\-]", "");
        if (safe.Length == 0)
        {
            safe = "icon";
        }

        var fileName = $"{safe}-{Guid.NewGuid():N}".Substring(0, Math.Min(40, safe.Length + 9)) + extension;
        File.Copy(sourcePath, Path.Combine(IconsDirectory, fileName), true);
        return "icons/" + fileName;
    }

    public void Dispose()
    {
        bool already;
        lock (_gate)
        {
            already = _disposed;
        }

        if (already)
        {
            return;
        }

        Flush();

        lock (_gate)
        {
            _disposed = true;
            _timer.Dispose();
        }

        Config.Changed -= OnConfigChanged;
    }

    private void OnConfigChanged(object? sender, EventArgs e) => RequestSave();

    private void OnTimer(object? state)
    {
        if (_sync != null)
        {
            _sync.Post(_ => Flush(), null);
        }
        else
        {
            Flush();
        }
    }

    private bool TryGetManagedIconFile(string iconPath, out string file, out string name)
    {
        file = string.Empty;
        name = string.Empty;

        try
        {
            var full = Path.GetFullPath(DataPaths.ResolveIconPath(DirectoryPath, iconPath));
            var iconsRoot = Path.GetFullPath(IconsDirectory) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(iconsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            name = Path.GetFileName(full);
            file = full;
            return SafeIconName.IsMatch(name);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void MoveCorruptedAside()
    {
        try
        {
            var target = Path.Combine(DirectoryPath, $"config.corrupted-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(ConfigPath, target, true);
        }
        catch (Exception)
        {
            // If it cannot be moved it will simply be overwritten by the next save.
        }
    }

    private AppConfig? TryLoadBackup()
    {
        try
        {
            if (File.Exists(BackupPath))
            {
                var cfg = Deserialize(File.ReadAllText(BackupPath, Encoding.UTF8));
                LoadWarning += " The last good backup (config.json.bak) was restored.";
                return cfg;
            }
        }
        catch (Exception)
        {
            // fall through to defaults
        }

        return null;
    }
}

/// <summary>Enums are stored as camelCase strings; unknown values fall back to the first member instead of failing the whole load.</summary>
internal sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert));
}

internal sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var parsed) ? parsed : default;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                return (T)Enum.ToObject(typeof(T), number);
            default:
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var name = value.ToString();
        writer.WriteStringValue(name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name);
    }
}
