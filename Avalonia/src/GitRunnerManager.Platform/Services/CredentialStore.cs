using System.Diagnostics;
using GitRunnerManager.Core.Interfaces;

namespace GitRunnerManager.Platform.Services;

public class CredentialStore : ICredentialStore
{
    private const string Service = "GitRunnerManager";
    private const string Account = "GitHubOAuthToken";

    public Task<string?> GetGitHubTokenAsync()
    {
        if (OperatingSystem.IsMacOS())
            return Task.FromResult(GetMacOsToken());

        return Task.FromResult(GetFallbackToken());
    }

    public Task SaveGitHubTokenAsync(string token)
    {
        if (OperatingSystem.IsMacOS())
            SaveMacOsToken(token);
        else
            SaveFallbackToken(token);

        return Task.CompletedTask;
    }

    public Task DeleteGitHubTokenAsync()
    {
        if (OperatingSystem.IsMacOS())
            RunSecurity($"delete-generic-password -s \"{Service}\" -a \"{Account}\"");
        else
            DeleteFallbackToken();

        return Task.CompletedTask;
    }

    private static string? GetMacOsToken()
    {
        var result = RunSecurity($"find-generic-password -s \"{Service}\" -a \"{Account}\" -w");
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    private static void SaveMacOsToken(string token)
    {
        RunSecurity($"delete-generic-password -s \"{Service}\" -a \"{Account}\"");
        RunSecurity($"add-generic-password -U -s \"{Service}\" -a \"{Account}\" -w \"{Escape(token)}\"");
    }

    private static (int ExitCode, string Output) RunSecurity(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return (-1, "");

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return (process.ExitCode, output);
        }
        catch
        {
            return (-1, "");
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string FallbackPath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitRunnerManager");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "github-token.txt");
    }

    private static string? GetFallbackToken()
    {
        var path = FallbackPath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void SaveFallbackToken(string token)
    {
        File.WriteAllText(FallbackPath(), token);
    }

    private static void DeleteFallbackToken()
    {
        var path = FallbackPath();
        if (File.Exists(path))
            File.Delete(path);
    }
}
