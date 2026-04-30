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

    [ObservableProperty] private GitHubAccountInfo account = new();
    [ObservableProperty] private IReadOnlyList<GitHubRunnerInfo> runners = [];
    [ObservableProperty] private IReadOnlyList<GitHubWorkflowRunInfo> workflowRuns = [];
    [ObservableProperty] private IReadOnlyList<GitHubWorkflowJobInfo> selectedJobs = [];
    [ObservableProperty] private GitHubWorkflowRunInfo? selectedRun;
    [ObservableProperty] private GitHubApiPermissionStatus permissionStatus = new();
    [ObservableProperty] private DateTimeOffset? lastRefreshTime;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string deviceCode = "";
    [ObservableProperty] private string verificationUri = "";
    [ObservableProperty] private string oauthClientId = "";

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
        _ = PollAsync(_pollingCts.Token);
    }

    public void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            Account = await _authService.GetAccountAsync(cancellationToken);
            if (!Account.IsSignedIn)
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
            Runners = snapshot.Runners;
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

    public async Task SignInAsync(Action<string> openUrl, CancellationToken cancellationToken = default)
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
            await _authService.CompleteDeviceFlowAsync(clientId, flow.DeviceCode, flow.Interval, cancellationToken);
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
        WorkflowRuns = [];
        Runners = [];
        SelectedJobs = [];
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
        await RefreshAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var interval = WorkflowRuns.Any(run => run.IsActive) ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(45);
            await Task.Delay(interval, cancellationToken);
            await RefreshAsync(cancellationToken);
        }
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
