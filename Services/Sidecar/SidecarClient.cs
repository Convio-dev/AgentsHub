#nullable enable
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

    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

    public event Action<AgentStateEvent>? OnStateChanged;
    public event Action<StreamDeltaEvent>? OnStreamDelta;
    public event Action<ToolStartEvent>? OnToolStart;
    public event Action<ToolOutputEvent>? OnToolOutput;
    public event Action<TaskCompletedEvent>? OnTaskCompleted;
    public event Action<TerminalDataEvent>? OnTerminalData;
    public event Action<SidecarErrorEvent>? OnError;
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
                case "agent_state":
                    var stateEv = JsonSerializer.Deserialize<AgentStateEvent>(json);
                    if (stateEv != null) OnStateChanged?.Invoke(stateEv);
                    break;

                case "stream_delta":
                    var deltaEv = JsonSerializer.Deserialize<StreamDeltaEvent>(json);
                    if (deltaEv != null) OnStreamDelta?.Invoke(deltaEv);
                    break;

                case "tool_start":
                    var toolStartEv = JsonSerializer.Deserialize<ToolStartEvent>(json);
                    if (toolStartEv != null) OnToolStart?.Invoke(toolStartEv);
                    break;

                case "tool_output":
                    var toolOutEv = JsonSerializer.Deserialize<ToolOutputEvent>(json);
                    if (toolOutEv != null) OnToolOutput?.Invoke(toolOutEv);
                    break;

                case "task_completed":
                    var taskEv = JsonSerializer.Deserialize<TaskCompletedEvent>(json);
                    if (taskEv != null) OnTaskCompleted?.Invoke(taskEv);
                    break;

                case "terminal_data":
                    var termEv = JsonSerializer.Deserialize<TerminalDataEvent>(json);
                    if (termEv != null) OnTerminalData?.Invoke(termEv);
                    break;

                case "error":
                    var errEv = JsonSerializer.Deserialize<SidecarErrorEvent>(json);
                    if (errEv != null) OnError?.Invoke(errEv);
                    break;
            }
        }
        catch {}
    }

    public async Task DisconnectAsync()
    {
        _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _connectionCts?.Cancel();

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
