namespace GitRunnerManager.Core.Models;

public enum RunnerControlMode
{
    Automatic,
    ForceRunning,
    ForceStopped
}

public class RunnerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "Local runner";
    public string RunnerDirectory { get; set; } = string.Empty;
    public string GitHubOwnerOrOrg { get; set; } = string.Empty;
    public string? RepositoryName { get; set; }
    public bool IsOrganizationRunner { get; set; }
    public List<string> Labels { get; set; } = [];
    public bool AutoStartEnabled { get; set; } = true;
    public bool AutomaticModeEnabled { get; set; } = true;
    public bool StopOnBattery { get; set; } = PreferenceDefaults.StopRunnerOnBattery;
    public bool StopOnMeteredNetwork { get; set; } = PreferenceDefaults.StopRunnerOnMeteredNetwork;
    public bool? UpdateAutomatically { get; set; }
    public bool IsEnabled { get; set; } = true;

    public RunnerConfig Clone()
    {
        return new RunnerConfig
        {
            Id = Id,
            DisplayName = DisplayName,
            RunnerDirectory = RunnerDirectory,
            GitHubOwnerOrOrg = GitHubOwnerOrOrg,
            RepositoryName = RepositoryName,
            IsOrganizationRunner = IsOrganizationRunner,
            Labels = [..Labels],
            AutoStartEnabled = AutoStartEnabled,
            AutomaticModeEnabled = AutomaticModeEnabled,
            StopOnBattery = StopOnBattery,
            StopOnMeteredNetwork = StopOnMeteredNetwork,
            UpdateAutomatically = UpdateAutomatically,
            IsEnabled = IsEnabled
        };
    }
}

public class RunnerFolderValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Markers { get; init; } = [];
}

