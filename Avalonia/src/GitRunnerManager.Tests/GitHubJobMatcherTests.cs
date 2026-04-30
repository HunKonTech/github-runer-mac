using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class GitHubJobMatcherTests
{
    [Fact]
    public void MatchesDisplayNameCaseInsensitively()
    {
        var runner = new RunnerConfig { DisplayName = "Mac Mini Runner" };

        Assert.True(GitHubJobMatcher.Matches("mac mini runner", runner));
    }

    [Fact]
    public void DoesNotMatchDifferentRunnerName()
    {
        var runner = new RunnerConfig { DisplayName = "Local runner" };

        Assert.False(GitHubJobMatcher.Matches("hosted-runner", runner));
    }

    [Fact]
    public void MatchesWorkflowJobToConfiguredRunner()
    {
        var job = new GitHubWorkflowJobInfo { RunnerName = "Local runner" };
        var runners = new[] { new RunnerConfig { DisplayName = "Local runner" } };

        Assert.True(GitHubJobMatcher.IsRunningOnLocalRunner(job, runners));
    }
}
