using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Orbix.Core.Models;
using Orbix.Core.Services;
using Orbix.Native;
using Orbix.UI;

namespace Orbix.Services;

/// <summary>
/// Icon of a menu item: either a built-in vector glyph (<see cref="GlyphName"/>) or a bitmap
/// (shell icon, custom picture or a coloured letter tile used as placeholder).
/// </summary>
internal readonly record struct IconResult(ImageSource? Image, string? GlyphName, bool IsPlaceholder, bool TargetMissing);

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(SIZE size, uint flags, out IntPtr phbm);
}

/// <summary>
/// Extracts icons through the Windows Shell API (<c>IShellItemImageFactory</c>, falling back to <c>SHGetFileInfo</c>),
/// resolves shortcuts to the icon of their target and supports custom pictures / "file.dll,index" resources.
/// Loading happens on one background STA thread; until an icon is ready a placeholder is shown, and a missing or
/// inaccessible target simply keeps its placeholder - nothing ever throws into the UI.
/// </summary>
internal sealed class IconService : IDisposable
{
    private const uint SIIGBF_BIGGERSIZEOK = 0x1;
    private const uint SIIGBF_ICONONLY = 0x4;

    private readonly string _dataDirectory;
    private readonly Dispatcher _ui;
    private readonly ConcurrentDictionary<string, IconResult> _cache = new();
    private readonly BlockingCollection<Job> _queue = new();
    private readonly Dictionary<string, Job> _pending = new();
    private readonly object _gate = new();
    private readonly Thread _worker;

    private sealed class Job
    {
        public Job(string key, string name, ItemKind kind, string? target, string? iconPath, int px)
        {
            Key = key;
            Name = name;
            Kind = kind;
            Target = target;
            IconPath = iconPath;
            Px = px;
        }

        public string Key { get; }

        public string Name { get; }

        public ItemKind Kind { get; }

        public string? Target { get; }

        public string? IconPath { get; }

        public int Px { get; }

        public List<Action<IconResult>> Callbacks { get; } = new();
    }

    public IconService(string dataDirectory, Dispatcher ui)
    {
        _dataDirectory = dataDirectory;
        _ui = ui;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Orbix.Icons" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>Rounds a pixel size up to one of the cached sizes.</summary>
    public static int Bucket(double px)
    {
        foreach (int size in new[] { 32, 48, 64, 96, 128, 192, 256 })
        {
            if (px <= size)
            {
                return size;
            }
        }

        return 256;
    }

    /// <summary>
    /// Returns the icon immediately (cache or placeholder). When a real icon still has to be loaded,
    /// <paramref name="onLoaded"/> is invoked on the UI thread once it is ready.
    /// </summary>
    public IconResult Get(RadialItem item, double px, Action<IconResult>? onLoaded = null)
    {
        int size = Bucket(px);

        if (!NeedsLoading(item))
        {
            return new IconResult(null, Glyphs.NameFor(item), false, false);
        }

        string key = Key(item, size);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Enqueue(key, item, size, onLoaded);
        return new IconResult(Glyphs.LetterTile(item.Name, false), null, true, false);
    }

    /// <summary>Warms the cache for a whole tree in the background (so the first menu opening shows real icons).</summary>
    public void Preload(IEnumerable<RadialItem> items, double px)
    {
        int size = Bucket(px);
        foreach (var item in ItemTree.Walk(items))
        {
            if (NeedsLoading(item) && !_cache.ContainsKey(Key(item, size)))
            {
                Enqueue(Key(item, size), item, size, null);
            }
        }
    }

    /// <summary>Forgets cached icons of the item (after its target or icon was edited).</summary>
    public void Invalidate(RadialItem item)
    {
        string prefix = KeyPrefix(item);
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void Clear() => _cache.Clear();

    /// <summary>Synchronously loads a custom picture / "file.dll,index" resource (used for the orb). Null if unreadable.</summary>
    public ImageSource? LoadPicture(string spec, int px) => LoadCustom(spec, Math.Max(16, px));

    public void Dispose()
    {
        _queue.CompleteAdding();
    }

    /// <summary>True when the item needs a shell icon or has a custom picture.</summary>
    private static bool NeedsLoading(RadialItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.IconPath))
        {
            return true;
        }

        return item.Kind == ItemKind.App && !string.IsNullOrWhiteSpace(item.Target);
    }

    private static string KeyPrefix(RadialItem item) => $"{item.Kind}|{item.Target}|{item.IconPath}|";

    private static string Key(RadialItem item, int size) => KeyPrefix(item) + size;

