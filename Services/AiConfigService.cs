using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HermesHub.Helpers;

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

public class DevToolItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

public class ClaudeGatewayProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool IsPreset { get; set; } = false;
}

public class PromptEnhancerPreset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("systemPrompt")]
    public string SystemPrompt { get; set; } = "";

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; } = false;
}

public class AiConfig
{
    [JsonPropertyName("devTools")]
    public List<DevToolItem> DevTools { get; set; } = new();

    [JsonPropertyName("toolTargets")]
    public Dictionary<string, string> ToolTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<AiProvider> Providers { get; set; } = new();
    public string? SelectedProviderId { get; set; }
    public string? SelectedModel { get; set; }
    public List<DashboardShortcut> Shortcuts { get; set; } = new();
    public List<CliWorkspaceConfig> CliWorkspaces { get; set; } = new();

    public string ClaudeEngineMode { get; set; } = "OpenRouter";
    public string ClaudeSelectedModel { get; set; } = "qwen/qwen-2.5-coder-32b-instruct:free";
    public string ClaudeCustomGatewayUrl { get; set; } = "https://openrouter.ai/api/v1";

    public bool ClaudeInjectionEnabled { get; set; } = true;
    public string ClaudeSelectedGatewayId { get; set; } = "preset_local_proxy";
    public List<ClaudeGatewayProvider> ClaudeGatewayProviders { get; set; } = new();

    [JsonPropertyName("promptEnhancerTemperature")]
    public double PromptEnhancerTemperature { get; set; } = 0.6;

    [JsonPropertyName("activePromptPresetId")]
    public string ActivePromptPresetId { get; set; } = "preset_general";

    [JsonPropertyName("promptPresets")]
    public List<PromptEnhancerPreset> PromptPresets { get; set; } = new();
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
            EnsurePromptPresets(newConfig);
            SyncToolTargets(newConfig);
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
            if (_config.DevTools != null)
            {
                var match = _config.DevTools.FirstOrDefault(d =>
                    string.Equals(d.Id, toolName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase) ||
                    (toolName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("Antigravity", StringComparison.OrdinalIgnoreCase)) ||
                    (toolName.Contains("FreeBuff", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("FreeBuff", StringComparison.OrdinalIgnoreCase)) ||
                    (toolName.Contains("Cline", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("Cline", StringComparison.OrdinalIgnoreCase)) ||
                    (toolName.Contains("OpenCode", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("OpenCode", StringComparison.OrdinalIgnoreCase)) ||
                    (toolName.Contains("Claude", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("Claude", StringComparison.OrdinalIgnoreCase)) ||
                    (toolName.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)));

                if (match != null && !string.IsNullOrWhiteSpace(match.Target))
                {
                    return match.Target;
                }
            }

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
            if (_config.DevTools != null)
            {
                var match = _config.DevTools.FirstOrDefault(d =>
                    string.Equals(d.Id, toolName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.Target = target;
                }
            }
        }
        SaveConfig(_config);
    }

    public List<DevToolItem> GetDefaultDevTools()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var freebuffPath = Path.Combine(localAppData, @"Programs\@codebufffreebuff-desktop\Freebuff.exe");
        var clinePath = Path.Combine(localAppData, @"Cline\cline-app.exe");
        var opencodePath = Path.Combine(localAppData, @"Programs\@opencodedesktop\OpenCode.exe");
        var antigravityPath = Path.Combine(localAppData, @"Programs\Antigravity IDE\Antigravity IDE.exe");

        return new List<DevToolItem>
        {
            new()
            {
                Id = "freebuff",
                Name = "FreeBuff Desktop",
                Description = "Main executable for FreeBuff Desktop",
                Target = File.Exists(freebuffPath) ? freebuffPath : string.Empty,
                IsEnabled = true
            },
            new()
            {
                Id = "cline",
                Name = "Cline App",
                Description = "Executable path for Cline App",
                Target = File.Exists(clinePath) ? clinePath : string.Empty,
                IsEnabled = true
            },
            new()
            {
                Id = "opencode",
                Name = "OpenCode Desktop",
                Description = "OpenCode desktop client executable",
                Target = File.Exists(opencodePath) ? opencodePath : string.Empty,
                IsEnabled = true
            },
            new()
            {
                Id = "antigravity_ide",
                Name = "Antigravity IDE",
                Description = "Path to Antigravity IDE.exe",
                Target = File.Exists(antigravityPath) ? antigravityPath : string.Empty,
                IsEnabled = true
            },
            new()
            {
                Id = "claude",
                Name = "Claude",
                Description = "Claude Desktop application",
                Target = @"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude",
                IsEnabled = true
            },
            new()
            {
                Id = "chatgpt",
                Name = "ChatGPT",
                Description = "ChatGPT Desktop application",
                Target = @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App",
                IsEnabled = true
            }
        };
    }

    public Dictionary<string, string> GetDefaultToolTargets()
    {
        var defaults = GetDefaultDevTools();
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in defaults)
        {
            dict[item.Name] = item.Target;
            dict[item.Id] = item.Target;
        }

        // Common aliases for backwards compatibility
        dict["FreeBuff"] = dict.GetValueOrDefault("freebuff", string.Empty);
        dict["Cline"] = dict.GetValueOrDefault("cline", string.Empty);
        dict["OpenCode"] = dict.GetValueOrDefault("opencode", string.Empty);
        dict["Antigravity-ide"] = dict.GetValueOrDefault("antigravity_ide", string.Empty);
        dict["Claude"] = @"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude";
        dict["ChatGPT"] = @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App";

        return dict;
    }

