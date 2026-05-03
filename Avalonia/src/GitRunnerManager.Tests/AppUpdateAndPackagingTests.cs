using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Platform.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class AppUpdateAndPackagingTests
{
    [Fact]
    public void FindPlatformAsset_OnWindows_PrefersMsixZipOverMsix()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var asset = AppUpdateService.FindPlatformAsset(
        [
            new GitHubAsset { Name = "GitRunnerManager-win-x64.exe", BrowserDownloadUrl = "https://example.com/GitRunnerManager-win-x64.exe" },
            new GitHubAsset { Name = "GitRunnerManager-win-x64.msix", BrowserDownloadUrl = "https://example.com/GitRunnerManager-win-x64.msix" },
            new GitHubAsset { Name = "GitRunnerManager-win-x64.zip", BrowserDownloadUrl = "https://example.com/GitRunnerManager-win-x64.zip" }
        ]);

        Assert.NotNull(asset);
        Assert.Equal("GitRunnerManager-win-x64.zip", asset!.Name);
    }

    [Fact]
    public void ResolveDownloadedFileExtension_UsesAssetNameExtension()
    {
        var extension = AppUpdateService.ResolveDownloadedFileExtension(new AppUpdateInfo
        {
            Version = "1.0.0",
            ReleasePageUrl = "https://example.com/release",
            DownloadUrl = "https://example.com/download",
            AssetName = "GitRunnerManager-win-x64.zip"
        });

        Assert.Equal(".zip", extension);
    }

    [Fact]
    public void LaunchAtLoginStatus_OnPackagedWindows_IsUnavailable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        WindowsPackageIdentity.DetectorOverride = static () => true;
        try
        {
            var service = new LaunchAtLoginService();

            Assert.Equal(LaunchAtLoginStatus.Unavailable, service.GetStatus());
        }
        finally
        {
            WindowsPackageIdentity.DetectorOverride = null;
        }
    }
}
