using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace HermesHub.Services;

public class ConnectedClientInfo
{
    public string IpAddress { get; set; } = "";
    public string DeviceName { get; set; } = "Network Device";
    public string? HostName { get; set; }
    public string? UserAgent { get; set; }
    public int ActiveConnections { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public class LanProxyService : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<string, DateTime> _recentlyNotifiedRemoteIps = new();
    private readonly ConcurrentDictionary<string, ConnectedClientInfo> _clients = new();

    public int ListenPort { get; set; } = 9191;
    public int TargetPort { get; set; } = 9119;
    public bool IsRunning { get; private set; }

    public Func<Task>? StartHermesAction { get; set; }
    public Func<Task<bool>>? CheckHermesAliveAction { get; set; }

    public event Action<bool>? StateChanged;
    public event Action<string>? LogMessage;
    public event Action<string>? RemoteClientConnected;
    public event Action<ConnectedClientInfo>? RemoteDeviceConnected;
    public event Action<IReadOnlyList<ConnectedClientInfo>>? ActiveClientsChanged;

    public bool Start(int listenPort = 9191, int targetPort = 9119)
    {
        lock (_lock)
        {
            if (IsRunning) return true;

            ListenPort = listenPort;
            TargetPort = targetPort;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, listenPort);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();

                IsRunning = true;
                _listenTask = Task.Run(() => AcceptConnectionsAsync(_listener, _cts.Token));

                LogMessage?.Invoke($"LAN Proxy active on 0.0.0.0:{listenPort} -> 127.0.0.1:{targetPort}");
                StateChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Failed to bind proxy listener on port {listenPort}: {ex.Message}");
                Stop();
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning && _listener == null) return;

            IsRunning = false;

            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // Suppress cancellation exceptions
            }

            try
            {
                _listener?.Stop();
            }
            catch
            {
                // Suppress socket stop exceptions
            }

            _listener = null;
            _cts?.Dispose();
            _cts = null;

            _clients.Clear();
            NotifyClientsChanged();

