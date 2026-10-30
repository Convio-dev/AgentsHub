using System.Diagnostics;
using System.Runtime.InteropServices;
using AgentsHub.Services.ConPty;

namespace AgentsHub.Services.Vyce;

/// <summary>
/// A console that Vyce will actually run in.
///
/// Vyce is a full-screen TUI and refuses to start unless it owns a real console: a pseudo-console
/// is not enough, because the child inherits the parent's standard handles and Node reports
/// <c>process.stdin.isTTY === undefined</c> inside it. So the process is given its own console
/// (hidden at creation) and the keys arrive through the Win32 console input buffer, which is what
/// a real keyboard writes to as well.
///
/// That leaves one process-wide side effect to respect: <c>AttachConsole</c> only succeeds while
/// the caller has no console, so every keystroke batch attaches, writes, and detaches again.
/// </summary>
internal sealed class VyceTty(string logPath) : IDisposable
{
    private const uint CreateNewConsole = 0x00000010;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseShowWindow = 0x00000001;
    private const short SwHide = 0;

    private const ushort KeyEventType = 0x0001;

    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;

    private Process? _process;
    private ConPtySession? _pty;
    private int _consolePid;
    private bool _disposed;

    /// <summary>The CLI is still running, so the next turn can reuse the console.</summary>
    public bool IsRunning
    {
        get
        {
            if (_pty is not null) return _pty.IsRunning;
            if (_consolePid <= 0) return false;

            try
            {
                using var process = Process.GetProcessById(_consolePid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>Starts the CLI inside <paramref name="root"/> and waits for its first screen.</summary>
    /// <param name="commandLine">
    /// What the console runs. Overridable so the injector can be exercised against a plain shell,
    /// where "did the keys arrive" is answerable without reading a TUI.
    /// </param>
    public void Start(string root, int readyDelayMs, string commandLine = "cmd.exe /c vyce")
    {
        Shutdown();

        if (OperatingSystem.IsWindows())
        {
            StartHiddenConsole(root, commandLine);
        }
        else
        {
            StartScriptedTty(root);
        }

        // No output to wait on: a TUI draws, it does not announce itself. A short fixed delay is the
        // only signal available, and a keystroke delivered too early is dropped by the input box.
        Thread.Sleep(readyDelayMs);
    }

    /// <summary>Types one line into the TUI's input box and presses Enter.</summary>
    /// <param name="pressEnter">False leaves the text in the box, which is how a menu is opened.</param>
    public void TypeLine(string text, bool pressEnter = true)
    {
        // Text and Enter in one batch raced the CLI's input handling: a warm console saw the Enter
        // before it had the text, so nothing was submitted and the prompt sat in the composer. The
        // gap is the difference between "typed and sent" and "typed".
        TypeText(text);

        if (!pressEnter) return;

        Thread.Sleep(KeyGapMs);
        PressEnter();
    }

    /// <summary>Breath between a keystroke and the one it belongs with.</summary>
    private const int KeyGapMs = 150;

    /// <summary>Types text with no Enter, for a menu that filters as it is typed into.</summary>
    public void TypeText(string text)
    {
        string single = SingleLine(text);

        if (_pty is not null)
        {
            _pty.Write(single);
            return;
        }

        WithConsole(consoleInput => WriteText(consoleInput, single));
    }

    /// <summary>Presses one key by virtual key code: arrows, Enter, Escape.</summary>
    public void PressKey(int virtualKey, char character = '\0')
    {
        if (_pty is not null)
        {
            string sequence = virtualKey switch
            {
                VkUp => "\u001b[A",
                VkDown => "\u001b[B",
                _ => string.Empty,
            };

            if (sequence.Length > 0) _pty.Write(sequence);
            return;
        }

        WithConsole(consoleInput => NativeConsole.WriteKey(consoleInput, character, virtualKey));
    }

    private static string SingleLine(string text) => text.Replace("\r", " ").Replace("\n", " ");

    /// <summary>Paced so the TUI can repaint between keys instead of dropping the batch.</summary>
    private static void WriteText(IntPtr consoleInput, string single)
    {
        for (int index = 0; index < single.Length; index++)
        {
            if (index > 0 && index % 64 == 0) Thread.Sleep(15);

            NativeConsole.WriteKey(consoleInput, single[index], VirtualKeyFor(single[index]));
        }
    }

    /// <summary>
    /// What the console currently shows.
    ///
    /// Only used when a prompt goes unanswered: "it is not the chat screen" is guesswork until the
    /// screen is readable, and the screen is the one thing this path can see without a terminal.
    /// </summary>
    public string ReadScreen()
    {
        if (_consolePid <= 0) return string.Empty;

        if (!NativeConsole.Attach(_consolePid)) return string.Empty;

        IntPtr consoleOutput = IntPtr.Zero;

        try
        {
            consoleOutput = NativeConsole.OpenConsoleOutput();
            return consoleOutput == IntPtr.Zero ? string.Empty : NativeConsole.ReadScreenText(consoleOutput);
        }
        catch (Exception)
        {
            return string.Empty;
        }
        finally
        {
            NativeConsole.CloseHandle(consoleOutput);
            NativeConsole.Detach();
        }
    }

    /// <summary>Moves the highlight in a TUI list.</summary>
    public void PressArrowUp() => PressKey(VkUp);

    /// <summary>Moves the highlight in a TUI list.</summary>
    public void PressArrowDown() => PressKey(VkDown);

    /// <summary>Confirms the highlighted row.</summary>
    public void PressEnter() => PressKey(VkReturn, '\r');

    /// <summary>
    /// A ctrl combination, sent the way a keyboard sends it: the control character plus the control
    /// modifier state. A TUI that matches on <c>ctrl+p</c> reads the character.
    /// </summary>
    public void PressControl(char letter)
    {
        char control = (char)(char.ToUpperInvariant(letter) - 'A' + 1);

        if (_pty is not null)
        {
            _pty.Write(control.ToString());
            return;
        }

        WithConsole(consoleInput => NativeConsole.WriteControlKey(consoleInput, control, VirtualKeyFor(letter)));
    }

    /// <summary>Esc cancels a streaming answer, per the CLI's own key list.</summary>
    public void SendEscape()
    {
        if (_pty is not null)
        {
            _pty.Write("\u001b");
            return;
        }

        WithConsole(consoleInput => NativeConsole.WriteKey(consoleInput, '\u001b', VkEscape));
    }

    /// <summary>
    /// Runs <paramref name="write"/> against the CLI's console input buffer.
    ///
    /// The handle is opened as <c>CONIN$</c> rather than taken from <c>GetStdHandle</c>: attaching
    /// does not rewrite this process's stored standard handles, so after an attach they still point
    /// at whatever the host inherited, and writing there types into nothing.
    /// </summary>
    private void WithConsole(Action<IntPtr> write)
    {
        if (_consolePid <= 0)
        {
            throw new InvalidOperationException("The Vyce console is not running.");
        }

        if (!NativeConsole.Attach(_consolePid))
        {
            throw new InvalidOperationException(
                "Could not attach to the Vyce console. Is the CLI still running?");
        }

        IntPtr consoleInput = IntPtr.Zero;
        IntPtr consoleOutput = IntPtr.Zero;

        try
        {
            consoleInput = NativeConsole.OpenConsoleInput();
            consoleOutput = NativeConsole.OpenConsoleOutput();

            if (consoleInput == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Could not open the Vyce console input ({Marshal.GetLastWin32Error()}).");
            }

            // The console exists before the first keystroke, but its window can still be painted by
            // the console host. Hiding it here keeps that off the user's screen.
            NativeConsole.HideConsoleWindow();

            write(consoleInput);
            Thread.Sleep(30);
        }
        finally
        {
            NativeConsole.CloseHandle(consoleInput);
            NativeConsole.CloseHandle(consoleOutput);
            NativeConsole.Detach();
        }
    }

    private void StartHiddenConsole(string root, string commandLine)
    {
        var startup = new NativePty.STARTUPINFOEX();
        startup.StartupInfo.cb = Marshal.SizeOf<NativePty.STARTUPINFOEX>();
        startup.StartupInfo.dwFlags = StartfUseShowWindow;
        startup.StartupInfo.wShowWindow = SwHide;

        // CREATE_NEW_CONSOLE is the whole point: this is the flag the pseudo-console path does not
        // have, and the reason vyce runs here and not there.
        uint flags = CreateNewConsole | CreateUnicodeEnvironment;

        Log($"spawn hidden console in {root}: {commandLine}");

        bool started = NativePty.CreateProcess(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            flags,
            IntPtr.Zero,
            root,
            ref startup,
            out var info);

        if (!started)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Could not start the Vyce CLI (CreateProcess error {error}). Is it installed and signed in?");
        }

        NativePty.CloseHandle(info.hThread);

        _consolePid = info.dwProcessId;

        try
        {
            _process = Process.GetProcessById(_consolePid);
        }
        catch (ArgumentException)
        {
            // The process can be gone this fast when the CLI fails immediately. IsRunning reports
            // the truth either way, so nothing is hidden.
            _process = null;
        }

        Log($"console pid {_consolePid}");
    }

    /// <summary>
    /// Unix has no AttachConsole, but it has <c>script</c>: it runs the command under a real
    /// terminal and forwards whatever arrives on its own stdin, which is our pipe.
    /// </summary>
    private void StartScriptedTty(string root)
    {
        bool mac = Services.Platform.PlatformHelper.IsMacOS;
        string command = mac
            ? "script -q /dev/null vyce"
            : "script -qfc \"vyce\" /dev/null";

        var pty = new ConPtySession();
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
        };

        bool started = pty.Start(command, root, 120, 40, environment);

        Log($"spawn {command} in {root} -> {started}");

        if (!started)
        {
            pty.Dispose();
            throw new InvalidOperationException("Could not start the Vyce CLI. Is it installed and signed in?");
        }

        _pty = pty;
    }

    /// <summary>
    /// The virtual key code a console key event carries. A real keyboard sends both, and a TUI that
    /// checks the key code rather than the character ignores events that omit it.
    /// </summary>
    private static int VirtualKeyFor(char value)
    {
        int scan = NativeConsole.VkKeyScan(value);

        // -1 means the character has no key on this layout; the character still gets through.
        return scan == -1 ? 0 : scan & 0xFF;
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(logPath, $"{DateTime.UtcNow:O}\t{message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // A log that cannot be written must never fail a turn.
        }
    }

    private void Shutdown()
    {
        if (_pty is not null)
        {
            try
            {
                _pty.Stop();
            }
            catch (Exception)
            {
                // A process that already exited needs no stopping.
            }

            _pty.Dispose();
            _pty = null;
        }

        if (_consolePid > 0)
        {
            try
            {
                using var process = Process.GetProcessById(_consolePid);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Gone already, or still exiting. Either way there is nothing left to own.
            }
        }

        _process?.Dispose();
        _process = null;
        _consolePid = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Shutdown();
    }

    /// <summary>The console calls the injector needs, kept away from the engine's logic.</summary>
    private static class NativeConsole
    {
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareReadAndWrite = 0x00000003;
        private const uint OpenExisting = 3;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetConsoleScreenBufferInfo(IntPtr consoleOutput, out ConsoleScreenBufferInfo info);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadConsoleOutputCharacterW(
            IntPtr consoleOutput,
            [Out] char[] buffer,
            uint length,
            Coord position,
            out uint read);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteConsoleInputW(
            IntPtr consoleInput,
            [In] InputRecord[] records,
            uint length,
            out uint written);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScan(char character);

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        public struct KeyEvent
        {
            [FieldOffset(0)] public int KeyDown;
            [FieldOffset(4)] public ushort RepeatCount;
            [FieldOffset(6)] public ushort VirtualKeyCode;
            [FieldOffset(8)] public ushort VirtualScanCode;
            [FieldOffset(10)] public char UnicodeChar;
            [FieldOffset(12)] public uint ControlKeyState;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputRecord
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public KeyEvent Key;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Coord
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SmallRect
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ConsoleScreenBufferInfo
        {
            public Coord Size;
            public Coord CursorPosition;
            public ushort Attributes;
            public SmallRect Window;
            public Coord MaximumWindowSize;
        }

        public static bool Attach(int processId)
        {
            // AttachConsole fails whenever the caller already owns a console, so drop ours first.
            // A GUI process usually has none; this covers a debug run from a terminal.
            FreeConsole();
            return AttachConsole((uint)processId);
        }

        public static void Detach() => FreeConsole();

        public static IntPtr OpenConsoleInput() => CreateFileW(
            "CONIN$", GenericRead | GenericWrite, FileShareReadAndWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        public static IntPtr OpenConsoleOutput() => CreateFileW(
            "CONOUT$", GenericRead | GenericWrite, FileShareReadAndWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        public static void HideConsoleWindow()
        {
            IntPtr window = GetConsoleWindow();
            if (window != IntPtr.Zero) ShowWindow(window, SwHide);
        }

        /// <summary>All text on the console screen, top to bottom.</summary>
        public static string ReadScreenText(IntPtr consoleOutput)
        {
            if (!GetConsoleScreenBufferInfo(consoleOutput, out var info)) return string.Empty;

            var builder = new System.Text.StringBuilder();

            int rows = Math.Min((int)info.Size.Y, 60);
            int columns = Math.Max(1, (int)info.Size.X);

            for (short row = 0; row < rows; row++)
            {
                var line = new char[columns];

                if (ReadConsoleOutputCharacterW(consoleOutput, line, (uint)columns, new Coord { X = 0, Y = row }, out uint read))
                {
                    builder.Append(new string(line, 0, (int)read).TrimEnd()).Append('\n');
                }
            }

            return builder.ToString();
        }

        /// <summary>Left ctrl held down, as a KEY_EVENT_RECORD reports it.</summary>
        private const uint LeftCtrlPressed = 0x0008;

        /// <summary>A ctrl combination: same shapes as a key press, with the ctrl state set.</summary>
        public static void WriteControlKey(IntPtr consoleInput, char control, int virtualKey) =>
            Write(consoleInput, control, virtualKey, LeftCtrlPressed);

        /// <summary>Sends one character as a key-down followed by a key-up, like a keyboard.</summary>
        public static void WriteKey(IntPtr consoleInput, char character, int virtualKey)
        {
            Write(consoleInput, character, virtualKey, 0);
        }

        private static void Write(IntPtr consoleInput, char character, int virtualKey, uint controlState)
        {
            var records = new[]
            {
                new InputRecord
                {
                    EventType = KeyEventType,
                    Key = new KeyEvent
                    {
                        KeyDown = 1,
                        RepeatCount = 1,
                        VirtualKeyCode = (ushort)virtualKey,
                        UnicodeChar = character,
                        ControlKeyState = controlState,
                    },
                },
                new InputRecord
                {
                    EventType = KeyEventType,
                    Key = new KeyEvent
                    {
                        KeyDown = 0,
                        RepeatCount = 1,
                        VirtualKeyCode = (ushort)virtualKey,
                        UnicodeChar = character,
                        ControlKeyState = controlState,
                    },
                },
            };

            if (!WriteConsoleInputW(consoleInput, records, (uint)records.Length, out _))
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Could not type into the Vyce console (error {error}).");
            }
        }
    }
}
