using System.Diagnostics;

namespace HermesHub.Helpers;

public static class FirewallHelper
{
    private const string RuleName = "HermesHub LAN";

    public static bool IsInboundRuleConfigured(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall show rule name=\"{RuleName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);

            if (process.ExitCode == 0 && output.Contains(port.ToString()))
            {
                return true;
            }
        }
        catch
        {
            // Fall back to unverified status on query error
        }

        return false;
    }

    public static async Task<bool> RequestAddInboundRuleAsync(int port)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Delete existing rule for clean configuration if port changed
                var deletePsi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"advfirewall firewall delete rule name=\"{RuleName}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                try
                {
                    using var delProc = Process.Start(deletePsi);
                    delProc?.WaitForExit(1500);
                }
                catch
                {
                    // Ignore delete failure if rule didn't exist
                }

                var addPsi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var addProc = Process.Start(addPsi);
                if (addProc == null) return false;

                addProc.WaitForExit(3000);
                return addProc.ExitCode == 0;
            }
            catch
            {
                // User may cancel UAC prompt or lack administrative privileges
                return false;
            }
        });
    }
}
