using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Core.Services;

public static class GitHubJobMatcher
{
    public static bool IsRunningOnLocalRunner(GitHubWorkflowJobInfo job, IEnumerable<RunnerConfig> runners)
    {
        return Match(job, runners, null, RunnerResourceUsage.Zero).Confidence != GitHubCorrelationConfidence.Unknown;
    }

    public static GitHubJobCorrelation Match(
        GitHubWorkflowJobInfo job,
        IEnumerable<RunnerConfig> runners,
        RunnerActivitySnapshot? activity,
        RunnerResourceUsage resourceUsage,
        DateTimeOffset? now = null)
    {
        var runnerList = runners.ToList();
        if (!string.IsNullOrWhiteSpace(job.RunnerName) && runnerList.Any(runner => Matches(job.RunnerName, runner)))
            return new GitHubJobCorrelation(GitHubCorrelationConfidence.Exact, "GitHub job runner_name matches a configured local runner name.");

        var localJobName = activity?.CurrentJobName;
        if (!string.IsNullOrWhiteSpace(localJobName) && NamesMatch(localJobName, job.Name))
            return new GitHubJobCorrelation(GitHubCorrelationConfidence.Probable, "Local runner log job name matches the GitHub job name.");

        var isActiveJob = job.Status is "in_progress" or "queued" or "waiting" or "requested";
        var localRunnerBusy = resourceUsage.IsJobActive || activity?.Kind == RunnerActivityKind.Busy;
        if (isActiveJob && localRunnerBusy && TimeOverlaps(job, now ?? DateTimeOffset.Now))
            return new GitHubJobCorrelation(GitHubCorrelationConfidence.Possible, "Local runner is busy and job timing overlaps, but GitHub did not expose a matching runner_name.");

        return new GitHubJobCorrelation(GitHubCorrelationConfidence.Unknown, "No reliable local runner correlation was found.");
    }

    public static bool Matches(string runnerName, RunnerConfig runner)
    {
        if (string.IsNullOrWhiteSpace(runnerName))
            return false;

        var candidates = new[]
        {
            runner.DisplayName,
            Directory.Exists(runner.RunnerDirectory) ? ReadRunnerName(runner.RunnerDirectory) : null,
            Environment.MachineName
        };

        return candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => string.Equals(value!.Trim(), runnerName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool NamesMatch(string left, string right)
    {
        return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase) ||
            NormalizeName(left).Contains(NormalizeName(right), StringComparison.OrdinalIgnoreCase) ||
            NormalizeName(right).Contains(NormalizeName(left), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value)
    {
        return value.Trim().Replace(" / ", "/").Replace(" ", "");
    }

    private static bool TimeOverlaps(GitHubWorkflowJobInfo job, DateTimeOffset now)
    {
        if (job.StartedAt == null)
            return true;

        var completedAt = job.CompletedAt ?? now;
        return completedAt >= now.AddMinutes(-30) && job.StartedAt <= now.AddMinutes(5);
    }

    private static string? ReadRunnerName(string runnerDirectory)
    {
        var file = Path.Combine(runnerDirectory, ".runner");
        if (!File.Exists(file))
            return null;

        var text = File.ReadAllText(file);
        const string marker = "\"runner_name\"";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var colon = text.IndexOf(':', index);
        var firstQuote = colon >= 0 ? text.IndexOf('"', colon) : -1;
        var secondQuote = firstQuote >= 0 ? text.IndexOf('"', firstQuote + 1) : -1;
        return firstQuote >= 0 && secondQuote > firstQuote ? text[(firstQuote + 1)..secondQuote] : null;
    }
}

public readonly record struct GitHubJobCorrelation(GitHubCorrelationConfidence Confidence, string Reason);
