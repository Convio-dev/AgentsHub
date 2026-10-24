using Microsoft.Win32;

namespace AgentsHub.Helpers;

public static class StartupHelper
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryKey = @"Software\AgentsHub";
    private const string AppValueName = "AgentsHub";

    public static bool IsWindowsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            var value = key?.GetValue(AppValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetWindowsStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                key.SetValue(AppValueName, $"\"{exePath}\" --tray");
            }
            else
            {
                key.DeleteValue(AppValueName, false);
            }
        }
        catch
        {
            // Ignore registry write permissions errors
        }
    }

    public static bool GetPreference(string name, bool defaultValue = false)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppRegistryKey, false);
            var val = key?.GetValue(name);
            if (val == null) return defaultValue;

            if (val is int intVal) return intVal == 1;
            if (val is string strVal && bool.TryParse(strVal, out var parsed)) return parsed;

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public static void SetPreference(string name, bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppRegistryKey, true);
            key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Ignore registry write failures
        }
    }

    public static int GetIntPreference(string name, int defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppRegistryKey, false);
            var val = key?.GetValue(name);
            if (val == null) return defaultValue;

            if (val is int intVal) return intVal;
            if (val is string strVal && int.TryParse(strVal, out var parsed)) return parsed;

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public static void SetIntPreference(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppRegistryKey, true);
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch
        {
            // Ignore registry write failures
        }
    }

    public static string GetStringPreference(string name, string defaultValue = "")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppRegistryKey, false);
            var val = key?.GetValue(name);
            if (val == null) return defaultValue;

            if (val is string strVal) return strVal;

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public static void SetStringPreference(string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppRegistryKey, true);
            key.SetValue(name, value, RegistryValueKind.String);
        }
        catch
        {
            // Ignore registry write failures
        }
    }
}
