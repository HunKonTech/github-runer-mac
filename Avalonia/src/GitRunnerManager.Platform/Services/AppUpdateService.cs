using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Platform.Services;

public class AppUpdateService : IAppUpdateService
{
    private const string Owner = "HunKonTech";
    private const string Repository = "GitRunnerManager";

    private readonly HttpClient _httpClient;
    private readonly IPreferencesStoreFactory _preferencesFactory;

    public AppUpdateService(IPreferencesStoreFactory? preferencesFactory = null)
    {
        _preferencesFactory = preferencesFactory ?? new PreferencesStoreFactory();
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitRunnerManager");
    }

    public async Task<AppUpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var channel = _preferencesFactory.Create().UpdateChannel;
            var apiUrl = channel == UpdateChannel.Preview
                ? $"https://api.github.com/repos/{Owner}/{Repository}/releases"
                : $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var release = channel == UpdateChannel.Preview
                ? JsonSerializer.Deserialize<List<GitHubReleaseResponse>>(json)?.FirstOrDefault()
                : JsonSerializer.Deserialize<GitHubReleaseResponse>(json);

            if (release == null)
                return null;

            var platformAsset = FindPlatformAsset(release.Assets);
            if (platformAsset == null)
                return null;

            return new AppUpdateInfo
            {
                Version = release.TagName?.TrimStart('v') ?? "0.0.0",
                ReleasePageUrl = release.HtmlUrl ?? "",
                DownloadUrl = platformAsset.BrowserDownloadUrl ?? "",
                AssetName = platformAsset.Name ?? "",
                PublishedAt = release.PublishedAt
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadAndOpenUpdateAsync(AppUpdateInfo update)
    {
        try
        {
            var response = await _httpClient.GetAsync(update.DownloadUrl);
            if (!response.IsSuccessStatusCode)
                return;

            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"GitRunnerManager_{update.Version}{ResolveDownloadedFileExtension(update)}");

            await using (var fs = new FileStream(tempFile, FileMode.Create))
            {
                await response.Content.CopyToAsync(fs);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempFile,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Silently fail
        }
    }

    internal static GitHubAsset? FindPlatformAsset(List<GitHubAsset>? assets)
    {
        if (assets == null || assets.Count == 0)
            return null;

        var os = GetCurrentOs();
        var arch = GetCurrentArchitecture();

        var candidates = assets
            .Where(a => a.Name?.Contains(os, StringComparison.OrdinalIgnoreCase) == true &&
                        a.Name?.Contains(arch, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = assets
                .Where(a => a.Name?.Contains(os, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        return candidates
            .OrderBy(GetAssetPriority)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    internal static string ResolveDownloadedFileExtension(AppUpdateInfo update)
    {
        var assetExtension = Path.GetExtension(update.AssetName);
        if (!string.IsNullOrWhiteSpace(assetExtension))
            return assetExtension;

        if (Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var uri))
        {
            var urlExtension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(urlExtension))
                return urlExtension;
        }

        if (OperatingSystem.IsWindows())
            return ".exe";

        if (OperatingSystem.IsMacOS())
            return ".dmg";

        return ".AppImage";
    }

    private static int GetAssetPriority(GitHubAsset asset)
    {
        var name = asset.Name ?? "";

        if (OperatingSystem.IsWindows())
        {
            if (name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase)) return 2;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return 3;
        }

        if (OperatingSystem.IsMacOS() && name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (OperatingSystem.IsLinux() && name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
            return 0;

        return 10;
    }

    private static string GetCurrentOs()
    {
        if (OperatingSystem.IsWindows()) return "win";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }

    private static string GetCurrentArchitecture()
    {
        if (Environment.Is64BitOperatingSystem)
            return "x64";
        return "x86";
    }
}

internal class GitHubReleaseResponse
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

internal class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
