#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AgentsHub.Services.Sidecar;

/// <summary>
/// Cross-platform process watchdog ensuring child sidecar engine processes are terminated
/// immediately when the parent host terminates or crashes (eliminates zombie processes).
/// </summary>
public static class SidecarProcessWatchdog
{
    private static readonly List<Process> TrackedProcesses = [];
    private static readonly Lock SyncLock = new();

#if WINDOWS
    private static readonly IntPtr _jobHandle = IntPtr.Zero;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }
#endif

    static SidecarProcessWatchdog()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAllTracked();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KillAllTracked();

#if WINDOWS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (_jobHandle != IntPtr.Zero)
                {
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(info, extendedInfoPtr, false);
                        SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(extendedInfoPtr);
                    }
                }
            }
            catch {}
        }
#endif
    }

    /// <summary>
    /// Attaches an operating system watchdog to the spawned sidecar process
    /// </summary>
    public static void AttachProcess(Process process)
    {
        lock (SyncLock)
        {
            TrackedProcesses.Add(process);
        }

#if WINDOWS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _jobHandle != IntPtr.Zero)
        {
            try
            {
                AssignProcessToJobObject(_jobHandle, process.Handle);
            }
            catch {}
        }
#endif
    }

    /// <summary>
    /// Ensures POSIX executable permission (chmod +x) on Linux and macOS
    /// </summary>
    public static void EnsureExecutablePermission(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(3000);
        }
        catch {}
    }

    public static void KillAllTracked()
    {
        lock (SyncLock)
        {
            foreach (var proc in TrackedProcesses)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                    }
                }
                catch {}
            }
            TrackedProcesses.Clear();
        }
    }
}
