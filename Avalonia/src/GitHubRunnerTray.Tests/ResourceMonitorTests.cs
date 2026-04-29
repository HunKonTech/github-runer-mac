using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Platform.Services;
using Xunit;

namespace GitHubRunnerTray.Tests;

public class ResourceMonitorTests
{
    [Fact]
    public void Aggregate_IncludesRecursiveDescendants()
    {
        var usage = ProcessTreeResourceAggregator.Aggregate(
        [
            Process(100, 1, "Runner.Listener", "/Users/test/actions-runner/bin/Runner.Listener", 1.5, 50),
            Process(101, 100, "Runner.Worker", "/Users/test/actions-runner/bin/Runner.Worker", 3.5, 100),
            Process(102, 101, "dotnet", "dotnet build", 80, 500),
            Process(103, 102, "clang", "clang source.c", 42, 250),
            Process(200, 1, "dotnet", "dotnet unrelated", 99, 999)
        ], "/Users/test/actions-runner");

        Assert.True(usage.IsRunning);
        Assert.True(usage.IsJobActive);
        Assert.Equal(100, usage.ParentProcessId);
        Assert.Equal(4, usage.ProcessCount);
        Assert.Equal(127, usage.TotalCpuPercent);
        Assert.Equal(900L * 1024 * 1024, usage.TotalMemoryBytes);
        Assert.DoesNotContain(usage.Processes, process => process.ProcessId == 200);
    }

    [Fact]
    public void Aggregate_DoesNotClampCpuAboveOneHundredPercent()
    {
        var usage = ProcessTreeResourceAggregator.Aggregate(
        [
            Process(100, 1, "Runner.Listener", "/Users/test/actions-runner/bin/Runner.Listener", 12, 50),
            Process(101, 100, "Runner.Worker", "/Users/test/actions-runner/bin/Runner.Worker", 75, 100),
            Process(102, 101, "xcodebuild", "xcodebuild build", 160, 500)
        ], "/Users/test/actions-runner");

        Assert.Equal(247, usage.TotalCpuPercent);
    }

    [Fact]
    public void ParseUnixOutput_SumsRunnerTreeFromPsRows()
    {
        var output = """
          100     1   2.0  51200 /Users/test/actions-runner/bin/Runner.Listener /Users/test/actions-runner/bin/Runner.Listener run
          101   100   3.0 102400 /Users/test/actions-runner/bin/Runner.Worker /Users/test/actions-runner/bin/Runner.Worker spawn
          102   101 181.0 524288 /usr/local/bin/dotnet dotnet build
          103   102  11.0 131072 /usr/bin/bash bash ./build.sh
          200     1  99.0 999999 /usr/local/bin/dotnet dotnet unrelated
        """;

        var usage = ResourceMonitor.ParseUnixOutput(output, "/Users/test/actions-runner");

        Assert.Equal(4, usage.ProcessCount);
        Assert.Equal(197, usage.TotalCpuPercent);
        Assert.Equal((51200L + 102400 + 524288 + 131072) * 1024, usage.TotalMemoryBytes);
    }

    private static ProcessResourceInfo Process(int pid, int parentPid, string name, string commandLine, double cpuPercent, long memoryMb)
    {
        return new ProcessResourceInfo
        {
            ProcessId = pid,
            ParentProcessId = parentPid,
            Name = name,
            CommandLine = commandLine,
            CpuPercent = cpuPercent,
            MemoryBytes = memoryMb * 1024 * 1024
        };
    }
}
