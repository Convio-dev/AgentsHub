#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class DiscordRpcSettings
{
    public const string OfficialAppId = "1562567704941174834";
    public const string OfficialLargeKey = "app";
    public const string OfficialSmallKey = "cli";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("applicationId")]
    public string ApplicationId { get; set; } = OfficialAppId;

    [JsonPropertyName("privacyMode")]
    public string PrivacyMode { get; set; } = "default"; // "default", "transparent", "project_only", "stealth"

    [JsonPropertyName("showElapsedTime")]
    public bool ShowElapsedTime { get; set; } = true;

    [JsonPropertyName("showPartySize")]
    public bool ShowPartySize { get; set; } = true;

    [JsonPropertyName("showButtons")]
    public bool ShowButtons { get; set; } = true;

    [JsonPropertyName("largeImageKey")]
    public string LargeImageKey { get; set; } = "app";

    [JsonPropertyName("largeImageText")]
    public string LargeImageText { get; set; } = "AgentsHub - AI Agent Station";

    [JsonPropertyName("smallImageKey")]
    public string SmallImageKey { get; set; } = "cli";

    [JsonPropertyName("smallImageText")]
    public string SmallImageText { get; set; } = "Terminal Active";

    [JsonPropertyName("button1Label")]
    public string Button1Label { get; set; } = "Get AgentsHub";

    [JsonPropertyName("button1Url")]
    public string Button1Url { get; set; } = "https://github.com/ZerroDevs/AgentsHub";

    [JsonPropertyName("button2Label")]
    public string? Button2Label { get; set; }

    [JsonPropertyName("button2Url")]
    public string? Button2Url { get; set; }

    [JsonPropertyName("statusTemplate")]
    public string StatusTemplate { get; set; } = "{repo} [{branch}]";

    public DiscordRpcSettings Clone() => (DiscordRpcSettings)MemberwiseClone();
}

public sealed class DiscordRpcStatusDto
{
    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; set; }

    [JsonPropertyName("connectedPipe")]
    public string? ConnectedPipe { get; set; }

    [JsonPropertyName("discordUsername")]
    public string? DiscordUsername { get; set; }

    [JsonPropertyName("discordUserId")]
    public string? DiscordUserId { get; set; }

    [JsonPropertyName("discordAvatar")]
    public string? DiscordAvatar { get; set; }

    [JsonPropertyName("currentDetails")]
    public string? CurrentDetails { get; set; }

    [JsonPropertyName("currentState")]
    public string? CurrentState { get; set; }

    [JsonPropertyName("sessionStartTime")]
    public long SessionStartTime { get; set; }

    [JsonPropertyName("settings")]
    public DiscordRpcSettings Settings { get; set; } = new();
}

public sealed class DiscordRpcService : IDisposable
{
    public static DiscordRpcService Instance { get; } = new();

    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private readonly string _settingsFile;
    private DiscordRpcSettings _settings = new();

    private CancellationTokenSource? _loopCts;
    private NamedPipeClientStream? _pipe;
    private bool _isConnected;
    private string? _connectedPipeName;
    private string? _discordUsername;
    private string? _discordUserId;
    private string? _discordAvatar;

    private readonly long _sessionStartTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private string? _lastDetails;
    private string? _lastState;
    private int _activeAgentsCount;
    private string _activeAgentName = "Claude";

    public event Action<DiscordRpcStatusDto>? StatusChanged;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DiscordRpcService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentsHub");
        Directory.CreateDirectory(appData);
        _settingsFile = Path.Combine(appData, "discord_rpc_settings.json");

