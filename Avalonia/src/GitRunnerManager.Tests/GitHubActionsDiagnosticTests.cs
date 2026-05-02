using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;
using GitRunnerManager.Platform.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class GitHubActionsDiagnosticTests
{
    [Fact]
    public void ApiMapper_MapsRunJobAndStepFields()
    {
        var run = GitHubActionsApiClient.MapRun("octo/repo", new RunResponse
        {
            Id = 123,
            RunNumber = 45,
            Name = "CI",
            Status = "in_progress",
            Conclusion = null,
            CreatedAt = DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            RunStartedAt = DateTimeOffset.Parse("2026-05-02T10:01:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-05-02T10:02:00Z"),
            HtmlUrl = "https://github.com/octo/repo/actions/runs/123",
            JobsUrl = "https://api.github.com/repos/octo/repo/actions/runs/123/jobs"
        });
        var job = GitHubActionsApiClient.MapJob(new JobResponse
        {
            Id = 99,
            Name = "build",
            Status = "completed",
            Conclusion = "failure",
            RunnerName = "Local runner",
            RunnerGroupName = "Default",
            Labels = ["self-hosted", "macOS"],
            Steps =
            [
                new StepResponse { Number = 1, Name = "Checkout", Status = "completed", Conclusion = "success" }
            ]
        });

        Assert.Equal(45, run.RunNumber);
        Assert.Equal("octo/repo", run.RepositoryFullName);
        Assert.Equal("Local runner", job.RunnerName);
        Assert.Equal("Default", job.RunnerGroupName);
        Assert.Equal("self-hosted", job.Labels[0]);
        Assert.Equal("Checkout", job.Steps[0].Name);
    }

    [Fact]
    public void Match_WithRunnerName_ReturnsExact()
    {
        var job = new GitHubWorkflowJobInfo { Name = "build", RunnerName = "Local runner" };
        var runners = new[] { new RunnerConfig { DisplayName = "Local runner" } };

        var result = GitHubJobMatcher.Match(job, runners, null, RunnerResourceUsage.Zero);

        Assert.Equal(GitHubCorrelationConfidence.Exact, result.Confidence);
    }

    [Fact]
    public void Match_WithLogJobName_ReturnsProbable()
    {
        var job = new GitHubWorkflowJobInfo { Name = "Build Avalonia macOS App", Status = "in_progress" };
        var activity = new RunnerActivitySnapshot
        {
            Kind = RunnerActivityKind.Busy,
            Description = "Working",
            CurrentJobName = "Build Avalonia macOS App"
        };

        var result = GitHubJobMatcher.Match(job, [], activity, RunnerResourceUsage.Zero);

        Assert.Equal(GitHubCorrelationConfidence.Probable, result.Confidence);
    }

    [Fact]
    public void Match_WithOnlyBusyProcessAndTiming_ReturnsPossible()
    {
        var job = new GitHubWorkflowJobInfo
        {
            Name = "test",
            Status = "in_progress",
            StartedAt = DateTimeOffset.Now.AddMinutes(-2)
        };
        var usage = new RunnerResourceUsage { IsRunning = true, IsJobActive = true };

        var result = GitHubJobMatcher.Match(job, [], null, usage);

        Assert.Equal(GitHubCorrelationConfidence.Possible, result.Confidence);
    }

    [Fact]
    public void MarkdownPrompt_IncludesRunJobStepsAndPermissions()
    {
        var context = new GitHubActionsDiagnosticContext
        {
            Account = new GitHubAccountInfo { IsSignedIn = true, Login = "octo" },
            Run = new GitHubWorkflowRunInfo
            {
                Id = 123,
                RunNumber = 45,
                RepositoryFullName = "octo/repo",
                WorkflowName = "CI",
                Status = "completed",
                Conclusion = "failure",
                HtmlUrl = "https://github.com/octo/repo/actions/runs/123"
            },
            Jobs =
            [
                new GitHubWorkflowJobInfo
                {
                    Id = 99,
                    Name = "build",
                    Status = "completed",
                    Conclusion = "failure",
                    RunnerName = "Local runner",
                    CorrelationConfidence = GitHubCorrelationConfidence.Exact,
                    Steps =
                    [
                        new GitHubWorkflowStepInfo { Number = 1, Name = "Checkout", Status = "completed", Conclusion = "success" },
                        new GitHubWorkflowStepInfo { Number = 2, Name = "Build", Status = "completed", Conclusion = "failure" }
                    ]
                }
            ],
            PermissionStatus = new GitHubApiPermissionStatus
            {
                HasWorkflowAccess = true,
                HasOrganizationRunnerAccess = false,
                Message = "Organization runner details require admin permission."
            }
        };

        var markdown = GitHubActionsDiagnosticExporter.ToMarkdownPrompt(context);

        Assert.Contains("Analyze this GitHub Actions run/job failure", markdown);
        Assert.Contains("octo/repo", markdown);
        Assert.Contains("Run number: 45", markdown);
        Assert.Contains("Build: completed / failure", markdown);
        Assert.Contains("Organization runner details require admin permission", markdown);
    }

    [Fact]
    public void JsonExport_IncludesCorrelationConfidence()
    {
        var context = new GitHubActionsDiagnosticContext
        {
            CorrelationConfidence = GitHubCorrelationConfidence.Possible
        };

        var json = GitHubActionsDiagnosticExporter.ToJson(context);

        Assert.Contains("\"CorrelationConfidence\": \"Possible\"", json);
    }

    [Fact]
    public void ReadRelevantRunnerLogLines_ReturnsRecentJobLines()
    {
        var tempDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var diag = Directory.CreateDirectory(Path.Combine(tempDir.FullName, "_diag"));
            File.WriteAllText(Path.Combine(diag.FullName, "Runner_2026.log"), string.Join('\n',
                "noise",
                "Listening for Jobs",
                "Running job: Build",
                "another line",
                "Job completed with result: Failed"));

            var lines = GitHubActionsDiagnosticExporter.ReadRelevantRunnerLogLines(tempDir.FullName);

            Assert.Contains(lines, line => line.Contains("Running job: Build"));
            Assert.DoesNotContain(lines, line => line == "noise");
        }
        finally
        {
            if (tempDir.Exists)
                tempDir.Delete(true);
        }
    }
}