    public static List<PromptEnhancerPreset> BuildDefaultPromptPresets()
    {
        return new List<PromptEnhancerPreset>
        {
            new()
            {
                Id = "preset_general",
                Name = "General & Balanced",
                IsBuiltIn = true,
                SystemPrompt = "You are an elite prompt engineer. Analyze the user's raw prompt and rewrite it to be clear, effective, and structured so advanced AI models produce their best response. Flesh out context, clarify objectives, define constraints, and specify format. Return ONLY the enhanced prompt without preamble or conversational remarks."
            },
            new()
            {
                Id = "preset_code",
                Name = "Code & Architecture",
                IsBuiltIn = true,
                SystemPrompt = "You are a principal software engineer and expert prompt craftsman. Rewrite the user's prompt to be highly rigorous, specifying programming language, modular architecture, interfaces, type safety, error handling, performance considerations, edge cases, and concrete deliverables. Return ONLY the enhanced prompt without meta-commentary."
            },
            new()
            {
                Id = "preset_concise",
                Name = "Direct & Concise",
                IsBuiltIn = true,
                SystemPrompt = "You are an expert prompt engineer. Refine the user's prompt into an unambiguous, concise, and focused set of instructions. Strip redundant words while making instructions and output formats 100% explicit. Return ONLY the enhanced prompt without meta-commentary."
            },
            new()
            {
                Id = "preset_structured",
                Name = "Comprehensive & Structured",
                IsBuiltIn = true,
                SystemPrompt = "You are an expert prompt engineer. Transform the user's prompt into a complete, structured master prompt with clear sections: Role, Context, Primary Objective, Detailed Instructions, Constraints, and Expected Output Format. Return ONLY the enhanced prompt without meta-commentary."
            }
        };
    }

