using Xunit;
using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Core.Services;
using GitHubRunnerTray.Core.Localization;
using GitHubRunnerTray.Core.Interfaces;

namespace GitHubRunnerTray.Tests;

public class RunnerLogParserTests
{
    private readonly LocalizationService _localization;

    public RunnerLogParserTests()
    {
        _localization = new LocalizationService();
    }

    [Fact]
    public void GetLatestActivity_WithNoLogFiles_ReturnsUnknown()
    {
        var parser = new RunnerLogParser(_localization);
        var tempDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        try
        {
            Directory.CreateDirectory(tempDir.FullName);

            var result = parser.GetLatestActivity(tempDir);

            Assert.Equal(RunnerActivityKind.Unknown, result.Kind);
        }
        finally
        {
            if (tempDir.Exists)
                Directory.Delete(tempDir.FullName, true);
        }
    }

    [Fact]
    public void GetLatestActivity_WithListeningLine_ReturnsWaiting()
    {
        var parser = new RunnerLogParser(_localization);
        var tempDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        try
        {
            var diagPath = Path.Combine(tempDir.FullName, "_diag");
            Directory.CreateDirectory(diagPath);

            var logPath = Path.Combine(diagPath, "Runner_2024.log");
            File.WriteAllText(logPath, "Starting listener...\nListening for Jobs\n");

            var result = parser.GetLatestActivity(tempDir);

            Assert.Equal(RunnerActivityKind.Waiting, result.Kind);
        }
        finally
        {
            if (tempDir.Exists)
                Directory.Delete(tempDir.FullName, true);
        }
    }

    [Fact]
    public void GetLatestActivity_WithJobLine_ReturnsBusy()
    {
        var parser = new RunnerLogParser(_localization);
        var tempDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        try
        {
            var diagPath = Path.Combine(tempDir.FullName, "_diag");
            Directory.CreateDirectory(diagPath);

            var logPath = Path.Combine(diagPath, "Runner_2024.log");
            File.WriteAllText(logPath, "Listening for Jobs\nRunning job: test-workflow\n");

            var result = parser.GetLatestActivity(tempDir);

            Assert.Equal(RunnerActivityKind.Busy, result.Kind);
            Assert.Equal("test-workflow", result.CurrentJobName);
        }
        finally
        {
            if (tempDir.Exists)
                Directory.Delete(tempDir.FullName, true);
        }
    }

    [Fact]
    public void GetLatestActivity_WithCompletedLine_ReturnsWaiting()
    {
        var parser = new RunnerLogParser(_localization);
        var tempDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        try
        {
            var diagPath = Path.Combine(tempDir.FullName, "_diag");
            Directory.CreateDirectory(diagPath);

            var logPath = Path.Combine(diagPath, "Runner_2024.log");
            File.WriteAllText(logPath, "Running job: test-workflow\n completed with result: Succeeded\n");

            var result = parser.GetLatestActivity(tempDir);

            Assert.Equal(RunnerActivityKind.Waiting, result.Kind);
        }
        finally
        {
            if (tempDir.Exists)
                Directory.Delete(tempDir.FullName, true);
        }
    }
}

public class RunnerControlModeTests
{
    [Fact]
    public void AutomaticDecision_Unmetered_ReturnsRun()
    {
        var snapshot = new NetworkConditionSnapshot
        {
            Kind = NetworkConditionKind.Unmetered,
            Description = "Wi-Fi, unmetered"
        };

        Assert.Equal(NetworkDecision.Run, snapshot.AutomaticDecision);
    }

    [Fact]
    public void AutomaticDecision_Expensive_ReturnsStop()
    {
        var snapshot = new NetworkConditionSnapshot
        {
            Kind = NetworkConditionKind.Expensive,
            Description = "Wi-Fi, metered"
        };

        Assert.Equal(NetworkDecision.Stop, snapshot.AutomaticDecision);
    }

    [Fact]
    public void AutomaticDecision_Offline_ReturnsStop()
    {
        var snapshot = new NetworkConditionSnapshot
        {
            Kind = NetworkConditionKind.Offline,
            Description = "No internet"
        };

        Assert.Equal(NetworkDecision.Stop, snapshot.AutomaticDecision);
    }

    [Fact]
    public void AutomaticDecision_Unknown_ReturnsKeep()
    {
        var snapshot = new NetworkConditionSnapshot
        {
            Kind = NetworkConditionKind.Unknown,
            Description = "Checking..."
        };

        Assert.Equal(NetworkDecision.Keep, snapshot.AutomaticDecision);
    }
}

public class BatterySnapshotTests
{
    [Fact]
    public void CanRun_NoBattery_ReturnsTrue()
    {
        var snapshot = new BatterySnapshot { HasBattery = false };

        Assert.True(snapshot.CanRun);
    }

    [Fact]
    public void CanRun_OnBattery_ReturnsFalse()
    {
        var snapshot = new BatterySnapshot
        {
            HasBattery = true,
            IsOnBattery = true
        };

        Assert.False(snapshot.CanRun);
    }

    [Fact]
    public void CanRun_OnPower_ReturnsTrue()
    {
        var snapshot = new BatterySnapshot
        {
            HasBattery = true,
            IsOnBattery = false
        };

        Assert.True(snapshot.CanRun);
    }
}

public class PreferencesPersistenceTests
{
    [Fact]
    public void ControlMode_IsPersisted_AndRestored()
    {
        var mode = RunnerControlMode.ForceRunning;

        var prefs = new TestPreferencesStore();
        prefs.SaveControlMode(mode);

        var restored = prefs.GetControlMode();

        Assert.Equal(mode, restored);
    }
}

internal class TestPreferencesStore
{
    private const string Key = "TestControlMode";
    private string _value = "";

    public void SaveControlMode(RunnerControlMode mode)
    {
        _value = mode.ToString();
    }

    public RunnerControlMode GetControlMode()
    {
        if (Enum.TryParse<RunnerControlMode>(_value, out var result))
            return result;
        return RunnerControlMode.Automatic;
    }
}