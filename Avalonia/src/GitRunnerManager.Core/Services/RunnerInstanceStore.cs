using CommunityToolkit.Mvvm.ComponentModel;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Core.Services;

public partial class RunnerInstanceStore : ObservableObject, IDisposable
{
    private readonly IRunnerController _controller;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly ILocalizationService _localization;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    [ObservableProperty]
    private RunnerConfig _profile;

    [ObservableProperty]
    private RunnerSnapshot _runnerSnapshot = RunnerSnapshot.Stopped;

    [ObservableProperty]
    private RunnerResourceUsage _resourceUsage = RunnerResourceUsage.Zero;

    [ObservableProperty]
    private string? _lastErrorMessage;

    [ObservableProperty]
    private DateTime? _lastRefreshTime;

    public RunnerInstanceStore(
        RunnerConfig profile,
        IRunnerController controller,
        IResourceMonitor resourceMonitor,
        ILocalizationService localization)
    {
        _profile = profile.Clone();
        _controller = controller;
        _resourceMonitor = resourceMonitor;
        _localization = localization;
    }

    public RunnerInstanceSnapshot Snapshot => new()
    {
        Profile = Profile.Clone(),
        Runner = RunnerSnapshot,
        ResourceUsage = ResourceUsage,
        LastErrorMessage = LastErrorMessage,
        LastRefreshTime = LastRefreshTime
    };

    public async Task ReconcileAsync(
        NetworkConditionSnapshot network,
        BatterySnapshot battery,
        RunnerControlMode globalControlMode)
    {
        if (_disposed)
            return;

        try
        {
            await _lock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed)
                return;

            RefreshSnapshot();
            await ApplyDesiredStateAsync(network, battery, globalControlMode);
            RefreshSnapshot();
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StartAsync()
    {
        await RunActionAsync(_controller.StartAsync);
    }

    public async Task StopAsync()
    {
        await RunActionAsync(_controller.StopAsync);
    }

    public async Task RestartAsync()
    {
        await RunActionAsync(async () =>
        {
            await _controller.StopAsync();
            await _controller.StartAsync();
        });
    }

    public void RefreshSnapshot()
    {
        var snapshot = _controller.GetCurrentSnapshot();
        if (snapshot.IsRunning && snapshot.Activity.Kind == RunnerActivityKind.Unknown)
        {
            snapshot = new RunnerSnapshot
            {
                IsRunning = true,
                Activity = RunnerSnapshot.IsRunning && RunnerSnapshot.Activity.Kind != RunnerActivityKind.Unknown
                    ? RunnerSnapshot.Activity
                    : new RunnerActivitySnapshot
                    {
                        Kind = RunnerActivityKind.Waiting,
                        Description = _localization.Get(LocalizationKeys.ActivityWaitingOrStarting)
                    }
            };
        }

        RunnerSnapshot = snapshot;
        ResourceUsage = _resourceMonitor.GetCurrentUsage();
        LastRefreshTime = DateTime.Now;
    }

    private async Task ApplyDesiredStateAsync(
        NetworkConditionSnapshot network,
        BatterySnapshot battery,
        RunnerControlMode globalControlMode)
    {
        if (!Profile.IsEnabled)
        {
            await _controller.StopAsync();
            return;
        }

        if (Profile.StopOnBattery && !battery.CanRun)
        {
            if (IsActiveJobRunning)
                return;

            await _controller.StopAsync();
            return;
        }

        switch (globalControlMode)
        {
            case RunnerControlMode.ForceRunning:
                await _controller.StartAsync();
                return;
            case RunnerControlMode.ForceStopped:
                await _controller.StopAsync();
                return;
        }

        if (!Profile.AutomaticModeEnabled)
            return;

        if (!Profile.AutoStartEnabled)
            return;

        var decision = !Profile.StopOnMeteredNetwork && network.Kind == NetworkConditionKind.Expensive
            ? NetworkDecision.Run
            : network.AutomaticDecision;

        switch (decision)
        {
            case NetworkDecision.Run:
                await _controller.StartAsync();
                break;
            case NetworkDecision.Stop:
                if (IsActiveJobRunning)
                    return;

                await _controller.StopAsync();
                break;
        }
    }

    private bool IsActiveJobRunning =>
        RunnerSnapshot.Activity.Kind == RunnerActivityKind.Busy ||
        ResourceUsage.IsJobActive;

    private async Task RunActionAsync(Func<Task> action)
    {
        if (_disposed)
            return;

        try
        {
            await _lock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed)
                return;

            await action();
            RefreshSnapshot();
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastErrorMessage = _localization.Get(LocalizationKeys.ErrorRunnerHandling, ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _resourceMonitor.Stop();
        _controller.Dispose();
        _lock.Dispose();
    }
}
