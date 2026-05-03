using GitRunnerManager.Core.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class DiagnosticLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GitRunnerManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_CreatesLogFileWithMessage()
    {
        var path = Path.Combine(_directory, "app.log");

        DiagnosticLog.Write("hello diagnostics", path);

        var content = File.ReadAllText(path);
        Assert.Contains("hello diagnostics", content);
    }

    [Fact]
    public void WriteException_IncludesExceptionDetails()
    {
        var path = Path.Combine(_directory, "app.log");

        DiagnosticLog.WriteException("failed", new InvalidOperationException("boom"), path);

        var content = File.ReadAllText(path);
        Assert.Contains("failed", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void Write_WithUnavailableDefaultPath_DoesNotThrow()
    {
        var fileAsDirectory = Path.Combine(_directory, "not-a-directory");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(fileAsDirectory, "");
        var path = Path.Combine(fileAsDirectory, "app.log");

        var exception = Record.Exception(() => DiagnosticLog.Write("fallback diagnostic", path));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
