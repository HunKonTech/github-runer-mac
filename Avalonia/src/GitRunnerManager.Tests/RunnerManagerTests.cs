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
    public async Task RefreshAll_GlobalManualStartIgnoresPerRunnerAutomaticMode()
    {
        var prefs = new InMemoryPreferencesStore
        {
            RunnerProfiles =
            [
                new RunnerConfig { Id = "one", DisplayName = "One", RunnerDirectory = "/tmp/one", AutomaticModeEnabled = false },
                new RunnerConfig { Id = "two", DisplayName = "Two", RunnerDirectory = "/tmp/two", AutomaticModeEnabled = false }
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
            RunnerControlMode.ForceRunning);

        Assert.Equal(1, controllerFactory.Controllers["/tmp/one"].StartCount);
        Assert.Equal(1, controllerFactory.Controllers["/tmp/two"].StartCount);
    }

    [Fact]
    public async Task RefreshAll_GlobalManualStopIgnoresPerRunnerAutomaticMode()
    {
        var prefs = new InMemoryPreferencesStore
        {
            RunnerProfiles =
            [
                new RunnerConfig { Id = "one", DisplayName = "One", RunnerDirectory = "/tmp/one", AutomaticModeEnabled = false },
                new RunnerConfig { Id = "two", DisplayName = "Two", RunnerDirectory = "/tmp/two", AutomaticModeEnabled = false }
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
            RunnerControlMode.ForceStopped);

        Assert.Equal(1, controllerFactory.Controllers["/tmp/one"].StopCount);
        Assert.Equal(1, controllerFactory.Controllers["/tmp/two"].StopCount);
    }

    [Fact]
    public async Task RefreshAll_GlobalManualStartOverridesBatteryStopRule()
    {
        var prefs = new InMemoryPreferencesStore
        {
            RunnerProfiles =
            [
                new RunnerConfig { Id = "one", DisplayName = "One", RunnerDirectory = "/tmp/one", StopOnBattery = true },
                new RunnerConfig { Id = "two", DisplayName = "Two", RunnerDirectory = "/tmp/two", StopOnBattery = true }
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
            new BatterySnapshot { HasBattery = true, IsOnBattery = true },
            RunnerControlMode.ForceRunning);

        Assert.Equal(1, controllerFactory.Controllers["/tmp/one"].StartCount);
        Assert.Equal(1, controllerFactory.Controllers["/tmp/two"].StartCount);
        Assert.Equal(0, controllerFactory.Controllers["/tmp/one"].StopCount);
        Assert.Equal(0, controllerFactory.Controllers["/tmp/two"].StopCount);
    }

    [Fact]
    public async Task ForceStartAsync_StartsEnabledRunnersWhenPreviousModeWasStopped()
    {
        var prefs = new InMemoryPreferencesStore
        {
            ControlMode = RunnerControlMode.ForceStopped,
            RunnerProfiles =
            [
                new RunnerConfig { Id = "one", DisplayName = "One", RunnerDirectory = "/tmp/one" },
                new RunnerConfig { Id = "two", DisplayName = "Two", RunnerDirectory = "/tmp/two" }
            ]
        };
        var controllerFactory = new FakeControllerFactory();
        using var store = new RunnerTrayStore(
            controllerFactory,
            new FakeResourceMonitorFactory(),
            new FakePreferencesStoreFactory(prefs),
            new LocalizationService(),
            new FakeNetworkMonitor(),
            new FakeBatteryMonitorFactory());

        await store.ForceStartAsync();

        Assert.Equal(RunnerControlMode.ForceRunning, store.ControlMode);
        Assert.Equal(1, controllerFactory.Controllers["/tmp/one"].StartCount);
        Assert.Equal(1, controllerFactory.Controllers["/tmp/two"].StartCount);
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

    [Fact]
    public async Task TryRefreshNowAsync_ReturnsFalseWhenRefreshIsAlreadyRunning()
    {
        var prefs = new InMemoryPreferencesStore
        {
            ControlMode = RunnerControlMode.ForceRunning,
            RunnerProfiles =
            [
                new RunnerConfig
                {
                    Id = "one",
                    DisplayName = "One",
                    RunnerDirectory = "/tmp/one"
                }
            ]
        };
        var controllerFactory = new BlockingStartControllerFactory();
        using var store = new RunnerTrayStore(
            controllerFactory,
            new FakeResourceMonitorFactory(),
            new FakePreferencesStoreFactory(prefs),
            new LocalizationService(),
            new FakeNetworkMonitor(),
            new FakeBatteryMonitorFactory());

        Assert.True(await controllerFactory.WaitForStartAsync(TimeSpan.FromSeconds(2)));
        Assert.False(await store.TryRefreshNowAsync());

        controllerFactory.Release();
    }

    [Fact]
    public async Task ForceStartAsync_SetsManualModeBeforeWaitingForAutomaticStop()
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
                    StopOnMeteredNetwork = true
                }
            ]
        };
        var controllerFactory = new BlockingStopControllerFactory();
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

        var refreshTask = store.RefreshNowAsync();
        Assert.True(await controllerFactory.WaitForStopAsync(TimeSpan.FromSeconds(2)));

        var startTask = store.ForceStartAsync();

        Assert.Equal(RunnerControlMode.ForceRunning, store.ControlMode);
        Assert.Equal(RunnerControlMode.ForceRunning, prefs.ControlMode);

        controllerFactory.ReleaseStop();
        await Task.WhenAll(refreshTask, startTask);
    }

    [Fact]
    public async Task RefreshAll_DefersAutomaticStopWhileJobIsActive()
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
                    StopOnMeteredNetwork = true
                }
            ]
        };
        var controllerFactory = new BusyControllerFactory();
        var manager = new RunnerManager(
            controllerFactory,
            new ActiveJobResourceMonitorFactory(),
            new FakePreferencesStoreFactory(prefs),
            new LocalizationService());

        await manager.RefreshAllAsync(
            new NetworkConditionSnapshot { Kind = NetworkConditionKind.Expensive, Description = "metered" },
            BatterySnapshot.NoBattery,
            RunnerControlMode.Automatic);

        Assert.Equal(0, controllerFactory.Controller.StopCount);
        Assert.True(controllerFactory.Controller.IsRunning);
    }

    [Fact]
    public void Runners_ReturnsStableSnapshotWhenProfilesReload()
    {
        var prefs = new InMemoryPreferencesStore
        {
            RunnerProfiles =
            [
                new RunnerConfig { Id = "one", DisplayName = "One", RunnerDirectory = "/tmp/one" }
            ]
        };
        var manager = new RunnerManager(
            new FakeControllerFactory(),
            new FakeResourceMonitorFactory(),
            new FakePreferencesStoreFactory(prefs),
            new LocalizationService());
        var runners = manager.Runners;

        prefs.RunnerProfiles =
        [
            new RunnerConfig { Id = "two", DisplayName = "Two", RunnerDirectory = "/tmp/two" }
        ];
        manager.ReloadProfiles();

        Assert.Equal("one", Assert.Single(runners).Profile.Id);
        Assert.Equal("two", Assert.Single(manager.Runners).Profile.Id);
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

internal sealed class BusyControllerFactory : IRunnerControllerFactory
{
    public BusyController Controller { get; } = new();

    public IRunnerController Create(DirectoryInfo runnerDirectory) => Controller;
}

internal sealed class BusyController : IRunnerController
{
    public int StopCount { get; private set; }
    public bool IsRunning { get; private set; } = true;

    public RunnerSnapshot GetCurrentSnapshot()
    {
        return new RunnerSnapshot
        {
            IsRunning = IsRunning,
            Activity = new RunnerActivitySnapshot
            {
                Kind = RunnerActivityKind.Busy,
                Description = "Working",
                CurrentJobName = "build"
            }
        };
    }

    public Task StartAsync()
    {
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

internal sealed class ActiveJobResourceMonitorFactory : IResourceMonitorFactory
{
    public IResourceMonitor Create(DirectoryInfo runnerDirectory) => new ActiveJobResourceMonitor();
}

internal sealed class ActiveJobResourceMonitor : IResourceMonitor
{
    public RunnerResourceUsage GetCurrentUsage()
    {
        return new RunnerResourceUsage
        {
            IsRunning = true,
            IsJobActive = true
        };
    }

    public void Stop()
    {
    }
}

internal sealed class BlockingStartControllerFactory : IRunnerControllerFactory
{
    private readonly TaskCompletionSource _startStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IRunnerController Create(DirectoryInfo runnerDirectory)
    {
        return new BlockingStartController(_startStarted, _release);
    }

    public async Task<bool> WaitForStartAsync(TimeSpan timeout)
    {
        var completed = await Task.WhenAny(_startStarted.Task, Task.Delay(timeout));
        return completed == _startStarted.Task;
    }

    public void Release()
    {
        _release.TrySetResult();
    }
}

internal sealed class BlockingStartController(TaskCompletionSource startStarted, TaskCompletionSource release) : IRunnerController
{
    public RunnerSnapshot GetCurrentSnapshot()
    {
        return RunnerSnapshot.Stopped;
    }

    public Task StartAsync()
    {
        startStarted.TrySetResult();
        return release.Task;
    }

    public Task StopAsync()
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        release.TrySetResult();
    }
}

internal sealed class BlockingStopControllerFactory : IRunnerControllerFactory
{
    private readonly TaskCompletionSource _stopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IRunnerController Create(DirectoryInfo runnerDirectory)
    {
        return new BlockingStopController(_stopStarted, _releaseStop);
    }

    public async Task<bool> WaitForStopAsync(TimeSpan timeout)
    {
        var completed = await Task.WhenAny(_stopStarted.Task, Task.Delay(timeout));
        return completed == _stopStarted.Task;
    }

    public void ReleaseStop()
    {
        _releaseStop.TrySetResult();
    }
}

internal sealed class BlockingStopController(TaskCompletionSource stopStarted, TaskCompletionSource releaseStop) : IRunnerController
{
    private bool _isRunning = true;

    public RunnerSnapshot GetCurrentSnapshot()
    {
        return new RunnerSnapshot { IsRunning = _isRunning, Activity = RunnerActivitySnapshot.Unknown };
    }

    public Task StartAsync()
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        stopStarted.TrySetResult();
        await releaseStop.Task;
        _isRunning = false;
    }

    public void Dispose()
    {
        releaseStop.TrySetResult();
    }
}
