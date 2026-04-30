using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Native;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                DiagnosticLog.WriteException("Unhandled app-domain exception", ex);
            else
                DiagnosticLog.Write($"Unhandled app-domain exception: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticLog.WriteException("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            DiagnosticLog.Write("Application starting");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            DiagnosticLog.Write("Application stopped");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("Fatal application exception", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
    }
}
