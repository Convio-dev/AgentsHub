using System.Collections.Concurrent;
using System.Text;
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

    public CliItemInfo GetInfo(string id)
    {
        id = AiConfigService.NormalizeCliId(id);
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
        return [.. _cliStates.Values];
    }

    public int GetActiveCount()
    {
        return _cliStates.Values.Count(x => x.State == CliState.Running || x.State == CliState.Starting);
    }

    public void Start(CliType type) => Start(CliTypeToNormalizedId(type));

    public void Start(string id)
    {
        id = AiConfigService.NormalizeCliId(id);
        if (string.Equals(id, "hermes", StringComparison.OrdinalIgnoreCase))
        {
            _hermesService.Start();
            return;
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
                if (string.Equals(id, "AntigravityCli", StringComparison.OrdinalIgnoreCase))
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

            if (string.Equals(id, "ClaudeCode", StringComparison.OrdinalIgnoreCase))
            {
                command = "cmd.exe /c claude";
            }
            else if (string.Equals(id, "AntigravityCli", StringComparison.OrdinalIgnoreCase))
            {
                command = "cmd.exe /c agy";
                _profileService.EnsureActiveProfileApplied();
            }
            else
            {
                var cfg = AiConfigService.Instance.CurrentConfig.CliWorkspaces.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                var rawCmd = cfg != null && !string.IsNullOrWhiteSpace(cfg.Command) ? cfg.Command.Trim() : id;
                if (!string.IsNullOrWhiteSpace(cfg?.WorkingDirectory) && Directory.Exists(cfg.WorkingDirectory))
                {
                    workDir = cfg.WorkingDirectory;
                }

                if (rawCmd.StartsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) || rawCmd.StartsWith("powershell", StringComparison.OrdinalIgnoreCase))
                {
                    command = rawCmd;
                }
                else
                {
                    command = $"cmd.exe /c {rawCmd}";
                }
            }

            bool started = session.Start(command, workDir, 120, 32);
            if (started)
            {
                _terminalSessions[id] = session;
                info.State = CliState.Running;
                info.ProcessId = session.ProcessId;
                info.StartTime = DateTime.UtcNow;
                info.StatusText = $"Running (PID: {session.ProcessId})";
                FireStatusChanged(id, CliState.Running, info.StatusText);
                LogMessage?.Invoke(info.ShortName, $"Session active with PID {session.ProcessId}.");
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
        id = AiConfigService.NormalizeCliId(id);
        if (string.Equals(id, "hermes", StringComparison.OrdinalIgnoreCase))
        {
            await _hermesService.StopAsync();
            return;
        }

        if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
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
        id = AiConfigService.NormalizeCliId(id);
        await StopAsync(id);
        await Task.Delay(300);
        Start(id);
    }

    public void SendInput(CliType type, string data) => SendInput(CliTypeToNormalizedId(type), data);

    public void SendInput(string id, string data)
    {
        id = AiConfigService.NormalizeCliId(id);
        if (_terminalSessions.TryGetValue(id, out var session) && session.IsRunning)
        {
            session.Write(data);
        }
    }

    public void Resize(CliType type, short cols, short rows) => Resize(CliTypeToNormalizedId(type), cols, rows);

    public void Resize(string id, short cols, short rows)
    {
        id = AiConfigService.NormalizeCliId(id);
        if (_terminalSessions.TryGetValue(id, out var session) && session.IsRunning)
        {
            session.Resize(cols, rows);
        }
    }

    public string GetTerminalHistory(CliType type) => GetTerminalHistory(CliTypeToNormalizedId(type));

    public string GetTerminalHistory(string id)
    {
        id = AiConfigService.NormalizeCliId(id);
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
        id = AiConfigService.NormalizeCliId(id);
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
        id = AiConfigService.NormalizeCliId(id);
        StatusChangedId?.Invoke(id, state, message);
        var ct = id switch
        {
            "claude_code" => CliType.ClaudeCode,
            "antigravity_cli" => CliType.AntigravityCli,
            "hermes" => CliType.Hermes,
            _ => (CliType?)null
        };
        if (ct.HasValue)
        {
            StatusChanged?.Invoke(ct.Value, state, message);
        }
    }

    private void FireTerminalDataReceived(string id, string data)
    {
        id = AiConfigService.NormalizeCliId(id);
        TerminalDataReceivedId?.Invoke(id, data);
        var ct = id switch
        {
            "claude_code" => CliType.ClaudeCode,
            "antigravity_cli" => CliType.AntigravityCli,
            "hermes" => CliType.Hermes,
            _ => (CliType?)null
        };
        if (ct.HasValue)
        {
            TerminalDataReceived?.Invoke(ct.Value, data);
        }
    }

    public async Task<bool> SwitchAntigravityProfileAsync(string profileName)
    {
        LogMessage?.Invoke("Antigravity", $"Switching profile to '{profileName}'...");

        try
        {
            _profileService.ExportCurrentSession(_profileService.ActiveProfile);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke("Antigravity Profile", $"Pre-switch export notice: {ex.Message}");
        }

        var wasRunning = _cliStates.TryGetValue("antigravity_cli", out var info) &&
                         (info.State == CliState.Running || info.State == CliState.Starting);

        if (wasRunning)
        {
            await StopAsync("antigravity_cli");
            await Task.Delay(350);
        }

        bool applied = _profileService.ApplyProfile(profileName);

        if (wasRunning && applied)
        {
            await Task.Delay(200);
            Start("antigravity_cli");
        }

        return applied;
    }

    public async Task<bool> CreateAndSwitchNewProfileAsync(string newProfileName)
    {
        LogMessage?.Invoke("Antigravity", $"Preparing new onboarding profile '{newProfileName}'...");

        try
        {
            _profileService.ExportCurrentSession(_profileService.ActiveProfile);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke("Antigravity Profile", $"Pre-create export notice: {ex.Message}");
        }

        var wasRunning = _cliStates.TryGetValue("antigravity_cli", out var info) &&
                         (info.State == CliState.Running || info.State == CliState.Starting);

        if (wasRunning)
        {
            await StopAsync("antigravity_cli");
            await Task.Delay(350);
        }

        bool prepared = _profileService.PrepareNewProfile(newProfileName);

        if (prepared)
        {
            await Task.Delay(200);
            Start("antigravity_cli");
        }

        return prepared;
    }

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
