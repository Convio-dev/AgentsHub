using System.Reflection;

namespace HermesHub.Helpers;

/// <summary>
/// The running version, read from the assembly so the project file stays the single place it is
/// written down.
///
/// It used to be a literal in four different files: the bridge's initial-state DTO, the dashboard
/// markup, the dashboard controller's fallbacks and the installer script. A release could
/// therefore ship a UI claiming one version and an installer claiming another, and an update
/// check has nothing reliable to compare against.
/// </summary>
public static class AppInfo
{
    /// <summary>Bare version, for example "1.0.3".</summary>
    public static string Version { get; } = Resolve();

    /// <summary>The version with the leading v the interface uses, for example "v1.0.3".</summary>
    public static string DisplayVersion => "v" + Version;

    private static string Resolve()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // The SDK appends "+<source revision>" to the informational version. Keep the number only.
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            var trimmed = (plus >= 0 ? informational[..plus] : informational).Trim();
            if (trimmed.Length > 0) return trimmed;
        }

        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion)) return fileVersion.Trim();

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
