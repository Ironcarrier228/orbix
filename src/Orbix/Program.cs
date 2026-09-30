using System.Runtime.InteropServices;
using Orbix.Core.Services;
using Orbix.Services;

namespace Orbix;

/// <summary>
/// Entry point. The single-instance check runs before WPF is even created, so a second start costs a few milliseconds:
/// it forwards its command to the running instance and exits.
///
/// Command line: --settings | --open | --toggle-orb | --exit | --minimized (accepted, the tray start is the default).
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string dataDirectory = DataPaths.DefaultDirectory;
        Logger.Initialize(dataDirectory);

        var mutex = SingleInstance.TryAcquire(dataDirectory);
        if (mutex == null)
        {
            // another instance owns the tray icon: hand over the command
            string command = SingleInstance.CommandOpen;
            if (args.Contains("--exit"))
            {
                command = SingleInstance.CommandExit;
            }
            else if (args.Contains("--settings"))
            {
                command = SingleInstance.CommandSettings;
            }
            else if (args.Contains("--toggle-orb"))
            {
                command = SingleInstance.CommandToggleOrb;
            }
            else if (args.Contains("--minimized"))
            {
                return 0; // started by autostart while already running: nothing to do
            }

            SingleInstance.SendCommand(command);
            return 0;
        }

        if (args.Contains("--exit"))
        {
            mutex.ReleaseMutex();
            return 0; // nothing is running, nothing to close
        }

        try
        {
            Logger.Info($"Orbix {typeof(Program).Assembly.GetName().Version} starting, data directory '{dataDirectory}'.");
            var app = new App(args, dataDirectory);
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error", ex);
            return 1;
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // the mutex belongs to another thread (only possible after a crash) - ignore
            }

            mutex.Dispose();
        }
    }
}
