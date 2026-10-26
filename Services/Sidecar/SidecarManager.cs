#nullable enable
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AgentsHub.Services.Sidecar;

public class SidecarManager : IDisposable
{
    private Process? _process;
    private int _port = 0;
    private string _authToken = string.Empty;
    private readonly Lock _lock = new();

    public int Port => _port;
    public string AuthToken => _authToken;
    public bool IsRunning => _process != null && !_process.HasExited && _port > 0;
    public int ProcessId => _process?.Id ?? 0;
    public string WebSocketUrl => $"ws://127.0.0.1:{_port}/ws?token={_authToken}";
    public string HttpHealthUrl => $"http://127.0.0.1:{_port}/health";

    public event Action<string>? OnLogReceived;
    public event Action? OnExited;

    /// <summary>
    /// Resolves the sidecar binary path for the active platform and processor architecture
    /// </summary>
    public static string ResolveBinaryPath(string? customBasePath = null)
    {
        string baseDir = customBasePath ?? AppContext.BaseDirectory;
        string rootDir = Directory.GetCurrentDirectory();

        string relativeSubPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            relativeSubPath = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Path.Combine("bin", "win-arm64", "AgentsEngine.exe")
                : Path.Combine("bin", "win-x64", "AgentsEngine.exe");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            relativeSubPath = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Path.Combine("bin", "osx-arm64", "AgentsEngine")
                : Path.Combine("bin", "osx-x64", "AgentsEngine");
        }
        else // Linux & others
        {
            relativeSubPath = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Path.Combine("bin", "linux-arm64", "AgentsEngine")
                : Path.Combine("bin", "linux-x64", "AgentsEngine");
        }

        // Search candidates: current working dir, base dir, parent directories
        string[] candidates = new[]
        {
            Path.Combine(rootDir, relativeSubPath),
            Path.Combine(baseDir, relativeSubPath),
            Path.Combine(baseDir, Path.GetFileName(relativeSubPath)),
            Path.Combine(rootDir, "dist", relativeSubPath)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        // Return standard expected path even if missing for helpful error messaging
        return Path.GetFullPath(candidates[0]);
    }

    /// <summary>
    /// Starts the sidecar process and waits asynchronously until the readiness handshake is received
    /// </summary>
    public async Task<int> StartAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (IsRunning) return _port;
        }

        string binaryPath = ResolveBinaryPath();
        if (!File.Exists(binaryPath))
        {
            throw new FileNotFoundException(
                $"Sidecar engine binary not found at '{binaryPath}'. Run 'build-sidecar.cmd' or 'bun run build.ts' to compile the runtime.",
                binaryPath);
        }

        SidecarProcessWatchdog.EnsureExecutablePermission(binaryPath);

        // Generate cryptographic token
        byte[] tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        _authToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        var tcsReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = $"--port=0 --token={_authToken} --headless",
            WorkingDirectory = Path.GetDirectoryName(binaryPath) ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["AGENTS_SIDECAR_TOKEN"] = _authToken;

        var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            OnLogReceived?.Invoke(e.Data);

            // Handshake matching: [AGENTS_ENGINE_READY] port=XXXX pid=YYYY token=ZZZZ
            if (!tcsReady.Task.IsCompleted && e.Data.Contains("[AGENTS_ENGINE_READY]"))
            {
                var match = Regex.Match(e.Data, @"port=(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int readyPort))
                {
                    _port = readyPort;
                    tcsReady.TrySetResult(readyPort);
                }
            }
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                OnLogReceived?.Invoke($"[Sidecar Error] {e.Data}");
            }
        };

        proc.Exited += (_, _) =>
        {
            _port = 0;
            tcsReady.TrySetException(new InvalidOperationException($"Sidecar process exited unexpectedly with code {proc.ExitCode}"));
            OnExited?.Invoke();
        };

        if (!proc.Start())
        {
            throw new InvalidOperationException($"Failed to launch sidecar process: '{binaryPath}'");
        }

        _process = proc;
        SidecarProcessWatchdog.AttachProcess(proc);

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Wait for handshake with timeout
        using var registration = ct.Register(() => tcsReady.TrySetCanceled());
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await using (linkedCts.Token.Register(() => tcsReady.TrySetException(new TimeoutException("Timed out waiting for sidecar engine readiness handshake."))))
            {
                return await tcsReady.Task;
            }
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        // Signal stdin EOF to initiate clean shutdown handler
                        _process.StandardInput.Close();
                        if (!_process.WaitForExit(1500))
                        {
                            _process.Kill(true);
                        }
                    }
                }
                catch {}
                finally
                {
                    _process.Dispose();
                    _process = null;
                    _port = 0;
                }
            }
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
