using Avalonia;
using Avalonia.Controls;

namespace GitHubRunnerTray.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About GitHub Runner Tray";
        Width = 400;
        Height = 250;
        Content = new StackPanel
        {
            Margin = Avalonia.Thickness.Parse("20"),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "GitHub Runner Tray",
                    FontSize = 24,
                    FontWeight = Avalonia.Media.FontWeight.Bold
                },
                new TextBlock { Text = "Version 1.0.0" },
                new TextBlock { Text = "" },
                new TextBlock { Text = "Created by Benedek Koncsik" },
                new TextBlock { Text = "MIT License" },
                new TextBlock { Text = "" },
                new TextBlock
                {
                    Text = "A menu bar app for managing a local GitHub Actions self-hosted runner."
                }
            }
        };
    }
}