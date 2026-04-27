using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Core.Localization;
using Timer = System.Timers.Timer;

namespace GitHubRunnerTray.Core.Services;

public partial class RunnerTrayStore : ObservableObject, IDisposable
{
    private readonly IRunnerControllerFactory _controllerFactory;
    private readonly IResourceMonitorFactory _resourceMonitorFactory;
    private readonly IPreferencesStoreFactory _preferencesFactory;
    private readonly ILocalizationService _localization;
    private readonly INetworkConditionMonitor _networkMonitor;
    private readonly IBatteryMonitorFactory _batteryMonitorFactory;

    private IRunnerController? _controller;
    private IResourceMonitor? _resourceMonitor;
    private IBatteryMonitor? _batteryMonitor;
    private Timer? _refreshTimer;
    private string _runnerDirectory;
    private bool _disposed;

    [ObservableProperty]
    private RunnerControlMode _controlMode = RunnerControlMode.Automatic;

    [ObservableProperty]
    private RunnerSnapshot _runnerSnapshot = RunnerSnapshot.Stopped;

    [ObservableProperty]
    private NetworkConditionSnapshot _networkSnapshot = NetworkConditionSnapshot.Unknown;

    [ObservableProperty]
    private BatterySnapshot _batterySnapshot = BatterySnapshot.NoBattery;

    [ObservableProperty]
    private RunnerResourceUsage _resourceUsage = RunnerResourceUsage.Zero;

    [ObservableProperty]
    private bool _launchAtLoginEnabled;

    [ObservableProperty]
    private string? _lastErrorMessage;

    public string RunnerDirectory
    {
        get => _runnerDirectory;
        set
        {
            if (SetProperty(ref _runnerDirectory, value))
            {
                UpdateController();
            }
        }
    }

    public bool StopRunnerOnBattery
    {
        get => _preferencesFactory.Create().StopRunnerOnBattery;
        set
        {
            _preferencesFactory.Create().StopRunnerOnBattery = value;
            ReconcileState("battery setting changed");
        }
    }

    public RunnerTrayStore(
        IRunnerControllerFactory controllerFactory,
        IResourceMonitorFactory resourceMonitorFactory,
        IPreferencesStoreFactory preferencesFactory,
        ILocalizationService localization,
        INetworkConditionMonitor networkMonitor,
        IBatteryMonitorFactory batteryMonitorFactory)
    {
        _controllerFactory = controllerFactory;
        _resourceMonitorFactory = resourceMonitorFactory;
        _preferencesFactory = preferencesFactory;
        _localization = localization;
        _networkMonitor = networkMonitor;
        _batteryMonitorFactory = batteryMonitorFactory;

        _runnerDirectory = GetDefaultRunnerDirectory();

        var prefs = _preferencesFactory.Create();
        if (!string.IsNullOrEmpty(prefs.RunnerDirectory))
            _runnerDirectory = prefs.RunnerDirectory;

        var savedControlMode = prefs.RunnerDirectory;
        if (Enum.TryParse<RunnerControlMode>(savedControlMode, out var mode))
            ControlMode = mode;

        Initialize();
    }

