using System.Runtime.InteropServices;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Core.Services;

public static class RunnerSetupValidator
{
    public static string CreateDefaultRunnerName()
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "unknown";
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var machine = string.IsNullOrWhiteSpace(Environment.MachineName) ? "runner" : Environment.MachineName.Trim();
        return $"{machine}-{os}-{arch}";
    }

    public static List<string> SuggestedLabels()
    {
        var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Unknown";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "X64",
            Architecture.Arm64 => "ARM64",
            Architecture.X86 => "X86",
            _ => RuntimeInformation.ProcessArchitecture.ToString()
        };
        return NormalizeLabels(["self-hosted", os, arch]);
    }

    public static List<string> NormalizeLabels(IEnumerable<string> labels)
    {
        return labels
            .Select(label => label.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static RunnerSetupValidationResult ValidateDraft(RunnerSetupDraft draft, IReadOnlyList<RunnerConfig> knownProfiles)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(draft.AccountLogin))
            errors.Add("Sign in to GitHub first.");

        if (string.IsNullOrWhiteSpace(draft.OwnerOrOrg))
            errors.Add(draft.Scope == GitHubRunnerScope.Organization ? "Choose an organization." : "The GitHub owner is missing.");

        if (draft.RepositoryAccessMode == RunnerRepositoryAccessMode.SelectedRepositories && draft.SelectedRepositories.Count == 0)
            errors.Add("Choose at least one repository.");

        if (draft.Scope == GitHubRunnerScope.Repository && draft.SelectedRepositories.Count == 0)
            errors.Add("Choose at least one repository for repository runners.");

        if (draft.Scope == GitHubRunnerScope.Organization && draft.RepositoryAccessMode == RunnerRepositoryAccessMode.SelectedRepositories && draft.SelectedRepositories.Count == 0)
            errors.Add("Choose at least one repository.");

        if (draft.FolderSetupMode == RunnerFolderSetupMode.ImportExisting && draft.SelectedRepositories.Count > 1)
            errors.Add("Importing an existing runner folder supports one repository at a time.");

        if (string.IsNullOrWhiteSpace(draft.RunnerName))
            errors.Add("Runner name is required.");

        if (string.IsNullOrWhiteSpace(draft.RunnerDirectory))
            errors.Add("Runner folder is required.");

        var labels = NormalizeLabels(draft.Labels);
        if (labels.Count == 0)
            errors.Add("Add at least one runner label.");

        if (knownProfiles.Any(profile => string.Equals(profile.DisplayName, draft.RunnerName.Trim(), StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Another local runner profile already uses this name.");

        return new RunnerSetupValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}
