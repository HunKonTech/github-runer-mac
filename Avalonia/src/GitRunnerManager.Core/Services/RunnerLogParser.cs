using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Localization;

namespace GitRunnerManager.Core.Services;

public class RunnerLogParser : IRunnerLogParser
{
    private readonly ILocalizationService _localization;

    public RunnerLogParser(ILocalizationService localization)
    {
        _localization = localization;
    }

    public RunnerActivitySnapshot GetLatestActivity(DirectoryInfo runnerDirectory)
    {
        try
        {
            var diagPath = Path.Combine(runnerDirectory.FullName, "_diag");
            if (!Directory.Exists(diagPath))
                return RunnerActivitySnapshot.Unknown;

            var latestLog = GetLatestLogFile(diagPath);
            if (latestLog == null)
                return RunnerActivitySnapshot.Unknown;

            var content = File.ReadAllText(latestLog.FullName);
            return ParseActivity(content);
        }
        catch
        {
            return RunnerActivitySnapshot.Unknown;
        }
    }

    private FileInfo? GetLatestLogFile(string diagPath)
    {
        var logFiles = new DirectoryInfo(diagPath)
            .GetFiles("Runner_*.log")
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        return logFiles.FirstOrDefault();
    }

    private RunnerActivitySnapshot ParseActivity(string content)
    {
        var latestActivity = RunnerActivitySnapshot.Unknown;
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            var jobName = ExtractJobName(trimmed);
            if (jobName != null)
            {
                latestActivity = new RunnerActivitySnapshot
                {
                    Kind = RunnerActivityKind.Busy,
                    Description = _localization.Get(LocalizationKeys.ActivityWorkingJob, jobName),
                    CurrentJobName = jobName
                };
                continue;
            }

            if (trimmed.Contains("Listening for Jobs"))
            {
                latestActivity = new RunnerActivitySnapshot
                {
                    Kind = RunnerActivityKind.Waiting,
                    Description = _localization.Get(LocalizationKeys.ActivityWaitingForJob)
                };
                continue;
            }

            if (trimmed.Contains("completed with result:"))
            {
                latestActivity = new RunnerActivitySnapshot
                {
                    Kind = RunnerActivityKind.Waiting,
                    Description = _localization.Get(LocalizationKeys.ActivityWaitingForJob)
                };
                continue;
            }

            if (trimmed.Contains("Exiting..."))
            {
                latestActivity = new RunnerActivitySnapshot
                {
                    Kind = RunnerActivityKind.Unknown,
                    Description = _localization.Get(LocalizationKeys.ActivityStopping)
                };
            }
        }

        return latestActivity;
    }

    private static string? ExtractJobName(string line)
    {
        const string marker = "Running job: ";
        var startIndex = line.IndexOf(marker, StringComparison.Ordinal);
        if (startIndex < 0)
            return null;

        startIndex += marker.Length;
        var endIndex = line.IndexOfAny(['\r', '\n'], startIndex);
        var jobName = endIndex > startIndex ? line[startIndex..endIndex] : line[startIndex..];

        return string.IsNullOrWhiteSpace(jobName) ? null : jobName.Trim();
    }
}
