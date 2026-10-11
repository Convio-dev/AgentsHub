#nullable enable
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HermesHub.Services;

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
    public List<OpenCodeSessionDto> ActiveSessions { get; set; } = new();
}

public sealed class OpenCodeWatcherService : IDisposable
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    private readonly object _stateLock = new();
    private readonly List<OpenCodeSessionDto> _sessions = new();
    private readonly List<FileSystemWatcher> _fileWatchers = new();
    private System.Threading.Timer? _heartbeatTimer;
    private CancellationTokenSource? _debounceCts;
    private bool _isDisposed;

    public bool IsOnline { get; private set; }
    public int Port { get; private set; }
    public string Password { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public int Pid { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;
    public string NormalizedBaseUrl { get; private set; } = string.Empty;

    public event Action<bool, int>? OnStatusChanged;
    public event Action<List<OpenCodeSessionDto>>? OnSessionsUpdated;

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
            SetOnlineStatus(true, port);
            await QuerySessionsAsync(normUrl, password);
        }
        else
        {
            SetOnlineStatus(false, 0);
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
        lock (_stateLock)
        {
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
            if (!online)
            {
                OnSessionsUpdated?.Invoke(new List<OpenCodeSessionDto>());
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
                    Status = "active"
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
            if (a[i].Id != b[i].Id || a[i].Title != b[i].Title || a[i].Directory != b[i].Directory || a[i].Status != b[i].Status)
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
            return _sessions.Select(s => new OpenCodeSessionDto
            {
                Id = s.Id,
                Title = s.Title,
                Directory = s.Directory,
                Status = s.Status
            }).ToList();
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

    public bool FocusOpenCodeSession(string? sessionId = null, string? directory = null)
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

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
