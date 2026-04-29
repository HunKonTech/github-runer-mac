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

public enum RunnerStatusKind
{
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
    public const UpdateChannel UpdateChannel = Models.UpdateChannel.Stable;
    public const AppLanguage Language = AppLanguage.System;
    public const bool StopRunnerOnBattery = false;
    public const bool StopRunnerOnMeteredNetwork = true;
    public const RunnerControlMode ControlMode = RunnerControlMode.Automatic;
}
