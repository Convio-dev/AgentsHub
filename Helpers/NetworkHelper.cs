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

    private static readonly HttpClient _publicIpHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<string?> FetchPublicIpAsync()
    {
        var providers = new[]
        {
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://ifconfig.me/ip",
            "https://checkip.amazonaws.com"
        };

        foreach (var url in providers)
        {
            try
            {
                var response = await _publicIpHttpClient.GetStringAsync(url);
                var ip = response.Trim();
                if (IPAddress.TryParse(ip, out _))
                {
                    return ip;
                }
            }
            catch
            {
                // Try next provider
            }
        }

        return null;
    }

    public static async Task<string> RotateCloudflareWarpIpAsync()
    {
        try
        {
            await RunWarpCliCommandAsync("disconnect");
            await Task.Delay(1500);

            var connected = await RunWarpCliCommandAsync("connect");
            if (!connected)
            {
                await Task.Delay(1000);
                await RunWarpCliCommandAsync("connect");
            }

            string? newIp = null;
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000);
                newIp = await FetchPublicIpAsync();
                if (!string.IsNullOrWhiteSpace(newIp)) break;
            }

            return newIp ?? "Rotation completed";
        }
        catch (Exception ex)
        {
            return "Failed: " + ex.Message;
        }
    }

    private static async Task<bool> RunWarpCliCommandAsync(string arguments)
    {
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "warp-cli.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            proc.Start();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
