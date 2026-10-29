#nullable enable
using System.Text;

namespace AgentsHub.Services;

/// <summary>
/// One autocomplete entry for the chat composer's slash palette.
/// </summary>
/// <param name="Name">Command body without the leading slash.</param>
/// <param name="Description">One line, shown next to the name.</param>
/// <param name="Source">Where the command came from: <c>builtin</c>, <c>skill</c>, <c>mcp</c> or <c>profile</c>.</param>
/// <param name="Engine">Engine the command belongs to, or <c>*</c> for every engine.</param>
/// <param name="Hint">Argument hint such as <c>&lt;path&gt;</c>.</param>
/// <param name="TakesArgument">Whether the palette should leave the caret open for an argument.</param>
public sealed record SlashCommandDto(
    string Name,
    string Description,
    string Source,
    string? Engine = "*",
    string? Hint = null,
    bool TakesArgument = false);

/// <summary>
/// Discovers the slash commands available to the active CLI.
/// <para>
/// Three sources are merged: a curated built-in table per engine, the skill and command manifests
/// installed on disk (project-level and user-level), and the host's own MCP servers and Antigravity
/// profiles. Discovery never spawns the engine binary — an interactive CLI would either block on a
/// TUI or cost seconds of process start-up on every palette open — so the result is a superset of
/// what the engine will accept rather than a probe of it. Results are cached per engine + workspace
/// for a minute because the scan walks the filesystem.
/// </para>
/// </summary>
public sealed class SlashCommandService
{
    public static SlashCommandService Instance { get; } = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const int MaxDescriptionLength = 150;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(DateTime AtUtc, List<SlashCommandDto> Commands);

