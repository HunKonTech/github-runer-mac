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
        if (IsRunnerRunning())
            return Task.CompletedTask;

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

        return Task.CompletedTask;
    }

    public Task StopAsync()
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

        return Task.CompletedTask;
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

    private bool MatchesRunnerProcess(Process proc)
    {
        try
        {
            var cmd = proc.ProcessName;
            var path = _runnerDirectory.FullName;
            var commandLine = GetCommandLine(proc);
            if (string.IsNullOrWhiteSpace(commandLine)
                || !commandLine.Contains(path, StringComparison.OrdinalIgnoreCase))
                return false;

            if (cmd == "run" || cmd == "run.sh" || cmd == "run.cmd" || cmd == "run.bat")
                return true;

            if (cmd == "Runner.Listener" || cmd == "Runner.Worker")
                return true;

            if (cmd == "run-helper" || cmd == "RunnerService")
                return true;

            return false;
        }
        catch
        {
            return false;
        }
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
