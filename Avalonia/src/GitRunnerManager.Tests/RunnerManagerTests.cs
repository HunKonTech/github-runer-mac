using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;
using System.Diagnostics;
using Xunit;

namespace GitRunnerManager.Tests;

public class RunnerManagerTests
{
    [Fact]
    public async Task RefreshAll_ManagesEachRunnerProfileIndependently()
    {
        var prefs = new InMemoryPreferencesStore
        {
            RunnerProfiles =
            [
                new RunnerConfig { Id = "one", DisplayName = "One", RunnerDirectory = "/tmp/one" },
                new RunnerConfig { Id = "two", DisplayName = "Two", RunnerDirectory = "/tmp/two", IsEnabled = false }
            ]
        };
        var controllerFactory = new FakeControllerFactory();
        var manager = new RunnerManager(
            controllerFactory,
            new FakeResourceMonitorFactory(),
            new FakePreferencesStoreFactory(prefs),
            new LocalizationService());

        await manager.RefreshAllAsync(
            new NetworkConditionSnapshot { Kind = NetworkConditionKind.Unmetered, Description = "unmetered" },
            BatterySnapshot.NoBattery,
            RunnerControlMode.Automatic);

        Assert.Equal(2, manager.Runners.Count);
        Assert.Equal(1, controllerFactory.Controllers["/tmp/one"].StartCount);
        Assert.Equal(0, controllerFactory.Controllers["/tmp/one"].StopCount);
        Assert.Equal(0, controllerFactory.Controllers["/tmp/two"].StartCount);
        Assert.Equal(1, controllerFactory.Controllers["/tmp/two"].StopCount);
    }

    [Fact]
    public void RunnerUpdateDecision_ComparesSemanticVersions()
    {
        Assert.True(RunnerUpdateDecision.IsUpdateAvailable("2.300.0", "2.301.0"));
        Assert.False(RunnerUpdateDecision.IsUpdateAvailable("2.301.0", "2.301.0"));
        Assert.False(RunnerUpdateDecision.IsUpdateAvailable("2.302.0", "2.301.0"));
        Assert.True(RunnerUpdateDecision.IsUpdateAvailable(null, "2.301.0"));
    }

    [Fact]
    public async Task StopRunnerOnMeteredNetwork_SetterDoesNotBlockWhileRunnerStateRefreshRuns()
    {
        var prefs = new InMemoryPreferencesStore
        {
            RunnerProfiles =
            [
                new RunnerConfig
                {
                    Id = "one",
                    DisplayName = "One",
                    RunnerDirectory = "/tmp/one",
                    StopOnMeteredNetwork = false
                }
            ]
        };
        var controllerFactory = new SlowStopControllerFactory();
        using var store = new RunnerTrayStore(
            controllerFactory,
            new FakeResourceMonitorFactory(),
            new FakePreferencesStoreFactory(prefs),
            new LocalizationService(),
            new FakeNetworkMonitor(),
            new FakeBatteryMonitorFactory());
        store.NetworkSnapshot = new NetworkConditionSnapshot
        {
            Kind = NetworkConditionKind.Expensive,
            Description = "metered"
        };

        var stopwatch = Stopwatch.StartNew();
        store.StopRunnerOnMeteredNetwork = true;
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 200);
        Assert.True(await controllerFactory.WaitForStopAsync(TimeSpan.FromSeconds(2)));
    }
}

internal sealed class FakePreferencesStoreFactory(InMemoryPreferencesStore store) : IPreferencesStoreFactory
{
    public IPreferencesStore Create() => store;
}

internal sealed class InMemoryPreferencesStore : IPreferencesStore
{
    public AppLanguage Language { get; set; } = AppLanguage.System;
    public string GitHubOAuthClientId { get; set; } = "";
    public string RunnerDirectory { get; set; } = "/tmp/runner";
    public List<RunnerConfig> RunnerProfiles { get; set; } = [];
    public RunnerControlMode ControlMode { get; set; } = RunnerControlMode.Automatic;
    public bool AutomaticUpdateCheckEnabled { get; set; }
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public bool StopRunnerOnBattery { get; set; }
    public bool StopRunnerOnMeteredNetwork { get; set; } = true;
}

internal sealed class FakeControllerFactory : IRunnerControllerFactory
{
    public Dictionary<string, FakeController> Controllers { get; } = [];

    public IRunnerController Create(DirectoryInfo runnerDirectory)
    {
        var controller = new FakeController();
        Controllers[runnerDirectory.FullName] = controller;
        return controller;
    }
}

internal sealed class FakeController : IRunnerController
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public bool IsRunning { get; private set; }

    public RunnerSnapshot GetCurrentSnapshot()
    {
        return new RunnerSnapshot { IsRunning = IsRunning, Activity = IsRunning ? RunnerActivitySnapshot.Unknown : RunnerActivitySnapshot.Stopped };
    }

    public Task StartAsync()
    {
        StartCount++;
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        IsRunning = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeResourceMonitorFactory : IResourceMonitorFactory
{
    public IResourceMonitor Create(DirectoryInfo runnerDirectory) => new FakeResourceMonitor();
}

internal sealed class FakeResourceMonitor : IResourceMonitor
{
    public RunnerResourceUsage GetCurrentUsage() => RunnerResourceUsage.Zero;
    public void Stop()
    {
    }
}

internal sealed class FakeNetworkMonitor : INetworkConditionMonitor
{
    public event EventHandler<NetworkConditionSnapshot>? OnChange;

    public void Start()
    {
        OnChange?.Invoke(this, NetworkConditionSnapshot.Unknown);
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeBatteryMonitorFactory : IBatteryMonitorFactory
{
    public IBatteryMonitor Create() => new FakeBatteryMonitor();
}

internal sealed class FakeBatteryMonitor : IBatteryMonitor
{
    public event EventHandler<BatterySnapshot>? OnChange;

    public void Start()
    {
        OnChange?.Invoke(this, BatterySnapshot.NoBattery);
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class SlowStopControllerFactory : IRunnerControllerFactory
{
    private readonly TaskCompletionSource _stopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IRunnerController Create(DirectoryInfo runnerDirectory)
    {
        return new SlowStopController(_stopStarted);
    }

    public async Task<bool> WaitForStopAsync(TimeSpan timeout)
    {
        var completed = await Task.WhenAny(_stopStarted.Task, Task.Delay(timeout));
        return completed == _stopStarted.Task;
    }
}

internal sealed class SlowStopController(TaskCompletionSource stopStarted) : IRunnerController
{
    public RunnerSnapshot GetCurrentSnapshot()
    {
        return RunnerSnapshot.Stopped;
    }

    public Task StartAsync()
    {
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        stopStarted.TrySetResult();
        Thread.Sleep(500);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
