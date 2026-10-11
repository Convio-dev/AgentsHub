#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using HermesHub.Helpers;

namespace HermesHub.Services;

public sealed class CliWebTerminalServer : IDisposable
{
    private sealed class WebSocketClient(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public async Task SendTextAsync(string text, CancellationToken ct)
        {
            if (Socket.State != WebSocketState.Open) return;
            await SendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                SendLock.Release();
            }
        }
    }

    private sealed class EndpointListener(CliType cliType, int port)
    {
        public CliType CliType { get; } = cliType;
        public int Port { get; } = port;
        public HttpListener? Listener { get; set; }
        public ConcurrentDictionary<WebSocket, WebSocketClient> Sockets { get; } = new();
    }

    private readonly CliManagerService _cliManager;
    private readonly string _webAssetsDir;
    private readonly EndpointListener[] _endpoints;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    public CliWebTerminalServer(CliManagerService cliManager)
    {
        _cliManager = cliManager;
        _webAssetsDir = Path.Combine(AppContext.BaseDirectory, "assets", "web");

        _endpoints =
        [
            new EndpointListener(CliType.ClaudeCode, 9229),
            new EndpointListener(CliType.AntigravityCli, 9339)
        ];

        _cliManager.TerminalDataReceived += OnTerminalDataReceived;
        _cliManager.StatusChanged += OnStatusChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    public void Start()
    {
        foreach (var endpoint in _endpoints)
        {
            StartEndpoint(endpoint);
        }
    }

    private void StartEndpoint(EndpointListener endpoint)
    {
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{endpoint.Port}/");
            listener.Prefixes.Add($"http://localhost:{endpoint.Port}/");
            listener.Start();
            endpoint.Listener = listener;

            _ = Task.Run(() => AcceptRequestsLoopAsync(endpoint, listener, _cts.Token));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to bind CLI web terminal on port {endpoint.Port}: {ex.Message}");
        }
    }

    private async Task AcceptRequestsLoopAsync(EndpointListener endpoint, HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(endpoint, context, ct), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                // Suppress transient accept errors
            }
        }
    }

    private async Task HandleRequestAsync(EndpointListener endpoint, HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            if (context.Request.IsWebSocketRequest)
            {
                string reqPath = context.Request.Url?.AbsolutePath ?? "/";
                if (reqPath == "/ws")
                {
                    await HandleWebSocketAsync(endpoint, context, ct).ConfigureAwait(false);
                    return;
                }
            }

            string path = context.Request.Url?.AbsolutePath ?? "/";
            if (path == "/" || path == "/index.html")
            {
                await ServeFileAsync(context, "terminal.html", "text/html; charset=utf-8").ConfigureAwait(false);
                return;
            }

            string relative = path.TrimStart('/');
            string localFile = Path.Combine(_webAssetsDir, relative);
            if (File.Exists(localFile))
            {
                string ext = Path.GetExtension(localFile).ToLowerInvariant();
                string contentType = ext switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".js" => "application/javascript; charset=utf-8",
                    ".css" => "text/css; charset=utf-8",
                    ".ico" => "image/x-icon",
                    _ => "application/octet-stream"
                };
                await ServeFileAsync(context, relative, contentType).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
        catch
        {
            try { context.Response.Close(); } catch { }
        }
    }

    private async Task ServeFileAsync(HttpListenerContext context, string relativeFile, string contentType)
    {
        string fullPath = Path.Combine(_webAssetsDir, relativeFile);
        if (!File.Exists(fullPath))
        {
            string altPath = Path.Combine(AppContext.BaseDirectory, "ui", relativeFile);
            if (File.Exists(altPath)) fullPath = altPath;
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }
        }

        byte[] bytes;
        if (relativeFile.Equals("terminal.html", StringComparison.OrdinalIgnoreCase) ||
            relativeFile.EndsWith("terminal.html", StringComparison.OrdinalIgnoreCase))
        {
            string html = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);

            string reqTheme = context.Request.QueryString["theme"] ?? (ThemeManager.CurrentTheme == AppTheme.Light ? "light" : "dark");
            string reqAccent = context.Request.QueryString["accent"] ?? ThemeManager.AccentHex;
            bool isLight = reqTheme.Equals("light", StringComparison.OrdinalIgnoreCase);

            var (h, s, l) = ThemeManager.HexToHsl(reqAccent);
            double lum = ThemeManager.GetLuminance(reqAccent);
            string accentText = lum > 0.62 ? "#0a0f14" : "#ffffff";

            string bgTerminal = isLight ? $"hsl({h}, 22%, 95.5%)" : $"hsl({h}, 22%, 5.5%)";
            string bgOverlay = isLight ? $"hsla({h}, 22%, 95.5%, 0.94)" : $"hsla({h}, 22%, 5.5%, 0.94)";
            string bgCard = isLight ? "#ffffff" : $"hsl({h}, 26%, 12.5%)";
            string borderSubtle = isLight ? $"hsl({h}, 25%, 83%)" : $"hsl({h}, 24%, 19%)";
            string textMain = isLight ? $"hsl({h}, 28%, 11%)" : "#f8fafc";
            string textMuted = isLight ? $"hsl({h}, 16%, 36%)" : $"hsl({h}, 12%, 68%)";
            string textDim = isLight ? $"hsl({h}, 10%, 52%)" : $"hsl({h}, 10%, 48%)";

            string dynamicRoot = $":root {{\n      --bg-terminal: {bgTerminal};\n      --bg-overlay: {bgOverlay};\n      --bg-card: {bgCard};\n      --border-subtle: {borderSubtle};\n      --text-main: {textMain};\n      --text-muted: {textMuted};\n      --text-dim: {textDim};\n      --accent-green: {reqAccent};\n      --accent-text: {accentText};\n    }}";

            var rootRegex = new Regex(@":root\s*\{[^}]*\}", RegexOptions.Singleline);
            if (rootRegex.IsMatch(html))
            {
                html = rootRegex.Replace(html, dynamicRoot, 1);
            }

            string scriptInject = $"<script>window.__INITIAL_THEME__='{reqTheme}';window.__INITIAL_ACCENT__='{reqAccent}';</script>\n</head>";
            html = html.Replace("</head>", scriptInject, StringComparison.OrdinalIgnoreCase);

            bytes = Encoding.UTF8.GetBytes(html);
        }
        else
        {
            bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
        }

        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private async Task HandleWebSocketAsync(EndpointListener endpoint, HttpListenerContext context, CancellationToken ct)
    {
        HttpListenerWebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        }
        catch
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
            return;
        }

        var webSocket = wsContext.WebSocket;
        var client = new WebSocketClient(webSocket);
        endpoint.Sockets.TryAdd(webSocket, client);

        try
        {
            var info = _cliManager.GetInfo(endpoint.CliType);
            var history = _cliManager.GetTerminalHistory(endpoint.CliType);
            var initPayload = new CliWebviewForm.TerminalInitPayload(
                "init",
                info.ShortName,
                info.State == CliState.Running,
                info.StatusText,
                history);

            string initJson = JsonSerializer.Serialize(initPayload, CliWebviewForm.CliJsonContext.Default.TerminalInitPayload);
            await client.SendTextAsync(initJson, ct).ConfigureAwait(false);

            // Send current active theme immediately
            string currentTheme = ThemeManager.CurrentTheme == AppTheme.Light ? "light" : "dark";
            string currentAccent = ThemeManager.AccentHex;
            var themePayload = new { type = "theme_changed", theme = currentTheme, accentColor = currentAccent };
            string themeJson = JsonSerializer.Serialize(themePayload);
            await client.SendTextAsync(themeJson, ct).ConfigureAwait(false);

            byte[] buffer = new byte[8192];
            using var ms = new MemoryStream();

            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct).ConfigureAwait(false);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string incomingJson = Encoding.UTF8.GetString(ms.ToArray());
                    using var doc = JsonDocument.Parse(incomingJson);
                    if (doc.RootElement.TryGetProperty("type", out var typeEl))
                    {
                        string? msgType = typeEl.GetString();
                        switch (msgType)
                        {
                            case "input":
                                if (doc.RootElement.TryGetProperty("data", out var dataEl))
                                {
                                    string? text = dataEl.GetString();
                                    if (text != null)
                                    {
                                        _cliManager.SendInput(endpoint.CliType, text);
                                    }
                                }
                                break;

                            case "resize":
                                if (doc.RootElement.TryGetProperty("cols", out var colsEl) &&
                                    doc.RootElement.TryGetProperty("rows", out var rowsEl))
                                {
                                    short cols = colsEl.GetInt16();
                                    short rows = rowsEl.GetInt16();
                                    _cliManager.Resize(endpoint.CliType, cols, rows);
                                }
                                break;

                            case "start":
                                _cliManager.Start(endpoint.CliType);
                                break;
                        }
                    }
                }
            }
        }
        catch
        {
            // Socket disconnected
        }
        finally
        {
            endpoint.Sockets.TryRemove(webSocket, out _);
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposed", CancellationToken.None).ConfigureAwait(false);
                }
                webSocket.Dispose();
            }
            catch { }
        }
    }

    private void OnTerminalDataReceived(CliType type, string text)
    {
        var endpoint = _endpoints.FirstOrDefault(e => e.CliType == type);
        if (endpoint == null || endpoint.Sockets.IsEmpty) return;

        var payload = new CliWebviewForm.TerminalOutputPayload("output", text);
        string json = JsonSerializer.Serialize(payload, CliWebviewForm.CliJsonContext.Default.TerminalOutputPayload);

        foreach (var client in endpoint.Sockets.Values)
        {
            _ = Task.Run(async () =>
            {
                try { await client.SendTextAsync(json, _cts.Token).ConfigureAwait(false); } catch { }
            });
        }
    }

    private void OnStatusChanged(CliType type, CliState state, string message)
    {
        var endpoint = _endpoints.FirstOrDefault(e => e.CliType == type);
        if (endpoint == null || endpoint.Sockets.IsEmpty) return;

        var payload = new CliWebviewForm.TerminalStatusPayload("status", state == CliState.Running, message);
        string json = JsonSerializer.Serialize(payload, CliWebviewForm.CliJsonContext.Default.TerminalStatusPayload);

        foreach (var client in endpoint.Sockets.Values)
        {
            _ = Task.Run(async () =>
            {
                try { await client.SendTextAsync(json, _cts.Token).ConfigureAwait(false); } catch { }
            });
        }
    }

    private void OnThemeChanged()
    {
        BroadcastTheme();
    }

    private void BroadcastTheme()
    {
        string theme = ThemeManager.CurrentTheme == AppTheme.Light ? "light" : "dark";
        string accent = ThemeManager.AccentHex;
        var payload = new { type = "theme_changed", theme, accentColor = accent };
        string json = JsonSerializer.Serialize(payload);

        foreach (var endpoint in _endpoints)
        {
            foreach (var client in endpoint.Sockets.Values)
            {
                _ = Task.Run(async () =>
                {
                    try { await client.SendTextAsync(json, _cts.Token).ConfigureAwait(false); } catch { }
                });
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cliManager.TerminalDataReceived -= OnTerminalDataReceived;
        _cliManager.StatusChanged -= OnStatusChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;

        _cts.Cancel();

        foreach (var endpoint in _endpoints)
        {
            try
            {
                endpoint.Listener?.Stop();
                endpoint.Listener?.Close();
            }
            catch { }

            foreach (var client in endpoint.Sockets.Values)
            {
                try { client.Socket.Dispose(); } catch { }
            }
            endpoint.Sockets.Clear();
        }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
