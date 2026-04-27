using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GitHubRunnerTray.Core.Localization;
using GitHubRunnerTray.Core.Services;

namespace GitHubRunnerTray.App;

public partial class SettingsWindow : Window
{
    private ILocalizationService? _localization;
    private RunnerTrayStore? _store;

    public SettingsWindow()
    {
        Title = "Settings";
        Width = 500;
        Height = 400;
    }

    public void InitializeStore(RunnerTrayStore store, ILocalizationService localization)
    {
        _store = store;
        _localization = localization;

        BuildUI();
    }

    private void BuildUI()
    {
        var tabControl = new TabControl();

        var generalTab = new TabItem { Header = "General" };
        generalTab.Content = BuildGeneralPanel();

        var runnerTab = new TabItem { Header = "Runner" };
        runnerTab.Content = BuildRunnerPanel();

        var updatesTab = new TabItem { Header = "Updates" };
        updatesTab.Content = BuildUpdatesPanel();

        var networkTab = new TabItem { Header = "Network" };
        networkTab.Content = BuildNetworkPanel();

        var aboutTab = new TabItem { Header = "About" };
        aboutTab.Content = BuildAboutPanel();

        tabControl.Items.Add(generalTab);
        tabControl.Items.Add(runnerTab);
        tabControl.Items.Add(updatesTab);
        tabControl.Items.Add(networkTab);
        tabControl.Items.Add(aboutTab);

        Content = tabControl;
    }

    private Control BuildGeneralPanel()
    {
        var panel = new StackPanel { Margin = Avalonia.Thickness.Parse("16"), Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = _localization?.Get(LocalizationKeys.SettingsGeneralTitle) ?? "General",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        panel.Children.Add(new TextBlock { Text = "Language" });
        var langCombo = new ComboBox { Width = 200 };
        langCombo.Items.Add(new ComboBoxItem { Content = "System default" });
        langCombo.Items.Add(new ComboBoxItem { Content = "Hungarian" });
        langCombo.Items.Add(new ComboBoxItem { Content = "English" });
        langCombo.SelectedIndex = 0;
        panel.Children.Add(langCombo);

        panel.Children.Add(new CheckBox { Content = "Launch automatically at login" });
        panel.Children.Add(new CheckBox { Content = "Pause runner on battery" });

        return panel;
    }

    private Control BuildRunnerPanel()
    {
        var panel = new StackPanel { Margin = Avalonia.Thickness.Parse("16"), Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = _localization?.Get(LocalizationKeys.SettingsRunnerTitle) ?? "Runner",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        panel.Children.Add(new TextBlock { Text = "Runner folder" });
        panel.Children.Add(new TextBox { IsReadOnly = true, Text = "Not configured" });

        panel.Children.Add(new TextBlock { Text = "Status" });
        panel.Children.Add(new TextBox { IsReadOnly = true, Text = GetRunnerStatus() });

        var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        buttonPanel.Children.Add(new Button { Content = "Start", Width = 80 });
        buttonPanel.Children.Add(new Button { Content = "Stop", Width = 80 });
        buttonPanel.Children.Add(new Button { Content = "Refresh", Width = 80 });
        panel.Children.Add(buttonPanel);

        return panel;
    }

    private Control BuildUpdatesPanel()
    {
        var panel = new StackPanel { Margin = Avalonia.Thickness.Parse("16"), Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = _localization?.Get(LocalizationKeys.SettingsUpdatesTitle) ?? "Updates",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        panel.Children.Add(new TextBlock { Text = "Current version" });
        panel.Children.Add(new TextBox { IsReadOnly = true, Text = "1.0.0" });
        panel.Children.Add(new TextBlock { Text = "Ready to check for updates." });

        return panel;
    }

    private Control BuildNetworkPanel()
    {
        var panel = new StackPanel { Margin = Avalonia.Thickness.Parse("16"), Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = _localization?.Get(LocalizationKeys.SettingsNetworkTitle) ?? "Network",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        panel.Children.Add(new TextBlock { Text = "Current network" });
        panel.Children.Add(new TextBox { IsReadOnly = true, Text = _store?.NetworkSnapshot.Description ?? "Unknown" });

        panel.Children.Add(new TextBlock { Text = "Network policy" });
        panel.Children.Add(new TextBox { IsReadOnly = true, Text = _store?.PolicySummary ?? "Automatic mode" });

        return panel;
    }

    private Control BuildAboutPanel()
    {
        var panel = new StackPanel { Margin = Avalonia.Thickness.Parse("16"), Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = "GitHub Runner Tray",
            FontSize = 24,
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        panel.Children.Add(new TextBlock { Text = "Version 1.0.0" });
        panel.Children.Add(new TextBlock { Text = "" });
        panel.Children.Add(new TextBlock { Text = "Created by Benedek Koncsik." });
        panel.Children.Add(new TextBlock { Text = "MIT License" });

        return panel;
    }

    private string GetRunnerStatus()
    {
        if (_store == null || _localization == null) return "Unknown";

        return _store.RunnerSnapshot.IsRunning
            ? _localization.Get(LocalizationKeys.RunnerRunning)
            : _localization.Get(LocalizationKeys.RunnerStopped);
    }
}