        LoadSettings();
    }

    public void LoadSettings()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile, Encoding.UTF8);
                    _settings = JsonSerializer.Deserialize<DiscordRpcSettings>(json, JsonOpts) ?? new DiscordRpcSettings();
                }
            }
            catch
            {
                _settings = new DiscordRpcSettings();
            }
        }
    }

    public void SaveSettings(DiscordRpcSettings newSettings)
    {
        _ = SaveSettingsAsync(newSettings);
    }

    public async Task SaveSettingsAsync(DiscordRpcSettings newSettings)
    {
        // Enforce official locked credentials
        newSettings.ApplicationId = DiscordRpcSettings.OfficialAppId;
        newSettings.LargeImageKey = DiscordRpcSettings.OfficialLargeKey;
        newSettings.SmallImageKey = DiscordRpcSettings.OfficialSmallKey;

        bool wasEnabled;
        bool nowEnabled;
        string oldAppId;
        string newAppId;

        lock (_lock)
        {
            wasEnabled = _settings.Enabled;
            nowEnabled = newSettings.Enabled;
            oldAppId = _settings.ApplicationId;
            newAppId = newSettings.ApplicationId;

            _settings = newSettings;
            try
            {
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(_settings, JsonOpts), Encoding.UTF8);
            }
            catch { }
        }

        if (!nowEnabled)
        {
            await ClearActivityAndDisconnectAsync().ConfigureAwait(false);
        }
        else if (!wasEnabled || !string.Equals(oldAppId, newAppId, StringComparison.Ordinal))
        {
            await ReconnectAsync().ConfigureAwait(false);
        }
        else
        {
            await UpdateActivityAsync().ConfigureAwait(false);
        }

        NotifyStatus();
    }

    public DiscordRpcStatusDto GetStatus()
    {
        lock (_lock)
        {
            return new DiscordRpcStatusDto
            {
                IsConnected = _isConnected,
                ConnectedPipe = _connectedPipeName,
                DiscordUsername = _discordUsername,
                DiscordUserId = _discordUserId,
                DiscordAvatar = _discordAvatar,
                CurrentDetails = _lastDetails,
                CurrentState = _lastState,
                SessionStartTime = _sessionStartTime,
                Settings = _settings.Clone()
            };
        }
    }

    public void Start()
    {
        if (_loopCts != null) return;

        // Subscribe to Git repository change events
        GitRepositoryManagerService.Instance.RepositoriesChanged += OnRepositoriesChanged;

        _loopCts = new CancellationTokenSource();
        _ = Task.Run(() => ConnectionLoopAsync(_loopCts.Token));
    }

    public void Stop()
    {
        GitRepositoryManagerService.Instance.RepositoriesChanged -= OnRepositoriesChanged;

        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;

        _ = ClearActivityAndDisconnectAsync();
    }

    public void SetActiveAgents(int count, string primaryAgentName = "Claude")
    {
        _activeAgentsCount = Math.Max(0, count);
        if (!string.IsNullOrWhiteSpace(primaryAgentName))
        {
            _activeAgentName = primaryAgentName;
        }
        _ = UpdateActivityAsync();
    }

    private void OnRepositoriesChanged()
    {
        _ = UpdateActivityAsync();
    }

    private async Task ConnectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool isEnabled;
            lock (_lock) { isEnabled = _settings.Enabled; }

            if (!isEnabled)
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
                continue;
            }

            if (!_isConnected || _pipe == null || !_pipe.IsConnected)
            {
                await TryConnectAsync(ct).ConfigureAwait(false);
            }

            // Periodic heartbeat / activity refresh (25 seconds to respect Discord rate limits)
            if (_isConnected)
            {
                await Task.Delay(25000, ct).ConfigureAwait(false);
                if (_isConnected)
                {
                    await UpdateActivityAsync(ct).ConfigureAwait(false);
                }
            }
            else
            {
                // Discord not open or pipe unreachable: silent backoff retry
                await Task.Delay(10000, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task TryConnectAsync(CancellationToken ct)
    {
        CleanupPipe();

        string appId;
        lock (_lock) { appId = _settings.ApplicationId; }

        for (int i = 0; i < 10 && !ct.IsCancellationRequested; i++)
        {
            string pipeName = $"discord-ipc-{i}";
            try
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var timeoutCts = new CancellationTokenSource(1500);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                await pipe.ConnectAsync(linkedCts.Token).ConfigureAwait(false);

                // Handshake Opcode 0
                var handshakePayload = JsonSerializer.Serialize(new { v = 1, client_id = appId });
                await WritePacketAsync(pipe, 0, handshakePayload, ct).ConfigureAwait(false);

                // Read Handshake Response Opcode 1 (FRAME)
                var (opcode, responseJson) = await ReadPacketAsync(pipe, ct).ConfigureAwait(false);
                if (opcode == 1 && !string.IsNullOrWhiteSpace(responseJson))
                {
                    _pipe = pipe;
                    _connectedPipeName = pipeName;
                    _isConnected = true;

                    ParseDiscordReady(responseJson);
                    NotifyStatus();

                    // Start background reader to drain ping/pong & incoming frames
                    _ = Task.Run(() => ReaderLoopAsync(pipe, ct), ct);

                    // Send initial presence immediately
                    await UpdateActivityAsync(ct).ConfigureAwait(false);
                    return;
                }
                else
                {
                    pipe.Dispose();
                }
            }
            catch
            {
                // Pipe not available, continue to next pipe index
            }
        }

        _isConnected = false;
        _connectedPipeName = null;
        NotifyStatus();
    }

    private void ParseDiscordReady(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.TryGetProperty("user", out var userEl))
            {
                _discordUsername = userEl.TryGetProperty("username", out var u) ? u.GetString() : null;
                _discordUserId = userEl.TryGetProperty("id", out var id) ? id.GetString() : null;
                _discordAvatar = userEl.TryGetProperty("avatar", out var av) ? av.GetString() : null;
            }
        }
        catch { }
    }

    private async Task ReaderLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var (opcode, json) = await ReadPacketAsync(pipe, ct).ConfigureAwait(false);
                if (opcode == 2) // CLOSE
                {
                    break;
                }
                else if (opcode == 3) // PING -> PONG
                {
                    await WritePacketAsync(pipe, 4, json, ct).ConfigureAwait(false);
                }
            }
        }
        catch { }
        finally
        {
            if (ReferenceEquals(_pipe, pipe))
            {
                _isConnected = false;
                _connectedPipeName = null;
                NotifyStatus();
            }
        }
    }

    public async Task UpdateActivityAsync(CancellationToken ct = default)
    {
        if (!_isConnected || _pipe == null || !_pipe.IsConnected) return;

        DiscordRpcSettings s;
        lock (_lock)
        {
            if (!_settings.Enabled) return;
            s = _settings;
        }

        try
        {
            // 1. Resolve project and branch info
            var workingId = GitRepositoryManagerService.Instance.GetWorkingProjectId();
            var repo = workingId != null ? GitRepositoryManagerService.Instance.GetRepository(workingId) : null;
            string projectName = repo?.Name ?? "AgentsHub Workspace";
            string branchName = !string.IsNullOrWhiteSpace(repo?.Branch) ? repo.Branch : "main";

            // 2. Build Details & State according to Privacy Mode
            string details;
            string state;

            int totalDirty = repo != null ? (repo.StagedCount + repo.UnstagedCount + repo.UntrackedCount) : 0;

            if (string.Equals(s.PrivacyMode, "default", StringComparison.OrdinalIgnoreCase))
            {
                details = "Coding with AgentsHub";
                state = _activeAgentsCount > 0 ? $"🤖 {_activeAgentName} ({_activeAgentsCount} Active)" : "⚡ AI Agent Station";
            }
            else if (string.Equals(s.PrivacyMode, "stealth", StringComparison.OrdinalIgnoreCase))
            {
                details = "Developing in Private Codebase";
                state = _activeAgentsCount > 0 ? "🤖 Working with AI Agent" : "⚡ AgentsHub Ready";
            }
            else if (string.Equals(s.PrivacyMode, "project_only", StringComparison.OrdinalIgnoreCase))
            {
                details = $"Working on {projectName}";
                state = _activeAgentsCount > 0
                    ? $"🤖 {_activeAgentName} ({_activeAgentsCount} Active)"
                    : (totalDirty > 0 ? $"📦 Reviewing {totalDirty} Changed Files" : "⚡ Workspace Ready");
            }
            else
            {
                // Transparent Mode
                details = $"Working on: {projectName} [{branchName}]";
                state = _activeAgentsCount > 0
                    ? $"🤖 {_activeAgentName} ({_activeAgentsCount} Agent{(_activeAgentsCount > 1 ? "s" : "")} Active)"
                    : (totalDirty > 0 ? $"📦 Reviewing {totalDirty} Changed Files" : "⚡ Idle in AgentsHub");
            }

            _lastDetails = details;
            _lastState = state;

            // 3. Compose Activity Dictionary
            var act = new Dictionary<string, object>
            {
                ["details"] = details,
                ["state"] = state
            };

            // Timestamps
            if (s.ShowElapsedTime)
            {
                act["timestamps"] = new Dictionary<string, object>
                {
                    ["start"] = _sessionStartTime
                };
            }

            // Assets (app / cli)
            var assets = new Dictionary<string, object>
            {
                ["large_image"] = string.IsNullOrWhiteSpace(s.LargeImageKey) ? "app" : s.LargeImageKey,
                ["large_text"] = string.IsNullOrWhiteSpace(s.LargeImageText) ? "AgentsHub - AI Agent Station" : s.LargeImageText
            };

            if (!string.IsNullOrWhiteSpace(s.SmallImageKey))
            {
                assets["small_image"] = s.SmallImageKey;
                assets["small_text"] = _activeAgentsCount > 0 ? $"{_activeAgentName} Active" : s.SmallImageText;
            }
            act["assets"] = assets;

            // Party Size (Active / Max agents)
            if (s.ShowPartySize && _activeAgentsCount > 0)
            {
                act["party"] = new Dictionary<string, object>
                {
                    ["id"] = "agentshub-party",
                    ["size"] = new int[] { _activeAgentsCount, 8 }
                };
            }

            // Buttons
            if (s.ShowButtons && !string.Equals(s.PrivacyMode, "stealth", StringComparison.OrdinalIgnoreCase))
            {
                var buttonsList = new List<Dictionary<string, string>>();
                if (!string.IsNullOrWhiteSpace(s.Button1Label) && !string.IsNullOrWhiteSpace(s.Button1Url))
                {
                    buttonsList.Add(new Dictionary<string, string>
                    {
                        ["label"] = s.Button1Label.Length > 32 ? s.Button1Label[..32] : s.Button1Label,
                        ["url"] = s.Button1Url
                    });
                }

                if (!string.IsNullOrWhiteSpace(s.Button2Label) && !string.IsNullOrWhiteSpace(s.Button2Url))
                {
                    buttonsList.Add(new Dictionary<string, string>
                    {
                        ["label"] = s.Button2Label.Length > 32 ? s.Button2Label[..32] : s.Button2Label,
                        ["url"] = s.Button2Url
                    });
                }

                if (buttonsList.Count > 0)
                {
                    act["buttons"] = buttonsList;
                    var buttonUrls = buttonsList.Select(b => b["url"]).ToList();
                    act["metadata"] = new Dictionary<string, object>
                    {
                        ["button_urls"] = buttonUrls
                    };
                }
            }

            // 4. Wrap in SET_ACTIVITY command
            var frame = new Dictionary<string, object>
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new Dictionary<string, object?>
                {
                    ["pid"] = Environment.ProcessId,
                    ["activity"] = act
                },
                ["nonce"] = Guid.NewGuid().ToString("N")
            };

            var json = JsonSerializer.Serialize(frame);
            await WritePacketAsync(_pipe, 1, json, ct).ConfigureAwait(false);
            NotifyStatus();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiscordRpcService] UpdateActivity error: {ex.Message}");
        }
    }

    public async Task ClearActivityAndDisconnectAsync()
    {
        if (_pipe != null && _pipe.IsConnected)
        {
            try
            {
                var clearFrame = new Dictionary<string, object>
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["args"] = new Dictionary<string, object?>
                    {
                        ["pid"] = Environment.ProcessId,
                        ["activity"] = null
                    },
                    ["nonce"] = Guid.NewGuid().ToString("N")
                };

                await WritePacketAsync(_pipe, 1, JsonSerializer.Serialize(clearFrame), CancellationToken.None).ConfigureAwait(false);
                await Task.Delay(150).ConfigureAwait(false);
            }
            catch { }
        }

        CleanupPipe();
        _isConnected = false;
        _connectedPipeName = null;
        _lastDetails = null;
        _lastState = null;
        NotifyStatus();
    }

    public async Task ReconnectAsync()
    {
        await ClearActivityAndDisconnectAsync().ConfigureAwait(false);
        if (_loopCts != null)
        {
            await TryConnectAsync(_loopCts.Token).ConfigureAwait(false);
        }
    }

    private void CleanupPipe()
    {
        try
        {
            _pipe?.Dispose();
        }
        catch { }
        _pipe = null;
    }

    private async Task WritePacketAsync(NamedPipeClientStream pipe, int opcode, string json, CancellationToken ct)
    {
        await _sendSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);
            byte[] headerBytes = new byte[8];
            BitConverter.GetBytes(opcode).CopyTo(headerBytes, 0);
            BitConverter.GetBytes(bodyBytes.Length).CopyTo(headerBytes, 4);

            await pipe.WriteAsync(headerBytes, ct).ConfigureAwait(false);
            await pipe.WriteAsync(bodyBytes, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    private static async Task<(int opcode, string json)> ReadPacketAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        byte[] header = new byte[8];
        int readHeader = 0;
        while (readHeader < 8)
        {
            int r = await pipe.ReadAsync(header.AsMemory(readHeader, 8 - readHeader), ct).ConfigureAwait(false);
            if (r == 0) return (-1, string.Empty);
            readHeader += r;
        }

        int opcode = BitConverter.ToInt32(header, 0);
        int length = BitConverter.ToInt32(header, 4);
        if (length <= 0 || length > 65536) return (opcode, string.Empty);

        byte[] body = new byte[length];
        int readBody = 0;
        while (readBody < length)
        {
            int r = await pipe.ReadAsync(body.AsMemory(readBody, length - readBody), ct).ConfigureAwait(false);
            if (r == 0) break;
            readBody += r;
        }

        string json = Encoding.UTF8.GetString(body, 0, readBody);
        return (opcode, json);
    }

    private void NotifyStatus()
    {
        StatusChanged?.Invoke(GetStatus());
    }

    public void Dispose()
    {
        Stop();
        _sendSemaphore.Dispose();
    }
}
