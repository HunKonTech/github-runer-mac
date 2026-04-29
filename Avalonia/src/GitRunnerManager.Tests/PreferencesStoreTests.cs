using GitRunnerManager.Core.Models;
using GitRunnerManager.Platform.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class PreferencesStoreTests : IDisposable
{
    private readonly string _settingsDirectory;
    private readonly string _settingsPath;

    public PreferencesStoreTests()
    {
        _settingsDirectory = Path.Combine(Path.GetTempPath(), "GitRunnerManagerTests", Guid.NewGuid().ToString());
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
        Assert.NotEmpty(store.RunnerProfiles);
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
        Assert.Equal("/tmp/actions-runner", restored.RunnerProfiles[0].RunnerDirectory);
    }

    [Fact]
    public void LegacyRunnerDirectory_MigratesToRunnerProfiles()
    {
        File.WriteAllText(_settingsPath, """
        {
          "RunnerDirectory": "/tmp/legacy-runner",
          "ControlMode": "Automatic",
          "StopRunnerOnBattery": true,
          "StopRunnerOnMeteredNetwork": false
        }
        """);

        var store = new PreferencesStore(_settingsPath);

        var profile = Assert.Single(store.RunnerProfiles);
        Assert.Equal("/tmp/legacy-runner", profile.RunnerDirectory);
        Assert.True(profile.StopOnBattery);
        Assert.False(profile.StopOnMeteredNetwork);
        Assert.True(profile.AutomaticModeEnabled);
    }

    [Fact]
    public void RunnerProfiles_PersistAcrossInstances()
    {
        var store = new PreferencesStore(_settingsPath)
        {
            RunnerProfiles =
            [
                new RunnerConfig
                {
                    Id = "one",
                    DisplayName = "Build runner",
                    RunnerDirectory = "/tmp/runner-one",
                    GitHubOwnerOrOrg = "HunKonTech",
                    RepositoryName = "GitRunnerManager",
                    IsOrganizationRunner = false,
                    Labels = ["macos", "arm64"],
                    AutoStartEnabled = true,
                    AutomaticModeEnabled = true,
                    StopOnBattery = true,
                    StopOnMeteredNetwork = true,
                    UpdateAutomatically = false,
                    IsEnabled = true
                },
                new RunnerConfig
                {
                    Id = "two",
                    DisplayName = "Org runner",
                    RunnerDirectory = "/tmp/runner-two",
                    GitHubOwnerOrOrg = "HunKonTech",
                    IsOrganizationRunner = true,
                    Labels = ["linux"],
                    IsEnabled = false
                }
            ]
        };

        var restored = new PreferencesStore(_settingsPath);

        Assert.Equal(2, restored.RunnerProfiles.Count);
        Assert.Equal("Build runner", restored.RunnerProfiles[0].DisplayName);
        Assert.Equal(["macos", "arm64"], restored.RunnerProfiles[0].Labels);
        Assert.True(restored.RunnerProfiles[1].IsOrganizationRunner);
        Assert.False(restored.RunnerProfiles[1].IsEnabled);
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
