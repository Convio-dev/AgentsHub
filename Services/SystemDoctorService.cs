#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class DiagnosticCheckItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "Runtime"; // Runtime, Network, Security, Agents
    public string Status { get; set; } = "Pass"; // Pass, Warning, Error
    public string Details { get; set; } = string.Empty;
    public bool CanAutoFix { get; set; }
    public string FixLabel { get; set; } = "Auto-Fix";
}

public sealed class SystemDoctorService
{
    public static SystemDoctorService Instance { get; } = new();

    public async Task<List<DiagnosticCheckItem>> RunDiagnosticsAsync()
    {
        return
        [
            await CheckCommandAsync("node", "Node.js Runtime", "node -v", "Runtime", "https://nodejs.org/").ConfigureAwait(false),
            await CheckCommandAsync("python", "Python 3 Runtime", "python --version", "Runtime", "https://www.python.org/").ConfigureAwait(false),
            await CheckCommandAsync("git", "Git Version Control", "git --version", "Runtime", "https://git-scm.com/").ConfigureAwait(false),
            CheckInnoSetup(),
            await CheckFirewallRuleAsync().ConfigureAwait(false),
            await CheckCommandAsync("agy", "Antigravity CLI (agy)", "agy --version", "Agents", "https://github.com/google-deepmind/antigravity").ConfigureAwait(false),
            await CheckCommandAsync("claude", "Claude Code CLI (claude)", "claude --version", "Agents", "https://claude.ai/code").ConfigureAwait(false),
            CheckPortAvailability(9191, "LAN Proxy Port 9191"),
            CheckPortAvailability(9119, "Hub Engine Port 9119")
        ];
    }

    public async Task<bool> AutoFixIssueAsync(string issueId)
    {
        if (string.Equals(issueId, "firewall", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c netsh advfirewall firewall add rule name=\"Agents Hub LAN Dashboard\" dir=in action=allow protocol=TCP localport=9191")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    // CreateNoWindow is ignored when UseShellExecute is true, so an elevated console
                    // command would still flash. WindowStyle is what ShellExecute honours.
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync().ConfigureAwait(false);
                    return proc.ExitCode == 0;
                }
            }
            catch { }
        }
        else if (string.Equals(issueId, "inno", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("https://jrsoftware.org/isdl.php") { UseShellExecute = true });
            return true;
        }
        else if (string.Equals(issueId, "node", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("https://nodejs.org/en/download") { UseShellExecute = true });
            return true;
        }
        else if (string.Equals(issueId, "git", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("https://git-scm.com/downloads/win") { UseShellExecute = true });
            return true;
        }

        return false;
    }

    private static async Task<DiagnosticCheckItem> CheckCommandAsync(string id, string title, string testCmd, string category, string downloadUrl)
    {
        try
        {
            var parts = testCmd.Split(' ', 2);
            var psi = new ProcessStartInfo("cmd.exe", $"/c {testCmd}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await proc.WaitForExitAsync().ConfigureAwait(false);
                if (proc.ExitCode == 0)
                {
                    return new DiagnosticCheckItem
                    {
                        Id = id,
                        Title = title,
                        Category = category,
                        Status = "Pass",
                        Details = string.IsNullOrWhiteSpace(output) ? "Installed & responding in PATH" : output.Trim()
                    };
                }
            }
        }
        catch { }

        return new DiagnosticCheckItem
        {
            Id = id,
            Title = title,
            Category = category,
            Status = category == "Agents" ? "Warning" : "Error",
            Details = $"Not found in PATH. Install from {downloadUrl}",
            CanAutoFix = true,
            FixLabel = "Open Download Page"
        };
    }

    private static DiagnosticCheckItem CheckInnoSetup()
    {
        var iscc64 = @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe";
        var iscc32 = @"C:\Program Files\Inno Setup 6\ISCC.exe";

        if (File.Exists(iscc64) || File.Exists(iscc32))
        {
            return new DiagnosticCheckItem
            {
                Id = "inno",
                Title = "Inno Setup 6 Compiler",
                Category = "Runtime",
                Status = "Pass",
                Details = "Inno Setup 6 compiler ISCC.exe is ready"
            };
        }

        return new DiagnosticCheckItem
        {
            Id = "inno",
            Title = "Inno Setup 6 Compiler",
            Category = "Runtime",
            Status = "Warning",
            Details = "ISCC.exe not found. Required only for building setup installers.",
            CanAutoFix = true,
            FixLabel = "Download Inno Setup"
        };
    }

    private static async Task<DiagnosticCheckItem> CheckFirewallRuleAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c netsh advfirewall firewall show rule name=\"Agents Hub LAN Dashboard\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var outText = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await proc.WaitForExitAsync().ConfigureAwait(false);
                if (proc.ExitCode == 0 && outText.Contains("Rule Name:", StringComparison.OrdinalIgnoreCase))
                {
                    return new DiagnosticCheckItem
                    {
                        Id = "firewall",
                        Title = "Windows Firewall LAN Inbound Rule",
                        Category = "Network",
                        Status = "Pass",
                        Details = "Inbound rule for LAN port 9191 is active"
                    };
                }
            }
        }
        catch { }

        return new DiagnosticCheckItem
        {
            Id = "firewall",
            Title = "Windows Firewall LAN Inbound Rule",
            Category = "Network",
            Status = "Warning",
            Details = "Inbound rule for port 9191 not found. Local devices may be blocked.",
            CanAutoFix = true,
            FixLabel = "Create Firewall Rule (UAC)"
        };
    }

    private static DiagnosticCheckItem CheckPortAvailability(int port, string label)
    {
        try
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            foreach (var ep in tcpListeners)
            {
                if (ep.Port == port)
                {
                    return new DiagnosticCheckItem
                    {
                        Id = "port_" + port,
                        Title = label,
                        Category = "Network",
                        Status = "Pass",
                        Details = $"Listening on port {port} (active)"
                    };
                }
            }
        }
        catch { }

        return new DiagnosticCheckItem
        {
            Id = "port_" + port,
            Title = label,
            Category = "Network",
            Status = "Pass",
            Details = $"Port {port} is free and ready to bind"
        };
    }
}
