using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;

namespace GitRunnerManager.App;

public sealed class SettingsWindow : Window
{
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static readonly SolidColorBrush WindowBrush = Brush("#211f18");
    private static readonly SolidColorBrush SidebarBrush = Brush("#17150f");
    private static readonly SolidColorBrush SidebarBorderBrush = Brush("#494234");
    private static readonly SolidColorBrush SelectedBrush = Brush("#4a4a4a");
    private static readonly SolidColorBrush PrimaryTextBrush = Brush("#eeeeeb");
    private static readonly SolidColorBrush SecondaryTextBrush = Brush("#aaa7a0");
    private static readonly SolidColorBrush FieldBrush = Brush("#3d3a33");
    private static readonly SolidColorBrush AccentBrush = Brush("#2d7dca");

    private readonly string[] _sections =
    [
        LocalizationKeys.SettingsGeneralTitle,
        LocalizationKeys.SettingsRunnersTitle,
        LocalizationKeys.SettingsGitHubAccountsTitle,
        LocalizationKeys.SettingsUpdatesTitle,
        LocalizationKeys.SettingsNetworkTitle,
        LocalizationKeys.SettingsAdvancedTitle,
        LocalizationKeys.SettingsDeveloperTitle,
        LocalizationKeys.SettingsAboutTitle
    ];

    private readonly string[] _sectionIcons = ["⚙", "▻", "◎", "⇩", "◉", "⚒", "⌘", "ⓘ"];

