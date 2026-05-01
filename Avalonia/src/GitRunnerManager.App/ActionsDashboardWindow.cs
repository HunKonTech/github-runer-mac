using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;

namespace GitRunnerManager.App;

public sealed class ActionsDashboardWindow : Window
{
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
    private static readonly SolidColorBrush WindowBrush = Brush("#211f18");
    private static readonly SolidColorBrush CardBrush = Brush("#2a2822");
    private static readonly SolidColorBrush CardBorderBrush = Brush("#494234");
    private static readonly SolidColorBrush FieldBrush = Brush("#3d3a33");
    private static readonly SolidColorBrush PrimaryTextBrush = Brush("#eeeeeb");
    private static readonly SolidColorBrush SecondaryTextBrush = Brush("#aaa7a0");
    private static readonly SolidColorBrush AccentBrush = Brush("#2d7dca");
    private static readonly SolidColorBrush GreenBrush = Brush("#14e56f");
    private static readonly SolidColorBrush OrangeBrush = Brush("#ff9f0a");
    private static readonly SolidColorBrush RedBrush = Brush("#ff453a");
    private static readonly SolidColorBrush GrayBrush = Brush("#9a9aa0");

    private readonly ActionsDashboardViewModel _viewModel;
    private readonly ILocalizationService _localization;
    private bool _buildQueued;

