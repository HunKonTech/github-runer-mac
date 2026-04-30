using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GitRunnerManager.Core.Localization;

namespace GitRunnerManager.App;

public sealed class InitializingTrayWindow : Window
{
    private const double PopoverWidth = 390;
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static readonly SolidColorBrush PanelBrush = Brush("#171717");
    private static readonly SolidColorBrush PanelBorderBrush = Brush("#3f3f3f");
    private static readonly SolidColorBrush PrimaryTextBrush = Brush("#f0f0f0");
    private static readonly SolidColorBrush SecondaryTextBrush = Brush("#aaaaaa");

    private readonly ILocalizationService _localization;

    public InitializingTrayWindow(ILocalizationService localization)
    {
        _localization = localization;
        Width = PopoverWidth;
        MinWidth = PopoverWidth;
        MaxWidth = PopoverWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
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

    private void Build()
    {
        Content = new Border
        {
            Background = PanelBrush,
            BorderBrush = PanelBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = _localization.Get(LocalizationKeys.AppInitializingTitle),
                        FontSize = 15,
                        FontWeight = FontWeight.Bold,
                        Foreground = PrimaryTextBrush
                    },
                    new ProgressBar
                    {
                        IsIndeterminate = true,
                        Height = 4,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    },
                    new TextBlock
                    {
                        Text = _localization.Get(LocalizationKeys.AppInitializingDescription),
                        FontSize = 12.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = SecondaryTextBrush,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
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
