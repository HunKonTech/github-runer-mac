using CommunityToolkit.Mvvm.ComponentModel;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.App;

public sealed partial class ActionsDashboardViewModel : ObservableObject, IDisposable
{
    private readonly RunnerTrayStore _store;
    private readonly IPreferencesStore _preferences;
    private readonly IGitHubAuthService _authService;
    private readonly IGitHubActionsService _actionsService;
    private readonly ILocalizationService _localization;
    private CancellationTokenSource? _pollingCts;
    private DateTimeOffset _lastApiRefresh = DateTimeOffset.MinValue;

    [ObservableProperty] private GitHubAccountInfo account = new();
    [ObservableProperty] private IReadOnlyList<GitHubAccountConnection> accounts = [];
    [ObservableProperty] private IReadOnlyList<GitHubRunnerInfo> runners = [];
    [ObservableProperty] private IReadOnlyList<GitHubWorkflowRunInfo> workflowRuns = [];
    [ObservableProperty] private IReadOnlyList<GitHubWorkflowJobInfo> selectedJobs = [];
    [ObservableProperty] private GitHubWorkflowRunInfo? selectedRun;
    [ObservableProperty] private GitHubApiPermissionStatus permissionStatus = new();
    [ObservableProperty] private DateTimeOffset? lastRefreshTime;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isRealtimeRefreshActive;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string deviceCode = "";
    [ObservableProperty] private string verificationUri = "";
    [ObservableProperty] private string oauthClientId = "";
    [ObservableProperty] private string organizationLogin = "";

    public ActionsDashboardViewModel(
        RunnerTrayStore store,
        IPreferencesStore preferences,
        IGitHubAuthService authService,
        IGitHubActionsService actionsService,
        ILocalizationService localization)
    {
        _store = store;
        _preferences = preferences;
        _authService = authService;
        _actionsService = actionsService;
        _localization = localization;
        oauthClientId = preferences.GitHubOAuthClientId;
    }

    partial void OnOauthClientIdChanged(string value)
    {
        _preferences.GitHubOAuthClientId = value.Trim();
    }

    partial void OnSelectedRunChanged(GitHubWorkflowRunInfo? value)
    {
        _ = LoadJobsAsync(value);
    }

