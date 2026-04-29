using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Platform.Services;
using Xunit;

namespace GitHubRunnerTray.Tests;

public class PreferencesStoreTests : IDisposable
{
    private readonly string _settingsDirectory;
    private readonly string _settingsPath;

    public PreferencesStoreTests()
    {
        _settingsDirectory = Path.Combine(Path.GetTempPath(), "GitHubRunnerTrayTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_settingsDirectory);
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
    }

    [Fact]
    public void Defaults_MatchBlueprint()
    {
        var store = new PreferencesStore(_settingsPath);

        Assert.Equal(AppLanguage.System, store.Language);
        Assert.Equal(UpdateChannel.Stable, store.UpdateChannel);
        Assert.Equal(RunnerControlMode.Automatic, store.ControlMode);
        Assert.False(store.StopRunnerOnBattery);
        Assert.True(store.StopRunnerOnMeteredNetwork);
        Assert.False(string.IsNullOrWhiteSpace(store.RunnerDirectory));
    }

    [Fact]
    public void Values_PersistAcrossInstances()
    {
        var store = new PreferencesStore(_settingsPath)
        {
            Language = AppLanguage.Hungarian,
            UpdateChannel = UpdateChannel.Preview,
            ControlMode = RunnerControlMode.ForceStopped,
            StopRunnerOnBattery = true,
            StopRunnerOnMeteredNetwork = false,
            RunnerDirectory = "/tmp/actions-runner"
        };

        var restored = new PreferencesStore(_settingsPath);

        Assert.Equal(AppLanguage.Hungarian, restored.Language);
        Assert.Equal(UpdateChannel.Preview, restored.UpdateChannel);
        Assert.Equal(RunnerControlMode.ForceStopped, restored.ControlMode);
        Assert.True(restored.StopRunnerOnBattery);
        Assert.False(restored.StopRunnerOnMeteredNetwork);
        Assert.Equal("/tmp/actions-runner", restored.RunnerDirectory);
    }

    [Fact]
    public void InvalidEnumValues_FallBackToDefaults()
    {
        File.WriteAllText(_settingsPath, """
        {
          "Language": "Klingon",
          "UpdateChannel": "Nightly",
          "ControlMode": "Manual"
        }
        """);

        var store = new PreferencesStore(_settingsPath);

        Assert.Equal(AppLanguage.System, store.Language);
        Assert.Equal(UpdateChannel.Stable, store.UpdateChannel);
        Assert.Equal(RunnerControlMode.Automatic, store.ControlMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
            Directory.Delete(_settingsDirectory, true);
    }
}