    public ActionsDashboardWindow(ActionsDashboardViewModel viewModel, ILocalizationService localization)
    {
        _viewModel = viewModel;
        _localization = localization;
        Width = 1120;
        Height = 760;
        MinWidth = 920;
        MinHeight = 620;
        Background = WindowBrush;
        Title = T(LocalizationKeys.ActionsDashboardTitle);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Opened += (_, _) => _viewModel.StartPolling();
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
        };
        Build();
    }

    private void Build()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = WindowBrush,
            Margin = new Thickness(18)
        };
        root.Children.Add(BuildTopArea());
        Grid.SetRow(root.Children[^1], 0);

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("360,*,330"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 14, 0, 0)
        };
        columns.Children.Add(BuildRunnerSection());
        Grid.SetColumn(columns.Children[^1], 0);
        columns.Children.Add(BuildRunsSection());
        Grid.SetColumn(columns.Children[^1], 1);
        columns.Children.Add(BuildJobsSection());
        Grid.SetColumn(columns.Children[^1], 2);
        root.Children.Add(columns);
        Grid.SetRow(columns, 1);
        Content = root;
    }

    private Control BuildTopArea()
    {
        var panel = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = T(LocalizationKeys.ActionsDashboardTitle),
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button(LocalizationKeys.ButtonRefresh, async () => await _viewModel.RefreshRealtimeAsync(), true));
        header.Children.Add(buttons);
        Grid.SetColumn(buttons, 1);
        panel.Children.Add(header);

        var accountCard = Card(new StackPanel { Spacing = 8 });
        var content = (StackPanel)accountCard.Child!;
        content.Children.Add(Row(LocalizationKeys.UpdateStatusTitle, _viewModel.Account.IsSignedIn ? T(LocalizationKeys.GitHubSignedInAs, _viewModel.Account.Login ?? "") : T(LocalizationKeys.GitHubNotSignedIn), _viewModel.Account.IsSignedIn ? GreenBrush : GrayBrush));
        content.Children.Add(TextField(LocalizationKeys.GitHubOAuthClientIdTitle, _viewModel.OauthClientId, value => _viewModel.OauthClientId = value));
        content.Children.Add(TextField(LocalizationKeys.ActionsOrganizationLogin, _viewModel.OrganizationLogin, value => _viewModel.OrganizationLogin = value));
        var signInButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        signInButtons.Children.Add(Button(LocalizationKeys.ActionsSignInPersonal, async () => await _viewModel.SignInAsync(OpenUrl), true));
        signInButtons.Children.Add(Button(LocalizationKeys.ActionsSignInOrganization, async () => await _viewModel.SignInOrganizationAsync(OpenUrl)));
        content.Children.Add(signInButtons);
        content.Children.Add(Secondary(T(LocalizationKeys.ActionsConnectedAccounts), PrimaryTextBrush));
        foreach (var account in _viewModel.Accounts)
            content.Children.Add(AccountRow(account));
        if (!string.IsNullOrWhiteSpace(_viewModel.DeviceCode))
            content.Children.Add(Row(LocalizationKeys.GitHubDeviceCode, _viewModel.DeviceCode, OrangeBrush));
        content.Children.Add(Row(LocalizationKeys.ActionsLastRefresh, _viewModel.LastRefreshTime?.ToLocalTime().ToString("HH:mm:ss") ?? "-", GrayBrush));
        if (_viewModel.IsRealtimeRefreshActive)
            content.Children.Add(Secondary(T(LocalizationKeys.ActionsAutoRefreshActive), GreenBrush));
        if (!_viewModel.PermissionStatus.HasRunnerAdminAccess)
            content.Children.Add(Secondary(T(LocalizationKeys.ActionsRunnerAdminPermissionRequired), OrangeBrush));
        if (!string.IsNullOrWhiteSpace(_viewModel.StatusMessage))
            content.Children.Add(Secondary(_viewModel.StatusMessage, _viewModel.StatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ? RedBrush : SecondaryTextBrush));
        panel.Children.Add(accountCard);
        return panel;
    }

    private Control AccountRow(GitHubAccountConnection account)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var kind = account.Kind == GitHubAccountConnectionKind.Organization
            ? T(LocalizationKeys.ActionsOrganizationAccount)
            : T(LocalizationKeys.ActionsPersonalAccount);
        grid.Children.Add(Secondary($"{kind}: {account.DisplayName}", SecondaryTextBrush));
        var button = Button(LocalizationKeys.GitHubSignOutButton, async () => await _viewModel.SignOutAsync(account.Id));
        grid.Children.Add(button);
        Grid.SetColumn(button, 1);
        return grid;
    }

    private Control BuildRunnerSection()
    {
        var panel = Section(LocalizationKeys.ActionsRunnerSection);
        if (_viewModel.Runners.Count == 0)
            panel.Children.Add(Secondary(T(LocalizationKeys.RunnerStatusNoRunners), GrayBrush));

        panel.Children.Add(Card(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = T(LocalizationKeys.ActionsOrganizationRunnerGuideTitle), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush, TextWrapping = TextWrapping.Wrap },
                Secondary(T(LocalizationKeys.ActionsOrganizationRunnerGuideBody), SecondaryTextBrush)
            }
        }));

        foreach (var runner in _viewModel.Runners)
        {
            var card = Card(new StackPanel { Spacing = 5 });
            var content = (StackPanel)card.Child!;
            content.Children.Add(Row(LocalizationKeys.GitHubRunnerNameField, runner.Name, RunnerColor(runner)));
            content.Children.Add(Row(LocalizationKeys.GitHubRunnerScopeTitle, ScopeText(runner), GrayBrush));
            content.Children.Add(Row(LocalizationKeys.RunnerStatusTitle, RunnerStatusText(runner), RunnerColor(runner)));
            if (runner.IsLocalRunnerBusy)
                content.Children.Add(Row(LocalizationKeys.ActionsRunnerWorkingNow, runner.LocalActivityDescription, GreenBrush));
            else if (!string.IsNullOrWhiteSpace(runner.LocalActivityDescription))
                content.Children.Add(Row(LocalizationKeys.ActionsLocalRunnerActivity, runner.LocalActivityDescription, GrayBrush));
            content.Children.Add(Row(LocalizationKeys.RunnerLabelsTitle, runner.Labels.Count == 0 ? "-" : string.Join(", ", runner.Labels), GrayBrush));
            content.Children.Add(Row(LocalizationKeys.ActionsRunnerGroup, runner.Group?.Name ?? "-", GrayBrush));
            content.Children.Add(Row(LocalizationKeys.ActionsAllowedRepositories, AllowedRepositoriesText(runner), GrayBrush));
            panel.Children.Add(card);
        }

        return Scroll(panel);
    }

    private Control BuildRunsSection()
    {
        var panel = Section(LocalizationKeys.ActionsWorkflowRunsSection);
        if (_viewModel.IsLoading)
            panel.Children.Add(Secondary(T(LocalizationKeys.ActionsLoading), OrangeBrush));
        if (_viewModel.WorkflowRuns.Count == 0 && !_viewModel.IsLoading)
            panel.Children.Add(Secondary(T(LocalizationKeys.ActionsNoWorkflowRuns), GrayBrush));

        foreach (var run in _viewModel.WorkflowRuns)
        {
            var card = Card(new StackPanel { Spacing = 4 }, run.IsActive ? OrangeBrush : CardBorderBrush);
            var content = (StackPanel)card.Child!;
            content.Children.Add(new TextBlock { Text = $"{run.RepositoryFullName} · {run.WorkflowName}", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(Secondary($"{run.Branch} · {WorkflowStatusText(run.Status)} · {WorkflowConclusionText(run.Conclusion)}", run.IsActive ? OrangeBrush : SecondaryTextBrush));
            content.Children.Add(Secondary($"{FormatDate(run.StartedAt)} · {FormatDuration(run.Duration)} · {run.Actor}", SecondaryTextBrush));
            if (run.IsRunningOnThisRunner)
                content.Children.Add(Secondary(T(LocalizationKeys.ActionsRunningOnThisRunner), GreenBrush));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(Button(LocalizationKeys.ActionsSelectRun, () =>
            {
                _viewModel.SelectedRun = run;
                return Task.CompletedTask;
            }));
            if (!string.IsNullOrWhiteSpace(run.HtmlUrl))
                row.Children.Add(Button(LocalizationKeys.ActionsOpenInBrowser, () =>
                {
                    OpenUrl(run.HtmlUrl);
                    return Task.CompletedTask;
                }));
            content.Children.Add(row);
            panel.Children.Add(card);
        }

        return Scroll(panel);
    }

    private Control BuildJobsSection()
    {
        var panel = Section(LocalizationKeys.ActionsJobDetailSection);
        if (_viewModel.SelectedRun == null)
        {
            panel.Children.Add(Secondary(T(LocalizationKeys.ActionsSelectRunHint), GrayBrush));
            return Scroll(panel);
        }

        panel.Children.Add(Secondary($"{_viewModel.SelectedRun.RepositoryFullName} · {_viewModel.SelectedRun.WorkflowName}", PrimaryTextBrush));
        foreach (var job in _viewModel.SelectedJobs)
        {
            var card = Card(new StackPanel { Spacing = 4 }, job.IsRunningOnThisRunner ? GreenBrush : CardBorderBrush);
            var content = (StackPanel)card.Child!;
            content.Children.Add(new TextBlock { Text = job.Name, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(Secondary($"{WorkflowStatusText(job.Status)} · {WorkflowConclusionText(job.Conclusion)}", SecondaryTextBrush));
            content.Children.Add(Secondary($"{T(LocalizationKeys.GitHubRunnerNameField)}: {(string.IsNullOrWhiteSpace(job.RunnerName) ? "-" : job.RunnerName)}", job.IsRunningOnThisRunner ? GreenBrush : SecondaryTextBrush));
            content.Children.Add(Secondary($"{FormatDate(job.StartedAt)} - {FormatDate(job.CompletedAt)}", SecondaryTextBrush));
            if (!string.IsNullOrWhiteSpace(job.HtmlUrl))
                content.Children.Add(Button(LocalizationKeys.ActionsOpenInBrowser, () => { OpenUrl(job.HtmlUrl); return Task.CompletedTask; }));
            panel.Children.Add(card);
        }

        return Scroll(panel);
    }

    private StackPanel Section(string titleKey)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = T(titleKey),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush
        });
        return panel;
    }

    private Border Card(Control child, IBrush? borderBrush = null)
    {
        return new Border
        {
            Background = CardBrush,
            BorderBrush = borderBrush ?? CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = child
        };
    }

    private Control Row(string titleKey, string value, IBrush color)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        grid.Children.Add(new TextBlock { Text = T(titleKey), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = new TextBlock { Text = value, FontSize = 12, Foreground = color, TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(detail);
        Grid.SetColumn(detail, 1);
        return grid;
    }

    private TextBlock Secondary(string text, IBrush brush)
    {
        return new TextBlock { Text = text, FontSize = 12, Foreground = brush, TextWrapping = TextWrapping.Wrap };
    }

    private Control TextField(string titleKey, string value, Action<string> onChanged)
    {
        var box = new TextBox
        {
            Text = value,
            PlaceholderText = T(titleKey),
            Height = 34,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = CardBorderBrush
        };
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text))
                onChanged(box.Text ?? "");
        };
        return box;
    }

    private Button Button(string key, Func<Task> action, bool prominent = false)
    {
        var button = new Button
        {
            Content = T(key),
            Background = prominent ? AccentBrush : FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 6),
            FontWeight = FontWeight.SemiBold
        };
        button.Click += async (_, _) =>
        {
            await action();
            Build();
        };
        return button;
    }

    private ScrollViewer Scroll(Control content)
    {
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    private string ScopeText(GitHubRunnerInfo runner)
    {
        if (runner.Repository != null)
            return $"{T(LocalizationKeys.GitHubRunnerScopeRepository)} · {runner.Repository.FullName}";
        return $"{T(LocalizationKeys.GitHubRunnerScopeOrganization)} · {runner.Owner.Login}";
    }

    private string AllowedRepositoriesText(GitHubRunnerInfo runner)
    {
        if (!string.IsNullOrWhiteSpace(runner.PermissionMessage))
            return T(LocalizationKeys.ActionsPermissionRequired);
        if (runner.Group?.PermissionDenied == true)
            return T(LocalizationKeys.ActionsPermissionRequired);
        if (runner.Group?.AllowsAllRepositories == true)
            return T(LocalizationKeys.ActionsAllOrganizationRepositories);
        if (runner.Group?.SelectedRepositories.Count > 0)
            return string.Join(", ", runner.Group.SelectedRepositories.Select(repository => repository.FullName));
        return runner.Repository?.FullName ?? "-";
    }

    private string RunnerStatusText(GitHubRunnerInfo runner)
    {
        if (runner.Busy)
            return T(LocalizationKeys.RunnerStatusBusy);

        return runner.Status switch
        {
            "online" => T(LocalizationKeys.ActionsStatusOnline),
            "offline" => T(LocalizationKeys.ActionsStatusOffline),
            _ => T(LocalizationKeys.ActionsStatusUnknown)
        };
    }

    private string WorkflowStatusText(string status)
    {
        return status switch
        {
            "queued" => T(LocalizationKeys.ActionsStatusQueued),
            "in_progress" => T(LocalizationKeys.ActionsStatusInProgress),
            "completed" => T(LocalizationKeys.ActionsStatusCompleted),
            "waiting" => T(LocalizationKeys.ActionsStatusWaiting),
            "requested" => T(LocalizationKeys.ActionsStatusRequested),
            _ => T(LocalizationKeys.ActionsStatusUnknown)
        };
    }

    private string WorkflowConclusionText(string conclusion)
    {
        return conclusion switch
        {
            "success" => T(LocalizationKeys.ActionsConclusionSuccess),
            "failure" => T(LocalizationKeys.ActionsConclusionFailure),
            "cancelled" => T(LocalizationKeys.ActionsConclusionCancelled),
            "skipped" => T(LocalizationKeys.ActionsConclusionSkipped),
            "timed_out" => T(LocalizationKeys.ActionsConclusionTimedOut),
            "" or "unknown" => T(LocalizationKeys.ActionsStatusUnknown),
            _ => conclusion
        };
    }

    private static IBrush RunnerColor(GitHubRunnerInfo runner)
    {
        if (runner.Busy || runner.IsLocalRunnerBusy)
            return OrangeBrush;
        return runner.Status == "online" ? GreenBrush : runner.Status == "offline" ? RedBrush : GrayBrush;
    }

    private static string FormatDate(DateTimeOffset? value) => value?.ToLocalTime().ToString("MM-dd HH:mm") ?? "-";

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration == null)
            return "-";
        return duration.Value.TotalHours >= 1 ? $"{duration.Value.TotalHours:0.0}h" : $"{duration.Value.TotalMinutes:0}m";
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_buildQueued)
            return;
        _buildQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _buildQueued = false;
            Build();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private string T(string key, params object[] args) => args.Length == 0 ? _localization.Get(key) : _localization.Get(key, args);

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
