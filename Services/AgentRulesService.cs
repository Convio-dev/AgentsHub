#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed partial class AgentRulesService
{
    public static AgentRulesService Instance { get; } = new();

    private string GlobalRulesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AgentsHub",
        "rules"
    );

    public AgentRulesService()
    {
        try
        {
            if (!Directory.Exists(GlobalRulesDir))
            {
                Directory.CreateDirectory(GlobalRulesDir);
            }
        }
        catch { }
    }

    public async Task<List<AgentRuleItem>> GetGlobalRulesAsync()
    {
        var rules = new List<AgentRuleItem>();

        try
        {
            if (Directory.Exists(GlobalRulesDir))
            {
                foreach (var file in Directory.GetFiles(GlobalRulesDir, "*.md"))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                        var name = Path.GetFileNameWithoutExtension(file);
                        var desc = ExtractSummary(content);

                        rules.Add(new AgentRuleItem
                        {
                            Id = "global_" + name.ToLowerInvariant(),
                            Name = name,
                            Description = desc,
                            Content = content,
                            Scope = "Global",
                            SourceFilePath = file,
                            IsEnabled = true,
                            TokenFootprintApprox = Math.Max(1, content.Length / 4),
                            LastModifiedUtc = File.GetLastWriteTimeUtc(file)
                        });
                    }
                    catch { }
                }
            }

            // Also check global Antigravity rules if present (~/.gemini/antigravity/rules)
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var agyRulesDir = Path.Combine(userHome, ".gemini", "antigravity", "rules");
            if (Directory.Exists(agyRulesDir))
            {
                foreach (var file in Directory.GetFiles(agyRulesDir, "*.md"))
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (rules.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

                        var content = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                        rules.Add(new AgentRuleItem
                        {
                            Id = "agy_global_" + name.ToLowerInvariant(),
                            Name = name,
                            Description = ExtractSummary(content),
                            Content = content,
                            Scope = "Global",
                            SourceFilePath = file,
                            IsEnabled = true,
                            TokenFootprintApprox = Math.Max(1, content.Length / 4),
                            LastModifiedUtc = File.GetLastWriteTimeUtc(file)
                        });
                    }
                    catch { }
                }
            }
        }
        catch { }

        return rules;
    }

    public async Task<List<AgentRuleItem>> GetWorkspaceRulesAsync(ProjectWorkspace? workspace)
    {
        var rules = new List<AgentRuleItem>();
        if (workspace == null || string.IsNullOrWhiteSpace(workspace.WorkingDirectory) || !Directory.Exists(workspace.WorkingDirectory))
        {
            return rules;
        }

        var workDir = workspace.WorkingDirectory;

        // 1. Parse AGENTS.md in workspace root
        var agentsMdPath = Path.Combine(workDir, "AGENTS.md");
        if (File.Exists(agentsMdPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(agentsMdPath).ConfigureAwait(false);
                var parsedBlocks = ParseRulesFromMarkdown(content, agentsMdPath, workspace.Id);
                rules.AddRange(parsedBlocks);
            }
            catch { }
        }

        // 2. Parse standalone .agents/rules/*.md if present
        var rulesSubDir = Path.Combine(workDir, ".agents", "rules");
        if (Directory.Exists(rulesSubDir))
        {
            foreach (var file in Directory.GetFiles(rulesSubDir, "*.md"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (rules.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

                    var fileContent = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    rules.Add(new AgentRuleItem
                    {
                        Id = "ws_" + workspace.Id + "_" + name.ToLowerInvariant(),
                        Name = name,
                        Description = ExtractSummary(fileContent),
                        Content = fileContent,
                        Scope = "Project",
                        WorkspaceId = workspace.Id,
                        SourceFilePath = file,
                        IsEnabled = true,
                        TokenFootprintApprox = Math.Max(1, fileContent.Length / 4),
                        LastModifiedUtc = File.GetLastWriteTimeUtc(file)
                    });
                }
                catch { }
            }
        }

        return rules;
    }

    public async Task<object> GetRulesOverviewAsync(string? workspaceId = null)
    {
        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var activeWs = !string.IsNullOrEmpty(workspaceId)
            ? workspaces.FirstOrDefault(w => string.Equals(w.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
            : AiConfigService.Instance.GetActiveWorkspace();

        var globalRules = await GetGlobalRulesAsync().ConfigureAwait(false);
        var workspaceRules = await GetWorkspaceRulesAsync(activeWs).ConfigureAwait(false);

        var disabledIds = activeWs?.DisabledRuleIds ?? [];
        bool isIsolated = activeWs?.IsolateRules ?? false;

        var allItems = new List<AgentRuleItem>();

        // Process Global Rules
        foreach (var gr in globalRules)
        {
            var isGloballyDisabled = disabledIds.Contains(gr.Id, StringComparer.OrdinalIgnoreCase) ||
                                     disabledIds.Contains(gr.Name, StringComparer.OrdinalIgnoreCase);

            gr.WorkspaceId = activeWs?.Id;
            gr.IsExcludedByIsolation = isIsolated;
            gr.IsEnabled = !isIsolated && !isGloballyDisabled;
            allItems.Add(gr);
        }

        // Process Workspace Rules
        foreach (var wr in workspaceRules)
        {
            var isWsDisabled = disabledIds.Contains(wr.Id, StringComparer.OrdinalIgnoreCase) ||
                               disabledIds.Contains(wr.Name, StringComparer.OrdinalIgnoreCase);

            wr.IsExcludedByIsolation = false;
            wr.IsEnabled = !isWsDisabled;
            allItems.Add(wr);
        }

        return new
        {
            activeWorkspaceId = activeWs?.Id ?? "",
            activeWorkspaceName = activeWs?.Name ?? "No Workspace Selected",
            isIsolated,
            disabledRuleIds = disabledIds,
            rules = allItems,
            globalCount = globalRules.Count,
            workspaceCount = workspaceRules.Count,
            effectiveCount = allItems.Count(r => r.IsEnabled)
        };
    }

    public async Task<bool> SaveRuleAsync(AgentRuleItem rule, string? targetWorkspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.Content))
        {
            return false;
        }

        var safeName = Regex.Replace(rule.Name.Trim(), @"[^a-zA-Z0-9_\-\.]", "_");

        if (string.Equals(rule.Scope, "Global", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(GlobalRulesDir)) Directory.CreateDirectory(GlobalRulesDir);
                var path = Path.Combine(GlobalRulesDir, $"{safeName}.md");
                await File.WriteAllTextAsync(path, rule.Content.Trim()).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Project Scope
        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = !string.IsNullOrEmpty(targetWorkspaceId)
            ? workspaces.FirstOrDefault(w => string.Equals(w.Id, targetWorkspaceId, StringComparison.OrdinalIgnoreCase))
            : AiConfigService.Instance.GetActiveWorkspace();

        if (ws == null || string.IsNullOrWhiteSpace(ws.WorkingDirectory) || !Directory.Exists(ws.WorkingDirectory))
        {
            return false;
        }

        var agentsMdPath = Path.Combine(ws.WorkingDirectory, "AGENTS.md");
        try
        {
            string existing = File.Exists(agentsMdPath) ? await File.ReadAllTextAsync(agentsMdPath).ConfigureAwait(false) : "";
            string newContent = UpsertRuleBlockInMarkdown(existing, safeName, rule.Content.Trim());
            await File.WriteAllTextAsync(agentsMdPath, newContent).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteRuleAsync(string ruleId, string ruleName, string scope, string? targetWorkspaceId = null)
    {
        var effectiveName = !string.IsNullOrWhiteSpace(ruleName) ? ruleName : ruleId;

        if (string.Equals(scope, "Global", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var safeName = Regex.Replace(effectiveName.Trim(), @"[^a-zA-Z0-9_\-\.]", "_");
                var path = Path.Combine(GlobalRulesDir, $"{safeName}.md");
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch { }
            return false;
        }

        // Project Scope
        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = !string.IsNullOrEmpty(targetWorkspaceId)
            ? workspaces.FirstOrDefault(w => string.Equals(w.Id, targetWorkspaceId, StringComparison.OrdinalIgnoreCase))
            : AiConfigService.Instance.GetActiveWorkspace();

        if (ws == null || string.IsNullOrWhiteSpace(ws.WorkingDirectory)) return false;

        var agentsMdPath = Path.Combine(ws.WorkingDirectory, "AGENTS.md");
        if (File.Exists(agentsMdPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(agentsMdPath).ConfigureAwait(false);
                var newContent = RemoveRuleBlockFromMarkdown(content, effectiveName);
                await File.WriteAllTextAsync(agentsMdPath, newContent).ConfigureAwait(false);

                // Clean up workspace rule overrides if any
                ws.DisabledRuleIds?.RemoveAll(id => string.Equals(id, ruleId, StringComparison.OrdinalIgnoreCase) || string.Equals(id, effectiveName, StringComparison.OrdinalIgnoreCase));
                ws.EnabledProjectRuleIds?.RemoveAll(id => string.Equals(id, ruleId, StringComparison.OrdinalIgnoreCase) || string.Equals(id, effectiveName, StringComparison.OrdinalIgnoreCase));
                AiConfigService.Instance.SaveWorkspace(ws);

                return true;
            }
            catch { }
        }

        return false;
    }

    public bool ToggleRuleForWorkspace(string workspaceId, string ruleIdentifier, bool enable)
    {
        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = workspaces.FirstOrDefault(w => string.Equals(w.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
        if (ws == null) return false;

        ws.DisabledRuleIds ??= [];

        if (enable)
        {
            ws.DisabledRuleIds.RemoveAll(id => string.Equals(id, ruleIdentifier, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            if (!ws.DisabledRuleIds.Any(id => string.Equals(id, ruleIdentifier, StringComparison.OrdinalIgnoreCase)))
            {
                ws.DisabledRuleIds.Add(ruleIdentifier);
            }
        }

        AiConfigService.Instance.SaveWorkspace(ws);
        return true;
    }

    public bool SetWorkspaceRuleIsolation(string workspaceId, bool isolate)
    {
        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = workspaces.FirstOrDefault(w => string.Equals(w.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
        if (ws == null) return false;

        ws.IsolateRules = isolate;
        AiConfigService.Instance.SaveWorkspace(ws);
        return true;
    }

    private static List<AgentRuleItem> ParseRulesFromMarkdown(string markdown, string filePath, string workspaceId)
    {
        var list = new List<AgentRuleItem>();
        if (string.IsNullOrWhiteSpace(markdown)) return list;

        // Match <!-- <name>:start --> ... <!-- <name>:end -->
        var blockRegex = new Regex(@"<!--\s*([a-zA-Z0-9_\-\.]+):start\s*-->([\s\S]*?)<!--\s*\1:end\s*-->", RegexOptions.IgnoreCase);
        var matches = blockRegex.Matches(markdown);

        if (matches.Count > 0)
        {
            foreach (Match m in matches)
            {
                var name = m.Groups[1].Value.Trim();
                var blockContent = m.Groups[2].Value.Trim();

                list.Add(new AgentRuleItem
                {
                    Id = "ws_" + workspaceId + "_" + name.ToLowerInvariant(),
                    Name = name,
                    Description = ExtractSummary(blockContent),
                    Content = blockContent,
                    Scope = "Project",
                    WorkspaceId = workspaceId,
                    SourceFilePath = filePath,
                    IsEnabled = true,
                    TokenFootprintApprox = Math.Max(1, blockContent.Length / 4),
                    LastModifiedUtc = File.GetLastWriteTimeUtc(filePath)
                });
            }
            return list;
        }

        // Fallback: split by Markdown H2 headings ## Name
        var headingRegex = new Regex(@"(^|\n)##\s+([^\r\n]+)\r?\n", RegexOptions.Multiline);
        var hMatches = headingRegex.Matches(markdown);
        if (hMatches.Count > 0)
        {
            for (int i = 0; i < hMatches.Count; i++)
            {
                var title = hMatches[i].Groups[2].Value.Trim();
                var start = hMatches[i].Index + hMatches[i].Length;
                var end = (i + 1 < hMatches.Count) ? hMatches[i + 1].Index : markdown.Length;
                var chunk = markdown[start..end].Trim();

                list.Add(new AgentRuleItem
                {
                    Id = "ws_" + workspaceId + "_" + Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9_]", "_"),
                    Name = title,
                    Description = ExtractSummary(chunk),
                    Content = chunk,
                    Scope = "Project",
                    WorkspaceId = workspaceId,
                    SourceFilePath = filePath,
                    IsEnabled = true,
                    TokenFootprintApprox = Math.Max(1, chunk.Length / 4),
                    LastModifiedUtc = File.GetLastWriteTimeUtc(filePath)
                });
            }
        }
        else
        {
            // Whole file as single rule
            list.Add(new AgentRuleItem
            {
                Id = "ws_" + workspaceId + "_agents_md",
                Name = "Workspace AGENTS.md",
                Description = ExtractSummary(markdown),
                Content = markdown.Trim(),
                Scope = "Project",
                WorkspaceId = workspaceId,
                SourceFilePath = filePath,
                IsEnabled = true,
                TokenFootprintApprox = Math.Max(1, markdown.Length / 4),
                LastModifiedUtc = File.GetLastWriteTimeUtc(filePath)
            });
        }

        return list;
    }

    private static string UpsertRuleBlockInMarkdown(string markdown, string ruleName, string content)
    {
        var block = $"<!-- {ruleName}:start -->\n{content}\n<!-- {ruleName}:end -->";
        var pattern = $@"<!--\s*{Regex.Escape(ruleName)}:start\s*-->[\s\S]*?<!--\s*{Regex.Escape(ruleName)}:end\s*-->";

        if (Regex.IsMatch(markdown, pattern, RegexOptions.IgnoreCase))
        {
            return Regex.Replace(markdown, pattern, block, RegexOptions.IgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return block + "\n";
        }

        return markdown.TrimEnd() + "\n\n" + block + "\n";
    }

    private static string RemoveRuleBlockFromMarkdown(string markdown, string ruleName)
    {
        var pattern = $@"<!--\s*{Regex.Escape(ruleName)}:start\s*-->[\s\S]*?<!--\s*{Regex.Escape(ruleName)}:end\s*-->\r?\n?";
        var cleaned = Regex.Replace(markdown, pattern, "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrEmpty(cleaned) ? "" : cleaned + "\n";
    }

    private static string ExtractSummary(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "No description provided.";

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var l in lines)
        {
            var trimmed = l.Trim();
            if (trimmed.StartsWith('#') || trimmed.StartsWith("<!--") || trimmed.StartsWith("```") || string.IsNullOrEmpty(trimmed))
            {
                continue;
            }
            if (trimmed.Length > 120) return trimmed[..117] + "...";
            return trimmed;
        }

        return "Agent behavior guideline";
    }
}
