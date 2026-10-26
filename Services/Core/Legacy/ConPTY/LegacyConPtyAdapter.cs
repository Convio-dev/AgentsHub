#nullable enable
using System.Runtime.InteropServices;
using AgentsHub.Services.Core;

namespace AgentsHub.Core.Legacy.ConPTY;

/// <summary>
/// Legacy ConPTY adapter implementing IEngineAdapter as a fallback mechanism for Windows.
/// </summary>
[Obsolete("Legacy ConPTY terminal scraping is deprecated. Use SidecarEngineAdapter instead.", false)]
public class LegacyConPtyAdapter : IEngineAdapter
{
    private readonly Dictionary<string, LegacyConPtySession> _sessions = [];
    private readonly Lock _lock = new();

    public EngineMode Mode => EngineMode.LegacyConPTY;
    public bool IsReady => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public event Action<string, string>? OnStateChanged;
    public event Action<string, string>? OnStreamToken;
#pragma warning disable CS0067
    public event Action<string, string, bool>? OnToolExecuted;
#pragma warning restore CS0067
    public event Action<string, string>? OnTerminalData;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Legacy ConPTY adapter is exclusively supported on Windows.");
        }
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        lock (_lock)
        {
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
            _sessions.Clear();
        }
        return Task.CompletedTask;
    }

    public Task DispatchPromptAsync(string sessionId, string prompt, string? workDir = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                session = new LegacyConPtySession();
                session.OutputDataReceived += (data) =>
                {
                    OnTerminalData?.Invoke(sessionId, data);
                    OnStreamToken?.Invoke(sessionId, data);
                };
                session.Exited += (code) =>
                {
                    OnStateChanged?.Invoke(sessionId, code == 0 ? "completed" : "error");
                };

                string cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/sh";
                session.Start(cmd, workDir);
                _sessions[sessionId] = session;
            }

            OnStateChanged?.Invoke(sessionId, "executing");
            session.Write(prompt + "\r\n");
        }
        return Task.CompletedTask;
    }

    public Task AbortTaskAsync(string taskId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(taskId, out var session))
            {
                session.Write("\x03"); // SIGINT / Ctrl+C
            }
        }
        return Task.CompletedTask;
    }

    public Task SendTerminalInputAsync(string sessionId, string input)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Write(input);
            }
        }
        return Task.CompletedTask;
    }

    public Task ResizeTerminalAsync(string sessionId, int cols, int rows)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Resize((short)cols, (short)rows);
            }
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        ShutdownAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
