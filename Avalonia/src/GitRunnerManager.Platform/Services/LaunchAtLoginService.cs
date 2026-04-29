using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;
using System.Diagnostics;
using System.Security;
using System.Runtime.InteropServices;

namespace GitRunnerManager.Platform.Services;

public class LaunchAtLoginService : ILaunchAtLoginService
{
    private const string AppName = "GitRunnerManager";
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

                var userId = await GetUserIdAsync();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await RunLaunchctlAsync("bootout", $"gui/{userId}", path);
                await File.WriteAllTextAsync(path, CreateMacOsLaunchAgentPlist(executablePath));
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
        var arguments = CreateMacOsProgramArguments(executablePath)
            .Select(argument => $"        <string>{SecurityElement.Escape(argument) ?? argument}</string>");
        var argumentXml = string.Join(Environment.NewLine, arguments);

        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>{MacOsLaunchAgentLabel}</string>
    <key>ProgramArguments</key>
    <array>
{argumentXml}
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
</dict>
</plist>
""";
    }

    private static IReadOnlyList<string> CreateMacOsProgramArguments(string executablePath)
    {
        var appBundlePath = FindMacOsAppBundlePath(executablePath);
        if (!string.IsNullOrWhiteSpace(appBundlePath))
            return ["/usr/bin/open", appBundlePath];

        return [executablePath];
    }

    private static string? FindMacOsAppBundlePath(string executablePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(executablePath) ?? "");

        while (directory != null)
        {
            if (directory.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
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
