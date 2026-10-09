namespace HermesHub;

internal static class Program
{
    private const string MutexName = @"Global\HermesHub_SingleInstance_Mutex";
    private const string ShowWindowEventName = @"Global\HermesHub_ShowWindow_Event";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var isOnlyInstance);

        if (!isOnlyInstance)
        {
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
    }

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
}
