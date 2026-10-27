#nullable enable
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentsHub.Services;

public sealed class OpenCodeSessionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("directory")]
    public string Directory { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    [JsonPropertyName("isBusy")]
    public bool IsBusy { get; set; }

    [JsonPropertyName("agentState")]
    public string AgentState { get; set; } = "idle";
}

public sealed class OpenCodeInteractivePromptDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "permission"; // "permission" or "question"

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

public sealed class OpenCodeStateDto
{
    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("activeSessions")]
    public List<OpenCodeSessionDto> ActiveSessions { get; set; } = [];
}

public sealed partial class OpenCodeWatcherService : IDisposable
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    private readonly Lock _stateLock = new();
    private readonly List<OpenCodeSessionDto> _sessions = [];
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private readonly System.Threading.Timer? _heartbeatTimer;
    private CancellationTokenSource? _debounceCts;
    private bool _isDisposed;

    public bool IsOnline { get; private set; }
    public int Port { get; private set; }
    public string Password { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public int Pid { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;
    public string NormalizedBaseUrl { get; private set; } = string.Empty;

    private readonly HashSet<string> _notifiedPromptIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _sseCts;
    private Task? _sseListeningTask;
    private int _consecutiveFailures;

    public event Action<bool, int>? OnStatusChanged;
    public event Action<List<OpenCodeSessionDto>>? OnSessionsUpdated;
    public event Action<OpenCodeInteractivePromptDto>? OnInteractivePrompt;

    public OpenCodeWatcherService()
    {
        InitializeWatchers();
        _ = DiscoverAndReloadCredentialsAsync();

        // Background periodic healthcheck (every 4 seconds)
        _heartbeatTimer = new System.Threading.Timer(
            async _ => await CheckHealthAndRefreshAsync(),
            null,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(4)
        );
    }

    private void InitializeWatchers()
    {
        var watchDirs = new List<string>();

        // 1. Primary path dir: %USERPROFILE%\.local\state\opencode
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var primaryDir = Path.Combine(userProfile, ".local", "state", "opencode");
        watchDirs.Add(primaryDir);

        // 2. Fallback path dir: %APPDATA%\ai.opencode.desktop\opencode
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var fallbackDir = Path.Combine(appData, "ai.opencode.desktop", "opencode");
        watchDirs.Add(fallbackDir);
        watchDirs.Add(Path.Combine(appData, "ai.opencode.desktop"));

        foreach (var dir in watchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Deleted += OnFileEvent;
                watcher.Renamed += OnFileRenamed;

                _fileWatchers.Add(watcher);
            }
            catch
            {
                // Fall back to timer polling if FileSystemWatcher fails on restricted drives
            }
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsCandidateFile(e.Name))
        {
            ScheduleDebouncedReload();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsCandidateFile(e.Name) || IsCandidateFile(e.OldName))
        {
            ScheduleDebouncedReload();
        }
    }

    private static bool IsCandidateFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return fileName.Contains("service.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("credentials", StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleDebouncedReload()
    {
        lock (_stateLock)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    if (!token.IsCancellationRequested)
                    {
                        await DiscoverAndReloadCredentialsAsync();
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    public static string NormalizeBaseUrl(string rawUrl, int port)
    {
        string b = string.IsNullOrWhiteSpace(rawUrl)
            ? (port > 0 ? $"http://127.0.0.1:{port}" : string.Empty)
            : rawUrl.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(b)) return string.Empty;
        if (b.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) return b;
        return $"{b}/api";
    }

    public async Task DiscoverAndReloadCredentialsAsync()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Check locations in strict priority order:
        // 1. Primary (Installed Desktop build): %USERPROFILE%\.local\state\opencode\service.json
        // 2. Fallback (Alternative Desktop build): %APPDATA%\ai.opencode.desktop\opencode\.sidecar-credentials.json
        // 3. Fallback alternative: %APPDATA%\ai.opencode.desktop\.sidecar-credentials.json
        var candidatePaths = new[]
        {
            Path.Combine(userProfile, ".local", "state", "opencode", "service.json"),
            Path.Combine(appData, "ai.opencode.desktop", "opencode", ".sidecar-credentials.json"),
            Path.Combine(appData, "ai.opencode.desktop", ".sidecar-credentials.json")
        };

        foreach (var candidateFile in candidatePaths)
        {
            if (!File.Exists(candidateFile)) continue;

            // Retry-reads with FileShare.ReadWrite to eliminate Windows file-locking exceptions
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    using var fs = new FileStream(candidateFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var doc = await JsonDocument.ParseAsync(fs);
                    var root = doc.RootElement;

                    int port = 0;
                    string rawUrl = string.Empty;
                    string password = string.Empty;
                    int pid = 0;
                    string version = string.Empty;

                    // Schema A: { "url": "http://127.0.0.1:PORT", "password": "...", "pid": ..., "version": "..." }
                    if (root.TryGetProperty("url", out var urlEl))
                    {
                        rawUrl = urlEl.GetString() ?? string.Empty;
                        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                        {
                            port = uri.Port;
                        }
                    }

                    // Schema B: { "port": PORT, "password": "..." }
                    if (port <= 0 && root.TryGetProperty("port", out var portEl))
                    {
                        if (portEl.ValueKind == JsonValueKind.Number && portEl.TryGetInt32(out var pNum))
                        {
                            port = pNum;
                        }
                        else if (portEl.ValueKind == JsonValueKind.String && int.TryParse(portEl.GetString(), out var pStr))
                        {
                            port = pStr;
                        }
                        rawUrl = $"http://127.0.0.1:{port}";
                    }

                    if (root.TryGetProperty("password", out var pwdEl))
                    {
                        password = pwdEl.GetString() ?? string.Empty;
                    }

                    if (root.TryGetProperty("pid", out var pidEl))
                    {
                        if (pidEl.ValueKind == JsonValueKind.Number && pidEl.TryGetInt32(out var pNum))
                        {
                            pid = pNum;
                        }
                        else if (pidEl.ValueKind == JsonValueKind.String && int.TryParse(pidEl.GetString(), out var pStr))
                        {
                            pid = pStr;
                        }
                    }

                    if (root.TryGetProperty("version", out var verEl))
                    {
                        version = verEl.GetString() ?? string.Empty;
                    }

                    if (port > 0)
                    {
                        var normalized = NormalizeBaseUrl(rawUrl, port);
                        lock (_stateLock)
                        {
                            Port = port;
                            BaseUrl = rawUrl;
                            NormalizedBaseUrl = normalized;
                            Password = password;
                            if (pid > 0) Pid = pid;
                            if (!string.IsNullOrWhiteSpace(version)) Version = version;
                        }
                        await CheckHealthAndRefreshAsync();
                        return;
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    public async Task CheckHealthAndRefreshAsync()
    {
        string normUrl;
        string password;
        int port;

        lock (_stateLock)
        {
            normUrl = NormalizedBaseUrl;
            password = Password;
            port = Port;
        }

        if (string.IsNullOrWhiteSpace(normUrl) || port <= 0)
        {
            await DiscoverAndReloadCredentialsAsync();
            lock (_stateLock)
            {
                normUrl = NormalizedBaseUrl;
                password = Password;
                port = Port;
            }

            if (string.IsNullOrWhiteSpace(normUrl) || port <= 0)
            {
                SetOnlineStatus(false, 0);
                return;
            }
        }

        var authCandidates = new[]
        {
            $"opencode:{password}",
            $":{password}"
        };

        bool pingOk = false;
        var infoUrl = $"{normUrl}/info";

        foreach (var authCandidate in authCandidates)
        {
            try
            {
                // Active heartbeat probe to ${NormalizedBaseUrl}/info (e.g. http://127.0.0.1:PORT/api/info)
                using var req = new HttpRequestMessage(HttpMethod.Get, infoUrl);
                var authBytes = Encoding.UTF8.GetBytes(authCandidate);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                req.Headers.ConnectionClose = true;

                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var content = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;

                    // Extract version and pid from /api/info response
                    if (root.TryGetProperty("version", out var vEl))
                    {
                        var ver = vEl.GetString();
                        if (!string.IsNullOrWhiteSpace(ver))
                        {
                            lock (_stateLock) { Version = ver; }
                        }
                    }
                    if (root.TryGetProperty("pid", out var pEl))
                    {
                        if (pEl.ValueKind == JsonValueKind.Number && pEl.TryGetInt32(out var pNum))
                        {
                            lock (_stateLock) { Pid = pNum; }
                        }
                        else if (pEl.ValueKind == JsonValueKind.String && int.TryParse(pEl.GetString(), out var pStr))
                        {
                            lock (_stateLock) { Pid = pStr; }
                        }
                    }

                    pingOk = true;
                    break;
                }
            }
            catch
            {
                // Continue to next auth candidate
            }
        }

        if (pingOk)
        {
            _consecutiveFailures = 0;
            SetOnlineStatus(true, port);
            await QuerySessionsAsync(normUrl, password);
            await PollPendingInteractivePromptsAsync(normUrl, password);
        }
        else
        {
            _consecutiveFailures++;

            bool processAlive = false;
            int currentPid;
            lock (_stateLock) { currentPid = Pid; }
            if (currentPid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(currentPid);
                    processAlive = !proc.HasExited;
                }
                catch { }
            }
            if (!processAlive)
            {
                try
                {
                    processAlive = Process.GetProcessesByName("opencode").Length > 0 ||
                                   Process.GetProcessesByName("OpenCode").Length > 0;
                }
                catch { }
            }

            // If the local OpenCode process is alive, debounce momentary network drops (>= 3 consecutive failures)
            if (!processAlive || _consecutiveFailures >= 3)
            {
                SetOnlineStatus(false, 0);
            }
        }
    }

    private async Task QuerySessionsAsync(string normUrl, string password)
    {
        try
        {
            // OpenCode API endpoint for active sessions: ${NormalizedBaseUrl}/session
            var sessionEndpoints = new[] { $"{normUrl}/session", $"{normUrl}/sessions" };
            var authCandidates = new[] { $"opencode:{password}", $":{password}" };

            foreach (var authCandidate in authCandidates)
            {
                var authBytes = Encoding.UTF8.GetBytes(authCandidate);
                var authHeader = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                foreach (var ep in sessionEndpoints)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, ep);
                        req.Headers.Authorization = authHeader;
                        req.Headers.ConnectionClose = true;

                        using var resp = await _httpClient.SendAsync(req);
                        if (resp.IsSuccessStatusCode)
                        {
                            var content = await resp.Content.ReadAsStringAsync();
                            ParseSessionsJson(content);
                            await QuerySessionStatusesAsync(normUrl, authHeader);
                            return;
                        }
                    }
                    catch { }
                }
            }

            ExtractFallbackSessionsFromStorage();
        }
        catch
        {
            ExtractFallbackSessionsFromStorage();
        }
    }

    private void SetOnlineStatus(bool online, int port)
    {
        bool changed = false;
        string normUrl;
        string password;
        lock (_stateLock)
        {
            normUrl = NormalizedBaseUrl;
            password = Password;
            if (IsOnline != online)
            {
                IsOnline = online;
                changed = true;
                if (!online)
                {
                    _sessions.Clear();
                }
            }
        }

        if (changed)
        {
            OnStatusChanged?.Invoke(online, port);
            if (online)
            {
                StartSseListener(normUrl, password);
            }
            else
            {
                StopSseListener();
                OnSessionsUpdated?.Invoke([]);
            }
        }
    }

    private void ParseSessionsJson(string json)
    {
        try
        {
            var newSessions = new List<OpenCodeSessionDto>();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement itemsArray = default;
            if (root.ValueKind == JsonValueKind.Array)
            {
                itemsArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dProp) && dProp.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = dProp;
                }
                else if (root.TryGetProperty("sessions", out var sProp) && sProp.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = sProp;
                }
            }

            if (itemsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in itemsArray.EnumerateArray())
                {
                    var dto = new OpenCodeSessionDto();
                    if (el.TryGetProperty("id", out var idProp)) dto.Id = idProp.GetString() ?? string.Empty;
                    if (el.TryGetProperty("title", out var titleProp)) dto.Title = titleProp.GetString() ?? string.Empty;
                    else if (el.TryGetProperty("name", out var nameProp)) dto.Title = nameProp.GetString() ?? string.Empty;

                    // Extract directory from location.directory or direct fields
                    if (el.TryGetProperty("location", out var locProp) && locProp.ValueKind == JsonValueKind.Object)
                    {
                        if (locProp.TryGetProperty("directory", out var locDirProp))
                        {
                            dto.Directory = locDirProp.GetString() ?? string.Empty;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(dto.Directory))
                    {
                        if (el.TryGetProperty("directory", out var dirProp)) dto.Directory = dirProp.GetString() ?? string.Empty;
                        else if (el.TryGetProperty("worktree", out var wtProp)) dto.Directory = wtProp.GetString() ?? string.Empty;
                        else if (el.TryGetProperty("path", out var pathProp)) dto.Directory = pathProp.GetString() ?? string.Empty;
                    }

                    if (el.TryGetProperty("status", out var stProp)) dto.Status = stProp.GetString() ?? "active";
                    else if (el.TryGetProperty("state", out var stateProp)) dto.Status = stateProp.GetString() ?? "active";

                    bool isBusy = dto.Status.Equals("busy", StringComparison.OrdinalIgnoreCase) ||
                                  dto.Status.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                                  dto.Status.Equals("generating", StringComparison.OrdinalIgnoreCase) ||
                                  dto.Status.Equals("thinking", StringComparison.OrdinalIgnoreCase);

                    dto.IsBusy = isBusy;
                    dto.AgentState = isBusy ? "busy" : "idle";

                    // Friendly title fallback from directory folder name
                    if ((string.IsNullOrWhiteSpace(dto.Title) || dto.Title.StartsWith("New session", StringComparison.OrdinalIgnoreCase))
                        && !string.IsNullOrWhiteSpace(dto.Directory))
                    {
                        var dirName = Path.GetFileName(dto.Directory.TrimEnd('\\', '/'));
                        if (!string.IsNullOrWhiteSpace(dirName))
                        {
                            dto.Title = dirName;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(dto.Id))
                    {
                        dto.Id = !string.IsNullOrWhiteSpace(dto.Directory) ? dto.Directory : Guid.NewGuid().ToString("N");
                    }

                    newSessions.Add(dto);
                }
            }

            bool sessionsChanged = false;
            lock (_stateLock)
            {
                if (!AreSessionsEqual(_sessions, newSessions))
                {
                    _sessions.Clear();
                    _sessions.AddRange(newSessions);
                    sessionsChanged = true;
                }
            }

            if (sessionsChanged)
            {
                OnSessionsUpdated?.Invoke(GetSessions());
            }
        }
        catch
        {
            ExtractFallbackSessionsFromStorage();
        }
    }

    private async Task QuerySessionStatusesAsync(string normUrl, AuthenticationHeaderValue authHeader)
    {
        List<OpenCodeSessionDto> current;
        lock (_stateLock)
        {
            current = [.. _sessions];
        }

        bool updatedAny = false;
        foreach (var s in current)
        {
            if (string.IsNullOrWhiteSpace(s.Directory)) continue;
            try
            {
                var queryUrl = $"{normUrl}/session/status?directory={Uri.EscapeDataString(s.Directory)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, queryUrl);
                req.Headers.Authorization = authHeader;
                req.Headers.ConnectionClose = true;

                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var statusJson = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(statusJson);
                    var root = doc.RootElement;
                    string statusVal = "";
                    if (root.TryGetProperty("status", out var stP)) statusVal = stP.GetString() ?? "";
                    else if (root.TryGetProperty("state", out var spP)) statusVal = spP.GetString() ?? "";

                    if (!string.IsNullOrEmpty(statusVal))
                    {
                        bool busy = statusVal.Equals("busy", StringComparison.OrdinalIgnoreCase) ||
                                    statusVal.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                                    statusVal.Equals("generating", StringComparison.OrdinalIgnoreCase) ||
                                    statusVal.Equals("thinking", StringComparison.OrdinalIgnoreCase);
                        if (s.IsBusy != busy || s.Status != statusVal)
                        {
                            s.Status = statusVal;
                            s.IsBusy = busy;
                            s.AgentState = busy ? "busy" : "idle";
                            updatedAny = true;
                        }
                    }
                }
            }
            catch { }
        }

        if (updatedAny)
        {
            OnSessionsUpdated?.Invoke(GetSessions());
        }
    }


    private void ExtractFallbackSessionsFromStorage()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsPath = Path.Combine(appData, "ai.opencode.desktop", "opencode.settings");

            var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(settingsPath))
            {
                var content = File.ReadAllText(settingsPath);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.TryGetProperty("recentWorkspaces", out var recProp) && recProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in recProp.EnumerateArray())
                    {
                        var dir = item.GetString();
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        {
                            discoveredPaths.Add(dir);
                        }
                    }
                }
            }

            if (discoveredPaths.Count > 0)
            {
                var fallbackSessions = discoveredPaths.Select(dir => new OpenCodeSessionDto
                {
                    Id = dir,
                    Title = Path.GetFileName(dir.TrimEnd('\\', '/')),
                    Directory = dir,
                    Status = "idle",
                    IsBusy = false,
                    AgentState = "idle"
                }).ToList();

                bool changed = false;
                lock (_stateLock)
                {
                    if (!AreSessionsEqual(_sessions, fallbackSessions))
                    {
                        _sessions.Clear();
                        _sessions.AddRange(fallbackSessions);
                        changed = true;
                    }
                }

                if (changed)
                {
                    OnSessionsUpdated?.Invoke(GetSessions());
                }
            }
        }
        catch { }
    }

    private static bool AreSessionsEqual(List<OpenCodeSessionDto> a, List<OpenCodeSessionDto> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Id != b[i].Id ||
                a[i].Title != b[i].Title ||
                a[i].Directory != b[i].Directory ||
                a[i].Status != b[i].Status ||
                a[i].IsBusy != b[i].IsBusy ||
                a[i].AgentState != b[i].AgentState)
            {
                return false;
            }
        }
        return true;
    }

    public List<OpenCodeSessionDto> GetSessions()
    {
        lock (_stateLock)
        {
            return [.. _sessions.Select(s => new OpenCodeSessionDto
            {
                Id = s.Id,
                Title = s.Title,
                Directory = s.Directory,
                Status = s.Status,
                IsBusy = s.IsBusy,
                AgentState = s.AgentState
            })];
        }
    }

    public OpenCodeStateDto GetStateDto()
    {
        lock (_stateLock)
        {
            return new OpenCodeStateDto
            {
                IsOnline = IsOnline,
                Port = Port,
                Version = Version,
                Pid = Pid,
                ActiveSessions = GetSessions()
            };
        }
    }

    public static bool FocusOpenCodeSession(string? directory = null)
    {
        try
        {
            // 1. Check for running OpenCode process with a visible window
            var openCodeProcesses = Process.GetProcesses()
                .Where(p => p.ProcessName.Contains("opencode", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var proc in openCodeProcesses)
            {
                var hWnd = proc.MainWindowHandle;
                if (hWnd != IntPtr.Zero)
                {
                    if (IsIconic(hWnd))
                    {
                        ShowWindowAsync(hWnd, SW_RESTORE);
                    }
                    SetForegroundWindow(hWnd);
                    return true;
                }
            }

            // 2. If not already foregrounded or running, launch OpenCode Desktop
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var candidateExes = new[]
            {
                Path.Combine(localAppData, "Programs", "@opencodedesktop", "OpenCode.exe"),
                Path.Combine(localAppData, "Programs", "OpenCode", "OpenCode.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenCode", "OpenCode.exe"),
                Path.Combine(appData, "npm", "opencode.cmd")
            };

            foreach (var exe in candidateExes)
            {
                if (File.Exists(exe))
                {
                    var args = !string.IsNullOrWhiteSpace(directory) ? $"\"{directory}\"" : string.Empty;
                    Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> PurgeIdleSessionsAsync()
    {
        string normUrl;
        string password;
        lock (_stateLock)
        {
            normUrl = NormalizedBaseUrl;
            password = Password;
        }

        if (string.IsNullOrWhiteSpace(normUrl) || !IsOnline)
        {
            return 0;
        }

        var authCandidates = new[] { $"opencode:{password}", $":{password}" };
        int deletedCount = 0;

        try
        {
            // 1. Fetch current sessions JSON from /api/session
            string? sessionsJson = null;
            string workingAuth = authCandidates[0];

            foreach (var authCandidate in authCandidates)
            {
                var authBytes = Encoding.UTF8.GetBytes(authCandidate);
                var authHeader = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                using var req = new HttpRequestMessage(HttpMethod.Get, $"{normUrl}/session");
                req.Headers.Authorization = authHeader;
                req.Headers.ConnectionClose = true;

                try
                {
                    using var resp = await _httpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        sessionsJson = await resp.Content.ReadAsStringAsync();
                        workingAuth = authCandidate;
                        break;
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(sessionsJson))
            {
                return 0;
            }

            using var doc = JsonDocument.Parse(sessionsJson);
            var root = doc.RootElement;
            JsonElement itemsArray = default;

            if (root.ValueKind == JsonValueKind.Array)
            {
                itemsArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dProp) && dProp.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = dProp;
                }
                else if (root.TryGetProperty("sessions", out var sProp) && sProp.ValueKind == JsonValueKind.Array)
                {
                    itemsArray = sProp;
                }
            }

            if (itemsArray.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var authBytesWorking = Encoding.UTF8.GetBytes(workingAuth);
            var authHeaderWorking = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytesWorking));

            foreach (var el in itemsArray.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var idProp)) continue;
                var id = idProp.GetString();
                if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("ses", StringComparison.OrdinalIgnoreCase)) continue;

                // Identify idle or completed sessions (exclude busy/generating sessions)
                bool isBusy = false;
                if (el.TryGetProperty("status", out var stProp))
                {
                    var st = stProp.GetString()?.ToLowerInvariant();
                    if (st == "busy" || st == "running" || st == "generating")
                    {
                        isBusy = true;
                    }
                }

                if (isBusy) continue;

                // Send DELETE request to Sidecar endpoint: DELETE /api/session/{id}
                try
                {
                    using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"{normUrl}/session/{id}");
                    delReq.Headers.Authorization = authHeaderWorking;
                    delReq.Headers.ConnectionClose = true;

                    using var delResp = await _httpClient.SendAsync(delReq);
                    if (delResp.IsSuccessStatusCode || delResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        deletedCount++;
                    }
                }
                catch { }
            }

            // 2. Refresh active sessions list after purge
            await QuerySessionsAsync(normUrl, password);
        }
        catch { }

        return deletedCount;
    }

    public async Task<OpenCodeStateDto> RefreshAsync()
    {
        await DiscoverAndReloadCredentialsAsync();
        await CheckHealthAndRefreshAsync();
        return GetStateDto();
    }

    public static string? FindOpenCodeExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidatePaths = new[]
        {
            Path.Combine(localAppData, "Programs", "@opencodedesktop", "OpenCode.exe"),
            Path.Combine(localAppData, "Programs", "OpenCode", "OpenCode.exe"),
            Path.Combine(localAppData, "Programs", "OpenCode Desktop", "OpenCode.exe"),
            Path.Combine(localAppData, "ai.opencode.desktop", "OpenCode.exe"),
            Path.Combine(programFiles, "OpenCode", "OpenCode.exe"),
            Path.Combine(programFilesX86, "OpenCode", "OpenCode.exe"),
            Path.Combine(appData, "npm", "opencode.cmd"),
            Path.Combine(userProfile, "AppData", "Roaming", "npm", "opencode.cmd"),
            Path.Combine(userProfile, ".local", "bin", "opencode"),
            "/usr/local/bin/opencode",
            "/opt/homebrew/bin/opencode",
            "/Applications/OpenCode.app/Contents/MacOS/OpenCode"
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path)) return path;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var exesToCheck = OperatingSystem.IsWindows()
            ? new[] { "OpenCode.exe", "opencode.cmd", "opencode.exe" }
            : new[] { "opencode", "OpenCode" };
        foreach (var folder in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var exeName in exesToCheck)
            {
                try
                {
                    var full = Path.Combine(folder.Trim(), exeName);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        }

        return null;
    }

    public async Task<(bool success, string message)> StartOpenCodeServiceAsync()
    {
        if (IsOnline)
        {
            return (true, "OpenCode service is already running and online.");
        }

        var exe = FindOpenCodeExecutable();
        if (string.IsNullOrWhiteSpace(exe))
        {
            return (false, "OpenCode executable not found in common install paths.");
        }

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
            Process.Start(psi);

            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                await DiscoverAndReloadCredentialsAsync();
                await CheckHealthAndRefreshAsync();
                if (IsOnline)
                {
                    return (true, $"OpenCode started successfully (Port {Port}).");
                }
            }

            return (false, "OpenCode was launched, but Sidecar API did not respond within 10 seconds.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to launch OpenCode: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> StopOpenCodeServiceAsync()
    {
        return await Task.Run(() =>
        {
            int targetPid = Pid;
            int killedCount = 0;

            if (targetPid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(targetPid);
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                        killedCount++;
                    }
                }
                catch { }
            }

            try
            {
                var procs = Process.GetProcesses()
                    .Where(p => p.ProcessName.Contains("opencode", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                            killedCount++;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            SetOnlineStatus(false, 0);

            return killedCount > 0
                ? (true, $"OpenCode stopped ({killedCount} process(es) terminated).")
                : (true, "OpenCode was stopped.");
        });
    }

    public async Task<bool> ReplyPromptAsync(string id, string type, bool approved, string? answer = null)
    {
        string normUrl;
        string password;
        lock (_stateLock)
        {
            normUrl = NormalizedBaseUrl;
            password = Password;
        }

        if (string.IsNullOrWhiteSpace(normUrl) || !IsOnline) return false;

        var authCandidates = new[] { $"opencode:{password}", $":{password}" };
        bool isQuestion = type.Equals("question", StringComparison.OrdinalIgnoreCase);
        string endpoint = isQuestion ? $"{normUrl}/question/{id}/reply" : $"{normUrl}/permission/{id}/reply";

        object bodyObj = isQuestion
            ? (object)new { answer = !string.IsNullOrWhiteSpace(answer) ? answer : (approved ? "yes" : "no") }
            : (object)new { reply = approved ? "always" : "reject" };

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        foreach (var authCandidate in authCandidates)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                var authBytes = Encoding.UTF8.GetBytes(authCandidate);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                req.Headers.ConnectionClose = true;
                req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    lock (_stateLock)
                    {
                        _notifiedPromptIds.Remove(id);
                    }
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    private void StartSseListener(string normUrl, string password)
    {
        StopSseListener();
        _sseCts = new CancellationTokenSource();
        var token = _sseCts.Token;

        _sseListeningTask = Task.Run(async () =>
        {
            var authCandidates = new[] { $"opencode:{password}", $":{password}" };
            while (!token.IsCancellationRequested && IsOnline)
            {
                bool connected = false;
                foreach (var authCandidate in authCandidates)
                {
                    if (token.IsCancellationRequested || !IsOnline) break;
                    try
                    {
                        var req = new HttpRequestMessage(HttpMethod.Get, $"{normUrl}/event");
                        var authBytes = Encoding.UTF8.GetBytes(authCandidate);
                        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                        if (resp.IsSuccessStatusCode)
                        {
                            connected = true;
                            using var stream = await resp.Content.ReadAsStreamAsync(token);
                            using var reader = new StreamReader(stream, Encoding.UTF8);

                            while (!token.IsCancellationRequested)
                            {
                                var line = await reader.ReadLineAsync(token);
                                if (line == null) break;
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var dataJson = line[5..].Trim();
                                    HandleSseEventJson(dataJson);
                                }
                            }
                            break;
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch { }
                }

                if (!connected && !token.IsCancellationRequested && IsOnline)
                {
                    try { await Task.Delay(3000, token); } catch { return; }
                }
            }
        }, token);
    }

    private void StopSseListener()
    {
        try
        {
            _sseCts?.Cancel();
            _sseCts?.Dispose();
        }
        catch { }
        _sseCts = null;
        _sseListeningTask = null;
    }

    private void HandleSseEventJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : string.Empty;

            if (string.Equals(type, "question.asked", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "permission.asked", StringComparison.OrdinalIgnoreCase))
            {
                JsonElement data = root.TryGetProperty("data", out var dProp) ? dProp : root;
                var prompt = ParseInteractivePromptElement(data, (type ?? string.Empty).StartsWith("question", StringComparison.OrdinalIgnoreCase) ? "question" : "permission");
                if (prompt != null)
                {
                    DispatchPromptIfNew(prompt);
                }
            }
            else if (string.Equals(type, "session.status", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(type, "session.updated", StringComparison.OrdinalIgnoreCase))
            {
                _ = RefreshAsync();
            }
        }
        catch { }
    }

    private async Task PollPendingInteractivePromptsAsync(string normUrl, string password)
    {
        try
        {
            var authCandidates = new[] { $"opencode:{password}", $":{password}" };
            string workingAuth = authCandidates[0];

            foreach (var auth in authCandidates)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{normUrl}/question");
                    var authBytes = Encoding.UTF8.GetBytes(auth);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                    req.Headers.ConnectionClose = true;
                    using var resp = await _httpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        ParseAndDispatchPromptList(json, "question");
                        workingAuth = auth;
                        break;
                    }
                }
                catch { }
            }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{normUrl}/permission");
                var authBytes = Encoding.UTF8.GetBytes(workingAuth);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                req.Headers.ConnectionClose = true;
                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    ParseAndDispatchPromptList(json, "permission");
                }
            }
            catch { }
        }
        catch { }
    }

    private void DispatchPromptIfNew(OpenCodeInteractivePromptDto prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt.Id)) return;
        bool isNew;
        lock (_stateLock)
        {
            isNew = _notifiedPromptIds.Add(prompt.Id);
        }
        if (isNew)
        {
            OnInteractivePrompt?.Invoke(prompt);
        }
    }

    private void ParseAndDispatchPromptList(string json, string defaultType)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement arrayEl = default;

            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayEl = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dEl) && dEl.ValueKind == JsonValueKind.Array) arrayEl = dEl;
                else if (root.TryGetProperty("questions", out var qEl) && qEl.ValueKind == JsonValueKind.Array) arrayEl = qEl;
                else if (root.TryGetProperty("permissions", out var pEl) && pEl.ValueKind == JsonValueKind.Array) arrayEl = pEl;
            }

            if (arrayEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arrayEl.EnumerateArray())
                {
                    var prompt = ParseInteractivePromptElement(item, defaultType);
                    if (prompt != null)
                    {
                        DispatchPromptIfNew(prompt);
                    }
                }
            }
        }
        catch { }
    }

    private static OpenCodeInteractivePromptDto? ParseInteractivePromptElement(JsonElement el, string defaultType)
    {
        try
        {
            var p = new OpenCodeInteractivePromptDto { Type = defaultType };
            if (el.TryGetProperty("id", out var idProp)) p.Id = idProp.GetString() ?? string.Empty;
            if (el.TryGetProperty("sessionID", out var sIdProp)) p.SessionId = sIdProp.GetString() ?? string.Empty;
            else if (el.TryGetProperty("sessionId", out var sId2Prop)) p.SessionId = sId2Prop.GetString() ?? string.Empty;

            if (el.TryGetProperty("type", out var typeProp))
            {
                var t = typeProp.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(t)) p.Type = t.StartsWith("question", StringComparison.OrdinalIgnoreCase) ? "question" : "permission";
            }

            if (el.TryGetProperty("title", out var titleProp)) p.Title = titleProp.GetString() ?? string.Empty;
            if (el.TryGetProperty("prompt", out var promptProp)) p.Prompt = promptProp.GetString() ?? string.Empty;
            else if (el.TryGetProperty("question", out var qProp)) p.Prompt = qProp.GetString() ?? string.Empty;
            else if (el.TryGetProperty("message", out var msgProp)) p.Prompt = msgProp.GetString() ?? string.Empty;

            if (el.TryGetProperty("command", out var cmdProp)) p.Command = cmdProp.GetString() ?? string.Empty;
            else if (el.TryGetProperty("pattern", out var patProp)) p.Command = patProp.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(p.Title))
            {
                p.Title = p.Type.Equals("question", StringComparison.OrdinalIgnoreCase)
                    ? "OpenCode Agent Question"
                    : "Tool Execution Permission Request";
            }

            if (el.TryGetProperty("options", out var optsProp) && optsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var opt in optsProp.EnumerateArray())
                {
                    var s = opt.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) p.Options.Add(s);
                }
            }

            return !string.IsNullOrWhiteSpace(p.Id) ? p : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopSseListener();
        _heartbeatTimer?.Dispose();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        foreach (var watcher in _fileWatchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }
        _fileWatchers.Clear();
    }
}
