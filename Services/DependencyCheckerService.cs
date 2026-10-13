using System.Diagnostics;
using System.Text;

namespace HermesHub.Services;

/// <summary>Static description of a tool the hub depends on.</summary>
public sealed class DependencyDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>"powershell" runs <see cref="InstallCommand"/>; "browser" opens <see cref="SiteUrl"/>.</summary>
    public string InstallKind { get; init; } = "powershell";

    public string SiteUrl { get; init; } = "";
    public string InstallCommand { get; init; } = "";
    public string CommandPreview { get; init; } = "";

    /// <summary>Executable names looked up on PATH (without extension).</summary>
    public string[] Commands { get; init; } = [];

    /// <summary>Absolute fallbacks; may contain %ENVIRONMENT% tokens.</summary>
    public string[] FallbackPaths { get; init; } = [];
}

/// <summary>Detection result for one tool.</summary>
public sealed class DependencyStatus
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsInstalled { get; init; }
    public string Version { get; init; } = "";
    public string Path { get; init; } = "";
    public string InstallKind { get; init; } = "powershell";
    public string SiteUrl { get; init; } = "";
    public string CommandPreview { get; init; } = "";
}

/// <summary>
/// Detects whether the external CLI tools the hub drives are present.
///
/// Detection resolves executables from PATH plus a few standard install locations, then probes
/// each hit for a version string. Every probe spawns a short-lived process, so callers must use
/// <see cref="GetDependenciesStatusAsync"/> — it fans the probes out onto background threads and
/// never runs on the UI thread.
/// </summary>
public static class DependencyCheckerService
{
    // Some CLIs are slow to start: "hermes --version" is a ~4s Python interpreter boot, so the
    // probe budget has to clear that. Probes run in parallel, so this bounds the whole scan.
    private const int VersionProbeTimeoutMs = 8000;

    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static IReadOnlyList<DependencyDefinition> Catalog { get; } =
    [
        new DependencyDefinition
        {
            Id = "hermes",
            Name = "Hermes Agent Engine",
            Description = "Backend agent daemon that powers the Hermes web dashboard.",
            InstallKind = "browser",
            SiteUrl = "https://hermes-agent.nousresearch.com/",
            CommandPreview = "hermes dashboard",
            Commands = ["hermes"],
            FallbackPaths =
            [
                @"%APPDATA%\Python\Scripts\hermes.exe",
                @"%LOCALAPPDATA%\Programs\Python\Scripts\hermes.exe"
            ]
        },
        new DependencyDefinition
        {
            Id = "claude",
            Name = "Claude Code CLI",
            Description = "Anthropic's terminal coding agent. Routed through the local OpenRouter gateway.",
            InstallKind = "powershell",
            SiteUrl = "https://claude.ai/",
            InstallCommand = "irm https://claude.ai/install.ps1 | iex",
            CommandPreview = "irm https://claude.ai/install.ps1 | iex",
            Commands = ["claude"],
            FallbackPaths =
            [
                @"%APPDATA%\npm\claude.cmd",
                @"%APPDATA%\npm\claude",
                @"%USERPROFILE%\.claude\local\claude.exe"
            ]
        },
        new DependencyDefinition
        {
            Id = "antigravity",
            Name = "Antigravity CLI (agy)",
            Description = "Google Antigravity terminal agent, run per isolated profile.",
            InstallKind = "powershell",
            SiteUrl = "https://antigravity.google/",
            InstallCommand = "irm https://antigravity.google/cli/install.ps1 | iex",
            CommandPreview = "irm https://antigravity.google/cli/install.ps1 | iex",
            Commands = ["agy"],
            FallbackPaths =
            [
                @"%LOCALAPPDATA%\Programs\Antigravity CLI\agy.exe",
                @"%APPDATA%\npm\agy.cmd"
            ]
        },
        new DependencyDefinition
        {
            Id = "warp",
            Name = "Cloudflare WARP",
            Description = "Used to rotate the public IP when resetting provider quota.",
            InstallKind = "browser",
            SiteUrl = "https://one.one.one.one/",
            CommandPreview = "warp-cli status",
            Commands = ["warp-cli"],
            FallbackPaths =
            [
                @"%ProgramFiles%\Cloudflare\Cloudflare WARP\Cloudflare WARP.exe",
                @"%ProgramFiles(x86)%\Cloudflare\Cloudflare WARP\Cloudflare WARP.exe",
                @"%ProgramFiles%\Cloudflare\Cloudflare WARP\warp-cli.exe"
            ]
        }
    ];

