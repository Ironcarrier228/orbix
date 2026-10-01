using Microsoft.Win32;

namespace Orbix.Services;

/// <summary>
/// Start with Windows: a value in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// No administrator rights and no scheduled task are needed. The StartupApproved key (written by the
/// "Startup" page of Task Manager) is respected as well.
/// </summary>
internal static class AutostartService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ValueName = "Orbix";

    public static bool IsEnabled()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (run?.GetValue(ValueName) is not string command || string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            // Task Manager writes a binary value whose first byte has bit 0 set when the entry is disabled.
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, false);
            if (approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1)
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("Cannot read the autostart state: " + ex.Message);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    return false;
                }

                run.SetValue(ValueName, $"\"{exe}\" --minimized", RegistryValueKind.String);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true);
                approved?.DeleteValue(ValueName, false);
            }
            else
            {
                run.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Cannot change the autostart setting", ex);
            return false;
        }
    }
}
