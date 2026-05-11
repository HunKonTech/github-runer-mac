using System.Diagnostics;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.Platform.Services;

public class RunnerController : IRunnerController
{
    private readonly DirectoryInfo _runnerDirectory;
    private readonly IRunnerLogParser _logParser;
    private Process? _managedProcess;
    private bool _disposed;

    public RunnerController(DirectoryInfo runnerDirectory, IRunnerLogParser logParser)
    {
        _runnerDirectory = runnerDirectory;
        _logParser = logParser;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _managedProcess?.Dispose();
    }

    public RunnerSnapshot GetCurrentSnapshot()
    {
        var isRunning = IsRunnerRunning();
        var activity = isRunning ? _logParser.GetLatestActivity(_runnerDirectory) : RunnerActivitySnapshot.Stopped;

        return new RunnerSnapshot
        {
            IsRunning = isRunning,
            Activity = activity
        };
    }

    public Task StartAsync()
    {
        return Task.Run(StartCore);
    }

    private void StartCore()
    {
        if (IsRunnerRunning())
            return;

        var runScript = GetRunScript();
        if (runScript == null || !File.Exists(runScript))
            throw new InvalidOperationException($"Cannot find the runner startup script: {runScript}");

        var startInfo = CreateProcessStartInfo(runScript);

        _managedProcess = Process.Start(startInfo);
        var process = _managedProcess;
        if (process != null)
        {
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data) && e.Data.Contains("error", StringComparison.OrdinalIgnoreCase))
                    DiagnosticLog.Write($"Runner stderr: {e.Data}");
            };
            process.Exited += (_, _) =>
            {
                try
                {
                    DiagnosticLog.Write($"Runner process exited with code {process.ExitCode}");
                }
                catch
                {
                    DiagnosticLog.Write("Runner process exited");
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
    }

    public Task StopAsync()
    {
        return Task.Run(StopCore);
    }

    private void StopCore()
    {
        if (_managedProcess != null && !_managedProcess.HasExited)
        {
            try
            {
                _managedProcess.Kill(entireProcessTree: true);
            }
            catch { }
        }

        var pids = GetRunnerProcessIds();
        foreach (var pid in pids)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }

    private string? GetRunScript()
    {
        var sh = Path.Combine(_runnerDirectory.FullName, "run.sh");
        if (File.Exists(sh))
            return sh;

        var cmd = Path.Combine(_runnerDirectory.FullName, "run.cmd");
        if (File.Exists(cmd))
            return cmd;

        var bat = Path.Combine(_runnerDirectory.FullName, "run.bat");
        if (File.Exists(bat))
            return bat;

        return null;
    }

    private ProcessStartInfo CreateProcessStartInfo(string script)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = _runnerDirectory.FullName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var env = startInfo.Environment;
        env["RUNNER_MANUALLY_TRAP_SIG"] = "1";

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = script;
            startInfo.Arguments = "";
        }
        else
        {
            startInfo.FileName = "/bin/bash";
            startInfo.Arguments = $"\"{script}\"";
        }

        return startInfo;
    }

    private bool IsRunnerRunning()
    {
        if (_managedProcess != null && !_managedProcess.HasExited)
            return true;

        return GetRunnerProcessIds().Count > 0;
    }

    private List<int> GetRunnerProcessIds()
    {
        if (!OperatingSystem.IsWindows())
            return GetUnixRunnerProcessIds();

        var pids = new List<int>();

        try
        {
            var processes = Process.GetProcesses();

            foreach (var proc in processes)
            {
                try
                {
                    if (MatchesRunnerProcess(proc))
                        pids.Add(proc.Id);
                }
                catch
                {
                    // Skip processes we can't access
                }
            }
        }
        catch
        {
            // Fallback
        }

        return pids;
    }

    private List<int> GetUnixRunnerProcessIds()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/ps",
                Arguments = "-axo pid=,comm=,args=",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return [];

            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                return [];
            }

            var output = outputTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                return [];

            return ParseUnixRunnerProcessIds(output, _runnerDirectory.FullName);
        }
        catch
        {
            return [];
        }
    }

    internal static List<int> ParseUnixRunnerProcessIds(string output, string runnerDirectory)
    {
        var pids = new List<int>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3)
                continue;

            if (!int.TryParse(fields[0], out var pid))
                continue;

            if (MatchesRunnerProcess(fields[1], fields[2], runnerDirectory))
                pids.Add(pid);
        }

        return pids;
    }

    private bool MatchesRunnerProcess(Process proc)
    {
        try
        {
            var commandLine = GetCommandLine(proc);
            return MatchesRunnerProcess(proc.ProcessName, commandLine, _runnerDirectory.FullName);
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesRunnerProcess(string processName, string? commandLine, string runnerDirectory)
    {
        if (string.IsNullOrWhiteSpace(commandLine)
            || !commandLine.Contains(runnerDirectory, StringComparison.OrdinalIgnoreCase))
            return false;

        var executableName = Path.GetFileName(processName);
        if (string.Equals(executableName, "run", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "run.sh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "run.cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "run.bat", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(executableName, "Runner.Listener", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "Runner.Listener.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "Runner.Worker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "Runner.Worker.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "run-helper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "RunnerService", StringComparison.OrdinalIgnoreCase))
            return true;

        return commandLine.Contains("run.sh", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("run.cmd", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("run.bat", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("Runner.Listener", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("Runner.Worker", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetCommandLine(Process proc)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return proc.MainModule?.FileName;

            using var ps = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/ps",
                Arguments = $"-p {proc.Id} -o command=",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return ps?.StandardOutput.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}

public class RunnerControllerFactory : IRunnerControllerFactory
{
    private readonly IRunnerLogParser _logParser;

    public RunnerControllerFactory(IRunnerLogParser logParser)
    {
        _logParser = logParser;
    }

    public IRunnerController Create(DirectoryInfo runnerDirectory)
    {
        return new RunnerController(runnerDirectory, _logParser);
    }
}