    public static DependencyDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        foreach (var definition in Catalog)
        {
            if (string.Equals(definition.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) return definition;
        }

        return null;
    }

    /// <summary>
    /// Detects every catalog entry concurrently. Safe to call from the UI thread: all process
    /// work happens on the thread pool.
    /// </summary>
    public static async Task<List<DependencyStatus>> GetDependenciesStatusAsync(CancellationToken cancellationToken = default)
    {
        var probes = Catalog
            .Select(definition => Task.Run(() => Detect(definition), cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(probes).ConfigureAwait(false);
        return [.. results];
    }

    /// <summary>Detects a single entry by id. Returns null when the id is unknown.</summary>
    public static DependencyStatus? GetStatus(string? id)
    {
        var definition = Find(id);
        return definition == null ? null : Detect(definition);
    }

    private static DependencyStatus Detect(DependencyDefinition definition)
    {
        string path = "";

        foreach (var command in definition.Commands)
        {
            var resolved = ResolveOnPath(command);
            if (resolved != null)
            {
                path = resolved;
                break;
            }
        }

        if (path.Length == 0)
        {
            foreach (var candidate in definition.FallbackPaths)
            {
                var expanded = Expand(candidate);
                if (expanded.Length > 0 && File.Exists(expanded))
                {
                    path = expanded;
                    break;
                }
            }
        }

        string version = path.Length > 0 ? ProbeVersion(path) : "";

        return new DependencyStatus
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            IsInstalled = path.Length > 0,
            Version = version,
            Path = path,
            InstallKind = definition.InstallKind,
            SiteUrl = definition.SiteUrl,
            CommandPreview = definition.CommandPreview
        };
    }

    /// <summary>
    /// Resolves an executable against PATH and PATHEXT. Done in managed code rather than by
    /// shelling out to where.exe, so detection costs no extra process.
    /// </summary>
    private static string? ResolveOnPath(string command)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable)) return null;

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0) continue;

            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(trimmed, command + extension.ToLowerInvariant());
                    if (File.Exists(candidate)) return candidate;

                    candidate = Path.Combine(trimmed, command + extension.ToUpperInvariant());
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // Malformed PATH entry; skip it.
                }
            }
        }

        return null;
    }

    private static string Expand(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";

        var expanded = Environment.ExpandEnvironmentVariables(template);

        // ProgramFiles(x86) is not expanded by ExpandEnvironmentVariables on 64-bit hosts.
        if (template.Contains("%ProgramFiles(x86)%", StringComparison.OrdinalIgnoreCase))
        {
            expanded = expanded.Replace("%ProgramFiles(x86)%", ProgramFilesX86, StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    /// <summary>
    /// Runs "&lt;tool&gt; --version" and pulls the first dotted number out of the output. Returns
    /// an empty string when the probe times out or prints nothing useful.
    /// </summary>
    private static string ProbeVersion(string executablePath)
    {
        try
        {
            string fileName = executablePath;
            string arguments = "--version";

            var extension = Path.GetExtension(executablePath).ToLowerInvariant();
            if (extension is ".cmd" or ".bat")
            {
                // cmd scripts cannot be launched directly with UseShellExecute = false.
                fileName = "cmd.exe";
                arguments = $"/c \"\"{executablePath}\" --version\"";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = UserProfile
            };

            using var process = Process.Start(startInfo);
            if (process == null) return "";

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(VersionProbeTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return "";
            }

            string combined = stdout.GetAwaiter().GetResult() + " " + stderr.GetAwaiter().GetResult();
            return ExtractVersion(combined);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Pulls a version out of probe output without a regex. Takes the leading digits-and-dots run
    /// of the first token that starts with one, so build metadata and dates are not mistaken for
    /// the version ("v0.21.6+187.g1e0c773" yields v0.21.6, not the build date beside it).
    /// </summary>
    private static string ExtractVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        foreach (var rawToken in text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (token.Length < 3) continue;

            int start = token[0] is 'v' or 'V' ? 1 : 0;
            if (start >= token.Length || !char.IsDigit(token[start])) continue;

            int end = start;
            while (end < token.Length && (char.IsDigit(token[end]) || token[end] == '.')) end++;

            var candidate = token[start..end].TrimEnd('.');
            if (candidate.Contains('.')) return "v" + candidate;
        }

        return "";
    }
}
