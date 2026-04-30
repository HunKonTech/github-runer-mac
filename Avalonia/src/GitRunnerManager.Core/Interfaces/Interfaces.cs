using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.Core.Interfaces;

public interface IRunnerController : IDisposable
{
    RunnerSnapshot GetCurrentSnapshot();
    Task StartAsync();
    Task StopAsync();
}

public interface IRunnerControllerFactory
{
    IRunnerController Create(DirectoryInfo runnerDirectory);
}

public interface IRunnerLogParser
{
    RunnerActivitySnapshot GetLatestActivity(DirectoryInfo runnerDirectory);
}

public interface IResourceMonitor
{
    RunnerResourceUsage GetCurrentUsage();
    void Stop();
}

public interface IResourceMonitorFactory
{
    IResourceMonitor Create(DirectoryInfo runnerDirectory);
}

public interface INetworkConditionMonitor : IDisposable
{
    event EventHandler<NetworkConditionSnapshot>? OnChange;
    void Start();
    void Stop();
}

public interface IBatteryMonitor : IDisposable
{
    event EventHandler<BatterySnapshot>? OnChange;
    void Start();
    void Stop();
}

public interface IBatteryMonitorFactory
{
    IBatteryMonitor Create();
}

public interface IPreferencesStore
{
    AppLanguage Language { get; set; }
    string GitHubOAuthClientId { get; set; }
    string RunnerDirectory { get; set; }
    List<RunnerConfig> RunnerProfiles { get; set; }
    RunnerControlMode ControlMode { get; set; }
    bool AutomaticUpdateCheckEnabled { get; set; }
    UpdateChannel UpdateChannel { get; set; }
    bool StopRunnerOnBattery { get; set; }
    bool StopRunnerOnMeteredNetwork { get; set; }
}

public interface IPreferencesStoreFactory
{
    IPreferencesStore Create();
}

public interface IAppUpdateService
{
    Task<AppUpdateInfo?> CheckForUpdatesAsync();
    Task DownloadAndOpenUpdateAsync(AppUpdateInfo update);
}

public interface ICredentialStore
{
    Task<string?> GetGitHubTokenAsync();
    Task SaveGitHubTokenAsync(string token);
    Task DeleteGitHubTokenAsync();
}

public interface IGitHubService
{
    Task<GitHubDeviceFlowStart> StartDeviceFlowAsync(string clientId, CancellationToken cancellationToken = default);
    Task<string> CompleteDeviceFlowAsync(string clientId, string deviceCode, int intervalSeconds, CancellationToken cancellationToken = default);
    Task<GitHubAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync();
    Task<GitHubRegistrationToken> CreateRegistrationTokenAsync(GitHubRunnerSetupRequest request, CancellationToken cancellationToken = default);
    Task<GitHubRunnerSetupResult> ConfigureRunnerAsync(GitHubRunnerSetupRequest request, GitHubRegistrationToken token, CancellationToken cancellationToken = default);
}

public interface IRunnerManager : IDisposable
{
    IReadOnlyList<RunnerInstanceStore> Runners { get; }
    RunnerInstanceStore? GetRunner(string runnerId);
    void ReloadProfiles();
    void SaveProfile(RunnerConfig profile);
    void AddProfile(RunnerConfig profile);
    void RemoveProfile(string runnerId);
    Task RefreshAllAsync(NetworkConditionSnapshot network, BatterySnapshot battery, RunnerControlMode controlMode);
    Task StartAllAsync();
    Task StopAllAsync();
}

public interface IRunnerUpdateService
{
    Task<RunnerUpdateCheckResult> CheckForUpdateAsync(RunnerConfig profile, CancellationToken cancellationToken = default);
    Task UpdateRunnerAsync(
        RunnerConfig profile,
        bool restartAfterUpdate,
        IProgress<RunnerUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public class AppUpdateInfo
{
    public required string Version { get; init; }
    public required string ReleasePageUrl { get; init; }
    public required string DownloadUrl { get; init; }
    public DateTime? PublishedAt { get; init; }
}

public interface ILaunchAtLoginService
{
    LaunchAtLoginStatus GetStatus();
    Task<bool> SetEnabledAsync(bool enabled);
}

public interface ILaunchAtLoginServiceFactory
{
    ILaunchAtLoginService Create();
}

public interface IClock
{
    DateTime Now { get; }
}

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<FileInfo> GetFiles(string path, string searchPattern);
    Task<string> ReadAllTextAsync(string path);
    DirectoryInfo CreateDirectory(string path);
    void OpenInExplorer(string path);
}
