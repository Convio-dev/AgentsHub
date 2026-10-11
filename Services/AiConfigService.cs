using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HermesHub.Services;

public class AiProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public List<string> Models { get; set; } = new();
    public bool IsEnabled { get; set; } = true;

    public override string ToString() => IsEnabled ? Name : $"{Name} (Disabled)";
}

public class DashboardShortcut
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "AI Dev Tool";
}

public class CliWorkspaceConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("isWebUi")]
    public bool IsWebUi { get; set; } = false;

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; } = false;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

public class AiConfig
{
    public Dictionary<string, string> ToolTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AiProvider> Providers { get; set; } = new();
    public string? SelectedProviderId { get; set; }
    public string? SelectedModel { get; set; }
    public List<DashboardShortcut> Shortcuts { get; set; } = new();
    public List<CliWorkspaceConfig> CliWorkspaces { get; set; } = new();
}

public class AiConfigService
{
    private static readonly Lazy<AiConfigService> _instance = new(() => new AiConfigService());
    public static AiConfigService Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly string _configFilePath;
    private AiConfig _config = new();

    public event Action? ConfigChanged;

    public AiConfig CurrentConfig
    {
        get
        {
            lock (_lock)
            {
                return _config;
            }
        }
    }

    public AiConfigService()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentsHub");
        var legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HermesHub");

