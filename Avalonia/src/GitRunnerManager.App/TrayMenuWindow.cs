using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Localization;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Services;
using System.Runtime.InteropServices;

namespace GitRunnerManager.App;

public sealed class TrayMenuWindow : Window
{
    private const double PopoverWidth = 390;

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static readonly SolidColorBrush PanelBrush = Brush("#171717");
    private static readonly SolidColorBrush PanelBorderBrush = Brush("#3f3f3f");
    private static readonly SolidColorBrush DividerBrush = Brush("#333333");
    private static readonly SolidColorBrush PrimaryTextBrush = Brush("#f0f0f0");
    private static readonly SolidColorBrush SecondaryTextBrush = Brush("#aaaaaa");
    private static readonly SolidColorBrush ButtonBrush = Brush("#333333");
    private static readonly SolidColorBrush AccentBrush = Brush("#0a84ff");
    private static readonly SolidColorBrush GreenBrush = Brush("#14e56f");
    private static readonly SolidColorBrush GrayBrush = Brush("#9a9aa0");
    private static readonly SolidColorBrush OrangeBrush = Brush("#ff9f0a");
    private static readonly SolidColorBrush RedBrush = Brush("#ff453a");

    private readonly RunnerTrayStore _store;
    private readonly ILocalizationService _localization;
    private readonly ILaunchAtLoginService _launchAtLoginService;
    private readonly Func<Task> _startRunner;
    private readonly Func<Task> _stopRunner;
    private readonly Func<Task> _automaticMode;
    private readonly Func<Task> _refresh;
    private readonly Action _openSettings;
    private readonly Action _openActionsDashboard;
    private readonly Action _quit;
    private bool _isUpdatingLaunchAtLogin;
    private bool _isAdvancedOpen;
    private bool _isBuildQueued;

    public TrayMenuWindow(
        RunnerTrayStore store,
        ILocalizationService localization,
        ILaunchAtLoginService launchAtLoginService,
        Func<Task> startRunner,
        Func<Task> stopRunner,
        Func<Task> automaticMode,
        Func<Task> refresh,
        Action openSettings,
        Action openActionsDashboard,
        Action quit)
    {
        _store = store;
        _localization = localization;
        _launchAtLoginService = launchAtLoginService;
        _startRunner = startRunner;
        _stopRunner = stopRunner;
        _automaticMode = automaticMode;
        _refresh = refresh;
        _openSettings = openSettings;
        _openActionsDashboard = openActionsDashboard;
        _quit = quit;

        ApplyResponsiveSize();
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;

        Deactivated += OnDeactivated;
        _store.PropertyChanged += OnStorePropertyChanged;
        Build();
    }

    public void ShowAsPopover()
    {
        var wasVisible = IsVisible;
        Show();
        if (!wasVisible)
            PositionNearMenuBar();
        Activate();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionNearMenuBar();
    }

    protected override void OnClosed(EventArgs e)
    {
        Deactivated -= OnDeactivated;
        _store.PropertyChanged -= OnStorePropertyChanged;
        base.OnClosed(e);
    }

    private void PositionNearMenuBar()
    {
        var pointer = GetPointerLocation();
        var screen = pointer.HasValue ? Screens.ScreenFromPoint(pointer.Value) : Screens.Primary;
        if (screen == null)
            return;

        var area = screen.WorkingArea;
        var anchorX = pointer?.X ?? area.X + area.Width - 220;
        var x = Math.Clamp(anchorX - (int)Width / 2, area.X + 8, area.X + area.Width - (int)Width - 8);
        Position = new PixelPoint(x, area.Y + 4);
    }

