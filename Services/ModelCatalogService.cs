#nullable enable
using System.Diagnostics;
using System.Text;

namespace AgentsHub.Services;

/// <summary>One selectable model reported by the active CLI.</summary>
/// <param name="Id">Exact identifier handed back to the engine via <c>--model</c>.</param>
/// <param name="Label">Human-readable name, as the engine prints it.</param>
/// <param name="Provider">Model family, derived from the id prefix.</param>
/// <param name="Tier">Reasoning tier encoded in the id (<c>low</c>, <c>medium</c>, <c>high</c>), if any.</param>
public sealed record ModelInfoDto(string Id, string Label, string Provider, string? Tier);

/// <summary>The model list plus the effort levels the engine accepts.</summary>
/// <param name="Models">Every model the CLI reported, in its own order.</param>
/// <param name="Efforts">Reasoning effort values accepted by the engine.</param>
/// <param name="Source">Where the list came from: <c>cli</c> when the engine answered, <c>fallback</c> otherwise.</param>
/// <param name="Error">Diagnostic message when the engine could not be queried.</param>
public sealed record ModelCatalogDto(
    IReadOnlyList<ModelInfoDto> Models,
    IReadOnlyList<string> Efforts,
    string Source,
    string? Error);

/// <summary>
/// Reads the model list from the CLI itself (<c>agy models</c>) instead of shipping a hardcoded table.
/// <para>
/// The engine prints one <c>id\tLabel</c> pair per line and answers in a couple of seconds, so the
/// result is cached for a few minutes. If the binary is missing or the call times out the catalog
/// degrades to an empty list with an error, and the UI keeps the session's current model rather than
/// inventing alternatives.
/// </para>
/// </summary>
public sealed class ModelCatalogService
{
    public static ModelCatalogService Instance { get; } = new();

    /// <summary>Effort levels accepted by `agy --effort`, verified from its own help output.</summary>
    private static readonly string[] EffortLevels = ["low", "medium", "high", "xhigh", "max"];

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private readonly Lock _gate = new();
    private CacheEntry? _cache;

    private sealed record CacheEntry(DateTime AtUtc, string Key, ModelCatalogDto Catalog);

    public async Task<ModelCatalogDto> GetCatalogAsync(string? cli, CancellationToken cancellationToken = default)
    {
        string engine = NormaliseEngine(cli);

        lock (_gate)
        {
            if (_cache != null && _cache.Key == engine && DateTime.UtcNow - _cache.AtUtc < CacheTtl)
            {
                return _cache.Catalog;
            }
        }

        ModelCatalogDto catalog = engine switch
        {
            "antigravity" => await ProbeAgyAsync(cancellationToken).ConfigureAwait(false),
            _ => new ModelCatalogDto([], EffortLevels, "fallback", $"No model probe is implemented for '{engine}'.")
        };

        lock (_gate)
        {
            _cache = new CacheEntry(DateTime.UtcNow, engine, catalog);
        }

        return catalog;
    }

    /// <summary>Drops the cached catalog so the next request re-queries the engine.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cache = null;
        }
    }

    private static string NormaliseEngine(string? cli)
    {
        if (string.IsNullOrWhiteSpace(cli)) return "antigravity";

        string id = cli.Trim().ToLowerInvariant().Replace('-', '_');
        if (id.Contains("antigravity", StringComparison.Ordinal) || id == "agy") return "antigravity";
        if (id.Contains("claude", StringComparison.Ordinal)) return "claude";
        if (id.Contains("codex", StringComparison.Ordinal)) return "codex";
        return "custom";
    }

    private static async Task<ModelCatalogDto> ProbeAgyAsync(CancellationToken cancellationToken)
    {
        string? binary = ResolveAgyBinary();
        if (binary == null)
        {
            return new ModelCatalogDto([], EffortLevels, "fallback", "The agy CLI was not found on PATH.");
        }

        try
        {
            var startInfo = new ProcessStartInfo(binary, "models")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return new ModelCatalogDto([], EffortLevels, "fallback", "The agy model list timed out.");
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            var models = ParseModelList(stdout);
            if (models.Count == 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? "No models were reported." : stderr.Trim();
                return new ModelCatalogDto([], EffortLevels, "fallback", detail);
            }

            return new ModelCatalogDto(models, EffortLevels, "cli", null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModelCatalogService] agy models failed: {ex.Message}");
            return new ModelCatalogDto([], EffortLevels, "fallback", ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // the process is already gone or cannot be signalled
        }
    }

    /// <summary>
    /// Parses `<c>id\tLabel</c>` lines. Progress chatter such as "Fetching available models…" has no
    /// tab and is skipped, which keeps the parser tolerant of extra output.
    /// </summary>
    private static List<ModelInfoDto> ParseModelList(string output)
    {
        var models = new List<ModelInfoDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;

            string id = line[..tab].Trim();
            string label = line[(tab + 1)..].Trim();
            if (id.Length == 0 || !seen.Add(id)) continue;

            models.Add(new ModelInfoDto(id, label.Length > 0 ? label : id, ProviderOf(id), TierOf(id)));
        }

        return models;
    }

    private static string ProviderOf(string id)
    {
        string lowered = id.ToLowerInvariant();
        if (lowered.StartsWith("gemini", StringComparison.Ordinal)) return "Gemini";
        if (lowered.StartsWith("claude", StringComparison.Ordinal)) return "Claude";
        if (lowered.StartsWith("gpt", StringComparison.Ordinal)) return "GPT-OSS";
        if (lowered.StartsWith("deepseek", StringComparison.Ordinal)) return "DeepSeek";

        int dash = lowered.IndexOf('-');
        return dash > 0 ? lowered[..dash] : lowered;
    }

    private static string? TierOf(string id)
    {
        string lowered = id.ToLowerInvariant();
        if (lowered.EndsWith("-xhigh", StringComparison.Ordinal)) return "xhigh";
        if (lowered.EndsWith("-high", StringComparison.Ordinal)) return "high";
        if (lowered.EndsWith("-medium", StringComparison.Ordinal)) return "medium";
        if (lowered.EndsWith("-low", StringComparison.Ordinal)) return "low";
        return null;
    }

    /// <summary>
    /// Finds the CLI the same way a shell would: PATH first, then the installer's default location.
    /// </summary>
    private static string? ResolveAgyBinary()
    {
        bool windows = OperatingSystem.IsWindows();
        string exe = windows ? "agy.exe" : "agy";

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string entry in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;

                string dir = entry.Trim().Trim('"');
                if (dir.Length == 0) continue;

                try
                {
                    string candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // malformed PATH entry, keep looking
                }
            }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            string known = Path.Combine(localAppData, "agy", "bin", exe);
            if (File.Exists(known)) return known;
        }

        return null;
    }
}