        if (!Directory.Exists(baseDir) && Directory.Exists(legacyDir))
        {
            try
            {
                Directory.CreateDirectory(baseDir);
                var legacyFile = Path.Combine(legacyDir, "ai_config.json");
                var newFile = Path.Combine(baseDir, "ai_config.json");
                if (File.Exists(legacyFile) && !File.Exists(newFile))
                {
                    File.Copy(legacyFile, newFile, true);
                }
            }
            catch
            {
                // Fallback silently if migration fails
            }
        }
        else if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        _configFilePath = Path.Combine(baseDir, "ai_config.json");
        LoadConfig();
    }

    public void LoadConfig()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var loaded = JsonSerializer.Deserialize<AiConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (loaded != null)
                    {
                        _config = loaded;
                    }
                }
            }
            catch
            {
                _config = new AiConfig();
            }

            EnsureDefaults();
        }
    }

    public void SaveConfig(AiConfig newConfig)
    {
        lock (_lock)
        {
            _config = newConfig;
            try
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_configFilePath, json);
            }
            catch
            {
                // Ignore file save failures
            }
        }

        ConfigChanged?.Invoke();
    }

    public string GetToolTarget(string toolName, string defaultTarget)
    {
        lock (_lock)
        {
            if (_config.ToolTargets.TryGetValue(toolName, out var target) && !string.IsNullOrWhiteSpace(target))
            {
                return target;
            }
            return defaultTarget;
        }
    }

    public void SetToolTarget(string toolName, string target)
    {
        lock (_lock)
        {
            _config.ToolTargets[toolName] = target;
        }
        SaveConfig(_config);
    }

    public Dictionary<string, string> GetDefaultToolTargets()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FreeBuff"] = Path.Combine(localAppData, @"Programs\@codebufffreebuff-desktop\Freebuff.exe"),
            ["Cline"] = Path.Combine(localAppData, @"Cline\cline-app.exe"),
            ["DeepSeek"] = @"E:\DeepSeek Harness\DeepSeek Harness.exe",
            ["OpenCode"] = Path.Combine(localAppData, @"Programs\@opencodedesktop\OpenCode.exe"),
            ["Antigravity-ide"] = Path.Combine(localAppData, @"Programs\Antigravity IDE\Antigravity IDE.exe"),
            ["Claude"] = @"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude",
            ["ChatGPT"] = @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App"
        };
    }

    private void EnsureDefaults()
    {
        var defaults = GetDefaultToolTargets();
        foreach (var kvp in defaults)
        {
            if (!_config.ToolTargets.ContainsKey(kvp.Key) || string.IsNullOrWhiteSpace(_config.ToolTargets[kvp.Key]))
            {
                _config.ToolTargets[kvp.Key] = kvp.Value;
            }
        }

        if (_config.Providers.Count == 0)
        {
            _config.Providers.Add(new AiProvider
            {
                Id = "openrouter",
                Name = "OpenRouter",
                BaseUrl = "https://openrouter.ai/api/v1",
                ApiKey = string.Empty,
                Models = new List<string>
                {
                    "anthropic/claude-3.5-sonnet",
                    "deepseek/deepseek-chat",
                    "openai/gpt-4o-mini",
                    "google/gemini-2.0-flash-exp:free"
                }
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "openai",
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                ApiKey = string.Empty,
                Models = new List<string>
                {
                    "gpt-4o",
                    "gpt-4o-mini",
                    "o3-mini"
                }
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "groq",
                Name = "Groq",
                BaseUrl = "https://api.groq.com/openai/v1",
                ApiKey = string.Empty,
                Models = new List<string>
                {
                    "llama-3.3-70b-versatile",
                    "deepseek-r1-distill-llama-70b"
                }
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "deepseek",
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com/v1",
                ApiKey = string.Empty,
                Models = new List<string>
                {
                    "deepseek-chat",
                    "deepseek-reasoner"
                }
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "ollama",
                Name = "Local Ollama",
                BaseUrl = "http://localhost:11434/v1",
                ApiKey = string.Empty,
                Models = new List<string>
                {
                    "llama3.2",
                    "qwen2.5-coder"
                }
            });
        }

        if (_config.Shortcuts == null || _config.Shortcuts.Count == 0)
        {
            _config.Shortcuts = new List<DashboardShortcut>
            {
                new() { Name = "Antigravity IDE", Target = _config.ToolTargets.GetValueOrDefault("Antigravity-ide", defaults.GetValueOrDefault("Antigravity-ide", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "Claude Desktop", Target = _config.ToolTargets.GetValueOrDefault("Claude", defaults.GetValueOrDefault("Claude", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "ChatGPT Desktop", Target = _config.ToolTargets.GetValueOrDefault("ChatGPT", defaults.GetValueOrDefault("ChatGPT", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "OpenCode Desktop", Target = _config.ToolTargets.GetValueOrDefault("OpenCode", defaults.GetValueOrDefault("OpenCode", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "Cline", Target = _config.ToolTargets.GetValueOrDefault("Cline", defaults.GetValueOrDefault("Cline", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "DeepSeek", Target = _config.ToolTargets.GetValueOrDefault("DeepSeek", defaults.GetValueOrDefault("DeepSeek", string.Empty)), Category = "AI Dev Tool" }
            };
        }
        else
        {
            // Repair any shortcuts that have missing or empty target
            foreach (var sc in _config.Shortcuts)
            {
                if (string.IsNullOrWhiteSpace(sc.Target))
                {
                    if (_config.ToolTargets.TryGetValue(sc.Name, out var exactMatch) && !string.IsNullOrWhiteSpace(exactMatch))
                    {
                        sc.Target = exactMatch;
                    }
                    else if (sc.Name.Contains("Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        sc.Target = _config.ToolTargets.GetValueOrDefault("Antigravity-ide", defaults.GetValueOrDefault("Antigravity-ide", string.Empty));
                    }
                    else if (sc.Name.Contains("Claude", StringComparison.OrdinalIgnoreCase))
                    {
                        sc.Target = _config.ToolTargets.GetValueOrDefault("Claude", defaults.GetValueOrDefault("Claude", string.Empty));
                    }
                    else if (sc.Name.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
                    {
                        sc.Target = _config.ToolTargets.GetValueOrDefault("ChatGPT", defaults.GetValueOrDefault("ChatGPT", string.Empty));
                    }
                    else if (sc.Name.Contains("OpenCode", StringComparison.OrdinalIgnoreCase))
                    {
                        sc.Target = _config.ToolTargets.GetValueOrDefault("OpenCode", defaults.GetValueOrDefault("OpenCode", string.Empty));
                    }
                    else if (sc.Name.Contains("Cline", StringComparison.OrdinalIgnoreCase))
                    {
                        sc.Target = _config.ToolTargets.GetValueOrDefault("Cline", defaults.GetValueOrDefault("Cline", string.Empty));
                    }
                    else if (sc.Name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase))
                    {
                        sc.Target = _config.ToolTargets.GetValueOrDefault("DeepSeek", defaults.GetValueOrDefault("DeepSeek", string.Empty));
                    }
                }
            }
        }

        if (_config.CliWorkspaces == null || _config.CliWorkspaces.Count == 0)
        {
            _config.CliWorkspaces = new List<CliWorkspaceConfig>();
        }

        // Migrate legacy IDs if present in loaded config
        foreach (var ws in _config.CliWorkspaces)
        {
            if (string.Equals(ws.Id, "AntigravityCli", StringComparison.OrdinalIgnoreCase)) ws.Id = "antigravity_cli";
            else if (string.Equals(ws.Id, "ClaudeCode", StringComparison.OrdinalIgnoreCase)) ws.Id = "claude_code";
            else if (string.Equals(ws.Id, "Hermes", StringComparison.OrdinalIgnoreCase)) ws.Id = "hermes";
        }

        void EnsureDefaultCli(string id, string name, string cmd, int port, bool isWebUi)
        {
            var existing = _config.CliWorkspaces.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _config.CliWorkspaces.Add(new CliWorkspaceConfig
                {
                    Id = id,
                    Name = name,
                    Command = cmd,
                    Port = port,
                    IsWebUi = isWebUi,
                    IsDefault = true,
                    IsEnabled = true
                });
            }
            else
            {
                existing.Id = id;
                existing.IsDefault = true;
                if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = name;
                if (string.IsNullOrWhiteSpace(existing.Command)) existing.Command = cmd;
                if (existing.Port == null || existing.Port == 0) existing.Port = port;
                existing.IsWebUi = isWebUi;
            }
        }

        EnsureDefaultCli("hermes", "Hermes", "hermes dashboard", 9119, true);
        EnsureDefaultCli("claude_code", "Claude Code", "claude", 9229, false);
        EnsureDefaultCli("antigravity_cli", "Antigravity CLI", "agy", 9339, false);

        // Remove any duplicates after migration
        var uniqueWorkspaces = new List<CliWorkspaceConfig>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ws in _config.CliWorkspaces)
        {
            if (seenIds.Add(ws.Id))
            {
                uniqueWorkspaces.Add(ws);
            }
        }
        _config.CliWorkspaces = uniqueWorkspaces;

        SaveConfig(_config);
    }

    public static string NormalizeCliId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "hermes";
        var clean = id.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
        return clean switch
        {
            "hermes" => "hermes",
            "claude" or "claudecode" => "claude_code",
            "antigravity" or "antigravitycli" => "antigravity_cli",
            _ => id.Trim()
        };
    }

    public List<CliWorkspaceConfig> GetCliWorkspaces()
    {
        lock (_lock)
        {
            return [.. _config.CliWorkspaces];
        }
    }

    public void SaveCliWorkspace(CliWorkspaceConfig item)
    {
        lock (_lock)
        {
            var idx = _config.CliWorkspaces.FindIndex(c => string.Equals(c.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                item.IsDefault = _config.CliWorkspaces[idx].IsDefault;
                _config.CliWorkspaces[idx] = item;
            }
            else
            {
                item.IsDefault = false;
                _config.CliWorkspaces.Add(item);
            }
        }
        SaveConfig(_config);
    }

    public bool DeleteCliWorkspace(string id)
    {
        lock (_lock)
        {
            var item = _config.CliWorkspaces.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item == null || item.IsDefault)
            {
                return false;
            }
            _config.CliWorkspaces.Remove(item);
        }
        SaveConfig(_config);
        return true;
    }

    public void ToggleCliWorkspace(string id, bool isEnabled)
    {
        lock (_lock)
        {
            var item = _config.CliWorkspaces.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.IsEnabled = isEnabled;
            }
        }
        SaveConfig(_config);
    }
}