            LogMessage?.Invoke("LAN Proxy stopped.");
            StateChanged?.Invoke(false);
        }
    }

    public IReadOnlyList<ConnectedClientInfo> GetActiveClients()
    {
        var now = DateTime.UtcNow;
        return [.. _clients.Values
            .Where(c => c.ActiveConnections > 0 || (now - c.LastActivity).TotalSeconds < 90)
            .OrderByDescending(c => c.ActiveConnections > 0)
            .ThenByDescending(c => c.LastActivity)];
    }

    public string GetConnectedClientsSummary()
    {
        var active = GetActiveClients();
        if (active.Count == 0)
        {
            return "Active Clients: 0 (No remote devices)";
        }

        var items = active.Select(c => $"{c.IpAddress} [{c.DeviceName}]");
        return $"Active Clients: {active.Count} • {string.Join(", ", items)}";
    }

    private void NotifyClientsChanged()
    {
        ActiveClientsChanged?.Invoke(GetActiveClients());
    }

    private void RegisterClientConnection(string ip)
    {
        var client = _clients.GetOrAdd(ip, address =>
        {
            var info = new ConnectedClientInfo
            {
                IpAddress = address,
                DeviceName = "Network Device",
                FirstSeen = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                ActiveConnections = 0
            };
            _ = TryResolveHostNameAsync(address);
            return info;
        });

        lock (client)
        {
            client.ActiveConnections++;
            client.LastActivity = DateTime.UtcNow;
        }

        NotifyRemoteClientIfApplicable(client);
        NotifyClientsChanged();
    }

    private void UnregisterClientConnection(string ip)
    {
        if (_clients.TryGetValue(ip, out var client))
        {
            lock (client)
            {
                client.ActiveConnections = Math.Max(0, client.ActiveConnections - 1);
                client.LastActivity = DateTime.UtcNow;
            }
            NotifyClientsChanged();
        }
    }

    private void UpdateClientUserAgent(string ip, string userAgent)
    {
        if (_clients.TryGetValue(ip, out var client))
        {
            var updated = false;
            lock (client)
            {
                if (client.UserAgent != userAgent)
                {
                    client.UserAgent = userAgent;
                    client.DeviceName = CombineHostAndDevice(client.HostName, userAgent);
                    updated = true;
                }
            }
            if (updated)
            {
                NotifyClientsChanged();
            }
        }
    }

    private async Task TryResolveHostNameAsync(string ip)
    {
        try
        {
            using var cts = new CancellationTokenSource(800);
            var entry = await Dns.GetHostEntryAsync(ip, cts.Token);
            if (!string.IsNullOrWhiteSpace(entry.HostName) && entry.HostName != ip)
            {
                var clean = entry.HostName.Split('.')[0];
                if (_clients.TryGetValue(ip, out var client))
                {
                    lock (client)
                    {
                        client.HostName = clean;
                        client.DeviceName = CombineHostAndDevice(clean, client.UserAgent);
                    }
                    NotifyClientsChanged();
                }
            }
        }
        catch
        {
            // Reverse DNS timeout or unresolvable on local subnet
        }
    }

    private static string CombineHostAndDevice(string? hostName, string? userAgent)
    {
        var deviceType = DetectDeviceType(userAgent);
        if (!string.IsNullOrWhiteSpace(hostName) && !hostName.Equals(deviceType, StringComparison.OrdinalIgnoreCase))
        {
            return $"{hostName} ({deviceType})";
        }
        return deviceType;
    }

    public static string DetectDeviceType(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Network Device";

        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "Apple iPhone";
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "Apple iPad";
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            if (userAgent.Contains("Samsung", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("SM-", StringComparison.OrdinalIgnoreCase))
                return "Samsung Galaxy";
            if (userAgent.Contains("Pixel", StringComparison.OrdinalIgnoreCase))
                return "Google Pixel";
            if (userAgent.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Redmi", StringComparison.OrdinalIgnoreCase))
                return "Xiaomi Phone";
            return "Android Device";
        }
        if (userAgent.Contains("Windows NT", StringComparison.OrdinalIgnoreCase)) return "Windows PC";
        if (userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) return "Mac";
        if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux PC";
        if (userAgent.Contains("curl", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("python", StringComparison.OrdinalIgnoreCase))
            return "API Client";

        return "Remote Device";
    }

    private static string? ExtractUserAgent(string headers)
    {
        var match = Regex.Match(headers, @"(?im)^User-Agent:\s*([^\r\n]+)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Proxy accept warning: {ex.Message}");
            }
        }
    }

    private void NotifyRemoteClientIfApplicable(ConnectedClientInfo client)
    {
        try
        {
            var ipStr = client.IpAddress;
            var now = DateTime.UtcNow;

            // Throttle notifications to once per 2 minutes per remote IP
            if (!_recentlyNotifiedRemoteIps.TryGetValue(ipStr, out var lastTime) ||
                (now - lastTime).TotalMinutes >= 2)
            {
                _recentlyNotifiedRemoteIps[ipStr] = now;
                RemoteClientConnected?.Invoke(ipStr);
                RemoteDeviceConnected?.Invoke(client);
            }
        }
        catch
        {
            // Ignore socket endpoint interrogation failures
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        string clientIp = "127.0.0.1";
        bool isRemote = false;

        try
        {
            if (client.Client.RemoteEndPoint is IPEndPoint endpoint)
            {
                clientIp = endpoint.Address.ToString();
                isRemote = !IPAddress.IsLoopback(endpoint.Address);
            }
        }
        catch
        {
            // Endpoint interrogation issue
        }

        if (isRemote)
        {
            RegisterClientConnection(clientIp);
        }

        using (client)
        {
            TcpClient? backend = null;
            try
            {
                using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var clientStream = client.GetStream();

                var buffer = new byte[8192];
                var bytesRead = await clientStream.ReadAsync(buffer.AsMemory(0, buffer.Length), clientCts.Token);
                if (bytesRead <= 0) return;

                var initialText = Encoding.ASCII.GetString(buffer, 0, Math.Min(bytesRead, 1024));
                var requestPath = ExtractRequestPath(initialText);

                if (isRemote)
                {
                    var userAgent = ExtractUserAgent(initialText);
                    if (!string.IsNullOrEmpty(userAgent))
                    {
                        UpdateClientUserAgent(clientIp, userAgent);
                    }
                }

                // Handle internal proxy control endpoints
                if (requestPath.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
                    requestPath.StartsWith("/__hermes/start", StringComparison.OrdinalIgnoreCase))
                {
                    if (StartHermesAction != null)
                    {
                        _ = StartHermesAction.Invoke();
                    }
                    await SendStartingPageAsync(clientStream);
                    return;
                }

                if (requestPath.StartsWith("/status", StringComparison.OrdinalIgnoreCase) ||
                    requestPath.StartsWith("/__hermes/status", StringComparison.OrdinalIgnoreCase))
                {
                    var isAlive = await CheckBackendAliveAsync(TargetPort);
                    await SendStatusJsonAsync(clientStream, isAlive);
                    return;
                }

                // Verify if backend is reachable
                var backendHealthy = await CheckBackendAliveAsync(TargetPort);
                if (!backendHealthy)
                {
                    await SendOfflinePageAsync(clientStream);
                    return;
                }

                // Connect to backend daemon
                backend = new TcpClient();
                try
                {
                    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await backend.ConnectAsync(IPAddress.Loopback, TargetPort, connectCts.Token);
                }
                catch
                {
                    await SendOfflinePageAsync(clientStream);
                    return;
                }

                var backendStream = backend.GetStream();

                // Forward the initial modified request
                var modifiedInitialBytes = ProcessClientBuffer(buffer, bytesRead, TargetPort);
                await backendStream.WriteAsync(modifiedInitialBytes.AsMemory(), clientCts.Token);
                await backendStream.FlushAsync(clientCts.Token);

                // Stream bidirectionally with continuous header adjustment on keep-alive
                var clientToBackend = ForwardClientToBackendAsync(clientStream, backendStream, TargetPort, clientCts.Token);
                var backendToClient = StreamCopyAsync(backendStream, clientStream, clientCts.Token);

                await Task.WhenAny(clientToBackend, backendToClient);
                clientCts.Cancel();
            }
            catch
            {
                // Normal connection teardown
            }
            finally
            {
                if (isRemote)
                {
                    UnregisterClientConnection(clientIp);
                }
                backend?.Dispose();
            }
        }
    }

    private static string ExtractRequestPath(string text)
    {
        var firstLineEnd = text.IndexOfAny(['\r', '\n']);
        var line = firstLineEnd > 0 ? text[..firstLineEnd] : text;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : "/";
    }

    private async Task<bool> CheckBackendAliveAsync(int port)
    {
        if (CheckHermesAliveAction != null)
        {
            return await CheckHermesAliveAction.Invoke();
        }

        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(400);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ForwardClientToBackendAsync(Stream clientStream, Stream backendStream, int targetPort, CancellationToken token)
    {
        var buffer = new byte[16384];
        while (!token.IsCancellationRequested)
        {
            var bytesRead = await clientStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (bytesRead <= 0) break;

            var modifiedBytes = ProcessClientBuffer(buffer, bytesRead, targetPort);
            await backendStream.WriteAsync(modifiedBytes.AsMemory(), token);
            await backendStream.FlushAsync(token);
        }
    }

    private static byte[] ProcessClientBuffer(byte[] buffer, int length, int targetPort)
    {
        try
        {
            if (StartsWithHttpMethod(buffer, length))
            {
                var text = Encoding.ASCII.GetString(buffer, 0, length);
                var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                if (headerEnd > 0)
                {
                    var headerSection = text[..headerEnd];
                    var bodySection = buffer[(headerEnd + 4)..length];

                    // Replace Host line with target while strictly keeping CRLF intact
                    var rewrittenHeaders = Regex.Replace(
                        headerSection,
                        @"(?im)^Host:[^\r\n]*",
                        $"Host: 127.0.0.1:{targetPort}"
                    );

                    var newHeaderBytes = Encoding.ASCII.GetBytes(rewrittenHeaders + "\r\n\r\n");
                    var result = new byte[newHeaderBytes.Length + bodySection.Length];
                    Buffer.BlockCopy(newHeaderBytes, 0, result, 0, newHeaderBytes.Length);
                    Buffer.BlockCopy(bodySection, 0, result, newHeaderBytes.Length, bodySection.Length);
                    return result;
                }
            }
        }
        catch
        {
            // Fall back to original bytes on format parse issues
        }

        var fallback = new byte[length];
        Buffer.BlockCopy(buffer, 0, fallback, 0, length);
        return fallback;
    }

    private static bool StartsWithHttpMethod(byte[] buffer, int length)
    {
        if (length < 4) return false;
        return (buffer[0] == 'G' && buffer[1] == 'E' && buffer[2] == 'T' && buffer[3] == ' ') ||
               (buffer[0] == 'P' && buffer[1] == 'O' && buffer[2] == 'S' && buffer[3] == 'T') ||
               (buffer[0] == 'P' && buffer[1] == 'U' && buffer[2] == 'T' && buffer[3] == ' ') ||
               (buffer[0] == 'D' && buffer[1] == 'E' && buffer[2] == 'L' && buffer[3] == 'E') ||
               (buffer[0] == 'P' && buffer[1] == 'A' && buffer[2] == 'T' && buffer[3] == 'C') ||
               (buffer[0] == 'H' && buffer[1] == 'E' && buffer[2] == 'A' && buffer[3] == 'D') ||
               (buffer[0] == 'O' && buffer[1] == 'P' && buffer[2] == 'T' && buffer[3] == 'I');
    }

    private static async Task StreamCopyAsync(Stream source, Stream destination, CancellationToken token)
    {
        var buffer = new byte[16384];
        while (!token.IsCancellationRequested)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (read <= 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            await destination.FlushAsync(token);
        }
    }

    private static async Task SendOfflinePageAsync(Stream stream)
    {
        const string html = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>Agents Hub - Engine Offline</title>
              <style>
                * { box-sizing: border-box; }
                body { background: #09090b; color: #fafafa; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 16px; }
                .card { background: #18181b; border: 1px solid #27272a; border-radius: 12px; padding: 36px 28px; max-width: 460px; width: 100%; text-align: center; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }
                .badge { display: inline-block; background: rgba(239, 68, 68, 0.15); color: #ef4444; border: 1px solid rgba(239, 68, 68, 0.3); padding: 4px 12px; border-radius: 999px; font-size: 0.8rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 16px; }
                h1 { font-size: 1.4rem; font-weight: 700; margin: 0 0 10px 0; color: #fafafa; }
                p { color: #a1a1aa; font-size: 0.95rem; line-height: 1.5; margin: 0 0 24px 0; }
                .btn-group { display: flex; flex-direction: column; gap: 10px; }
                .btn { display: block; width: 100%; padding: 12px 18px; border-radius: 8px; font-size: 0.95rem; font-weight: 600; text-decoration: none; cursor: pointer; transition: all 0.2s ease; border: none; text-align: center; }
                .btn-start { background: #10b981; color: #09090b; }
                .btn-start:hover { background: #059669; }
                .btn-open { background: #27272a; color: #fafafa; border: 1px solid #3f3f46; }
                .btn-open:hover { background: #3f3f46; }
                .progress { display: none; margin-top: 20px; padding: 16px; background: #09090b; border: 1px solid #27272a; border-radius: 8px; }
                .spinner { width: 28px; height: 28px; border: 3px solid rgba(16, 185, 129, 0.2); border-top-color: #10b981; border-radius: 50%; animation: spin 0.8s linear infinite; margin: 0 auto 12px auto; }
                @keyframes spin { to { transform: rotate(360deg); } }
                .status-msg { font-size: 0.9rem; color: #10b981; font-weight: 600; margin: 0 0 4px 0; }
                .status-sub { font-size: 0.8rem; color: #71717a; margin: 0; }
              </style>
            </head>
            <body>
              <div class="card">
                <div class="badge">Engine Offline</div>
                <h1>Hermes Engine Offline</h1>
                <p>The local Hermes daemon on port 9119 is stopped. You can launch it directly from this web page.</p>
                <div class="btn-group" id="actions">
                  <button type="button" class="btn btn-start" onclick="triggerStart()">Start Hermes Engine</button>
                  <a href="/" class="btn btn-open">Open Dashboard (Retry)</a>
                </div>
                <div class="progress" id="progressArea">
                  <div class="spinner"></div>
                  <p class="status-msg" id="statusMsg">Initiating Hermes Engine...</p>
                  <p class="status-sub">Waiting for port 9119 to become ready. Redirecting automatically.</p>
                </div>
              </div>
              <script>
                let isPolling = false;
                function triggerStart() {
                  document.getElementById('actions').style.display = 'none';
                  document.getElementById('progressArea').style.display = 'block';
                  fetch('/__hermes/start').catch(() => {});
                  startPolling();
                }
                function startPolling() {
                  if (isPolling) return;
                  isPolling = true;
                  const interval = setInterval(async () => {
                    try {
                      const res = await fetch('/__hermes/status');
                      if (res.ok) {
                        const data = await res.json();
                        if (data.online) {
                          clearInterval(interval);
                          document.getElementById('statusMsg').innerText = 'Hermes Online! Loading...';
                          setTimeout(() => { window.location.href = '/'; }, 600);
                        }
                      }
                    } catch (e) {}
                  }, 1200);
                }
                // Background check in case Hermes was started elsewhere
                setInterval(async () => {
                  if (isPolling) return;
                  try {
                    const res = await fetch('/__hermes/status');
                    if (res.ok) {
                      const data = await res.json();
                      if (data.online) {
                        window.location.href = '/';
                      }
                    }
                  } catch (e) {}
                }, 2500);
              </script>
            </body>
            </html>
            """;

        var bodyBytes = Encoding.UTF8.GetBytes(html);
        var header = $"HTTP/1.1 502 Bad Gateway\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes.AsMemory());
        await stream.WriteAsync(bodyBytes.AsMemory());
        await stream.FlushAsync();
    }

    private static async Task SendStartingPageAsync(Stream stream)
    {
        const string html = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>Starting Hermes Engine</title>
              <style>
                * { box-sizing: border-box; }
                body { background: #09090b; color: #fafafa; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 16px; }
                .card { background: #18181b; border: 1px solid #27272a; border-radius: 12px; padding: 36px 28px; max-width: 460px; width: 100%; text-align: center; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }
                .spinner { width: 34px; height: 34px; border: 3px solid rgba(16, 185, 129, 0.2); border-top-color: #10b981; border-radius: 50%; animation: spin 0.8s linear infinite; margin: 0 auto 16px auto; }
                @keyframes spin { to { transform: rotate(360deg); } }
                h1 { font-size: 1.35rem; font-weight: 700; margin: 0 0 10px 0; color: #10b981; }
                p { color: #a1a1aa; font-size: 0.95rem; line-height: 1.5; margin: 0 0 20px 0; }
                .btn { display: inline-block; padding: 10px 20px; border-radius: 8px; font-size: 0.9rem; font-weight: 600; text-decoration: none; background: #27272a; color: #fafafa; border: 1px solid #3f3f46; }
                .btn:hover { background: #3f3f46; }
              </style>
            </head>
            <body>
              <div class="card">
                <div class="spinner"></div>
                <h1>Starting Hermes Engine...</h1>
                <p>Background process has been launched. Waiting for local port 9119 to respond.</p>
                <a href="/" class="btn">Open Hermes Dashboard</a>
              </div>
              <script>
                const interval = setInterval(async () => {
                  try {
                    const res = await fetch('/__hermes/status');
                    if (res.ok) {
                      const data = await res.json();
                      if (data.online) {
                        clearInterval(interval);
                        setTimeout(() => { window.location.href = '/'; }, 500);
                      }
                    }
                  } catch (e) {}
                }, 1000);
              </script>
            </body>
            </html>
            """;

        var bodyBytes = Encoding.UTF8.GetBytes(html);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes.AsMemory());
        await stream.WriteAsync(bodyBytes.AsMemory());
        await stream.FlushAsync();
    }

    private static async Task SendStatusJsonAsync(Stream stream, bool isAlive)
    {
        var json = $"{{\"online\": {(isAlive ? "true" : "false")}}}";
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes.AsMemory());
        await stream.WriteAsync(bodyBytes.AsMemory());
        await stream.FlushAsync();
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
