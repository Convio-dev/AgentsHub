using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HermesHub.Services.ConPty;

public class ConPtySession : IDisposable
{
    private IntPtr _hPC = IntPtr.Zero;
    private IntPtr _hProcess = IntPtr.Zero;
    private IntPtr _hThread = IntPtr.Zero;
    private int _processId;

    private FileStream? _inWriter;
    private FileStream? _outReader;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    private readonly Lock _syncLock = new();
    private bool _isDisposed;
    private bool _isRunning;

    public int ProcessId => _processId;
    public bool IsRunning => _isRunning;

    public event Action<string>? OutputDataReceived;
    public event Action<int>? Exited;

    public bool Start(string commandLine, string? workingDirectory = null, short cols = 120, short rows = 32, IDictionary<string, string>? environmentVariables = null)
    {
        lock (_syncLock)
        {
            if (_isRunning)
            {
                return false;
            }

            try
            {
                var sa = new NativePty.SECURITY_ATTRIBUTES();
                sa.nLength = Marshal.SizeOf(sa);
                sa.bInheritHandle = 1;

                if (!NativePty.CreatePipe(out var inPipeRead, out var inPipeWrite, ref sa, 0) ||
                    !NativePty.CreatePipe(out var outPipeRead, out var outPipeWrite, ref sa, 0))
                {
                    throw new InvalidOperationException("Failed to create anonymous pipes for ConPTY.");
                }

                var size = new NativePty.COORD { X = cols, Y = rows };
                int hr = NativePty.CreatePseudoConsole(size, inPipeRead, outPipeWrite, 0, out _hPC);
                if (hr != 0)
                {
                    inPipeRead.Dispose();
                    inPipeWrite.Dispose();
                    outPipeRead.Dispose();
                    outPipeWrite.Dispose();
                    throw new InvalidOperationException($"CreatePseudoConsole failed with HRESULT 0x{hr:X8}");
                }

                // In PseudoConsole architecture, child owns the read end of input and write end of output.
                // Closing handles in parent ensures EOF signals correctly when child exits.
                inPipeRead.Dispose();
                outPipeWrite.Dispose();

                _inWriter = new FileStream(inPipeWrite, FileAccess.Write, 4096, false);
                _outReader = new FileStream(outPipeRead, FileAccess.Read, 4096, false);

                IntPtr attrListSize = IntPtr.Zero;
                NativePty.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
                IntPtr attrList = Marshal.AllocHGlobal(attrListSize);

                try
                {
                    if (!NativePty.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
                    {
                        throw new InvalidOperationException("Failed to initialize proc thread attribute list.");
                    }

                    if (!NativePty.UpdateProcThreadAttribute(
                        attrList,
                        0,
                        (IntPtr)NativePty.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                        _hPC,
                        (IntPtr)IntPtr.Size,
                        IntPtr.Zero,
                        IntPtr.Zero))
                    {
                        throw new InvalidOperationException("Failed to bind pseudo console to proc thread attribute list.");
                    }

                    var siex = new NativePty.STARTUPINFOEX();
                    siex.StartupInfo.cb = Marshal.SizeOf<NativePty.STARTUPINFOEX>();
                    siex.lpAttributeList = attrList;

                    var targetDir = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;

                    uint creationFlags = NativePty.EXTENDED_STARTUPINFO_PRESENT;
                    IntPtr lpEnv = IntPtr.Zero;
                    if (environmentVariables != null && environmentVariables.Count > 0)
                    {
                        creationFlags |= NativePty.CREATE_UNICODE_ENVIRONMENT;
                        lpEnv = CreateUnicodeEnvironmentBlock(environmentVariables);
                    }

                    try
                    {
                        bool success = NativePty.CreateProcess(
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            creationFlags,
                            lpEnv,
                            targetDir,
                            ref siex,
                            out var pi);

                        if (!success)
                        {
                            int err = Marshal.GetLastWin32Error();
                            throw new InvalidOperationException($"CreateProcess failed for command '{commandLine}'. Win32 Error: {err}");
                        }

                        _hProcess = pi.hProcess;
                        _hThread = pi.hThread;
                        _processId = pi.dwProcessId;
                        _isRunning = true;
                    }
                    finally
                    {
                        if (lpEnv != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(lpEnv);
                        }
                    }
                }
                finally
                {
                    NativePty.DeleteProcThreadAttributeList(attrList);
                    Marshal.FreeHGlobal(attrList);
                }

                _readCts = new CancellationTokenSource();
                _readTask = Task.Run(() => ReadOutputLoopAsync(_outReader, _readCts.Token));

                // Process exit monitoring
                _ = Task.Run(() => MonitorProcessExitAsync(_processId));

                return true;
            }
            catch (Exception)
            {
                CleanupInternal();
                throw;
            }
        }
    }

    public void Write(string data)
    {
        lock (_syncLock)
        {
            if (!_isRunning || _inWriter == null)
            {
                return;
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                _inWriter.Write(bytes, 0, bytes.Length);
                _inWriter.Flush();
            }
            catch
            {
                // Pipe write errors handled on process termination
            }
        }
    }

    public void Resize(short cols, short rows)
    {
        lock (_syncLock)
        {
            if (!_isRunning || _hPC == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var size = new NativePty.COORD { X = cols, Y = rows };
                _ = NativePty.ResizePseudoConsole(_hPC, size);
            }
            catch
            {
                // Ignore transient resize errors
            }
        }
    }

    private void ReadOutputLoopAsync(FileStream reader, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = reader.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    break;
                }

                string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                OutputDataReceived?.Invoke(text);
            }
        }
        catch
        {
            // Termination of stream indicates child process exited
        }
    }

    private async Task MonitorProcessExitAsync(int pid)
    {
        await Task.Run(() =>
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.WaitForExit();
                int exitCode = proc.ExitCode;

                lock (_syncLock)
                {
                    _isRunning = false;
                }

                Exited?.Invoke(exitCode);
            }
            catch
            {
                lock (_syncLock)
                {
                    _isRunning = false;
                }
                Exited?.Invoke(-1);
            }
        });
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isRunning && _hPC == IntPtr.Zero)
            {
                return;
            }

            CleanupInternal();
        }
    }

    private void CleanupInternal()
    {
        _isRunning = false;
        _readCts?.Cancel();

        try
        {
            if (_processId > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(_processId);
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore process handle query errors on exit
                }
            }

            if (_hPC != IntPtr.Zero)
            {
                NativePty.ClosePseudoConsole(_hPC);
                _hPC = IntPtr.Zero;
            }

            if (_hThread != IntPtr.Zero)
            {
                NativePty.CloseHandle(_hThread);
                _hThread = IntPtr.Zero;
            }

            if (_hProcess != IntPtr.Zero)
            {
                NativePty.CloseHandle(_hProcess);
                _hProcess = IntPtr.Zero;
            }

            _inWriter?.Dispose();
            _inWriter = null;

            _outReader?.Dispose();
            _outReader = null;
        }
        catch
        {
            // Suppress cleanup exceptions
        }
    }

    private static IntPtr CreateUnicodeEnvironmentBlock(IDictionary<string, string> customVars)
    {
        var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var k = entry.Key?.ToString();
            var v = entry.Value?.ToString();
            if (!string.IsNullOrEmpty(k) && v != null)
            {
                env[k] = v;
            }
        }
        foreach (var kvp in customVars)
        {
            if (string.IsNullOrEmpty(kvp.Value))
            {
                env.Remove(kvp.Key);
            }
            else
            {
                env[kvp.Key] = kvp.Value;
            }
        }
        var sb = new StringBuilder();
        foreach (var kvp in env)
        {
            sb.Append(kvp.Key).Append('=').Append(kvp.Value).Append('\0');
        }
        sb.Append('\0');
        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        _readCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
