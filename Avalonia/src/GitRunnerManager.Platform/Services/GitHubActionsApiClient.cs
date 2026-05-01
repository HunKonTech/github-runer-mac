using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.Platform.Services;

public sealed class GitHubActionsApiClient : IGitHubActionsService
{
    private readonly IGitHubTokenStore _tokenStore;
    private readonly IGitHubAuthService _authService;
    private readonly HttpClient _httpClient = new();

    public GitHubActionsApiClient(IGitHubTokenStore tokenStore, IGitHubAuthService authService)
    {
        _tokenStore = tokenStore;
        _authService = authService;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GitRunnerManager");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<GitHubDashboardSnapshot> GetDashboardAsync(IReadOnlyList<RunnerConfig> runners, CancellationToken cancellationToken = default)
    {
        var account = await _authService.GetAccountAsync(cancellationToken);
        var accounts = await _tokenStore.GetAccountsAsync();
        if (!account.IsSignedIn || accounts.Count == 0)
            return new GitHubDashboardSnapshot { Account = account };

        var permission = new GitHubApiPermissionStatus();
        var runnerInfos = new List<GitHubRunnerInfo>();
        foreach (var runner in runners)
            runnerInfos.Add(await GetRunnerInfoAsync(runner, accounts, cancellationToken));

        var repositories = await GetRepositoriesAsync(runners, accounts, cancellationToken);
        var runs = new List<GitHubWorkflowRunInfo>();
        foreach (var repository in repositories.Take(12))
        {
            var repositoryRuns = await GetWorkflowRunsAsync(repository, runners, accounts, cancellationToken);
            runs.AddRange(repositoryRuns);
        }

        runs = [.. runs.OrderByDescending(run => run.StartedAt ?? DateTimeOffset.MinValue).Take(30)];
        if (runnerInfos.Any(runner => !string.IsNullOrWhiteSpace(runner.PermissionMessage)))
            permission = permission.withRunnerPermissionMessage("Organization runner details require admin permission.");

        return new GitHubDashboardSnapshot
        {
            Account = account,
            Runners = runnerInfos,
            WorkflowRuns = runs,
            PermissionStatus = permission,
            RefreshedAt = DateTimeOffset.Now
        };
    }

    public async Task<IReadOnlyList<GitHubWorkflowJobInfo>> GetJobsAsync(GitHubWorkflowRunInfo run, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(run.JobsUrl))
            return [];

        var response = await SendAnyAccountAsync(HttpMethod.Get, run.JobsUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<JobsResponse>(body, JsonOptions.Default);
        return parsed?.Jobs.Select(MapJob).ToList() ?? [];
    }

    private async Task<GitHubRunnerInfo> GetRunnerInfoAsync(RunnerConfig runner, IReadOnlyList<GitHubStoredAccount> accounts, CancellationToken cancellationToken)
    {
        var owner = new GitHubOwnerInfo
        {
            Login = runner.GitHubOwnerOrOrg,
            Kind = runner.IsOrganizationRunner ? GitHubOwnerKind.Organization : GitHubOwnerKind.Repository
        };
        var fallback = new GitHubRunnerInfo
        {
            Name = runner.DisplayName,
            Status = "unknown",
            Labels = runner.Labels,
            Owner = owner,
            Repository = string.IsNullOrWhiteSpace(runner.RepositoryName) ? null : new GitHubRepositoryInfo
            {
                Owner = runner.GitHubOwnerOrOrg,
                Name = runner.RepositoryName,
                FullName = $"{runner.GitHubOwnerOrOrg}/{runner.RepositoryName}",
                HtmlUrl = $"https://github.com/{runner.GitHubOwnerOrOrg}/{runner.RepositoryName}"
            }
        };

        if (string.IsNullOrWhiteSpace(runner.GitHubOwnerOrOrg))
            return fallback;

        var path = runner.IsOrganizationRunner
            ? $"https://api.github.com/orgs/{runner.GitHubOwnerOrOrg}/actions/runners?per_page=100"
            : $"https://api.github.com/repos/{runner.GitHubOwnerOrOrg}/{runner.RepositoryName}/actions/runners?per_page=100";
        var response = await SendBestAccountAsync(HttpMethod.Get, path, runner.GitHubOwnerOrOrg, accounts, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            return fallback.withPermission("Organization runner details require admin permission.");
        if (!response.IsSuccessStatusCode)
            return fallback;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<RunnersResponse>(body, JsonOptions.Default);
        var match = parsed?.Runners.FirstOrDefault(item => GitHubJobMatcher.Matches(item.Name ?? "", runner));
        if (match == null)
            return fallback;

        var group = runner.IsOrganizationRunner && match.RunnerGroupId.HasValue
            ? await GetRunnerGroupAsync(runner.GitHubOwnerOrOrg, match.RunnerGroupId.Value, accounts, cancellationToken)
            : match.RunnerGroupName == null ? null : new GitHubRunnerGroupInfo { Name = match.RunnerGroupName };

        return new GitHubRunnerInfo
        {
            Id = match.Id,
            Name = match.Name ?? runner.DisplayName,
            Status = match.Status ?? "unknown",
            Busy = match.Busy,
            Labels = match.Labels.Select(label => label.Name ?? "").Where(label => label.Length > 0).ToList(),
            Owner = owner,
            Repository = fallback.Repository,
            Group = group
        };
    }

    private async Task<GitHubRunnerGroupInfo?> GetRunnerGroupAsync(string organization, long groupId, IReadOnlyList<GitHubStoredAccount> accounts, CancellationToken cancellationToken)
    {
        var response = await SendBestAccountAsync(HttpMethod.Get, $"https://api.github.com/orgs/{organization}/actions/runner-groups/{groupId}", organization, accounts, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            return new GitHubRunnerGroupInfo { Id = groupId, PermissionDenied = true };
        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var group = JsonSerializer.Deserialize<RunnerGroupResponse>(body, JsonOptions.Default);
        if (group == null)
            return null;

        var allowsAll = string.Equals(group.Visibility, "all", StringComparison.OrdinalIgnoreCase);
        var repositories = allowsAll ? [] : await GetRunnerGroupRepositoriesAsync(organization, groupId, accounts, cancellationToken);
        return new GitHubRunnerGroupInfo
        {
            Id = group.Id,
            Name = group.Name ?? "",
            AllowsAllRepositories = allowsAll,
            SelectedRepositories = repositories
        };
    }

    private async Task<IReadOnlyList<GitHubRepositoryInfo>> GetRunnerGroupRepositoriesAsync(string organization, long groupId, IReadOnlyList<GitHubStoredAccount> accounts, CancellationToken cancellationToken)
    {
        var response = await SendBestAccountAsync(HttpMethod.Get, $"https://api.github.com/orgs/{organization}/actions/runner-groups/{groupId}/repositories?per_page=100", organization, accounts, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<RepositoriesResponse>(body, JsonOptions.Default);
        return parsed?.Repositories.Select(MapRepository).ToList() ?? [];
    }

    private async Task<IReadOnlyList<GitHubRepositoryInfo>> GetRepositoriesAsync(IReadOnlyList<RunnerConfig> runners, IReadOnlyList<GitHubStoredAccount> accounts, CancellationToken cancellationToken)
    {
        var configured = runners
            .Where(runner => !runner.IsOrganizationRunner && !string.IsNullOrWhiteSpace(runner.GitHubOwnerOrOrg) && !string.IsNullOrWhiteSpace(runner.RepositoryName))
            .Select(runner => new GitHubRepositoryInfo
            {
                Owner = runner.GitHubOwnerOrOrg,
                Name = runner.RepositoryName!,
                FullName = $"{runner.GitHubOwnerOrOrg}/{runner.RepositoryName}",
                HtmlUrl = $"https://github.com/{runner.GitHubOwnerOrOrg}/{runner.RepositoryName}"
            })
            .ToList();
        if (configured.Count > 0)
            return configured.DistinctBy(repository => repository.FullName).ToList();

        var result = new List<GitHubRepositoryInfo>();
        foreach (var account in accounts)
        {
            var response = await SendAsync(HttpMethod.Get, "https://api.github.com/user/repos?per_page=20&sort=pushed&affiliation=owner,collaborator,organization_member", account.Token, cancellationToken);
            if (!response.IsSuccessStatusCode)
                continue;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var repositories = JsonSerializer.Deserialize<List<RepositoryResponse>>(body, JsonOptions.Default) ?? [];
            result.AddRange(repositories.Select(MapRepository).Where(repository => !string.IsNullOrWhiteSpace(repository.FullName)));
        }

        return result.DistinctBy(repository => repository.FullName).ToList();
    }

    private async Task<IReadOnlyList<GitHubWorkflowRunInfo>> GetWorkflowRunsAsync(GitHubRepositoryInfo repository, IReadOnlyList<RunnerConfig> runners, IReadOnlyList<GitHubStoredAccount> accounts, CancellationToken cancellationToken)
    {
        var response = await SendBestAccountAsync(HttpMethod.Get, $"https://api.github.com/repos/{repository.FullName}/actions/runs?per_page=10", repository.Owner, accounts, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<RunsResponse>(body, JsonOptions.Default);
        if (parsed == null)
            return [];

        var result = new List<GitHubWorkflowRunInfo>();
        foreach (var run in parsed.WorkflowRuns)
        {
            var info = MapRun(repository.FullName, run);
            if (info.IsActive)
            {
                var jobs = await GetJobsAsync(info, cancellationToken);
                info = info.withRunningOnThisRunner(jobs.Any(job => GitHubJobMatcher.IsRunningOnLocalRunner(job, runners)));
            }
            result.Add(info);
        }

        return result;
    }

    private async Task<HttpResponseMessage> SendAnyAccountAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var accounts = await _tokenStore.GetAccountsAsync();
        foreach (var account in accounts)
        {
            var response = await SendAsync(method, url, account.Token, cancellationToken);
            if (response.IsSuccessStatusCode)
                return response;
        }

        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> SendBestAccountAsync(HttpMethod method, string url, string ownerOrOrganization, IReadOnlyList<GitHubStoredAccount> accounts, CancellationToken cancellationToken)
    {
        var orderedAccounts = accounts
            .OrderByDescending(account => account.Kind == GitHubAccountConnectionKind.Organization && string.Equals(account.Organization, ownerOrOrganization, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(account => account.Kind == GitHubAccountConnectionKind.Organization)
            .ToList();

        foreach (var account in orderedAccounts)
        {
            var response = await SendAsync(method, url, account.Token, cancellationToken);
            if (response.IsSuccessStatusCode)
                return response;
            if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized))
                return response;
        }

        return new HttpResponseMessage(HttpStatusCode.Forbidden);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static GitHubRepositoryInfo MapRepository(RepositoryResponse repository)
    {
        return new GitHubRepositoryInfo
        {
            Owner = repository.Owner?.Login ?? "",
            Name = repository.Name ?? "",
            FullName = repository.FullName ?? "",
            HtmlUrl = repository.HtmlUrl ?? ""
        };
    }

    private static GitHubWorkflowRunInfo MapRun(string repositoryFullName, RunResponse run)
    {
        return new GitHubWorkflowRunInfo
        {
            Id = run.Id,
            RepositoryFullName = repositoryFullName,
            WorkflowName = run.Name ?? run.DisplayTitle ?? "Workflow",
            Branch = run.HeadBranch ?? "",
            Status = run.Status ?? "unknown",
            Conclusion = run.Conclusion ?? "unknown",
            StartedAt = run.RunStartedAt ?? run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            Actor = run.Actor?.Login ?? "",
            HtmlUrl = run.HtmlUrl ?? "",
            JobsUrl = run.JobsUrl ?? ""
        };
    }

    private static GitHubWorkflowJobInfo MapJob(JobResponse job)
    {
        return new GitHubWorkflowJobInfo
        {
            Id = job.Id,
            Name = job.Name ?? "",
            Status = job.Status ?? "unknown",
            Conclusion = job.Conclusion ?? "unknown",
            RunnerName = job.RunnerName ?? "",
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            HtmlUrl = job.HtmlUrl ?? ""
        };
    }
}

internal static class GitHubInfoExtensions
{
    public static GitHubRunnerInfo withPermission(this GitHubRunnerInfo info, string message) => new()
    {
        Id = info.Id,
        Name = info.Name,
        Status = info.Status,
        Busy = info.Busy,
        IsLocalRunnerBusy = info.IsLocalRunnerBusy,
        LocalActivityDescription = info.LocalActivityDescription,
        Labels = info.Labels,
        Owner = info.Owner,
        Repository = info.Repository,
        Group = info.Group,
        PermissionMessage = message
    };

    public static GitHubWorkflowRunInfo withRunningOnThisRunner(this GitHubWorkflowRunInfo run, bool value) => new()
    {
        Id = run.Id,
        RepositoryFullName = run.RepositoryFullName,
        WorkflowName = run.WorkflowName,
        Branch = run.Branch,
        Status = run.Status,
        Conclusion = run.Conclusion,
        StartedAt = run.StartedAt,
        UpdatedAt = run.UpdatedAt,
        Actor = run.Actor,
        HtmlUrl = run.HtmlUrl,
        JobsUrl = run.JobsUrl,
        IsRunningOnThisRunner = value
    };

    public static GitHubApiPermissionStatus withRunnerPermissionMessage(this GitHubApiPermissionStatus status, string message) => new()
    {
        HasWorkflowAccess = status.HasWorkflowAccess,
        HasRunnerAdminAccess = false,
        IsRateLimited = status.IsRateLimited,
        Message = message
    };
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed class RunnersResponse
{
    [JsonPropertyName("runners")]
    public List<RunnerResponse> Runners { get; set; } = [];
}

internal sealed class RunnerResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("busy")]
    public bool Busy { get; set; }
    [JsonPropertyName("labels")]
    public List<RunnerLabelResponse> Labels { get; set; } = [];
    [JsonPropertyName("runner_group_name")]
    public string? RunnerGroupName { get; set; }
    [JsonPropertyName("runner_group_id")]
    public long? RunnerGroupId { get; set; }
}

internal sealed class RunnerGroupResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }
}

internal sealed class RepositoriesResponse
{
    [JsonPropertyName("repositories")]
    public List<RepositoryResponse> Repositories { get; set; } = [];
}

internal sealed class RunnerLabelResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class RepositoryResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
    [JsonPropertyName("owner")]
    public OwnerResponse? Owner { get; set; }
}

internal sealed class OwnerResponse
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

internal sealed class RunsResponse
{
    [JsonPropertyName("workflow_runs")]
    public List<RunResponse> WorkflowRuns { get; set; } = [];
}

internal sealed class RunResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }
    [JsonPropertyName("head_branch")]
    public string? HeadBranch { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; set; }
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("run_started_at")]
    public DateTimeOffset? RunStartedAt { get; set; }
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
    [JsonPropertyName("jobs_url")]
    public string? JobsUrl { get; set; }
    [JsonPropertyName("actor")]
    public OwnerResponse? Actor { get; set; }
}

internal sealed class JobsResponse
{
    [JsonPropertyName("jobs")]
    public List<JobResponse> Jobs { get; set; } = [];
}

internal sealed class JobResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; set; }
    [JsonPropertyName("runner_name")]
    public string? RunnerName { get; set; }
    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}
