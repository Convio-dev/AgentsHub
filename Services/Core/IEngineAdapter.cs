#nullable enable

namespace AgentsHub.Services.Core;

public enum EngineMode
{
    Sidecar,
    LegacyConPTY
}

/// <summary>
/// Unified abstraction for agent execution engines.
/// Implemented by high-performance Cross-Platform SidecarEngineAdapter and legacy Windows LegacyConPtyAdapter.
/// </summary>
public interface IEngineAdapter : IDisposable
{
    EngineMode Mode { get; }
    bool IsReady { get; }

    event Action<string, string>? OnStateChanged; // taskId, state
    event Action<string, string>? OnStreamToken;   // taskId, delta
    event Action<string, string, bool>? OnToolExecuted; // taskId, tool, success
    event Action<string, string>? OnTerminalData; // sessionId, data

    Task InitializeAsync(CancellationToken ct = default);
    Task ShutdownAsync();

    Task DispatchPromptAsync(string sessionId, string prompt, string? workDir = null, CancellationToken ct = default);
    Task AbortTaskAsync(string taskId);
    Task SendTerminalInputAsync(string sessionId, string input);
    Task ResizeTerminalAsync(string sessionId, int cols, int rows);
}
