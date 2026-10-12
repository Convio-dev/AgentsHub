using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HermesHub.Services.ConPty;

namespace HermesHub.Services;

public enum CliType
{
    Hermes = 0,
    ClaudeCode = 1,
    AntigravityCli = 2,
    GeminiCli = AntigravityCli
}

public enum CliState
{
    Offline,
    Starting,
    Running,
    Error
}

public class CliItemInfo
{
    public string Id { get; set; } = "";
    public CliType Type { get; set; } = CliType.Hermes;
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string CommandDescription { get; set; } = "";
    public CliState State { get; set; } = CliState.Offline;
    public string StatusText { get; set; } = "Offline";
    public int ProcessId { get; set; }
    public DateTime? StartTime { get; set; }
    public bool IsCustom { get; set; } = false;
    public bool IsEnabled { get; set; } = true;
    public int? Port { get; set; }
    public bool IsWebUi => !string.IsNullOrEmpty(WebUrl) || Type == CliType.Hermes;
    public string? WebUrl { get; set; }
    public string? ProfileName { get; set; }
}

public class CliManagerService : IDisposable
{
    private readonly HermesProcessService _hermesService;
    private readonly CliWebTerminalServer _webTerminalServer;
    private readonly AntigravityProfileService _profileService;
    private readonly ConcurrentDictionary<string, ConPtySession> _terminalSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StringBuilder> _terminalBuffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CliItemInfo> _cliStates = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxBufferSize = 150_000;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public HermesProcessService HermesService => _hermesService;
    public CliWebTerminalServer WebTerminalServer => _webTerminalServer;
    public AntigravityProfileService ProfileService => _profileService;

    public event Action<CliType, CliState, string>? StatusChanged;
    public event Action<CliType, string>? TerminalDataReceived;
    public event Action<string, CliState, string>? StatusChangedId;
    public event Action<string, string>? TerminalDataReceivedId;
    public event Action<string, string>? LogMessage;

    public CliManagerService(HermesProcessService hermesService)
    {
        _hermesService = hermesService;
        _profileService = new AntigravityProfileService();

        _profileService.LogMessage += (msg) =>
        {
            LogMessage?.Invoke("Antigravity Profile", msg);
        };

        _profileService.ProfileSaved += (name) =>
        {
            LogMessage?.Invoke("Antigravity Profile", $"Saved credentials for profile '{name}'.");
        };

        _cliStates["Hermes"] = new CliItemInfo
        {
            Id = "Hermes",
            Type = CliType.Hermes,
            DisplayName = "Hermes (Web Dashboard)",
            ShortName = "Hermes",
            CommandDescription = "hermes dashboard --port [PORT]",
            State = CliState.Offline,
            StatusText = "Engine: Offline",
            Port = 9119,
            WebUrl = "http://127.0.0.1:9119"
        };

        _cliStates["ClaudeCode"] = new CliItemInfo
        {
            Id = "ClaudeCode",
            Type = CliType.ClaudeCode,
            DisplayName = "Claude Code (CLI)",
            ShortName = "Claude Code",
            CommandDescription = "claude",
            State = CliState.Offline,
            StatusText = "CLI: Offline",
            Port = 9229
        };

        _cliStates["AntigravityCli"] = new CliItemInfo
        {
            Id = "AntigravityCli",
            Type = CliType.AntigravityCli,
            DisplayName = "Antigravity CLI",
            ShortName = "Antigravity",
            CommandDescription = "agy",
            State = CliState.Offline,
            StatusText = "CLI: Offline",
            Port = 9339
        };

        _terminalBuffers["ClaudeCode"] = new StringBuilder();
        _terminalBuffers["AntigravityCli"] = new StringBuilder();

        // Bind Hermes service events
        _hermesService.StatusChanged += (hStatus, msg) =>
        {
            var cliState = hStatus switch
            {
                HermesStatus.Online => CliState.Running,
                HermesStatus.Starting => CliState.Starting,
                HermesStatus.Error => CliState.Error,
                _ => CliState.Offline
            };

            var item = _cliStates.GetOrAdd("Hermes", _ => new CliItemInfo
            {
                Id = "Hermes",
                Type = CliType.Hermes,
                DisplayName = "Hermes (Web Dashboard)",
                ShortName = "Hermes",
                CommandDescription = "hermes dashboard --port [PORT]",
                Port = 9119,
                WebUrl = "http://127.0.0.1:9119"
            });
            item.State = cliState;
            item.StatusText = msg;
            StatusChanged?.Invoke(CliType.Hermes, cliState, msg);
            StatusChangedId?.Invoke("Hermes", cliState, msg);
        };

        _hermesService.LogMessage += (msg) =>
        {
            LogMessage?.Invoke("Hermes", msg);
        };

        _webTerminalServer = new CliWebTerminalServer(this);
        _webTerminalServer.Start();
    }

