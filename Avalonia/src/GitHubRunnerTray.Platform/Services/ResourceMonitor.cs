using System.Diagnostics;
using System.Globalization;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Platform.Services;

public class ResourceMonitor : IResourceMonitor
{
    private readonly DirectoryInfo _runnerDirectory;
    private RunnerResourceUsage _lastUsage = RunnerResourceUsage.Zero;
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
            var usage = OperatingSystem.IsWindows()
                ? GetWindowsUsage()
                : GetUnixUsage();

            _lastUsage = usage;
            return usage;
        }
        catch (Exception ex)
        {
            return WithWarning(_lastUsage, $"Resource monitoring failed: {ex.Message}");
        }
    }

    private RunnerResourceUsage GetUnixUsage()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/ps",
            Arguments = "-axo pid=,ppid=,pcpu=,rss=,comm=,args=",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return WithWarning(_lastUsage, "Resource monitoring failed: ps could not be started.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return WithWarning(_lastUsage, $"Resource monitoring failed: ps exited with {process.ExitCode}.");

        return ParseUnixOutput(output, _runnerDirectory.FullName);
    }

    internal static RunnerResourceUsage ParseUnixOutput(string output, string runnerDirectory)
    {
        var processes = new List<ProcessResourceInfo>();
        var normalizedRunnerDirectory = runnerDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5)
                continue;

            if (!int.TryParse(fields[0], out var pid))
                continue;

            if (!int.TryParse(fields[1], out var parentPid))
                continue;

            _ = double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu);
            _ = long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rssKb);

            var command = fields.Length >= 6 ? fields[5] : fields[4];
            processes.Add(new ProcessResourceInfo
            {
                ProcessId = pid,
                ParentProcessId = parentPid,
                Name = GetUnixProcessName(fields[4], command),
                CommandLine = command,
                CpuPercent = cpu,
                MemoryBytes = rssKb * 1024
            });
        }

        return ProcessTreeResourceAggregator.Aggregate(processes, normalizedRunnerDirectory);
    }

    private RunnerResourceUsage GetWindowsUsage()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wmic",
            Arguments = "process get ProcessId,ParentProcessId,Name,CommandLine,WorkingSetSize /format:csv",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return WithWarning(_lastUsage, "Resource monitoring failed: wmic could not be started.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return WithWarning(_lastUsage, $"Resource monitoring failed: wmic exited with {process.ExitCode}.");

        return ParseWindowsOutput(output, _runnerDirectory.FullName);
    }

    internal static RunnerResourceUsage ParseWindowsOutput(string output, string runnerDirectory)
    {
        var processes = new List<ProcessResourceInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return RunnerResourceUsage.Zero;

        var headers = SplitCsvLine(lines[0]).Select(header => header.Trim()).ToArray();
        var indexes = headers
            .Select((header, index) => new { header, index })
            .ToDictionary(item => item.header, item => item.index, StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Skip(1))
        {
            var fields = SplitCsvLine(line);
            if (!TryGetField(fields, indexes, "ProcessId", out var processIdText)
                || !TryGetField(fields, indexes, "ParentProcessId", out var parentProcessIdText)
                || !TryGetField(fields, indexes, "Name", out var name))
                continue;

            if (!int.TryParse(processIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                continue;

            if (!int.TryParse(parentProcessIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parentPid))
                continue;

            _ = TryGetField(fields, indexes, "CommandLine", out var commandLine);
            _ = TryGetField(fields, indexes, "WorkingSetSize", out var workingSetText);
            _ = long.TryParse(workingSetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var memoryBytes);

            processes.Add(new ProcessResourceInfo
            {
                ProcessId = pid,
                ParentProcessId = parentPid,
                Name = name,
                CommandLine = commandLine,
                CpuPercent = 0,
                MemoryBytes = memoryBytes
            });
        }

        return ProcessTreeResourceAggregator.Aggregate(processes, runnerDirectory);
    }

    private static bool TryGetField(string[] fields, IReadOnlyDictionary<string, int> indexes, string name, out string value)
    {
        value = string.Empty;
        if (!indexes.TryGetValue(name, out var index) || index < 0 || index >= fields.Length)
            return false;

        value = fields[index];
        return true;
    }

    private static RunnerResourceUsage WithWarning(RunnerResourceUsage usage, string warning)
    {
        return new RunnerResourceUsage
        {
            IsRunning = usage.IsRunning,
            IsJobActive = usage.IsJobActive,
            ParentProcessId = usage.ParentProcessId,
            TotalCpuPercent = usage.TotalCpuPercent,
            TotalMemoryBytes = usage.TotalMemoryBytes,
            ProcessCount = usage.ProcessCount,
            Processes = usage.Processes,
            Timestamp = DateTime.Now,
            Warning = warning
        };
    }

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringWriter(CultureInfo.InvariantCulture);
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.GetStringBuilder().Clear();
                continue;
            }

            current.Write(character);
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private static string GetUnixProcessName(string comm, string commandLine)
    {
        var executable = commandLine
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(executable))
        {
            var executableName = Path.GetFileName(executable);
            if (!string.IsNullOrWhiteSpace(executableName))
                return executableName;
        }

        return Path.GetFileName(comm);
    }
}

