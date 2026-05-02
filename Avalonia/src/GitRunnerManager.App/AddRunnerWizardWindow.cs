using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.App;

public sealed class AddRunnerWizardWindow : Window
{
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
    private static readonly SolidColorBrush WindowBrush = Brush("#211f18");
    private static readonly SolidColorBrush SidebarBorderBrush = Brush("#494234");
    private static readonly SolidColorBrush PrimaryTextBrush = Brush("#eeeeeb");
    private static readonly SolidColorBrush SecondaryTextBrush = Brush("#aaa7a0");
    private static readonly SolidColorBrush FieldBrush = Brush("#3d3a33");
    private static readonly SolidColorBrush WarningBrush = Brush("#f2c66d");

    private readonly RunnerTrayStore _store;
    private readonly ILocalizationService _localization;
    private readonly IGitHubService _gitHubService;
    private readonly IRunnerFolderValidator _folderValidator;
    private readonly IPreferencesStore _preferences;
    private readonly RunnerSetupDraft _draft = new();
    private readonly List<GitHubOwnerInfo> _organizations = [];
    private readonly List<GitHubRepositoryInfo> _repositories = [];
    private GitHubAccountSnapshot _account = new();
    private GitHubPermissionEvaluation _permissions = new();
    private int _step;
    private bool _isBusy;
    private string _status = "";

    public AddRunnerWizardWindow(
        RunnerTrayStore store,
        ILocalizationService localization,
        IGitHubService gitHubService,
        IRunnerFolderValidator folderValidator,
        IPreferencesStore preferences)
    {
        _store = store;
        _localization = localization;
        _gitHubService = gitHubService;
        _folderValidator = folderValidator;
        _preferences = preferences;
        Width = 760;
        Height = 680;
        MinWidth = 620;
        MinHeight = 520;
        Title = T(LocalizationKeys.AddRunnerWizardTitle);
        Background = WindowBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _draft.RunnerName = RunnerSetupValidator.CreateDefaultRunnerName();
        _draft.Labels = RunnerSetupValidator.SuggestedLabels();
        _draft.RunnerDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "actions-runner-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));
        Opened += async (_, _) => await LoadInitialAsync();
    }

