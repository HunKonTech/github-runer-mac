using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.Platform.Services;

public class GitHubService : IGitHubService
{
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
                ["scope"] = "repo admin:org"
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
                return result.AccessToken;
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

    public async Task<GitHubAccountSnapshot> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        var token = await _credentialStore.GetGitHubTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return new GitHubAccountSnapshot { IsSignedIn = false };

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new GitHubAccountSnapshot { IsSignedIn = false, Error = response.ReasonPhrase };

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var user = JsonSerializer.Deserialize<GitHubUserResponse>(json);
        return new GitHubAccountSnapshot { IsSignedIn = true, Login = user?.Login };
    }

    public Task SignOutAsync()
    {
        return _credentialStore.DeleteGitHubTokenAsync();
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
        return new GitHubRunnerSetupResult
        {
            Succeeded = succeeded,
            Message = succeeded ? "Runner configured successfully." : $"{stdout}\n{stderr}".Trim(),
            RunnerProfile = succeeded ? new RunnerConfig
            {
                DisplayName = runnerName,
                RunnerDirectory = request.RunnerDirectory,
                GitHubOwnerOrOrg = request.OwnerOrOrg,
                RepositoryName = request.Scope == GitHubRunnerScope.Repository ? request.RepositoryName : null,
                IsOrganizationRunner = request.Scope == GitHubRunnerScope.Organization,
                Labels = [..request.Labels],
                IsEnabled = true
            } : null
        };
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
}

internal class GitHubRegistrationTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }
}