    public void StartPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = new CancellationTokenSource();
        IsRealtimeRefreshActive = true;
        _ = PollAsync(_pollingCts.Token);
    }

    public void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;
        IsRealtimeRefreshActive = false;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            Account = await _authService.GetAccountAsync(cancellationToken);
            Accounts = await _authService.GetAccountsAsync(cancellationToken);
            if (!Account.IsSignedIn || Accounts.Count == 0)
            {
                WorkflowRuns = [];
                Runners = [];
                SelectedJobs = [];
                StatusMessage = Account.Error ?? T(LocalizationKeys.ActionsSignInRequired);
                LastRefreshTime = DateTimeOffset.Now;
                return;
            }

            var snapshot = await _actionsService.GetDashboardAsync(_store.Runners.Select(runner => runner.Profile).ToList(), cancellationToken);
            Account = snapshot.Account;
            Runners = MergeLocalRunnerState(snapshot.Runners);
            WorkflowRuns = snapshot.WorkflowRuns;
            PermissionStatus = snapshot.PermissionStatus;
            LastRefreshTime = snapshot.RefreshedAt;
            StatusMessage = snapshot.PermissionStatus.HasRunnerAdminAccess ? "" : T(LocalizationKeys.ActionsRunnerAdminPermissionRequired);
            if (SelectedRun != null)
                SelectedRun = WorkflowRuns.FirstOrDefault(run => run.Id == SelectedRun.Id);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = T(LocalizationKeys.GitHubStatusError, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task RefreshRealtimeAsync(CancellationToken cancellationToken = default)
    {
        return RefreshLocalAndRemoteAsync(true, cancellationToken);
    }

    public Task SignInAsync(Action<string> openUrl, CancellationToken cancellationToken = default)
    {
        return SignInAsync(GitHubAccountConnectionKind.Personal, "", openUrl, cancellationToken);
    }

    public Task SignInOrganizationAsync(Action<string> openUrl, CancellationToken cancellationToken = default)
    {
        return SignInAsync(GitHubAccountConnectionKind.Organization, OrganizationLogin, openUrl, cancellationToken);
    }

    private async Task SignInAsync(GitHubAccountConnectionKind kind, string organization, Action<string> openUrl, CancellationToken cancellationToken)
    {
        var clientId = OauthClientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            StatusMessage = T(LocalizationKeys.ActionsOAuthClientIdRequired);
            return;
        }

        try
        {
            StatusMessage = T(LocalizationKeys.GitHubStatusSigningIn);
            var flow = await _authService.StartDeviceFlowAsync(clientId, cancellationToken);
            DeviceCode = flow.UserCode;
            VerificationUri = string.IsNullOrWhiteSpace(flow.VerificationUriComplete) ? flow.VerificationUri : flow.VerificationUriComplete;
            openUrl(VerificationUri);
            await _authService.CompleteDeviceFlowAsync(clientId, flow.DeviceCode, flow.Interval, kind, organization, cancellationToken);
            DeviceCode = "";
            VerificationUri = "";
            StatusMessage = T(LocalizationKeys.ActionsSignedIn);
            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = T(LocalizationKeys.GitHubStatusError, ex.Message);
        }
    }

    public async Task SignOutAsync()
    {
        await _authService.SignOutAsync();
        Account = new GitHubAccountInfo();
        Accounts = [];
        WorkflowRuns = [];
        Runners = [];
        SelectedJobs = [];
        StatusMessage = T(LocalizationKeys.ActionsSignedOut);
    }

    public async Task SignOutAsync(string accountId)
    {
        await _authService.SignOutAsync(accountId);
        Accounts = await _authService.GetAccountsAsync();
        if (Accounts.Count == 0)
            Account = new GitHubAccountInfo();
        await RefreshAsync();
        StatusMessage = T(LocalizationKeys.ActionsSignedOut);
    }

    private async Task LoadJobsAsync(GitHubWorkflowRunInfo? run)
    {
        if (run == null)
        {
            SelectedJobs = [];
            return;
        }

        var jobs = await _actionsService.GetJobsAsync(run);
        var profiles = _store.Runners.Select(runner => runner.Profile).ToList();
        SelectedJobs = jobs.Select(job => new GitHubWorkflowJobInfo
        {
            Id = job.Id,
            Name = job.Name,
            Status = job.Status,
            Conclusion = job.Conclusion,
            RunnerName = job.RunnerName,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            HtmlUrl = job.HtmlUrl,
            IsRunningOnThisRunner = GitHubJobMatcher.IsRunningOnLocalRunner(job, profiles)
        }).ToList();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        await RefreshLocalAndRemoteAsync(true, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var interval = IsAnyLocalRunnerBusy || WorkflowRuns.Any(run => run.IsActive)
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(15);
            await Task.Delay(interval, cancellationToken);
            await RefreshLocalAndRemoteAsync(false, cancellationToken);
        }
    }

    private bool IsAnyLocalRunnerBusy => _store.Runners.Any(runner =>
        runner.Snapshot.StatusKind == RunnerStatusKind.Busy ||
        runner.ResourceUsage.IsJobActive ||
        runner.RunnerSnapshot.Activity.Kind == RunnerActivityKind.Busy);

    private async Task RefreshLocalAndRemoteAsync(bool forceRemote, CancellationToken cancellationToken)
    {
        await _store.RefreshNowAsync();
        Runners = MergeLocalRunnerState(Runners);

        var now = DateTimeOffset.Now;
        var remoteInterval = IsAnyLocalRunnerBusy || WorkflowRuns.Any(run => run.IsActive)
            ? TimeSpan.FromSeconds(10)
            : TimeSpan.FromSeconds(45);
        if (forceRemote || now - _lastApiRefresh >= remoteInterval)
        {
            _lastApiRefresh = now;
            await RefreshAsync(cancellationToken);
        }
    }

    private IReadOnlyList<GitHubRunnerInfo> MergeLocalRunnerState(IReadOnlyList<GitHubRunnerInfo> githubRunners)
    {
        return githubRunners.Select(githubRunner =>
        {
            var localRunner = _store.Runners.FirstOrDefault(runner =>
                GitHubJobMatcher.Matches(githubRunner.Name, runner.Profile) ||
                string.Equals(runner.Profile.DisplayName, githubRunner.Name, StringComparison.OrdinalIgnoreCase));
            if (localRunner == null)
                return githubRunner;

            var activity = localRunner.RunnerSnapshot.Activity;
            var isBusy = localRunner.Snapshot.StatusKind == RunnerStatusKind.Busy ||
                localRunner.ResourceUsage.IsJobActive ||
                activity.Kind == RunnerActivityKind.Busy;

            return new GitHubRunnerInfo
            {
                Id = githubRunner.Id,
                Name = githubRunner.Name,
                Status = githubRunner.Status,
                Busy = githubRunner.Busy,
                IsLocalRunnerBusy = isBusy,
                LocalActivityDescription = activity.Description,
                Labels = githubRunner.Labels,
                Owner = githubRunner.Owner,
                Repository = githubRunner.Repository,
                Group = githubRunner.Group,
                PermissionMessage = githubRunner.PermissionMessage
            };
        }).ToList();
    }

    public void Dispose()
    {
        StopPolling();
    }

    private string T(string key, params object[] args)
    {
        return args.Length == 0 ? _localization.Get(key) : _localization.Get(key, args);
    }
}
