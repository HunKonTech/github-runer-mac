using System.Net.Http;
using System.Text.Json;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Platform.Services;

public class AppUpdateService : IAppUpdateService
{
    private const string Owner = "HunKonTech";
    private const string Repository = "github-runer-mac";

    private readonly HttpClient _httpClient;

    public AppUpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubRunnerTray");
    }

    public async Task<AppUpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubReleaseResponse>(json);

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

            var tempFile = Path.Combine(Path.GetTempPath(), $"GitHubRunnerTray_{update.Version}");
            if (OperatingSystem.IsWindows())
                tempFile += ".exe";
            else if (OperatingSystem.IsMacOS())
                tempFile += ".dmg";
            else
                tempFile += ".AppImage";

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

    private static GitHubAsset? FindPlatformAsset(List<GitHubAsset>? assets)
    {
        if (assets == null || assets.Count == 0)
            return null;

        var os = GetCurrentOs();
        var arch = GetCurrentArchitecture();

        return assets.FirstOrDefault(a =>
            a.Name?.Contains(os, StringComparison.OrdinalIgnoreCase) == true &&
            a.Name?.Contains(arch, StringComparison.OrdinalIgnoreCase) == true) ?? assets.FirstOrDefault(a =>
            a.Name?.Contains(os, StringComparison.OrdinalIgnoreCase) == true);
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
    public string? TagName { get; set; }
    public string? HtmlUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public List<GitHubAsset>? Assets { get; set; }
}

internal class GitHubAsset
{
    public string? Name { get; set; }
    public string? BrowserDownloadUrl { get; set; }
}