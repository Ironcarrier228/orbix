using System.Diagnostics;
using System.Text;

namespace Orbix.Services;

/// <summary>
/// Minimal file logger (orbix.log in the data directory). Thread-safe, never throws, rotates at 512 KB.
/// Used for diagnostics: unhandled exceptions, failed launches, open-menu latency.
/// </summary>
internal static class Logger
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "orbix.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
            {
                var old = _path + ".old";
                File.Copy(_path, old, true);
                File.Delete(_path);
            }
        }
        catch (Exception)
        {
            _path = null;
        }
    }

    public static string? FilePath => _path;

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception == null ? message : message + " :: " + exception);

    private static void Write(string level, string message)
    {
        Debug.WriteLine("[Orbix] " + message);
        var path = _path;
        if (path == null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must never break the application.
        }
    }
}
