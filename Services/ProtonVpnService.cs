using System.Diagnostics;
using System.Text.Json;
using HermesHub.Helpers;

namespace HermesHub.Services;

public sealed class ProtonVpnRotationResult
{
    public bool Success { get; set; }
    public string Provider { get; set; } = "ProtonVPN";
    public DualStackIpDto Old { get; set; } = new();
    public DualStackIpDto New { get; set; } = new();
    public string OldIp { get; set; } = "unknown";
    public string NewIp { get; set; } = "unknown";
    public string CountryCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryEmoji { get; set; } = "🌐";
    public string City { get; set; } = string.Empty;
    public int CooldownSeconds { get; set; } = 60;
    public string Message { get; set; } = string.Empty;
}

public sealed class ProtonVpnService
{
    private readonly OpenCodeWatcherService? _openCodeWatcher;
    private DateTime _lastRotationTime = DateTime.MinValue;
    private readonly object _rotationLock = new();

    public ProtonVpnService(OpenCodeWatcherService? openCodeWatcher = null)
    {
        _openCodeWatcher = openCodeWatcher;
    }

    public string? FindProtonExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidatePaths = new List<string>
        {
            Path.Combine(programFiles, "Proton", "VPN", "ProtonVPN.Launcher.exe"),
            Path.Combine(programFiles, "Proton", "VPN", "ProtonVPN.exe"),
            Path.Combine(localAppData, "Proton", "VPN", "ProtonVPN.Launcher.exe"),
            Path.Combine(localAppData, "ProtonVPN", "ProtonVPN.exe"),
            Path.Combine(programFilesX86, "Proton", "VPN", "ProtonVPN.Launcher.exe")
        };

        // Also check versioned folders in Program Files (e.g., C:\Program Files\Proton\VPN\v5.1.8\ProtonVPN.Client.exe)
        var protonBase = Path.Combine(programFiles, "Proton", "VPN");
        if (Directory.Exists(protonBase))
        {
            try
            {
                var versionDirs = Directory.GetDirectories(protonBase, "v*")
                    .OrderByDescending(d => d);
                foreach (var vDir in versionDirs)
                {
                    candidatePaths.Add(Path.Combine(vDir, "ProtonVPN.Client.exe"));
                }
            }
            catch { }
        }

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public bool IsClientRunning()
    {
        return Process.GetProcessesByName("ProtonVPN.Client").Length > 0 ||
               Process.GetProcessesByName("ProtonVPN").Length > 0 ||
               Process.GetProcessesByName("ProtonVPN.Launcher").Length > 0;
    }

    public async Task<bool> LaunchClientAsync()
    {
        if (IsClientRunning()) return true;

        var exe = FindProtonExecutable();
        if (string.IsNullOrWhiteSpace(exe)) return false;

        var dir = Path.GetDirectoryName(exe) ?? string.Empty;

        // Method 1: Launch via explorer.exe to guarantee interactive desktop GUI invocation
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{exe}\"",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            await Task.Delay(2500);
            if (IsClientRunning()) return true;
        }
        catch { }

        // Method 2: ShellExecute with explicit working directory
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = dir,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
            proc.Start();
            await Task.Delay(2500);
            return IsClientRunning();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ConnectAsync()
    {
        var exe = FindProtonExecutable();
        if (string.IsNullOrWhiteSpace(exe)) return false;

        try
        {
            if (!IsClientRunning())
            {
                await LaunchClientAsync();
            }

            // Attempt to trigger connection via URI scheme
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c start protonvpn://connect",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                proc.Start();
            }
            catch { }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            // Attempt to trigger disconnect via URI scheme
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c start protonvpn://disconnect",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                proc.Start();
            }
            catch { }

