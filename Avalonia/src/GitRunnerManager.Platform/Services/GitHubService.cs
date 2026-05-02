using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.Platform.Services;

public class GitHubService : IGitHubService, IGitHubAuthService
{
    private const string RunnerLatestReleaseUrl = "https://api.github.com/repos/actions/runner/releases/latest";
    private readonly ICredentialStore _credentialStore;
    private readonly HttpClient _httpClient;

    public GitHubService(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GitRunnerManager");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<GitHubDeviceFlowStart> StartDeviceFlowAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            "https://github.com/login/device/code",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = "repo admin:org read:org"
            }),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<GitHubDeviceCodeResponse>(json) ?? throw new InvalidOperationException("GitHub did not return a device code.");
        return new GitHubDeviceFlowStart
        {
            DeviceCode = result.DeviceCode ?? "",
            UserCode = result.UserCode ?? "",
            VerificationUri = result.VerificationUri ?? "https://github.com/login/device",
            VerificationUriComplete = result.VerificationUriComplete ?? result.VerificationUri ?? "https://github.com/login/device",
            ExpiresIn = result.ExpiresIn,
            Interval = Math.Max(1, result.Interval)
        };
    }

    public async Task<string> CompleteDeviceFlowAsync(string clientId, string deviceCode, int intervalSeconds, CancellationToken cancellationToken = default)
    {
        var account = await CompleteDeviceFlowAsync(clientId, deviceCode, intervalSeconds, GitHubAccountConnectionKind.Personal, "", cancellationToken);
        var stored = (await ((IGitHubTokenStore)_credentialStore).GetAccountsAsync()).FirstOrDefault(item => item.Id == account.Id);
        return stored?.Token ?? "";
    }

    public async Task<GitHubAccountConnection> CompleteDeviceFlowAsync(string clientId, string deviceCode, int intervalSeconds, GitHubAccountConnectionKind kind, string organization, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)), cancellationToken);
            var response = await _httpClient.PostAsync(
                "https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                }),
                cancellationToken);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<GitHubAccessTokenResponse>(json);
            if (!string.IsNullOrWhiteSpace(result?.AccessToken))
            {
                await _credentialStore.SaveGitHubTokenAsync(result.AccessToken);
                var account = await GetAccountInfoAsync(result.AccessToken, cancellationToken);
                var connection = new GitHubStoredAccount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Login = account.Login ?? "GitHub",
                    Token = result.AccessToken,
                    Kind = kind,
                    Organization = organization.Trim()
                };
                await ((IGitHubTokenStore)_credentialStore).SaveAccountAsync(connection);
                return connection;
            }

            if (result?.Error is "authorization_pending" or "slow_down")
            {
                if (result.Error == "slow_down")
                    intervalSeconds += 5;
                continue;
            }

            throw new InvalidOperationException(result?.ErrorDescription ?? result?.Error ?? "GitHub sign in failed.");
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public async Task<GitHubAccountConnection> ImportExistingTokenAsync(GitHubAccountConnectionKind kind, string organization, CancellationToken cancellationToken = default)
    {
        var token = await GetExistingTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("No GitHub token was found. Sign in with GitHub CLI using 'gh auth login', or add an OAuth Client ID in Settings.");

        var account = await GetAccountInfoAsync(token, cancellationToken);
        if (!account.IsSignedIn)
            throw new InvalidOperationException(account.Error ?? "The existing GitHub token could not be validated.");

        var connection = new GitHubStoredAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Login = account.Login ?? "GitHub",
            Token = token,
            Kind = kind,
            Organization = organization.Trim()
        };
        await _credentialStore.SaveGitHubTokenAsync(token);
        await ((IGitHubTokenStore)_credentialStore).SaveAccountAsync(connection);
        return connection;
    }

    public async Task<GitHubAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        return (await GetAccountInfoAsync(cancellationToken)).ToSnapshot();
    }

    async Task<GitHubAccountInfo> IGitHubAuthService.GetAccountAsync(CancellationToken cancellationToken)
    {
        return await GetAccountInfoAsync(cancellationToken);
    }

    private async Task<GitHubAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        var token = await _credentialStore.GetGitHubTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            var accounts = await ((IGitHubTokenStore)_credentialStore).GetAccountsAsync();
            token = accounts.FirstOrDefault()?.Token;
        }
        if (string.IsNullOrWhiteSpace(token))
            return new GitHubAccountInfo { IsSignedIn = false };

        return await GetAccountInfoAsync(token, cancellationToken);
    }

    private async Task<GitHubAccountInfo> GetAccountInfoAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new GitHubAccountInfo { IsSignedIn = false, Error = response.ReasonPhrase };

        var scopes = response.Headers.TryGetValues("x-oauth-scopes", out var scopeValues)
            ? scopeValues.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToList()
            : [];
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var user = JsonSerializer.Deserialize<GitHubUserResponse>(json);
        return new GitHubAccountInfo
        {
            IsSignedIn = true,
            Login = user?.Login,
            Name = user?.Name,
            AvatarUrl = user?.AvatarUrl,
            HtmlUrl = user?.HtmlUrl,
            OAuthScopes = scopes
        };
    }

    private static async Task<string?> GetExistingTokenAsync(CancellationToken cancellationToken)
    {
        var environmentToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
            return environmentToken.Trim();

        return await GetGitHubCliTokenAsync(cancellationToken);
    }

    private static async Task<string?> GetGitHubCliTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth token",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return null;

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                return null;

            var token = (await outputTask).Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    public Task SignOutAsync()
    {
        return _credentialStore.DeleteGitHubTokenAsync();
    }

    public async Task<GitHubPermissionEvaluation> GetPermissionEvaluationAsync(CancellationToken cancellationToken = default)
    {
        var account = await GetAccountInfoAsync(cancellationToken);
        return GitHubPermissionEvaluator.Evaluate(account.IsSignedIn, account.OAuthScopes);
    }

    public async Task<IReadOnlyList<GitHubOwnerInfo>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        var json = await SendGitHubJsonAsync(HttpMethod.Get, "https://api.github.com/user/orgs?per_page=100", cancellationToken);
        var orgs = JsonSerializer.Deserialize<List<GitHubOrganizationResponse>>(json, JsonOptions.Default) ?? [];
        return orgs
            .Where(org => !string.IsNullOrWhiteSpace(org.Login))
            .Select(org => new GitHubOwnerInfo { Login = org.Login!, Kind = GitHubOwnerKind.Organization })
            .ToList();
    }

    public async Task<IReadOnlyList<GitHubRepositoryInfo>> GetUserRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var json = await SendGitHubJsonAsync(HttpMethod.Get, "https://api.github.com/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member", cancellationToken);
        return MapRepositories(JsonSerializer.Deserialize<List<GitHubRepositoryResponse>>(json, JsonOptions.Default) ?? []);
    }

    public async Task<IReadOnlyList<GitHubRepositoryInfo>> GetOrganizationRepositoriesAsync(string organization, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organization))
            return [];

        var json = await SendGitHubJsonAsync(HttpMethod.Get, $"https://api.github.com/orgs/{organization}/repos?per_page=100&sort=pushed", cancellationToken);
        return MapRepositories(JsonSerializer.Deserialize<List<GitHubRepositoryResponse>>(json, JsonOptions.Default) ?? []);
    }

    public async Task<IReadOnlyList<GitHubAccountConnection>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await ((IGitHubTokenStore)_credentialStore).GetAccountsAsync();
        return accounts.Select(account => new GitHubAccountConnection
        {
            Id = account.Id,
            Login = account.Login,
            Kind = account.Kind,
            Organization = account.Organization
        }).ToList();
    }

    public Task SignOutAsync(string accountId)
    {
        return ((IGitHubTokenStore)_credentialStore).DeleteAccountAsync(accountId);
    }

    public async Task<GitHubRegistrationToken> CreateRegistrationTokenAsync(GitHubRunnerSetupRequest request, CancellationToken cancellationToken = default)
    {
        var token = await _credentialStore.GetGitHubTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Sign in to GitHub first.");

        var path = request.Scope == GitHubRunnerScope.Organization
            ? $"https://api.github.com/orgs/{request.OwnerOrOrg}/actions/runners/registration-token"
            : $"https://api.github.com/repos/{request.OwnerOrOrg}/{request.RepositoryName}/actions/runners/registration-token";

        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var response = await _httpClient.SendAsync(message, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub registration token request failed: {(int)response.StatusCode} {content}");

        var parsed = JsonSerializer.Deserialize<GitHubRegistrationTokenResponse>(content) ?? throw new InvalidOperationException("GitHub did not return a registration token.");
        return new GitHubRegistrationToken
        {
            Token = parsed.Token ?? "",
            ExpiresAt = parsed.ExpiresAt
        };
    }

    public async Task<GitHubRunnerSetupResult> SetupRunnerAsync(GitHubRunnerSetupRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Scope == GitHubRunnerScope.Organization
            && request.RepositoryAccessMode == RunnerRepositoryAccessMode.SelectedRepositories
            && request.SelectedRepositories.Count > 0)
        {
            return await SetupRepositoryRunnersAsync(request, cancellationToken);
        }

        if (request.Scope == GitHubRunnerScope.Repository && request.SelectedRepositories.Count > 1)
            return await SetupRepositoryRunnersAsync(request, cancellationToken);

        if (request.Scope == GitHubRunnerScope.Repository && request.SelectedRepositories.Count == 1)
            request = CopyForRepository(request, request.SelectedRepositories[0], 0, request.SelectedRepositories.Count);

        if (request.FolderSetupMode == RunnerFolderSetupMode.CreateNew)
            await EnsureRunnerPackageAsync(request.RunnerDirectory, cancellationToken);

        var token = await CreateRegistrationTokenAsync(request, cancellationToken);
        return await ConfigureRunnerAsync(request, token, cancellationToken);
    }

    private async Task<GitHubRunnerSetupResult> SetupRepositoryRunnersAsync(GitHubRunnerSetupRequest request, CancellationToken cancellationToken)
    {
        var profiles = new List<RunnerConfig>();
        for (var index = 0; index < request.SelectedRepositories.Count; index++)
        {
            var repositoryRequest = CopyForRepository(request, request.SelectedRepositories[index], index, request.SelectedRepositories.Count);
            if (repositoryRequest.FolderSetupMode == RunnerFolderSetupMode.CreateNew)
                await EnsureRunnerPackageAsync(repositoryRequest.RunnerDirectory, cancellationToken);

            var token = await CreateRegistrationTokenAsync(repositoryRequest, cancellationToken);
            var result = await ConfigureRunnerAsync(repositoryRequest, token, cancellationToken);
            if (!result.Succeeded || result.RunnerProfile == null)
                return new GitHubRunnerSetupResult
                {
                    Succeeded = false,
                    Message = string.IsNullOrWhiteSpace(result.Message)
                        ? $"Runner setup failed for {repositoryRequest.OwnerOrOrg}/{repositoryRequest.RepositoryName}."
                        : result.Message,
                    RunnerProfiles = profiles
                };

            profiles.Add(result.RunnerProfile);
        }

        return new GitHubRunnerSetupResult
        {
            Succeeded = true,
            Message = profiles.Count == 1 ? "Runner configured successfully." : $"{profiles.Count} runners configured successfully.",
            RunnerProfile = profiles.FirstOrDefault(),
            RunnerProfiles = profiles
        };
    }

    private static GitHubRunnerSetupRequest CopyForRepository(GitHubRunnerSetupRequest request, GitHubRepositoryReference repository, int index, int total)
    {
        var suffix = total <= 1 ? "" : $"-{SafeName(repository.Repo)}";
        var directory = total <= 1
            ? request.RunnerDirectory
            : Path.Combine(request.RunnerDirectory, SafeName(repository.FullName));
        return new GitHubRunnerSetupRequest
        {
            Scope = GitHubRunnerScope.Repository,
            RepositoryAccessMode = RunnerRepositoryAccessMode.SelectedRepositories,
            FolderSetupMode = request.FolderSetupMode,
            OwnerOrOrg = repository.Owner,
            RepositoryName = repository.Repo,
            RunnerDirectory = directory,
            RunnerName = total <= 1 ? request.RunnerName : $"{request.RunnerName}{suffix}",
            Labels = [..request.Labels],
            SelectedRepositories = [repository]
        };
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '-' : ch).ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? "runner" : clean;
    }

    public async Task<GitHubRunnerSetupResult> ConfigureRunnerAsync(GitHubRunnerSetupRequest request, GitHubRegistrationToken token, CancellationToken cancellationToken = default)
    {
        var configScript = GetConfigScript(request.RunnerDirectory);
        if (configScript == null)
            return new GitHubRunnerSetupResult { Succeeded = false, Message = "Cannot find config.sh or config.cmd in the runner directory." };

        Directory.CreateDirectory(request.RunnerDirectory);
        var labels = request.Labels.Count == 0 ? "self-hosted" : string.Join(",", request.Labels);
        var runnerName = string.IsNullOrWhiteSpace(request.RunnerName)
            ? Environment.MachineName
            : request.RunnerName;
        var arguments = $"--unattended --url \"{request.GitHubUrl}\" --token \"{token.Token}\" --name \"{runnerName}\" --labels \"{labels}\" --replace";
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? configScript : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? arguments : $"\"{configScript}\" {arguments}",
            WorkingDirectory = request.RunnerDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            return new GitHubRunnerSetupResult { Succeeded = false, Message = "Could not start runner configuration." };

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var succeeded = process.ExitCode == 0;
        var profile = succeeded ? new RunnerConfig
        {
            DisplayName = runnerName,
            RunnerDirectory = request.RunnerDirectory,
            GitHubOwnerOrOrg = request.OwnerOrOrg,
            RepositoryName = request.Scope == GitHubRunnerScope.Repository ? request.RepositoryName : null,
            IsOrganizationRunner = request.Scope == GitHubRunnerScope.Organization,
            Labels = [..request.Labels],
            IsEnabled = true
        } : null;
        return new GitHubRunnerSetupResult
        {
            Succeeded = succeeded,
            Message = succeeded ? "Runner configured successfully." : $"{stdout}\n{stderr}".Trim(),
            RunnerProfile = profile,
            RunnerProfiles = profile == null ? [] : [profile]
        };
    }

    private async Task EnsureRunnerPackageAsync(string runnerDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(runnerDirectory);
        if (GetConfigScript(runnerDirectory) != null)
            return;

        var releaseJson = await SendAnonymousJsonAsync(RunnerLatestReleaseUrl, cancellationToken);
        var release = JsonSerializer.Deserialize<GitHubRunnerReleaseResponse>(releaseJson, JsonOptions.Default);
        var asset = release?.Assets.FirstOrDefault(asset => IsCurrentPlatformRunnerAsset(asset.Name));
        if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            throw new InvalidOperationException("Could not find a GitHub Actions runner package for this platform.");

        var extension = asset.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true ? ".zip" : ".tar.gz";
        var archivePath = Path.Combine(Path.GetTempPath(), $"actions-runner-{Guid.NewGuid():N}{extension}");
        await using (var stream = await _httpClient.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken))
        await using (var output = File.Create(archivePath))
        {
            await stream.CopyToAsync(output, cancellationToken);
        }

        if (extension == ".zip")
        {
            ZipFile.ExtractToDirectory(archivePath, runnerDirectory, overwriteFiles: true);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf \"{archivePath}\" -C \"{runnerDirectory}\"",
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

    private async Task<string> SendGitHubJsonAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var token = await _credentialStore.GetGitHubTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Sign in to GitHub first.");

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub API request failed: {(int)response.StatusCode} {content}");

        return content;
    }

    private async Task<string> SendAnonymousJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub API request failed: {(int)response.StatusCode} {content}");

        return content;
    }

    private static IReadOnlyList<GitHubRepositoryInfo> MapRepositories(IEnumerable<GitHubRepositoryResponse> repositories)
    {
        return repositories
            .Select(repository => new GitHubRepositoryInfo
            {
                Owner = repository.Owner?.Login ?? "",
                Name = repository.Name ?? "",
                FullName = repository.FullName ?? "",
                HtmlUrl = repository.HtmlUrl ?? "",
                ActionsEnabled = repository.HasActions
            })
            .Where(repository => !string.IsNullOrWhiteSpace(repository.Owner) && !string.IsNullOrWhiteSpace(repository.Name))
            .ToList();
    }

    private static bool IsCurrentPlatformRunnerAsset(string? assetName)
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

    private static string? GetConfigScript(string runnerDirectory)
    {
        var sh = Path.Combine(runnerDirectory, "config.sh");
        if (File.Exists(sh))
            return sh;

        var cmd = Path.Combine(runnerDirectory, "config.cmd");
        if (File.Exists(cmd))
            return cmd;

        return null;
    }
}

internal class GitHubDeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; set; }
    [JsonPropertyName("user_code")]
    public string? UserCode { get; set; }
    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; set; }
    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; set; }
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")]
    public int Interval { get; set; }
}

internal class GitHubAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

internal class GitHubUserResponse
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

internal class GitHubRegistrationTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }
}

internal class GitHubOrganizationResponse
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

internal class GitHubRepositoryResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
    [JsonPropertyName("has_actions")]
    public bool HasActions { get; set; } = true;
    [JsonPropertyName("owner")]
    public GitHubRepositoryOwnerResponse? Owner { get; set; }
}

internal class GitHubRepositoryOwnerResponse
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

internal class GitHubRunnerReleaseResponse
{
    [JsonPropertyName("assets")]
    public List<GitHubRunnerReleaseAssetResponse> Assets { get; set; } = [];
}

internal class GitHubRunnerReleaseAssetResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
