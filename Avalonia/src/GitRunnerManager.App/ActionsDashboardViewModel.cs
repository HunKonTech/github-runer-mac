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
    [ObservableProperty] private IReadOnlyList<GitHubRepositoryInfo> repositories = [];
    [ObservableProperty] private IReadOnlyList<GitHubRunnerInfo> runners = [];
    [ObservableProperty] private IReadOnlyList<GitHubWorkflowRunInfo> allWorkflowRuns = [];
    [ObservableProperty] private IReadOnlyList<GitHubWorkflowRunInfo> workflowRuns = [];
    [ObservableProperty] private IReadOnlyList<GitHubWorkflowJobInfo> selectedJobs = [];
    [ObservableProperty] private GitHubRepositoryInfo? selectedRepository;
    [ObservableProperty] private GitHubWorkflowRunInfo? selectedRun;
    [ObservableProperty] private GitHubApiPermissionStatus permissionStatus = new();
    [ObservableProperty] private DateTimeOffset? lastRefreshTime;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isRealtimeRefreshActive;
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
        DiagnosticLog.Write("[Actions] ViewModel created (v2)");
    }

    partial void OnOauthClientIdChanged(string value)
    {
        _preferences.GitHubOAuthClientId = value.Trim();
    }

    partial void OnSelectedRunChanged(GitHubWorkflowRunInfo? value)
    {
        _ = LoadJobsAsync(value);
    }

    partial void OnSelectedRepositoryChanged(GitHubRepositoryInfo? value)
    {
        ApplyRepositoryFilter();
    }

    public void StartPolling()
    {
        DiagnosticLog.Write("[Actions] StartPolling called");
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
        DiagnosticLog.Write($"[Actions] RefreshAsync entered, IsLoading={IsLoading}");
        if (IsLoading)
        {
            DiagnosticLog.Write("[Actions] RefreshAsync skipped: already loading");
            return;
        }

        IsLoading = true;
        try
        {
            DiagnosticLog.Write("[Actions] RefreshAsync started");
            Accounts = await _authService.GetAccountsAsync(cancellationToken);
            DiagnosticLog.Write($"[Actions] GetAccountsAsync returned {Accounts.Count} account(s): {string.Join(", ", Accounts.Select(a => $"{a.Login}({a.Kind})"))}");
            if (Accounts.Count == 0)
            {
                var accountInfo = await _authService.GetAccountAsync(cancellationToken);
                DiagnosticLog.Write($"[Actions] GetAccountAsync (no accounts stored): IsSignedIn={accountInfo.IsSignedIn}, Login={accountInfo.Login}, Error={accountInfo.Error}");
                if (!accountInfo.IsSignedIn)
                {
                    WorkflowRuns = [];
                    AllWorkflowRuns = [];
                    Repositories = [];
                    Runners = [];
                    SelectedJobs = [];
                    Account = accountInfo;
                    StatusMessage = accountInfo.Error ?? T(LocalizationKeys.ActionsSignInRequired);
                    LastRefreshTime = DateTimeOffset.Now;
                    DiagnosticLog.Write("[Actions] Early return: no accounts and not signed in");
                    return;
                }
            }

            DiagnosticLog.Write("[Actions] Calling GetDashboardAsync...");
            var snapshot = await _actionsService.GetDashboardAsync(_store.Runners.Select(runner => runner.Profile).ToList(), cancellationToken);
            DiagnosticLog.Write($"[Actions] GetDashboardAsync returned: IsSignedIn={snapshot.Account.IsSignedIn}, Login={snapshot.Account.Login}, Repos={snapshot.Repositories.Count}, Runs={snapshot.WorkflowRuns.Count}, Runners={snapshot.Runners.Count}");
            Account = snapshot.Account;
            Repositories = snapshot.Repositories;
            Runners = MergeLocalRunnerState(snapshot.Runners);
            AllWorkflowRuns = snapshot.WorkflowRuns;
            ApplyRepositoryFilter();
            PermissionStatus = snapshot.PermissionStatus;
            LastRefreshTime = snapshot.RefreshedAt;
            StatusMessage = snapshot.PermissionStatus.HasRunnerAdminAccess ? "" : T(LocalizationKeys.ActionsRunnerAdminPermissionRequired);
            if (SelectedRun != null)
                SelectedRun = WorkflowRuns.FirstOrDefault(run => run.Id == SelectedRun.Id);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Write("[Actions] RefreshAsync cancelled");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("[Actions] RefreshAsync exception", ex);
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
        return SignInAsync(GitHubAccountConnectionKind.Organization, "", openUrl, cancellationToken);
    }

    private async Task SignInAsync(GitHubAccountConnectionKind kind, string organization, Action<string> openUrl, CancellationToken cancellationToken)
    {
        var clientId = OauthClientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            try
            {
                StatusMessage = T(LocalizationKeys.GitHubStatusSigningIn);
                await _authService.ImportExistingTokenAsync(kind, organization, cancellationToken);
                StatusMessage = T(LocalizationKeys.ActionsSignedIn);
                await RefreshAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusMessage = ex.Message.Contains("No GitHub token", StringComparison.OrdinalIgnoreCase)
                    ? T(LocalizationKeys.ActionsGitHubCliTokenRequired)
                    : T(LocalizationKeys.GitHubStatusError, ex.Message);
            }
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
        AllWorkflowRuns = [];
        Repositories = [];
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
        var activity = CurrentLocalRunner?.RunnerSnapshot.Activity ?? _store.RunnerSnapshot.Activity;
        var usage = CurrentLocalRunner?.ResourceUsage ?? _store.ResourceUsage;
        SelectedJobs = jobs.Select(job =>
        {
            var correlation = GitHubJobMatcher.Match(job, profiles, activity, usage);
            return new GitHubWorkflowJobInfo
            {
                Id = job.Id,
                Name = job.Name,
                Status = job.Status,
                Conclusion = job.Conclusion,
                RunnerName = job.RunnerName,
                RunnerGroupName = job.RunnerGroupName,
                Labels = job.Labels,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                HtmlUrl = job.HtmlUrl,
                Steps = job.Steps,
                IsRunningOnThisRunner = correlation.Confidence != GitHubCorrelationConfidence.Unknown,
                CorrelationConfidence = correlation.Confidence,
                CorrelationReason = correlation.Reason
            };
        }).ToList();
    }

    public GitHubActionsDiagnosticContext BuildDiagnosticContext()
    {
        var localRunner = CurrentLocalRunner;
        var currentJob = SelectedJobs
            .Where(job => job.CorrelationConfidence != GitHubCorrelationConfidence.Unknown)
            .OrderBy(job => job.CorrelationConfidence)
            .FirstOrDefault()
            ?? SelectedJobs.FirstOrDefault(job => job.Status is "in_progress" or "queued" or "waiting")
            ?? SelectedJobs.FirstOrDefault(job => job.Conclusion is "failure" or "timed_out")
            ?? SelectedJobs.FirstOrDefault();
        var confidence = currentJob?.CorrelationConfidence ?? SelectedRun?.CorrelationConfidence ?? GitHubCorrelationConfidence.Unknown;

        return new GitHubActionsDiagnosticContext
        {
            Account = Account,
            Run = SelectedRun,
            Jobs = SelectedJobs,
            CurrentJob = currentJob,
            LocalRunner = localRunner?.Profile,
            LocalRunnerStatus = localRunner?.RunnerSnapshot ?? _store.RunnerSnapshot,
            ResourceUsage = localRunner?.ResourceUsage ?? _store.ResourceUsage,
            LastRelevantRunnerLogLines = localRunner == null ? [] : GitHubActionsDiagnosticExporter.ReadRelevantRunnerLogLines(localRunner.Profile.RunnerDirectory),
            CorrelationConfidence = confidence,
            CorrelationReason = currentJob?.CorrelationReason ?? SelectedRun?.CorrelationReason ?? "",
            PermissionStatus = PermissionStatus,
            ExportedAt = DateTimeOffset.Now
        };
    }

    public string BuildMarkdownDiagnosticPrompt()
    {
        return GitHubActionsDiagnosticExporter.ToMarkdownPrompt(BuildDiagnosticContext());
    }

    public string BuildJsonDiagnosticContext()
    {
        return GitHubActionsDiagnosticExporter.ToJson(BuildDiagnosticContext());
    }

    private RunnerInstanceStore? CurrentLocalRunner => _store.Runners.FirstOrDefault(runner =>
            runner.Snapshot.StatusKind == RunnerStatusKind.Busy ||
            runner.ResourceUsage.IsJobActive ||
            runner.RunnerSnapshot.Activity.Kind == RunnerActivityKind.Busy)
        ?? _store.Runners.FirstOrDefault();

    private void ApplyRepositoryFilter()
    {
        if (SelectedRepository == null || string.IsNullOrWhiteSpace(SelectedRepository.FullName))
        {
            WorkflowRuns = AllWorkflowRuns;
            return;
        }

        WorkflowRuns = AllWorkflowRuns
            .Where(run => string.Equals(run.RepositoryFullName, SelectedRepository.FullName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (SelectedRun != null && WorkflowRuns.All(run => run.Id != SelectedRun.Id))
            SelectedRun = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        DiagnosticLog.Write("[Actions] PollAsync started");
        try
        {
            await RefreshLocalAndRemoteAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiagnosticLog.WriteException("[Actions] PollAsync first refresh failed", ex);
        }

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
        DiagnosticLog.Write($"[Actions] RefreshLocalAndRemoteAsync forceRemote={forceRemote}");
        var localRefresh = _store.TryRefreshNowAsync();
        var completed = await Task.WhenAny(localRefresh, Task.Delay(TimeSpan.FromSeconds(2))) == localRefresh;
        cancellationToken.ThrowIfCancellationRequested();
        if (completed)
        {
            var refreshed = await localRefresh;
            if (!refreshed)
                DiagnosticLog.Write("[Actions] Local refresh skipped: runner store is busy");
        }
        else
        {
            DiagnosticLog.Write("[Actions] Local refresh timed out; continuing remote refresh");
        }

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
