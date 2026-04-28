using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Localization;
using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Core.Services;
using GitHubRunnerTray.Platform.Services;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;


namespace GitHubRunnerTray.App;

public partial class App : Application
{
    private RunnerTrayStore? _store;
    private ILocalizationService? _localization;
    private PreferencesStoreFactory? _prefsFactory;
    private IPreferencesStore? _preferences;
    private IAppUpdateService? _updateService;
    private ILaunchAtLoginService? _launchAtLoginService;
    private TrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private TrayIconState? _currentTrayIconState;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            // No main window for tray-only app
            desktop.MainWindow = null;

            var services = new ServiceCollection();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            var sp = services.BuildServiceProvider();

            _localization = sp.GetRequiredService<ILocalizationService>();

            _prefsFactory = new PreferencesStoreFactory();
            _preferences = _prefsFactory.Create();
            _localization.CurrentLanguage = _preferences.Language;
            _updateService = new AppUpdateService(_prefsFactory);
            _launchAtLoginService = new LaunchAtLoginServiceFactory().Create();

            _store = new RunnerTrayStore(
                new RunnerControllerFactory(new RunnerLogParser(_localization)),
                new ResourceMonitorFactory(),
                _prefsFactory,
                _localization,
                new NetworkConditionMonitor(_localization),
                new BatteryMonitorFactory()
            );

            CreateTrayIcon();
            _store.PropertyChanged += OnStorePropertyChanged;
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "GitHub Runner Tray",
            IsVisible = true,
            Menu = CreateTrayActivationMenu()
        };
        
        _trayIcon.Clicked += (s, e) => ShowTrayMenuWindow();

        var trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, trayIcons);
        UpdateTrayIcon();
    }

    private NativeMenu CreateTrayActivationMenu()
    {
        var menu = new NativeMenu();
        menu.Opening += (_, _) => ShowTrayMenuWindow();
        menu.NeedsUpdate += (_, _) => ShowTrayMenuWindow();

        var activationItem = new NativeMenuItem(_localization?.Get(LocalizationKeys.AppName) ?? "github runer mac")
        {
            IsVisible = false,
            IsEnabled = false
        };
        menu.Items.Add(activationItem);

        return menu;
    }

    private void QuitApp()
    {
        if (_store != null)
            _store.PropertyChanged -= OnStorePropertyChanged;

        _store?.Dispose();
        _desktop?.TryShutdown();
    }

    private void ToggleTrayMenuWindow()
    {
        if (_trayMenuWindow?.IsVisible == true)
        {
            _trayMenuWindow.Hide();
            return;
        }

        ShowTrayMenuWindow();
    }

    private void ShowTrayMenuWindow()
    {
        if (_trayMenuWindow == null)
        {
            _trayMenuWindow = new TrayMenuWindow(
                _store!,
                _localization!,
                _launchAtLoginService!,
                () => RunRunnerActionAsync(() => _store?.ForceStartAsync() ?? Task.CompletedTask),
                () => RunRunnerActionAsync(() => _store?.ForceStopAsync() ?? Task.CompletedTask),
                () => RunRunnerActionAsync(() => _store?.SetAutomaticModeAsync() ?? Task.CompletedTask),
                () => RunRunnerActionAsync(() => _store?.RefreshNowAsync() ?? Task.CompletedTask),
                ShowSettingsWindow,
                QuitApp
            );
            _trayMenuWindow.Closed += (_, _) => _trayMenuWindow = null;
        }

        _trayMenuWindow.ShowAsPopover();
    }

    private async Task RunRunnerActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (_store != null && _localization != null)
                _store.LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, ex.Message);
        }
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Initialize(_store!, _localization!, _preferences!, _updateService!, _launchAtLoginService!);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowAboutWindow()
    {
        if (_aboutWindow == null)
        {
            _aboutWindow = new AboutWindow();
        }
        _aboutWindow.Show();
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RunnerTrayStore.RunnerSnapshot) or nameof(RunnerTrayStore.ControlMode))
            Dispatcher.UIThread.Post(UpdateTrayIcon);
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null || _store == null)
            return;

        var nextState = GetTrayIconState(_store);
        if (_currentTrayIconState == nextState)
            return;

        var icon = LoadTrayIcon(nextState);
        if (icon == null)
            return;

        _trayIcon.Icon = icon;
        _currentTrayIconState = nextState;
    }

    private static TrayIconState GetTrayIconState(RunnerTrayStore store)
    {
        if (!store.RunnerSnapshot.IsRunning || store.ControlMode == RunnerControlMode.ForceStopped)
            return TrayIconState.Paused;

        return store.RunnerSnapshot.Activity.Kind == RunnerActivityKind.Busy
            ? TrayIconState.Busy
            : TrayIconState.Waiting;
    }

    private static WindowIcon? LoadTrayIcon(TrayIconState state)
    {
        var fileName = state switch
        {
            TrayIconState.Busy => "TrayBusy.png",
            TrayIconState.Waiting => "TrayWaiting.png",
            TrayIconState.Paused => "TrayPaused.png",
            _ => "TrayWaiting.png"
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!File.Exists(iconPath))
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");

        if (!File.Exists(iconPath))
            return null;

        try
        {
            using var iconStream = File.OpenRead(iconPath);
            return new WindowIcon(iconStream);
        }
        catch
        {
            return null;
        }
    }

    private enum TrayIconState
    {
        Paused,
        Waiting,
        Busy
    }
}
