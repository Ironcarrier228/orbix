using System.Windows;
using System.Windows.Threading;
using Orbix.Services;

namespace Orbix;

/// <summary>WPF application object: owns the <see cref="AppHost"/> which wires all services together.</summary>
public partial class App : Application
{
    private readonly string[] _args;
    private readonly string _dataDirectory;
    private AppHost? _host;

    public App(string[] args, string dataDirectory)
    {
        _args = args;
        _dataDirectory = dataDirectory;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Logger.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            _host = new AppHost(_args, _dataDirectory);
            _host.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Start-up failed", ex);
            MessageBox.Show("Orbix cannot start:\n\n" + ex.Message + "\n\nDetails: " + Logger.FilePath, "Orbix", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A failing handler must not kill the tray application.
        Logger.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
