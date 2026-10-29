using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace AgentsHub.Services;

public enum HubStatus
{
    Offline,
    Starting,
    Online,
    Error
}

public class HubProcessService : IDisposable
{
    public const int DefaultHubPort = 9119;
    public int Port { get; set; } = DefaultHubPort;
    private Process? _spawnedProcess;
    private readonly CancellationTokenSource _cts = new();
    private HubStatus _status = HubStatus.Offline;
    private string _statusMessage = "Offline";
    private DateTime? _startRequestedTime;

    private readonly List<string> _logHistory = [];
    private readonly Lock _logLock = new();
    private const int MaxLogEntries = 600;

    public HubStatus CurrentStatus => _status;
    public string StatusMessage => _statusMessage;

    public event Action<HubStatus, string>? StatusChanged;
    public event Action<string>? LogMessage;

    public HubProcessService(int port = DefaultHubPort)
    {
        Port = port;
        _ = StartMonitoringLoopAsync(_cts.Token);
    }

    public void AddLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_logLock)
        {
            _logHistory.Add(timestamped);
            if (_logHistory.Count > MaxLogEntries)
            {
                _logHistory.RemoveAt(0);
            }
        }
        LogMessage?.Invoke(timestamped);
    }

    public IReadOnlyList<string> GetLogHistory()
    {
        lock (_logLock)
        {
            return [.. _logHistory];
        }
    }

    public void ClearLogs()
    {
        lock (_logLock)
        {
            _logHistory.Clear();
        }
    }

    public void Start()
    {
        if (_status == HubStatus.Online || _status == HubStatus.Starting)
        {
            return;
        }

        UpdateStatus(HubStatus.Starting, $"Initiating hub dashboard on port {Port}...");
        _startRequestedTime = DateTime.UtcNow;
        AddLog($"Initiating Hub dashboard on port {Port}...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"hub dashboard --port {Port}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _spawnedProcess = Process.Start(psi);
            if (_spawnedProcess != null)
            {
                _spawnedProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        AddLog(e.Data);
                    }
                };
                _spawnedProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        AddLog($"[stderr] {e.Data}");
                    }
                };
                _spawnedProcess.BeginOutputReadLine();
                _spawnedProcess.BeginErrorReadLine();
                AddLog($"Hub background process spawned (PID: {_spawnedProcess.Id}).");
            }
        }
        catch (Exception ex)
        {
            AddLog($"Failed to spawn Hub: {ex.Message}");
            UpdateStatus(HubStatus.Error, $"Launch failed: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        UpdateStatus(HubStatus.Offline, "Stopping Hub service...");
        AddLog("Stop command issued. Terminating Hub process tree...");

        await Task.Run(() =>
        {
            try
            {
                if (_spawnedProcess != null && !_spawnedProcess.HasExited)
                {
                    _spawnedProcess.Kill(entireProcessTree: true);
                    _spawnedProcess.Dispose();
                    _spawnedProcess = null;
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error stopping spawned process: {ex.Message}");
            }

            // Clean up any remaining Hub processes
            KillProcessesByName("hub");
            KillProcessesByName("Hub");

            // Terminate any listening process holding the target port
            KillProcessOnPort(Port);
        });

        // Verify port closure
        var isAlive = await CheckPortAliveAsync(Port, 500);
        if (!isAlive)
        {
            UpdateStatus(HubStatus.Offline, "Engine: Offline");
        }
        else
        {
            UpdateStatus(HubStatus.Online, $"Engine: Running on :{Port} (External process)");
        }
    }

    private static void KillProcessesByName(string processName)
    {
        try
        {
            int currentPid = Environment.ProcessId;
            var currentProcName = Process.GetCurrentProcess().ProcessName;

            // Never kill ourselves or any process with the same name as our host application
            if (string.Equals(processName, currentProcName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var processes = Process.GetProcessesByName(processName);
            foreach (var proc in processes)
            {
                if (proc.Id == currentPid) continue;
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.Dispose();
                }
                catch
                {
                    // Ignore individual termination access issues
                }
            }
        }
        catch
        {
            // Ignore process query errors
        }
    }

    private static void KillProcessOnPort(int port)
    {
        try
        {
            int currentPid = Environment.ProcessId;
            var psi = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p tcp",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            var portSuffix = $":{port}";
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                if (!trimmed.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[1].EndsWith(portSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(parts[4], out int pid) && pid > 4 && pid != currentPid)
                    {
                        try
                        {
                            using var targetProc = Process.GetProcessById(pid);
                            if (targetProc.Id != currentPid)
                            {
                                targetProc.Kill(entireProcessTree: true);
                            }
                        }
                        catch
                        {
                            // Process might have already exited
                        }
                    }
                }
            }
        }
        catch
        {
            // Suppress port termination exceptions
        }
    }

    public static async Task<bool> CheckPortAliveAsync(int port = DefaultHubPort, int timeoutMs = 500)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(timeoutMs);
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

    private async Task StartMonitoringLoopAsync(CancellationToken token)
    {
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(1500));

        while (!token.IsCancellationRequested)
        {
            try
            {
                await periodicTimer.WaitForNextTickAsync(token);
                var isPortOpen = await CheckPortAliveAsync(Port, 600);

                if (_status == HubStatus.Starting)
                {
                    if (isPortOpen)
                    {
                        UpdateStatus(HubStatus.Online, $"Engine: Running on :{Port}");
                    }
                    else if (_startRequestedTime.HasValue &&
                             (DateTime.UtcNow - _startRequestedTime.Value).TotalSeconds > 30)
                    {
                        UpdateStatus(HubStatus.Error, $"Engine: Start timed out (:{Port} not responding)");
                    }
                }
                else if (isPortOpen && _status != HubStatus.Online)
                {
                    UpdateStatus(HubStatus.Online, $"Engine: Running on :{Port}");
                }
                else if (!isPortOpen && _status == HubStatus.Online)
                {
                    UpdateStatus(HubStatus.Offline, "Engine: Offline");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue heartbeat loop on transient errors
            }
        }
    }

    private void UpdateStatus(HubStatus newStatus, string message)
    {
        var changed = _status != newStatus;
        _status = newStatus;
        _statusMessage = message;
        if (changed)
        {
            AddLog($"Status update: {newStatus} ({message})");
        }
        StatusChanged?.Invoke(newStatus, message);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        try
        {
            if (_spawnedProcess != null && !_spawnedProcess.HasExited)
            {
                _spawnedProcess.Kill(entireProcessTree: true);
                _spawnedProcess.Dispose();
            }
        }
        catch
        {
            // Suppress cleanup exceptions on process shutdown
        }

        GC.SuppressFinalize(this);
    }
}
