using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Platform.Services;

public class RunnerUpdateService : IRunnerUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/actions/runner/releases/latest";
    private readonly HttpClient _httpClient;

    public RunnerUpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitRunnerManager");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    public async Task<RunnerUpdateCheckResult> CheckForUpdateAsync(
        RunnerConfig profile,
        CancellationToken cancellationToken = default)
    {
        var installed = await DetectInstalledVersionAsync(profile.RunnerDirectory, cancellationToken);
        var release = await GetLatestReleaseAsync(cancellationToken);
        var asset = release?.Assets?.FirstOrDefault(asset => IsCurrentPlatformRunnerAsset(asset.Name));
        var latest = release?.TagName?.TrimStart('v');
        var available = RunnerUpdateDecision.IsUpdateAvailable(installed, latest);

        return new RunnerUpdateCheckResult
        {
            Profile = profile.Clone(),
            InstalledVersion = installed,
            LatestVersion = latest,
            DownloadUrl = asset?.BrowserDownloadUrl,
            IsUpdateAvailable = available && !string.IsNullOrWhiteSpace(asset?.BrowserDownloadUrl),
            StatusMessage = available
                ? "Runner update available."
                : "Runner is up to date."
        };
    }

    public async Task UpdateRunnerAsync(
        RunnerConfig profile,
        bool restartAfterUpdate,
        IProgress<RunnerUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var check = await CheckForUpdateAsync(profile, cancellationToken);
        if (!check.IsUpdateAvailable || string.IsNullOrWhiteSpace(check.DownloadUrl))
        {
            progress?.Report(Message(profile, "Runner is already up to date.", 1));
            return;
        }

        progress?.Report(Message(profile, "Downloading runner package.", 0.15));
        var archivePath = Path.Combine(Path.GetTempPath(), $"actions-runner-{check.LatestVersion}-{Guid.NewGuid():N}.tar.gz");
        await using (var stream = await _httpClient.GetStreamAsync(check.DownloadUrl, cancellationToken))
        await using (var output = File.Create(archivePath))
        {
            await stream.CopyToAsync(output, cancellationToken);
        }

        var extractPath = Path.Combine(Path.GetTempPath(), $"actions-runner-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractPath);

        progress?.Report(Message(profile, "Extracting runner package.", 0.35));
        await ExtractArchiveAsync(archivePath, extractPath, cancellationToken);

        progress?.Report(Message(profile, "Preserving runner configuration.", 0.55));
        PreserveConfiguration(profile.RunnerDirectory);

        progress?.Report(Message(profile, "Replacing runner binaries.", 0.75));
        CopyDirectory(extractPath, profile.RunnerDirectory);

        if (restartAfterUpdate)
            progress?.Report(Message(profile, "Runner package updated. Restart requested by manager.", 0.95));

        progress?.Report(Message(profile, "Runner update complete.", 1));
    }

    private static void PreserveConfiguration(string runnerDirectory)
    {
        foreach (var name in new[] { ".runner", ".credentials", ".credentials_rsaparams" })
        {
            var path = Path.Combine(runnerDirectory, name);
            if (File.Exists(path))
                File.Copy(path, path + ".bak", overwrite: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            if (IsConfigurationFile(relativePath))
                continue;

            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static async Task ExtractArchiveAsync(string archivePath, string extractPath, CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf \"{archivePath}\" -C \"{extractPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Could not start tar to extract the runner package.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Could not extract the runner package.");
    }

    private static bool IsConfigurationFile(string relativePath)
    {
        var name = relativePath.Replace('\\', '/');
        return name is ".runner" or ".credentials" or ".credentials_rsaparams";
    }

    private async Task<string?> DetectInstalledVersionAsync(string runnerDirectory, CancellationToken cancellationToken)
    {
        var listener = OperatingSystem.IsWindows()
            ? Path.Combine(runnerDirectory, "bin", "Runner.Listener.exe")
            : Path.Combine(runnerDirectory, "bin", "Runner.Listener");

        if (!File.Exists(listener))
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = listener,
                Arguments = "--version",
                WorkingDirectory = runnerDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output.Trim().Split('\n').FirstOrDefault()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<RunnerReleaseResponse?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<RunnerReleaseResponse>(json);
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsCurrentPlatformRunnerAsset(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            return false;

        var name = assetName.ToLowerInvariant();
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return name.Contains("actions-runner")
            && name.Contains(os)
            && name.Contains(arch)
            && (name.EndsWith(".tar.gz") || name.EndsWith(".zip"));
    }

    private static RunnerUpdateProgress Message(RunnerConfig profile, string message, double percent)
    {
        return new RunnerUpdateProgress
        {
            RunnerId = profile.Id,
            Message = message,
            Percent = percent
        };
    }
}

internal class RunnerReleaseResponse
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("assets")]
    public List<RunnerReleaseAsset>? Assets { get; set; }
}

internal class RunnerReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
