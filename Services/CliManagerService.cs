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
    public CliType Type { get; set; }
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string CommandDescription { get; set; } = "";
    public CliState State { get; set; } = CliState.Offline;
    public string StatusText { get; set; } = "Offline";
    public int ProcessId { get; set; }
    public DateTime? StartTime { get; set; }
    public bool IsWebUi => Type == CliType.Hermes;
}

public class CliManagerService : IDisposable
{
    private readonly HermesProcessService _hermesService;
    private readonly CliWebTerminalServer _webTerminalServer;
    private readonly AntigravityProfileService _profileService;
    private readonly ConcurrentDictionary<CliType, ConPtySession> _terminalSessions = new();
    private readonly ConcurrentDictionary<CliType, StringBuilder> _terminalBuffers = new();
    private readonly ConcurrentDictionary<CliType, CliItemInfo> _cliStates = new();
    private const int MaxBufferSize = 150_000;

    public HermesProcessService HermesService => _hermesService;
    public CliWebTerminalServer WebTerminalServer => _webTerminalServer;
    public AntigravityProfileService ProfileService => _profileService;

    public event Action<CliType, CliState, string>? StatusChanged;
    public event Action<CliType, string>? TerminalDataReceived;
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

        _cliStates[CliType.Hermes] = new CliItemInfo
        {
            Type = CliType.Hermes,
            DisplayName = "Hermes (Web Dashboard)",
            ShortName = "Hermes",
            CommandDescription = "hermes dashboard --port [PORT]",
            State = CliState.Offline,
            StatusText = "Engine: Offline"
        };

        _cliStates[CliType.ClaudeCode] = new CliItemInfo
        {
            Type = CliType.ClaudeCode,
            DisplayName = "Claude Code (CLI)",
            ShortName = "Claude Code",
            CommandDescription = "claude",
            State = CliState.Offline,
            StatusText = "CLI: Offline"
        };

        _cliStates[CliType.AntigravityCli] = new CliItemInfo
        {
            Type = CliType.AntigravityCli,
            DisplayName = "Antigravity CLI",
            ShortName = "Antigravity",
            CommandDescription = "agy",
            State = CliState.Offline,
            StatusText = "CLI: Offline"
        };

        _terminalBuffers[CliType.ClaudeCode] = new StringBuilder();
        _terminalBuffers[CliType.AntigravityCli] = new StringBuilder();

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

