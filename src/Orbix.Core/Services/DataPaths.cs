namespace Orbix.Core.Services;

/// <summary>Locations of the files used by the application.</summary>
public static class DataPaths
{
    /// <summary>Name of the folder inside %APPDATA% (as required by the specification).</summary>
    public const string FolderName = "RadialLauncher";

    /// <summary>Environment variable that overrides the data directory (portable use, tests, CI).</summary>
    public const string OverrideVariable = "ORBIX_DATA_DIR";

    /// <summary>%APPDATA%\RadialLauncher, or the directory from <see cref="OverrideVariable"/>.</summary>
    public static string DefaultDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridden));
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
        }
    }

    public static string ConfigFile(string dataDirectory) => Path.Combine(dataDirectory, "config.json");

    public static string IconsDirectory(string dataDirectory) => Path.Combine(dataDirectory, "icons");

    public static string LogFile(string dataDirectory) => Path.Combine(dataDirectory, "orbix.log");

    /// <summary>
    /// Resolves a custom icon path: absolute paths are returned as is, relative ones are relative to the data directory.
    /// </summary>
    public static string ResolveIconPath(string dataDirectory, string iconPath)
    {
        if (Path.IsPathRooted(iconPath))
        {
            return iconPath;
        }

        return Path.Combine(dataDirectory, iconPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
