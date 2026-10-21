using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AgentsHub.Helpers;

/// <summary>
/// How well Windows Firewall is set up for the LAN feature.
/// </summary>
public enum FirewallRuleState
{
    /// <summary>The query could not be completed, so nothing can be claimed.</summary>
    Unknown,

    /// <summary>No rule for this app exists. Inbound connections from other devices are blocked.</summary>
    Missing,

    /// <summary>A rule exists but does not cover every port the app is listening on.</summary>
    Partial,

    /// <summary>A rule covers every required port.</summary>
    Ready
}

public sealed class FirewallStatus
{
    public bool QuerySucceeded { get; init; }
    public string Detail { get; init; } = "";

    public bool RuleExists { get; init; }
    public bool RuleEnabled { get; init; }
    public bool RuleInbound { get; init; }
    public bool RuleAllow { get; init; }
    public string RuleProfile { get; init; } = "";
    public IReadOnlyList<int> RulePorts { get; init; } = [];

    public bool DomainEnabled { get; init; }
    public bool PrivateEnabled { get; init; }
    public bool PublicEnabled { get; init; }

    public string NetworkName { get; init; } = "";
    public string NetworkCategory { get; init; } = "";
    public string NetworkInterface { get; init; } = "";

    /// <summary>
    /// The firewall profile that governs the connection carrying the LAN address.
    /// An unknown category falls back to Private, the least surprising default.
    /// </summary>
    public string ActiveProfile =>
        NetworkCategory.Equals("Public", StringComparison.OrdinalIgnoreCase) ? "Public"
        : NetworkCategory.Equals("DomainAuthenticated", StringComparison.OrdinalIgnoreCase) ? "Domain"
        : "Private";

    public bool FirewallOnForActiveProfile => ActiveProfile switch
    {
        "Public" => PublicEnabled,
        "Domain" => DomainEnabled,
        _ => PrivateEnabled
    };

    /// <summary>
    /// True when inbound traffic from another device is dropped before it reaches the app.
    /// A Public network is the common case: Windows blocks inbound there by design.
    /// </summary>
    public bool InboundBlockedByNetworkCategory =>
        QuerySucceeded && FirewallOnForActiveProfile && !(RuleExists && RuleEnabled);

    public FirewallRuleState StateFor(IReadOnlyList<int> requiredPorts)
    {
        if (!QuerySucceeded) return FirewallRuleState.Unknown;
        if (!RuleExists || !RuleEnabled || !RuleInbound || !RuleAllow) return FirewallRuleState.Missing;
        if (requiredPorts.Count == 0) return FirewallRuleState.Ready;

        return requiredPorts.All(p => RulePorts.Contains(p))
            ? FirewallRuleState.Ready
            : FirewallRuleState.Partial;
    }

    public IReadOnlyList<int> MissingPorts(IReadOnlyList<int> requiredPorts) =>
        [.. requiredPorts.Where(p => !RulePorts.Contains(p)).Distinct().Order()];
}

public sealed class FirewallGrantResult
{
    public bool Succeeded { get; init; }

    /// <summary>True when the user dismissed the elevation prompt rather than the operation failing.</summary>
    public bool UserCancelled { get; init; }

    public string Message { get; init; } = "";
}

/// <summary>
/// Reads and configures the Windows Firewall rule that lets other devices on the same network
/// reach the app's LAN ports.
///
/// The query runs unelevated and uses PowerShell's NetFirewall cmdlets rather than parsing
/// <c>netsh</c> text: netsh prints localised field labels, so matching on them breaks on a
/// non-English Windows, while cmdlet property names stay in English everywhere.
///
/// Only the write needs elevation, and it reports a dismissed UAC prompt separately from a real
/// failure so the UI can tell the user which one happened.
/// </summary>
public static class FirewallHelper
{
    private const string RuleName = "AgentsHub LAN";
    private const int QueryTimeoutMs = 20000;
    private const int WriteTimeoutMs = 15000;

