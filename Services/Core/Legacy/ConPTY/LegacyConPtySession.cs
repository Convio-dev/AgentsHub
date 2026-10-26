#nullable enable
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgentsHub.Core.Legacy.ConPTY;

/// <summary>
/// Deprecated legacy Windows ConPTY terminal session.
/// Preserved exclusively as a fallback mode on Windows platforms.
/// </summary>
[Obsolete("Legacy ConPTY terminal scraping is deprecated. Use SidecarEngineAdapter instead.", false)]
public class LegacyConPtySession : IDisposable
{
#if WINDOWS
    private IntPtr _hPC = IntPtr.Zero;
    private IntPtr _hProcess = IntPtr.Zero;
    private IntPtr _hThread = IntPtr.Zero;
    private FileStream? _inWriter;
    private FileStream? _outReader;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
#endif
    private int _processId;

    private readonly Lock _syncLock = new();
    private bool _isDisposed;
    private bool _isRunning;

    public int ProcessId => _processId;
    public bool IsRunning => _isRunning;

    public event Action<string>? OutputDataReceived;
    public event Action<int>? Exited;

    private Process? _posixProcess;

    public bool Start(string commandLine, string? workingDirectory = null, short cols = 120, short rows = 32, IDictionary<string, string>? environmentVariables = null)
    {
        lock (_syncLock)
        {
            if (_isRunning)
            {
                return false;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return StartPosixFallback(commandLine, workingDirectory, environmentVariables);
            }

#if WINDOWS
            return StartWindowsConPty(commandLine, workingDirectory, cols, rows, environmentVariables);
#else
            return StartPosixFallback(commandLine, workingDirectory, environmentVariables);
#endif
        }
    }

    private bool StartPosixFallback(string commandLine, string? workingDirectory, IDictionary<string, string>? environmentVariables)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{commandLine.Replace("\"", "\\\"")}\"",
                WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    psi.Environment[kvp.Key] = kvp.Value;
                }
            }

            _posixProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _posixProcess.OutputDataReceived += (_, e) => { if (e.Data != null) OutputDataReceived?.Invoke(e.Data + "\r\n"); };
            _posixProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) OutputDataReceived?.Invoke(e.Data + "\r\n"); };
            _posixProcess.Exited += (_, _) =>
            {
                _isRunning = false;
                Exited?.Invoke(_posixProcess?.ExitCode ?? 0);
            };

            if (_posixProcess.Start())
            {
                _processId = _posixProcess.Id;
                _isRunning = true;
                _posixProcess.BeginOutputReadLine();
                _posixProcess.BeginErrorReadLine();
                return true;
            }
        }
        catch {}
        return false;
    }

