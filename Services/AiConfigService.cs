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
    public List<string> Models { get; set; } = [];
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

    /// <summary>
    /// Command-line arguments appended to the target. The shortcut model has always carried
    /// arguments; dev tools did not, which meant a tool that needs a flag could not be launched.
    /// </summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";

    /// <summary>Directory to start the executable in. Empty means "inherited from the app".</summary>
    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

public class ClaudeGatewayProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public List<string> ApiKeys { get; set; } = [];
    public bool IsPreset { get; set; } = false;

    public List<string> GetEffectiveApiKeys()
    {
        var list = new List<string>();
        if (ApiKeys != null && ApiKeys.Count > 0)
        {
            foreach (var k in ApiKeys)
            {
                if (!string.IsNullOrWhiteSpace(k) && !list.Contains(k.Trim()))
                {
                    list.Add(k.Trim());
                }
            }
        }
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(ApiKey))
        {
            list.Add(ApiKey.Trim());
        }
        return list;
    }
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
    public List<DevToolItem> DevTools { get; set; } = [];

    [JsonPropertyName("toolTargets")]
    public Dictionary<string, string> ToolTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<AiProvider> Providers { get; set; } = [];
    public string? SelectedProviderId { get; set; }
    public string? SelectedModel { get; set; }
    public List<DashboardShortcut> Shortcuts { get; set; } = [];
    public List<CliWorkspaceConfig> CliWorkspaces { get; set; } = [];

    public string ClaudeEngineMode { get; set; } = "OpenRouter";
    public string ClaudeSelectedModel { get; set; } = "qwen/qwen-2.5-coder-32b-instruct:free";
    public string ClaudeCustomGatewayUrl { get; set; } = "https://openrouter.ai/api/v1";

    public bool ClaudeInjectionEnabled { get; set; } = true;
    public string ClaudeSelectedGatewayId { get; set; } = "preset_local_proxy";
    public List<ClaudeGatewayProvider> ClaudeGatewayProviders { get; set; } = [];

    [JsonPropertyName("promptEnhancerTemperature")]
    public double PromptEnhancerTemperature { get; set; } = 0.6;

    [JsonPropertyName("activePromptPresetId")]
    public string ActivePromptPresetId { get; set; } = "preset_general";

    [JsonPropertyName("promptPresets")]
    public List<PromptEnhancerPreset> PromptPresets { get; set; } = [];

    /// <summary>
    /// Last workspace preset the user applied. One of "Preset_Clean", "Preset_Duo", "Preset_All".
    /// </summary>
    [JsonPropertyName("terminalWorkspacePreset")]
    public string TerminalWorkspacePreset { get; set; } = "Preset_Duo";
}

public class AiConfigService
{
    private static readonly Lazy<AiConfigService> _instance = new(() => new AiConfigService());
    public static AiConfigService Instance => _instance.Value;

    private readonly Lock _lock = new();
    private readonly string _configFilePath;
    private AiConfig _config = new();

    // Cached so config load/save does not allocate a new options instance on every call.
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

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
                    var loaded = JsonSerializer.Deserialize<AiConfig>(json, CaseInsensitiveJsonOptions);

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

            // A payload that never carried the preset label must not reset it.
            if (string.IsNullOrWhiteSpace(newConfig.TerminalWorkspacePreset))
            {
                newConfig.TerminalWorkspacePreset = _config.TerminalWorkspacePreset;
            }