    private static string GetDefaultRunnerDirectory()
    {
        if (OperatingSystem.IsMacOS())
            return "/Users/" + Environment.UserName + "/GitHub/actions-runner";

        if (OperatingSystem.IsLinux())
            return "/home/" + Environment.UserName + "/actions-runner";

        if (OperatingSystem.IsWindows())
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "GitHub", "actions-runner");
        }

        return "/actions-runner";
    }

    private void Initialize()
    {
        UpdateController();

        _networkMonitor.OnChange += OnNetworkChanged;
        _networkMonitor.Start();

        _batteryMonitor = _batteryMonitorFactory.Create();
        _batteryMonitor.OnChange += OnBatteryChanged;
        _batteryMonitor.Start();

        _refreshTimer = new Timer(5000);
        _refreshTimer.Elapsed += OnRefreshTimerElapsed;
        _refreshTimer.Start();

        ReconcileState("initialization");
    }

    private void UpdateController()
    {
        _controller?.Dispose();
        _resourceMonitor?.Stop();

        var dirInfo = new DirectoryInfo(_runnerDirectory);
        _controller = _controllerFactory.Create(dirInfo);
        _resourceMonitor = _resourceMonitorFactory.Create(dirInfo);

        ReconcileState("controller updated");
    }

    private void OnNetworkChanged(object? sender, NetworkConditionSnapshot snapshot)
    {
        NetworkSnapshot = snapshot;
        ReconcileState("network change");
    }

    private void OnBatteryChanged(object? sender, BatterySnapshot snapshot)
    {
        BatterySnapshot = snapshot;
        ReconcileState("battery change");
    }

    private void OnRefreshTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        ReconcileState("periodic refresh");
    }

    public void RefreshNow()
    {
        ReconcileState("manual refresh");
    }

    public void SetAutomaticMode()
    {
        SetControlMode(RunnerControlMode.Automatic, "automatic mode");
    }

    public void ForceStart()
    {
        SetControlMode(RunnerControlMode.ForceRunning, "manual start");
    }

    public void ForceStop()
    {
        SetControlMode(RunnerControlMode.ForceStopped, "manual stop");
    }

    public async Task StartAsync()
    {
        if (_controller == null) return;

        try
        {
            await _controller.StartAsync();
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, ex.Message);
        }

        ReconcileState("manual start");
    }

    public async Task StopAsync()
    {
        if (_controller == null) return;

        try
        {
            await _controller.StopAsync();
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, ex.Message);
        }

        ReconcileState("manual stop");
    }

    public void OpenRunnerDirectory()
    {
        if (Directory.Exists(_runnerDirectory))
        {
            _controllerFactory.Create(new DirectoryInfo(_runnerDirectory));
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _runnerDirectory,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }
    }

    private void SetControlMode(RunnerControlMode mode, string trigger)
    {
        ControlMode = mode;

        var prefs = _preferencesFactory.Create();
        prefs.RunnerDirectory = mode.ToString();

        ReconcileState(trigger);
    }

    private void ReconcileState(string trigger)
    {
        try
        {
            ApplyDesiredRunnerState();
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, ex.Message);
        }

        UpdateSnapshot(trigger);
    }

    private void ApplyDesiredRunnerState()
    {
        if (StopRunnerOnBattery && !BatterySnapshot.CanRun)
        {
            _controller?.StopAsync().Wait();
            return;
        }

        switch (ControlMode)
        {
            case RunnerControlMode.Automatic:
                ApplyAutomaticDecision();
                break;
            case RunnerControlMode.ForceRunning:
                _controller?.StartAsync().Wait();
                break;
            case RunnerControlMode.ForceStopped:
                _controller?.StopAsync().Wait();
                break;
        }
    }

    private void ApplyAutomaticDecision()
    {
        var decision = NetworkSnapshot.AutomaticDecision;
        switch (decision)
        {
            case NetworkDecision.Run:
                _controller?.StartAsync().Wait();
                break;
            case NetworkDecision.Stop:
                _controller?.StopAsync().Wait();
                break;
            case NetworkDecision.Keep:
                break;
        }
    }

    private void UpdateSnapshot(string trigger)
    {
        if (_controller == null)
            return;

        var snapshot = _controller.GetCurrentSnapshot();
        if (snapshot.IsRunning && snapshot.Activity.Kind == RunnerActivityKind.Unknown)
        {
            RunnerActivitySnapshot activityToUse;
            if (RunnerSnapshot.IsRunning && RunnerSnapshot.Activity.Kind != RunnerActivityKind.Unknown)
            {
                activityToUse = RunnerSnapshot.Activity;
            }
            else
            {
                activityToUse = new RunnerActivitySnapshot
                {
                    Kind = RunnerActivityKind.Waiting,
                    Description = _localization.Get(LocalizationKeys.ActivityWaitingOrStarting)
                };
            }

            snapshot = new RunnerSnapshot
            {
                IsRunning = snapshot.IsRunning,
                Activity = activityToUse
            };
        }

        RunnerSnapshot = snapshot;

        if (_resourceMonitor != null)
            ResourceUsage = _resourceMonitor.GetCurrentUsage();
    }

    public string PolicySummary => ControlMode switch
    {
        RunnerControlMode.Automatic => NetworkSnapshot.Kind switch
        {
            NetworkConditionKind.Unmetered => _localization.Get(LocalizationKeys.PolicyAutomaticRun),
            NetworkConditionKind.Expensive => _localization.Get(LocalizationKeys.PolicyAutomaticExpensive),
            NetworkConditionKind.Offline => _localization.Get(LocalizationKeys.PolicyAutomaticOffline),
            NetworkConditionKind.Unknown => _localization.Get(LocalizationKeys.PolicyAutomaticUnknown),
            _ => _localization.Get(LocalizationKeys.PolicyAutomaticUnknown)
        },
        RunnerControlMode.ForceRunning => _localization.Get(LocalizationKeys.PolicyForceRunning),
        RunnerControlMode.ForceStopped => _localization.Get(LocalizationKeys.PolicyForceStopped),
        _ => ""
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _networkMonitor.Stop();
        _batteryMonitor?.Stop();
        _controller?.Dispose();
    }
}