    private void Build()
    {
        ApplyResponsiveSize();

        var root = new Border
        {
            Background = PanelBrush,
            BorderBrush = PanelBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 14),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                MaxHeight = Math.Max(280, MaxHeight - 28),
                Content = BuildContent()
            }
        };

        Content = root;
    }

    private void ApplyResponsiveSize()
    {
        var pointer = GetPointerLocation();
        var screen = pointer.HasValue ? Screens.ScreenFromPoint(pointer.Value) : Screens.Primary;
        var availableWidth = screen?.WorkingArea.Width - 16 ?? PopoverWidth;
        var availableHeight = screen?.WorkingArea.Height - 16 ?? 560;
        var width = Math.Clamp(PopoverWidth, 320, Math.Max(320, availableWidth));

        Width = width;
        MinWidth = width;
        MinHeight = 0;
        MaxWidth = width;
        MaxHeight = Math.Max(320, availableHeight);
    }

    private Control BuildContent()
    {
        var panel = new StackPanel
        {
            Spacing = 6
        };

        panel.Children.Add(BuildHeader());

        panel.Children.Add(new TextBlock
        {
            Text = TrimText(_store.PolicySummary, 64),
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = SecondaryTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });

        foreach (var runner in _store.Runners.Take(5))
        {
            panel.Children.Add(BuildRunnerRow(runner));
            var jobName = runner.RunnerSnapshot.Activity.CurrentJobName;
            if (!string.IsNullOrWhiteSpace(jobName) || runner.ResourceUsage.IsJobActive)
                panel.Children.Add(SmallStatusRow(
                    LocalizationKeys.ActionsLocalRunnerActivity,
                    $"{(string.IsNullOrWhiteSpace(jobName) ? runner.RunnerSnapshot.Activity.Description : jobName)} · {T(LocalizationKeys.ActionsCorrelationConfidence)}: {GitHubCorrelationConfidence.Possible}"));
        }

        if (_store.Runners.Count == 0)
            panel.Children.Add(StatusRow(LocalizationKeys.StatusRunnerTitle, T(LocalizationKeys.RunnerStatusNoRunners), GrayBrush));

        panel.Children.Add(StatusRow(LocalizationKeys.StatusNetworkTitle, _store.NetworkSnapshot.Description, NetworkColor()));
        panel.Children.Add(StatusRow(LocalizationKeys.StatusModeTitle, ControlModeText(_store.ControlMode), GreenBrush));
        panel.Children.Add(StatusRow(LocalizationKeys.StatusLaunchAtLoginTitle, LaunchStatusText(), LaunchStatusColor()));

        panel.Children.Add(BuildAdvancedSection());
        panel.Children.Add(Divider());

        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        actions.Children.Add(ActionButton(
            _store.RunnerSnapshot.IsRunning ? LocalizationKeys.ButtonStop : LocalizationKeys.ButtonStart,
            _store.RunnerSnapshot.IsRunning ? _stopRunner : _startRunner,
            true));
        actions.Children.Add(ActionButton(LocalizationKeys.ButtonAutomaticMode, _automaticMode));
        panel.Children.Add(actions);

        panel.Children.Add(Divider());
        panel.Children.Add(BuildLaunchAtLoginToggle());
        panel.Children.Add(Divider());

        panel.Children.Add(ActionButton(LocalizationKeys.ButtonOpenSettingsWindow, () =>
        {
            Hide();
            _openSettings();
            return Task.CompletedTask;
        }));
        panel.Children.Add(ActionButton(LocalizationKeys.ButtonOpenActionsDashboard, () =>
        {
            Hide();
            _openActionsDashboard();
            return Task.CompletedTask;
        }));

        panel.Children.Add(ActionButton(LocalizationKeys.ButtonQuit, () =>
        {
            _quit();
            return Task.CompletedTask;
        }, compact: true));

        if (!string.IsNullOrWhiteSpace(_store.LastErrorMessage))
        {
            panel.Children.Add(new TextBlock
            {
                Text = _store.LastErrorMessage,
                FontSize = 12,
                Foreground = RedBrush,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private Control BuildHeader()
    {
        return new TextBlock
        {
            Text = T(LocalizationKeys.AppName),
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
    }

    private Control BuildRunnerRow(RunnerInstanceStore runner)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("22,*"),
            Height = 34
        };

        var dot = new TextBlock
        {
            Text = "●",
            FontSize = 12,
            Foreground = RunnerColor(runner),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(dot);
        Grid.SetColumn(dot, 0);

        var label = new TextBlock
        {
            Text = $"{runner.Profile.DisplayName} · {RunnerStatusText(runner)}",
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        grid.Children.Add(label);
        Grid.SetColumn(label, 1);

        return grid;
    }

    private Control BuildAdvancedSection()
    {
        var expander = new Expander
        {
            Header = T(LocalizationKeys.AdvancedViewTitle),
            IsExpanded = _isAdvancedOpen,
            Foreground = PrimaryTextBrush,
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
            Content = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(24, 5, 0, 0),
                Children =
                {
                    SmallStatusRow(LocalizationKeys.SettingsAdvancedCPU, $"{_store.ResourceUsage.CpuPercent:0.0}%"),
                    SmallStatusRow(LocalizationKeys.SettingsAdvancedMemory, FormatMemory(_store.ResourceUsage.TotalMemoryBytes)),
                    SmallStatusRow(LocalizationKeys.SettingsAdvancedProcesses, _store.ResourceUsage.ProcessCount.ToString()),
                    SmallStatusRow(LocalizationKeys.SettingsAdvancedJobActive, _store.ResourceUsage.IsJobActive ? T(LocalizationKeys.BooleanYes) : T(LocalizationKeys.BooleanNo))
                }
            }
        };

        expander.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(Expander.IsExpanded))
            {
                _isAdvancedOpen = expander.IsExpanded;
                Build();
            }
        };

        return expander;
    }

    private Control BuildLaunchAtLoginToggle()
    {
        var status = _launchAtLoginService.GetStatus();
        var checkbox = new CheckBox
        {
            Content = T(LocalizationKeys.ToggleLaunchAtLogin),
            IsChecked = status is LaunchAtLoginStatus.Enabled or LaunchAtLoginStatus.RequiresApproval,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryTextBrush
        };

        checkbox.IsCheckedChanged += async (_, _) =>
        {
            if (_isUpdatingLaunchAtLogin)
                return;

            _isUpdatingLaunchAtLogin = true;
            await _launchAtLoginService.SetEnabledAsync(checkbox.IsChecked == true);
            _isUpdatingLaunchAtLogin = false;
            Build();
        };

        return checkbox;
    }

    private Control StatusRow(string titleKey, string value, IBrush color)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("22,92,*"),
            Height = 27
        };

        var dot = new TextBlock
        {
            Text = "●",
            FontSize = 12,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(dot);
        Grid.SetColumn(dot, 0);

        var title = new TextBlock
        {
            Text = T(titleKey),
            FontSize = 12.5,
            FontWeight = FontWeight.Bold,
            Foreground = PrimaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        grid.Children.Add(title);
        Grid.SetColumn(title, 1);

        var detail = new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = SecondaryTextBrush,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        grid.Children.Add(detail);
        Grid.SetColumn(detail, 2);

        return grid;
    }

    private Control SmallStatusRow(string titleKey, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var title = new TextBlock
        {
            Text = T(titleKey),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        grid.Children.Add(title);
        Grid.SetColumn(title, 0);

        var detail = new TextBlock
        {
            Text = value,
            FontSize = 11,
            Foreground = SecondaryTextBrush,
            Margin = new Thickness(12, 0, 0, 0),
            TextAlignment = TextAlignment.Right
        };
        grid.Children.Add(detail);
        Grid.SetColumn(detail, 1);

        return grid;
    }

    private static string FormatMemory(long bytes)
    {
        var mb = bytes / 1024.0 / 1024.0;
        if (mb >= 1024)
            return $"{mb / 1024.0:0.0} GB";

        return $"{mb:0} MB";
    }

    private Button ActionButton(string key, Func<Task> action, bool prominent = false, bool compact = false)
    {
        var button = new Button
        {
            Content = T(key),
            Background = prominent ? AccentBrush : ButtonBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Padding = compact ? new Thickness(12, 4) : new Thickness(13, 5),
            Margin = new Thickness(0, 0, 8, 6)
        };

        button.Click += async (_, _) =>
        {
            try
            {
                await action();
                Build();
            }
            catch (Exception ex)
            {
                DiagnosticLog.WriteException($"Tray action failed: {T(key)}", ex);
                _store.LastErrorMessage = T(LocalizationKeys.ErrorRunnerHandling).Replace("{0}", ex.Message);
                Build();
            }
        };

        return button;
    }

    private Border Divider()
    {
        return new Border
        {
            Height = 1,
            Background = DividerBrush,
            Margin = new Thickness(0, 4)
        };
    }

    private string RunnerStatusText()
    {
        return _store.RunnerSnapshot.IsRunning ? T(LocalizationKeys.RunnerRunning) : T(LocalizationKeys.RunnerStopped);
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
            RunnerControlMode.ForceRunning or RunnerControlMode.ForceStopped => T(LocalizationKeys.ControlModeManual),
            _ => T(LocalizationKeys.ControlModeAutomatic)
        };
    }

    private string LaunchStatusText()
    {
        return _launchAtLoginService.GetStatus() switch
        {
            LaunchAtLoginStatus.Enabled => T(LocalizationKeys.LaunchAtLoginEnabled),
            LaunchAtLoginStatus.RequiresApproval => T(LocalizationKeys.LaunchAtLoginRequiresApproval),
            LaunchAtLoginStatus.Disabled => T(LocalizationKeys.LaunchAtLoginDisabled),
            LaunchAtLoginStatus.Unavailable => T(LocalizationKeys.LaunchAtLoginUnavailable),
            _ => T(LocalizationKeys.LaunchAtLoginUnknown)
        };
    }

    private IBrush RunnerColor()
    {
        return _store.RunnerSnapshot.IsRunning ? GreenBrush : RedBrush;
    }

    private IBrush RunnerColor(RunnerInstanceStore runner)
    {
        return runner.Snapshot.StatusKind switch
        {
            RunnerStatusKind.Busy => OrangeBrush,
            RunnerStatusKind.Waiting => GreenBrush,
            RunnerStatusKind.Running => GreenBrush,
            RunnerStatusKind.Error => RedBrush,
            _ => GrayBrush
        };
    }

    private IBrush ActivityColor()
    {
        return _store.RunnerSnapshot.Activity.Kind switch
        {
            RunnerActivityKind.Busy => OrangeBrush,
            RunnerActivityKind.Waiting => GrayBrush,
            _ => _store.RunnerSnapshot.IsRunning ? OrangeBrush : GrayBrush
        };
    }

    private IBrush NetworkColor()
    {
        return _store.NetworkSnapshot.Kind switch
        {
            NetworkConditionKind.Unmetered => GreenBrush,
            NetworkConditionKind.Expensive => OrangeBrush,
            NetworkConditionKind.Offline => RedBrush,
            _ => GrayBrush
        };
    }

    private IBrush LaunchStatusColor()
    {
        return _launchAtLoginService.GetStatus() switch
        {
            LaunchAtLoginStatus.Enabled => GreenBrush,
            LaunchAtLoginStatus.RequiresApproval => OrangeBrush,
            LaunchAtLoginStatus.Disabled => GrayBrush,
            LaunchAtLoginStatus.Unavailable => RedBrush,
            _ => GrayBrush
        };
    }

    private string T(string key)
    {
        var value = _localization.Get(key);
        return value == key && char.IsUpper(key.FirstOrDefault()) ? key : value;
    }

    private static string TrimText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text[..Math.Max(0, maxLength - 1)] + "…";
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isBuildQueued)
            return;

        _isBuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isBuildQueued = false;
            if (IsVisible)
                Build();
        }, DispatcherPriority.Background);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _ = HideAfterDeactivationAsync();
    }

    private async Task HideAfterDeactivationAsync()
    {
        await Task.Delay(180);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsActive && IsVisible)
                Hide();
        }, DispatcherPriority.Background);
    }

    private static PixelPoint? GetPointerLocation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return null;

        var cgEvent = CGEventCreate(IntPtr.Zero);
        if (cgEvent == IntPtr.Zero)
            return null;

        try
        {
            var point = CGEventGetLocation(cgEvent);
            return new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }
        finally
        {
            CFRelease(cgEvent);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr cgEvent);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}
