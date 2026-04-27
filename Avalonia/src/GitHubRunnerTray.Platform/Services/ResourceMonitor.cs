using System.Diagnostics;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Platform.Services;

public class ResourceMonitor : IResourceMonitor
{
    private readonly DirectoryInfo _runnerDirectory;
    private bool _stopped;

    public ResourceMonitor(DirectoryInfo runnerDirectory)
    {
        _runnerDirectory = runnerDirectory;
    }

    public void Stop()
    {
        _stopped = true;
    }

    public RunnerResourceUsage GetCurrentUsage()
    {
        if (_stopped)
            return RunnerResourceUsage.Zero;

        try
        {
            if (OperatingSystem.IsWindows())
                return GetWindowsUsage();
            else
                return GetUnixUsage();
        }
        catch
        {
            return RunnerResourceUsage.Zero;
        }
    }

    private RunnerResourceUsage GetUnixUsage()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/ps",
                Arguments = "-axo pid,ppid,pcpu,rss,comm",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return RunnerResourceUsage.Zero;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return ParseUnixOutput(output);
        }
        catch
        {
            return RunnerResourceUsage.Zero;
        }
    }

    private RunnerResourceUsage ParseUnixOutput(string output)
    {
        var hasListener = false;
        var hasWorker = false;
        var cpuPercent = 0.0;
        var rssKB = 0.0;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5)
                continue;

            var command = fields[4].Trim();
            var processName = Path.GetFileName(command);

            if (processName is "Runner.Listener" or "Runner.Worker" or "RunnerService")
            {
                if (processName == "Runner.Listener")
                    hasListener = true;
                if (processName == "Runner.Worker")
                    hasWorker = true;

                if (double.TryParse(fields[2], out var cpu))
                    cpuPercent += cpu;
                if (double.TryParse(fields[3], out var rss))
                    rssKB += rss;
            }
        }

        var isRunning = hasListener || hasWorker;

        return new RunnerResourceUsage
        {
            IsRunning = isRunning,
            IsJobActive = hasWorker,
            CpuPercent = isRunning ? cpuPercent : 0,
            MemoryMB = isRunning ? rssKB / 1024 : 0
        };
    }

    private RunnerResourceUsage GetWindowsUsage()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "process where \"name='Runner.Listener.exe' or name='Runner.Worker.exe'\" get WorkingSetSize,ThreadCount /format:csv",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return RunnerResourceUsage.Zero;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return ParseWindowsOutput(output);
        }
        catch
        {
            return RunnerResourceUsage.Zero;
        }
    }

    private RunnerResourceUsage ParseWindowsOutput(string output)
    {
        var hasListener = false;
        var hasWorker = false;
        var memoryMB = 0.0;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Skip(1))
        {
            if (!line.Contains("Runner."))
                continue;

            if (line.Contains("Listener"))
                hasListener = true;
            if (line.Contains("Worker"))
                hasWorker = true;

            var fields = line.Split(',');
            if (fields.Length > 1 && double.TryParse(fields[1], out var workingSet))
                memoryMB += workingSet / (1024 * 1024);
        }

        var isRunning = hasListener || hasWorker;

        return new RunnerResourceUsage
        {
            IsRunning = isRunning,
            IsJobActive = hasWorker,
            CpuPercent = 0,
            MemoryMB = memoryMB
        };
    }
}

public class ResourceMonitorFactory : IResourceMonitorFactory
{
    public IResourceMonitor Create(DirectoryInfo runnerDirectory)
    {
        return new ResourceMonitor(runnerDirectory);
    }
}