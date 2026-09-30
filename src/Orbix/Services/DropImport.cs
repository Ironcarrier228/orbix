using System.Diagnostics;
using System.Windows;
using Orbix.Core.Models;

namespace Orbix.Services;

/// <summary>
/// Turns dropped files, folders, internet shortcuts and URLs into menu items
/// (and the same conversion is used by the "add file" commands).
/// </summary>
internal static class DropImport
{
    public static bool CanImport(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) ||
        (data.GetDataPresent(DataFormats.UnicodeText) && LooksLikeUrl(data.GetData(DataFormats.UnicodeText) as string));

    /// <summary>Items for everything in the data object (never throws; unreadable entries are skipped).</summary>
    public static List<RadialItem> FromData(IDataObject data)
    {
        var result = new List<RadialItem>();
        try
        {
            if (data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                foreach (var path in paths)
                {
                    var item = FromPath(path);
                    if (item != null)
                    {
                        result.Add(item);
                    }
                }
            }
            else if (data.GetData(DataFormats.UnicodeText) is string text && LooksLikeUrl(text))
            {
                result.Add(FromUrl(text.Trim()));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Reading dropped data failed: " + ex.Message);
        }

        return result;
    }

    public static RadialItem? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (Directory.Exists(path))
            {
                var name = new DirectoryInfo(path).Name;
                return new RadialItem { Name = string.IsNullOrWhiteSpace(name) ? path : name, Kind = ItemKind.App, Target = path };
            }

            if (!File.Exists(path))
            {
                return null;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".url")
            {
                var url = ReadInternetShortcut(path);
                if (url != null)
                {
                    var item = FromUrl(url);
                    item.Name = Path.GetFileNameWithoutExtension(path);
                    return item;
                }
            }

            return new RadialItem { Name = DisplayName(path, extension), Kind = ItemKind.App, Target = path };
        }
        catch (Exception ex)
        {
            Logger.Warn($"Cannot import '{path}': {ex.Message}");
            return null;
        }
    }

    public static RadialItem FromUrl(string url)
    {
        string name = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            name = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        }

        return new RadialItem { Name = name, Kind = ItemKind.Url, Target = url };
    }

    private static bool LooksLikeUrl(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Trim().Length < 2000 &&
        !text.Contains('\n') &&
        (text.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         text.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string DisplayName(string path, string extension)
    {
        string fallback = Path.GetFileNameWithoutExtension(path);
        if (extension == ".exe")
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                var description = info.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description) && description.Length <= 40)
                {
                    return description;
                }

                var product = info.ProductName?.Trim();
                if (!string.IsNullOrEmpty(product) && product.Length <= 40)
                {
                    return product;
                }
            }
            catch (Exception)
            {
                // unreadable version resource: use the file name
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? Path.GetFileName(path) : fallback;
    }

    private static string? ReadInternetShortcut(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                return line[4..].Trim();
            }
        }

        return null;
    }
}
