using System.Net;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Platform.Services;
using Xunit;

namespace GitRunnerManager.Tests;

public class GitHubServiceTests
{
    [Fact]
    public async Task GetAccountAsync_UsesStoredAccountWhenLegacyTokenIsInvalid()
    {
        var credentialStore = new FakeGitHubCredentialStore
        {
            LegacyToken = "legacy-token",
            Accounts =
            [
                new GitHubStoredAccount
                {
                    Id = "stored",
                    Login = "stored-login",
                    Token = "stored-token"
                }
            ]
        };
        using var httpClient = new HttpClient(new AccountValidationHandler());
        var service = new GitHubService(credentialStore, httpClient);

        var account = await ((IGitHubAuthService)service).GetAccountAsync();

        Assert.True(account.IsSignedIn);
        Assert.Equal("stored-login", account.Login);
    }

    [Fact]
    public async Task ActionsDashboard_LoadsRunsWhenStoredAccountExistsAndAccountValidationFails()
    {
        var tokenStore = new FakeGitHubCredentialStore
        {
            Accounts =
            [
                new GitHubStoredAccount
                {
                    Id = "stored",
                    Login = "stored-login",
                    Token = "stored-token"
                }
            ]
        };
        using var httpClient = new HttpClient(new ActionsDashboardHandler());
        var client = new GitHubActionsApiClient(tokenStore, new SignedOutGitHubAuthService(), httpClient);

        var snapshot = await client.GetDashboardAsync(
        [
            new RunnerConfig
            {
                DisplayName = "runner-one",
                RunnerDirectory = "/tmp/runner-one",
                GitHubOwnerOrOrg = "octo",
                RepositoryName = "repo"
            }
        ]);

        Assert.True(snapshot.Account.IsSignedIn);
        Assert.Equal("stored-login", snapshot.Account.Login);
        Assert.Single(snapshot.WorkflowRuns);
        Assert.Equal("octo/repo", snapshot.WorkflowRuns[0].RepositoryFullName);
    }

    [Fact]
    public async Task ActionsDashboard_LoadsRunsFromLegacyTokenWhenAccountListIsEmpty()
    {
        var tokenStore = new FakeGitHubCredentialStore
        {
            LegacyToken = "legacy-token",
            Accounts = []
        };
        using var httpClient = new HttpClient(new ActionsDashboardHandler());
        var client = new GitHubActionsApiClient(tokenStore, new LegacySignedInGitHubAuthService(), httpClient);

        var snapshot = await client.GetDashboardAsync(
        [
            new RunnerConfig
            {
                DisplayName = "runner-one",
                RunnerDirectory = "/tmp/runner-one",
                GitHubOwnerOrOrg = "octo",
                RepositoryName = "repo"
            }
        ]);

        Assert.True(snapshot.Account.IsSignedIn);
        Assert.Equal("BenKoncsik", snapshot.Account.Login);
        Assert.Single(snapshot.WorkflowRuns);
    }
}

internal sealed class AccountValidationHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = request.Headers.Authorization?.Parameter;
        if (token == "stored-token")
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"login":"stored-login","name":"Stored User"}""")
            };
            response.Headers.Add("x-oauth-scopes", "repo, read:org");
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Bad credentials"
        });
    }
}

internal sealed class ActionsDashboardHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";
        if (url.EndsWith("/actions/runners?per_page=100", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(JsonResponse("""{"runners":[]}"""));

        if (url.EndsWith("/actions/runs?per_page=10", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(JsonResponse("""
            {
              "workflow_runs": [
                {
                  "id": 123,
                  "run_number": 45,
                  "name": "CI",
                  "status": "completed",
                  "conclusion": "success",
                  "created_at": "2026-05-03T10:00:00Z",
                  "run_started_at": "2026-05-03T10:01:00Z",
                  "updated_at": "2026-05-03T10:05:00Z",
                  "html_url": "https://github.com/octo/repo/actions/runs/123",
                  "jobs_url": "https://api.github.com/repos/octo/repo/actions/runs/123/jobs",
                  "head_branch": "main",
                  "actor": { "login": "octo" }
                }
              ]
            }
            """));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }
}

internal sealed class SignedOutGitHubAuthService : IGitHubAuthService
{
    public Task<GitHubDeviceFlowStart> StartDeviceFlowAsync(string clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string> CompleteDeviceFlowAsync(string clientId, string deviceCode, int intervalSeconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitHubAccountConnection> CompleteDeviceFlowAsync(string clientId, string deviceCode, int intervalSeconds, GitHubAccountConnectionKind kind, string organization, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitHubAccountConnection> ImportExistingTokenAsync(GitHubAccountConnectionKind kind, string organization, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitHubAccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult(new GitHubAccountInfo { IsSignedIn = false });
    public Task<IReadOnlyList<GitHubAccountConnection>> GetAccountsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubAccountConnection>>([]);
    public Task SignOutAsync(string accountId) => throw new NotSupportedException();
    public Task SignOutAsync() => throw new NotSupportedException();
}

internal sealed class LegacySignedInGitHubAuthService : IGitHubAuthService
{
    public Task<GitHubDeviceFlowStart> StartDeviceFlowAsync(string clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string> CompleteDeviceFlowAsync(string clientId, string deviceCode, int intervalSeconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitHubAccountConnection> CompleteDeviceFlowAsync(string clientId, string deviceCode, int intervalSeconds, GitHubAccountConnectionKind kind, string organization, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitHubAccountConnection> ImportExistingTokenAsync(GitHubAccountConnectionKind kind, string organization, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GitHubAccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult(new GitHubAccountInfo { IsSignedIn = true, Login = "BenKoncsik" });
    public Task<IReadOnlyList<GitHubAccountConnection>> GetAccountsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubAccountConnection>>([]);
    public Task SignOutAsync(string accountId) => throw new NotSupportedException();
    public Task SignOutAsync() => throw new NotSupportedException();
}

internal sealed class FakeGitHubCredentialStore : ICredentialStore, IGitHubTokenStore
{
    public string? LegacyToken { get; set; }
    public IReadOnlyList<GitHubStoredAccount> Accounts { get; set; } = [];

    public Task<string?> GetGitHubTokenAsync() => Task.FromResult(LegacyToken);
    public Task SaveGitHubTokenAsync(string token)
    {
        LegacyToken = token;
        return Task.CompletedTask;
    }

    public Task DeleteGitHubTokenAsync()
    {
        LegacyToken = null;
        return Task.CompletedTask;
    }

    public Task<string?> GetTokenAsync() => GetGitHubTokenAsync();
    public Task SaveTokenAsync(string token) => SaveGitHubTokenAsync(token);
    public Task DeleteTokenAsync() => DeleteGitHubTokenAsync();
    public Task<IReadOnlyList<GitHubStoredAccount>> GetAccountsAsync() => Task.FromResult(Accounts);
    public Task SaveAccountAsync(GitHubStoredAccount account)
    {
        Accounts = [..Accounts, account];
        return Task.CompletedTask;
    }

    public Task DeleteAccountAsync(string accountId)
    {
        Accounts = Accounts.Where(account => account.Id != accountId).ToList();
        return Task.CompletedTask;
    }
}
