using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using GitHubRunnerTray.Core.Localization;
using GitHubRunnerTray.Core.Services;
using GitHubRunnerTray.Platform.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace GitHubRunnerTray.App;

public partial class App : Application
{
    private RunnerTrayStore? _store;
    private ILocalizationService? _localization;
    private TrayIcon? _trayIcon;
    private NativeMenu? _trayMenu;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            var sp = services.BuildServiceProvider();

            _localization = sp.GetRequiredService<ILocalizationService>();

            var prefsFactory = new PreferencesStoreFactory();

            _store = new RunnerTrayStore(
                new RunnerControllerFactory(new RunnerLogParser(_localization)),
                new ResourceMonitorFactory(),
                prefsFactory,
                _localization,
                new NetworkConditionMonitor(_localization),
                new BatteryMonitorFactory()
            );

            desktop.ShutdownRequested += (s, e) =>
            {
                _store?.Dispose();
            };

            desktop.MainWindow = null;

            base.OnFrameworkInitializationCompleted();
        }
    }

    private Stream? GetIconStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream("GitHubRunnerTray.App.Assets.Icon.png");
    }
}