    /// <summary>
    /// Returns the merged command list for a CLI and workspace. Never throws: a failed manifest scan
    /// degrades to the built-in table so the palette always opens with something usable.
    /// </summary>
    public async Task<List<SlashCommandDto>> GetCommandsAsync(
        string? cli,
        string? workingDirectory,
        IReadOnlyList<string>? profiles = null)
    {
        string engine = NormaliseEngine(cli);
        string key = $"{engine}::{workingDirectory ?? string.Empty}";

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.AtUtc < CacheTtl)
            {
                return hit.Commands;
            }
        }

        var commands = new List<SlashCommandDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(SlashCommandDto command)
        {
            if (string.IsNullOrWhiteSpace(command.Name) || !seen.Add(command.Name)) return;
            commands.Add(command);
        }

        foreach (var command in GetBuiltinCommands(engine)) Add(command);

        foreach (var command in await GetSkillCommandsAsync(workingDirectory).ConfigureAwait(false)) Add(command);

        foreach (var command in await GetMcpCommandsAsync().ConfigureAwait(false)) Add(command);

        foreach (var command in GetProfileCommands(profiles)) Add(command);

        lock (_gate)
        {
            _cache[key] = new CacheEntry(DateTime.UtcNow, commands);
        }

        return commands;
    }

    /// <summary>Drops cached results, used when skills or MCP servers are installed or removed.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    // --- Built-in command tables -----------------------------------------------------------------

    private static string NormaliseEngine(string? cli)
    {
        if (string.IsNullOrWhiteSpace(cli)) return "custom";

        string id = cli.Trim().ToLowerInvariant().Replace('-', '_');

        if (id.Contains("claude", StringComparison.Ordinal)) return "claude";
        if (id.Contains("antigravity", StringComparison.Ordinal) || id == "agy") return "antigravity";
        if (id.Contains("codex", StringComparison.Ordinal)) return "codex";
        if (id.Contains("hub", StringComparison.Ordinal)) return "hub";
        return "custom";
    }

    /// <summary>
    /// Curated per-engine command sets. These are the commands the engine documents for its
    /// interactive mode; anything the user installs on top of that arrives through the manifest scan.
    /// </summary>
    private static IEnumerable<SlashCommandDto> GetBuiltinCommands(string engine)
    {
        List<SlashCommandDto> common =
        [
            new("help", "List the commands this engine accepts", "builtin", engine),
            new("clear", "Clear the conversation and start fresh", "builtin", engine),
            new("model", "Show or switch the active model", "builtin", engine, "<name>", true),
        ];

        List<SlashCommandDto> perEngine = engine switch
        {
            "claude" =>
            [
                new("add-dir", "Add another directory to the working set", "builtin", engine, "<path>", true),
                new("compact", "Summarise the conversation to reclaim context", "builtin", engine),
                new("config", "Open the configuration panel", "builtin", engine),
                new("context", "Show what is currently in the context window", "builtin", engine),
                new("cost", "Report token spend for this session", "builtin", engine),
                new("diff", "Show the pending diff in the workspace", "builtin", engine),
                new("doctor", "Check the installation and environment", "builtin", engine),
                new("init", "Create a project memory file from the codebase", "builtin", engine),
                new("mcp", "Manage MCP servers for this project", "builtin", engine),
                new("memory", "Edit the project memory file", "builtin", engine),
                new("permissions", "Review and change tool permissions", "builtin", engine),
                new("review", "Review the current changes", "builtin", engine),
                new("status", "Show session, account and model status", "builtin", engine),
                new("todos", "List the tracked task list", "builtin", engine),
                new("usage", "Show plan usage and limits", "builtin", engine),
                new("vim", "Toggle vim key bindings in the editor", "builtin", engine),
            ],
            "antigravity" =>
            [
                new("add-dir", "Add another directory to the working set", "builtin", engine, "<path>", true),
                new("btw", "Add a side note without derailing the current task", "builtin", engine, "<note>", true),
                new("compact", "Summarise the conversation to reclaim context", "builtin", engine),
                new("diff", "Show the pending diff in the workspace", "builtin", engine),
                new("plan", "Switch to planning mode before editing", "builtin", engine),
                new("skills", "List the installed agent skills", "builtin", engine),
                new("usage", "Show plan usage and limits", "builtin", engine),
            ],
            "codex" =>
            [
                new("approvals", "Change when the agent may run commands", "builtin", engine),
                new("diff", "Show the pending diff in the workspace", "builtin", engine),
                new("init", "Create a project memory file from the codebase", "builtin", engine),
                new("new", "Start a new conversation", "builtin", engine),
                new("status", "Show session, account and model status", "builtin", engine),
            ],
            "hub" =>
            [
                new("status", "Show session, account and model status", "builtin", engine),
            ],
            _ => [],
        };

        return common.Concat(perEngine);
    }

    // --- Skill and command manifests -------------------------------------------------------------

    private async Task<List<SlashCommandDto>> GetSkillCommandsAsync(string? workingDirectory)
    {
        var commands = new List<SlashCommandDto>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in EnumerateManifestRoots(workingDirectory))
        {
            if (!visited.Add(root)) continue;

            try
            {
                if (!Directory.Exists(root)) continue;

                // Two layouts are supported: one folder per skill with a SKILL.md inside, and one
                // markdown file per command (the .claude/commands convention).
                foreach (string dir in SafeEnumerateDirectories(root))
                {
                    string manifest = Path.Combine(dir, "SKILL.md");
                    if (!File.Exists(manifest)) continue;

                    var command = await ReadSkillManifestAsync(manifest, Path.GetFileName(dir)).ConfigureAwait(false);
                    if (command != null) commands.Add(command);
                }

                foreach (string file in SafeEnumerateFiles(root, "*.md"))
                {
                    var command = await ReadCommandFileAsync(file).ConfigureAwait(false);
                    if (command != null) commands.Add(command);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SlashCommandService] Manifest scan failed for {root}: {ex.Message}");
            }
        }

        return commands;
    }

    private static IEnumerable<string> EnumerateManifestRoots(string? workingDirectory)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            yield return Path.Combine(workingDirectory, ".agents", "skills");
            yield return Path.Combine(workingDirectory, ".claude", "skills");
            yield return Path.Combine(workingDirectory, ".gemini", "skills");
            yield return Path.Combine(workingDirectory, ".claude", "commands");
        }

        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".agents", "skills");
            yield return Path.Combine(home, ".claude", "skills");
            yield return Path.Combine(home, ".claude", "commands");
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static async Task<SlashCommandDto?> ReadSkillManifestAsync(string path, string fallbackName)
    {
        try
        {
            // Frontmatter sits at the top; reading the whole file would mean loading large skill docs.
            string text = await ReadHeadAsync(path, 60).ConfigureAwait(false);

            string name = ReadFrontmatterValue(text, "name") ?? fallbackName;
            string description = ReadFrontmatterValue(text, "description") ?? string.Empty;

            return BuildSkillCommand(name, description);
        }
        catch
        {
            return BuildSkillCommand(fallbackName, string.Empty);
        }
    }

    private static async Task<SlashCommandDto?> ReadCommandFileAsync(string path)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name)) return null;

            string text = await ReadHeadAsync(path, 30).ConfigureAwait(false);
            string? frontmatterDescription = ReadFrontmatterValue(text, "description");
            string description = frontmatterDescription ?? FirstMeaningfulLine(text);

            return new SlashCommandDto(Slug(name), Truncate(description, MaxDescriptionLength), "skill", "*", null, false);
        }
        catch
        {
            return null;
        }
    }

    private static SlashCommandDto BuildSkillCommand(string name, string description)
    {
        string slug = Slug(name);
        string label = Truncate(description, MaxDescriptionLength);
        if (string.IsNullOrWhiteSpace(label)) label = "Installed agent skill";
        return new SlashCommandDto(slug, label, "skill", "*", null, false);
    }

    private static async Task<string> ReadHeadAsync(string path, int maxLines)
    {
        var builder = new StringBuilder();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        for (int i = 0; i < maxLines; i++)
        {
            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null) break;
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    /// <summary>Reads `key: value` from a leading YAML frontmatter block, if the file has one.</summary>
    private static string? ReadFrontmatterValue(string text, string key)
    {
        string[] lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return null;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Trim() == "---") break;

            int separator = line.IndexOf(':');
            if (separator <= 0) continue;
            if (!line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            string value = line[(separator + 1)..].Trim();
            return value.Trim('"', '\'');
        }

        return null;
    }

    private static string FirstMeaningfulLine(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line == "---" || line.StartsWith('#')) continue;
            if (line.Contains(':', StringComparison.Ordinal) && !line.Contains(' ', StringComparison.Ordinal)) continue;
            return line;
        }
        return string.Empty;
    }

    // --- Host-owned sources ----------------------------------------------------------------------

    private static async Task<List<SlashCommandDto>> GetMcpCommandsAsync()
    {
        try
        {
            var servers = await McpSkillsService.Instance.GetMcpServersAsync().ConfigureAwait(false);
            if (servers.Count == 0) return [];

            string names = string.Join(", ", servers.Take(4).Select(s => s.Name));
            string suffix = servers.Count > 4 ? $" +{servers.Count - 4} more" : string.Empty;

            return
            [
                new SlashCommandDto(
                    "mcp",
                    $"Manage MCP servers ({servers.Count} configured: {names}{suffix})",
                    "mcp",
                    "*")
            ];
        }
        catch
        {
            return [];
        }
    }

    private static List<SlashCommandDto> GetProfileCommands(IReadOnlyList<string>? profiles)
    {
        if (profiles == null || profiles.Count == 0) return [];

        string names = string.Join(", ", profiles.Take(4));
        string suffix = profiles.Count > 4 ? $" +{profiles.Count - 4} more" : string.Empty;

        return
        [
            new SlashCommandDto(
                "switch-profile",
                $"Switch the active runner profile ({names}{suffix})",
                "profile",
                "*",
                "<name>",
                true)
        ];
    }

    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        bool lastWasSeparator = false;

        foreach (char c in name.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string Truncate(string value, int max)
    {
        string flat = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (flat.Contains("  ", StringComparison.Ordinal)) flat = flat.Replace("  ", " ");
        if (flat.Length <= max) return flat;
        return string.Concat(flat.AsSpan(0, max - 1).TrimEnd(), "…");
    }
}
