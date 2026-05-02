using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;
using GitRunnerManager.Platform.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class RunnerFolderValidatorTests
{
    [Fact]
    public void Validate_WithRunnerMarkers_ReturnsValid()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "run.sh"), "");
        File.WriteAllText(Path.Combine(temp.Path, ".runner"), "{}");

        var result = new RunnerFolderValidator().Validate(temp.Path);

        Assert.True(result.IsValid);
        Assert.Contains("run.sh", result.Markers);
        Assert.Contains(".runner", result.Markers);
    }

    [Fact]
    public void Validate_WithoutRunnerMarkers_ReturnsInvalid()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "README.md"), "");

        var result = new RunnerFolderValidator().Validate(temp.Path);

        Assert.False(result.IsValid);
    }
}

public class RunnerProfileImportTests
{
    [Fact]
    public void AddProfile_PersistsImportedRunnerProfile()
    {
        var prefs = new InMemoryPreferencesStore { RunnerProfiles = [] };
        var manager = new RunnerManager(
            new FakeControllerFactory(),
            new FakeResourceMonitorFactory(),
            new FakePreferencesStoreFactory(prefs),
            new LocalizationService());
        var profile = new RunnerConfig
        {
            Id = "imported",
            DisplayName = "Imported",
            RunnerDirectory = "/tmp/imported-runner",
            AutoStartEnabled = false
        };

        manager.AddProfile(profile);

        var stored = Assert.Single(prefs.RunnerProfiles);
        Assert.Equal("imported", stored.Id);
        Assert.Equal("/tmp/imported-runner", stored.RunnerDirectory);
        Assert.False(stored.AutoStartEnabled);
    }
}

public class RunnerLogServiceTests
{
    [Fact]
    public void ReadLog_SelectsLatestRunnerLog()
    {
        using var temp = new TemporaryDirectory();
        var diag = Directory.CreateDirectory(Path.Combine(temp.Path, "_diag")).FullName;
        var older = Path.Combine(diag, "Runner_older.log");
        var latest = Path.Combine(diag, "Runner_latest.log");
        File.WriteAllText(older, "old");
        File.WriteAllText(latest, "latest");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(latest, DateTime.UtcNow);

        var snapshot = new RunnerLogService().ReadLog(new RunnerConfig { RunnerDirectory = temp.Path }, preferActiveLog: true);

        Assert.Equal(latest, snapshot.FilePath);
        Assert.Equal("latest", snapshot.Content);
    }

    [Fact]
    public void ReadLog_BoundsLargeLogAndKeepsHeaderAndTail()
    {
        using var temp = new TemporaryDirectory();
        var diag = Directory.CreateDirectory(Path.Combine(temp.Path, "_diag")).FullName;
        var log = Path.Combine(diag, "Runner_large.log");
        File.WriteAllText(log, "header\n" + new string('x', 100_000) + "\ntail");

        var snapshot = new RunnerLogService().ReadLog(new RunnerConfig { RunnerDirectory = temp.Path }, preferActiveLog: false, maxBytes: 16 * 1024);

        Assert.True(snapshot.IsTruncated);
        Assert.Contains("header", snapshot.Content);
        Assert.Contains("tail", snapshot.Content);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(snapshot.Content) < 20 * 1024);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public TemporaryDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, true);
    }
}
