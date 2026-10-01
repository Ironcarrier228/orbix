using System.ComponentModel;
using System.Diagnostics;
using Orbix.Core.Models;
using Orbix.Native;

namespace Orbix.Services;

/// <summary>Outcome of starting an item. <see cref="ErrorKey"/> is a localization key.</summary>
internal sealed record LaunchResult(bool Success, string? ErrorKey = null, string? Detail = null, bool Cancelled = false)
{
    public static LaunchResult Ok { get; } = new(true);

    public static LaunchResult Fail(string key, string? detail = null) => new(false, key, detail);
}

/// <summary>Immutable copy of the fields needed to start an item (the model must not be touched from other threads).</summary>
internal sealed record LaunchRequest(
    ItemKind Kind,
    string Name,
    string? Target,
    string? Arguments,
    string? WorkingDirectory,
    bool RunAsAdmin,
    SystemActionKind Action)
{
    public static LaunchRequest From(RadialItem item) => new(
        item.Kind, item.Name, item.Target, item.Arguments, item.WorkingDirectory, item.RunAsAdmin, item.SystemAction);
}

/// <summary>
/// Starts applications, folders, files, URLs, commands and system actions. Everything runs on a short-lived STA
/// thread (the shell likes STA and a slow network path must never freeze the menu). Errors are returned, never thrown:
/// a missing exe produces a message, not a crash.
/// </summary>
internal sealed class LaunchService
{
    /// <summary>Actions that need a second click to be executed (when the option is enabled).</summary>
    public static bool IsDangerous(RadialItem item) =>
        item.Kind == ItemKind.System &&
        item.SystemAction is SystemActionKind.Shutdown or SystemActionKind.Restart or SystemActionKind.SignOut or SystemActionKind.Hibernate;

    public Task<LaunchResult> LaunchAsync(RadialItem item)
    {
        var request = LaunchRequest.From(item);
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Launch(request));
            }
            catch (Exception ex)
            {
                Logger.Error("Launch failed: " + request.Name, ex);
                completion.SetResult(LaunchResult.Fail("Launch.Failed", ex.Message));
            }
        })
        {
            IsBackground = true,
            Name = "Orbix.Launch",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>Synchronous start (runs on the calling thread).</summary>
    public static LaunchResult Launch(LaunchRequest request)
    {
        switch (request.Kind)
        {
            case ItemKind.Group:
                return LaunchResult.Ok;
            case ItemKind.System:
                return SystemActions.Run(request.Action);
            case ItemKind.Command:
                return StartCommand(request);
            case ItemKind.Url:
                return StartShell(request.Target, null, null, request.RunAsAdmin);
            default:
                return StartShell(request.Target, request.Arguments, request.WorkingDirectory, request.RunAsAdmin);
        }
    }

    private static LaunchResult StartShell(string? target, string? arguments, string? workingDirectory, bool runAsAdmin)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return LaunchResult.Fail("Launch.NoTarget");
        }

        var path = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        bool isPath = Path.IsPathRooted(path) && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        bool isFile = false;
        if (isPath)
        {
            isFile = File.Exists(path);
            if (!isFile && !Directory.Exists(path))
            {
                Logger.Warn("Launch target not found: " + path);
                return LaunchResult.Fail("Launch.NotFound", path);
            }
        }

        var info = new ProcessStartInfo(path) { UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            info.Arguments = Environment.ExpandEnvironmentVariables(arguments);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var dir = Environment.ExpandEnvironmentVariables(workingDirectory.Trim().Trim('"'));
            if (Directory.Exists(dir))
            {
                info.WorkingDirectory = dir;
            }
        }
        else if (isFile)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                info.WorkingDirectory = dir;
            }
        }

        if (runAsAdmin)
        {
            info.Verb = "runas";
        }

        return Start(info, path);
    }

    private static LaunchResult StartCommand(LaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target))
        {
            return LaunchResult.Fail("Launch.NoTarget");
        }

        var info = new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c " + Environment.ExpandEnvironmentVariables(request.Target),
            UseShellExecute = request.RunAsAdmin,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (request.RunAsAdmin)
        {
            info.Verb = "runas";
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory) && Directory.Exists(request.WorkingDirectory))
        {
            info.WorkingDirectory = request.WorkingDirectory;
        }

        return Start(info, request.Target);
    }

    internal static LaunchResult Start(ProcessStartInfo info, string description)
    {
        try
        {
            using var process = Process.Start(info);
            return LaunchResult.Ok;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user cancelled the UAC prompt: not an error.
            return new LaunchResult(false, Cancelled: true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            Logger.Warn($"Not found: {description} ({ex.Message})");
            return LaunchResult.Fail("Launch.NotFound", description);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return LaunchResult.Fail("Launch.AccessDenied", description);
        }
        catch (Exception ex)
        {
            Logger.Error("Cannot start " + description, ex);
            return LaunchResult.Fail("Launch.Failed", ex.Message);
        }
    }
}

/// <summary>Built-in system functions.</summary>
internal static class SystemActions
{
    public static LaunchResult Run(SystemActionKind action)
    {
        switch (action)
        {
            case SystemActionKind.Lock:
                return Win32.LockWorkStation() ? LaunchResult.Ok : LaunchResult.Fail("Launch.Failed", "LockWorkStation");
            case SystemActionKind.Sleep:
                Win32.SetSuspendState(false, false, false);
                return LaunchResult.Ok;
            case SystemActionKind.Hibernate:
                return Shutdown("/h");
            case SystemActionKind.SignOut:
                return Shutdown("/l");
            case SystemActionKind.Restart:
                return Shutdown("/r /t 0");
            case SystemActionKind.Shutdown:
                return Shutdown("/s /t 0");
            case SystemActionKind.Screenshot:
                Thread.Sleep(220); // let the menu disappear first
                var clip = LaunchService.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }, "ms-screenclip:");
                if (!clip.Success && !clip.Cancelled)
                {
                    SendKeys(Win32.VK_LWIN, 0x10, 'S'); // Win+Shift+S
                }

                return LaunchResult.Ok;
            case SystemActionKind.ShowDesktop:
                Thread.Sleep(220);
                SendKeys(Win32.VK_LWIN, 0, 'D');
                return LaunchResult.Ok;
            case SystemActionKind.TaskManager:
                return LaunchService.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }, "taskmgr.exe");
            default:
                return LaunchResult.Ok;
        }
    }

    private static LaunchResult Shutdown(string arguments) =>
        LaunchService.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true }, "shutdown.exe " + arguments);

    /// <summary>Presses modifier(s) + a key (keybd_event keeps the code free of INPUT structures).</summary>
    private static void SendKeys(int modifier1, int modifier2, char key)
    {
        void Down(int vk) => Win32.keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        void Up(int vk) => Win32.keybd_event((byte)vk, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);

        Down(modifier1);
        if (modifier2 != 0)
        {
            Down(modifier2);
        }

        Down(key);
        Up(key);
        if (modifier2 != 0)
        {
            Up(modifier2);
        }

        Up(modifier1);
    }
}
