using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;
using GitRunnerManager.Platform.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class RunnerSetupWizardTests
{
    [Fact]
    public void CreateDefaultRunnerName_IncludesMachineOsAndArchitecture()
    {
        var name = RunnerSetupValidator.CreateDefaultRunnerName();

        Assert.Contains(Environment.MachineName, name);
        Assert.Contains("-", name);
    }

    [Fact]
    public void NormalizeLabels_RemovesEmptyAndDuplicateLabels()
    {
        var labels = RunnerSetupValidator.NormalizeLabels(["self-hosted", "", " Linux ", "linux"]);

        Assert.Equal(["self-hosted", "Linux"], labels);
    }

    [Fact]
    public void ValidateDraft_RequiresSignedInAccountRepositoryAndName()
    {
        var result = RunnerSetupValidator.ValidateDraft(new RunnerSetupDraft
        {
            Scope = GitHubRunnerScope.Repository,
            AccountLogin = "",
            OwnerOrOrg = "",
            RunnerName = "",
            RunnerDirectory = "",
            Labels = []
        }, []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Sign in", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Runner name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateDraft_WarnsWhenRunnerNameAlreadyExists()
    {
        var result = RunnerSetupValidator.ValidateDraft(new RunnerSetupDraft
        {
            Scope = GitHubRunnerScope.Repository,
            AccountLogin = "octo",
            OwnerOrOrg = "octo",
            SelectedRepositories = [new GitHubRepositoryInfo { Owner = "octo", Name = "repo", FullName = "octo/repo" }],
            RunnerName = "Local runner",
            RunnerDirectory = "/tmp/runner",
            Labels = ["self-hosted"]
        }, [new RunnerConfig { DisplayName = "local RUNNER", RunnerDirectory = "/tmp/existing" }]);

        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void ValidateDraft_AllowsMultipleRepositoriesWhenCreatingNewFolders()
    {
        var result = RunnerSetupValidator.ValidateDraft(new RunnerSetupDraft
        {
            Scope = GitHubRunnerScope.Repository,
            FolderSetupMode = RunnerFolderSetupMode.CreateNew,
            AccountLogin = "octo",
            OwnerOrOrg = "octo",
            SelectedRepositories =
            [
                new GitHubRepositoryInfo { Owner = "octo", Name = "one", FullName = "octo/one" },
                new GitHubRepositoryInfo { Owner = "octo", Name = "two", FullName = "octo/two" }
            ],
            RunnerName = "Local runner",
            RunnerDirectory = "/tmp/runner",
            Labels = ["self-hosted"]
        }, []);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateDraft_BlocksMultipleRepositoriesForExistingFolderImport()
    {
        var result = RunnerSetupValidator.ValidateDraft(new RunnerSetupDraft
        {
            Scope = GitHubRunnerScope.Repository,
            FolderSetupMode = RunnerFolderSetupMode.ImportExisting,
            AccountLogin = "octo",
            OwnerOrOrg = "octo",
            SelectedRepositories =
            [
                new GitHubRepositoryInfo { Owner = "octo", Name = "one", FullName = "octo/one" },
                new GitHubRepositoryInfo { Owner = "octo", Name = "two", FullName = "octo/two" }
            ],
            RunnerName = "Local runner",
            RunnerDirectory = "/tmp/runner",
            Labels = ["self-hosted"]
        }, []);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GitHubPermissionEvaluator_ReportsMissingOrganizationScope()
    {
        var result = GitHubPermissionEvaluator.Evaluate(true, ["repo", "read:org"]);

        Assert.True(result.HasRepoScope);
        Assert.False(result.HasAdminOrgScope);
        Assert.Equal(["admin:org"], result.MissingOrganizationRunnerScopes);
        Assert.Empty(result.MissingRepositoryRunnerScopes);
    }

    [Fact]
    public void FolderSetupValidation_CreateNewAcceptsWritableEmptyFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "grm-runner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var result = new RunnerFolderValidator().ValidateSetupFolder(folder, RunnerFolderSetupMode.CreateNew);

            Assert.True(result.IsValid);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void FolderSetupValidation_ImportRequiresRunnerMarkers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "grm-runner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var result = new RunnerFolderValidator().ValidateSetupFolder(folder, RunnerFolderSetupMode.ImportExisting);

            Assert.False(result.IsValid);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