    private ILocalizationService? _localization;
    private RunnerTrayStore? _store;
    private IPreferencesStore? _preferences;
    private IAppUpdateService? _updateService;
    private IRunnerUpdateService? _runnerUpdateService;
    private IGitHubService? _gitHubService;
    private ILaunchAtLoginService? _launchAtLoginService;
    private IRunnerFolderValidator? _runnerFolderValidator;
    private IRunnerLogService? _runnerLogService;
    private ContentControl? _detail;
    private Border? _sidebar;
    private AppUpdateInfo? _availableUpdate;
    private string _updateStatus = "";
    private bool _isCheckingUpdates;
    private int _selectedSection;
    private string? _selectedRunnerId;
    private string _runnerUpdateStatus = "";
    private GitHubAccountSnapshot _gitHubAccount = new();
    private string _gitHubStatus = "";
    private string? _gitHubDeviceCode;
    private string? _gitHubVerificationUri;
    private GitHubRunnerScope _gitHubRunnerScope = GitHubRunnerScope.Repository;
    private string _gitHubOwnerOrOrg = "";
    private string _gitHubRepositoryName = "";
    private string _gitHubRunnerName = Environment.MachineName;
    private string _gitHubRunnerDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "actions-runner");
    private string _gitHubLabels = "self-hosted";
    private readonly Button[] _sidebarButtons = new Button[8];
    private bool _isPageRefreshQueued;
    private readonly DispatcherTimer _runnerLogTimer;
    private string _runnerLogText = "";
    private string _runnerLogStatus = "";
    private string? _runnerLogPath;

    public SettingsWindow()
    {
        Width = 980;
        Height = 720;
        MinWidth = 820;
        MinHeight = 560;
        Background = WindowBrush;
        _runnerLogTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _runnerLogTimer.Tick += async (_, _) => await RefreshSelectedRunnerLogAsync(true);
    }

    public void Initialize(
        RunnerTrayStore store,
        ILocalizationService localization,
        IPreferencesStore preferences,
        IAppUpdateService updateService,
        IRunnerUpdateService runnerUpdateService,
        IGitHubService gitHubService,
        ILaunchAtLoginService launchAtLoginService,
        IRunnerFolderValidator runnerFolderValidator,
        IRunnerLogService runnerLogService)
    {
        _store = store;
        _localization = localization;
        _preferences = preferences;
        _updateService = updateService;
        _runnerUpdateService = runnerUpdateService;
        _gitHubService = gitHubService;
        _launchAtLoginService = launchAtLoginService;
        _runnerFolderValidator = runnerFolderValidator;
        _runnerLogService = runnerLogService;
        _updateStatus = T(LocalizationKeys.UpdateIdle);
        _runnerUpdateStatus = T(LocalizationKeys.RunnerUpdateIdle);
        _gitHubStatus = T(LocalizationKeys.GitHubStatusReady);
        _runnerLogStatus = T(LocalizationKeys.RunnerLogNoLogs);
        Title = T(LocalizationKeys.SettingsWindowTitle);

        _localization.CurrentLanguage = _preferences.Language;
        _localization.LanguageChanged += OnLanguageChanged;
        _store.PropertyChanged += OnStorePropertyChanged;

        BuildShell();
        _ = RefreshGitHubAccountAsync();
    }

    public void ShowGitHubAccountsPage()
    {
        SelectSection(2);
        Show();
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_localization != null)
            _localization.LanguageChanged -= OnLanguageChanged;

        if (_store != null)
            _store.PropertyChanged -= OnStorePropertyChanged;

        _runnerLogTimer.Stop();
        base.OnClosed(e);
    }

    private void BuildShell()
    {
        _sidebar = BuildSidebar();
        _detail = new ContentControl();

        var grid = new Grid
        {
            Background = WindowBrush,
            ColumnDefinitions = new ColumnDefinitions("300,*")
        };
        grid.Children.Add(_sidebar);
        Grid.SetColumn(_sidebar, 0);

        var detailHost = new Border
        {
            Background = WindowBrush,
            Child = _detail
        };
        grid.Children.Add(detailHost);
        Grid.SetColumn(detailHost, 1);

        Content = grid;
        BuildSelectedPage();
    }

    private void BuildSelectedPage()
    {
        if (_detail == null)
            return;

        _detail.Content = _selectedSection switch
        {
            1 => BuildRunnersPage(),
            2 => BuildGitHubAccountsPage(),
            3 => BuildUpdatesPage(),
            4 => BuildNetworkPage(),
            5 => BuildAdvancedPage(),
            6 => BuildDeveloperPage(),
            7 => BuildAboutPage(),
            _ => BuildGeneralPage()
        };
    }

    private void SelectSection(int index)
    {
        if (_selectedSection == index)
            return;

        _selectedSection = index;
        UpdateSidebarSelection();
        BuildSelectedPage();
    }

    private void UpdateSidebarSelection()
    {
        for (var index = 0; index < _sidebarButtons.Length; index++)
        {
            var button = _sidebarButtons[index];
            if (button != null)
                button.Background = index == _selectedSection ? SelectedBrush : Brushes.Transparent;
        }
    }

    private Border BuildSidebar()
    {
        var items = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(18)
        };

        for (var index = 0; index < _sections.Length; index++)
        {
            items.Children.Add(BuildSidebarItem(index));
        }

        return new Border
        {
            Width = 300,
            Margin = new Thickness(0, 14, 0, 12),
            Padding = new Thickness(2),
            Background = SidebarBrush,
            BorderBrush = SidebarBorderBrush,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(10),
            Child = items
        };
    }

    private Button BuildSidebarItem(int index)
    {
        var selected = index == _selectedSection;
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("42,*"),
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var icon = new TextBlock
        {
            Text = _sectionIcons[index],
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(icon);
        Grid.SetColumn(icon, 0);

        var label = new TextBlock
        {
            Text = T(_sections[index]),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(label);
        Grid.SetColumn(label, 1);

        var button = new Button
        {
            Content = content,
            Background = selected ? SelectedBrush : Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(10)
        };
        button.Click += (_, _) => SelectSection(index);
        _sidebarButtons[index] = button;

        return button;
    }

    private Control BuildGeneralPage()
    {
        var panel = Page(LocalizationKeys.SettingsGeneralTitle);

        panel.Children.Add(Label(LocalizationKeys.SettingsLanguageTitle));
        var language = new ComboBox
        {
            Width = 430,
            Height = 42,
            FontSize = 18,
            Foreground = PrimaryTextBrush,
            Background = FieldBrush,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(4),
            ItemsSource = new[]
            {
                T(LocalizationKeys.LanguageSystemDefault),
                T(LocalizationKeys.LanguageHungarian),
                T(LocalizationKeys.LanguageEnglish)
            },
            SelectedIndex = _preferences?.Language switch
            {
                AppLanguage.Hungarian => 1,
                AppLanguage.English => 2,
                _ => 0
            }
        };
        language.SelectionChanged += (_, _) =>
        {
            if (_preferences == null || _localization == null)
                return;

            _preferences.Language = language.SelectedIndex switch
            {
                1 => AppLanguage.Hungarian,
                2 => AppLanguage.English,
                _ => AppLanguage.System
            };
            _localization.CurrentLanguage = _preferences.Language;
        };
        panel.Children.Add(language);
        panel.Children.Add(SecondaryText(T(LocalizationKeys.SettingsLanguageRestartHint)));

        var launchStatus = _launchAtLoginService?.GetStatus() ?? LaunchAtLoginStatus.Unknown;
        var launch = new CheckBox
        {
            Content = T(LocalizationKeys.ToggleLaunchAtLogin),
            IsChecked = launchStatus is LaunchAtLoginStatus.Enabled or LaunchAtLoginStatus.RequiresApproval,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryTextBrush
        };
        launch.IsCheckedChanged += async (_, _) =>
        {
            if (_launchAtLoginService == null)
                return;

            await _launchAtLoginService.SetEnabledAsync(launch.IsChecked == true);
        };
        panel.Children.Add(launch);
        panel.Children.Add(SecondaryText(LaunchStatusText(launchStatus)));

        return Scroll(panel);
    }

    private Control BuildRunnersPage()
    {
        var panel = Page(LocalizationKeys.SettingsRunnersTitle);
        var headerButtons = ButtonWrap();
        headerButtons.Children.Add(Button(LocalizationKeys.ButtonAddRunner, async () => await ShowAddRunnerDialogAsync()));
        headerButtons.Children.Add(Button(LocalizationKeys.ButtonUpdateAllRunners, async () => await UpdateAllRunnersAsync()));
        panel.Children.Add(headerButtons);
        panel.Children.Add(SecondaryText(_runnerUpdateStatus));

        var runners = _store?.Runners ?? [];
        if (_selectedRunnerId == null || runners.All(runner => runner.Profile.Id != _selectedRunnerId))
            _selectedRunnerId = runners.FirstOrDefault()?.Profile.Id;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("260,*"),
            ColumnSpacing = 18
        };

        var list = new StackPanel { Spacing = 8 };
        foreach (var runner in runners)
        {
            list.Children.Add(RunnerListItem(runner));
        }
        if (runners.Count == 0)
            list.Children.Add(SecondaryText(T(LocalizationKeys.RunnerNoRunnersConfigured)));

        var remove = Button(LocalizationKeys.ButtonRemoveRunner, RemoveSelectedRunner, _selectedRunnerId != null);
        list.Children.Add(remove);
        var listBorder = new Border
        {
            BorderBrush = SidebarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = list
        };
        grid.Children.Add(listBorder);
        Grid.SetColumn(listBorder, 0);

        var selected = runners.FirstOrDefault(runner => runner.Profile.Id == _selectedRunnerId);
        grid.Children.Add(BuildRunnerDetail(selected));
        Grid.SetColumn(grid.Children[^1], 1);
        panel.Children.Add(grid);

        return Scroll(panel);
    }

    private async Task ImportRunnerFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = T(LocalizationKeys.ButtonImportRunnerFolder)
        });

        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        await ImportRunnerFolderAsync(path);
    }

    private Task ImportRunnerFolderAsync(string path)
    {
        var validation = _runnerFolderValidator?.Validate(path);
        if (validation?.IsValid == false)
            throw new InvalidOperationException(T(LocalizationKeys.RunnerFolderInvalid));

        var profile = new RunnerConfig
        {
            DisplayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            RunnerDirectory = path,
            StopOnBattery = _preferences?.StopRunnerOnBattery ?? PreferenceDefaults.StopRunnerOnBattery,
            StopOnMeteredNetwork = _preferences?.StopRunnerOnMeteredNetwork ?? PreferenceDefaults.StopRunnerOnMeteredNetwork
        };
        _store?.AddRunnerProfile(profile);
        _selectedRunnerId = profile.Id;
        BuildSelectedPage();
        return Task.CompletedTask;
    }

    private async Task ShowAddRunnerDialogAsync()
    {
        if (_store == null)
            return;

        await RefreshGitHubAccountAsync();
        var importPath = "";
        var status = T(LocalizationKeys.GitHubStatusReady);
        var dialog = new Window
        {
            Title = T(LocalizationKeys.ButtonAddRunner),
            Width = 680,
            Height = 720,
            MinWidth = 560,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = WindowBrush
        };

        var root = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16
        };

        TextBlock StatusText() => new()
        {
            Text = status == T(LocalizationKeys.GitHubStatusReady) ? _gitHubStatus : status,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = SecondaryTextBrush
        };

        void RebuildDialog()
        {
            root.Children.Clear();
            root.Children.Add(Label(LocalizationKeys.ButtonAddRunner));
            root.Children.Add(SecondaryText(T(LocalizationKeys.RunnerAddDialogDescription)));
            root.Children.Add(Separator());
            root.Children.Add(Label(LocalizationKeys.ButtonImportRunnerFolder));
            root.Children.Add(TextField(LocalizationKeys.RunnerDirectoryTitle, importPath, value => importPath = value.Trim()));
            var importButtons = ButtonWrap();
            importButtons.Children.Add(Button(LocalizationKeys.ButtonChooseRunnerDirectory, async () =>
            {
                var folders = await dialog.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = T(LocalizationKeys.ButtonImportRunnerFolder)
                });
                var path = folders.FirstOrDefault()?.Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    importPath = path;
                    RebuildDialog();
                }
            }));
            importButtons.Children.Add(Button(LocalizationKeys.RunnerImportSelectedFolder, async () =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(importPath))
                    {
                        status = T(LocalizationKeys.RunnerFolderRequired);
                        RebuildDialog();
                        return;
                    }

                    var validation = _runnerFolderValidator?.Validate(importPath);
                    if (validation?.IsValid == false)
                    {
                        status = T(LocalizationKeys.RunnerFolderInvalid);
                        RebuildDialog();
                        return;
                    }

                    await ImportRunnerFolderAsync(importPath);
                    dialog.Close(true);
                }
                catch (Exception ex)
                {
                    status = ex.Message;
                    RebuildDialog();
                }
            }));
            root.Children.Add(importButtons);

            root.Children.Add(Separator());
            root.Children.Add(Label(LocalizationKeys.RunnerConfigureWithGitHubAccount));
            root.Children.Add(SecondaryText(_gitHubAccount.IsSignedIn
                ? GitHubAccountText()
                : T(LocalizationKeys.RunnerGitHubAccountRequired)));
            AddGitHubRunnerFields(root, dialog, RebuildDialog);
            root.Children.Add(StatusText());
            root.Children.Add(Button(LocalizationKeys.ButtonCancel, () => dialog.Close(false)));
        }

        dialog.Content = Scroll(root);
        RebuildDialog();
        await dialog.ShowDialog<bool>(this);
    }

    private async Task RemoveSelectedRunner()
    {
        if (_selectedRunnerId == null)
            return;

        var dialog = new Window
        {
            Title = T(LocalizationKeys.RunnerRemoveTitle),
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = WindowBrush,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children =
                {
                    SecondaryText(T(LocalizationKeys.RunnerRemoveConfirmation)),
                    ButtonRow()
                }
            }
        };

        var buttons = (StackPanel)((StackPanel)dialog.Content!).Children[1];
        buttons.Children.Add(Button(LocalizationKeys.ButtonCancel, () => dialog.Close(false)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonRemoveProfile, () => dialog.Close(true)));
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        _store?.RemoveRunnerProfile(_selectedRunnerId);
        _selectedRunnerId = _store?.Runners.FirstOrDefault()?.Profile.Id;
        BuildSelectedPage();
    }

    private void SaveProfile(RunnerConfig profile)
    {
        _store?.SaveRunnerProfile(profile);
        _selectedRunnerId = profile.Id;
    }

    private async Task UpdateRunnerAsync(RunnerConfig profile)
    {
        if (_runnerUpdateService == null || _store == null)
            return;

        var runner = _store.GetRunner(profile.Id);
        var wasRunning = runner?.RunnerSnapshot.IsRunning == true;
        var check = await _runnerUpdateService.CheckForUpdateAsync(profile);
        _runnerUpdateStatus = T(
            LocalizationKeys.RunnerUpdateVersionSummary,
            profile.DisplayName,
            check.InstalledVersion ?? T(LocalizationKeys.UpdateUnknownVersion),
            check.LatestVersion ?? T(LocalizationKeys.UpdateUnknownVersion));
        BuildSelectedPage();

        if (!check.IsUpdateAvailable)
            return;

        await _store.StopRunnerAsync(profile.Id);
        var progress = new Progress<RunnerUpdateProgress>(item =>
        {
            _runnerUpdateStatus = $"{profile.DisplayName}: {item.Message}";
            BuildSelectedPage();
        });
        await _runnerUpdateService.UpdateRunnerAsync(profile, wasRunning || profile.AutoStartEnabled, progress);

        if (wasRunning || profile.AutoStartEnabled)
            await _store.StartRunnerAsync(profile.Id);
    }

    private async Task UpdateAllRunnersAsync()
    {
        if (_store == null)
            return;

        foreach (var runner in _store.Runners)
            await UpdateRunnerAsync(runner.Profile);
    }

    private Control RunnerListItem(RunnerInstanceStore runner)
    {
        var button = new Button
        {
            Background = runner.Profile.Id == _selectedRunnerId ? SelectedBrush : FieldBrush,
            BorderBrush = SidebarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 8),
            Content = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = runner.Profile.DisplayName, Foreground = PrimaryTextBrush, FontWeight = FontWeight.Bold, FontSize = 15 },
                    new TextBlock { Text = $"{RunnerStatusText(runner)} · {runner.ResourceUsage.CpuPercent:0.0}% CPU · {FormatMemory(runner.ResourceUsage.TotalMemoryBytes)}", Foreground = SecondaryTextBrush, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 }
                }
            }
        };
        button.Click += (_, _) =>
        {
            _selectedRunnerId = runner.Profile.Id;
            _runnerLogText = "";
            _runnerLogPath = null;
            _ = RefreshSelectedRunnerLogAsync(true);
            BuildSelectedPage();
        };
        return button;
    }

    private Control BuildRunnerDetail(RunnerInstanceStore? runner)
    {
        if (runner == null)
            return SecondaryText(T(LocalizationKeys.RunnerAddOrImportHint));

        var profile = runner.Profile.Clone();
        var detail = new StackPanel { Spacing = 11 };
        detail.Children.Add(Row(LocalizationKeys.RunnerStatusTitle, RunnerStatusText(runner)));
        detail.Children.Add(Row(LocalizationKeys.StatusActivityTitle, runner.RunnerSnapshot.Activity.Description));
        detail.Children.Add(Row(LocalizationKeys.RunnerResourcesTitle, $"{runner.ResourceUsage.CpuPercent:0.0}% CPU · {FormatMemory(runner.ResourceUsage.TotalMemoryBytes)}"));

        detail.Children.Add(TextField(LocalizationKeys.RunnerDisplayNameTitle, profile.DisplayName, value => { profile.DisplayName = value; SaveProfile(profile); }));
        detail.Children.Add(TextField(LocalizationKeys.RunnerDirectoryTitle, profile.RunnerDirectory, value => { profile.RunnerDirectory = value; SaveProfile(profile); }));

        var pathButtons = ButtonRow();
        pathButtons.Children.Add(Button(LocalizationKeys.ButtonChooseRunnerDirectory, async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = T(LocalizationKeys.RunnerDirectoryTitle)
            });

            var folder = folders.FirstOrDefault();
            if (folder?.Path.LocalPath is { Length: > 0 } path)
            {
                profile.RunnerDirectory = path;
                SaveProfile(profile);
            }
        }));
        detail.Children.Add(pathButtons);

        detail.Children.Add(TextField(LocalizationKeys.RunnerOwnerOrOrgTitle, profile.GitHubOwnerOrOrg, value => { profile.GitHubOwnerOrOrg = value; SaveProfile(profile); }));
        detail.Children.Add(TextField(LocalizationKeys.RunnerRepositoryNameTitle, profile.RepositoryName ?? "", value => { profile.RepositoryName = string.IsNullOrWhiteSpace(value) ? null : value; SaveProfile(profile); }));
        detail.Children.Add(TextField(LocalizationKeys.RunnerLabelsTitle, string.Join(", ", profile.Labels), value => { profile.Labels = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(); SaveProfile(profile); }));
        detail.Children.Add(Toggle(LocalizationKeys.RunnerOrganizationToggle, profile.IsOrganizationRunner, value => { profile.IsOrganizationRunner = value; SaveProfile(profile); }));
        detail.Children.Add(Toggle(LocalizationKeys.RunnerAutoStartToggle, profile.AutoStartEnabled, value => { profile.AutoStartEnabled = value; SaveProfile(profile); }));
        detail.Children.Add(Toggle(LocalizationKeys.RunnerAutomaticModeToggle, profile.AutomaticModeEnabled, value => { profile.AutomaticModeEnabled = value; SaveProfile(profile); }));
        detail.Children.Add(Toggle(LocalizationKeys.RunnerStopOnBatteryToggle, profile.StopOnBattery, value => { profile.StopOnBattery = value; SaveProfile(profile); }));
        detail.Children.Add(Toggle(LocalizationKeys.RunnerStopOnMeteredNetworkToggle, profile.StopOnMeteredNetwork, value => { profile.StopOnMeteredNetwork = value; SaveProfile(profile); }));
        detail.Children.Add(Toggle(LocalizationKeys.RunnerEnabledToggle, profile.IsEnabled, value => { profile.IsEnabled = value; SaveProfile(profile); }));

        var buttons = ButtonWrap();
        buttons.Children.Add(Button(LocalizationKeys.ButtonStart, async () => await (_store?.StartRunnerAsync(profile.Id) ?? Task.CompletedTask)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonStop, async () => await (_store?.StopRunnerAsync(profile.Id) ?? Task.CompletedTask)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonRestart, async () => await (_store?.RestartRunnerAsync(profile.Id) ?? Task.CompletedTask)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonRefreshStatus, async () => await (_store?.RefreshNowAsync() ?? Task.CompletedTask)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonOpenFolder, () => _store?.OpenRunnerDirectory(profile.Id)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonViewLogs, () => _store?.OpenRunnerLogs(profile.Id)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonUpdateRunner, async () => await UpdateRunnerAsync(profile), true));
        detail.Children.Add(buttons);
        detail.Children.Add(BuildRunnerLogViewer(runner));
        UpdateRunnerLogTimer(runner);

        return detail;
    }

    private Control BuildRunnerLogViewer(RunnerInstanceStore runner)
    {
        if (string.IsNullOrWhiteSpace(_runnerLogText) && _runnerLogPath == null)
            _ = RefreshSelectedRunnerLogAsync(false);

        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };
        panel.Children.Add(Label(LocalizationKeys.RunnerLogsTitle));
        panel.Children.Add(SecondaryText(_runnerLogPath ?? _runnerLogStatus));

        var logText = string.IsNullOrWhiteSpace(_runnerLogText)
            ? T(LocalizationKeys.RunnerLogNoLogs)
            : _runnerLogText;

        panel.Children.Add(new TextBox
        {
            Text = logText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            FontFamily = FontFamily.Parse("Menlo,Consolas,monospace"),
            FontSize = 12,
            MinHeight = 220,
            MaxHeight = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var buttons = ButtonWrap();
        buttons.Children.Add(Button(LocalizationKeys.RunnerLogRefresh, async () => await RefreshSelectedRunnerLogAsync(true)));
        buttons.Children.Add(Button(LocalizationKeys.RunnerLogOpenFolder, () => _runnerLogService?.OpenLogDirectory(runner.Profile)));
        panel.Children.Add(buttons);
        return panel;
    }

    private async Task RefreshSelectedRunnerLogAsync(bool rebuild)
    {
        if (_store == null || _runnerLogService == null || _selectedRunnerId == null)
            return;

        var runner = _store.GetRunner(_selectedRunnerId);
        if (runner == null)
            return;

        var profile = runner.Profile.Clone();
        var isRunning = runner.RunnerSnapshot.IsRunning;
        var snapshot = await Task.Run(() => _runnerLogService.ReadLog(profile, isRunning));
        _runnerLogPath = snapshot.FilePath;
        _runnerLogText = snapshot.Content;
        _runnerLogStatus = snapshot.Exists
            ? snapshot.IsTruncated
                ? T(LocalizationKeys.RunnerLogTruncated)
                : T(LocalizationKeys.GitHubStatusReady)
            : T(LocalizationKeys.RunnerLogNoLogs);

        if (rebuild)
            QueueSelectedPageRefresh();
    }

    private void UpdateRunnerLogTimer(RunnerInstanceStore runner)
    {
        if (runner.RunnerSnapshot.IsRunning)
        {
            if (!_runnerLogTimer.IsEnabled)
                _runnerLogTimer.Start();
            return;
        }

        _runnerLogTimer.Stop();
    }

    private Control BuildGitHubAccountsPage()
    {
        var panel = Page(LocalizationKeys.SettingsGitHubAccountsTitle);
        panel.Children.Add(Label(LocalizationKeys.GitHubAccountSectionTitle));
        panel.Children.Add(Row(LocalizationKeys.UpdateStatusTitle, GitHubAccountText()));
        if (!string.IsNullOrWhiteSpace(_gitHubStatus))
            panel.Children.Add(SecondaryText(_gitHubStatus));

        var loginButtons = ButtonWrap();
        loginButtons.Children.Add(Button(LocalizationKeys.ActionsSignInPersonal, async () => await SignInGitHubAsync(GitHubAccountConnectionKind.Personal), true));
        loginButtons.Children.Add(Button(LocalizationKeys.ActionsSignInOrganization, async () => await SignInGitHubAsync(GitHubAccountConnectionKind.Organization)));
        panel.Children.Add(loginButtons);

        if (_gitHubAccount.IsSignedIn)
        {
            var accountButtons = ButtonWrap();
            accountButtons.Margin = new Thickness(0, 0, 0, 4);
            accountButtons.Children.Add(DestructiveButton(LocalizationKeys.GitHubSignOutButton, async () => await SignOutGitHubAsync()));
            panel.Children.Add(accountButtons);
        }

        if (!string.IsNullOrWhiteSpace(_gitHubDeviceCode))
        {
            panel.Children.Add(Row(LocalizationKeys.GitHubDeviceCode, _gitHubDeviceCode));
            var deviceButtons = ButtonWrap();
            deviceButtons.Children.Add(Button(LocalizationKeys.GitHubCopyDeviceCodeButton, async () => await CopyDeviceCodeAsync()));
            if (!string.IsNullOrWhiteSpace(_gitHubVerificationUri))
                deviceButtons.Children.Add(LinkButton(LocalizationKeys.GitHubOpenDevicePageButton, _gitHubVerificationUri));
            panel.Children.Add(deviceButtons);
        }

        panel.Children.Add(SecondaryText(T(LocalizationKeys.GitHubAccountsRunnerManagementHint)));
        return Scroll(panel);
    }

    private void AddGitHubRunnerFields(StackPanel panel, Window owner, Action rebuild)
    {
        panel.Children.Add(Label(LocalizationKeys.GitHubRunnerScopeTitle));
        var scope = new ComboBox
        {
            Width = 320,
            Height = 38,
            FontSize = 16,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            ItemsSource = new[] { T(LocalizationKeys.GitHubRunnerScopeRepository), T(LocalizationKeys.GitHubRunnerScopeOrganization) },
            SelectedIndex = _gitHubRunnerScope == GitHubRunnerScope.Organization ? 1 : 0
        };
        scope.SelectionChanged += (_, _) =>
        {
            _gitHubRunnerScope = scope.SelectedIndex == 1 ? GitHubRunnerScope.Organization : GitHubRunnerScope.Repository;
            rebuild();
        };
        panel.Children.Add(scope);

        panel.Children.Add(TextField(LocalizationKeys.GitHubOwnerOrgField, _gitHubOwnerOrOrg, value => _gitHubOwnerOrOrg = value.Trim()));
        if (_gitHubRunnerScope == GitHubRunnerScope.Repository)
            panel.Children.Add(TextField(LocalizationKeys.GitHubRepositoryField, _gitHubRepositoryName, value => _gitHubRepositoryName = value.Trim()));
        panel.Children.Add(TextField(LocalizationKeys.GitHubRunnerNameField, _gitHubRunnerName, value => _gitHubRunnerName = value.Trim()));
        panel.Children.Add(TextField(LocalizationKeys.GitHubRunnerDirectoryField, _gitHubRunnerDirectory, value => _gitHubRunnerDirectory = value.Trim()));
        panel.Children.Add(TextField(LocalizationKeys.RunnerLabelsTitle, _gitHubLabels, value => _gitHubLabels = value));

        var runnerButtons = ButtonWrap();
        runnerButtons.Children.Add(Button(LocalizationKeys.ButtonChooseRunnerDirectory, async () =>
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = T(LocalizationKeys.GitHubRunnerDirectoryField)
            });
            var path = folders.FirstOrDefault()?.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                _gitHubRunnerDirectory = path;
                rebuild();
            }
        }));
        runnerButtons.Children.Add(Button(LocalizationKeys.GitHubCreateConfigureRunnerButton, async () =>
        {
            var created = await CreateAndConfigureGitHubRunnerAsync();
            if (created)
                owner.Close(true);
            else
                rebuild();
        }, _gitHubAccount.IsSignedIn));
        panel.Children.Add(runnerButtons);
    }

    private string GitHubAccountText()
    {
        return _gitHubAccount.IsSignedIn
            ? T(LocalizationKeys.GitHubSignedInAs, _gitHubAccount.Login ?? "")
            : T(LocalizationKeys.GitHubNotSignedIn);
    }

    private async Task RefreshGitHubAccountAsync()
    {
        if (_gitHubService == null)
            return;

        _gitHubAccount = await _gitHubService.GetAccountAsync();
        QueueSelectedPageRefresh();
    }

    private async Task SignInGitHubAsync(GitHubAccountConnectionKind kind)
    {
        if (_gitHubService == null || _preferences == null)
            return;

        var clientId = _preferences.GitHubOAuthClientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
            return;

        try
        {
            _gitHubStatus = T(LocalizationKeys.GitHubStatusSigningIn);
            var flow = await _gitHubService.StartDeviceFlowAsync(clientId);
            _gitHubDeviceCode = flow.UserCode;
            _gitHubVerificationUri = string.IsNullOrWhiteSpace(flow.VerificationUriComplete) ? flow.VerificationUri : flow.VerificationUriComplete;
            OpenUrl(_gitHubVerificationUri);
            BuildSelectedPage();
            await _gitHubService.CompleteDeviceFlowAsync(clientId, flow.DeviceCode, flow.Interval, kind, "");
            _gitHubDeviceCode = null;
            _gitHubVerificationUri = null;
            await RefreshGitHubAccountAsync();
            _gitHubStatus = T(LocalizationKeys.GitHubStatusReady);
        }
        catch (Exception ex)
        {
            _gitHubStatus = T(LocalizationKeys.GitHubStatusError, ex.Message);
        }

        BuildSelectedPage();
    }

    private async Task CopyDeviceCodeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_gitHubDeviceCode) && Clipboard != null)
        {
            await Clipboard.SetTextAsync(_gitHubDeviceCode);
            _gitHubStatus = T(LocalizationKeys.GitHubDeviceCodeCopied);
            BuildSelectedPage();
        }
    }

    private async Task SignOutGitHubAsync()
    {
        if (_gitHubService == null)
            return;

        await _gitHubService.SignOutAsync();
        _gitHubAccount = new GitHubAccountSnapshot();
        BuildSelectedPage();
    }

    private async Task<bool> CreateAndConfigureGitHubRunnerAsync()
    {
        if (_gitHubService == null || _store == null)
            return false;

        try
        {
            var request = new GitHubRunnerSetupRequest
            {
                Scope = _gitHubRunnerScope,
                OwnerOrOrg = _gitHubOwnerOrOrg.Trim(),
                RepositoryName = _gitHubRepositoryName.Trim(),
                RunnerDirectory = _gitHubRunnerDirectory.Trim(),
                RunnerName = _gitHubRunnerName.Trim(),
                Labels = _gitHubLabels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
            };
            var token = await _gitHubService.CreateRegistrationTokenAsync(request);
            var result = await _gitHubService.ConfigureRunnerAsync(request, token);
            _gitHubStatus = result.Succeeded
                ? T(LocalizationKeys.GitHubStatusConfigured)
                : T(LocalizationKeys.GitHubStatusError, result.Message);
            if (result.RunnerProfile != null)
            {
                _store.AddRunnerProfile(result.RunnerProfile);
                _selectedRunnerId = result.RunnerProfile.Id;
            }
            BuildSelectedPage();
            return result.Succeeded;
        }
        catch (Exception ex)
        {
            _gitHubStatus = T(LocalizationKeys.GitHubStatusError, ex.Message);
        }

        BuildSelectedPage();
        return false;
    }

    private Control BuildUpdatesPage()
    {
        var panel = Page(LocalizationKeys.SettingsUpdatesTitle);

        panel.Children.Add(Row(LocalizationKeys.UpdateInstalledVersionTitle, VersionText()));
        panel.Children.Add(Row(LocalizationKeys.UpdateLatestVersionTitle, _availableUpdate?.Version ?? T(LocalizationKeys.UpdateUnknownVersion)));
        panel.Children.Add(Row(LocalizationKeys.UpdateStatusTitle, _updateStatus));

        panel.Children.Add(Label(LocalizationKeys.SettingsUpdateChannelTitle));
        var channel = new ComboBox
        {
            Width = 300,
            Height = 38,
            FontSize = 16,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            ItemsSource = new[] { T(LocalizationKeys.UpdateChannelStable), T(LocalizationKeys.UpdateChannelPreview) },
            SelectedIndex = _preferences?.UpdateChannel == UpdateChannel.Preview ? 1 : 0
        };
        channel.SelectionChanged += (_, _) =>
        {
            if (_preferences != null)
                _preferences.UpdateChannel = channel.SelectedIndex == 1 ? UpdateChannel.Preview : UpdateChannel.Stable;
        };
        panel.Children.Add(channel);

        var automaticUpdates = new CheckBox
        {
            Content = T(LocalizationKeys.SettingsAutomaticUpdateCheckTitle),
            IsChecked = _preferences?.AutomaticUpdateCheckEnabled == true,
            Foreground = PrimaryTextBrush,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        };
        automaticUpdates.IsCheckedChanged += (_, _) =>
        {
            if (_preferences != null)
                _preferences.AutomaticUpdateCheckEnabled = automaticUpdates.IsChecked == true;
        };
        panel.Children.Add(automaticUpdates);

        var buttons = ButtonRow();
        buttons.Children.Add(Button(LocalizationKeys.ButtonCheckForUpdates, async () => await CheckForUpdatesAsync(), !_isCheckingUpdates));
        buttons.Children.Add(Button(LocalizationKeys.ButtonInstallUpdate, async () => await InstallUpdateAsync(), _availableUpdate != null && !_isCheckingUpdates));
        panel.Children.Add(buttons);

        return Scroll(panel);
    }

    private Control BuildNetworkPage()
    {
        var panel = Page(LocalizationKeys.SettingsNetworkTitle);

        panel.Children.Add(Row(LocalizationKeys.SettingsNetworkStateTitle, _store?.NetworkSnapshot.Description ?? T(LocalizationKeys.NetworkChecking)));
        panel.Children.Add(Row(LocalizationKeys.SettingsNetworkPolicyTitle, _store?.PolicySummary ?? ""));
        panel.Children.Add(Row(LocalizationKeys.SettingsNetworkOverrideTitle, ControlModeText(_store?.ControlMode ?? RunnerControlMode.Automatic)));
        panel.Children.Add(SecondaryText(T(LocalizationKeys.SettingsNetworkDecisionExplanation)));
        panel.Children.Add(SecondaryText(T(LocalizationKeys.SettingsNetworkExplanation)));

        return Scroll(panel);
    }

    private Control BuildAdvancedPage()
    {
        var panel = Page(LocalizationKeys.SettingsAdvancedTitle);
        var usage = _store?.ResourceUsage ?? RunnerResourceUsage.Zero;

        panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedRunnerProcessTitle, usage.IsRunning ? T(LocalizationKeys.RunnerRunning) : T(LocalizationKeys.RunnerStopped)));
        panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedJobActive, usage.IsJobActive ? T(LocalizationKeys.BooleanYes) : T(LocalizationKeys.BooleanNo)));
        panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedCPU, $"{usage.CpuPercent:0.0}%"));
        panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedMemory, FormatMemory(usage.TotalMemoryBytes)));
        panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedProcesses, usage.ProcessCount.ToString()));
        if (!string.IsNullOrWhiteSpace(usage.Warning))
            panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedWarning, usage.Warning));
        if (!string.IsNullOrWhiteSpace(usage.Error))
            panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedWarning, usage.Error));
        panel.Children.Add(Row(LocalizationKeys.StatusModeTitle, ControlModeText(_store?.ControlMode ?? RunnerControlMode.Automatic)));
        panel.Children.Add(Row(LocalizationKeys.SettingsAdvancedLastRefresh, _store?.LastRefreshTime?.ToString("T") ?? T(LocalizationKeys.UpdateUnknownVersion)));
        panel.Children.Add(Row(LocalizationKeys.SettingsRunnerFolderTitle, _store?.RunnerDirectory ?? ""));
        panel.Children.Add(Button(LocalizationKeys.ButtonRefresh, async () => await (_store?.RefreshNowAsync() ?? Task.CompletedTask)));

        return Scroll(panel);
    }

    private static string FormatMemory(long bytes)
    {
        var mb = bytes / 1024.0 / 1024.0;
        if (mb >= 1024)
            return $"{mb / 1024.0:0.0} GB";

        return $"{mb:0} MB";
    }

    private Control BuildAboutPage()
    {
        var panel = Page(LocalizationKeys.SettingsAboutTitle);

        panel.Children.Add(Row(LocalizationKeys.SettingsAboutAppNameTitle, T(LocalizationKeys.AppName)));
        panel.Children.Add(Row(LocalizationKeys.UpdateInstalledVersionTitle, VersionText()));
        panel.Children.Add(Row(LocalizationKeys.SettingsAboutLicenseTitle, T(LocalizationKeys.SettingsAboutLicenseValue)));

        var buttons = ButtonRow();
        buttons.Children.Add(LinkButton(LocalizationKeys.ButtonOpenAuthorGitHub, "https://github.com/BenKoncsik"));
        buttons.Children.Add(LinkButton(LocalizationKeys.ButtonOpenAuthorX, "https://x.com/BenedekKoncsik"));
        buttons.Children.Add(LinkButton(LocalizationKeys.ButtonOpenRepository, "https://github.com/HunKonTech/GitRunnerManager"));
        panel.Children.Add(buttons);

        return Scroll(panel);
    }

    private Control BuildDeveloperPage()
    {
        var panel = Page(LocalizationKeys.SettingsDeveloperTitle);
        var logDirectory = DiagnosticLog.DefaultLogDirectory;

        panel.Children.Add(SecondaryText(T(LocalizationKeys.SettingsDeveloperLogDirectoryDescription)));
        panel.Children.Add(Row(LocalizationKeys.SettingsDeveloperLogDirectoryTitle, logDirectory));
        panel.Children.Add(Button(LocalizationKeys.ButtonOpenFolder, () => OpenFolder(logDirectory)));

        return Scroll(panel);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateService == null)
            return;

        _isCheckingUpdates = true;
        _updateStatus = T(LocalizationKeys.UpdateChecking);
        BuildSelectedPage();

        _availableUpdate = await _updateService.CheckForUpdatesAsync();
        _updateStatus = _availableUpdate == null
            ? T(LocalizationKeys.UpdateUpToDate)
            : T(LocalizationKeys.UpdateAvailableFallback);
        _isCheckingUpdates = false;
        BuildSelectedPage();
    }

    private async Task InstallUpdateAsync()
    {
        if (_updateService == null || _availableUpdate == null)
            return;

        _updateStatus = T(LocalizationKeys.UpdateDownloading);
        BuildSelectedPage();
        await _updateService.DownloadAndOpenUpdateAsync(_availableUpdate);
        _updateStatus = T(LocalizationKeys.UpdateInstalling);
        BuildSelectedPage();
    }

    private StackPanel Page(string titleKey)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(42, 34, 28, 24),
            Spacing = 15,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        panel.Children.Add(new TextBlock
        {
            Text = T(titleKey),
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush,
            Margin = new Thickness(0, 0, 0, 12)
        });

        return panel;
    }

    private static ScrollViewer Scroll(Control content)
    {
        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    private TextBlock Label(string key)
    {
        return new TextBlock
        {
            Text = T(key),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush
        };
    }

    private static TextBlock SecondaryText(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = SecondaryTextBrush,
            MaxWidth = 650
        };
    }

    private static Control Separator()
    {
        return new Border
        {
            Height = 1,
            Background = SidebarBorderBrush,
            Margin = new Thickness(0, 8)
        };
    }

    private Grid Row(string key, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            Margin = new Thickness(0, 4)
        };

        var title = new TextBlock
        {
            Text = T(key),
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush
        };
        var detail = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Foreground = SecondaryTextBrush
        };

        grid.Children.Add(title);
        Grid.SetColumn(title, 0);
        grid.Children.Add(detail);
        Grid.SetColumn(detail, 1);

        return grid;
    }

    private Control TextField(string label, string value, Action<string> onChanged)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(new TextBlock
        {
            Text = T(label),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush
        });
        var box = new TextBox
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            FontSize = 15,
            MinHeight = 32
        };
        box.LostFocus += (_, _) => onChanged(box.Text ?? "");
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                onChanged(box.Text ?? "");
        };
        panel.Children.Add(box);
        return panel;
    }

    private Control Toggle(string text, bool value, Action<bool> onChanged)
    {
        var toggle = new CheckBox
        {
            Content = T(text),
            IsChecked = value,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryTextBrush
        };
        toggle.IsCheckedChanged += (_, _) => onChanged(toggle.IsChecked == true);
        return toggle;
    }

    private static StackPanel ButtonRow()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
    }

    private static WrapPanel ButtonWrap()
    {
        return new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2),
            ItemSpacing = 10,
            LineSpacing = 10
        };
    }

    private Button Button(string text, Action action, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = ButtonContent(text),
            IsEnabled = isEnabled,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            FontSize = 15,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (_store != null)
                    _store.LastErrorMessage = T(LocalizationKeys.ErrorRunnerHandling).Replace("{0}", ex.Message);
            }
        };
        return button;
    }

    private Button Button(string key, Action action)
    {
        return Button(T(key), action, true);
    }

    private Button Button(string key, Func<Task> action, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = ButtonContent(T(key)),
            IsEnabled = isEnabled,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            FontSize = 15,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                if (_store != null)
                    _store.LastErrorMessage = T(LocalizationKeys.ErrorRunnerHandling).Replace("{0}", ex.Message);
            }
        };
        return button;
    }

    private static TextBlock ButtonContent(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 240
        };
    }

    private Button LinkButton(string key, string url)
    {
        return Button(key, () => OpenUrl(url));
    }

    private Button DestructiveButton(string key, Func<Task> action)
    {
        var button = Button(key, action);
        button.Background = Brush("#3a2b2b");
        button.BorderBrush = Brush("#8b4f4f");
        button.Foreground = Brush("#f0d0d0");
        return button;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private string T(string key)
    {
        var value = _localization?.Get(key) ?? key;
        return value == key && char.IsUpper(key.FirstOrDefault()) ? key : value;
    }

    private string T(string key, params object[] args)
    {
        return _localization?.Get(key, args) ?? key;
    }

    private string RunnerStatusText()
    {
        return _store?.RunnerSnapshot.IsRunning == true
            ? T(LocalizationKeys.RunnerRunning)
            : T(LocalizationKeys.RunnerStopped);
    }

    private string RunnerStatusText(RunnerInstanceStore runner)
    {
        return runner.Snapshot.StatusKind switch
        {
            RunnerStatusKind.Busy => T(LocalizationKeys.RunnerStatusBusy),
            RunnerStatusKind.Waiting => T(LocalizationKeys.RunnerStatusWaiting),
            RunnerStatusKind.Error => T(LocalizationKeys.RunnerStatusError),
            RunnerStatusKind.Running => T(LocalizationKeys.RunnerRunning),
            _ => T(LocalizationKeys.RunnerStopped)
        };
    }

    private string ControlModeText(RunnerControlMode mode)
    {
        return mode switch
        {
            RunnerControlMode.ForceRunning => T(LocalizationKeys.ControlModeForceRunning),
            RunnerControlMode.ForceStopped => T(LocalizationKeys.ControlModeForceStopped),
            _ => T(LocalizationKeys.ControlModeAutomatic)
        };
    }

    private string LaunchStatusText(LaunchAtLoginStatus status)
    {
        return status switch
        {
            LaunchAtLoginStatus.Enabled => T(LocalizationKeys.LaunchAtLoginEnabled),
            LaunchAtLoginStatus.RequiresApproval => T(LocalizationKeys.LaunchAtLoginRequiresApproval),
            LaunchAtLoginStatus.Disabled => T(LocalizationKeys.LaunchAtLoginDisabled),
            LaunchAtLoginStatus.Unavailable => T(LocalizationKeys.LaunchAtLoginUnavailable),
            _ => T(LocalizationKeys.LaunchAtLoginUnknown)
        };
    }

    private static string VersionText()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "1.0.0";

        return version;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = T(LocalizationKeys.SettingsWindowTitle);
        BuildShell();
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var propertyName = e.PropertyName;
        Dispatcher.UIThread.Post(() =>
        {
            if (ShouldRefreshForStoreChange(propertyName))
                QueueSelectedPageRefresh();
        }, DispatcherPriority.Background);
    }

    private bool ShouldRefreshForStoreChange(string? propertyName)
    {
        if (!IsVisible)
            return false;

        return _selectedSection switch
        {
            1 => propertyName is nameof(RunnerTrayStore.Runners)
                or nameof(RunnerTrayStore.RunnerSnapshot)
                or nameof(RunnerTrayStore.ResourceUsage)
                or nameof(RunnerTrayStore.LastRefreshTime)
                or nameof(RunnerTrayStore.LastErrorMessage),
            4 => propertyName is nameof(RunnerTrayStore.NetworkSnapshot)
                or nameof(RunnerTrayStore.ControlMode)
                or nameof(RunnerTrayStore.Runners),
            5 => propertyName is nameof(RunnerTrayStore.Runners)
                or nameof(RunnerTrayStore.RunnerSnapshot)
                or nameof(RunnerTrayStore.ResourceUsage)
                or nameof(RunnerTrayStore.ControlMode)
                or nameof(RunnerTrayStore.LastRefreshTime)
                or nameof(RunnerTrayStore.LastErrorMessage),
            _ => false
        };
    }

    private void QueueSelectedPageRefresh()
    {
        if (_isPageRefreshQueued)
            return;

        _isPageRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isPageRefreshQueued = false;
            if (IsVisible)
                BuildSelectedPage();
        }, DispatcherPriority.Background);
    }
}
