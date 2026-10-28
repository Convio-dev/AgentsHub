#nullable enable
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AgentsHub.Services.Sidecar.DTOs;

namespace AgentsHub.Services.Sidecar;

/// <summary>
/// High-resilience ClientWebSocket manager with automatic reconnection, heartbeat, and typed event dispatching.
/// </summary>
public class SidecarClient : IDisposable
{
    private readonly SidecarManager _manager;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _connectionCts;
    private Task? _receiveLoopTask;
    private readonly System.Threading.Timer _heartbeatTimer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _isDisposed;
    private long _lastPingTimestamp;

    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
    public int LastPingLatencyMs { get; private set; } = -1;
    public int ActiveSessionsCount { get; private set; } = 0;
    public int SidecarPid { get; private set; } = 0;

    public event Action<AgentStateEvent>? OnStateChanged;
    public event Action<StreamDeltaEvent>? OnStreamDelta;
    public event Action<ChatTokenEvent>? OnChatToken;
    public event Action<ThinkingEvent>? OnThinking;
    public event Action<SessionStatusEvent>? OnSessionStatus;
    public event Action<AccountStateEvent>? OnAccountState;
    public event Action<ToolStartEvent>? OnToolStart;
    public event Action<ToolOutputEvent>? OnToolOutput;
    public event Action<TokenUsageEvent>? OnTokenUsage;
    public event Action<QuotaUpdateEvent>? OnQuotaUpdate;
    public event Action<ModelInfoEvent>? OnModelInfo;
    public event Action<TaskCompletedEvent>? OnTaskCompleted;
    public event Action<TerminalDataEvent>? OnTerminalData;
    public event Action<SidecarErrorEvent>? OnError;
    public event Action<PongEvent, int>? OnPongReceived;
    public event Action<SidecarStatusEvent>? OnSidecarStatus;
    public event Action<BaseSidecarEvent>? OnEventReceived;
    public event Action<bool>? OnStatusChanged;
    public event Action? OnDisconnected;

    public SidecarClient(SidecarManager manager)
    {
        _manager = manager;
        _heartbeatTimer = new System.Threading.Timer(async _ => await SendPingAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _connectionCts?.Cancel();
        _connectionCts = new CancellationTokenSource();

        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {_manager.AuthToken}");

        var uri = new Uri(_manager.WebSocketUrl);
        await ws.ConnectAsync(uri, ct);

        _ws = ws;
        OnStatusChanged?.Invoke(true);

        // Start receive loop
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_connectionCts.Token), _connectionCts.Token);