#if WINDOWS
    private bool StartWindowsConPty(string commandLine, string? workingDirectory, short cols, short rows, IDictionary<string, string>? environmentVariables)
    {
        SafeFileHandle? hPipeIn = null;
        SafeFileHandle? hPipeOut = null;
        SafeFileHandle? hPipePtyIn = null;
        SafeFileHandle? hPipePtyOut = null;

        try
        {
            var sa = new Services.ConPty.NativePty.SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<Services.ConPty.NativePty.SECURITY_ATTRIBUTES>(),
                bInheritHandle = 1,
                lpSecurityDescriptor = IntPtr.Zero
            };

            if (!Services.ConPty.NativePty.CreatePipe(out hPipePtyIn, out hPipeIn, ref sa, 0) ||
                !Services.ConPty.NativePty.CreatePipe(out hPipeOut, out hPipePtyOut, ref sa, 0))
            {
                return false;
            }

            var coord = new Services.ConPty.NativePty.COORD { X = cols, Y = rows };
            int hr = Services.ConPty.NativePty.CreatePseudoConsole(coord, hPipePtyIn, hPipePtyOut, 0, out _hPC);
            if (hr != 0 || _hPC == IntPtr.Zero)
            {
                return false;
            }

            hPipePtyIn.Dispose();
            hPipePtyOut.Dispose();

            _inWriter = new FileStream(hPipeIn, FileAccess.Write, 4096, false);
            _outReader = new FileStream(hPipeOut, FileAccess.Read, 4096, false);

            if (!LaunchWindowsProcess(commandLine, workingDirectory, environmentVariables))
            {
                return false;
            }

            _isRunning = true;
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_outReader, _readCts.Token));

            return true;
        }
        catch
        {
            Cleanup();
            return false;
        }
    }

    private bool LaunchWindowsProcess(string commandLine, string? workingDirectory, IDictionary<string, string>? environmentVariables)
    {
        var startupInfo = new Services.ConPty.NativePty.STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<Services.ConPty.NativePty.STARTUPINFOEX>();

        IntPtr lpSize = IntPtr.Zero;
        Services.ConPty.NativePty.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
        startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);
        Services.ConPty.NativePty.InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref lpSize);

        Services.ConPty.NativePty.UpdateProcThreadAttribute(
            startupInfo.lpAttributeList,
            0,
            (IntPtr)Services.ConPty.NativePty.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            _hPC,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero);

        var processInfo = new Services.ConPty.NativePty.PROCESS_INFORMATION();
        uint creationFlags = Services.ConPty.NativePty.EXTENDED_STARTUPINFO_PRESENT | Services.ConPty.NativePty.CREATE_UNICODE_ENVIRONMENT;

        IntPtr pEnv = IntPtr.Zero;
        if (environmentVariables != null && environmentVariables.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var kvp in environmentVariables)
            {
                sb.Append(kvp.Key).Append('=').Append(kvp.Value).Append('\0');
            }
            sb.Append('\0');
            pEnv = Marshal.StringToHGlobalUni(sb.ToString());
        }

        try
        {
            bool success = Services.ConPty.NativePty.CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                pEnv,
                workingDirectory,
                ref startupInfo,
                out processInfo);

            if (!success)
            {
                return false;
            }

            _hProcess = processInfo.hProcess;
            _hThread = processInfo.hThread;
            _processId = processInfo.dwProcessId;

            Task.Run(() => WaitForWindowsProcessExit());
            return true;
        }
        finally
        {
            if (pEnv != IntPtr.Zero) Marshal.FreeHGlobal(pEnv);
            if (startupInfo.lpAttributeList != IntPtr.Zero)
            {
                Services.ConPty.NativePty.DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
                Marshal.FreeHGlobal(startupInfo.lpAttributeList);
            }
        }
    }

    private void WaitForWindowsProcessExit()
    {
        if (_processId <= 0) return;
        try
        {
            using var proc = Process.GetProcessById(_processId);
            proc.WaitForExit();
            int exitCode = proc.ExitCode;
            _isRunning = false;
            Exited?.Invoke(exitCode);
        }
        catch
        {
            _isRunning = false;
            Exited?.Invoke(-1);
        }
    }
#endif

    public void Write(string data)
    {
        lock (_syncLock)
        {
            if (!_isRunning) return;

            if (_posixProcess != null)
            {
                try
                {
                    _posixProcess.StandardInput.Write(data);
                    _posixProcess.StandardInput.Flush();
                }
                catch {}
                return;
            }

#if WINDOWS
            if (_inWriter == null) return;

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                _inWriter.Write(bytes, 0, bytes.Length);
                _inWriter.Flush();
            }
            catch {}
#endif
        }
    }

    public void Resize(short cols, short rows)
    {
#if WINDOWS
        lock (_syncLock)
        {
            if (!_isRunning || _hPC == IntPtr.Zero) return;
            var coord = new Services.ConPty.NativePty.COORD { X = cols, Y = rows };
            Services.ConPty.NativePty.ResizePseudoConsole(_hPC, coord);
        }
#endif
    }

#if WINDOWS
    private async Task ReadLoop(FileStream reader, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (bytesRead <= 0) break;

                string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                OutputDataReceived?.Invoke(text);
            }
        }
        catch {}
    }
#endif

    private void Cleanup()
    {
#if WINDOWS
        _readCts?.Cancel();
        _inWriter?.Dispose();
        _outReader?.Dispose();

        if (_hPC != IntPtr.Zero)
        {
            Services.ConPty.NativePty.ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }
        if (_hProcess != IntPtr.Zero)
        {
            Services.ConPty.NativePty.CloseHandle(_hProcess);
            _hProcess = IntPtr.Zero;
        }
        if (_hThread != IntPtr.Zero)
        {
            Services.ConPty.NativePty.CloseHandle(_hThread);
            _hThread = IntPtr.Zero;
        }
#endif
        _isRunning = false;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Cleanup();
            GC.SuppressFinalize(this);
        }
    }
}
