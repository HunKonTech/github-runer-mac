using System.Diagnostics;
using System.Globalization;
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
                Arguments = "-axo pid=,ppid=,pcpu=,rss=,args=",
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
        var processes = new List<UnixProcessInfo>();
        var runnerDirectory = _runnerDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var fields = line.Trim().Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5)
                continue;

            if (!int.TryParse(fields[0], out var pid))
                continue;

            if (!int.TryParse(fields[1], out var parentPid))
                continue;

            _ = double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu);
            _ = double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rss);

            processes.Add(new UnixProcessInfo(pid, parentPid, cpu, rss, fields[4]));
        }

        var processByPid = processes.ToDictionary(process => process.Pid);
        var rootPids = processes
            .Where(process => ProcessBelongsToRunnerDirectory(process, runnerDirectory))
            .Select(process => process.Pid)
            .ToHashSet();

        foreach (var process in processes)
        {
            if (!IsRunnerProcess(process) || !IsInRunnerTree(process, rootPids, processByPid))
                continue;

            if (IsListenerProcess(process))
                hasListener = true;
            if (IsWorkerProcess(process))
                hasWorker = true;

            cpuPercent += process.CpuPercent;
            rssKB += process.RssKB;
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

    private static bool ProcessBelongsToRunnerDirectory(UnixProcessInfo process, string runnerDirectory)
    {
        return process.Command.Contains(runnerDirectory, StringComparison.Ordinal);
    }

    private static bool IsInRunnerTree(
        UnixProcessInfo process,
        HashSet<int> rootPids,
        Dictionary<int, UnixProcessInfo> processByPid)
    {
        var current = process;
        var visited = new HashSet<int>();

        while (visited.Add(current.Pid))
        {
            if (rootPids.Contains(current.Pid))
                return true;

            if (!processByPid.TryGetValue(current.ParentPid, out current!))
                return false;
        }

        return false;
    }

    private static bool IsRunnerProcess(UnixProcessInfo process)
    {
        return IsListenerProcess(process) || IsWorkerProcess(process) || ProcessName(process) == "RunnerService";
    }

    private static bool IsListenerProcess(UnixProcessInfo process)
    {
        return ProcessName(process) == "Runner.Listener";
    }

    private static bool IsWorkerProcess(UnixProcessInfo process)
    {
        return ProcessName(process) == "Runner.Worker";
    }

    private static string ProcessName(UnixProcessInfo process)
    {
        var executable = process.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return Path.GetFileName(executable);
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

internal sealed record UnixProcessInfo(
    int Pid,
    int ParentPid,
    double CpuPercent,
    double RssKB,
    string Command);

public class ResourceMonitorFactory : IResourceMonitorFactory
{
    public IResourceMonitor Create(DirectoryInfo runnerDirectory)
    {
        return new ResourceMonitor(runnerDirectory);
    }
}
