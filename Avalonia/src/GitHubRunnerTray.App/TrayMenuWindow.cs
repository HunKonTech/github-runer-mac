using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Localization;
using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Core.Services;
using System.Runtime.InteropServices;

namespace GitHubRunnerTray.App;

public sealed class TrayMenuWindow : Window
{
    private const double PopoverWidth = 390;
    private const double CollapsedPopoverHeight = 470;
    private const double ExpandedPopoverHeight = 560;

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
    private readonly Action _quit;
    private bool _isUpdatingLaunchAtLogin;
    private bool _isAdvancedOpen;

    public TrayMenuWindow(
        RunnerTrayStore store,
        ILocalizationService localization,
        ILaunchAtLoginService launchAtLoginService,
        Func<Task> startRunner,
        Func<Task> stopRunner,
        Func<Task> automaticMode,
        Func<Task> refresh,
        Action openSettings,
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
        _quit = quit;

        ApplyResponsiveSize();
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
                Content = BuildContent()
            }
        };

        Content = root;
    }

    private void ApplyResponsiveSize()
    {
        var targetHeight = _isAdvancedOpen ? ExpandedPopoverHeight : CollapsedPopoverHeight;
        var pointer = GetPointerLocation();
        var screen = pointer.HasValue ? Screens.ScreenFromPoint(pointer.Value) : Screens.Primary;
        var availableWidth = screen?.WorkingArea.Width - 16 ?? PopoverWidth;
        var availableHeight = screen?.WorkingArea.Height - 16 ?? targetHeight;
        var width = Math.Clamp(PopoverWidth, 320, Math.Max(320, availableWidth));
        var height = Math.Clamp(targetHeight, 360, Math.Max(360, availableHeight));

        Width = width;
        Height = height;
        MinWidth = width;
        MinHeight = height;
        MaxWidth = width;
        MaxHeight = height;
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

        panel.Children.Add(StatusRow(LocalizationKeys.StatusRunnerTitle, RunnerStatusText(), RunnerColor()));
        panel.Children.Add(StatusRow(LocalizationKeys.StatusActivityTitle, _store.RunnerSnapshot.Activity.Description, ActivityColor()));
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
        actions.Children.Add(ActionButton(LocalizationKeys.ButtonManualStart, _startRunner, true));
        actions.Children.Add(ActionButton(LocalizationKeys.ButtonManualStop, _stopRunner));
        actions.Children.Add(ActionButton(LocalizationKeys.ButtonAutomaticMode, _automaticMode));
        actions.Children.Add(ActionButton(LocalizationKeys.ButtonRefresh, _refresh));
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

    private string ControlModeText(RunnerControlMode mode)
    {
        return mode switch
        {
            RunnerControlMode.ForceRunning => T(LocalizationKeys.ControlModeForceRunning),
            RunnerControlMode.ForceStopped => T(LocalizationKeys.ControlModeForceStopped),
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
        return _localization.Get(key);
    }

    private static string TrimText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text[..Math.Max(0, maxLength - 1)] + "…";
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(Build);
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
