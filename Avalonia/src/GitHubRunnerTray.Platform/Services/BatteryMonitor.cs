using System.Management;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Platform.Services;

public class BatteryMonitor : IBatteryMonitor, IDisposable
{
    private bool _disposed;

    public event EventHandler<BatterySnapshot>? OnChange;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    public void Start()
    {
        var snapshot = GetCurrentSnapshot();
        OnChange?.Invoke(this, snapshot);
    }

    public void Stop()
    {
        // Already handled by Dispose
    }

    private BatterySnapshot GetCurrentSnapshot()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return GetWindowsBattery();
            else if (OperatingSystem.IsMacOS())
                return GetMacBattery();
            else if (OperatingSystem.IsLinux())
                return GetLinuxBattery();

            return new BatterySnapshot { HasBattery = false };
        }
        catch
        {
            return new BatterySnapshot { HasBattery = false };
        }
    }

    private BatterySnapshot GetWindowsBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            foreach (ManagementObject battery in searcher.Get())
            {
                var batteryStatus = battery["BatteryStatus"];
                var estimatedChargeRemaining = battery["EstimatedChargeRemaining"];

                var isOnBattery = batteryStatus != null && (batteryStatus.ToString() == "1" || batteryStatus.ToString() == "2");
                var isCharging = batteryStatus != null && batteryStatus.ToString() == "2";

                return new BatterySnapshot
                {
                    IsOnBattery = isOnBattery,
                    IsCharging = isCharging,
                    HasBattery = true
                };
            }

            return new BatterySnapshot { HasBattery = false };
        }
        catch
        {
            return new BatterySnapshot { HasBattery = false };
        }
    }

    private BatterySnapshot GetMacBattery()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pmset",
                Arguments = "-g battstatus",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                return new BatterySnapshot { HasBattery = false };

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var hasBattery = output.Contains("Battery Present");
            var isCharging = output.Contains("IsCharging = Yes");
            var isOnBattery = output.Contains("Power Source") && !output.Contains("AC Power");

            return new BatterySnapshot
            {
                HasBattery = hasBattery,
                IsOnBattery = isOnBattery,
                IsCharging = isCharging
            };
        }
        catch
        {
            return new BatterySnapshot { HasBattery = false };
        }
    }

    private BatterySnapshot GetLinuxBattery()
    {
        try
        {
            var powerSupplyPath = "/sys/class/power_supply";
            if (!Directory.Exists(powerSupplyPath))
                return new BatterySnapshot { HasBattery = false };

            var batteries = Directory.GetDirectories(powerSupplyPath)
                .Where(d => File.Exists(Path.Combine(d, "uevent"))).ToList();

            foreach (var battery in batteries)
            {
                var ueventPath = Path.Combine(battery, "uevent");
                if (!File.Exists(ueventPath))
                    continue;

                var content = File.ReadAllText(ueventPath);
                if (!content.Contains("POWER_SUPPLY_TYPE=Battery"))
                    continue;

                var statusPath = Path.Combine(battery, "status");
                var status = File.Exists(statusPath) ? File.ReadAllText(statusPath).Trim() : "";

                var isCharging = status == "Charging";
                var isOnBattery = status == "Discharging";

                return new BatterySnapshot
                {
                    HasBattery = true,
                    IsOnBattery = isOnBattery,
                    IsCharging = isCharging
                };
            }

            return new BatterySnapshot { HasBattery = false };
        }
        catch
        {
            return new BatterySnapshot { HasBattery = false };
        }
    }
}