using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Platform.Services;

namespace GitRunnerManager.App;

public class BatteryMonitorFactory : IBatteryMonitorFactory
{
    public IBatteryMonitor Create() => new BatteryMonitor();
}