    public static bool IsAntigravityProfileId(string id, out string profileName)
    {
        id = id?.Trim() ?? string.Empty;
        if (id.StartsWith("antigravity_cli:", StringComparison.OrdinalIgnoreCase))
        {
            profileName = id["antigravity_cli:".Length..].Trim();
            return !string.IsNullOrEmpty(profileName);
        }
        profileName = string.Empty;
        return false;
    }

    public static string FormatAntigravityProfileId(string profileName)
    {
        return $"antigravity_cli:{profileName.Trim()}";
    }

    public int AllocatePortForProfile(string profileName)
    {
        var profId = FormatAntigravityProfileId(profileName);
        if (_cliStates.TryGetValue(profId, out var existingInfo) && existingInfo.Port.HasValue && existingInfo.Port.Value >= 9339)
        {
            int preferred = existingInfo.Port.Value;
            if (IsPortAvailable(preferred) && !IsPortUsedByOtherCli(preferred, profId))
            {
                return preferred;
            }
        }

        int port = 9339;
        while (!IsPortAvailable(port) || IsPortUsedByOtherCli(port, profId))
        {
            port++;
        }
        return port;
    }

    private bool IsPortUsedByOtherCli(int port, string currentId)
    {
        foreach (var kvp in _cliStates)
        {
            if (string.Equals(kvp.Key, currentId, StringComparison.OrdinalIgnoreCase)) continue;
            if ((kvp.Value.State == CliState.Running || kvp.Value.State == CliState.Starting) && kvp.Value.Port == port)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartAntigravityProfile(string profileName) => Start(FormatAntigravityProfileId(profileName));
    public Task StopAntigravityProfileAsync(string profileName) => StopAsync(FormatAntigravityProfileId(profileName));
    public Task RestartAntigravityProfileAsync(string profileName) => RestartAsync(FormatAntigravityProfileId(profileName));

    public bool IsProfileRunning(string profileName)
    {
        var id = FormatAntigravityProfileId(profileName);
        return _cliStates.TryGetValue(id, out var info) && (info.State == CliState.Running || info.State == CliState.Starting);
    }

    public int? GetPortForProfile(string profileName)
    {
        var id = FormatAntigravityProfileId(profileName);
        if (_cliStates.TryGetValue(id, out var info) && info.Port.HasValue)
        {
            return info.Port.Value;
        }
        return null;
    }

    public IReadOnlyList<string> GetRunningAntigravityProfiles()
    {
        var list = new List<string>();
        foreach (var kvp in _cliStates)
        {
            if (IsAntigravityProfileId(kvp.Key, out var pName) && (kvp.Value.State == CliState.Running || kvp.Value.State == CliState.Starting))
            {
                list.Add(pName);
            }
        }
        return list;
    }

    public CliItemInfo GetInfo(string id)
    {
        if (IsAntigravityProfileId(id, out var profName))
        {
            string profId = FormatAntigravityProfileId(profName);
            if (_cliStates.TryGetValue(profId, out var existingProfInfo))
            {
                return existingProfInfo;
            }

            int allocPort = AllocatePortForProfile(profName);
            var createdProf = new CliItemInfo
            {
                Id = profId,
                Type = CliType.AntigravityCli,
                DisplayName = $"Antigravity ({profName})",
                ShortName = $"agy: {profName}",
                CommandDescription = $"agy (Profile: {profName})",
                State = CliState.Offline,
                StatusText = "CLI: Offline",
                Port = allocPort,
                WebUrl = $"http://127.0.0.1:{allocPort}",
                ProfileName = profName
            };
            _cliStates[profId] = createdProf;
            return createdProf;
        }

        id = AiConfigService.NormalizeCliId(id);
        if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            var activeProf = _profileService.ActiveProfile;
            if (!string.IsNullOrWhiteSpace(activeProf))
            {
                var activeProfId = FormatAntigravityProfileId(activeProf);
                if (_cliStates.TryGetValue(activeProfId, out var activeInfo))
                {
                    return activeInfo;
                }
            }
        }

        if (_cliStates.TryGetValue(id, out var info))
        {
            return info;
        }

        var cfg = AiConfigService.Instance.CurrentConfig.CliWorkspaces.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (cfg != null)
        {
            var created = new CliItemInfo
            {
                Id = cfg.Id,
                DisplayName = cfg.Name,
                ShortName = cfg.Name,
                CommandDescription = cfg.Command,
                IsCustom = !cfg.IsDefault,
                IsEnabled = cfg.IsEnabled,
                Port = cfg.Port,
                WebUrl = cfg.IsWebUi ? $"http://127.0.0.1:{cfg.Port ?? 9119}" : null
            };
            _cliStates[cfg.Id] = created;
            return created;
        }

        if (Enum.TryParse<CliType>(id, out var ct))
        {
            return new CliItemInfo { Id = id, Type = ct, ShortName = id, DisplayName = id };
        }

        return new CliItemInfo { Id = id, ShortName = id, DisplayName = id };
    }

    private static string CliTypeToNormalizedId(CliType type) => type switch
    {
        CliType.ClaudeCode => "claude_code",
        CliType.AntigravityCli => "antigravity_cli",
        _ => "hermes"
    };

    public CliItemInfo GetInfo(CliType type) => GetInfo(CliTypeToNormalizedId(type));

    public IReadOnlyList<CliItemInfo> GetAllInfos()
    {
        var workspaces = AiConfigService.Instance.CurrentConfig.CliWorkspaces;
        foreach (var ws in workspaces)
        {
            GetInfo(ws.Id);
        }
        foreach (var prof in _profileService.GetProfiles())
        {
            GetInfo(FormatAntigravityProfileId(prof));
        }
        return [.. _cliStates.Values];
    }

    public int GetActiveCount()
    {
        return _cliStates.Values.Count(x => x.State == CliState.Running || x.State == CliState.Starting);
    }

    public void Start(CliType type) => Start(CliTypeToNormalizedId(type));

    public void Start(string id)
    {
        bool isAgyProf = IsAntigravityProfileId(id, out var profileName);
        if (!isAgyProf)
        {
            id = AiConfigService.NormalizeCliId(id);
            if (string.Equals(id, "hermes", StringComparison.OrdinalIgnoreCase))
            {
                _hermesService.Start();
                return;
            }

            if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
            {
                var activeProf = _profileService.ActiveProfile;
                if (string.IsNullOrWhiteSpace(activeProf))
                {
                    var allProfs = _profileService.GetProfiles();
                    activeProf = allProfs.Count > 0 ? allProfs[0] : "Default";
                }
                id = FormatAntigravityProfileId(activeProf);
                isAgyProf = IsAntigravityProfileId(id, out profileName);
            }
        }

        var info = GetInfo(id);
        if (info.State == CliState.Running || info.State == CliState.Starting)
        {
            return;
        }

        info.State = CliState.Starting;
        info.StatusText = $"Starting {info.ShortName}...";
        FireStatusChanged(id, CliState.Starting, info.StatusText);
        LogMessage?.Invoke(info.ShortName, $"Launching interactive {info.ShortName} session...");

        try
        {
            if (_terminalSessions.TryRemove(id, out var oldSession))
            {
                oldSession.Dispose();
            }

            var buf = _terminalBuffers.GetOrAdd(id, _ => new StringBuilder());
            lock (buf)
            {
                buf.Clear();
            }

            var session = new ConPtySession();

            session.OutputDataReceived += (text) =>
            {
                lock (buf)
                {
                    buf.Append(text);
                    if (buf.Length > MaxBufferSize)
                    {
                        buf.Remove(0, buf.Length - MaxBufferSize);
                    }
                }

                FireTerminalDataReceived(id, text);
            };

            session.Exited += (exitCode) =>
            {
                if (IsAntigravityProfileId(id, out var exitProf))
                {
                    try
                    {
                        _profileService.StopVirtualHomeWatcher(exitProf);
                        _profileService.SyncVirtualHomeToProfile(exitProf);
                    }
                    catch { }
                }
                else if (string.Equals(id, "AntigravityCli", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _profileService.ExportCurrentSession(_profileService.ActiveProfile);
                    }
                    catch { }
                }

                info.State = CliState.Offline;
                info.StatusText = exitCode == 0 ? "Exited cleanly" : $"Exited with code {exitCode}";
                info.ProcessId = 0;
                FireStatusChanged(id, CliState.Offline, info.StatusText);
                LogMessage?.Invoke(info.ShortName, $"Process terminated with exit code {exitCode}.");
            };

            string command;
            string workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            IDictionary<string, string>? envVars = null;

            if (isAgyProf)
            {
                int port = AllocatePortForProfile(profileName);
                info.Port = port;
                info.WebUrl = $"http://127.0.0.1:{port}";

                string virtualHomeDir = _profileService.PrepareVirtualHome(profileName);
                _profileService.StartVirtualHomeWatcher(profileName);

                envVars = new Dictionary<string, string>
                {
                    ["USERPROFILE"] = virtualHomeDir,
                    ["HOME"] = virtualHomeDir,
                    ["APPDATA"] = Path.Combine(virtualHomeDir, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(virtualHomeDir, "AppData", "Local"),
                    ["PORT"] = port.ToString(),
                    ["ANTIGRAVITY_PORT"] = port.ToString()
                };

                command = "cmd.exe /c agy";
                workDir = virtualHomeDir;
            }
            else if (string.Equals(id, "ClaudeCode", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(id, "claude_code", StringComparison.OrdinalIgnoreCase))
            {
                command = "cmd.exe /c claude";
                bool injectionEnabled = AiConfigService.Instance.GetClaudeInjectionEnabled();
                if (injectionEnabled)
                {
                    var activeProvider = AiConfigService.Instance.GetActiveClaudeGatewayProvider();
                    string selectedClaudeModel = AiConfigService.Instance.GetClaudeSelectedModel();

                    // If active provider is local proxy (or port 9230), ensure local proxy is running
                    if (string.Equals(activeProvider.Id, "preset_local_proxy", StringComparison.OrdinalIgnoreCase) ||
                        activeProvider.BaseUrl.Contains(":9230"))
                    {
                        ClaudeOpenRouterProxyService.Instance.Start(9230);
                    }

                    // Pre-emptively suppress "Both ANTHROPIC_AUTH_TOKEN and /login managed key set" conflict
                    EnsureClaudeOpenRouterTokenPrecedence();

                    // Synchronize %USERPROFILE%\.claude\settings.json with provider env and strip ANTHROPIC_API_KEY
                    SyncClaudeGatewaySettingsJson(activeProvider.ApiKey, selectedClaudeModel, activeProvider.BaseUrl);

                    // Strictly inject ONLY ANTHROPIC_AUTH_TOKEN and explicitly remove ANTHROPIC_API_KEY
                    envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ANTHROPIC_BASE_URL"] = string.IsNullOrWhiteSpace(activeProvider.BaseUrl) ? "http://127.0.0.1:9230" : activeProvider.BaseUrl,
                        ["ANTHROPIC_AUTH_TOKEN"] = activeProvider.ApiKey,
                        ["ANTHROPIC_API_KEY"] = "", // Empty signals ConPtySession to remove from child environment
                        ["ANTHROPIC_MODEL"] = selectedClaudeModel,
                        ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = "deepseek/deepseek-r1:free"
                    };

                    try { Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null); } catch { }

                    if (string.IsNullOrWhiteSpace(activeProvider.ApiKey))
                    {
                        LogMessage?.Invoke(info.ShortName, $"Notice: API Key for '{activeProvider.Name}' is empty. Configure key in Gateway Engine.");
                    }
                    else
                    {
                        LogMessage?.Invoke(info.ShortName, $"Gateway active: [{activeProvider.Name}] Model: {selectedClaudeModel} via {activeProvider.BaseUrl}");
                    }
                }
                else
                {
                    RestoreClaudeOfficialLoginIfBackedUp();
                    LogMessage?.Invoke(info.ShortName, "Official Anthropic API mode active (Environment injection disabled).");
                }
            }
            else
            {
                var cfg = AiConfigService.Instance.CurrentConfig.CliWorkspaces.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                var rawCmd = cfg != null && !string.IsNullOrWhiteSpace(cfg.Command) ? cfg.Command.Trim() : id;
                if (!string.IsNullOrWhiteSpace(cfg?.WorkingDirectory) && Directory.Exists(cfg.WorkingDirectory))
                {
                    workDir = cfg.WorkingDirectory;
                }

                if (string.Equals(rawCmd, "cmd", StringComparison.OrdinalIgnoreCase) || rawCmd.StartsWith("cmd.exe", StringComparison.OrdinalIgnoreCase))
                {
                    command = rawCmd.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? rawCmd : "cmd.exe";
                }
                else if (rawCmd.StartsWith("powershell", StringComparison.OrdinalIgnoreCase))
                {
                    command = rawCmd;
                }
                else
                {
                    command = $"cmd.exe /c {rawCmd}";
                }
            }

            bool started = session.Start(command, workDir, 120, 32, envVars);
            if (started)
            {
                _terminalSessions[id] = session;
                info.State = CliState.Running;
                info.ProcessId = session.ProcessId;
                info.StartTime = DateTime.UtcNow;
                string statusDetail = string.Equals(id, "ClaudeCode", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "claude_code", StringComparison.OrdinalIgnoreCase)
                    ? (AiConfigService.Instance.GetClaudeInjectionEnabled() ? $", {AiConfigService.Instance.GetActiveClaudeGatewayProvider().Name}" : ", Official")
                    : (info.Port != null ? $", Port: {info.Port}" : "");
                info.StatusText = $"Running (PID: {session.ProcessId}{statusDetail})";
                FireStatusChanged(id, CliState.Running, info.StatusText);
                LogMessage?.Invoke(info.ShortName, $"Session active with PID {session.ProcessId}{statusDetail}.");
            }
            else
            {
                info.State = CliState.Error;
                info.StatusText = "Failed to launch session.";
                FireStatusChanged(id, CliState.Error, info.StatusText);
                LogMessage?.Invoke(info.ShortName, "Session launch failed.");
            }
        }
        catch (Exception ex)
        {
            info.State = CliState.Error;
            info.StatusText = $"Launch error: {ex.Message}";
            FireStatusChanged(id, CliState.Error, info.StatusText);
            LogMessage?.Invoke(info.ShortName, $"Exception during launch: {ex.Message}");
        }
    }

    public Task StopAsync(CliType type) => StopAsync(CliTypeToNormalizedId(type));

    public async Task StopAsync(string id)
    {
        bool isAgyProf = IsAntigravityProfileId(id, out var profileName);
        if (!isAgyProf)
        {
            id = AiConfigService.NormalizeCliId(id);
            if (string.Equals(id, "hermes", StringComparison.OrdinalIgnoreCase))
            {
                await _hermesService.StopAsync();
                return;
            }

            if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
            {
                var activeProf = _profileService.ActiveProfile;
                if (!string.IsNullOrWhiteSpace(activeProf))
                {
                    id = FormatAntigravityProfileId(activeProf);
                    isAgyProf = IsAntigravityProfileId(id, out profileName);
                }
            }
        }

        if (isAgyProf)
        {
            try
            {
                _profileService.StopVirtualHomeWatcher(profileName);
                _profileService.SyncVirtualHomeToProfile(profileName);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke("Antigravity Profile", $"Pre-stop sync notice: {ex.Message}");
            }
        }
        else if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _profileService.ExportCurrentSession(_profileService.ActiveProfile);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke("Antigravity Profile", $"Pre-stop export error: {ex.Message}");
            }
        }