    private async Task LoadInitialAsync()
    {
        _isBusy = true;
        Build();
        try
        {
            _account = await _gitHubService.GetAccountAsync();
            _permissions = await _gitHubService.GetPermissionEvaluationAsync();
            _draft.AccountLogin = _account.Login ?? "";
            if (_account.IsSignedIn)
            {
                _draft.OwnerOrOrg = _account.Login ?? "";
                _organizations.Clear();
                _organizations.AddRange(await _gitHubService.GetOrganizationsAsync());
                await LoadRepositoriesAsync();
            }
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        finally
        {
            _isBusy = false;
            Build();
        }
    }

    private async Task LoadRepositoriesAsync()
    {
        _repositories.Clear();
        if (_draft.Scope == GitHubRunnerScope.Organization)
            _repositories.AddRange(await _gitHubService.GetOrganizationRepositoriesAsync(_draft.OwnerOrOrg));
        else
            _repositories.AddRange(await _gitHubService.GetUserRepositoriesAsync());

        _draft.SelectedRepositories = _draft.SelectedRepositories
            .Where(selected => _repositories.Any(repository => repository.FullName == selected.FullName))
            .ToList();
    }

    private void Build()
    {
        var content = new DockPanel();
        var buttons = BuildButtons();
        DockPanel.SetDock(buttons, Dock.Bottom);
        content.Children.Add(buttons);

        var body = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14
        };
        body.Children.Add(new TextBlock
        {
            Text = T(LocalizationKeys.AddRunnerWizardTitle),
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush
        });
        body.Children.Add(Secondary($"{T(LocalizationKeys.AddRunnerWizardStep)} {_step + 1}/5"));
        body.Children.Add(Separator());
        if (_isBusy)
            body.Children.Add(Secondary(T(LocalizationKeys.ActionsLoading)));
        else
            body.Children.Add(BuildStep());
        if (!string.IsNullOrWhiteSpace(_status))
            body.Children.Add(Secondary(_status, WarningBrush));

        content.Children.Add(new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
        Content = content;
    }

    private Control BuildStep()
    {
        return _step switch
        {
            1 => BuildRepositoryAccessStep(),
            2 => BuildDetailsStep(),
            3 => BuildFolderStep(),
            4 => BuildReviewStep(),
            _ => BuildScopeStep()
        };
    }

    private Control BuildScopeStep()
    {
        var panel = Stack();
        panel.Children.Add(Heading(LocalizationKeys.AddRunnerScopeTitle));
        panel.Children.Add(Secondary(_account.IsSignedIn
            ? T(LocalizationKeys.GitHubSignedInAs, _account.Login ?? "")
            : T(LocalizationKeys.RunnerGitHubAccountRequired)));
        panel.Children.Add(Secondary(_permissions.Message));
        panel.Children.Add(Combo(LocalizationKeys.GitHubRunnerScopeTitle, new[] { T(LocalizationKeys.AddRunnerPersonalScope), T(LocalizationKeys.GitHubRunnerScopeOrganization) }, _draft.Scope == GitHubRunnerScope.Organization ? 1 : 0, async index =>
        {
            _draft.Scope = index == 1 ? GitHubRunnerScope.Organization : GitHubRunnerScope.Repository;
            _draft.OwnerOrOrg = _draft.Scope == GitHubRunnerScope.Organization ? _organizations.FirstOrDefault()?.Login ?? "" : _account.Login ?? "";
            _draft.SelectedRepositories.Clear();
            await LoadRepositoriesAsync();
            Build();
        }));

        if (_draft.Scope == GitHubRunnerScope.Organization)
        {
            panel.Children.Add(Combo(LocalizationKeys.ActionsOrganizationLogin, _organizations.Select(org => org.Login).DefaultIfEmpty(T(LocalizationKeys.AddRunnerNoOrganizations)).ToArray(), Math.Max(0, _organizations.FindIndex(org => org.Login == _draft.OwnerOrOrg)), async index =>
            {
                if (index >= 0 && index < _organizations.Count)
                {
                    _draft.OwnerOrOrg = _organizations[index].Login;
                    _draft.SelectedRepositories.Clear();
                    await LoadRepositoriesAsync();
                    Build();
                }
            }));
        }
        return panel;
    }

    private Control BuildRepositoryAccessStep()
    {
        var panel = Stack();
        panel.Children.Add(Heading(LocalizationKeys.AddRunnerRepositoryAccessTitle));
        panel.Children.Add(Combo(LocalizationKeys.AddRunnerRepositoryAccessTitle, new[] { T(LocalizationKeys.ActionsAllRepositories), T(LocalizationKeys.AddRunnerSelectedRepositories) }, _draft.RepositoryAccessMode == RunnerRepositoryAccessMode.SelectedRepositories ? 1 : 0, index =>
        {
            _draft.RepositoryAccessMode = index == 1 ? RunnerRepositoryAccessMode.SelectedRepositories : RunnerRepositoryAccessMode.AllRepositories;
            Build();
            return Task.CompletedTask;
        }));
        if (_draft.Scope == GitHubRunnerScope.Repository || _draft.RepositoryAccessMode == RunnerRepositoryAccessMode.SelectedRepositories)
            panel.Children.Add(BuildRepositoryPicker());
        panel.Children.Add(Secondary(_draft.Scope == GitHubRunnerScope.Repository
            ? T(LocalizationKeys.AddRunnerUserScopeLimitation)
            : _draft.RepositoryAccessMode == RunnerRepositoryAccessMode.SelectedRepositories
                ? T(LocalizationKeys.AddRunnerOrgSelectedLimitation)
                : T(LocalizationKeys.AddRunnerOrgAllExplanation)));
        return panel;
    }

    private Control BuildRepositoryPicker()
    {
        var list = Stack();
        if (_repositories.Count == 0)
        {
            list.Children.Add(Secondary(T(LocalizationKeys.AddRunnerNoRepositories)));
            return list;
        }

        foreach (var repository in _repositories.Take(80))
        {
            var selected = _draft.SelectedRepositories.Any(item => item.FullName == repository.FullName);
            var box = new CheckBox
            {
                Content = repository.FullName,
                IsChecked = selected,
                Foreground = PrimaryTextBrush,
                FontSize = 14
            };
            box.IsCheckedChanged += (_, _) =>
            {
                _draft.SelectedRepositories.RemoveAll(item => item.FullName == repository.FullName);
                if (box.IsChecked == true)
                {
                    _draft.SelectedRepositories.Add(repository);
                    _draft.OwnerOrOrg = repository.Owner;
                }
                Build();
            };
            list.Children.Add(box);
        }
        return list;
    }

    private Control BuildDetailsStep()
    {
        var panel = Stack();
        panel.Children.Add(Heading(LocalizationKeys.AddRunnerDetailsTitle));
        panel.Children.Add(TextField(LocalizationKeys.GitHubRunnerNameField, _draft.RunnerName, value => _draft.RunnerName = value.Trim()));
        panel.Children.Add(TextField(LocalizationKeys.RunnerLabelsTitle, string.Join(", ", _draft.Labels), value => _draft.Labels = RunnerSetupValidator.NormalizeLabels(value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))));
        var validation = RunnerSetupValidator.ValidateDraft(_draft, _store.Runners.Select(runner => runner.Profile).ToList());
        foreach (var warning in validation.Warnings)
            panel.Children.Add(Secondary(warning, WarningBrush));
        return panel;
    }

