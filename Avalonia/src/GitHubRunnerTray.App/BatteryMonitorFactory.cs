using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Platform.Services;

namespace GitHubRunnerTray.App;

public class BatteryMonitorFactory : IBatteryMonitorFactory
{
    public IBatteryMonitor Create() => new BatteryMonitor();
}