            // Flush DNS to clear routing cache
            await NetworkHelper.FlushDnsAsync();
            await Task.Delay(1000);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ProtonVpnRotationResult> RotateServerAsync(Action<string>? onProgress = null)
    {
        // Enforce cooldown check
        lock (_rotationLock)
        {
            var elapsed = (DateTime.UtcNow - _lastRotationTime).TotalSeconds;
            if (elapsed < 60.0)
            {
                int remaining = (int)Math.Ceiling(60.0 - elapsed);
                return new ProtonVpnRotationResult
                {
                    Success = false,
                    Message = $"Proton VPN cooldown active. Please wait {remaining}s before switching servers.",
                    CooldownSeconds = remaining
                };
            }
            _lastRotationTime = DateTime.UtcNow;
        }

        var report = new ProtonVpnRotationResult
        {
            Provider = "ProtonVPN",
            CooldownSeconds = 60
        };

        onProgress?.Invoke("Proton VPN: Capturing initial IP before rotation...");
        var initialDual = await NetworkHelper.FetchDualStackIpsAsync(3000);
        report.Old = initialDual;
        report.OldIp = !string.IsNullOrWhiteSpace(initialDual.Ipv4) && initialDual.Ipv4 != "unknown"
            ? initialDual.Ipv4
            : (!string.IsNullOrWhiteSpace(initialDual.Ipv6) && initialDual.Ipv6 != "Not assigned" ? initialDual.Ipv6 : "unknown");

        // Ensure Proton client GUI is running so user sees the window
        if (!IsClientRunning())
        {
            onProgress?.Invoke("Proton VPN: Launching Proton VPN GUI...");
            await LaunchClientAsync();
            await Task.Delay(2000);
        }

        onProgress?.Invoke("Proton VPN: Triggering connection attempt...");
        await ConnectAsync();

        onProgress?.Invoke("Proton VPN: Flushing DNS cache...");
        await NetworkHelper.FlushDnsAsync();

        onProgress?.Invoke("Proton VPN: Polling for new IP assignment (max 14s)...");
        DualStackIpDto? newDual = null;
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(1500);
            var probe = await NetworkHelper.FetchDualStackIpsAsync(2500);
            bool hasV4 = !string.IsNullOrWhiteSpace(probe.Ipv4) && probe.Ipv4 != "unknown";
            bool hasV6 = !string.IsNullOrWhiteSpace(probe.Ipv6) && probe.Ipv6 != "Not assigned";

            bool v4Changed = hasV4 && (report.Old.Ipv4 == "unknown" || probe.Ipv4 != report.Old.Ipv4);
            bool v6Changed = hasV6 && (report.Old.Ipv6 == "Not assigned" || probe.Ipv6 != report.Old.Ipv6);

            if (v4Changed || v6Changed)
            {
                newDual = probe;
                break;
            }
        }

        if (newDual != null)
        {
            report.New = newDual;
            report.NewIp = !string.IsNullOrWhiteSpace(newDual.Ipv4) && newDual.Ipv4 != "unknown"
                ? newDual.Ipv4
                : (!string.IsNullOrWhiteSpace(newDual.Ipv6) && newDual.Ipv6 != "Not assigned" ? newDual.Ipv6 : "unknown");

            // Fetch fresh geolocation details
            var geoDetails = await NetworkHelper.FetchPublicIpDetailsAsync();
            report.CountryCode = geoDetails.CountryCode;
            report.Country = geoDetails.Country;
            report.CountryEmoji = geoDetails.CountryEmoji;
            report.City = geoDetails.City;

            // Sync OpenCode Sidecar only on successful rotation
            if (_openCodeWatcher != null && _openCodeWatcher.IsOnline)
            {
                _ = _openCodeWatcher.RefreshAsync();
            }

            report.Success = true;
            report.Message = $"Proton VPN server rotated successfully. New IP: {report.NewIp} ({report.Country})";
            onProgress?.Invoke(report.Message);
        }
        else
        {
            report.Success = false;
            report.New = report.Old;
            report.NewIp = report.OldIp;
            report.Message = "Proton VPN did not establish a new tunnel within 14s. Please click 'Connect' in the Proton VPN window, then click Reset.";
            onProgress?.Invoke(report.Message);
        }

        return report;
    }
}
