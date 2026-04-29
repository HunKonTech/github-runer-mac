using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Localization;
using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Core.Services;

namespace GitHubRunnerTray.App;

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
        LocalizationKeys.SettingsRunnerTitle,
        LocalizationKeys.SettingsUpdatesTitle,
        LocalizationKeys.SettingsNetworkTitle,
        LocalizationKeys.SettingsAdvancedTitle,
        LocalizationKeys.SettingsAboutTitle
    ];

    private readonly string[] _sectionIcons = ["⚙", "▻", "⇩", "◉", "⚒", "ⓘ"];

    private ILocalizationService? _localization;
    private RunnerTrayStore? _store;
    private IPreferencesStore? _preferences;
    private IAppUpdateService? _updateService;
    private ILaunchAtLoginService? _launchAtLoginService;
    private ContentControl? _detail;
    private Border? _sidebar;
    private AppUpdateInfo? _availableUpdate;
    private string _updateStatus = "";
    private bool _isCheckingUpdates;
    private bool _suppressStoreRefresh;
    private int _selectedSection;

    public SettingsWindow()
    {
        Width = 980;
        Height = 720;
        MinWidth = 820;
        MinHeight = 560;
        Background = WindowBrush;
    }

    public void Initialize(
        RunnerTrayStore store,
        ILocalizationService localization,
        IPreferencesStore preferences,
        IAppUpdateService updateService,
        ILaunchAtLoginService launchAtLoginService)
    {
        _store = store;
        _localization = localization;
        _preferences = preferences;
        _updateService = updateService;
        _launchAtLoginService = launchAtLoginService;
        _updateStatus = T(LocalizationKeys.UpdateIdle);
        Title = T(LocalizationKeys.SettingsWindowTitle);

        _localization.CurrentLanguage = _preferences.Language;
        _localization.LanguageChanged += OnLanguageChanged;
        _store.PropertyChanged += OnStorePropertyChanged;

        BuildShell();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_localization != null)
            _localization.LanguageChanged -= OnLanguageChanged;

        if (_store != null)
            _store.PropertyChanged -= OnStorePropertyChanged;

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
            1 => BuildRunnerPage(),
            2 => BuildUpdatesPage(),
            3 => BuildNetworkPage(),
            4 => BuildAdvancedPage(),
            5 => BuildAboutPage(),
            _ => BuildGeneralPage()
        };
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
        button.Click += (_, _) =>
        {
            _selectedSection = index;
            BuildShell();
        };

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

        var stopOnBattery = new CheckBox
        {
            Content = T(LocalizationKeys.SettingsStopOnBatteryTitle),
            IsChecked = _store?.StopRunnerOnBattery == true,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryTextBrush
        };
        stopOnBattery.IsCheckedChanged += (_, _) =>
        {
            if (_store != null)
                _store.StopRunnerOnBattery = stopOnBattery.IsChecked == true;
        };
        panel.Children.Add(stopOnBattery);
        panel.Children.Add(SecondaryText(T(LocalizationKeys.SettingsStopOnBatteryExplanation)));

        return Scroll(panel);
    }

    private Control BuildRunnerPage()
    {
        var panel = Page(LocalizationKeys.SettingsRunnerTitle);

        panel.Children.Add(Row(LocalizationKeys.StatusRunnerTitle, RunnerStatusText()));
        panel.Children.Add(Row(LocalizationKeys.StatusActivityTitle, _store?.RunnerSnapshot.Activity.Description ?? T(LocalizationKeys.ActivityUnknown)));
        panel.Children.Add(Label(LocalizationKeys.SettingsRunnerFolderTitle));

        var pathBox = new TextBox
        {
            Text = _store?.RunnerDirectory ?? "",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            FontSize = 16,
            MinHeight = 34
        };
        pathBox.TextChanged += (_, _) =>
        {
            if (_store != null && !string.IsNullOrWhiteSpace(pathBox.Text))
            {
                _suppressStoreRefresh = true;
                try
                {
                    _store.RunnerDirectory = pathBox.Text;
                }
                finally
                {
                    _suppressStoreRefresh = false;
                }
            }
        };
        panel.Children.Add(pathBox);

        var pathButtons = ButtonRow();
        pathButtons.Children.Add(Button(LocalizationKeys.ButtonChooseRunnerDirectory, async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = T(LocalizationKeys.SettingsRunnerFolderTitle)
            });

            var folder = folders.FirstOrDefault();
            if (folder?.Path.LocalPath is { Length: > 0 } path && _store != null)
            {
                _suppressStoreRefresh = true;
                try
                {
                    _store.RunnerDirectory = path;
                    pathBox.Text = path;
                }
                finally
                {
                    _suppressStoreRefresh = false;
                }
            }
        }));
        pathButtons.Children.Add(Button(LocalizationKeys.ButtonOpenRunnerDirectory, () => _store?.OpenRunnerDirectory()));
        panel.Children.Add(pathButtons);

        var buttons = ButtonRow();
        buttons.Children.Add(Button(LocalizationKeys.ButtonManualStart, async () => await (_store?.ForceStartAsync() ?? Task.CompletedTask)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonManualStop, async () => await (_store?.ForceStopAsync() ?? Task.CompletedTask)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonAutomaticMode, async () => await (_store?.SetAutomaticModeAsync() ?? Task.CompletedTask)));
        buttons.Children.Add(Button(LocalizationKeys.ButtonRefresh, async () => await (_store?.RefreshNowAsync() ?? Task.CompletedTask)));
        panel.Children.Add(buttons);

        return Scroll(panel);
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
        buttons.Children.Add(LinkButton(LocalizationKeys.ButtonOpenRepository, "https://github.com/HunKonTech/github-runer-mac"));
        panel.Children.Add(buttons);

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

    private static StackPanel ButtonRow()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
    }

    private Button Button(string text, Action action, bool isEnabled = true)
    {
        var button = new Button
        {
            Content = text,
            IsEnabled = isEnabled,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            FontSize = 15,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(8)
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
            Content = T(key),
            IsEnabled = isEnabled,
            Background = FieldBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = SidebarBorderBrush,
            FontSize = 15,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(8)
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

    private Button LinkButton(string key, string url)
    {
        return Button(key, () =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        });
    }

    private string T(string key)
    {
        return _localization?.Get(key) ?? key;
    }

    private string RunnerStatusText()
    {
        return _store?.RunnerSnapshot.IsRunning == true
            ? T(LocalizationKeys.RunnerRunning)
            : T(LocalizationKeys.RunnerStopped);
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
        if (_suppressStoreRefresh)
            return;

        Dispatcher.UIThread.Post(BuildSelectedPage);
    }
}