            SyncToolTargets(newConfig);
            _config = newConfig;
            try
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_config, IndentedJsonOptions);
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
                if (match != null) match.Target = target;
            }
        }
        SaveConfig(_config);
    }

    public static string FindAntigravityAppPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var candidates = new[]
        {
            Path.Combine(localAppData, @"Programs\antigravity\Antigravity.exe"),
            Path.Combine(localAppData, @"Programs\Antigravity\Antigravity.exe"),
            Path.Combine(localAppData, @"antigravity\Antigravity.exe"),
            Path.Combine(localAppData, @"Antigravity\Antigravity.exe"),
            Path.Combine(programFiles, @"antigravity\Antigravity.exe"),
            Path.Combine(programFiles, @"Antigravity\Antigravity.exe"),
            Path.Combine(programFilesX86, @"antigravity\Antigravity.exe"),
            Path.Combine(programFilesX86, @"Antigravity\Antigravity.exe"),
            Path.Combine(appData, @"Programs\antigravity\Antigravity.exe"),
            Path.Combine(localAppData, @"Programs\antigravity-app\Antigravity.exe")
        };

        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c) && File.Exists(c))
            {
                return c;
            }
        }

        return string.Empty;
    }

    public static List<DevToolItem> GetDefaultDevTools()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var freebuffPath = Path.Combine(localAppData, @"Programs\@codebufffreebuff-desktop\Freebuff.exe");
        var clinePath = Path.Combine(localAppData, @"Cline\cline-app.exe");
        var opencodePath = Path.Combine(localAppData, @"Programs\@opencodedesktop\OpenCode.exe");
        var antigravityPath = Path.Combine(localAppData, @"Programs\Antigravity IDE\Antigravity IDE.exe");
        var antigravityAppPath = FindAntigravityAppPath();

        return
        [
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
                Id = "antigravity_app",
                Name = "Antigravity App",
                Description = "Path to Antigravity.exe",
                Target = !string.IsNullOrWhiteSpace(antigravityAppPath) ? antigravityAppPath : string.Empty,
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
        ];
    }

    public static Dictionary<string, string> GetDefaultToolTargets()
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
        dict["Antigravity App"] = dict.GetValueOrDefault("antigravity_app", string.Empty);
        dict["antigravity_app"] = dict.GetValueOrDefault("antigravity_app", string.Empty);
        dict["Antigravity"] = dict.GetValueOrDefault("antigravity_app", string.Empty);
        dict["Claude"] = @"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude";
        dict["ChatGPT"] = @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App";

        return dict;
    }

    public static List<PromptEnhancerPreset> BuildDefaultPromptPresets()
    {
        return
        [
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
        ];
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

    public static void SyncToolTargets(AiConfig config)    {
        if (config == null) return;
        config.ToolTargets ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        _config.DevTools ??= [];

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
            // Ensure any default dev tool missing from existing user config is added
            foreach (var def in defaultDevTools)
            {
                if (!_config.DevTools.Any(d => string.Equals(d.Id, def.Id, StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(d.Name, def.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _config.DevTools.Add(new DevToolItem
                    {
                        Id = def.Id,
                        Name = def.Name,
                        Description = def.Description,
                        Target = def.Target,
                        IsEnabled = def.IsEnabled
                    });
                }
            }

            // Sanitize existing items and auto-detect empty paths for known tools
            foreach (var tool in _config.DevTools)
            {
                if (!IsCleanPath(tool.Target))
                {
                    tool.Target = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(tool.Target))
                {
                    if (string.Equals(tool.Id, "antigravity_app", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tool.Name, "Antigravity App", StringComparison.OrdinalIgnoreCase))
                    {
                        var detected = FindAntigravityAppPath();
                        if (!string.IsNullOrWhiteSpace(detected))
                        {
                            tool.Target = detected;
                        }
                    }
                    else if (string.Equals(tool.Id, "antigravity_ide", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(tool.Name, "Antigravity IDE", StringComparison.OrdinalIgnoreCase))
                    {
                        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var idePath = Path.Combine(localApp, @"Programs\Antigravity IDE\Antigravity IDE.exe");
                        if (File.Exists(idePath)) tool.Target = idePath;
                    }
                }
            }
        }

        // 2b. Repair targets that cannot be launched as stored. A path pasted from Explorer's
        // "Copy as path" arrives wrapped in quotes, which Process.Start cannot resolve, so the
        // tool stops launching while still looking configured. Cleaning it here keeps it working
        // without making the user pick the file again. Runs before SyncToolTargets so the
        // lookup map receives the repaired value.
        foreach (var tool in _config.DevTools)
        {
            tool.Target = DevToolTargets.Normalize(tool.Target);
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
                Models =
                [
                    "anthropic/claude-3.5-sonnet",
                    "deepseek/deepseek-chat",
                    "openai/gpt-4o-mini",
                    "google/gemini-2.0-flash-exp:free"
                ]
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "openai",
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                ApiKey = string.Empty,
                Models =
                [
                    "gpt-4o",
                    "gpt-4o-mini",
                    "o3-mini"
                ]
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "groq",
                Name = "Groq",
                BaseUrl = "https://api.groq.com/openai/v1",
                ApiKey = string.Empty,
                Models =
                [
                    "llama-3.3-70b-versatile",
                    "deepseek-r1-distill-llama-70b"
                ]
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "deepseek",
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com/v1",
                ApiKey = string.Empty,
                Models =
                [
                    "deepseek-chat",
                    "deepseek-reasoner"
                ]
            });

            _config.Providers.Add(new AiProvider
            {
                Id = "ollama",
                Name = "Local Ollama",
                BaseUrl = "http://localhost:11434/v1",
                ApiKey = string.Empty,
                Models =
                [
                    "llama3.2",
                    "qwen2.5-coder"
                ]
            });
        }

        if (_config.Shortcuts == null || _config.Shortcuts.Count == 0)
        {
            _config.Shortcuts =
            [
                new() { Name = "Antigravity IDE", Target = _config.ToolTargets.GetValueOrDefault("Antigravity-ide", defaults.GetValueOrDefault("Antigravity-ide", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "Claude Desktop", Target = _config.ToolTargets.GetValueOrDefault("Claude", defaults.GetValueOrDefault("Claude", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "ChatGPT Desktop", Target = _config.ToolTargets.GetValueOrDefault("ChatGPT", defaults.GetValueOrDefault("ChatGPT", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "OpenCode Desktop", Target = _config.ToolTargets.GetValueOrDefault("OpenCode", defaults.GetValueOrDefault("OpenCode", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "Cline", Target = _config.ToolTargets.GetValueOrDefault("Cline", defaults.GetValueOrDefault("Cline", string.Empty)), Category = "AI Dev Tool" },
                new() { Name = "FreeBuff Desktop", Target = _config.ToolTargets.GetValueOrDefault("FreeBuff", defaults.GetValueOrDefault("FreeBuff", string.Empty)), Category = "AI Dev Tool" }
            ];
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

        // A config with no workspaces at all has never had a preset applied, so the
        // stored preset is authoritative for the initial enable/disable state.
        bool isFreshWorkspaceConfig = _config.CliWorkspaces == null || _config.CliWorkspaces.Count == 0;

        if (_config.CliWorkspaces == null || _config.CliWorkspaces.Count == 0)
        {
            _config.CliWorkspaces = [];
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

        if (isFreshWorkspaceConfig)
        {
            ApplyWorkspacePresetFlags(_config.TerminalWorkspacePreset);
        }
        else
        {
            // Keep the stored label describing the workspace the user is actually in, so the
            // dashboard never advertises a preset that no longer matches the enabled tabs.
            _config.TerminalWorkspacePreset = MatchPresetToFlags();
        }

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

        _config.ClaudeGatewayProviders ??= [];

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
            return $"antigravity_cli:{id["antigravity_cli:".Length..].Trim()}";
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
            if (item != null) item.IsEnabled = isEnabled;
        }
        SaveConfig(_config);
    }

    /// <summary>
    /// Maps an incoming preset name onto a known preset id. Unknown values fall back to Preset_Duo.
    /// </summary>
    public static string NormalizeWorkspacePreset(string? presetName)
    {
        return (presetName ?? string.Empty).Trim() switch
        {
            "Preset_Clean" => "Preset_Clean",
            "Preset_All" => "Preset_All",
            _ => "Preset_Duo"
        };
    }

    /// <summary>
    /// Applies a preset's enable/disable flags in place. Callers hold the lock.
    /// </summary>
    private void ApplyWorkspacePresetFlags(string? presetName)
    {
        var preset = NormalizeWorkspacePreset(presetName);

        var (hermes, claude, antigravity) = preset switch
        {
            "Preset_Clean" => (false, false, false),
            "Preset_All" => (true, true, true),
            _ => (false, true, true)
        };

        SetWorkspaceEnabled("hermes", hermes);
        SetWorkspaceEnabled("claude_code", claude);
        SetWorkspaceEnabled("antigravity_cli", antigravity);

        _config.TerminalWorkspacePreset = preset;
    }

    private void SetWorkspaceEnabled(string normalizedId, bool enabled)
    {
        if (_config.CliWorkspaces == null) return;

        var ws = _config.CliWorkspaces.FirstOrDefault(c =>
            string.Equals(NormalizeCliId(c.Id), normalizedId, StringComparison.OrdinalIgnoreCase));
        if (ws != null) ws.IsEnabled = enabled;
    }

    private bool IsWorkspaceEnabled(string normalizedId)
    {
        var ws = _config.CliWorkspaces?.FirstOrDefault(c =>
            string.Equals(NormalizeCliId(c.Id), normalizedId, StringComparison.OrdinalIgnoreCase));
        return ws?.IsEnabled ?? false;
    }

    /// <summary>
    /// Names the preset whose flags match the current workspace exactly. A hand-toggled
    /// combination that matches none of the three returns "" (no preset active).
    /// </summary>
    private string MatchPresetToFlags()
    {
        bool hermes = IsWorkspaceEnabled("hermes");
        bool claude = IsWorkspaceEnabled("claude_code");
        bool antigravity = IsWorkspaceEnabled("antigravity_cli");

        if (!hermes && !claude && !antigravity) return "Preset_Clean";
        if (hermes && claude && antigravity) return "Preset_All";
        if (!hermes && claude && antigravity) return "Preset_Duo";

        return string.Empty;
    }

    /// <summary>
    /// Applies one of the three workspace presets and persists the result to ai_config.json.
    /// </summary>
    public void ApplyWorkspacePreset(string presetName)
    {
        lock (_lock)
        {
            ApplyWorkspacePresetFlags(presetName);
        }
        SaveConfig(_config);
    }

    /// <summary>
    /// The active workspace preset: "Preset_Clean", "Preset_Duo", "Preset_All", or "" when the
    /// enabled tabs are a hand-toggled mix that matches no preset.
    /// </summary>
    public string GetTerminalWorkspacePreset()
    {
        lock (_lock)
        {
            return _config.TerminalWorkspacePreset ?? string.Empty;
        }
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
            if (provider != null) provider.ApiKey = apiKey.Trim();
            StartupHelper.SetStringPreference("ClaudeGatewayApiKey", apiKey.Trim());

            // Also synchronize active preset's API key
            var activeProv = GetActiveClaudeGatewayProvider();
            if (activeProv != null) activeProv.ApiKey = apiKey.Trim();
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
            var list = _config.ClaudeGatewayProviders ?? [];
            return [.. list.Select(p => new ClaudeGatewayProvider
            {
                Id = p.Id,
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                ApiKey = p.ApiKey,
                ApiKeys = p.GetEffectiveApiKeys(),
                IsPreset = p.IsPreset
            })];
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

            var providers = _config.ClaudeGatewayProviders ?? [];
            var selectedId = GetClaudeSelectedGatewayId();
            var found = providers.FirstOrDefault(p => string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return new ClaudeGatewayProvider
                {
                    Id = found.Id,
                    Name = found.Name,
                    BaseUrl = found.BaseUrl,
                    ApiKey = found.ApiKey,
                    ApiKeys = found.GetEffectiveApiKeys(),
                    IsPreset = found.IsPreset
                };
            }

            var localPreset = providers.FirstOrDefault(p => string.Equals(p.Id, "preset_local_proxy", StringComparison.OrdinalIgnoreCase));
            if (localPreset != null)
            {
                return new ClaudeGatewayProvider
                {
                    Id = localPreset.Id,
                    Name = localPreset.Name,
                    BaseUrl = localPreset.BaseUrl,
                    ApiKey = localPreset.ApiKey,
                    ApiKeys = localPreset.GetEffectiveApiKeys(),
                    IsPreset = localPreset.IsPreset
                };
            }

            return providers.FirstOrDefault() ?? new ClaudeGatewayProvider
            {
                Id = "preset_local_proxy",
                Name = "Local Proxy (127.0.0.1:9230)",
                BaseUrl = "http://127.0.0.1:9230",
                ApiKey = GetOpenRouterApiKey(),
                ApiKeys = !string.IsNullOrWhiteSpace(GetOpenRouterApiKey()) ? [GetOpenRouterApiKey()] : [],
                IsPreset = true
            };
        }
    }

    public void SaveClaudeGatewayProvider(ClaudeGatewayProvider provider)
    {
        lock (_lock)
        {
            _config.ClaudeGatewayProviders ??= [];

            var idx = _config.ClaudeGatewayProviders.FindIndex(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var existing = _config.ClaudeGatewayProviders[idx];
                existing.Name = provider.Name;
                existing.BaseUrl = provider.BaseUrl;
                existing.ApiKey = provider.ApiKey;
                existing.ApiKeys = provider.ApiKeys;
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

    private readonly List<string> _cachedFreeModels = [];
    private DateTime _lastFreeModelsFetch = DateTime.MinValue;

    public IReadOnlyList<string> GetCachedOpenRouterFreeModels()
    {
        lock (_lock)
        {
            if (_cachedFreeModels.Count > 0)
            {
                return [.. _cachedFreeModels];
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
                return [.. _cachedFreeModels];
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
            return [.. _cachedFreeModels];
        }
    }
}