    /// <summary>
    /// Guarantees the preset list is usable: seeds built-ins when missing, restores any
    /// built-in that was dropped, and keeps the active preset pointing at something real.
    /// User edits to a built-in's text are preserved (only blank text is refilled).
    /// </summary>
    public static void EnsurePromptPresets(AiConfig? config)
    {
        if (config == null) return;

        if (config.PromptPresets == null || config.PromptPresets.Count == 0)
        {
            config.PromptPresets = BuildDefaultPromptPresets();
        }
        else
        {
            foreach (var builtIn in BuildDefaultPromptPresets())
            {
                var existing = config.PromptPresets.FirstOrDefault(p =>
                    string.Equals(p.Id, builtIn.Id, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    config.PromptPresets.Insert(0, builtIn);
                }
                else
                {
                    existing.IsBuiltIn = true;
                    if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = builtIn.Name;
                    if (string.IsNullOrWhiteSpace(existing.SystemPrompt)) existing.SystemPrompt = builtIn.SystemPrompt;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(config.ActivePromptPresetId) ||
            !config.PromptPresets.Any(p => string.Equals(p.Id, config.ActivePromptPresetId, StringComparison.OrdinalIgnoreCase)))
        {
            config.ActivePromptPresetId = "preset_general";
        }

        if (double.IsNaN(config.PromptEnhancerTemperature) ||
            config.PromptEnhancerTemperature < 0.0 ||
            config.PromptEnhancerTemperature > 1.5)
        {
            config.PromptEnhancerTemperature = 0.6;
        }
    }

    public void SyncToolTargets(AiConfig config)    {
        if (config == null) return;
        if (config.ToolTargets == null)
        {
            config.ToolTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (config.DevTools != null)
        {
            foreach (var tool in config.DevTools)
            {
                if (!string.IsNullOrWhiteSpace(tool.Name))
                {
                    config.ToolTargets[tool.Name] = tool.Target;
                }
                if (!string.IsNullOrWhiteSpace(tool.Id))
                {
                    config.ToolTargets[tool.Id] = tool.Target;
                }
                if (tool.Name.Contains("Antigravity", StringComparison.OrdinalIgnoreCase))
                {
                    config.ToolTargets["Antigravity-ide"] = tool.Target;
                }
                if (tool.Name.Contains("FreeBuff", StringComparison.OrdinalIgnoreCase))
                {
                    config.ToolTargets["FreeBuff"] = tool.Target;
                }
                if (tool.Name.Contains("OpenCode", StringComparison.OrdinalIgnoreCase))
                {
                    config.ToolTargets["OpenCode"] = tool.Target;
                }
                if (tool.Name.Contains("Cline", StringComparison.OrdinalIgnoreCase))
                {
                    config.ToolTargets["Cline"] = tool.Target;
                }
            }
        }
    }

    private static bool IsCleanPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Contains("DeepSeek Harness", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    private void EnsureDefaults()
    {
        var defaultDevTools = GetDefaultDevTools();
        var defaults = GetDefaultToolTargets();

        // 1. Sanitize legacy ToolTargets from hardcoded user paths
        var keysToRemove = new List<string>();
        foreach (var kvp in _config.ToolTargets)
        {
            if (!IsCleanPath(kvp.Value) || string.Equals(kvp.Key, "DeepSeek", StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var k in keysToRemove)
        {
            _config.ToolTargets.Remove(k);
        }

        // 2. Ensure DevTools collection
        if (_config.DevTools == null)
        {
            _config.DevTools = new List<DevToolItem>();
        }

        if (_config.DevTools.Count == 0)
        {
            _config.DevTools = defaultDevTools;
            // Backwards compatibility migration from existing ToolTargets
            foreach (var tool in _config.DevTools)
            {
                if (_config.ToolTargets.TryGetValue(tool.Name, out var target) && !string.IsNullOrWhiteSpace(target) && IsCleanPath(target))
                {
                    tool.Target = target;
                }
                else if (_config.ToolTargets.TryGetValue(tool.Id, out var targetId) && !string.IsNullOrWhiteSpace(targetId) && IsCleanPath(targetId))
                {
                    tool.Target = targetId;
                }
            }
        }
        else
        {
            // Sanitize existing items
            foreach (var tool in _config.DevTools)
            {
                if (!IsCleanPath(tool.Target))
                {
                    tool.Target = string.Empty;
                }
            }
        }

        // 3. Keep ToolTargets synchronized
        SyncToolTargets(_config);

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
                new() { Name = "FreeBuff Desktop", Target = _config.ToolTargets.GetValueOrDefault("FreeBuff", defaults.GetValueOrDefault("FreeBuff", string.Empty)), Category = "AI Dev Tool" }
            };
        }
        else
        {
            // Repair any shortcuts that have missing or empty or tainted target
            foreach (var sc in _config.Shortcuts)
            {
                if (!IsCleanPath(sc.Target))
                {
                    sc.Target = string.Empty;
                }

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
                    else if (sc.Name.Contains("FreeBuff", StringComparison.OrdinalIgnoreCase))
                    {
                        sc.Target = _config.ToolTargets.GetValueOrDefault("FreeBuff", defaults.GetValueOrDefault("FreeBuff", string.Empty));
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

        if (string.IsNullOrWhiteSpace(_config.ClaudeEngineMode))
        {
            _config.ClaudeEngineMode = "OpenRouter";
        }
        if (string.IsNullOrWhiteSpace(_config.ClaudeSelectedModel))
        {
            _config.ClaudeSelectedModel = "qwen/qwen-2.5-coder-32b-instruct:free";
        }
        if (string.IsNullOrWhiteSpace(_config.ClaudeCustomGatewayUrl))
        {
            _config.ClaudeCustomGatewayUrl = "https://openrouter.ai/api/v1";
        }

        if (_config.ClaudeGatewayProviders == null)
        {
            _config.ClaudeGatewayProviders = new List<ClaudeGatewayProvider>();
        }

        var existingKey = GetOpenRouterApiKey();

        var localProxyPreset = _config.ClaudeGatewayProviders.FirstOrDefault(p => string.Equals(p.Id, "preset_local_proxy", StringComparison.OrdinalIgnoreCase));
        if (localProxyPreset == null)
        {
            _config.ClaudeGatewayProviders.Insert(0, new ClaudeGatewayProvider
            {
                Id = "preset_local_proxy",
                Name = "Local Proxy (127.0.0.1:9230)",
                BaseUrl = "http://127.0.0.1:9230",
                ApiKey = existingKey,
                IsPreset = true
            });
        }
        else
        {
            localProxyPreset.IsPreset = true;
            if (string.IsNullOrWhiteSpace(localProxyPreset.BaseUrl)) localProxyPreset.BaseUrl = "http://127.0.0.1:9230";
            if (string.IsNullOrWhiteSpace(localProxyPreset.ApiKey) && !string.IsNullOrWhiteSpace(existingKey)) localProxyPreset.ApiKey = existingKey;
        }

        var openRouterPreset = _config.ClaudeGatewayProviders.FirstOrDefault(p => string.Equals(p.Id, "preset_openrouter", StringComparison.OrdinalIgnoreCase));
        if (openRouterPreset == null)
        {
            _config.ClaudeGatewayProviders.Add(new ClaudeGatewayProvider
            {
                Id = "preset_openrouter",
                Name = "OpenRouter Direct",
                BaseUrl = "https://openrouter.ai/api/v1",
                ApiKey = existingKey,
                IsPreset = true
            });
        }
        else
        {
            openRouterPreset.IsPreset = true;
            if (string.IsNullOrWhiteSpace(openRouterPreset.BaseUrl)) openRouterPreset.BaseUrl = "https://openrouter.ai/api/v1";
            if (string.IsNullOrWhiteSpace(openRouterPreset.ApiKey) && !string.IsNullOrWhiteSpace(existingKey)) openRouterPreset.ApiKey = existingKey;
        }

        if (string.IsNullOrWhiteSpace(_config.ClaudeSelectedGatewayId))
        {
            _config.ClaudeSelectedGatewayId = "preset_local_proxy";
        }

        SaveConfig(_config);
    }

    public static string NormalizeCliId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "hermes";
        id = id.Trim();
        if (id.StartsWith("antigravity_cli:", StringComparison.OrdinalIgnoreCase))
        {
            return $"antigravity_cli:{id.Substring("antigravity_cli:".Length).Trim()}";
        }
        var clean = id.ToLowerInvariant().Replace("_", "").Replace("-", "");
        return clean switch
        {
            "hermes" => "hermes",
            "claude" or "claudecode" => "claude_code",
            "antigravity" or "antigravitycli" => "antigravity_cli",
            _ => id
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

    public string GetClaudeEngineMode()
    {
        lock (_lock)
        {
            return string.IsNullOrWhiteSpace(_config.ClaudeEngineMode) ? "OpenRouter" : _config.ClaudeEngineMode;
        }
    }

    public void SetClaudeEngineMode(string mode)
    {
        lock (_lock)
        {
            _config.ClaudeEngineMode = mode;
            SaveConfig(_config);
        }
    }

    public string GetClaudeSelectedModel()
    {
        lock (_lock)
        {
            return string.IsNullOrWhiteSpace(_config.ClaudeSelectedModel) ? "qwen/qwen-2.5-coder-32b-instruct:free" : _config.ClaudeSelectedModel;
        }
    }

    public void SetClaudeSelectedModel(string model)
    {
        lock (_lock)
        {
            _config.ClaudeSelectedModel = model;
            SaveConfig(_config);
        }
    }

    public string GetClaudeCustomGatewayUrl()
    {
        lock (_lock)
        {
            return string.IsNullOrWhiteSpace(_config.ClaudeCustomGatewayUrl) ? "https://openrouter.ai/api/v1" : _config.ClaudeCustomGatewayUrl;
        }
    }

    public void SetClaudeCustomGatewayUrl(string url)
    {
        lock (_lock)
        {
            _config.ClaudeCustomGatewayUrl = url;
            SaveConfig(_config);
        }
    }

    public string GetOpenRouterApiKey()
    {
        lock (_lock)
        {
            var provider = _config.Providers.FirstOrDefault(p => string.Equals(p.Id, "openrouter", StringComparison.OrdinalIgnoreCase));
            if (provider != null && !string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                return provider.ApiKey.Trim();
            }
            return StartupHelper.GetStringPreference("ClaudeGatewayApiKey", string.Empty).Trim();
        }
    }

    public void SetClaudeGatewayApiKey(string apiKey)
    {
        lock (_lock)
        {
            var provider = _config.Providers.FirstOrDefault(p => string.Equals(p.Id, "openrouter", StringComparison.OrdinalIgnoreCase));
            if (provider != null)
            {
                provider.ApiKey = apiKey.Trim();
            }
            StartupHelper.SetStringPreference("ClaudeGatewayApiKey", apiKey.Trim());

            // Also synchronize active preset's API key
            var activeProv = GetActiveClaudeGatewayProvider();
            if (activeProv != null)
            {
                activeProv.ApiKey = apiKey.Trim();
            }
            SaveConfig(_config);
        }
    }

    public bool GetClaudeInjectionEnabled()
    {
        lock (_lock)
        {
            return _config.ClaudeInjectionEnabled;
        }
    }

    public void SetClaudeInjectionEnabled(bool enabled)
    {
        lock (_lock)
        {
            _config.ClaudeInjectionEnabled = enabled;
            SaveConfig(_config);
        }
    }

    public string GetClaudeSelectedGatewayId()
    {
        lock (_lock)
        {
            return string.IsNullOrWhiteSpace(_config.ClaudeSelectedGatewayId) ? "preset_local_proxy" : _config.ClaudeSelectedGatewayId;
        }
    }

    public void SetClaudeSelectedGatewayId(string id)
    {
        lock (_lock)
        {
            _config.ClaudeSelectedGatewayId = string.IsNullOrWhiteSpace(id) ? "preset_local_proxy" : id.Trim();
            SaveConfig(_config);
        }
    }

    public List<ClaudeGatewayProvider> GetClaudeGatewayProviders()
    {
        lock (_lock)
        {
            if (_config.ClaudeGatewayProviders == null || _config.ClaudeGatewayProviders.Count == 0)
            {
                EnsureDefaults();
            }
            var list = _config.ClaudeGatewayProviders ?? new List<ClaudeGatewayProvider>();
            return list.Select(p => new ClaudeGatewayProvider
            {
                Id = p.Id,
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                ApiKey = p.ApiKey,
                IsPreset = p.IsPreset
            }).ToList();
        }
    }

    public ClaudeGatewayProvider GetActiveClaudeGatewayProvider()
    {
        lock (_lock)
        {
            if (_config.ClaudeGatewayProviders == null || _config.ClaudeGatewayProviders.Count == 0)
            {
                EnsureDefaults();
            }

            var providers = _config.ClaudeGatewayProviders ?? new List<ClaudeGatewayProvider>();
            var selectedId = GetClaudeSelectedGatewayId();
            var found = providers.FirstOrDefault(p => string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return found;
            }

            var localPreset = providers.FirstOrDefault(p => string.Equals(p.Id, "preset_local_proxy", StringComparison.OrdinalIgnoreCase));
            if (localPreset != null)
            {
                return localPreset;
            }

            return providers.FirstOrDefault() ?? new ClaudeGatewayProvider
            {
                Id = "preset_local_proxy",
                Name = "Local Proxy (127.0.0.1:9230)",
                BaseUrl = "http://127.0.0.1:9230",
                IsPreset = true
            };
        }
    }

    public void SaveClaudeGatewayProvider(ClaudeGatewayProvider provider)
    {
        lock (_lock)
        {
            if (_config.ClaudeGatewayProviders == null)
            {
                _config.ClaudeGatewayProviders = new List<ClaudeGatewayProvider>();
            }

            var idx = _config.ClaudeGatewayProviders.FindIndex(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var existing = _config.ClaudeGatewayProviders[idx];
                existing.Name = provider.Name;
                existing.BaseUrl = provider.BaseUrl;
                existing.ApiKey = provider.ApiKey;
            }
            else
            {
                _config.ClaudeGatewayProviders.Add(provider);
            }

            SaveConfig(_config);
        }
    }

    public bool DeleteClaudeGatewayProvider(string id)
    {
        lock (_lock)
        {
            if (_config.ClaudeGatewayProviders == null) return false;

            var item = _config.ClaudeGatewayProviders.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item == null || item.IsPreset)
            {
                return false;
            }

            _config.ClaudeGatewayProviders.Remove(item);
            if (string.Equals(_config.ClaudeSelectedGatewayId, id, StringComparison.OrdinalIgnoreCase))
            {
                _config.ClaudeSelectedGatewayId = "preset_local_proxy";
            }

            SaveConfig(_config);
            return true;
        }
    }

    public static readonly string[] DefaultFallbackFreeModels =
    [
        "qwen/qwen-2.5-coder-32b-instruct:free",
        "deepseek/deepseek-r1:free",
        "meta-llama/llama-3.3-70b-instruct:free",
        "google/gemini-2.0-flash-exp:free",
        "google/gemini-2.0-flash-thinking-exp:free",
        "minimax/minimax-01:free",
        "mistralai/mistral-small-24b-instruct-2501:free",
        "nousresearch/hermes-3-llama-3.1-405b:free",
        "microsoft/phi-3-medium-128k-instruct:free",
        "huggingfaceh4/zephyr-7b-beta:free"
    ];

    private readonly List<string> _cachedFreeModels = new();
    private DateTime _lastFreeModelsFetch = DateTime.MinValue;

    public IReadOnlyList<string> GetCachedOpenRouterFreeModels()
    {
        lock (_lock)
        {
            if (_cachedFreeModels.Count > 0)
            {
                return _cachedFreeModels.ToList();
            }
            return DefaultFallbackFreeModels;
        }
    }

    public async Task<List<string>> FetchOpenRouterFreeModelsAsync(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && _cachedFreeModels.Count > 0 && (DateTime.UtcNow - _lastFreeModelsFetch).TotalHours < 2)
            {
                return _cachedFreeModels.ToList();
            }
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetStringAsync("https://openrouter.ai/api/v1/models");
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in dataEl.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.GetString()?.Trim();
                        if (string.IsNullOrWhiteSpace(id)) continue;

                        bool isFree = id.EndsWith(":free", StringComparison.OrdinalIgnoreCase);

                        if (!isFree && item.TryGetProperty("pricing", out var pricingEl))
                        {
                            var prompt = pricingEl.TryGetProperty("prompt", out var pEl) ? pEl.GetString() : null;
                            var completion = pricingEl.TryGetProperty("completion", out var cEl) ? cEl.GetString() : null;
                            if ((prompt == "0" || prompt == "0.0") && (completion == "0" || completion == "0.0"))
                            {
                                isFree = true;
                            }
                        }

                        if (isFree)
                        {
                            discovered.Add(id);
                        }
                    }
                }

                if (discovered.Count > 0)
                {
                    var sorted = discovered.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
                    lock (_lock)
                    {
                        _cachedFreeModels.Clear();
                        _cachedFreeModels.AddRange(sorted);
                        _lastFreeModelsFetch = DateTime.UtcNow;
                    }
                    return sorted;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiConfigService] Error fetching OpenRouter models: {ex.Message}");
        }

        lock (_lock)
        {
            if (_cachedFreeModels.Count == 0)
            {
                _cachedFreeModels.AddRange(DefaultFallbackFreeModels);
            }
            return _cachedFreeModels.ToList();
        }
    }
}