public class RunnerLogSnapshot
{
    public string? FilePath { get; init; }
    public string Content { get; init; } = string.Empty;
    public bool Exists => !string.IsNullOrWhiteSpace(FilePath);
    public bool IsTruncated { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTimeOffset? LastWriteTime { get; init; }
}

public enum GitHubRunnerScope
{
    Repository,
    Organization
}

public class GitHubAccountSnapshot
{
    public bool IsSignedIn { get; init; }
    public string? Login { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> OAuthScopes { get; init; } = [];
}

public class GitHubAccountInfo
{
    public bool IsSignedIn { get; init; }
    public string? Login { get; init; }
    public string? Name { get; init; }
    public string? AvatarUrl { get; init; }
    public string? HtmlUrl { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> OAuthScopes { get; init; } = [];

    public GitHubAccountSnapshot ToSnapshot() => new()
    {
        IsSignedIn = IsSignedIn,
        Login = Login,
        Error = Error,
        OAuthScopes = OAuthScopes
    };
}

public enum GitHubAccountConnectionKind
{
    Personal,
    Organization
}

public class GitHubAccountConnection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public GitHubAccountConnectionKind Kind { get; init; } = GitHubAccountConnectionKind.Personal;
    public string Login { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string DisplayName => Kind == GitHubAccountConnectionKind.Organization && !string.IsNullOrWhiteSpace(Organization)
        ? $"{Login} · {Organization}"
        : Login;
}

public class GitHubStoredAccount : GitHubAccountConnection
{
    public string Token { get; init; } = string.Empty;
}

public enum GitHubOwnerKind
{
    User,
    Organization,
    Repository,
    Unknown
}

public class GitHubOwnerInfo
{
    public string Login { get; init; } = string.Empty;
    public GitHubOwnerKind Kind { get; init; } = GitHubOwnerKind.Unknown;
}

public class GitHubRepositoryInfo
{
    public string Owner { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public bool ActionsEnabled { get; init; } = true;
}

public class GitHubRepositoryReference
{
    public string Owner { get; init; } = string.Empty;
    public string Repo { get; init; } = string.Empty;
    public string FullName => string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Repo)
        ? string.Empty
        : $"{Owner}/{Repo}";
}

public enum RunnerRepositoryAccessMode
{
    AllRepositories,
    SelectedRepositories
}

public enum RunnerFolderSetupMode
{
    CreateNew,
    ImportExisting
}

public class RunnerSetupDraft
{
    public GitHubRunnerScope Scope { get; set; } = GitHubRunnerScope.Repository;
    public RunnerRepositoryAccessMode RepositoryAccessMode { get; set; } = RunnerRepositoryAccessMode.AllRepositories;
    public RunnerFolderSetupMode FolderSetupMode { get; set; } = RunnerFolderSetupMode.CreateNew;
    public string AccountLogin { get; set; } = string.Empty;
    public string OwnerOrOrg { get; set; } = string.Empty;
    public List<GitHubRepositoryInfo> SelectedRepositories { get; set; } = [];
    public string RunnerName { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = [];
    public string RunnerDirectory { get; set; } = string.Empty;
}

public class RunnerSetupValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public class RunnerFolderSetupValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
}

public class GitHubPermissionEvaluation
{
    public bool IsSignedIn { get; init; }
    public bool HasRepoScope { get; init; }
    public bool HasAdminOrgScope { get; init; }
    public bool HasUserOrReadOrgScope { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public IReadOnlyList<string> MissingRepositoryRunnerScopes { get; init; } = [];
    public IReadOnlyList<string> MissingOrganizationRunnerScopes { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}

public class GitHubRunnerGroupInfo
{
    public long? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool AllowsAllRepositories { get; init; }
    public IReadOnlyList<GitHubRepositoryInfo> SelectedRepositories { get; init; } = [];
    public bool PermissionDenied { get; init; }
}

public class GitHubRunnerInfo
{
    public long? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = "unknown";
    public bool Busy { get; init; }
    public bool IsLocalRunnerBusy { get; init; }
    public string LocalActivityDescription { get; init; } = string.Empty;
    public IReadOnlyList<string> Labels { get; init; } = [];
    public GitHubOwnerInfo Owner { get; init; } = new();
    public GitHubRepositoryInfo? Repository { get; init; }
    public GitHubRunnerGroupInfo? Group { get; init; }
    public string PermissionMessage { get; init; } = string.Empty;
}

public class GitHubWorkflowRunInfo
{
    public long Id { get; init; }
    public string RepositoryFullName { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public long RunNumber { get; init; }
    public string Branch { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Conclusion { get; init; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public bool IsRunningOnThisRunner { get; init; }
    public GitHubCorrelationConfidence CorrelationConfidence { get; init; } = GitHubCorrelationConfidence.Unknown;
    public string CorrelationReason { get; init; } = string.Empty;
    public string JobsUrl { get; init; } = string.Empty;

    public TimeSpan? Duration => StartedAt.HasValue && UpdatedAt.HasValue ? UpdatedAt.Value - StartedAt.Value : null;
    public bool IsActive => Status is "queued" or "in_progress" or "waiting" or "requested";
}

public class GitHubWorkflowJobInfo
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Conclusion { get; init; } = string.Empty;
    public string RunnerName { get; init; } = string.Empty;
    public string RunnerGroupName { get; init; } = string.Empty;
    public IReadOnlyList<string> Labels { get; init; } = [];
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string HtmlUrl { get; init; } = string.Empty;
    public IReadOnlyList<GitHubWorkflowStepInfo> Steps { get; init; } = [];
    public bool IsRunningOnThisRunner { get; init; }
    public GitHubCorrelationConfidence CorrelationConfidence { get; init; } = GitHubCorrelationConfidence.Unknown;
    public string CorrelationReason { get; init; } = string.Empty;
}

public class GitHubWorkflowStepInfo
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Conclusion { get; init; } = string.Empty;
    public int Number { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public enum GitHubCorrelationConfidence
{
    Exact,
    Probable,
    Possible,
    Unknown
}

public class GitHubApiPermissionStatus
{
    public bool HasWorkflowAccess { get; init; } = true;
    public bool HasRunnerAdminAccess { get; init; } = true;
    public bool HasRepositoryRunnerAccess { get; init; } = true;
    public bool HasOrganizationRunnerAccess { get; init; } = true;
    public bool IsRateLimited { get; init; }
    public string Message { get; init; } = string.Empty;
    public string TechnicalDetails { get; init; } = string.Empty;
}

public class GitHubDashboardSnapshot
{
    public GitHubAccountInfo Account { get; init; } = new();
    public IReadOnlyList<GitHubRepositoryInfo> Repositories { get; init; } = [];
    public IReadOnlyList<GitHubRunnerInfo> Runners { get; init; } = [];
    public IReadOnlyList<GitHubWorkflowRunInfo> WorkflowRuns { get; init; } = [];
    public GitHubApiPermissionStatus PermissionStatus { get; init; } = new();
    public DateTimeOffset RefreshedAt { get; init; } = DateTimeOffset.Now;
}

public class GitHubActionsDiagnosticContext
{
    public GitHubAccountInfo Account { get; init; } = new();
    public GitHubWorkflowRunInfo? Run { get; init; }
    public IReadOnlyList<GitHubWorkflowJobInfo> Jobs { get; init; } = [];
    public GitHubWorkflowJobInfo? CurrentJob { get; init; }
    public RunnerConfig? LocalRunner { get; init; }
    public RunnerSnapshot LocalRunnerStatus { get; init; } = RunnerSnapshot.Stopped;
    public RunnerResourceUsage ResourceUsage { get; init; } = RunnerResourceUsage.Zero;
    public IReadOnlyList<string> LastRelevantRunnerLogLines { get; init; } = [];
    public GitHubCorrelationConfidence CorrelationConfidence { get; init; } = GitHubCorrelationConfidence.Unknown;
    public string CorrelationReason { get; init; } = string.Empty;
    public GitHubApiPermissionStatus PermissionStatus { get; init; } = new();
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.Now;
}

public class GitHubDeviceFlowStart
{
    public required string DeviceCode { get; init; }
    public required string UserCode { get; init; }
    public required string VerificationUri { get; init; }
    public required string VerificationUriComplete { get; init; }
    public int ExpiresIn { get; init; }
    public int Interval { get; init; }
}

public class GitHubRegistrationToken
{
    public required string Token { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class GitHubRunnerSetupRequest
{
    public GitHubRunnerScope Scope { get; init; }
    public RunnerRepositoryAccessMode RepositoryAccessMode { get; init; } = RunnerRepositoryAccessMode.AllRepositories;
    public RunnerFolderSetupMode FolderSetupMode { get; init; } = RunnerFolderSetupMode.CreateNew;
    public string OwnerOrOrg { get; init; } = string.Empty;
    public string RepositoryName { get; init; } = string.Empty;
    public string RunnerDirectory { get; init; } = string.Empty;
    public string RunnerName { get; init; } = string.Empty;
    public List<string> Labels { get; init; } = [];
    public List<GitHubRepositoryReference> SelectedRepositories { get; init; } = [];

    public string GitHubUrl => Scope == GitHubRunnerScope.Organization
        ? $"https://github.com/{OwnerOrOrg}"
        : $"https://github.com/{OwnerOrOrg}/{RepositoryName}";
}

public class GitHubRunnerSetupResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public RunnerConfig? RunnerProfile { get; init; }
    public IReadOnlyList<RunnerConfig> RunnerProfiles { get; init; } = [];
}

public enum RunnerStatusKind
{
    Starting,
    Stopping,
    Running,
    Waiting,
    Busy,
    Stopped,
    Error
}

public enum NetworkConditionKind
{
    Offline,
    Expensive,
    Unmetered,
    Unknown
}

public enum NetworkDecision
{
    Run,
    Stop,
    Keep
}

public class NetworkConditionSnapshot
{
    public NetworkConditionKind Kind { get; init; }
    public string Description { get; init; } = string.Empty;

    public NetworkDecision AutomaticDecision => Kind switch
    {
        NetworkConditionKind.Unmetered => NetworkDecision.Run,
        NetworkConditionKind.Offline => NetworkDecision.Stop,
        NetworkConditionKind.Expensive => NetworkDecision.Stop,
        NetworkConditionKind.Unknown => NetworkDecision.Keep,
        _ => NetworkDecision.Keep
    };

    public static NetworkConditionSnapshot Unknown => new()
    {
        Kind = NetworkConditionKind.Unknown,
        Description = "Checking network..."
    };

    public static NetworkConditionSnapshot Offline => new()
    {
        Kind = NetworkConditionKind.Offline,
        Description = "No internet connection"
    };
}

public enum RunnerActivityKind
{
    Starting,
    Stopping,
    Busy,
    Waiting,
    Unknown
}

public class RunnerActivitySnapshot
{
    public RunnerActivityKind Kind { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? CurrentJobName { get; init; }

    public static RunnerActivitySnapshot Stopped => new()
    {
        Kind = RunnerActivityKind.Unknown,
        Description = "The runner is stopped"
    };

    public static RunnerActivitySnapshot Unknown => new()
    {
        Kind = RunnerActivityKind.Unknown,
        Description = "Unknown status"
    };
}

public class RunnerSnapshot
{
    public bool IsRunning { get; init; }
    public RunnerActivitySnapshot Activity { get; init; } = RunnerActivitySnapshot.Stopped;

    public static RunnerSnapshot Stopped => new()
    {
        IsRunning = false,
        Activity = RunnerActivitySnapshot.Stopped
    };
}

public class RunnerInstanceSnapshot
{
    public RunnerConfig Profile { get; init; } = new();
    public RunnerSnapshot Runner { get; init; } = RunnerSnapshot.Stopped;
    public RunnerResourceUsage ResourceUsage { get; init; } = RunnerResourceUsage.Zero;
    public string? LastErrorMessage { get; init; }
    public DateTime? LastRefreshTime { get; init; }

    public RunnerStatusKind StatusKind
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LastErrorMessage))
                return RunnerStatusKind.Error;

            if (!Runner.IsRunning)
                return RunnerStatusKind.Stopped;

            return Runner.Activity.Kind switch
            {
                RunnerActivityKind.Starting => RunnerStatusKind.Starting,
                RunnerActivityKind.Stopping => RunnerStatusKind.Stopping,
                RunnerActivityKind.Busy => RunnerStatusKind.Busy,
                RunnerActivityKind.Waiting => RunnerStatusKind.Waiting,
                _ => RunnerStatusKind.Running
            };
        }
    }
}

public class RunnerResourceSnapshot
{
    public int? ParentProcessId { get; init; }
    public double TotalCpuPercent { get; init; }
    public long TotalMemoryBytes { get; init; }
    public int ProcessCount { get; init; }
    public IReadOnlyList<ProcessResourceInfo> Processes { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string? Error { get; init; }
    public string? Warning { get; init; }
}

public class RunnerResourceUsage : RunnerResourceSnapshot
{
    public bool IsRunning { get; init; }
    public bool IsJobActive { get; init; }
    public double CpuPercent => TotalCpuPercent;
    public double MemoryMB => TotalMemoryBytes / 1024.0 / 1024.0;

    public static RunnerResourceUsage Zero => new()
    {
        IsRunning = false,
        IsJobActive = false,
        ParentProcessId = null,
        TotalCpuPercent = 0,
        TotalMemoryBytes = 0,
        ProcessCount = 0,
        Processes = [],
        Timestamp = DateTime.Now
    };
}

public class ProcessResourceInfo
{
    public int ProcessId { get; init; }
    public int ParentProcessId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? CommandLine { get; init; }
    public double CpuPercent { get; init; }
    public long MemoryBytes { get; init; }
}

public class RunnerUpdateCheckResult
{
    public RunnerConfig Profile { get; init; } = new();
    public string? InstalledVersion { get; init; }
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public class RunnerUpdateProgress
{
    public string RunnerId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double? Percent { get; init; }
}

public static class RunnerUpdateDecision
{
    public static bool IsUpdateAvailable(string? installedVersion, string? latestVersion)
    {
        if (string.IsNullOrWhiteSpace(latestVersion))
            return false;

        if (string.IsNullOrWhiteSpace(installedVersion))
            return true;

        return TryParseVersion(latestVersion, out var latest)
            && TryParseVersion(installedVersion, out var installed)
            && latest > installed;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var clean = value.Trim().TrimStart('v');
        return Version.TryParse(clean, out version!);
    }
}

public class BatterySnapshot
{
    public bool IsOnBattery { get; init; }
    public bool IsCharging { get; init; }
    public bool HasBattery { get; init; }

    public bool CanRun => !HasBattery || !IsOnBattery;

    public static BatterySnapshot NoBattery => new()
    {
        IsOnBattery = false,
        IsCharging = false,
        HasBattery = false
    };
}

public enum AppLanguage
{
    System,
    Hungarian,
    English
}

public enum UpdateChannel
{
    Stable,
    Preview
}

public enum LaunchAtLoginStatus
{
    Enabled,
    RequiresApproval,
    Disabled,
    Unavailable,
    Unknown
}

public static class PreferenceDefaults
{
    public const string MacOsRunnerDirectory = "/Users/koncsikbenedek/GitHub/actions-runner";
    public const string GitHubOAuthClientId = "Ov23liuWbzhLR0LpcXwv";
    public const UpdateChannel UpdateChannel = Models.UpdateChannel.Stable;
    public const AppLanguage Language = AppLanguage.System;
    public const bool StopRunnerOnBattery = false;
    public const bool StopRunnerOnMeteredNetwork = true;
    public const RunnerControlMode ControlMode = RunnerControlMode.Automatic;
}