    private void Enqueue(string key, RadialItem item, int size, Action<IconResult>? onLoaded)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var job))
            {
                job = new Job(key, item.Name, item.Kind, item.Target, item.IconPath, size);
                _pending[key] = job;
                if (onLoaded != null)
                {
                    job.Callbacks.Add(onLoaded);
                }

                try
                {
                    _queue.Add(job);
                }
                catch (InvalidOperationException)
                {
                    _pending.Remove(key); // disposed
                }
            }
            else if (onLoaded != null)
            {
                job.Callbacks.Add(onLoaded);
            }
        }
    }

    private void WorkerLoop()
    {
        foreach (var job in _queue.GetConsumingEnumerable())
        {
            IconResult result;
            try
            {
                result = Load(job);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Icon of '{job.Name}' cannot be loaded: {ex.Message}");
                result = new IconResult(Glyphs.LetterTile(job.Name, false), null, true, false);
            }

            _cache[job.Key] = result;

            List<Action<IconResult>> callbacks;
            lock (_gate)
            {
                callbacks = new List<Action<IconResult>>(job.Callbacks);
                _pending.Remove(job.Key);
            }

            if (callbacks.Count > 0)
            {
                try
                {
                    _ui.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        foreach (var callback in callbacks)
                        {
                            try
                            {
                                callback(result);
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn("Icon callback failed: " + ex.Message);
                            }
                        }
                    }));
                }
                catch (Exception)
                {
                    // dispatcher is shutting down
                }
            }
        }
    }

    private IconResult Load(Job job)
    {
        bool missing = false;
        string? path = null;

        if (job.Kind == ItemKind.App && !string.IsNullOrWhiteSpace(job.Target))
        {
            path = ResolveTarget(job.Target!, out missing);
        }

        // 1. custom icon
        if (!string.IsNullOrWhiteSpace(job.IconPath))
        {
            var custom = LoadCustom(job.IconPath!, job.Px);
            if (custom != null)
            {
                return new IconResult(custom, null, false, missing);
            }
        }

        // 2. the shell icon of the target
        if (path != null)
        {
            var shell = LoadShellIcon(path, job.Px);
            if (shell != null)
            {
                return new IconResult(shell, null, false, false);
            }
        }

        // 3. placeholder (missing file, inaccessible path, unknown type)
        if (job.Kind != ItemKind.App)
        {
            // A custom picture that cannot be read: the UI falls back to the built-in glyph of the item.
            return new IconResult(null, null, true, false);
        }

        return new IconResult(Glyphs.LetterTile(job.Name, missing), null, true, missing);
    }

    // ---------------------------------------------------------------- target resolution

    /// <summary>
    /// Expands variables and finds "notepad.exe"-style names on PATH / in the App Paths registry.
    /// <paramref name="missing"/> is true when an absolute path does not exist.
    /// </summary>
    internal static string? ResolveTarget(string target, out bool missing)
    {
        missing = false;
        var path = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (Path.IsPathRooted(path))
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                return path;
            }

            missing = true;
            return null;
        }

        return FindExecutable(path);
    }

    private static string? FindExecutable(string name)
    {
        try
        {
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.COM;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
            var candidates = new List<string> { name };
            if (string.IsNullOrEmpty(Path.GetExtension(name)))
            {
                candidates.AddRange(extensions.Select(e => name + e));
            }

            var directories = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            };
            directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries));

            foreach (var dir in directories)
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var full = Path.Combine(dir.Trim('"'), candidate);
                        if (File.Exists(full))
                        {
                            return full;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // invalid characters in a PATH entry
                    }
                }
            }

            foreach (var candidate in candidates.Where(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + candidate, false);
                    if (key?.GetValue(null) is string registered)
                    {
                        registered = Environment.ExpandEnvironmentVariables(registered.Trim('"'));
                        if (File.Exists(registered))
                        {
                            return registered;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Executable lookup failed: " + ex.Message);
        }

        return null;
    }

    // ---------------------------------------------------------------- custom icons

    private ImageSource? LoadCustom(string iconPath, int px)
    {
        try
        {
            string spec = Environment.ExpandEnvironmentVariables(iconPath.Trim().Trim('"'));

            // "C:\Windows\System32\shell32.dll,21"
            int comma = spec.LastIndexOf(',');
            if (comma > 0 && int.TryParse(spec[(comma + 1)..].Trim(), out int index))
            {
                var file = spec[..comma].Trim().Trim('"');
                var resource = ExtractIconResource(DataPaths.ResolveIconPath(_dataDirectory, file), index, px);
                if (resource != null)
                {
                    return resource;
                }
            }

            var resolved = DataPaths.ResolveIconPath(_dataDirectory, spec);
            if (!File.Exists(resolved))
            {
                return null;
            }

            string extension = Path.GetExtension(resolved).ToLowerInvariant();
            if (extension is ".exe" or ".dll" or ".icl")
            {
                return ExtractIconResource(resolved, 0, px);
            }

            return LoadImageFile(resolved, px);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Custom icon '{iconPath}' cannot be loaded: {ex.Message}");
            return null;
        }
    }

    private static ImageSource? LoadImageFile(string path, int px)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            return null;
        }

        // .ico files contain several sizes: take the smallest one that is big enough (or the biggest available).
        BitmapSource frame = decoder.Frames
            .OrderBy(f => f.PixelWidth < px ? int.MaxValue - f.PixelWidth : f.PixelWidth)
            .First();

        BitmapSource result = frame;
        if (frame.PixelWidth > px * 2)
        {
            double scale = px * 2.0 / frame.PixelWidth;
            result = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        }

        result = new FormatConvertedBitmap(result, PixelFormats.Pbgra32, null, 0);
        result.Freeze();
        return result;
    }

    private static ImageSource? ExtractIconResource(string file, int index, int px)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        var icons = new IntPtr[1];
        int count = Win32.PrivateExtractIcons(file, index, px, px, icons, null, 1, 0);
        if (count <= 0 || icons[0] == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return FromIcon(icons[0]);
        }
        finally
        {
            Win32.DestroyIcon(icons[0]);
        }
    }

    // ---------------------------------------------------------------- shell icons

    private static ImageSource? LoadShellIcon(string path, int px)
    {
        // A shortcut shows the icon of the program it points to (no arrow overlay).
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var (target, iconFile, iconIndex) = ResolveShortcut(path);
            if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
            {
                var fromFile = ExtractIconResource(iconFile!, iconIndex, px);
                if (fromFile != null)
                {
                    return fromFile;
                }
            }

            if (!string.IsNullOrEmpty(target) && (File.Exists(target) || Directory.Exists(target)))
            {
                path = target!;
            }
        }

        return ShellImage(path, px) ?? ShellFileInfoIcon(path);
    }

    private static ImageSource? ShellImage(string path, int px)
    {
        IntPtr unknown = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            int hr = Win32.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out unknown);
            if (hr != 0 || unknown == IntPtr.Zero)
            {
                return null;
            }

            var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(unknown);
            try
            {
                hr = factory.GetImage(new SIZE(px, px), SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY, out var bitmap);
                if (hr != 0 || bitmap == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return FromHBitmap(bitmap);
                }
                finally
                {
                    Win32.DeleteObject(bitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"IShellItemImageFactory failed for '{path}': {ex.Message}");
            return null;
        }
        finally
        {
            if (unknown != IntPtr.Zero)
            {
                Marshal.Release(unknown);
            }
        }
    }

    private static ImageSource? ShellFileInfoIcon(string path)
    {
        try
        {
            var info = new SHFILEINFO { szDisplayName = string.Empty, szTypeName = string.Empty };
            var result = Win32.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return FromIcon(info.hIcon);
            }
            finally
            {
                Win32.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"SHGetFileInfo failed for '{path}': {ex.Message}");
            return null;
        }
    }

    private static ImageSource? FromIcon(IntPtr icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    /// <summary>Reads a 32-bit HBITMAP (top-down) into a frozen BitmapSource, keeping the alpha channel.</summary>
    private static BitmapSource? FromHBitmap(IntPtr bitmap)
    {
        if (Win32.GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0)
        {
            return null;
        }

        int width = bm.bmWidth;
        int height = Math.Abs(bm.bmHeight);
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            },
        };

        var data = new byte[width * height * 4];
        IntPtr dc = Win32.GetDC(IntPtr.Zero);
        try
        {
            if (Win32.GetDIBits(dc, bitmap, 0, (uint)height, data, ref info, Win32.DIB_RGB_COLORS) == 0)
            {
                return null;
            }
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, dc);
        }

        bool anyAlpha = false;
        bool straight = false;
        for (int i = 0; i < data.Length; i += 4)
        {
            byte a = data[i + 3];
            if (a != 0)
            {
                anyAlpha = true;
            }

            if (a < 255 && (data[i] > a || data[i + 1] > a || data[i + 2] > a))
            {
                straight = true;
            }
        }

        if (!anyAlpha)
        {
            // No alpha information at all: treat the picture as opaque.
            for (int i = 3; i < data.Length; i += 4)
            {
                data[i] = 255;
            }
        }

        var format = straight ? PixelFormats.Bgra32 : PixelFormats.Pbgra32;
        var source = BitmapSource.Create(width, height, 96, 96, format, null, data, width * 4);
        source.Freeze();
        return source;
    }

    /// <summary>Resolves a .lnk with WScript.Shell (late bound, no COM vtable declarations needed).</summary>
    internal static (string? Target, string? IconFile, int IconIndex) ResolveShortcut(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
            {
                return default;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic link = shell.CreateShortcut(lnkPath);
                string target = link.TargetPath ?? string.Empty;
                string location = link.IconLocation ?? string.Empty;

                string? iconFile = null;
                int index = 0;
                int comma = location.LastIndexOf(',');
                if (comma >= 0)
                {
                    int.TryParse(location[(comma + 1)..].Trim(), out index);
                    iconFile = location[..comma].Trim();
                }
                else if (!string.IsNullOrWhiteSpace(location))
                {
                    iconFile = location.Trim();
                }

                if (!string.IsNullOrWhiteSpace(iconFile))
                {
                    iconFile = Environment.ExpandEnvironmentVariables(iconFile);
                }

                return (string.IsNullOrWhiteSpace(target) ? null : target, string.IsNullOrWhiteSpace(iconFile) ? null : iconFile, index);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Cannot read the shortcut '{lnkPath}': {ex.Message}");
            return default;
        }
    }
}
