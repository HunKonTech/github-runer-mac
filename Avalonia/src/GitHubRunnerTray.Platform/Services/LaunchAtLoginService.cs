using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;
using System.Diagnostics;
using System.Security;
using System.Runtime.InteropServices;

namespace GitHubRunnerTray.Platform.Services;

public class LaunchAtLoginService : ILaunchAtLoginService
{
    private const string AppName = "GitHubRunnerTray";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string MacOsLaunchAgentLabel = "com.koncsikbenedek.github-runner-tray";
    private const string MacOsLaunchAgentFileName = MacOsLaunchAgentLabel + ".plist";

    public LaunchAtLoginStatus GetStatus()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return File.Exists(MacOsLaunchAgentPath) ? LaunchAtLoginStatus.Enabled : LaunchAtLoginStatus.Disabled;

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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return await SetMacOsEnabledAsync(enabled);

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

    private static string MacOsLaunchAgentPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "LaunchAgents", MacOsLaunchAgentFileName);
        }
    }

    private static async Task<bool> SetMacOsEnabledAsync(bool enabled)
    {
        try
        {
            var path = MacOsLaunchAgentPath;

            if (enabled)
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                    return false;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, CreateMacOsLaunchAgentPlist(executablePath));
                await RunLaunchctlAsync("bootout", $"gui/{await GetUserIdAsync()}", path);
                await RunLaunchctlAsync("bootstrap", $"gui/{await GetUserIdAsync()}", path);
                return File.Exists(path);
            }

            await RunLaunchctlAsync("bootout", $"gui/{await GetUserIdAsync()}", path);
            if (File.Exists(path))
                File.Delete(path);

            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string CreateMacOsLaunchAgentPlist(string executablePath)
    {
        var escapedPath = SecurityElement.Escape(executablePath) ?? executablePath;
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>{MacOsLaunchAgentLabel}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{escapedPath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
</dict>
</plist>
""";
    }

    private static async Task<string> GetUserIdAsync()
    {
        var result = await RunProcessAsync("/usr/bin/id", "-u");
        return string.IsNullOrWhiteSpace(result) ? Environment.UserName : result.Trim();
    }

    private static Task RunLaunchctlAsync(params string[] arguments)
    {
        return RunProcessAsync("/bin/launchctl", arguments).ContinueWith(_ => { });
    }

    private static async Task<string> RunProcessAsync(string fileName, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}

public class LaunchAtLoginServiceFactory : ILaunchAtLoginServiceFactory
{
    public ILaunchAtLoginService Create()
    {
        return new LaunchAtLoginService();
    }
}
