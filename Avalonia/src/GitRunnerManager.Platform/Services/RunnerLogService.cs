using System.Text;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Platform.Services;

public class RunnerLogService : IRunnerLogService
{
    private const int HeaderBytes = 12 * 1024;

    public RunnerLogSnapshot ReadLog(RunnerConfig profile, bool preferActiveLog, int maxBytes = 96 * 1024)
    {
        var log = SelectLogFile(profile.RunnerDirectory, preferActiveLog);
        if (log == null)
            return new RunnerLogSnapshot();

        var max = Math.Max(8 * 1024, maxBytes);
        return new RunnerLogSnapshot
        {
            FilePath = log.FullName,
            Content = ReadBounded(log.FullName, max),
            IsTruncated = log.Length > max,
            FileSizeBytes = log.Length,
            LastWriteTime = log.LastWriteTime
        };
    }

    public string? GetLogDirectory(RunnerConfig profile)
    {
        if (string.IsNullOrWhiteSpace(profile.RunnerDirectory))
            return null;

        var path = Path.Combine(profile.RunnerDirectory, "_diag");
        return Directory.Exists(path) ? path : null;
    }

    public void OpenLogDirectory(RunnerConfig profile)
    {
        var directory = GetLogDirectory(profile);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    private static FileInfo? SelectLogFile(string runnerDirectory, bool preferActiveLog)
    {
        if (string.IsNullOrWhiteSpace(runnerDirectory))
            return null;

        var diagPath = Path.Combine(runnerDirectory, "_diag");
        if (!Directory.Exists(diagPath))
            return null;

        var logs = new DirectoryInfo(diagPath)
            .GetFiles("Runner_*.log")
            .OrderByDescending(file => preferActiveLog ? file.LastWriteTimeUtc : file.CreationTimeUtc)
            .ThenByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        return logs.FirstOrDefault();
    }

    private static string ReadBounded(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= maxBytes)
            return ReadSegment(stream, 0, (int)stream.Length);

        var headerLength = Math.Min(HeaderBytes, maxBytes / 3);
        var tailLength = maxBytes - headerLength;
        var header = ReadSegment(stream, 0, headerLength);
        var tailStart = Math.Max(headerLength, stream.Length - tailLength);
        var tail = ReadSegment(stream, tailStart, tailLength);
        return header.TrimEnd() + Environment.NewLine + Environment.NewLine + "[...]" + Environment.NewLine + Environment.NewLine + tail.TrimStart();
    }

    private static string ReadSegment(FileStream stream, long offset, int length)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[length];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
