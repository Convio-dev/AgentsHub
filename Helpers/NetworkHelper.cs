using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HermesHub.Helpers;

public static class NetworkHelper
{
    public static string GetLocalIPv4()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                          nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .ThenBy(nic => IsVirtualAdapter(nic.Description))
                .ToList();

            foreach (var nic in interfaces)
            {
                var ipProps = nic.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicast.Address))
                    {
                        var ipStr = unicast.Address.ToString();
                        // Filter out APIPA addresses (169.254.x.x)
                        if (!ipStr.StartsWith("169.254."))
                        {
                            return ipStr;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back to loopback on permission or socket query failures
        }

        return "127.0.0.1";
    }

    public static string GetLanUrl(int port = 9191)
    {
        var ip = GetLocalIPv4();
        return $"http://{ip}:{port}";
    }

    private static bool IsVirtualAdapter(string description)
    {
        var desc = description.ToLowerInvariant();
        return desc.Contains("virtual") ||
               desc.Contains("hyper-v") ||
               desc.Contains("vethernet") ||
               desc.Contains("wsl") ||
               desc.Contains("vmware") ||
               desc.Contains("npcap");
    }
}
