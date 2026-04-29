using System.Net.NetworkInformation;
using System.Diagnostics;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Core.Localization;

namespace GitHubRunnerTray.Platform.Services;

public class NetworkConditionMonitor : INetworkConditionMonitor, IDisposable
{
    private readonly ILocalizationService _localization;
    private bool _disposed;

    public event EventHandler<NetworkConditionSnapshot>? OnChange;

    public NetworkConditionMonitor(ILocalizationService localization)
    {
        _localization = localization;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
    }

    public void Start()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        var snapshot = GetCurrentSnapshot();
        OnChange?.Invoke(this, snapshot);
    }

    public void Stop()
    {
        if (_disposed) return;
        _disposed = true;

        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var snapshot = GetCurrentSnapshot();
        OnChange?.Invoke(this, snapshot);
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        var snapshot = GetCurrentSnapshot();
        OnChange?.Invoke(this, snapshot);
    }

    private NetworkConditionSnapshot GetCurrentSnapshot()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                return GetMacOsSnapshot();

            var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            if (networkInterface == null)
            {
                return new NetworkConditionSnapshot
                {
                    Kind = NetworkConditionKind.Offline,
                    Description = _localization.Get(LocalizationKeys.NetworkNoInternet)
                };
            }

            var interfaceType = GetInterfaceDescription(networkInterface);

            // Check for expensive network by interface type
            var isExpensive = networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

            if (isExpensive)
            {
                return new NetworkConditionSnapshot
                {
                    Kind = NetworkConditionKind.Expensive,
                    Description = _localization.Get(LocalizationKeys.NetworkMetered, interfaceType)
                };
            }

            return new NetworkConditionSnapshot
            {
                Kind = NetworkConditionKind.Unmetered,
                Description = _localization.Get(LocalizationKeys.NetworkUnmetered, interfaceType)
            };
        }
        catch
        {
            return new NetworkConditionSnapshot
            {
                Kind = NetworkConditionKind.Unknown,
                Description = _localization.Get(LocalizationKeys.NetworkChecking)
            };
        }
    }

    private NetworkConditionSnapshot GetMacOsSnapshot()
    {
        var defaultInterface = GetMacOsDefaultInterface();
        if (string.IsNullOrWhiteSpace(defaultInterface))
        {
            return new NetworkConditionSnapshot
            {
                Kind = NetworkConditionKind.Offline,
                Description = _localization.Get(LocalizationKeys.NetworkNoInternet)
            };
        }

        var scutilOutput = RunCommand("/usr/sbin/scutil", "--nwi");
        if (IsMacOsNetworkConstrained(scutilOutput))
            return MacOsSnapshot(NetworkConditionKind.Expensive, defaultInterface);

        var hardwarePort = GetMacOsHardwarePort(defaultInterface);
        var isExpensive = IsMacOsExpensiveInterface(defaultInterface, hardwarePort, scutilOutput);

        return MacOsSnapshot(isExpensive ? NetworkConditionKind.Expensive : NetworkConditionKind.Unmetered, defaultInterface, hardwarePort);
    }

    private NetworkConditionSnapshot MacOsSnapshot(NetworkConditionKind kind, string defaultInterface, string? hardwarePort = null)
    {
        var interfaceType = GetMacOsInterfaceDescription(defaultInterface, hardwarePort);
        var localizationKey = kind == NetworkConditionKind.Expensive
            ? LocalizationKeys.NetworkMetered
            : LocalizationKeys.NetworkUnmetered;

        return new NetworkConditionSnapshot
        {
            Kind = kind,
            Description = _localization.Get(localizationKey, interfaceType)
        };
    }

    private static bool IsMacOsNetworkConstrained(string output)
    {
        return output.Contains("expensive", StringComparison.OrdinalIgnoreCase)
            || output.Contains("constrained", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMacOsExpensiveInterface(string defaultInterface, string? hardwarePort, string scutilOutput)
    {
        var port = hardwarePort ?? string.Empty;
        return IsMacOsNetworkConstrained(scutilOutput)
            || port.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
            || port.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || port.Contains("WWAN", StringComparison.OrdinalIgnoreCase)
            || port.Contains("Cell", StringComparison.OrdinalIgnoreCase)
            || port.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
            || defaultInterface.StartsWith("pdp_ip", StringComparison.OrdinalIgnoreCase)
            || defaultInterface.StartsWith("ipsec", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMacOsInterfaceDescription(string defaultInterface, string? hardwarePort)
    {
        if (!string.IsNullOrWhiteSpace(hardwarePort))
            return hardwarePort;

        return defaultInterface.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "Ethernet" : "Connection";
    }

    private static string? GetMacOsDefaultInterface()
    {
        var output = RunCommand("/sbin/route", "-n get default");
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("interface:", StringComparison.OrdinalIgnoreCase))
                continue;

            return trimmed["interface:".Length..].Trim();
        }

        return null;
    }

    private static string? GetMacOsHardwarePort(string defaultInterface)
    {
        var output = RunCommand("/usr/sbin/networksetup", "-listallhardwareports");
        string? currentPort = null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Hardware Port:", StringComparison.OrdinalIgnoreCase))
            {
                currentPort = trimmed["Hardware Port:".Length..].Trim();
                continue;
            }

            if (!trimmed.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                continue;

            var device = trimmed["Device:".Length..].Trim();
            if (string.Equals(device, defaultInterface, StringComparison.OrdinalIgnoreCase))
                return currentPort;
        }

        return null;
    }

    private static string RunCommand(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process == null)
            return string.Empty;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);
        return output;
    }

    private static string GetInterfaceDescription(NetworkInterface ni)
    {
        return ni.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Ethernet => "Ethernet",
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            _ => "Connection"
        };
    }
}
