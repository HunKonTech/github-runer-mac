using System.Diagnostics;
using System.Text.Json;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Platform.Services;

public class CredentialStore : ICredentialStore, IGitHubTokenStore
{
    public Task<string?> GetTokenAsync() => GetGitHubTokenAsync();
    public Task SaveTokenAsync(string token) => SaveGitHubTokenAsync(token);
    public Task DeleteTokenAsync() => DeleteGitHubTokenAsync();

    private const string Service = "GitRunnerManager";
    private const string Account = "GitHubOAuthToken";
    private const string AccountsAccount = "GitHubOAuthAccounts";

    public async Task<IReadOnlyList<GitHubStoredAccount>> GetAccountsAsync()
    {
        var json = OperatingSystem.IsMacOS() ? GetMacOsToken(AccountsAccount) : GetFallbackText("github-accounts.json");
        var accounts = string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<GitHubStoredAccount>>(json) ?? [];
        var legacyToken = await GetGitHubTokenAsync();
        if (!string.IsNullOrWhiteSpace(legacyToken) && accounts.All(account => account.Token != legacyToken))
        {
            accounts.Insert(0, new GitHubStoredAccount
            {
                Id = "legacy",
                Login = "GitHub",
                Token = legacyToken,
                Kind = GitHubAccountConnectionKind.Personal
            });
        }

        return accounts;
    }

    public async Task SaveAccountAsync(GitHubStoredAccount account)
    {
        var accounts = (await GetAccountsAsync()).Where(item => item.Id != account.Id && item.Token != account.Token).ToList();
        accounts.Add(account);
        await SaveAccountsAsync(accounts);
        if (accounts.Count == 1)
            await SaveGitHubTokenAsync(account.Token);
    }

    public async Task DeleteAccountAsync(string accountId)
    {
        var accounts = (await GetAccountsAsync()).Where(item => item.Id != accountId).ToList();
        await SaveAccountsAsync(accounts);
        if (accountId == "legacy" || accounts.Count == 0)
            await DeleteGitHubTokenAsync();
    }

    private static Task SaveAccountsAsync(IReadOnlyList<GitHubStoredAccount> accounts)
    {
        var json = JsonSerializer.Serialize(accounts);
        if (OperatingSystem.IsMacOS())
            SaveMacOsToken(AccountsAccount, json);
        else
            SaveFallbackText("github-accounts.json", json);
        return Task.CompletedTask;
    }

    public Task<string?> GetGitHubTokenAsync()
    {
        if (OperatingSystem.IsMacOS())
            return Task.FromResult(GetMacOsToken(Account));

        return Task.FromResult(GetFallbackToken());
    }

    public Task SaveGitHubTokenAsync(string token)
    {
        if (OperatingSystem.IsMacOS())
            SaveMacOsToken(Account, token);
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

    private static string? GetMacOsToken(string account)
    {
        var result = RunSecurity($"find-generic-password -s \"{Service}\" -a \"{account}\" -w");
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    private static void SaveMacOsToken(string account, string token)
    {
        RunSecurity($"delete-generic-password -s \"{Service}\" -a \"{account}\"");
        RunSecurity($"add-generic-password -U -s \"{Service}\" -a \"{account}\" -w \"{Escape(token)}\"");
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

    private static string? GetFallbackText(string fileName)
    {
        var path = Path.Combine(Path.GetDirectoryName(FallbackPath())!, fileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void SaveFallbackToken(string token)
    {
        File.WriteAllText(FallbackPath(), token);
    }

    private static void SaveFallbackText(string fileName, string text)
    {
        var path = Path.Combine(Path.GetDirectoryName(FallbackPath())!, fileName);
        File.WriteAllText(path, text);
    }

    private static void DeleteFallbackToken()
    {
        var path = FallbackPath();
        if (File.Exists(path))
            File.Delete(path);
    }
}