        var info = GetInfo(id);
        info.StatusText = $"Stopping {info.ShortName}...";
        FireStatusChanged(id, CliState.Offline, info.StatusText);
        LogMessage?.Invoke(info.ShortName, "Terminating interactive session...");

        await Task.Run(() =>
        {
            if (_terminalSessions.TryRemove(id, out var session))
            {
                session.Stop();
                session.Dispose();
            }

            info.State = CliState.Offline;
            info.StatusText = "CLI: Offline";
            info.ProcessId = 0;
            FireStatusChanged(id, CliState.Offline, info.StatusText);
            LogMessage?.Invoke(info.ShortName, "Session stopped.");
        });
    }

    public Task RestartAsync(CliType type) => RestartAsync(CliTypeToNormalizedId(type));

    public async Task RestartAsync(string id)
    {
        if (!IsAntigravityProfileId(id, out _))
        {
            id = AiConfigService.NormalizeCliId(id);
        }
        await StopAsync(id);
        await Task.Delay(300);
        Start(id);
    }

    public void SendInput(CliType type, string data) => SendInput(CliTypeToNormalizedId(type), data);

    public void SendInput(string id, string data)
    {
        if (!IsAntigravityProfileId(id, out _))
        {
            id = AiConfigService.NormalizeCliId(id);
        }
        if (_terminalSessions.TryGetValue(id, out var session) && session.IsRunning)
        {
            session.Write(data);
        }
    }

    public void Resize(CliType type, short cols, short rows) => Resize(CliTypeToNormalizedId(type), cols, rows);

    public void Resize(string id, short cols, short rows)
    {
        if (!IsAntigravityProfileId(id, out _))
        {
            id = AiConfigService.NormalizeCliId(id);
        }
        if (_terminalSessions.TryGetValue(id, out var session) && session.IsRunning)
        {
            session.Resize(cols, rows);
        }
    }

    public string GetTerminalHistory(CliType type) => GetTerminalHistory(CliTypeToNormalizedId(type));

    public string GetTerminalHistory(string id)
    {
        if (!IsAntigravityProfileId(id, out _))
        {
            id = AiConfigService.NormalizeCliId(id);
        }
        if (_terminalBuffers.TryGetValue(id, out var buf))
        {
            lock (buf)
            {
                return buf.ToString();
            }
        }

        return string.Empty;
    }

    public void ClearTerminalHistory(CliType type) => ClearTerminalHistory(CliTypeToNormalizedId(type));

    public void ClearTerminalHistory(string id)
    {
        if (!IsAntigravityProfileId(id, out _))
        {
            id = AiConfigService.NormalizeCliId(id);
        }
        if (_terminalBuffers.TryGetValue(id, out var buf))
        {
            lock (buf)
            {
                buf.Clear();
            }
        }
    }

    private void FireStatusChanged(string id, CliState state, string message)
    {
        StatusChangedId?.Invoke(id, state, message);
        var ct = id switch
        {
            "claude_code" => CliType.ClaudeCode,
            "antigravity_cli" => CliType.AntigravityCli,
            "hermes" => CliType.Hermes,
            _ => (CliType?)null
        };
        if (!ct.HasValue && IsAntigravityProfileId(id, out _))
        {
            ct = CliType.AntigravityCli;
        }
        if (ct.HasValue)
        {
            StatusChanged?.Invoke(ct.Value, state, message);
        }
    }

    private void FireTerminalDataReceived(string id, string data)
    {
        TerminalDataReceivedId?.Invoke(id, data);
        var ct = id switch
        {
            "claude_code" => CliType.ClaudeCode,
            "antigravity_cli" => CliType.AntigravityCli,
            "hermes" => CliType.Hermes,
            _ => (CliType?)null
        };
        if (!ct.HasValue && IsAntigravityProfileId(id, out _))
        {
            ct = CliType.AntigravityCli;
        }
        if (ct.HasValue)
        {
            TerminalDataReceived?.Invoke(ct.Value, data);
        }
    }

    public async Task<bool> SwitchAntigravityProfileAsync(string profileName)
    {
        LogMessage?.Invoke("Antigravity", $"Switching active profile to '{profileName}'...");

        bool applied = _profileService.ApplyProfile(profileName);
        if (applied)
        {
            _profileService.PrepareVirtualHome(profileName);
        }

        await Task.CompletedTask;
        return applied;
    }

    public async Task<bool> CreateAndSwitchNewProfileAsync(string newProfileName)
    {
        LogMessage?.Invoke("Antigravity", $"Preparing new onboarding profile '{newProfileName}'...");

        bool prepared = _profileService.PrepareNewProfile(newProfileName);
        if (prepared)
        {
            _profileService.PrepareVirtualHome(newProfileName);
            await Task.Delay(200);
            StartAntigravityProfile(newProfileName);
        }

        return prepared;
    }

    public async Task<bool> ClaudeLogoutAsync()
    {
        try
        {
            var info = GetInfo("claude_code");
            if (info.State == CliState.Running || info.State == CliState.Starting)
            {
                await StopAsync("claude_code");
            }

            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c claude logout",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                await p.WaitForExitAsync();
            }
            catch { }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var claudeJsonPath = Path.Combine(userProfile, ".claude.json");
            var backupPath = Path.Combine(userProfile, ".claude.json.authbak");
            if (File.Exists(backupPath))
            {
                try { File.Delete(backupPath); } catch { }
            }

            if (File.Exists(claudeJsonPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(claudeJsonPath);
                    using var doc = JsonDocument.Parse(content);
                    var dict = new Dictionary<string, object?>();

                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "oauthAccount", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(prop.Name, "primaryApiKey", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(prop.Name, "sessionKey", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        dict[prop.Name] = JsonElementToValue(prop.Value);
                    }

                    var newJson = JsonSerializer.Serialize(dict, IndentedJsonOptions);
                    await File.WriteAllTextAsync(claudeJsonPath, newJson);
                }
                catch { }
            }

            var credsPath = Path.Combine(userProfile, ".claude", ".credentials.json");
            if (File.Exists(credsPath))
            {
                try { await File.WriteAllTextAsync(credsPath, "{}"); } catch { }
            }

            var settingsJsonPath = Path.Combine(userProfile, ".claude", "settings.json");
            if (File.Exists(settingsJsonPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(settingsJsonPath);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var rootDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                if (string.Equals(prop.Name, "env", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
                                {
                                    var envDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var ep in prop.Value.EnumerateObject())
                                    {
                                        if (string.Equals(ep.Name, "ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }
                                        envDict[ep.Name] = JsonElementToValue(ep.Value);
                                    }
                                    rootDict["env"] = envDict;
                                }
                                else
                                {
                                    rootDict[prop.Name] = JsonElementToValue(prop.Value);
                                }
                            }
                            var newJson = JsonSerializer.Serialize(rootDict, IndentedJsonOptions);
                            await File.WriteAllTextAsync(settingsJsonPath, newJson);
                        }
                    }
                }
                catch { }
            }

            LogMessage?.Invoke("Claude Code", "Claude login credentials logged out. Token conflict resolved.");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke("Claude Code", $"Error during Claude logout: {ex.Message}");
            return false;
        }
    }

    public static void SyncClaudeGatewaySettingsJson(string apiKey, string selectedClaudeModel, string gatewayUrl)
    {
        SyncClaudeOpenRouterSettingsJson(apiKey, selectedClaudeModel, gatewayUrl);
    }

    public static void SyncClaudeOpenRouterSettingsJson(string openRouterApiKey, string selectedClaudeModel, string gatewayUrl)
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var claudeDir = Path.Combine(userProfile, ".claude");
            var settingsJsonPath = Path.Combine(claudeDir, "settings.json");

            var rootDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(settingsJsonPath))
            {
                var content = File.ReadAllText(settingsJsonPath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            rootDict[prop.Name] = JsonElementToValue(prop.Value);
                        }
                    }
                }
            }
            else if (!Directory.Exists(claudeDir))
            {
                Directory.CreateDirectory(claudeDir);
            }

            var envDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (rootDict.TryGetValue("env", out var existingEnvObj))
            {
                if (existingEnvObj is JsonElement envElem && envElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in envElem.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        envDict[prop.Name] = JsonElementToValue(prop.Value);
                    }
                }
                else if (existingEnvObj is Dictionary<string, object?> existingEnvMap)
                {
                    foreach (var kvp in existingEnvMap)
                    {
                        if (string.Equals(kvp.Key, "ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        envDict[kvp.Key] = kvp.Value;
                    }
                }
            }

            var effectiveGateway = string.IsNullOrWhiteSpace(gatewayUrl) ? "https://openrouter.ai/api/v1" : gatewayUrl;
            var effectiveModel = string.IsNullOrWhiteSpace(selectedClaudeModel) ? "qwen/qwen-2.5-coder-32b-instruct:free" : selectedClaudeModel;

            envDict["ANTHROPIC_BASE_URL"] = effectiveGateway;
            envDict["ANTHROPIC_AUTH_TOKEN"] = openRouterApiKey;
            envDict.Remove("ANTHROPIC_API_KEY");
            envDict["ANTHROPIC_MODEL"] = effectiveModel;
            envDict["ANTHROPIC_DEFAULT_OPUS_MODEL"] = "deepseek/deepseek-r1:free";

            rootDict["env"] = envDict;

            var newJson = JsonSerializer.Serialize(rootDict, IndentedJsonOptions);
            File.WriteAllText(settingsJsonPath, newJson);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CliManagerService] Error syncing Claude settings.json: {ex.Message}");
        }
    }

    private static void EnsureClaudeOpenRouterTokenPrecedence()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var claudeJsonPath = Path.Combine(userProfile, ".claude.json");
            var backupPath = Path.Combine(userProfile, ".claude.json.authbak");

            if (File.Exists(claudeJsonPath))
            {
                var content = File.ReadAllText(claudeJsonPath);
                using var doc = JsonDocument.Parse(content);
                bool hasAuthTokens = doc.RootElement.TryGetProperty("oauthAccount", out _) ||
                                     doc.RootElement.TryGetProperty("primaryApiKey", out _) ||
                                     doc.RootElement.TryGetProperty("sessionKey", out _);

                if (hasAuthTokens)
                {
                    File.Copy(claudeJsonPath, backupPath, overwrite: true);

                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "oauthAccount", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(prop.Name, "primaryApiKey", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(prop.Name, "sessionKey", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        dict[prop.Name] = JsonElementToValue(prop.Value);
                    }

                    var newJson = JsonSerializer.Serialize(dict, IndentedJsonOptions);
                    File.WriteAllText(claudeJsonPath, newJson);
                }
            }
        }
        catch { }
    }

    public static void RestoreClaudeOfficialLoginIfBackedUp()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var claudeJsonPath = Path.Combine(userProfile, ".claude.json");
            var backupPath = Path.Combine(userProfile, ".claude.json.authbak");

            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, claudeJsonPath, overwrite: true);
                File.Delete(backupPath);
            }

            var settingsJsonPath = Path.Combine(userProfile, ".claude", "settings.json");
            if (File.Exists(settingsJsonPath))
            {
                var content = File.ReadAllText(settingsJsonPath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var rootDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, "env", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                var envDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                foreach (var ep in prop.Value.EnumerateObject())
                                {
                                    if (string.Equals(ep.Name, "ANTHROPIC_BASE_URL", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(ep.Name, "ANTHROPIC_AUTH_TOKEN", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(ep.Name, "ANTHROPIC_MODEL", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(ep.Name, "ANTHROPIC_DEFAULT_OPUS_MODEL", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }
                                    envDict[ep.Name] = JsonElementToValue(ep.Value);
                                }
                                if (envDict.Count > 0)
                                {
                                    rootDict["env"] = envDict;
                                }
                            }
                            else
                            {
                                rootDict[prop.Name] = JsonElementToValue(prop.Value);
                            }
                        }
                        var newJson = JsonSerializer.Serialize(rootDict, IndentedJsonOptions);
                        File.WriteAllText(settingsJsonPath, newJson);
                    }
                }
            }
        }
        catch { }
    }

    private static object? JsonElementToValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => JsonDocument.Parse(el.GetRawText()).RootElement.Clone()
    };

    public void Dispose()
    {
        foreach (var session in _terminalSessions.Values)
        {
            try
            {
                session.Dispose();
            }
            catch
            {
                // Suppress disposal errors on shutdown
            }
        }
        _terminalSessions.Clear();
        _webTerminalServer.Dispose();
        _hermesService.Dispose();
        _profileService.Dispose();
        GC.SuppressFinalize(this);
    }
}
