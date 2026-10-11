using System.Diagnostics;
using System.IO;

namespace HermesHub.Helpers;

public static class ShellLauncher
{
    public static void OpenCommandPrompt(bool asAdministrator, string? initialDirectory = null)
    {
        LaunchShell("cmd.exe", asAdministrator, initialDirectory);
    }

    public static void OpenPowerShell(bool asAdministrator, string? initialDirectory = null)
    {
        LaunchShell("powershell.exe", asAdministrator, initialDirectory);
    }

    public static void LaunchShell(string executable, bool asAdministrator, string? initialDirectory = null)
    {
        try
        {
            string workDir = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workDir,
                UseShellExecute = true
            };

            if (asAdministrator)
            {
                psi.Verb = "runas";
            }

            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled elevation prompt
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellLauncher] Failed to launch {executable} (Admin: {asAdministrator}): {ex.Message}");
        }
    }
}
