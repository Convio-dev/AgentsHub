#nullable enable
using AgentsHub.Services.Sidecar;

namespace AgentsHub.Services.Core;

public class SidecarEngineAdapter : IEngineAdapter
{
    private readonly SidecarManager _manager;
    private readonly SidecarClient _client;

    public EngineMode Mode => EngineMode.Sidecar;
    public bool IsReady => _manager.IsRunning && _client.IsConnected;

    public event Action<string, string>? OnStateChanged;
    public event Action<string, string>? OnStreamToken;
    public event Action<string, string, bool>? OnToolExecuted;
    public event Action<string, string>? OnTerminalData;

    public SidecarEngineAdapter()
    {
        _manager = new SidecarManager();
        _client = new SidecarClient(_manager);

        _client.OnStateChanged += (ev) =>
        {
            OnStateChanged?.Invoke(ev.TaskId ?? "global", ev.State);
        };

        _client.OnStreamDelta += (ev) =>
        {
            OnStreamToken?.Invoke(ev.TaskId, ev.Text);
        };

        _client.OnToolOutput += (ev) =>
        {
            OnToolExecuted?.Invoke(ev.TaskId ?? ev.SessionId ?? "global", ev.Tool, ev.Success);
        };

        _client.OnTerminalData += (ev) =>
        {
            OnTerminalData?.Invoke(ev.SessionId, ev.Data);
        };
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _manager.StartAsync(ct);
        await _client.ConnectAsync(ct);
    }

    public async Task ShutdownAsync()
    {
        await _client.DisconnectAsync();
        _manager.Stop();
    }

    public async Task DispatchPromptAsync(string sessionId, string prompt, string? workDir = null, CancellationToken ct = default)
    {
        await _client.StartTaskAsync(sessionId, prompt, workDir, ct);
    }

    public async Task AbortTaskAsync(string taskId)
    {
        await _client.AbortTaskAsync(taskId);
    }

    public async Task SendTerminalInputAsync(string sessionId, string input)
    {
        await _client.SendTerminalInputAsync(sessionId, input);
    }

    public async Task ResizeTerminalAsync(string sessionId, int cols, int rows)
    {
        await _client.ResizeTerminalAsync(sessionId, cols, rows);
    }

    public void Dispose()
    {
        _client.Dispose();
        _manager.Dispose();
        GC.SuppressFinalize(this);
    }
}
