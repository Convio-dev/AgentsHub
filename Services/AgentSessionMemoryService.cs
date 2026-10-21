#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class WorkspaceMemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Agent Session";
    public string Summary { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<string> AffectedFiles { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string SourceEngine { get; set; } = "Antigravity";
    public bool IsManual { get; set; }
    public double RelevanceScore { get; set; }
}

public sealed partial class AgentSessionMemoryService
{
    public static AgentSessionMemoryService Instance { get; } = new();

    private readonly Lock _lock = new();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "in", "on", "at", "to", "for", "of", "with",
        "by", "from", "is", "it", "this", "that", "was", "be", "as", "are"
    };

    [GeneratedRegex(@"[^\w\s\.\-]", RegexOptions.Compiled)]
    private static partial Regex CleanTokenRegex();

    [GeneratedRegex(@"^---\r?\n(.*?)\r?\n---\r?\n(.*)$", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex YamlFrontmatterRegex();

    public async Task<List<WorkspaceMemoryEntry>> GetMemoriesAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            var results = new List<WorkspaceMemoryEntry>();
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return results;

            string memoryDir = Path.Combine(repoPath, ".agents", "memory");
            if (!Directory.Exists(memoryDir))
                return results;

            var files = Directory.GetFiles(memoryDir, "*.md", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    var entry = ParseMemoryFile(file);
                    if (entry != null) results.Add(entry);
                }
                catch
                {
                    // Ignore corrupted individual files
                }
            }

            return [.. results.OrderByDescending(m => m.Timestamp)];
        }).ConfigureAwait(false);
    }

    public async Task<List<WorkspaceMemoryEntry>> SearchMemoriesAsync(string repoPath, string query, int topK = 10)
    {
        var all = await GetMemoriesAsync(repoPath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(query))
            return [.. all.Take(topK)];

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return [.. all.Take(topK)];

        var scored = new List<WorkspaceMemoryEntry>();
        foreach (var entry in all)
        {
            double score = CalculateRelevance(entry, queryTokens);
            if (score > 0.0)
            {
                entry.RelevanceScore = Math.Round(score, 2);
                scored.Add(entry);
            }
        }

        return [.. scored.OrderByDescending(s => s.RelevanceScore).Take(topK)];
    }

    public async Task<(bool success, string message)> DeleteMemoryAsync(string repoPath, string memoryId)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(repoPath) || string.IsNullOrWhiteSpace(memoryId))
                return (false, "Repository path and memory ID are required.");

            string memoryDir = Path.Combine(repoPath, ".agents", "memory");
            if (!Directory.Exists(memoryDir))
                return (false, "Memory directory does not exist.");

            lock (_lock)
            {
                var files = Directory.GetFiles(memoryDir, "*.md", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    try
                    {
                        var entry = ParseMemoryFile(file);
                        if (entry != null && string.Equals(entry.Id, memoryId, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                            return (true, $"Memory '{entry.Title}' deleted successfully.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Failed to delete memory file: {ex.Message}");
                    }
                }
            }

            return (false, "Memory record not found.");
        }).ConfigureAwait(false);
    }

    public async Task<(bool success, int count, string message)> ClearAllMemoriesAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return (false, 0, "Invalid repository path.");

            string memoryDir = Path.Combine(repoPath, ".agents", "memory");
            if (!Directory.Exists(memoryDir))
                return (true, 0, "No memories found to clear.");

            int deletedCount = 0;
            lock (_lock)
            {
                var files = Directory.GetFiles(memoryDir, "*.md", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch { }
                }
            }

            return (true, deletedCount, $"Cleared {deletedCount} memory records.");
        }).ConfigureAwait(false);
    }

    public async Task<(bool success, WorkspaceMemoryEntry? entry, string message)> AddManualMemoryAsync(
        string repoPath, string title, string summary, List<string> tags)
    {
        return await Task.Run<(bool, WorkspaceMemoryEntry?, string)>(() =>
        {
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return (false, null, "Invalid repository path.");

            if (string.IsNullOrWhiteSpace(title))
                return (false, null, "Memory title is required.");

            string memoryDir = Path.Combine(repoPath, ".agents", "memory");
            Directory.CreateDirectory(memoryDir);

            var entry = new WorkspaceMemoryEntry
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Title = title.Trim(),
                Summary = summary.Trim(),
                Timestamp = DateTime.UtcNow,
                Tags = [.. tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => !string.IsNullOrEmpty(t)).Distinct()],
                SourceEngine = "Manual Note",
                IsManual = true
            };

            string safeTitle = CleanFileName(entry.Title);
            if (safeTitle.Length > 30) safeTitle = safeTitle[..30];
            string fileName = $"note-{entry.Timestamp:yyyyMMdd-HHmmss}-{safeTitle}.md";
            string filePath = Path.Combine(memoryDir, fileName);
            entry.FilePath = filePath;

            string content = FormatMarkdownMemory(entry);
            lock (_lock)
            {
                File.WriteAllText(filePath, content, Encoding.UTF8);
            }

            return (true, entry, "Memory created successfully.");
        }).ConfigureAwait(false);
    }

    public async Task RecordSessionCompletionAsync(
        string? targetRepoPath,
        string sessionId,
        string? brainSessionId,
        string summary,
        bool isError)
    {
        await Task.Run(async () =>
        {
            try
            {
                string? repoPath = targetRepoPath;
                if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                {
                    repoPath = GitRepositoryManagerService.Instance.GetWorkingProject()?.Path;
                }

                if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                    return;

                string memoryDir = Path.Combine(repoPath, ".agents", "memory");
                Directory.CreateDirectory(memoryDir);

                // Check git modified files to record affected files
                List<string> affectedFiles = [];
                try
                {
                    var gitChanges = await GitRepositoryManagerService.Instance.GetWorkingTreeChangesAsync(repoPath).ConfigureAwait(false);
                    affectedFiles = [.. gitChanges.Take(12).Select(c => c.Path)];
                }
                catch { }

                string title = string.IsNullOrWhiteSpace(summary) ? $"Agent Session ({sessionId})" : summary;
                if (title.Length > 80) title = title[..77] + "...";

                var tags = ExtractTags(summary, affectedFiles, isError);
                if (!string.IsNullOrWhiteSpace(brainSessionId)) tags.Add("brain");

                var entry = new WorkspaceMemoryEntry
                {
                    Id = Guid.NewGuid().ToString("N")[..8],
                    Title = isError ? $"[Error] {title}" : title,
                    Summary = summary,
                    Timestamp = DateTime.UtcNow,
                    AffectedFiles = affectedFiles,
                    Tags = tags,
                    SourceEngine = $"Antigravity ({sessionId})",
                    IsManual = false
                };

                string safeTitle = CleanFileName(isError ? "error" : "task");
                string fileName = $"session-{entry.Timestamp:yyyyMMdd-HHmmss}-{safeTitle}-{entry.Id}.md";
                string filePath = Path.Combine(memoryDir, fileName);
                entry.FilePath = filePath;

                string content = FormatMarkdownMemory(entry);
                lock (_lock)
                {
                    File.WriteAllText(filePath, content, Encoding.UTF8);
                }
            }
            catch
            {
                // Non-blocking background capture
            }
        }).ConfigureAwait(false);
    }

    public async Task<string> GetContextInjectionSnippetAsync(string repoPath, string query, int maxItems = 3)
    {
        var matches = await SearchMemoriesAsync(repoPath, query, maxItems).ConfigureAwait(false);
        if (matches.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("[AGENTS_HUB_MEMORY_CONTEXT]");
        sb.AppendLine("# Relevant lessons & architectural notes from past sessions in this workspace:");
        foreach (var m in matches)
        {
            sb.AppendLine($"- Title: {m.Title}");
            if (!string.IsNullOrWhiteSpace(m.Summary))
                sb.AppendLine($"  Lesson/Context: {m.Summary}");
            if (m.AffectedFiles.Count > 0)
                sb.AppendLine($"  Files: {string.Join(", ", m.AffectedFiles.Take(5))}");
        }
        sb.AppendLine("[/AGENTS_HUB_MEMORY_CONTEXT]");
        return sb.ToString();
    }

    private static WorkspaceMemoryEntry? ParseMemoryFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string text = File.ReadAllText(filePath, Encoding.UTF8);

        var match = YamlFrontmatterRegex().Match(text);
        if (!match.Success)
        {
            // Plain markdown file fallback
            var fi = new FileInfo(filePath);
            return new WorkspaceMemoryEntry
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                Title = Path.GetFileNameWithoutExtension(filePath),
                Summary = text.Length > 300 ? text[..300] + "..." : text,
                FilePath = filePath,
                Timestamp = fi.LastWriteTimeUtc,
                SourceEngine = "Documentation",
                IsManual = true
            };
        }

        string yaml = match.Groups[1].Value;
        string body = match.Groups[2].Value.Trim();

        var entry = new WorkspaceMemoryEntry
        {
            FilePath = filePath
        };

        var lines = yaml.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        bool inAffected = false;
        bool inTags = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                entry.Id = line["id:".Length..].Trim();
                inAffected = inTags = false;
            }
            else if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
            {
                entry.Title = line["title:".Length..].Trim().Trim('"', '\'');
                inAffected = inTags = false;
            }
            else if (line.StartsWith("timestamp:", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParse(line["timestamp:".Length..].Trim(), out var dt))
                    entry.Timestamp = dt;
                inAffected = inTags = false;
            }
            else if (line.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            {
                entry.SourceEngine = line["source:".Length..].Trim();
                inAffected = inTags = false;
            }
            else if (line.StartsWith("is_manual:", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(line["is_manual:".Length..].Trim(), out var b))
                    entry.IsManual = b;
                inAffected = inTags = false;
            }
            else if (line.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
            {
                inTags = true;
                inAffected = false;
                var rest = line["tags:".Length..].Trim();
                if (rest.StartsWith('[') && rest.EndsWith(']'))
                {
                    entry.Tags = [.. rest.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToLowerInvariant())];
                    inTags = false;
                }
            }
            else if (line.StartsWith("affected_files:", StringComparison.OrdinalIgnoreCase))
            {
                inAffected = true;
                inTags = false;
            }
            else if (inAffected && line.StartsWith('-'))
            {
                entry.AffectedFiles.Add(line.TrimStart('-', ' ').Trim());
            }
            else if (inTags && line.StartsWith('-'))
            {
                entry.Tags.Add(line.TrimStart('-', ' ').Trim().ToLowerInvariant());
            }
        }

        entry.Summary = body;
        return entry;
    }

    private static string FormatMarkdownMemory(WorkspaceMemoryEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {entry.Id}");
        sb.AppendLine($"title: \"{entry.Title.Replace("\"", "\\\"")}\"");
        sb.AppendLine($"timestamp: {entry.Timestamp:O}");
        sb.AppendLine($"source: {entry.SourceEngine}");
        sb.AppendLine($"is_manual: {entry.IsManual.ToString().ToLowerInvariant()}");

        if (entry.Tags.Count > 0)
        {
            sb.AppendLine("tags:");
            foreach (var tag in entry.Tags)
                sb.AppendLine($"  - {tag}");
        }

        if (entry.AffectedFiles.Count > 0)
        {
            sb.AppendLine("affected_files:");
            foreach (var f in entry.AffectedFiles)
                sb.AppendLine($"  - {f}");
        }

        sb.AppendLine("---");
        sb.AppendLine($"# {entry.Title}");
        sb.AppendLine();
        sb.AppendLine(entry.Summary);
        return sb.ToString();
    }

    private static double CalculateRelevance(WorkspaceMemoryEntry entry, List<string> queryTokens)
    {
        double score = 0.0;
        var entryTokens = Tokenize($"{entry.Title} {entry.Summary} {string.Join(" ", entry.Tags)} {string.Join(" ", entry.AffectedFiles)}");

        foreach (var qt in queryTokens)
        {
            // Exact title match weight = 5
            if (entry.Title.Contains(qt, StringComparison.OrdinalIgnoreCase))
                score += 5.0;

            // Tag match weight = 4
            if (entry.Tags.Any(t => t.Contains(qt, StringComparison.OrdinalIgnoreCase)))
                score += 4.0;

            // Affected files match weight = 3
            if (entry.AffectedFiles.Any(f => f.Contains(qt, StringComparison.OrdinalIgnoreCase)))
                score += 3.0;

            // General body match
            int count = entryTokens.Count(t => string.Equals(t, qt, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
                score += Math.Min(count * 1.5, 6.0);
        }

        return score;
    }

    private static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var clean = CleanTokenRegex().Replace(text, " ");
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. words
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 1 && !StopWords.Contains(w))];
    }

    private static List<string> ExtractTags(string summary, List<string> files, bool isError)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (isError) tags.Add("error");
        else tags.Add("success");

        foreach (var f in files)
        {
            string ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
            if (!string.IsNullOrEmpty(ext)) tags.Add(ext);
        }

        if (summary.Contains("git", StringComparison.OrdinalIgnoreCase)) tags.Add("git");
        if (summary.Contains("test", StringComparison.OrdinalIgnoreCase)) tags.Add("testing");
        if (summary.Contains("fix", StringComparison.OrdinalIgnoreCase) || summary.Contains("bug", StringComparison.OrdinalIgnoreCase)) tags.Add("fix");
        if (summary.Contains("rpc", StringComparison.OrdinalIgnoreCase)) tags.Add("rpc");
        if (summary.Contains("ui", StringComparison.OrdinalIgnoreCase)) tags.Add("ui");

        return [.. tags.Take(6)];
    }

    private static string CleanFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (!invalid.Contains(ch) && (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
                sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