    /// <summary>Raised by Process.Start when the user declines the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    public static async Task<FirewallStatus> GetStatusAsync(
        IReadOnlyList<int> requiredPorts,
        string lanIp,
        CancellationToken cancellationToken = default)
    {
        _ = requiredPorts;

        try
        {
            var script = BuildQueryScript(lanIp);
            var output = await RunPowerShellAsync(script, QueryTimeoutMs, cancellationToken).ConfigureAwait(false);
            return Parse(output);
        }
        catch (OperationCanceledException)
        {
            return new FirewallStatus { QuerySucceeded = false, Detail = "The firewall check timed out." };
        }
        catch (Exception ex)
        {
            return new FirewallStatus { QuerySucceeded = false, Detail = $"The firewall check failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Creates (or refreshes) one inbound rule covering every supplied port. Prompts for
    /// elevation. Returns without throwing when the user declines.
    /// </summary>
    public static Task<FirewallGrantResult> RequestInboundRuleAsync(IReadOnlyList<int> ports)
    {
        var distinct = ports.Where(p => p is > 0 and < 65536).Distinct().Order().ToList();
        if (distinct.Count == 0)
        {
            return Task.FromResult(new FirewallGrantResult
            {
                Succeeded = false,
                Message = "There are no LAN ports to allow yet."
            });
        }

        return Task.Run(() =>
        {
            try
            {
                var portList = string.Join(',', distinct);

                // netsh has no update verb, so an existing rule with a different port set is
                // replaced rather than left stale. A missing rule is the normal first-run case.
                _ = TryRunElevated($"advfirewall firewall delete rule name=\"{RuleName}\"", out bool deleteCancelled);
                if (deleteCancelled)
                {
                    return new FirewallGrantResult
                    {
                        Succeeded = false,
                        UserCancelled = true,
                        Message = "Elevation was declined, so the rule was not changed."
                    };
                }

                bool added = TryRunElevated(
                    $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP " +
                    $"localport={portList} profile=any",
                    out bool addCancelled);

                if (addCancelled)
                {
                    return new FirewallGrantResult
                    {
                        Succeeded = false,
                        UserCancelled = true,
                        Message = "Elevation was declined, so the rule was not created."
                    };
                }

                return added
                    ? new FirewallGrantResult { Succeeded = true, Message = $"Inbound rule created for port {portList}." }
                    : new FirewallGrantResult { Succeeded = false, Message = "netsh reported a failure while creating the rule." };
            }
            catch (Exception ex)
            {
                return new FirewallGrantResult { Succeeded = false, Message = $"The rule could not be created: {ex.Message}" };
            }
        });
    }

    /// <summary>Removes the rule if it exists. Prompts for elevation.</summary>
    public static Task<FirewallGrantResult> RemoveInboundRuleAsync() => Task.Run(() =>
    {
        bool removed = TryRunElevated($"advfirewall firewall delete rule name=\"{RuleName}\"", out bool cancelled);
        if (cancelled)
        {
            return new FirewallGrantResult { Succeeded = false, UserCancelled = true, Message = "Elevation was declined." };
        }

        return new FirewallGrantResult
        {
            Succeeded = removed,
            Message = removed ? "Rule removed." : "There was no rule to remove."
        };
    });

    private static bool TryRunElevated(string netshArguments, out bool userCancelled)
    {
        userCancelled = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = netshArguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            process.WaitForExit(WriteTimeoutMs);
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The user dismissed the UAC dialog. That is a decision, not a failure.
            userCancelled = true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildQueryScript(string lanIp)
    {
        var safeIp = (lanIp ?? "").Replace("'", "");
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='SilentlyContinue';");
        sb.Append("$rules=@(Get-NetFirewallRule -DisplayName '").Append(RuleName).Append("');");
        sb.Append("if($rules.Count -gt 0){");
        sb.Append("$r=$rules[0];");
        sb.Append("$f=$r|Get-NetFirewallPortFilter;");
        sb.Append("'RULE=1';");
        sb.Append("'ENABLED='+$r.Enabled;");
        sb.Append("'DIR='+$r.Direction;");
        sb.Append("'ACTION='+$r.Action;");
        sb.Append("'PROFILE='+$r.Profile;");
        sb.Append("'PORTS='+(($f.LocalPort)-join ',')");
        sb.Append("}else{'RULE=0'};");
        sb.Append("Get-NetFirewallProfile|ForEach-Object{'FP='+$_.Name+':'+$_.Enabled};");
        // Match the connection profile to the adapter that actually holds the LAN address.
        sb.Append("$idx=(Get-NetIPAddress -AddressFamily IPv4|Where-Object{$_.IPAddress -eq '").Append(safeIp).Append("'}|Select-Object -First 1).InterfaceIndex;");
        sb.Append("$np=@(Get-NetConnectionProfile|Where-Object{$_.InterfaceIndex -eq $idx});");
        sb.Append("if($np.Count -eq 0){$np=@(Get-NetConnectionProfile|Select-Object -First 1)};");
        sb.Append("$np|ForEach-Object{'NP='+$_.Name+':'+$_.NetworkCategory+':'+$_.InterfaceAlias}");
        return sb.ToString();
    }

    private static async Task<string> RunPowerShellAsync(string script, int timeoutMs, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) return "";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        _ = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return await stdout.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    internal static FirewallStatus Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new FirewallStatus { QuerySucceeded = false, Detail = "The firewall query returned nothing." };
        }

        bool querySucceeded = false;
        bool ruleExists = false;
        bool ruleEnabled = false;
        bool ruleInbound = false;
        bool ruleAllow = false;
        string ruleProfile = "";
        var rulePorts = new List<int>();
        bool domain = false, priv = false, pub = false;
        string netName = "", netCategory = "", netIface = "";

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("RULE=", StringComparison.Ordinal))
            {
                querySucceeded = true;
                ruleExists = line[5..].Trim() == "1";
            }
            else if (line.StartsWith("ENABLED=", StringComparison.Ordinal))
            {
                ruleEnabled = IsTrue(line[8..]);
            }
            else if (line.StartsWith("DIR=", StringComparison.Ordinal))
            {
                ruleInbound = line[4..].Trim().Equals("Inbound", StringComparison.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("ACTION=", StringComparison.Ordinal))
            {
                ruleAllow = line[7..].Trim().Equals("Allow", StringComparison.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("PROFILE=", StringComparison.Ordinal))
            {
                ruleProfile = line[8..].Trim();
            }
            else if (line.StartsWith("PORTS=", StringComparison.Ordinal))
            {
                rulePorts.AddRange(ParsePortList(line[6..]));
            }
            else if (line.StartsWith("FP=", StringComparison.Ordinal))
            {
                var parts = line[3..].Split(':');
                if (parts.Length < 2) continue;
                bool on = IsTrue(parts[1]);
                if (parts[0].Equals("Domain", StringComparison.OrdinalIgnoreCase)) domain = on;
                else if (parts[0].Equals("Private", StringComparison.OrdinalIgnoreCase)) priv = on;
                else if (parts[0].Equals("Public", StringComparison.OrdinalIgnoreCase)) pub = on;
            }
            else if (line.StartsWith("NP=", StringComparison.Ordinal))
            {
                var parts = line[3..].Split(':');
                if (parts.Length < 2) continue;

                // The first connection profile wins; the script already narrows it to the LAN adapter.
                if (netCategory.Length == 0)
                {
                    netName = parts[0];
                    netCategory = parts[1];
                    netIface = parts.Length > 2 ? parts[2] : "";
                }
            }
        }

        return new FirewallStatus
        {
            QuerySucceeded = querySucceeded,
            Detail = querySucceeded ? "" : "The firewall query produced no recognisable output.",
            RuleExists = ruleExists,
            RuleEnabled = ruleEnabled,
            RuleInbound = ruleInbound,
            RuleAllow = ruleAllow,
            RuleProfile = ruleProfile,
            RulePorts = [.. rulePorts.Distinct().Order()],
            DomainEnabled = domain,
            PrivateEnabled = priv,
            PublicEnabled = pub,
            NetworkName = netName,
            NetworkCategory = netCategory,
            NetworkInterface = netIface
        };
    }

    /// <summary>
    /// Accepts "9191", "9191,9229" and ranges like "9191-9200" (every port in the range counts).
    /// </summary>
    internal static IEnumerable<int> ParsePortList(string value)
    {
        foreach (var token in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            if (t.Length == 0) continue;

            int dash = t.IndexOf('-');
            if (dash > 0)
            {
                if (int.TryParse(t[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int from) &&
                    int.TryParse(t[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int to) &&
                    from <= to && to - from < 4096)
                {
                    for (int p = from; p <= to; p++) yield return p;
                }
                continue;
            }

            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port is > 0 and < 65536)
            {
                yield return port;
            }
        }
    }

    private static bool IsTrue(string value) => value.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
}