        // Start heartbeat ping every 10 seconds
        _heartbeatTimer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public async Task SendAsync<T>(T request, CancellationToken ct = default) where T : BaseRequest
    {
        if (!IsConnected || _ws == null)
        {
            throw new InvalidOperationException("Sidecar WebSocket is not connected.");
        }

        string json = JsonSerializer.Serialize(request);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task StartTaskAsync(string taskId, string prompt, string? workDir = null, CancellationToken ct = default)
    {
        var req = new StartTaskRequest
        {
            TaskId = taskId,
            Prompt = prompt,
            WorkingDirectory = workDir
        };
        await SendAsync(req, ct);
    }

    public async Task AbortTaskAsync(string taskId, string? reason = null, CancellationToken ct = default)
    {
        var req = new AbortTaskRequest
        {
            TaskId = taskId,
            Reason = reason
        };
        await SendAsync(req, ct);
    }

    public async Task SendTerminalInputAsync(string sessionId, string data, CancellationToken ct = default)
    {
        var req = new TerminalInputRequest
        {
            SessionId = sessionId,
            Data = data
        };
        await SendAsync(req, ct);
    }

    public async Task ResizeTerminalAsync(string sessionId, int cols, int rows, CancellationToken ct = default)
    {
        var req = new TerminalResizeRequest
        {
            SessionId = sessionId,
            Cols = cols,
            Rows = rows
        };
        await SendAsync(req, ct);
    }

    private async Task SendPingAsync()
    {
        if (!IsConnected) return;
        try
        {
            _lastPingTimestamp = Stopwatch.GetTimestamp();
            await SendAsync(new PingRequest());
        }
        catch {}
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        OnDisconnected?.Invoke();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    using var reader = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
                    string rawJson = await reader.ReadToEndAsync(ct);
                    DispatchEvent(rawJson);
                }
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception)
        {
            OnDisconnected?.Invoke();
        }
    }

    private void DispatchEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("event", out var eventProp)) return;

            string eventType = eventProp.GetString() ?? string.Empty;

            switch (eventType)
            {
                case "chat_token":
                    var chatTokEv = JsonSerializer.Deserialize<ChatTokenEvent>(json);
                    if (chatTokEv != null)
                    {
                        OnChatToken?.Invoke(chatTokEv);
                        OnEventReceived?.Invoke(chatTokEv);
                    }
                    break;

                case "thinking":
                    var thinkingEv = JsonSerializer.Deserialize<ThinkingEvent>(json);
                    if (thinkingEv != null)
                    {
                        OnThinking?.Invoke(thinkingEv);
                        OnEventReceived?.Invoke(thinkingEv);
                    }
                    break;

                case "session_status":
                    var sessStatusEv = JsonSerializer.Deserialize<SessionStatusEvent>(json);
                    if (sessStatusEv != null)
                    {
                        OnSessionStatus?.Invoke(sessStatusEv);
                        OnEventReceived?.Invoke(sessStatusEv);
                    }
                    break;

                case "account_state":
                    var acctEv = JsonSerializer.Deserialize<AccountStateEvent>(json);
                    if (acctEv != null)
                    {
                        OnAccountState?.Invoke(acctEv);
                        OnEventReceived?.Invoke(acctEv);
                    }
                    break;

                case "agent_state":
                    var stateEv = JsonSerializer.Deserialize<AgentStateEvent>(json);
                    if (stateEv != null)
                    {
                        OnStateChanged?.Invoke(stateEv);
                        OnEventReceived?.Invoke(stateEv);
                    }
                    break;

                case "stream_delta":
                    var deltaEv = JsonSerializer.Deserialize<StreamDeltaEvent>(json);
                    if (deltaEv != null)
                    {
                        OnStreamDelta?.Invoke(deltaEv);
                        OnEventReceived?.Invoke(deltaEv);
                    }
                    break;

                case "tool_start":
                    var toolStartEv = JsonSerializer.Deserialize<ToolStartEvent>(json);
                    if (toolStartEv != null)
                    {
                        OnToolStart?.Invoke(toolStartEv);
                        OnEventReceived?.Invoke(toolStartEv);
                    }
                    break;

                case "tool_output":
                    var toolOutEv = JsonSerializer.Deserialize<ToolOutputEvent>(json);
                    if (toolOutEv != null)
                    {
                        OnToolOutput?.Invoke(toolOutEv);
                        OnEventReceived?.Invoke(toolOutEv);
                    }
                    break;

                case "task_completed":
                    var taskEv = JsonSerializer.Deserialize<TaskCompletedEvent>(json);
                    if (taskEv != null)
                    {
                        OnTaskCompleted?.Invoke(taskEv);
                        OnEventReceived?.Invoke(taskEv);
                    }
                    break;

                case "permission_denied":
                    var permEv = JsonSerializer.Deserialize<PermissionDeniedEvent>(json);
                    if (permEv != null)
                    {
                        OnEventReceived?.Invoke(permEv);
                    }
                    break;

                case "conversation_started":
                    var convEv = JsonSerializer.Deserialize<ConversationStartedEvent>(json);
                    if (convEv != null)
                    {
                        OnEventReceived?.Invoke(convEv);
                    }
                    break;

                case "terminal_data":
                    var termEv = JsonSerializer.Deserialize<TerminalDataEvent>(json);
                    if (termEv != null)
                    {
                        OnTerminalData?.Invoke(termEv);
                        OnEventReceived?.Invoke(termEv);
                    }
                    break;

                case "error":
                    var errEv = JsonSerializer.Deserialize<SidecarErrorEvent>(json);
                    if (errEv != null)
                    {
                        OnError?.Invoke(errEv);
                        OnEventReceived?.Invoke(errEv);
                    }
                    break;

                case "pong":
                    var pongEv = JsonSerializer.Deserialize<PongEvent>(json);
                    if (pongEv != null)
                    {
                        int latency = _lastPingTimestamp > 0
                            ? (int)Stopwatch.GetElapsedTime(_lastPingTimestamp).TotalMilliseconds
                            : 1;
                        LastPingLatencyMs = latency;
                        ActiveSessionsCount = pongEv.ActiveSessionsCount;
                        SidecarPid = pongEv.Pid;
                        OnPongReceived?.Invoke(pongEv, latency);
                        OnEventReceived?.Invoke(pongEv);
                    }
                    break;

                case "sidecar_status":
                    var statusEv = JsonSerializer.Deserialize<SidecarStatusEvent>(json);
                    if (statusEv != null)
                    {
                        ActiveSessionsCount = statusEv.ActiveSessionsCount;
                        SidecarPid = statusEv.Pid;
                        OnSidecarStatus?.Invoke(statusEv);
                        OnEventReceived?.Invoke(statusEv);
                    }
                    break;

                case "token_usage":
                    var tokEv = JsonSerializer.Deserialize<TokenUsageEvent>(json);
                    if (tokEv != null)
                    {
                        OnTokenUsage?.Invoke(tokEv);
                        OnEventReceived?.Invoke(tokEv);
                    }
                    break;

                case "quota_update":
                    var qEv = JsonSerializer.Deserialize<QuotaUpdateEvent>(json);
                    if (qEv != null)
                    {
                        OnQuotaUpdate?.Invoke(qEv);
                        OnEventReceived?.Invoke(qEv);
                    }
                    break;

                case "quota_check_completed":
                    var qcEv = JsonSerializer.Deserialize<QuotaCheckCompletedEvent>(json);
                    if (qcEv != null)
                    {
                        OnEventReceived?.Invoke(qcEv);
                    }
                    break;

                case "model_info":
                    var mEv = JsonSerializer.Deserialize<ModelInfoEvent>(json);
                    if (mEv != null)
                    {
                        OnModelInfo?.Invoke(mEv);
                        OnEventReceived?.Invoke(mEv);
                    }
                    break;
            }
        }
        catch {}
    }

    public async Task DisconnectAsync()
    {
        _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _connectionCts?.Cancel();
        OnStatusChanged?.Invoke(false);

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
                }
            }
            catch {}
            finally
            {
                _ws.Dispose();
                _ws = null;
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _heartbeatTimer.Dispose();
        _sendLock.Dispose();
        _connectionCts?.Dispose();
        _ws?.Dispose();
        GC.SuppressFinalize(this);
    }
}
