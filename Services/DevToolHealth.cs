using System.IO;

namespace HermesHub.Services;

/// <summary>
/// Whether a configured dev tool target can actually be launched.
/// </summary>
public enum DevToolHealthState
{
    /// <summary>The target resolves to something launchable.</summary>
    Ready,

    /// <summary>No target has been set yet.</summary>
    Empty,

    /// <summary>A path is set but nothing exists there.</summary>
    Missing,

    /// <summary>The stored value is usable once cleaned up, and is not as stored.</summary>
    NeedsFixing,

    /// <summary>The value is not something this app can start.</summary>
    Unsupported
}

public sealed class DevToolHealth
{
    public DevToolHealthState State { get; init; } = DevToolHealthState.Empty;
    public string Detail { get; init; } = "";

    /// <summary>The target as it should be stored, with the fix applied.</summary>
    public string ResolvedTarget { get; init; } = "";

    /// <summary>"App alias" for a UWP entry point, "Executable" for a file path.</summary>
    public string Kind { get; init; } = "";

    public bool Launchable => State == DevToolHealthState.Ready || State == DevToolHealthState.NeedsFixing;
}

/// <summary>
/// Normalises and inspects dev tool targets.
///
/// Explorer's "Copy as path" hands over a path wrapped in double quotes, and that value used to
/// be stored as-is. <c>Process.Start</c> cannot resolve it, so the tool silently stopped
/// launching while the page still showed it as configured. A Windows path cannot contain a
/// double quote, so stripping surrounding ones is always safe.
/// </summary>
public static class DevToolTargets
{
    private const string AppAliasPrefix = "shell:AppsFolder\\";

    public static string Normalize(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;

        var trimmed = target.Trim();
        while (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    public static DevToolHealth Check(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new DevToolHealth
            {
                State = DevToolHealthState.Empty,
                Detail = "No executable has been set for this tool yet."
            };
        }

        var raw = target.Trim();
        var resolved = Normalize(raw);
        bool wasQuoted = !string.Equals(raw, resolved, StringComparison.Ordinal);

        if (resolved.StartsWith(AppAliasPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // A UWP entry point: there is no file to test, only the alias string.
            if (resolved.Length > AppAliasPrefix.Length)
            {
                return new DevToolHealth
                {
                    State = DevToolHealthState.Ready,
                    Kind = "App alias",
                    ResolvedTarget = resolved
                };
            }

            return new DevToolHealth
            {
                State = DevToolHealthState.Unsupported,
                Kind = "App alias",
                Detail = "The app alias has no package identifier after it."
            };
        }

        if (resolved.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return new DevToolHealth
            {
                State = DevToolHealthState.Unsupported,
                Detail = "This app cannot start that kind of shell target."
            };
        }

        bool exists = File.Exists(resolved);

        if (wasQuoted)
        {
            return new DevToolHealth
            {
                State = exists ? DevToolHealthState.NeedsFixing : DevToolHealthState.Missing,
                Kind = "Executable",
                ResolvedTarget = resolved,
                Detail = exists
                    ? "The stored path is wrapped in quotes, which stops it from launching."
                    : "The stored path is wrapped in quotes and nothing exists at that location."
            };
        }

        if (!exists)
        {
            return new DevToolHealth
            {
                State = DevToolHealthState.Missing,
                Kind = "Executable",
                ResolvedTarget = resolved,
                Detail = "Nothing exists at this path any more."
            };
        }

        return new DevToolHealth
        {
            State = DevToolHealthState.Ready,
            Kind = "Executable",
            ResolvedTarget = resolved
        };
    }
}
