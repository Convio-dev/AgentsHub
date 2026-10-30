using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentsHub.Helpers;

public sealed class DualStackIpDto
{
    [JsonPropertyName("ipv4")]
    public string? Ipv4 { get; set; }

    [JsonPropertyName("ipv6")]
    public string? Ipv6 { get; set; }
}

public sealed class PublicIpDetailsDto
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("ipv4")]
    public string? Ipv4 { get; set; }

    [JsonPropertyName("ipv6")]
    public string? Ipv6 { get; set; }

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("countryEmoji")]
    public string CountryEmoji { get; set; } = "🌐";

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;
}

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

    public static string CountryCodeToEmoji(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2) return "🌐";
        countryCode = countryCode.ToUpperInvariant();
        try
        {
            int first = 0x1F1E6 + (countryCode[0] - 'A');
            int second = 0x1F1E6 + (countryCode[1] - 'A');
            return char.ConvertFromUtf32(first) + char.ConvertFromUtf32(second);
        }
        catch
        {
            return "🌐";
        }
    }

    private static readonly HttpClient _publicIpHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<DualStackIpDto> FetchDualStackIpsAsync(int timeoutMs = 4000)
    {
        var dto = new DualStackIpDto();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

        var v4Task = FetchDedicatedIpv4Async(cts.Token);
        var v6Task = FetchDedicatedIpv6Async(cts.Token);

        await Task.WhenAll(v4Task, v6Task);

        dto.Ipv4 = await v4Task;
        dto.Ipv6 = await v6Task;
        return dto;
    }

    public static async Task<string?> FetchDedicatedIpv4Async(CancellationToken cancellationToken = default)
    {
        var v4Endpoints = new[]
        {
            "https://api4.ipify.org",
            "https://v4.ident.me",
            "https://ipv4.icanhazip.com"
        };

        foreach (var url in v4Endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await _publicIpHttpClient.SendAsync(req, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    var text = (await resp.Content.ReadAsStringAsync(cancellationToken)).Trim();
                    if (IPAddress.TryParse(text, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return text;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    public static async Task<string?> FetchDedicatedIpv6Async(CancellationToken cancellationToken = default)
    {
        var v6Endpoints = new[]
        {
            "https://api6.ipify.org",
            "https://v6.ident.me",
            "https://ipv6.icanhazip.com"
        };

        foreach (var url in v6Endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await _publicIpHttpClient.SendAsync(req, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    var text = (await resp.Content.ReadAsStringAsync(cancellationToken)).Trim();
                    if (IPAddress.TryParse(text, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        return text;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    public static async Task<PublicIpDetailsDto> FetchPublicIpDetailsAsync()
    {
        var dto = new PublicIpDetailsDto();
        var dualTask = FetchDualStackIpsAsync(4000);

        // 1. Query ipwho.is for IP and rich Geolocation
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://ipwho.is/");
            using var resp = await _publicIpHttpClient.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("ip", out var ipEl)) dto.Ip = ipEl.GetString() ?? string.Empty;
                if (root.TryGetProperty("country_code", out var ccEl)) dto.CountryCode = ccEl.GetString() ?? string.Empty;
                if (root.TryGetProperty("country", out var cEl)) dto.Country = cEl.GetString() ?? string.Empty;
                if (root.TryGetProperty("city", out var cityEl)) dto.City = cityEl.GetString() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(dto.CountryCode))
                {
                    dto.CountryEmoji = CountryCodeToEmoji(dto.CountryCode);
                }
            }
        }
        catch { }

        // 2. Query 1.1.1.1/cdn-cgi/trace if country code missing or IP missing
        if (string.IsNullOrWhiteSpace(dto.CountryCode) || string.IsNullOrWhiteSpace(dto.Ip))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://1.1.1.1/cdn-cgi/trace");
                using var resp = await _publicIpHttpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var trace = await resp.Content.ReadAsStringAsync();
                    var lines = trace.Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(dto.Ip) && trimmed.StartsWith("ip=", StringComparison.OrdinalIgnoreCase))
                        {
                            dto.Ip = trimmed[3..].Trim();
                        }
                        else if (string.IsNullOrWhiteSpace(dto.CountryCode) && trimmed.StartsWith("loc=", StringComparison.OrdinalIgnoreCase))
                        {
                            dto.CountryCode = trimmed[4..].Trim().ToUpperInvariant();
                            dto.CountryEmoji = CountryCodeToEmoji(dto.CountryCode);
                            dto.Country = dto.CountryCode;
                        }
                        else if (string.IsNullOrWhiteSpace(dto.City) && trimmed.StartsWith("colo=", StringComparison.OrdinalIgnoreCase))
                        {
                            dto.City = trimmed[5..].Trim();
                        }
                    }
                }
            }
            catch { }
        }

        var dual = await dualTask;
        dto.Ipv4 = dual.Ipv4 ?? (IPAddress.TryParse(dto.Ip, out var parsedIp) && parsedIp.AddressFamily == AddressFamily.InterNetwork ? dto.Ip : null);
        dto.Ipv6 = dual.Ipv6;

        if (string.IsNullOrWhiteSpace(dto.Ip))
        {
            dto.Ip = dto.Ipv4 ?? dto.Ipv6 ?? "Unavailable";
        }

        return dto;
    }

    public static async Task<bool> FlushDnsAsync()
    {
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true
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
