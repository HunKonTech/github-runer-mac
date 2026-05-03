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
