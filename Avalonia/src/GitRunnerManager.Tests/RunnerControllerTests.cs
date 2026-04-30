using System.Diagnostics;
using System.Reflection;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Services;
using GitRunnerManager.Platform.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class RunnerControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GitRunnerManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateProcessStartInfo_RedirectsOutputPipesForAsyncDrain()
    {
        Directory.CreateDirectory(_directory);
        var script = Path.Combine(_directory, OperatingSystem.IsWindows() ? "run.cmd" : "run.sh");
        File.WriteAllText(script, "");
        var controller = new RunnerController(new DirectoryInfo(_directory), new RunnerLogParser(new LocalizationService()));

        var method = typeof(RunnerController).GetMethod("CreateProcessStartInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        var startInfo = Assert.IsType<ProcessStartInfo>(method?.Invoke(controller, [script]));

        Assert.False(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