public static class ProcessTreeResourceAggregator
{
    public static RunnerResourceUsage Aggregate(IEnumerable<ProcessResourceInfo> processList, string runnerDirectory)
    {
        var processes = processList.ToList();
        var processByPid = processes
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.First());
        var childrenByParentPid = processes
            .GroupBy(process => process.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.Select(process => process.ProcessId).ToList());
        var root = FindRunnerRoot(processes, processByPid, runnerDirectory);

        if (root == null)
            return RunnerResourceUsage.Zero;

        var treeProcesses = CollectProcessTree(root.ProcessId, processByPid, childrenByParentPid);
        var hasWorker = treeProcesses.Any(IsWorkerProcess);

        return new RunnerResourceUsage
        {
            IsRunning = true,
            IsJobActive = hasWorker,
            ParentProcessId = root.ProcessId,
            TotalCpuPercent = treeProcesses.Sum(process => process.CpuPercent),
            TotalMemoryBytes = treeProcesses.Sum(process => process.MemoryBytes),
            ProcessCount = treeProcesses.Count,
            Processes = treeProcesses,
            Timestamp = DateTime.Now
        };
    }

    private static ProcessResourceInfo? FindRunnerRoot(
        IReadOnlyCollection<ProcessResourceInfo> processes,
        IReadOnlyDictionary<int, ProcessResourceInfo> processByPid,
        string runnerDirectory)
    {
        var candidates = processes
            .Where(process => IsRunnerRootCandidate(process, runnerDirectory))
            .ToList();

        var listener = candidates
            .Where(IsListenerProcess)
            .OrderBy(process => HasRunnerDirectory(process, runnerDirectory) ? 0 : 1)
            .ThenBy(process => process.ProcessId)
            .FirstOrDefault();

        if (listener != null)
            return listener;

        return candidates
            .Where(process => !processByPid.TryGetValue(process.ParentProcessId, out var parent) || !candidates.Any(candidate => candidate.ProcessId == parent.ProcessId))
            .OrderBy(process => process.ProcessId)
            .FirstOrDefault()
            ?? candidates.OrderBy(process => process.ProcessId).FirstOrDefault();
    }

    private static List<ProcessResourceInfo> CollectProcessTree(
        int rootPid,
        IReadOnlyDictionary<int, ProcessResourceInfo> processByPid,
        IReadOnlyDictionary<int, List<int>> childrenByParentPid)
    {
        var result = new List<ProcessResourceInfo>();
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(rootPid);

        while (stack.Count > 0)
        {
            var pid = stack.Pop();
            if (!seen.Add(pid) || !processByPid.TryGetValue(pid, out var process))
                continue;

            result.Add(process);

            if (!childrenByParentPid.TryGetValue(pid, out var children))
                continue;

            foreach (var childPid in children)
                stack.Push(childPid);
        }

        return result;
    }

    private static bool IsRunnerRootCandidate(ProcessResourceInfo process, string runnerDirectory)
    {
        if (IsListenerProcess(process) || IsWorkerProcess(process) || string.Equals(process.Name, "RunnerService", StringComparison.OrdinalIgnoreCase))
            return true;

        var commandLine = process.CommandLine ?? string.Empty;
        return HasRunnerDirectory(process, runnerDirectory)
            && (commandLine.Contains("run.sh", StringComparison.OrdinalIgnoreCase)
                || commandLine.Contains("run.cmd", StringComparison.OrdinalIgnoreCase)
                || commandLine.Contains("run.bat", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsListenerProcess(ProcessResourceInfo process)
    {
        var commandLine = process.CommandLine ?? string.Empty;
        return string.Equals(process.Name, "Runner.Listener", StringComparison.OrdinalIgnoreCase)
            || string.Equals(process.Name, "Runner.Listener.exe", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("Runner.Listener", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorkerProcess(ProcessResourceInfo process)
    {
        var commandLine = process.CommandLine ?? string.Empty;
        return string.Equals(process.Name, "Runner.Worker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(process.Name, "Runner.Worker.exe", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("Runner.Worker", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRunnerDirectory(ProcessResourceInfo process, string runnerDirectory)
    {
        if (string.IsNullOrWhiteSpace(runnerDirectory))
            return false;

        return (process.CommandLine ?? string.Empty).Contains(runnerDirectory, StringComparison.Ordinal);
    }
}

public class ResourceMonitorFactory : IResourceMonitorFactory
{
    public IResourceMonitor Create(DirectoryInfo runnerDirectory)
    {
        return new ResourceMonitor(runnerDirectory);
    }
}
