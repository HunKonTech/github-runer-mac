using CommunityToolkit.Mvvm.ComponentModel;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Core.Services;

public partial class RunnerManager : ObservableObject, IRunnerManager
{
    private readonly IRunnerControllerFactory _controllerFactory;
    private readonly IResourceMonitorFactory _resourceMonitorFactory;
    private readonly IPreferencesStoreFactory _preferencesFactory;
    private readonly ILocalizationService _localization;
    private readonly List<RunnerInstanceStore> _runners = [];
    private readonly object _runnersLock = new();

    public RunnerManager(
        IRunnerControllerFactory controllerFactory,
        IResourceMonitorFactory resourceMonitorFactory,
        IPreferencesStoreFactory preferencesFactory,
        ILocalizationService localization)
    {
        _controllerFactory = controllerFactory;
        _resourceMonitorFactory = resourceMonitorFactory;
        _preferencesFactory = preferencesFactory;
        _localization = localization;
        ReloadProfiles();
    }

    public IReadOnlyList<RunnerInstanceStore> Runners => GetRunnersSnapshot();

    public RunnerInstanceStore? GetRunner(string runnerId)
    {
        lock (_runnersLock)
        {
            return _runners.FirstOrDefault(runner => runner.Profile.Id == runnerId);
        }
    }

    public void ReloadProfiles()
    {
        var newRunners = _preferencesFactory.Create().RunnerProfiles.Select(profile =>
        {
            var directory = new DirectoryInfo(profile.RunnerDirectory);
            var runner = new RunnerInstanceStore(
                profile,
                _controllerFactory.Create(directory),
                _resourceMonitorFactory.Create(directory),
                _localization);
            runner.PropertyChanged += OnRunnerPropertyChanged;
            return runner;
        }).ToList();

        List<RunnerInstanceStore> oldRunners;
        lock (_runnersLock)
        {
            oldRunners = [.. _runners];
            _runners.Clear();
            _runners.AddRange(newRunners);
        }

        foreach (var runner in oldRunners)
        {
            runner.PropertyChanged -= OnRunnerPropertyChanged;
            runner.Dispose();
        }

        OnPropertyChanged(nameof(Runners));
    }

    public void SaveProfile(RunnerConfig profile)
    {
        var prefs = _preferencesFactory.Create();
        var profiles = prefs.RunnerProfiles;
        var index = profiles.FindIndex(item => item.Id == profile.Id);
        if (index >= 0)
            profiles[index] = profile.Clone();
        else
            profiles.Add(profile.Clone());

        prefs.RunnerProfiles = profiles;
        ReloadProfiles();
    }

    public void AddProfile(RunnerConfig profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
            profile.DisplayName = $"Runner {Runners.Count + 1}";

        var prefs = _preferencesFactory.Create();
        var profiles = prefs.RunnerProfiles;
        profiles.Add(profile.Clone());
        prefs.RunnerProfiles = profiles;
        ReloadProfiles();
    }

    public void RemoveProfile(string runnerId)
    {
        var prefs = _preferencesFactory.Create();
        var profiles = prefs.RunnerProfiles.Where(profile => profile.Id != runnerId).ToList();
        prefs.RunnerProfiles = profiles;
        ReloadProfiles();
    }

    public async Task RefreshAllAsync(
        NetworkConditionSnapshot network,
        BatterySnapshot battery,
        RunnerControlMode controlMode)
    {
        await Task.WhenAll(GetRunnersSnapshot().Select(runner => runner.ReconcileAsync(network, battery, controlMode)));
    }

    public async Task StartAllAsync()
    {
        await Task.WhenAll(GetRunnersSnapshot()
            .Where(runner => runner.Profile.IsEnabled)
            .Select(runner => runner.StartAsync()));
    }

    public async Task StopAllAsync()
    {
        await Task.WhenAll(GetRunnersSnapshot().Select(runner => runner.StopAsync()));
    }

    private void OnRunnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Runners));
    }

    public void Dispose()
    {
        List<RunnerInstanceStore> runners;
        lock (_runnersLock)
        {
            runners = [.. _runners];
            _runners.Clear();
        }

        foreach (var runner in runners)
        {
            runner.PropertyChanged -= OnRunnerPropertyChanged;
            runner.Dispose();
        }
    }

    private List<RunnerInstanceStore> GetRunnersSnapshot()
    {
        lock (_runnersLock)
        {
            return [.. _runners];
        }
    }
}
