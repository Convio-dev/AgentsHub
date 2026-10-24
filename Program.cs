namespace AgentsHub;

internal static partial class Program
{
    private const string MutexName = @"Global\AgentsHub_SingleInstance_Mutex";
    private const string ShowWindowEventName = @"Global\AgentsHub_ShowWindow_Event";

    [STAThread]
    private static void Main(string[] args)
    {
#if WINDOWS
        using var mutex = new Mutex(true, MutexName, out var isOnlyInstance);

        if (!isOnlyInstance)
        {
            AttachConsole(-1);
            Console.WriteLine("[AgentsHub] Agents Hub is already running. Brought the active window to the foreground.");
            // Wake the running instance instead of exiting silently
            SignalRunningInstance();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var startMinimized = args.Any(arg =>
            string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-minimized", StringComparison.OrdinalIgnoreCase));

        var form = new MainForm(startMinimized);

        using var showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        ThreadPool.RegisterWaitForSingleObject(
            showWindowEvent,
            (_, _) =>
            {
                if (form.IsHandleCreated)
                {
                    form.BeginInvoke(form.ShowAndActivate);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        Application.Run(form);
#else
        RunUnixHost(args);
#endif
    }

#if !WINDOWS
    private static void RunUnixHost(string[] args)
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine($" Agents Hub - Desktop Control Plane for AI Coding Agents");
        Console.WriteLine($" Platform: {Services.Platform.PlatformHelper.PlatformName} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
        Console.WriteLine("==========================================================");

        var hermes = new Services.HermesProcessService();
        var cliManager = new Services.CliManagerService(hermes);
        var lanProxy = new Services.LanProxyService();
        var openCode = new Services.OpenCodeWatcherService();
        using var bridge = new Services.DashboardWebBridge(null, cliManager, lanProxy, openCode);

        int port = lanProxy.ListenPort > 0 ? lanProxy.ListenPort : 9119;
        Console.WriteLine($"[AgentsHub] Web Dashboard available at: http://localhost:{port}");
        lanProxy.Start();

        bool noBrowser = args.Any(a => a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase) || a.Equals("-n", StringComparison.OrdinalIgnoreCase));
        if (!noBrowser)
        {
            Console.WriteLine("[AgentsHub] Launching dashboard in default browser...");
            Services.Platform.PlatformHelper.OpenUrlInBrowser($"http://localhost:{port}");
        }

        Console.WriteLine("[AgentsHub] Agents Hub is running. Press Ctrl+C to terminate.");
        using var exitEvent = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\r\n[AgentsHub] Shutting down services...");
            exitEvent.Set();
        };

        exitEvent.Wait();
        lanProxy.Dispose();
        cliManager.Dispose();
        Console.WriteLine("[AgentsHub] Stopped cleanly.");
    }
#endif

#if WINDOWS
    private static void SignalRunningInstance()
    {
        try
        {
            using var showWindowEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
            showWindowEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // First instance has not created the event yet; exiting is still correct
        }
        catch (UnauthorizedAccessException)
        {
            // Cross-session access denied; exiting is still correct
        }
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);
#endif
}
