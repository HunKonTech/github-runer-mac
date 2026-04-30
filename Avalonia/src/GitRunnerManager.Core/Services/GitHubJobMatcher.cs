using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Core.Services;

public static class GitHubJobMatcher
{
    public static bool IsRunningOnLocalRunner(GitHubWorkflowJobInfo job, IEnumerable<RunnerConfig> runners)
    {
        if (string.IsNullOrWhiteSpace(job.RunnerName))
            return false;

        return runners.Any(runner => Matches(job.RunnerName, runner));
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
