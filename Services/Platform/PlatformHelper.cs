#nullable enable
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AgentsHub.Services.Platform;

public static class PlatformHelper
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static string PlatformName =>
        IsWindows ? "Windows" :
        IsMacOS ? "macOS" :
        IsLinux ? "Linux" : "Unix";

    public static string GetDefaultShell()
    {
        if (IsWindows)
        {
            string? comspec = Environment.GetEnvironmentVariable("COMSPEC");
            return !string.IsNullOrWhiteSpace(comspec) ? comspec : "cmd.exe";
        }

        string? shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(shell) && File.Exists(shell))
        {
            return shell;
        }

        return File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
    }

    public static string GetConfigDirectory()
    {
        if (IsWindows)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AgentsHub");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "agentshub");
    }

    public static void OpenUrlInBrowser(string url)
    {
        try
        {
            if (IsWindows)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (IsMacOS)
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlatformHelper] Failed to open URL '{url}': {ex.Message}");
        }
    }
}