            var item = _cliStates[CliType.Hermes];
            item.State = cliState;
            item.StatusText = msg;
            StatusChanged?.Invoke(CliType.Hermes, cliState, msg);
        };

        _hermesService.LogMessage += (msg) =>
        {
            LogMessage?.Invoke("Hermes", msg);
        };

        _webTerminalServer = new CliWebTerminalServer(this);
        _webTerminalServer.Start();
    }

    public CliItemInfo GetInfo(CliType type)
    {
        if (_cliStates.TryGetValue(type, out var info))
        {
            return info;
        }

        return new CliItemInfo { Type = type };
    }

    public IReadOnlyList<CliItemInfo> GetAllInfos()
    {
        return [.. _cliStates.Values.OrderBy(x => (int)x.Type)];
    }

    public int GetActiveCount()
    {
        return _cliStates.Values.Count(x => x.State == CliState.Running || x.State == CliState.Starting);
    }

    public void Start(CliType type)
    {
        if (type == CliType.Hermes)
        {
            _hermesService.Start();
            return;
        }

        var info = _cliStates[type];
        if (info.State == CliState.Running || info.State == CliState.Starting)
        {
            return;
        }

        info.State = CliState.Starting;
        info.StatusText = $"Starting {info.ShortName}...";
        StatusChanged?.Invoke(type, CliState.Starting, info.StatusText);
        LogMessage?.Invoke(info.ShortName, $"Launching interactive {info.ShortName} session...");

        try
        {
            // Close existing session if any
            if (_terminalSessions.TryRemove(type, out var oldSession))
            {
                oldSession.Dispose();
            }

            // Clear previous buffer on new run
            if (_terminalBuffers.TryGetValue(type, out var buf))
            {
                lock (buf)
                {
                    buf.Clear();
                }
            }

            var session = new ConPtySession();

            session.OutputDataReceived += (text) =>
            {
                if (_terminalBuffers.TryGetValue(type, out var b))
                {
                    lock (b)
                    {
                        b.Append(text);
                        if (b.Length > MaxBufferSize)
                        {
                            b.Remove(0, b.Length - MaxBufferSize);
                        }
                    }
                }

                TerminalDataReceived?.Invoke(type, text);
            };

            session.Exited += (exitCode) =>
            {
                if (type == CliType.AntigravityCli)
                {
                    try
                    {
                        _profileService.ExportCurrentSession(_profileService.ActiveProfile);
                    }
                    catch
                    {
                        // Ignore exit export failures
                    }
                }

                info.State = CliState.Offline;
                info.StatusText = exitCode == 0 ? "Exited cleanly" : $"Exited with code {exitCode}";
                info.ProcessId = 0;
                StatusChanged?.Invoke(type, CliState.Offline, info.StatusText);
                LogMessage?.Invoke(info.ShortName, $"Process terminated with exit code {exitCode}.");
            };

            // Spawn through Windows command interpreter to resolve path and script aliases
            string command = type switch
            {
                CliType.ClaudeCode => "cmd.exe /c claude",
                CliType.AntigravityCli => "cmd.exe /c agy",
                _ => "cmd.exe"
            };

            if (type == CliType.AntigravityCli)
            {
                _profileService.EnsureActiveProfileApplied();
            }

            bool started = session.Start(command, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 120, 32);
            if (started)
            {
                _terminalSessions[type] = session;
                info.State = CliState.Running;
                info.ProcessId = session.ProcessId;
                info.StartTime = DateTime.UtcNow;
                info.StatusText = $"Running (PID: {session.ProcessId})";
                StatusChanged?.Invoke(type, CliState.Running, info.StatusText);
                LogMessage?.Invoke(info.ShortName, $"Session active with PID {session.ProcessId}.");
            }
            else
            {
                info.State = CliState.Error;
                info.StatusText = "Failed to launch session.";
                StatusChanged?.Invoke(type, CliState.Error, info.StatusText);
                LogMessage?.Invoke(info.ShortName, "Session launch failed.");
            }
        }
        catch (Exception ex)
        {
            info.State = CliState.Error;
            info.StatusText = $"Launch error: {ex.Message}";
            StatusChanged?.Invoke(type, CliState.Error, info.StatusText);
            LogMessage?.Invoke(info.ShortName, $"Exception during launch: {ex.Message}");
        }
    }

    public async Task StopAsync(CliType type)
    {
        if (type == CliType.Hermes)
        {
            await _hermesService.StopAsync();
            return;
        }

        if (type == CliType.AntigravityCli)
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

        var info = _cliStates[type];
        info.StatusText = $"Stopping {info.ShortName}...";
        StatusChanged?.Invoke(type, CliState.Offline, info.StatusText);
        LogMessage?.Invoke(info.ShortName, "Terminating interactive session...");

        await Task.Run(() =>
        {
            if (_terminalSessions.TryRemove(type, out var session))
            {
                session.Stop();
                session.Dispose();
            }

            info.State = CliState.Offline;
            info.StatusText = "CLI: Offline";
            info.ProcessId = 0;
            StatusChanged?.Invoke(type, CliState.Offline, info.StatusText);
            LogMessage?.Invoke(info.ShortName, "Session stopped.");
        });
    }

    public async Task RestartAsync(CliType type)
    {
        await StopAsync(type);
        await Task.Delay(300);
        Start(type);
    }

    public void SendInput(CliType type, string data)
    {
        if (_terminalSessions.TryGetValue(type, out var session) && session.IsRunning)
        {
            session.Write(data);
        }
    }

    public void Resize(CliType type, short cols, short rows)
    {
        if (_terminalSessions.TryGetValue(type, out var session) && session.IsRunning)
        {
            session.Resize(cols, rows);
        }
    }

    public string GetTerminalHistory(CliType type)
    {
        if (_terminalBuffers.TryGetValue(type, out var buf))
        {
            lock (buf)
            {
                return buf.ToString();
            }
        }

        return string.Empty;
    }

    public void ClearTerminalHistory(CliType type)
    {
        if (_terminalBuffers.TryGetValue(type, out var buf))
        {
            lock (buf)
            {
                buf.Clear();
            }
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

        var wasRunning = _cliStates.TryGetValue(CliType.AntigravityCli, out var info) &&
                         (info.State == CliState.Running || info.State == CliState.Starting);

        if (wasRunning)
        {
            await StopAsync(CliType.AntigravityCli);
            await Task.Delay(350);
        }

        bool applied = _profileService.ApplyProfile(profileName);

        if (wasRunning && applied)
        {
            await Task.Delay(200);
            Start(CliType.AntigravityCli);
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

        var wasRunning = _cliStates.TryGetValue(CliType.AntigravityCli, out var info) &&
                         (info.State == CliState.Running || info.State == CliState.Starting);

        if (wasRunning)
        {
            await StopAsync(CliType.AntigravityCli);
            await Task.Delay(350);
        }

        bool prepared = _profileService.PrepareNewProfile(newProfileName);

        if (prepared)
        {
            await Task.Delay(200);
            Start(CliType.AntigravityCli);
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
