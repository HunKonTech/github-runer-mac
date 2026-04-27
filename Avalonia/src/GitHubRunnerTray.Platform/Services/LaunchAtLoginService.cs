using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Platform.Services;

public class LaunchAtLoginService : ILaunchAtLoginService
{
    private const string AppName = "GitHubRunnerTray";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public LaunchAtLoginStatus GetStatus()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(AppName);

            if (value != null)
                return LaunchAtLoginStatus.Enabled;

            return LaunchAtLoginStatus.Disabled;
        }
        catch
        {
            return LaunchAtLoginStatus.Unknown;
        }
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null)
                    return false;

                if (enabled)
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                        return true;
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        });
    }
}

public class LaunchAtLoginServiceFactory : ILaunchAtLoginServiceFactory
{
    public ILaunchAtLoginService Create()
    {
        return new LaunchAtLoginService();
    }
}