    private Control BuildFolderStep()
    {
        var panel = Stack();
        panel.Children.Add(Heading(LocalizationKeys.AddRunnerFolderTitle));
        panel.Children.Add(Combo(LocalizationKeys.AddRunnerFolderModeTitle, new[] { T(LocalizationKeys.AddRunnerCreateNewFolder), T(LocalizationKeys.AddRunnerImportExistingFolder) }, _draft.FolderSetupMode == RunnerFolderSetupMode.ImportExisting ? 1 : 0, index =>
        {
            _draft.FolderSetupMode = index == 1 ? RunnerFolderSetupMode.ImportExisting : RunnerFolderSetupMode.CreateNew;
            Build();
            return Task.CompletedTask;
        }));
        panel.Children.Add(TextField(LocalizationKeys.GitHubRunnerDirectoryField, _draft.RunnerDirectory, value => _draft.RunnerDirectory = value.Trim()));
        panel.Children.Add(Button(LocalizationKeys.ButtonChooseRunnerDirectory, async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = T(LocalizationKeys.GitHubRunnerDirectoryField) });
            var path = folders.FirstOrDefault()?.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path))
                _draft.RunnerDirectory = path;
            Build();
        }));
        var folder = _folderValidator.ValidateSetupFolder(_draft.RunnerDirectory, _draft.FolderSetupMode);
        panel.Children.Add(Secondary(folder.Message, folder.IsValid ? SecondaryTextBrush : WarningBrush));
        return panel;
    }

    private Control BuildReviewStep()
    {
        var panel = Stack();
        panel.Children.Add(Heading(LocalizationKeys.AddRunnerReviewTitle));
        panel.Children.Add(Row(LocalizationKeys.GitHubAccountSectionTitle, _draft.AccountLogin));
        panel.Children.Add(Row(LocalizationKeys.GitHubRunnerScopeTitle, _draft.Scope == GitHubRunnerScope.Organization ? _draft.OwnerOrOrg : T(LocalizationKeys.AddRunnerPersonalScope)));
        panel.Children.Add(Row(LocalizationKeys.AddRunnerRepositoryAccessTitle, _draft.RepositoryAccessMode == RunnerRepositoryAccessMode.AllRepositories ? T(LocalizationKeys.ActionsAllRepositories) : T(LocalizationKeys.AddRunnerSelectedRepositories)));
        panel.Children.Add(Row(LocalizationKeys.GitHubRepositoryField, string.Join(", ", _draft.SelectedRepositories.Select(repository => repository.FullName))));
        panel.Children.Add(Row(LocalizationKeys.GitHubRunnerNameField, _draft.RunnerName));
        panel.Children.Add(Row(LocalizationKeys.RunnerLabelsTitle, string.Join(", ", _draft.Labels)));
        panel.Children.Add(Row(LocalizationKeys.GitHubRunnerDirectoryField, _draft.RunnerDirectory));
        foreach (var error in CurrentValidation().Errors)
            panel.Children.Add(Secondary(error, WarningBrush));
        return panel;
    }

    private StackPanel BuildButtons()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(18),
            Background = WindowBrush
        };
        panel.Children.Add(Button(LocalizationKeys.ButtonCancel, () => Close(false)));
        panel.Children.Add(Button(LocalizationKeys.AddRunnerBackButton, () => { _step = Math.Max(0, _step - 1); Build(); }, _step > 0 && !_isBusy));
        panel.Children.Add(_step == 4
            ? Button(LocalizationKeys.AddRunnerFinishButton, async () => await FinishAsync(), CanFinish() && !_isBusy)
            : Button(LocalizationKeys.AddRunnerNextButton, async () =>
            {
                if (_step == 0 || _step == 1)
                    await LoadRepositoriesAsync();
                _step = Math.Min(4, _step + 1);
                Build();
            }, CanContinue() && !_isBusy));
        return panel;
    }

    private bool CanContinue()
    {
        if (!_account.IsSignedIn)
            return false;
        if (_step == 0)
            return _draft.Scope == GitHubRunnerScope.Repository || !string.IsNullOrWhiteSpace(_draft.OwnerOrOrg);
        if (_step == 1)
            return _draft.RepositoryAccessMode == RunnerRepositoryAccessMode.AllRepositories && _draft.Scope == GitHubRunnerScope.Organization
                || _draft.SelectedRepositories.Count > 0;
        if (_step == 2)
            return !string.IsNullOrWhiteSpace(_draft.RunnerName) && RunnerSetupValidator.NormalizeLabels(_draft.Labels).Count > 0;
        if (_step == 3)
            return _folderValidator.ValidateSetupFolder(_draft.RunnerDirectory, _draft.FolderSetupMode).IsValid;
        return true;
    }

    private bool CanFinish() => CurrentValidation().IsValid && _folderValidator.ValidateSetupFolder(_draft.RunnerDirectory, _draft.FolderSetupMode).IsValid;

    private RunnerSetupValidationResult CurrentValidation() => RunnerSetupValidator.ValidateDraft(_draft, _store.Runners.Select(runner => runner.Profile).ToList());

    private async Task FinishAsync()
    {
        try
        {
            _isBusy = true;
            _status = T(LocalizationKeys.AddRunnerFinishing);
            Build();
            var selected = _draft.SelectedRepositories.FirstOrDefault();
            var request = new GitHubRunnerSetupRequest
            {
                Scope = _draft.Scope,
                RepositoryAccessMode = _draft.RepositoryAccessMode,
                FolderSetupMode = _draft.FolderSetupMode,
                OwnerOrOrg = _draft.Scope == GitHubRunnerScope.Organization ? _draft.OwnerOrOrg : selected?.Owner ?? _draft.OwnerOrOrg,
                RepositoryName = selected?.Name ?? "",
                RunnerDirectory = _draft.RunnerDirectory,
                RunnerName = _draft.RunnerName,
                Labels = RunnerSetupValidator.NormalizeLabels(_draft.Labels),
                SelectedRepositories = _draft.SelectedRepositories.Select(repository => new GitHubRepositoryReference { Owner = repository.Owner, Repo = repository.Name }).ToList()
            };
            var result = await _gitHubService.SetupRunnerAsync(request);
            if (!result.Succeeded || (result.RunnerProfile == null && result.RunnerProfiles.Count == 0))
            {
                _status = result.Message;
                return;
            }

            var profiles = result.RunnerProfiles.Count > 0 ? result.RunnerProfiles : [result.RunnerProfile!];
            foreach (var profile in profiles)
            {
                profile.StopOnBattery = _preferences.StopRunnerOnBattery;
                profile.StopOnMeteredNetwork = _preferences.StopRunnerOnMeteredNetwork;
                _store.AddRunnerProfile(profile);
            }
            Close(true);
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        finally
        {
            _isBusy = false;
            Build();
        }
    }

    private StackPanel Stack() => new() { Spacing = 10 };

    private TextBlock Heading(string key) => new()
    {
        Text = T(key),
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        Foreground = PrimaryTextBrush
    };

    private TextBlock Secondary(string text) => Secondary(text, SecondaryTextBrush);

    private static TextBlock Secondary(string text, IBrush brush) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        Foreground = brush,
        MaxWidth = 660
    };

    private Control Row(string key, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*") };
        grid.Children.Add(new TextBlock { Text = T(key), FontWeight = FontWeight.Bold, Foreground = PrimaryTextBrush });
        var detail = new TextBlock { Text = value, Foreground = SecondaryTextBrush, TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(detail);
        Grid.SetColumn(detail, 1);
        return grid;
    }

    private Control TextField(string key, string value, Action<string> changed)
    {
        var panel = Stack();
        panel.Children.Add(Heading(key));
        var box = new TextBox
        {
            Text = value,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        box.LostFocus += (_, _) => changed(box.Text ?? "");
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                changed(box.Text ?? "");
                Build();
            }
        };
        panel.Children.Add(box);
        return panel;
    }

    private Control Combo(string key, IReadOnlyList<string> items, int selectedIndex, Func<int, Task> changed)
    {
        var panel = Stack();
        panel.Children.Add(Heading(key));
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIndex,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            MinWidth = 300
        };
        combo.SelectionChanged += async (_, _) =>
        {
            await changed(combo.SelectedIndex);
        };
        panel.Children.Add(combo);
        return panel;
    }

    private Button Button(string key, Action action, bool enabled = true)
    {
        var button = StyledButton(key, enabled);
        button.Click += (_, _) => action();
        return button;
    }

    private Button Button(string key, Func<Task> action, bool enabled = true)
    {
        var button = StyledButton(key, enabled);
        button.Click += async (_, _) => await action();
        return button;
    }

    private Button StyledButton(string key, bool enabled)
    {
        return new Button
        {
            Content = new TextBlock { Text = T(key), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            IsEnabled = enabled,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(8)
        };
    }

    private static Border Separator() => new()
    {
        Height = 1,
        Background = SidebarBorderBrush,
        Margin = new Thickness(0, 4)
    };

    private string T(string key) => _localization.Get(key);
    private string T(string key, params object[] args) => _localization.Get(key, args);
}
