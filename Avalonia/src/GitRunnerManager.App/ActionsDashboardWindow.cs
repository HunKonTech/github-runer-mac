using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private readonly Action _openGitHubAccountSettings;
    private bool _buildQueued;

    public ActionsDashboardWindow(ActionsDashboardViewModel viewModel, ILocalizationService localization, Action openGitHubAccountSettings)
    {
        _viewModel = viewModel;
        _localization = localization;
        _openGitHubAccountSettings = openGitHubAccountSettings;
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
        buttons.Children.Add(ButtonKey(LocalizationKeys.ButtonRefresh, async () => await _viewModel.RefreshRealtimeAsync(), true));
        buttons.Children.Add(ButtonKey(LocalizationKeys.ActionsCopyForLlm, CopyMarkdownAsync, true));
        buttons.Children.Add(ButtonKey(LocalizationKeys.ActionsCopyJson, CopyJsonAsync));
        header.Children.Add(buttons);
        Grid.SetColumn(buttons, 1);
        panel.Children.Add(header);

        var accountCard = Card(new StackPanel { Spacing = 8 });
        var content = (StackPanel)accountCard.Child!;
        content.Children.Add(Row(LocalizationKeys.UpdateStatusTitle, _viewModel.Account.IsSignedIn ? T(LocalizationKeys.GitHubSignedInAs, _viewModel.Account.Login ?? "") : T(LocalizationKeys.GitHubNotSignedIn), _viewModel.Account.IsSignedIn ? GreenBrush : GrayBrush));
        if (!_viewModel.Account.IsSignedIn)
            content.Children.Add(ButtonKey(LocalizationKeys.ActionsOpenGitHubAccountSettings, () =>
            {
                _openGitHubAccountSettings();
                return Task.CompletedTask;
            }, true));
        else if (_viewModel.Accounts.Count > 0)
            content.Children.Add(Secondary($"{T(LocalizationKeys.ActionsConnectedAccounts)}: {string.Join(", ", _viewModel.Accounts.Select(account => account.DisplayName))}", SecondaryTextBrush));
        content.Children.Add(Row(LocalizationKeys.ActionsLastRefresh, _viewModel.LastRefreshTime?.ToLocalTime().ToString("HH:mm:ss") ?? "-", GrayBrush));
        if (_viewModel.IsRealtimeRefreshActive)
            content.Children.Add(Secondary(T(LocalizationKeys.ActionsAutoRefreshActive), GreenBrush));
        content.Children.Add(Card(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = T(LocalizationKeys.ActionsPermissionsTitle), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush, TextWrapping = TextWrapping.Wrap },
                Secondary(T(LocalizationKeys.ActionsPermissionsBody), SecondaryTextBrush),
                Secondary(PermissionAvailabilityText(), PermissionColor())
            }
        }));
        if (!_viewModel.PermissionStatus.HasRunnerAdminAccess)
            content.Children.Add(Secondary(T(LocalizationKeys.ActionsRunnerAdminPermissionRequired), OrangeBrush));
        if (!string.IsNullOrWhiteSpace(_viewModel.StatusMessage))
            content.Children.Add(Secondary(_viewModel.StatusMessage, _viewModel.StatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ? RedBrush : SecondaryTextBrush));
        panel.Children.Add(accountCard);
        return panel;
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
        panel.Children.Add(BuildRepositoryFilter());
        if (_viewModel.IsLoading)
            panel.Children.Add(Secondary(T(LocalizationKeys.ActionsLoading), OrangeBrush));
        if (_viewModel.WorkflowRuns.Count == 0 && !_viewModel.IsLoading)
            panel.Children.Add(Secondary(T(LocalizationKeys.ActionsNoWorkflowRuns), GrayBrush));

        foreach (var run in _viewModel.WorkflowRuns)
        {
            var card = Card(new StackPanel { Spacing = 4 }, run.IsActive ? OrangeBrush : CardBorderBrush);
            card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            card.PointerPressed += (_, _) => _viewModel.SelectedRun = run;
            var content = (StackPanel)card.Child!;
            content.Children.Add(new TextBlock { Text = $"{run.RepositoryFullName} · {run.WorkflowName}", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(Secondary($"{run.Branch} · {WorkflowStatusText(run.Status)} · {WorkflowConclusionText(run.Conclusion)}", run.IsActive ? OrangeBrush : SecondaryTextBrush));
            content.Children.Add(Secondary($"#{run.RunNumber} · {T(LocalizationKeys.ActionsCorrelationConfidence)}: {CorrelationText(run.CorrelationConfidence)}", run.IsRunningOnThisRunner ? GreenBrush : SecondaryTextBrush));
            content.Children.Add(Secondary($"{FormatDate(run.StartedAt)} · {FormatDuration(run.Duration)} · {run.Actor}", SecondaryTextBrush));
            if (run.IsRunningOnThisRunner)
                content.Children.Add(Secondary(T(LocalizationKeys.ActionsRunningOnThisRunner), GreenBrush));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(ButtonKey(LocalizationKeys.ActionsSelectRun, () =>
            {
                _viewModel.SelectedRun = run;
                return Task.CompletedTask;
            }));
            if (!string.IsNullOrWhiteSpace(run.HtmlUrl))
                row.Children.Add(ButtonKey(LocalizationKeys.ActionsOpenInBrowser, () =>
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
        var exportRow = new WrapPanel();
        exportRow.Children.Add(ButtonKey(LocalizationKeys.ActionsCopyForLlm, CopyMarkdownAsync, true));
        exportRow.Children.Add(ButtonKey(LocalizationKeys.ActionsCopyJson, CopyJsonAsync));
        exportRow.Children.Add(ButtonKey(LocalizationKeys.ActionsExportMarkdown, ExportMarkdownAsync));
        exportRow.Children.Add(ButtonKey(LocalizationKeys.ActionsExportJson, ExportJsonAsync));
        panel.Children.Add(exportRow);
        foreach (var job in _viewModel.SelectedJobs)
        {
            var card = Card(new StackPanel { Spacing = 4 }, job.IsRunningOnThisRunner ? GreenBrush : CardBorderBrush);
            var content = (StackPanel)card.Child!;
            content.Children.Add(new TextBlock { Text = job.Name, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(Secondary($"{WorkflowStatusText(job.Status)} · {WorkflowConclusionText(job.Conclusion)}", SecondaryTextBrush));
            content.Children.Add(Secondary($"{T(LocalizationKeys.GitHubRunnerNameField)}: {(string.IsNullOrWhiteSpace(job.RunnerName) ? "-" : job.RunnerName)}", job.IsRunningOnThisRunner ? GreenBrush : SecondaryTextBrush));
            content.Children.Add(Secondary($"{T(LocalizationKeys.ActionsCorrelationConfidence)}: {CorrelationText(job.CorrelationConfidence)}", job.IsRunningOnThisRunner ? GreenBrush : SecondaryTextBrush));
            if (!string.IsNullOrWhiteSpace(job.CorrelationReason))
                content.Children.Add(Secondary(job.CorrelationReason, SecondaryTextBrush));
            content.Children.Add(Secondary($"{FormatDate(job.StartedAt)} - {FormatDate(job.CompletedAt)}", SecondaryTextBrush));
            if (job.Steps.Count > 0)
                content.Children.Add(Secondary($"{T(LocalizationKeys.ActionsSteps)}: {StepSummary(job)}", SecondaryTextBrush));
            if (!string.IsNullOrWhiteSpace(job.HtmlUrl))
                content.Children.Add(ButtonKey(LocalizationKeys.ActionsOpenInBrowser, () => { OpenUrl(job.HtmlUrl); return Task.CompletedTask; }));
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

    private Control BuildRepositoryFilter()
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(Secondary(T(LocalizationKeys.ActionsRepositoryFilter), PrimaryTextBrush));
        var row = new WrapPanel();
        row.Children.Add(FilterButton(T(LocalizationKeys.ActionsAllRepositories), _viewModel.SelectedRepository == null, () =>
        {
            _viewModel.SelectedRepository = null;
            return Task.CompletedTask;
        }));

        foreach (var repository in _viewModel.Repositories.Take(12))
        {
            row.Children.Add(FilterButton(repository.FullName, _viewModel.SelectedRepository?.FullName == repository.FullName, () =>
            {
                _viewModel.SelectedRepository = repository;
                return Task.CompletedTask;
            }));
        }

        panel.Children.Add(row);
        return panel;
    }

    private Button FilterButton(string text, bool selected, Func<Task> action)
    {
        var button = ButtonText(text, action);
        button.Background = selected ? AccentBrush : FieldBrush;
        return button;
    }

    private Button ButtonKey(string key, Func<Task> action, bool prominent = false)
    {
        return ButtonText(T(key), action, prominent);
    }

    private Button ButtonText(string text, Func<Task> action, bool prominent = false)
    {
        var button = new Button
        {
            Content = text,
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

    private static string CorrelationText(GitHubCorrelationConfidence confidence)
    {
        return confidence switch
        {
            GitHubCorrelationConfidence.Exact => "Exact",
            GitHubCorrelationConfidence.Probable => "Probable",
            GitHubCorrelationConfidence.Possible => "Possible",
            _ => "Unknown"
        };
    }

    private string PermissionAvailabilityText()
    {
        var status = _viewModel.PermissionStatus;
        var parts = new[]
        {
            $"Actions: {(status.HasWorkflowAccess ? T(LocalizationKeys.BooleanYes) : T(LocalizationKeys.BooleanNo))}",
            $"Repo runners: {(status.HasRepositoryRunnerAccess ? T(LocalizationKeys.BooleanYes) : T(LocalizationKeys.BooleanNo))}",
            $"Org runners: {(status.HasOrganizationRunnerAccess ? T(LocalizationKeys.BooleanYes) : T(LocalizationKeys.BooleanNo))}",
            $"Rate limit: {(status.IsRateLimited ? T(LocalizationKeys.BooleanYes) : T(LocalizationKeys.BooleanNo))}"
        };
        return string.Join(" · ", parts);
    }

    private IBrush PermissionColor()
    {
        var status = _viewModel.PermissionStatus;
        return status.HasWorkflowAccess && !status.IsRateLimited ? GreenBrush : OrangeBrush;
    }

    private static string StepSummary(GitHubWorkflowJobInfo job)
    {
        return string.Join(", ", job.Steps.Take(6).Select(step => $"{step.Name} {step.Status}/{step.Conclusion}".Trim()));
    }

    private async Task CopyMarkdownAsync()
    {
        if (Clipboard != null)
            await Clipboard.SetTextAsync(_viewModel.BuildMarkdownDiagnosticPrompt());
        _viewModel.StatusMessage = T(LocalizationKeys.ActionsCopied);
    }

    private async Task CopyJsonAsync()
    {
        if (Clipboard != null)
            await Clipboard.SetTextAsync(_viewModel.BuildJsonDiagnosticContext());
        _viewModel.StatusMessage = T(LocalizationKeys.ActionsCopied);
    }

    private Task ExportMarkdownAsync()
    {
        return ExportTextAsync("gitrunnermanager-actions-diagnostic.md", _viewModel.BuildMarkdownDiagnosticPrompt());
    }

    private Task ExportJsonAsync()
    {
        return ExportTextAsync("gitrunnermanager-actions-diagnostic.json", _viewModel.BuildJsonDiagnosticContext());
    }

    private async Task ExportTextAsync(string suggestedName, string content)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName
        });
        if (file == null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        _viewModel.StatusMessage = T(LocalizationKeys.ActionsExported);
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
