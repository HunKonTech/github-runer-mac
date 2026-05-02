using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using Timer = System.Timers.Timer;

namespace GitRunnerManager.Core.Services;

public partial class RunnerTrayStore : ObservableObject, IDisposable
{
    private readonly IPreferencesStoreFactory _preferencesFactory;
    private readonly ILocalizationService _localization;
    private readonly INetworkConditionMonitor _networkMonitor;
    private readonly IBatteryMonitorFactory _batteryMonitorFactory;
    private readonly RunnerManager _runnerManager;
    private IBatteryMonitor? _batteryMonitor;
    private Timer? _refreshTimer;
    private bool _disposed;
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

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

    [ObservableProperty]
    private DateTime? _lastRefreshTime;

    public RunnerTrayStore(
        IRunnerControllerFactory controllerFactory,
        IResourceMonitorFactory resourceMonitorFactory,
        IPreferencesStoreFactory preferencesFactory,
        ILocalizationService localization,
        INetworkConditionMonitor networkMonitor,
        IBatteryMonitorFactory batteryMonitorFactory)
    {
        _preferencesFactory = preferencesFactory;
        _localization = localization;
        _networkMonitor = networkMonitor;
        _batteryMonitorFactory = batteryMonitorFactory;
        _runnerManager = new RunnerManager(controllerFactory, resourceMonitorFactory, preferencesFactory, localization);
        _runnerManager.PropertyChanged += (_, e) => UpdateAggregateSnapshot(e.PropertyName == nameof(RunnerManager.Runners));

        var prefs = _preferencesFactory.Create();
        ControlMode = prefs.ControlMode;

        Initialize();
    }

    public IReadOnlyList<RunnerInstanceStore> Runners => _runnerManager.Runners;

    public string RunnerDirectory
    {
        get => _preferencesFactory.Create().RunnerDirectory;
        set
        {
            var prefs = _preferencesFactory.Create();
            prefs.RunnerDirectory = value;
            _runnerManager.ReloadProfiles();
            _ = ReconcileStateAsync("runner directory changed");
        }
    }

    public bool StopRunnerOnBattery
    {
        get => _preferencesFactory.Create().RunnerProfiles.FirstOrDefault()?.StopOnBattery
            ?? _preferencesFactory.Create().StopRunnerOnBattery;
        set
        {
            var prefs = _preferencesFactory.Create();
            prefs.StopRunnerOnBattery = value;
            var profiles = prefs.RunnerProfiles;
            foreach (var profile in profiles)
                profile.StopOnBattery = value;
            prefs.RunnerProfiles = profiles;
            _runnerManager.ReloadProfiles();
            _ = ReconcileStateAsync("battery setting changed");
        }
    }

    public bool StopRunnerOnMeteredNetwork
    {
        get => _preferencesFactory.Create().RunnerProfiles.FirstOrDefault()?.StopOnMeteredNetwork
            ?? _preferencesFactory.Create().StopRunnerOnMeteredNetwork;
        set
        {
            var prefs = _preferencesFactory.Create();
            prefs.StopRunnerOnMeteredNetwork = value;
            var profiles = prefs.RunnerProfiles;
            foreach (var profile in profiles)
                profile.StopOnMeteredNetwork = value;
            prefs.RunnerProfiles = profiles;
            _runnerManager.ReloadProfiles();
            _ = ReconcileStateAsync("metered network setting changed");
        }
    }

    public RunnerInstanceStore? GetRunner(string runnerId) => _runnerManager.GetRunner(runnerId);

    public void SaveRunnerProfile(RunnerConfig profile)
    {
        _runnerManager.SaveProfile(profile);
        _ = ReconcileStateAsync("runner profile saved");
    }

    public void AddRunnerProfile(RunnerConfig profile)
    {
        _runnerManager.AddProfile(profile);
        _ = ReconcileStateAsync("runner profile added");
    }

    public void RemoveRunnerProfile(string runnerId)
    {
        _runnerManager.RemoveProfile(runnerId);
        _ = ReconcileStateAsync("runner profile removed");
    }

    private void Initialize()
    {
        _networkMonitor.OnChange += OnNetworkChanged;
        _networkMonitor.Start();

        _batteryMonitor = _batteryMonitorFactory.Create();
        _batteryMonitor.OnChange += OnBatteryChanged;
        _batteryMonitor.Start();

        _refreshTimer = new Timer(5000);
        _refreshTimer.Elapsed += OnRefreshTimerElapsed;
        _refreshTimer.Start();

        _ = ReconcileStateAsync("initialization");
    }

    private void OnNetworkChanged(object? sender, NetworkConditionSnapshot snapshot)
    {
        NetworkSnapshot = snapshot;
        _ = ReconcileStateAsync("network change");
    }

    private void OnBatteryChanged(object? sender, BatterySnapshot snapshot)
    {
        BatterySnapshot = snapshot;
        _ = ReconcileStateAsync("battery change");
    }

