#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class McpServerItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Scope { get; set; } = "Global"; // Global, Antigravity, ClaudeCode
    public bool IsEnabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public int ToolCount { get; set; }
    public bool RequiresAuth { get; set; }
    public string AuthStatus { get; set; } = "NotRequired"; // Authenticated, TokenExpired, NeedsAuth, NotRequired
    public string AuthDetails { get; set; } = string.Empty;
    public string? AuthKeyName { get; set; }
}

public sealed class AgentSkillItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string Scope { get; set; } = "Project"; // Project, Builtin
    public string? WorkspaceId { get; set; }
    public string? WorkspaceName { get; set; }
    public int TokenFootprintApprox { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Content { get; set; }
}

public sealed class McpSkillsService
{
    public static McpSkillsService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string ConfigFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentsHub",
        "mcp_servers.json"
    );

    public async Task<List<McpServerItem>> GetMcpServersAsync()
    {
        var list = new List<McpServerItem>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Read stored custom servers from AgentsHub config
        if (File.Exists(ConfigFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(ConfigFilePath).ConfigureAwait(false);
                var stored = JsonSerializer.Deserialize<List<McpServerItem>>(json, JsonOpts);
                if (stored != null)
                {
                    foreach (var s in stored)
                    {
                        if (seenNames.Add(s.Name)) list.Add(s);
                    }
                }
            }
            catch { }
        }

        // 2. Discover Antigravity MCP servers (~/.gemini/antigravity/mcp_config.json & ~/.gemini/antigravity/mcp)
        try
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var agyDir = Path.Combine(userHome, ".gemini", "antigravity");
            var agyMcpDir = Path.Combine(agyDir, "mcp");
            var mcpConfigPath = Path.Combine(agyDir, "mcp_config.json");

            // Read defined commands, args, and env from mcp_config.json
            if (File.Exists(mcpConfigPath))
            {
                var cfgJson = await File.ReadAllTextAsync(mcpConfigPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(cfgJson);
                if (doc.RootElement.TryGetProperty("mcpServers", out var serversProp) && serversProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var sProp in serversProp.EnumerateObject())
                    {
                        var sName = sProp.Name;
                        if (seenNames.Add(sName))
                        {
                            var cmd = sProp.Value.TryGetProperty("command", out var cProp) ? cProp.GetString() ?? "npx" : "npx";
                            var args = new List<string>();
                            if (sProp.Value.TryGetProperty("args", out var aProp) && aProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var a in aProp.EnumerateArray()) args.Add(a.GetString() ?? "");
                            }

                            var envDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            if (sProp.Value.TryGetProperty("env", out var eProp) && eProp.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var envEntry in eProp.EnumerateObject())
                                {
                                    envDict[envEntry.Name] = envEntry.Value.GetString() ?? "";
                                }
                            }

                            int toolCount = 0;
                            var toolDir = Path.Combine(agyMcpDir, sName);
                            if (Directory.Exists(toolDir))
                            {
                                toolCount = Directory.GetFiles(toolDir, "*.json").Length;
                            }

                            list.Add(new McpServerItem
                            {
                                Id = "agy_" + sName.ToLowerInvariant(),
                                Name = sName,
                                Command = cmd,
                                Args = args,
                                Env = envDict,
                                Scope = "Antigravity",
                                IsEnabled = true,
                                Description = toolCount > 0
                                    ? $"Antigravity MCP server with {toolCount} tool definition(s)"
                                    : $"Antigravity MCP server ({cmd})",
                                ToolCount = toolCount
                            });
                        }
                    }
                }
            }

            // Fallback for any tool folder not yet in mcp_config.json
            if (Directory.Exists(agyMcpDir))
            {
                foreach (var serverDir in Directory.GetDirectories(agyMcpDir))
                {
                    var dirName = Path.GetFileName(serverDir);
                    if (seenNames.Add(dirName))
                    {
                        var toolFiles = Directory.GetFiles(serverDir, "*.json");
                        list.Add(new McpServerItem
                        {
                            Id = "agy_" + dirName.ToLowerInvariant(),
                            Name = dirName,
                            Command = "builtin",
                            Scope = "Antigravity",
                            IsEnabled = true,
                            Description = $"Antigravity MCP server with {toolFiles.Length} tool definition(s)",
                            ToolCount = toolFiles.Length
                        });
                    }
                }
            }
        }
        catch { }

        // 3. Discover Claude Code desktop config (~/AppData/Roaming/Claude/claude_desktop_config.json)
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var claudeCfg = Path.Combine(appData, "Claude", "claude_desktop_config.json");
            if (File.Exists(claudeCfg))
            {
                var json = await File.ReadAllTextAsync(claudeCfg).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("mcpServers", out var mcpProp) && mcpProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in mcpProp.EnumerateObject())
                    {
                        if (seenNames.Add(prop.Name))
                        {
                            var cmd = prop.Value.TryGetProperty("command", out var cProp) ? cProp.GetString() ?? "" : "";
                            var args = new List<string>();
                            if (prop.Value.TryGetProperty("args", out var aProp) && aProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var a in aProp.EnumerateArray()) args.Add(a.GetString() ?? "");
                            }

                            var envDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            if (prop.Value.TryGetProperty("env", out var eProp) && eProp.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var envEntry in eProp.EnumerateObject())
                                {
                                    envDict[envEntry.Name] = envEntry.Value.GetString() ?? "";
                                }
                            }

                            list.Add(new McpServerItem
                            {
                                Id = "claude_" + prop.Name.ToLowerInvariant(),
                                Name = prop.Name,
                                Command = cmd,
                                Args = args,
                                Env = envDict,
                                Scope = "ClaudeCode",
                                IsEnabled = true,
                                Description = "Configured in Claude Desktop configuration"
                            });
                        }
                    }
                }
            }
        }
        catch { }

        // 4. Run automated auth detection across all servers
        foreach (var s in list)
        {
            DetectAuthStatus(s);
        }

        return list;
    }

    public async Task<bool> SaveMcpServerAsync(McpServerItem server)
    {
        var servers = await GetMcpServersAsync().ConfigureAwait(false);
        var idx = servers.FindIndex(s => string.Equals(s.Id, server.Id, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            servers[idx] = server;
        }
        else
        {
            servers.Add(server);
        }

        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(servers, JsonOpts);
            await File.WriteAllTextAsync(ConfigFilePath, json).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteMcpServerAsync(string id)
    {
        var servers = await GetMcpServersAsync().ConfigureAwait(false);
        var removed = servers.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            try
            {
                var json = JsonSerializer.Serialize(servers, JsonOpts);
                await File.WriteAllTextAsync(ConfigFilePath, json).ConfigureAwait(false);
                return true;
            }
            catch { }
        }
        return false;
    }

    public async Task<List<AgentSkillItem>> GetAgentSkillsAsync(string? workspaceDir = null)
    {
        var list = new List<AgentSkillItem>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var activeWs = AiConfigService.Instance.GetActiveWorkspace();

        var searchRoots = new List<(string Root, string Scope, string? WsId, string? WsName)>();

        // 1. Scan active workspace first
        if (!string.IsNullOrWhiteSpace(workspaceDir) && Directory.Exists(workspaceDir))
        {
            var matchedWs = workspaces.FirstOrDefault(w => string.Equals(w.WorkingDirectory, workspaceDir, StringComparison.OrdinalIgnoreCase)) ?? activeWs;
            searchRoots.Add((Path.Combine(workspaceDir, ".agents", "skills"), "Project", matchedWs?.Id, matchedWs?.Name));
        }
        else if (activeWs != null && !string.IsNullOrWhiteSpace(activeWs.WorkingDirectory) && Directory.Exists(activeWs.WorkingDirectory))
        {
            searchRoots.Add((Path.Combine(activeWs.WorkingDirectory, ".agents", "skills"), "Project", activeWs.Id, activeWs.Name));
        }

        // Scan all configured workspaces so user can see and jump to skills across projects
        foreach (var ws in workspaces)
        {
            if (!string.IsNullOrWhiteSpace(ws.WorkingDirectory) && Directory.Exists(ws.WorkingDirectory))
            {
                var skillsDir = Path.Combine(ws.WorkingDirectory, ".agents", "skills");
                if (!searchRoots.Any(sr => string.Equals(sr.Root, skillsDir, StringComparison.OrdinalIgnoreCase)))
                {
                    searchRoots.Add((skillsDir, "Project", ws.Id, ws.Name));
                }
            }
        }

        searchRoots.Add((Path.Combine(AppContext.BaseDirectory, ".agents", "skills"), "Project", activeWs?.Id, activeWs?.Name));

        // 2. Scan global Antigravity builtin skills
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        searchRoots.Add((Path.Combine(userHome, ".gemini", "antigravity", "builtin", "skills"), "Builtin", null, null));

        foreach (var (root, scope, wsId, wsName) in searchRoots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var skillDir in Directory.GetDirectories(root))
            {
                var skillFile = Path.Combine(skillDir, "SKILL.md");
                if (!File.Exists(skillFile)) continue;

                var dirName = Path.GetFileName(skillDir);
                var dedupeKey = $"{scope}_{wsId}_{dirName}";
                if (!seenKeys.Add(dedupeKey)) continue;

                var name = dirName;
                var desc = "Agent capability skill";
                string fullContent = string.Empty;

                try
                {
                    fullContent = await File.ReadAllTextAsync(skillFile).ConfigureAwait(false);
                    using var reader = new StringReader(fullContent);
                    string? line;
                    bool inFrontmatter = false;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var trimmed = line.Trim();
                        if (trimmed == "---")
                        {
                            if (!inFrontmatter) { inFrontmatter = true; continue; }
                            else break;
                        }

                        if (inFrontmatter)
                        {
                            if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                            {
                                name = trimmed[5..].Trim().Trim('"', '\'');
                            }
                            else if (trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                            {
                                desc = trimmed[12..].Trim().Trim('"', '\'');
                            }
                        }
                    }
                }
                catch { }

                list.Add(new AgentSkillItem
                {
                    Id = dirName,
                    Name = name,
                    Description = desc,
                    Path = skillFile,
                    DirectoryPath = skillDir,
                    Scope = scope,
                    WorkspaceId = wsId,
                    WorkspaceName = wsName,
                    TokenFootprintApprox = Math.Max(1, fullContent.Length / 4),
                    IsEnabled = true
                });
            }
        }

        return list;
    }

    public async Task<(string content, string name, string description, string scope, int tokenCount)> GetSkillContentAsync(string skillPath)
    {
        if (!File.Exists(skillPath))
            return (string.Empty, string.Empty, string.Empty, "Builtin", 0);

        var content = await File.ReadAllTextAsync(skillPath).ConfigureAwait(false);
        var dirName = Path.GetFileName(Path.GetDirectoryName(skillPath)) ?? "skill";
        var name = dirName;
        var desc = "Agent capability skill";
        var isBuiltin = skillPath.Contains("builtin", StringComparison.OrdinalIgnoreCase);

        try
        {
            using var reader = new StringReader(content);
            string? line;
            bool inFrontmatter = false;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed == "---")
                {
                    if (!inFrontmatter) inFrontmatter = true;
                    else break;
                    continue;
                }
                if (inFrontmatter)
                {
                    if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                        name = trimmed[5..].Trim().Trim('"', '\'');
                    else if (trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                        desc = trimmed[12..].Trim().Trim('"', '\'');
                }
            }
        }
        catch { }

        int tokenCount = Math.Max(1, content.Length / 4);
        return (content, name, desc, isBuiltin ? "Builtin" : "Project", tokenCount);
    }

    public async Task<bool> SaveSkillContentAsync(string skillPath, string name, string description, string content)
    {
        if (skillPath.Contains("builtin", StringComparison.OrdinalIgnoreCase))
            return false; // Builtin skills are immutable

        var dir = Path.GetDirectoryName(skillPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string fileText;
        if (content.TrimStart().StartsWith("---"))
        {
            fileText = content;
        }
        else
        {
            fileText = $"---\nname: {name.Trim()}\ndescription: {description.Trim()}\n---\n\n{content.Trim()}\n";
        }

        await File.WriteAllTextAsync(skillPath, fileText).ConfigureAwait(false);
        return true;
    }

    public async Task<AgentSkillItem?> CreateProjectSkillAsync(string? workspaceId, string name, string description, string content)
    {
        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = !string.IsNullOrEmpty(workspaceId)
            ? workspaces.FirstOrDefault(w => string.Equals(w.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
            : AiConfigService.Instance.GetActiveWorkspace();

        if (ws == null || string.IsNullOrWhiteSpace(ws.WorkingDirectory)) return null;

        var safeSlug = System.Text.RegularExpressions.Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9_\-]", "-").Trim('-');
        if (string.IsNullOrEmpty(safeSlug)) safeSlug = "custom-skill";

        var skillDir = Path.Combine(ws.WorkingDirectory, ".agents", "skills", safeSlug);
        Directory.CreateDirectory(skillDir);

        var skillFile = Path.Combine(skillDir, "SKILL.md");
        string fileText;
        if (content.TrimStart().StartsWith("---"))
        {
            fileText = content;
        }
        else
        {
            fileText = $"---\nname: {name.Trim()}\ndescription: {description.Trim()}\n---\n\n{content.Trim()}\n";
        }

        await File.WriteAllTextAsync(skillFile, fileText).ConfigureAwait(false);

        return new AgentSkillItem
        {
            Id = safeSlug,
            Name = name,
            Description = description,
            Path = skillFile,
            DirectoryPath = skillDir,
            Scope = "Project",
            WorkspaceId = ws.Id,
            WorkspaceName = ws.Name,
            TokenFootprintApprox = Math.Max(1, fileText.Length / 4),
            IsEnabled = true,
            Content = fileText
        };
    }

    public async Task<AgentSkillItem?> ForkSkillToWorkspaceAsync(string sourceSkillPath, string? targetWorkspaceId)
    {
        if (!File.Exists(sourceSkillPath)) return null;

        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = !string.IsNullOrEmpty(targetWorkspaceId)
            ? workspaces.FirstOrDefault(w => string.Equals(w.Id, targetWorkspaceId, StringComparison.OrdinalIgnoreCase))
            : AiConfigService.Instance.GetActiveWorkspace();

        if (ws == null || string.IsNullOrWhiteSpace(ws.WorkingDirectory)) return null;

        var sourceDir = Path.GetDirectoryName(sourceSkillPath);
        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return null;

        var skillSlug = Path.GetFileName(sourceDir);
        var targetDir = Path.Combine(ws.WorkingDirectory, ".agents", "skills", skillSlug);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(targetDir, relative);
            var destSubDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destSubDir) && !Directory.Exists(destSubDir))
                Directory.CreateDirectory(destSubDir);

            File.Copy(file, destFile, true);
        }

        var targetSkillFile = Path.Combine(targetDir, "SKILL.md");
        var content = await File.ReadAllTextAsync(targetSkillFile).ConfigureAwait(false);

        return new AgentSkillItem
        {
            Id = skillSlug,
            Name = skillSlug,
            Description = "Forked project skill",
            Path = targetSkillFile,
            DirectoryPath = targetDir,
            Scope = "Project",
            WorkspaceId = ws.Id,
            WorkspaceName = ws.Name,
            TokenFootprintApprox = Math.Max(1, content.Length / 4),
            IsEnabled = true,
            Content = content
        };
    }

    public bool DeleteProjectSkill(string skillPath)
    {
        if (skillPath.Contains("builtin", StringComparison.OrdinalIgnoreCase))
            return false;

        var skillDir = Path.GetDirectoryName(skillPath);
        if (string.IsNullOrEmpty(skillDir) || !Directory.Exists(skillDir)) return false;

        if (!skillDir.Replace('\\', '/').Contains("/.agents/skills/")) return false;

        try
        {
            Directory.Delete(skillDir, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return true;
            }
            else if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                return true;
            }
        }
        catch { }
        return false;
    }

    public static bool OpenInEditor(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
        }
        catch { }
        return false;
    }

    public static void DetectAuthStatus(McpServerItem item)
    {
        var name = item.Name.Trim().ToLowerInvariant();

        if (name.Contains("supabase"))
        {
            item.RequiresAuth = true;
            item.AuthKeyName = "SUPABASE_ACCESS_TOKEN";

            if (item.Env.TryGetValue("SUPABASE_ACCESS_TOKEN", out var token) && !string.IsNullOrWhiteSpace(token))
            {
                item.AuthStatus = "Authenticated";
                var masked = token.Length > 8 ? token[..4] + "..." + token[^4..] : "configured";
                item.AuthDetails = $"Access Token: {masked}";
                return;
            }

            if (item.Env.TryGetValue("SUPABASE_SERVICE_ROLE_KEY", out var sKey) && !string.IsNullOrWhiteSpace(sKey))
            {
                item.AuthStatus = "Authenticated";
                item.AuthDetails = "Service Role Key configured";
                return;
            }

            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var tokensDir = Path.Combine(userProfile, ".mcp-auth", "mcp-remote-v1");
                if (Directory.Exists(tokensDir))
                {
                    var tokenFiles = Directory.GetFiles(tokensDir, "*_tokens.json");
                    long latestExpiry = 0;
                    foreach (var tf in tokenFiles)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(tf));
                            if (doc.RootElement.TryGetProperty("expires_at", out var expProp) && expProp.TryGetInt64(out var expMs))
                            {
                                if (expMs > latestExpiry) latestExpiry = expMs;
                            }
                        }
                        catch { }
                    }

                    if (latestExpiry > 0)
                    {
                        var expDto = DateTimeOffset.FromUnixTimeMilliseconds(latestExpiry);
                        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < latestExpiry)
                        {
                            item.AuthStatus = "Authenticated";
                            item.AuthDetails = $"OAuth Active (valid until {expDto.LocalDateTime:MMM dd, HH:mm})";
                            return;
                        }
                        else
                        {
                            item.AuthStatus = "TokenExpired";
                            item.AuthDetails = $"OAuth token expired on {expDto.LocalDateTime:MMM dd, yyyy}";
                            return;
                        }
                    }
                }
            }
            catch { }

            item.AuthStatus = "NeedsAuth";
            item.AuthDetails = "Supabase Access Token or OAuth Login required";
            return;
        }

        if (name.Contains("firebase"))
        {
            item.RequiresAuth = true;
            item.AuthKeyName = "FIREBASE_TOKEN";

            if (item.Env.TryGetValue("FIREBASE_TOKEN", out var fbTok) && !string.IsNullOrWhiteSpace(fbTok))
            {
                item.AuthStatus = "Authenticated";
                item.AuthDetails = "Firebase Token configured in environment";
                return;
            }

            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var cfgPath = Path.Combine(userProfile, ".config", "configstore", "firebase-tools.json");
                if (File.Exists(cfgPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
                    if (doc.RootElement.TryGetProperty("user", out var uProp) &&
                        uProp.TryGetProperty("email", out var emProp))
                    {
                        var email = emProp.GetString();
                        if (!string.IsNullOrEmpty(email))
                        {
                            item.AuthStatus = "Authenticated";
                            item.AuthDetails = $"CLI logged in as {email}";
                            return;
                        }
                    }
                    if (doc.RootElement.TryGetProperty("tokens", out var _))
                    {
                        item.AuthStatus = "Authenticated";
                        item.AuthDetails = "OAuth session found in Firebase config";
                        return;
                    }
                }
            }
            catch { }

            item.AuthStatus = "NeedsAuth";
            item.AuthDetails = "Run 'firebase login' or set FIREBASE_TOKEN";
            return;
        }

        if (name.Contains("github"))
        {
            item.RequiresAuth = true;
            item.AuthKeyName = "GITHUB_PERSONAL_ACCESS_TOKEN";

            if (item.Env.TryGetValue("GITHUB_PERSONAL_ACCESS_TOKEN", out var ghTok) && !string.IsNullOrWhiteSpace(ghTok))
            {
                item.AuthStatus = "Authenticated";
                item.AuthDetails = "Personal Access Token configured";
                return;
            }

            var envGh = Environment.GetEnvironmentVariable("GITHUB_PERSONAL_ACCESS_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(envGh))
            {
                item.AuthStatus = "Authenticated";
                item.AuthDetails = "Token found in system environment";
                return;
            }

            item.AuthStatus = "NeedsAuth";
            item.AuthDetails = "Personal Access Token required";
            return;
        }

        foreach (var kvp in item.Env)
        {
            var k = kvp.Key.ToUpperInvariant();
            if (k.Contains("KEY") || k.Contains("TOKEN") || k.Contains("SECRET") || k.Contains("AUTH"))
            {
                item.RequiresAuth = true;
                item.AuthKeyName = kvp.Key;
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                {
                    item.AuthStatus = "Authenticated";
                    item.AuthDetails = $"{kvp.Key} configured";
                    return;
                }
                else
                {
                    item.AuthStatus = "NeedsAuth";
                    item.AuthDetails = $"{kvp.Key} is required";
                    return;
                }
            }
        }

        item.RequiresAuth = false;
        item.AuthStatus = "NotRequired";
        item.AuthDetails = "Local tool (No auth required)";
    }

    public async Task<bool> SaveMcpAuthKeyAsync(string serverName, string keyName, string keyValue)
    {
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(keyName)) return false;

        bool updatedAny = false;

        // 1. Update ~/.gemini/antigravity/mcp_config.json if it exists
        try
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var mcpConfigPath = Path.Combine(userHome, ".gemini", "antigravity", "mcp_config.json");
            if (File.Exists(mcpConfigPath))
            {
                var json = await File.ReadAllTextAsync(mcpConfigPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var rootDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOpts);
                if (rootDict != null && rootDict.TryGetValue("mcpServers", out var serversObj))
                {
                    var serversJson = JsonSerializer.Serialize(serversObj, JsonOpts);
                    var serversDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serversJson, JsonOpts);
                    if (serversDict != null)
                    {
                        foreach (var kvp in serversDict)
                        {
                            if (string.Equals(kvp.Key, serverName, StringComparison.OrdinalIgnoreCase))
                            {
                                var srvObj = JsonSerializer.Deserialize<Dictionary<string, object>>(kvp.Value.GetRawText(), JsonOpts) ?? [];
                                Dictionary<string, string> envDict = [];
                                if (srvObj.TryGetValue("env", out var existingEnv))
                                {
                                    envDict = JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(existingEnv), JsonOpts) ?? [];
                                }
                                envDict[keyName] = keyValue.Trim();
                                srvObj["env"] = envDict;
                                serversDict[kvp.Key] = JsonSerializer.SerializeToElement(srvObj, JsonOpts);
                                rootDict["mcpServers"] = serversDict;
                                await File.WriteAllTextAsync(mcpConfigPath, JsonSerializer.Serialize(rootDict, JsonOpts)).ConfigureAwait(false);
                                updatedAny = true;
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Also update AgentsHub custom mcp_servers.json if configured there
        try
        {
            var servers = await GetMcpServersAsync().ConfigureAwait(false);
            var target = servers.Find(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.Env[keyName] = keyValue.Trim();
                await SaveMcpServerAsync(target).ConfigureAwait(false);
                updatedAny = true;
            }
        }
        catch { }

        return updatedAny;
    }

    public async Task<object> TestMcpAuthAsync(string serverName, string keyValue)
    {
        var cleanVal = keyValue.Trim();
        if (string.IsNullOrWhiteSpace(cleanVal))
        {
            return new { valid = false, message = "Key or token cannot be empty." };
        }

        var sName = serverName.Trim().ToLowerInvariant();

        if (sName.Contains("supabase"))
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanVal);
                client.DefaultRequestHeaders.Add("User-Agent", "AgentsHub/1.0");

                var resp = await client.GetAsync("https://api.supabase.com/v1/projects").ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    return new { valid = true, message = "Supabase Access Token verified successfully! Connected to projects." };
                }
                else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new { valid = false, message = "Supabase rejected the token: 401 Unauthorized." };
                }
            }
            catch { }

            if (cleanVal.StartsWith("sbp_") || cleanVal.StartsWith("ey"))
            {
                return new { valid = true, message = "Token format valid (Supabase Access Token)." };
            }

            return new { valid = true, message = "Supabase key saved." };
        }

        if (sName.Contains("github"))
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanVal);
                client.DefaultRequestHeaders.Add("User-Agent", "AgentsHub/1.0");

                var resp = await client.GetAsync("https://api.github.com/user").ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    return new { valid = true, message = "GitHub Personal Access Token verified successfully!" };
                }
                else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new { valid = false, message = "GitHub rejected the token: 401 Unauthorized." };
                }
            }
            catch { }

            return new { valid = true, message = "GitHub token format accepted." };
        }

        return new { valid = true, message = $"{serverName} key format accepted." };
    }
}
