#nullable enable
using System.Diagnostics;

namespace AgentsHub.Services;

/// <summary>
/// Runs one interactive install command in a real pseudo-console so the user can answer its prompts.
///
/// `npx` packages routinely ask questions (press space to select, enter to confirm), and a detached
/// process with piped stdio cannot answer them — the install just hangs. This keeps a ConPTY session
/// alive, streams its output to the UI, and forwards keystrokes back into it.
/// </summary>
public static class NpxInstallHost
{
    private static readonly Lock Gate = new();
    private static ConPty.ConPtySession? _session;
    private static string _commandLine = string.Empty;

    public static bool IsRunning
    {
        get
        {
            lock (Gate) return _session?.IsRunning == true;
        }
    }

    public static string CurrentCommand
    {
        get
        {
            lock (Gate) return _commandLine;
        }
    }

    public static event Action<string>? OutputReceived;
    public static event Action<int>? Exited;

    /// <summary>Starts a command, replacing any session that is already running.</summary>
    public static bool Start(string commandLine, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;

        lock (Gate)
        {
            DisposeLocked();

            try
            {
                var session = new ConPty.ConPtySession();

                session.OutputDataReceived += text => OutputReceived?.Invoke(text);
                session.Exited += code =>
                {
                    lock (Gate)
                    {
                        _session = null;
                        _commandLine = string.Empty;
                    }
                    Exited?.Invoke(code);
                };

                // ConPTY needs an actual executable to launch: a bare "npx ..." is not a valid command
                // line, which is why the first attempt failed instantly.
                string shellCommand = OperatingSystem.IsWindows()
                    ? "cmd.exe /c " + commandLine
                    : "/bin/sh -lc \"" + commandLine + "\"";

                bool started = session.Start(shellCommand, workingDirectory);
                if (!started)
                {
                    session.Dispose();
                    return false;
                }

                _session = session;
                _commandLine = commandLine;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NpxInstallHost] Failed to start '{commandLine}': {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Forwards raw keystrokes (including \r for enter) into the running command.</summary>
    public static void Write(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        lock (Gate)
        {
            try { _session?.Write(data); } catch { }
        }
    }

    public static void Stop()
    {
        lock (Gate) DisposeLocked();
    }

    private static void DisposeLocked()
    {
        try { _session?.Stop(); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        _commandLine = string.Empty;
    }
}
