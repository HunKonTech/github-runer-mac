using System.Net.NetworkInformation;
using GitHubRunnerTray.Core.Interfaces;
using GitHubRunnerTray.Core.Models;
using GitHubRunnerTray.Core.Localization;

namespace GitHubRunnerTray.Platform.Services;

public class NetworkConditionMonitor : INetworkConditionMonitor, IDisposable
{
    private readonly ILocalizationService _localization;
    private NetworkChange? _networkChange;
    private bool _disposed;

    public event EventHandler<NetworkConditionSnapshot>? OnChange;

    public NetworkConditionMonitor(ILocalizationService localization)
    {
        _localization = localization;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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