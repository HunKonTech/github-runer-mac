using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitRunnerManager.Core.Interfaces;
using GitRunnerManager.Core.Models;
using GitRunnerManager.Core.Localization;

namespace GitRunnerManager.Platform.Services;

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

            var isExpensive = IsExpensiveInterface(networkInterface);

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
        var nativeSnapshot = GetMacOsNativePathSnapshot();
        if (nativeSnapshot != null)
            return MacOsSnapshot(nativeSnapshot);

        var defaultInterface = GetMacOsDefaultInterface();
        if (string.IsNullOrWhiteSpace(defaultInterface))
        {
            var fallback = GetFallbackNetworkInterface();
            if (fallback == null)
            {
                return new NetworkConditionSnapshot
                {
                    Kind = NetworkConditionKind.Offline,
                    Description = _localization.Get(LocalizationKeys.NetworkNoInternet)
                };
            }

            return new NetworkConditionSnapshot
            {
                Kind = IsExpensiveInterface(fallback) ? NetworkConditionKind.Expensive : NetworkConditionKind.Unmetered,
                Description = _localization.Get(
                    IsExpensiveInterface(fallback) ? LocalizationKeys.NetworkMetered : LocalizationKeys.NetworkUnmetered,
                    GetInterfaceDescription(fallback))
            };
        }

        var scutilOutput = RunCommand("/usr/sbin/scutil", "--nwi");
        if (IsMacOsNetworkConstrained(scutilOutput))
            return MacOsSnapshot(NetworkConditionKind.Expensive, defaultInterface);

        var hardwarePort = GetMacOsHardwarePort(defaultInterface);
        if (IsMacOsPersonalHotspot(defaultInterface, hardwarePort))
            return MacOsSnapshot(NetworkConditionKind.Expensive, defaultInterface, hardwarePort);

        var isExpensive = IsMacOsExpensiveInterface(defaultInterface, hardwarePort, scutilOutput);

        return MacOsSnapshot(isExpensive ? NetworkConditionKind.Expensive : NetworkConditionKind.Unmetered, defaultInterface, hardwarePort);
    }

    private NetworkConditionSnapshot MacOsSnapshot(MacOsNativePathSnapshot snapshot)
    {
        if (!snapshot.Satisfied)
        {
            return new NetworkConditionSnapshot
            {
                Kind = NetworkConditionKind.Offline,
                Description = _localization.Get(LocalizationKeys.NetworkNoInternet)
            };
        }

        var kind = snapshot.Constrained || snapshot.Expensive
            ? NetworkConditionKind.Expensive
            : NetworkConditionKind.Unmetered;
        var localizationKey = kind == NetworkConditionKind.Expensive
            ? LocalizationKeys.NetworkMetered
            : LocalizationKeys.NetworkUnmetered;
        var interfaceType = string.IsNullOrWhiteSpace(snapshot.InterfaceType)
            ? "Connection"
            : snapshot.InterfaceType;

        return new NetworkConditionSnapshot
        {
            Kind = kind,
            Description = _localization.Get(localizationKey, interfaceType)
        };
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
            || port.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || port.Contains("WWAN", StringComparison.OrdinalIgnoreCase)
            || port.Contains("Cell", StringComparison.OrdinalIgnoreCase)
            || port.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
            || defaultInterface.StartsWith("pdp_ip", StringComparison.OrdinalIgnoreCase)
            || defaultInterface.StartsWith("ipsec", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMacOsPersonalHotspot(string defaultInterface, string? hardwarePort)
    {
        if (hardwarePort?.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) != true)
            return false;

        var address = RunCommand("/usr/sbin/ipconfig", $"getifaddr {defaultInterface}").Trim();
        if (address.StartsWith("172.20.10.", StringComparison.OrdinalIgnoreCase))
            return true;

        var routeOutput = RunCommand("/sbin/route", "-n get default");
        return routeOutput.Contains("gateway: 172.20.10.", StringComparison.OrdinalIgnoreCase);
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

    private static NetworkInterface? GetFallbackNetworkInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.GetIPProperties().GatewayAddresses.Count > 0)
            ?? NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
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

    private static MacOsNativePathSnapshot? GetMacOsNativePathSnapshot()
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "NativeHelpers", "MacNetworkPathHelper");
        if (!File.Exists(helperPath))
            return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = helperPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2500))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                return null;
            }

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            return JsonSerializer.Deserialize<MacOsNativePathSnapshot>(output);
        }
        catch
        {
            return null;
        }
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

    private static bool IsExpensiveInterface(NetworkInterface networkInterface)
    {
        return networkInterface.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2;
    }
}

internal class MacOsNativePathSnapshot
{
    [JsonPropertyName("satisfied")]
    public bool Satisfied { get; set; }

    [JsonPropertyName("expensive")]
    public bool Expensive { get; set; }

    [JsonPropertyName("constrained")]
    public bool Constrained { get; set; }

    [JsonPropertyName("interfaceType")]
    public string InterfaceType { get; set; } = string.Empty;

    [JsonPropertyName("interfaceName")]
    public string InterfaceName { get; set; } = string.Empty;
}