    private void OnRefreshTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _ = ReconcileStateAsync("periodic refresh");
    }

    public void RefreshNow()
    {
        _ = RefreshNowAsync();
    }

    public Task RefreshNowAsync()
    {
        return ReconcileStateAsync("manual refresh");
    }

    public void SetAutomaticMode()
    {
        _ = SetAutomaticModeAsync();
    }

    public Task SetAutomaticModeAsync()
    {
        return SetControlModeAsync(RunnerControlMode.Automatic, "automatic mode");
    }

    public void ForceStart()
    {
        _ = ForceStartAsync();
    }

    public Task ForceStartAsync()
    {
        return SetControlModeAsync(RunnerControlMode.ForceRunning, "manual start");
    }

    public void ForceStop()
    {
        _ = ForceStopAsync();
    }

    public Task ForceStopAsync()
    {
        return SetControlModeAsync(RunnerControlMode.ForceStopped, "manual stop");
    }

    public async Task StartAllAsync()
    {
        await _runnerManager.StartAllAsync();
        UpdateAggregateSnapshot();
    }

    public async Task StopAllAsync()
    {
        await _runnerManager.StopAllAsync();
        UpdateAggregateSnapshot();
    }

    public Task StartRunnerAsync(string runnerId)
    {
        return _runnerManager.GetRunner(runnerId)?.StartAsync() ?? Task.CompletedTask;
    }

    public Task StopRunnerAsync(string runnerId)
    {
        return _runnerManager.GetRunner(runnerId)?.StopAsync() ?? Task.CompletedTask;
    }

    public Task RestartRunnerAsync(string runnerId)
    {
        return _runnerManager.GetRunner(runnerId)?.RestartAsync() ?? Task.CompletedTask;
    }

    public void OpenRunnerDirectory(string? runnerId = null)
    {
        var directory = runnerId == null
            ? RunnerDirectory
            : _runnerManager.GetRunner(runnerId)?.Profile.RunnerDirectory;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void OpenRunnerLogs(string runnerId)
    {
        var directory = _runnerManager.GetRunner(runnerId)?.Profile.RunnerDirectory;
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var diag = Path.Combine(directory, "_diag");
        if (Directory.Exists(diag))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = diag,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async Task SetControlModeAsync(RunnerControlMode mode, string trigger)
    {
        ControlMode = mode;
        _preferencesFactory.Create().ControlMode = mode;
        await ReconcileStateAsync(trigger);
    }

    private async Task ReconcileStateAsync(string trigger)
    {
        await _reconcileLock.WaitAsync();
        try
        {
            var networkSnapshot = NetworkSnapshot;
            var batterySnapshot = BatterySnapshot;
            var controlMode = ControlMode;

            await Task.Run(() => _runnerManager.RefreshAllAsync(networkSnapshot, batterySnapshot, controlMode));
            UpdateAggregateSnapshot();
            LastErrorMessage = Runners.Select(runner => runner.LastErrorMessage).FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
        }
        catch (Exception ex)
        {
            LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, ex.Message);
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private void UpdateAggregateSnapshot(bool runnersChanged = false)
    {
        var snapshots = Runners.Select(runner => runner.Snapshot).ToList();
        var primary = snapshots.FirstOrDefault(snapshot => snapshot.Runner.IsRunning)
            ?? snapshots.FirstOrDefault()
            ?? new RunnerInstanceSnapshot();

        RunnerSnapshot = primary.Runner;
        ResourceUsage = new RunnerResourceUsage
        {
            IsRunning = snapshots.Any(snapshot => snapshot.Runner.IsRunning),
            IsJobActive = snapshots.Any(snapshot => snapshot.ResourceUsage.IsJobActive),
            TotalCpuPercent = snapshots.Sum(snapshot => snapshot.ResourceUsage.TotalCpuPercent),
            TotalMemoryBytes = snapshots.Sum(snapshot => snapshot.ResourceUsage.TotalMemoryBytes),
            ProcessCount = snapshots.Sum(snapshot => snapshot.ResourceUsage.ProcessCount),
            Processes = snapshots.SelectMany(snapshot => snapshot.ResourceUsage.Processes).ToList(),
            Timestamp = DateTime.Now,
            Warning = snapshots.Select(snapshot => snapshot.ResourceUsage.Warning).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Error = snapshots.Select(snapshot => snapshot.ResourceUsage.Error).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        };
        LastRefreshTime = snapshots.Select(snapshot => snapshot.LastRefreshTime).Where(value => value.HasValue).DefaultIfEmpty().Max();
        if (runnersChanged)
            OnPropertyChanged(nameof(Runners));
    }

    public string PolicySummary => ControlMode switch
    {
        RunnerControlMode.Automatic => NetworkSnapshot.Kind switch
        {
            NetworkConditionKind.Unmetered => _localization.Get(LocalizationKeys.PolicyAutomaticRun),
            NetworkConditionKind.Expensive => StopRunnerOnMeteredNetwork
                ? _localization.Get(LocalizationKeys.PolicyAutomaticExpensive)
                : _localization.Get(LocalizationKeys.PolicyAutomaticExpensiveIgnored),
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
        _runnerManager.Dispose();
    }
}
