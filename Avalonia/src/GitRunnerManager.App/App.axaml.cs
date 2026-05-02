using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;
using GitRunnerManager.Platform.Services;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;


namespace GitRunnerManager.App;

public partial class App : Application
{
    private RunnerTrayStore? _store;
    private ILocalizationService? _localization;
    private PreferencesStoreFactory? _prefsFactory;
    private IPreferencesStore? _preferences;
    private IAppUpdateService? _updateService;
    private IRunnerUpdateService? _runnerUpdateService;
    private IGitHubService? _gitHubService;
    private IGitHubAuthService? _gitHubAuthService;
    private IGitHubActionsService? _gitHubActionsService;
    private IGitHubTokenStore? _gitHubTokenStore;
    private ILaunchAtLoginService? _launchAtLoginService;
    private IRunnerFolderValidator? _runnerFolderValidator;
    private IRunnerLogService? _runnerLogService;
    private TrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private InitializingTrayWindow? _initializingTrayWindow;
    private SettingsWindow? _settingsWindow;
    private ActionsDashboardWindow? _actionsDashboardWindow;
    private AboutWindow? _aboutWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private TrayIconState? _currentTrayIconState;
    private DateTimeOffset _lastTrayToggleAt;
    private bool _openTrayWhenStoreReady;

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

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
            _runnerUpdateService = new RunnerUpdateService();
            var credentialStore = new CredentialStore();
            _gitHubTokenStore = credentialStore;
            _gitHubService = new GitHubService(credentialStore);
            _gitHubAuthService = (IGitHubAuthService)_gitHubService;
            _gitHubActionsService = new GitHubActionsApiClient(_gitHubTokenStore, _gitHubAuthService);
            _launchAtLoginService = new LaunchAtLoginServiceFactory().Create();
            _runnerFolderValidator = new RunnerFolderValidator();
            _runnerLogService = new RunnerLogService();

            CreateTrayIcon();
            ObserveBackgroundTask(InitializeRunnerStoreAsync(), "Runner store initialization failed");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeRunnerStoreAsync()
    {
        if (_prefsFactory == null || _localization == null)
            return;

        var prefsFactory = _prefsFactory;
        var localization = _localization;

        var store = await Task.Run(() => new RunnerTrayStore(
            new RunnerControllerFactory(new RunnerLogParser(localization)),
            new ResourceMonitorFactory(),
            prefsFactory,
            localization,
            new NetworkConditionMonitor(localization),
            new BatteryMonitorFactory()
        ));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _store = store;
            _store.PropertyChanged += OnStorePropertyChanged;
            UpdateTrayIcon();
            if (_openTrayWhenStoreReady)
            {
                _openTrayWhenStoreReady = false;
                _initializingTrayWindow?.Close();
                _initializingTrayWindow = null;
                ShowTrayMenuWindow();
            }
        });
    }

    private void ObserveBackgroundTask(Task task, string message)
    {
        _ = task.ContinueWith(failedTask =>
        {
            var exception = failedTask.Exception?.GetBaseException();
            if (exception == null)
                return;

            DiagnosticLog.WriteException(message, exception);
            Dispatcher.UIThread.Post(() =>
            {
                if (_store != null && _localization != null)
                    _store.LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, exception.Message);
            });
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticLog.WriteException("Unhandled UI dispatcher exception", e.Exception);
        if (_store != null && _localization != null)
            _store.LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, e.Exception.Message);

        e.Handled = true;
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "Git runner manager",
            IsVisible = true,
            Menu = CreateTrayActivationMenu()
        };
        
        _trayIcon.Clicked += (s, e) => ToggleTrayMenuWindow();

        var trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, trayIcons);
        UpdateTrayIcon();
    }

    private NativeMenu CreateTrayActivationMenu()
    {
        var menu = new NativeMenu();
        menu.Opening += (_, _) => ToggleTrayMenuWindow();
        menu.NeedsUpdate += (_, _) => ToggleTrayMenuWindow();

        var activationItem = new NativeMenuItem(_localization?.Get(LocalizationKeys.AppName) ?? "Git runner manager")
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
        var now = DateTimeOffset.UtcNow;
        if (now - _lastTrayToggleAt < TimeSpan.FromMilliseconds(250))
            return;

        _lastTrayToggleAt = now;

        if (_trayMenuWindow?.IsVisible == true)
        {
            _trayMenuWindow.Hide();
            return;
        }

        if (_initializingTrayWindow?.IsVisible == true)
        {
            _initializingTrayWindow.Hide();
            return;
        }

        ShowTrayMenuWindow();
    }

    private void ShowTrayMenuWindow()
    {
        if (_localization == null)
            return;

        if (_store == null || _launchAtLoginService == null)
        {
            _openTrayWhenStoreReady = true;
            if (_initializingTrayWindow == null)
            {
                _initializingTrayWindow = new InitializingTrayWindow(_localization);
                _initializingTrayWindow.Closed += (_, _) => _initializingTrayWindow = null;
            }

            _initializingTrayWindow.ShowAsPopover();
            return;
        }

        if (_trayMenuWindow == null)
        {
            _trayMenuWindow = new TrayMenuWindow(
                _store,
                _localization,
                _launchAtLoginService,
                () => RunRunnerActionAsync(() => _store?.ForceStartAsync() ?? Task.CompletedTask),
                () => RunRunnerActionAsync(() => _store?.ForceStopAsync() ?? Task.CompletedTask),
                () => RunRunnerActionAsync(() => _store?.SetAutomaticModeAsync() ?? Task.CompletedTask),
                () => RunRunnerActionAsync(() => _store?.RefreshNowAsync() ?? Task.CompletedTask),
                ShowSettingsWindow,
                ShowActionsDashboardWindow,
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
            _settingsWindow.Initialize(_store!, _localization!, _preferences!, _updateService!, _runnerUpdateService!, _gitHubService!, _launchAtLoginService!, _runnerFolderValidator!, _runnerLogService!);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowActionsDashboardWindow()
    {
        if (_actionsDashboardWindow == null)
        {
            var viewModel = new ActionsDashboardViewModel(_store!, _preferences!, _gitHubAuthService!, _gitHubActionsService!, _localization!);
            _actionsDashboardWindow = new ActionsDashboardWindow(viewModel, _localization!, ShowGitHubAccountsSettingsWindow);
            _actionsDashboardWindow.Closed += (_, _) => _actionsDashboardWindow = null;
        }

        _actionsDashboardWindow.Show();
        _actionsDashboardWindow.Activate();
    }

    private void ShowGitHubAccountsSettingsWindow()
    {
        ShowSettingsWindow();
        _settingsWindow?.ShowGitHubAccountsPage();
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
        if (e.PropertyName is nameof(RunnerTrayStore.RunnerSnapshot) or nameof(RunnerTrayStore.ControlMode) or nameof(RunnerTrayStore.Runners))
            Dispatcher.UIThread.Post(UpdateTrayIcon);
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null)
            return;

        var nextState = _store == null ? TrayIconState.Paused : GetTrayIconState(_store);
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
