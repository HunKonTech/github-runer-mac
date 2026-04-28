using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Core.Interfaces;

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
    string RunnerDirectory { get; set; }
    RunnerControlMode ControlMode { get; set; }
    bool AutomaticUpdateCheckEnabled { get; set; }
    UpdateChannel UpdateChannel { get; set; }
    bool StopRunnerOnBattery { get; set; }
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
