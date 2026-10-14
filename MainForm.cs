#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.IO;
using HermesHub.Helpers;
using HermesHub.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HermesHub;

public partial class MainForm : Form
{
    private HermesProcessService? _hermesService;
    private CliManagerService? _cliManager;
    private LanProxyService? _lanProxyService;
    private DashboardWebBridge? _dashboardWebBridge;
    private OpenCodeWatcherService? _openCodeWatcher;
    private WebView2? _webView;

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _trayContextMenu;
    private ToolStripMenuItem? _trayOpenItem;
    private ToolStripMenuItem? _trayStartAllItem;
    private ToolStripMenuItem? _trayStopAllItem;
    private ToolStripMenuItem? _trayExitItem;

    private bool _isExiting;
    private Icon? _appIcon;

    public MainForm()
    {
        InitializeComponent();

        if (System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
        {
            InitializeRuntime(false);
        }
    }

    public MainForm(bool startMinimized) : this()
    {
        if (startMinimized && System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    private void InitializeRuntime(bool startMinimized)
    {
        ThemeManager.Initialize();

        // Load Ports and Config
        var savedEnginePort = StartupHelper.GetIntPreference("EnginePort", HermesProcessService.DefaultHermesPort);
        var savedLanPort = StartupHelper.GetIntPreference("LanPort", 9191);

        _hermesService = new HermesProcessService(savedEnginePort);
        _cliManager = new CliManagerService(_hermesService);
        _openCodeWatcher = new OpenCodeWatcherService();
        _lanProxyService = new LanProxyService
        {
            ListenPort = savedLanPort,
            TargetPort = savedEnginePort,
            StartHermesAction = () => { _cliManager.Start(CliType.Hermes); return Task.CompletedTask; },
            CheckHermesAliveAction = () => Task.FromResult(_cliManager.GetInfo(CliType.Hermes).State == CliState.Running)
        };
        var lanProxyEnabled = StartupHelper.GetPreference("LanProxyEnabled", true);
        if (lanProxyEnabled)
        {
            _lanProxyService.Start(savedLanPort, savedEnginePort);
        }

        try
        {
            ClaudeOpenRouterProxyService.Instance.Start(9230);
        }
        catch { }

        // App Icon
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
            if (File.Exists(iconPath))
            {
                _appIcon = new Icon(iconPath);
                Icon = _appIcon;
            }
        }
        catch { }

        // Setup System Tray
        SetupSystemTray();

        // Setup WebView2 Workspace
        _ = InitializeWebViewAsync(startMinimized);
    }

    private async Task InitializeWebViewAsync(bool startMinimized)
    {
        try
        {
            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(8, 10, 15)
            };
            Controls.Add(_webView);

            var baseLocalApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(baseLocalApp, "AgentsHub", "WebView2Data");
            if (!Directory.Exists(userDataFolder))
            {
                Directory.CreateDirectory(userDataFolder);
            }

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            var webAssetsPath = Path.Combine(AppContext.BaseDirectory, "ui");
            if (!Directory.Exists(webAssetsPath))
            {
                Directory.CreateDirectory(webAssetsPath);
            }

            // Map https://app.agentshub.local to ui folder
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.agentshub.local",
                webAssetsPath,
                CoreWebView2HostResourceAccessKind.Allow
            );

            // DevTools stay unavailable in every WebView2 the app hosts - this is a product
            // surface, not a browser. AreDevToolsEnabled = false removes the DevTools window
            // itself, the context-menu "Inspect" entry, and the F12 / Ctrl+Shift+I shortcuts.
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = true;

            // Attach C# backend bridge
            if (_cliManager != null)
            {
                _dashboardWebBridge = new DashboardWebBridge(this, _cliManager, _lanProxyService, _openCodeWatcher);
                _dashboardWebBridge.AttachWebView(_webView.CoreWebView2);
            }

            // Navigate to modern dashboard
            _webView.CoreWebView2.Navigate("https://app.agentshub.local/index.html");

            if (startMinimized)
            {
                Hide();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize modern workspace: {ex.Message}", "WebView2 Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetupSystemTray()
    {
        _trayContextMenu = new ContextMenuStrip();

        _trayOpenItem = new ToolStripMenuItem("Open Agents Hub", null, (_, _) => ShowAndActivate())
        {
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        _trayStartAllItem = new ToolStripMenuItem("Start All CLIs", null, (_, _) =>
        {
            if (_cliManager != null)
            {
                _cliManager.Start(CliType.Hermes);
                _cliManager.Start(CliType.ClaudeCode);
                _cliManager.Start(CliType.AntigravityCli);
            }
        });
        _trayStopAllItem = new ToolStripMenuItem("Stop All CLIs", null, async (_, _) =>
        {
            if (_cliManager != null)
            {
                await _cliManager.StopAsync(CliType.Hermes);
                await _cliManager.StopAsync(CliType.ClaudeCode);
                await _cliManager.StopAsync(CliType.AntigravityCli);
            }
        });
        _trayExitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        _trayContextMenu.Items.Add(_trayOpenItem);
        _trayContextMenu.Items.Add(new ToolStripSeparator());
        _trayContextMenu.Items.Add(_trayStartAllItem);
        _trayContextMenu.Items.Add(_trayStopAllItem);
        _trayContextMenu.Items.Add(new ToolStripSeparator());
        _trayContextMenu.Items.Add(_trayExitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "Agents Hub - Multi-Agent Workspace",
            Icon = _appIcon ?? SystemIcons.Application,
            ContextMenuStrip = _trayContextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowAndActivate();
    }

    public void ShowAndActivate()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ShowAndActivate));
            return;
        }

        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        BringToFront();
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        UnregisterGlobalHotkey();

        if (_cliManager != null)
        {
            _ = Task.Run(async () =>
            {
                await _cliManager.StopAsync(CliType.Hermes);
                await _cliManager.StopAsync(CliType.ClaudeCode);
                await _cliManager.StopAsync(CliType.AntigravityCli);
            });
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _openCodeWatcher?.Dispose();
        _dashboardWebBridge?.Dispose();
        try { ClaudeOpenRouterProxyService.Instance.Stop(); } catch { }

        Application.Exit();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (IsHandleCreated)
        {
            ThemeManager.ApplyDwmTitleBarTheme(Handle);
        }
    }

    public void ShowTrayBalloon(string title, string text)
    {
        if (!StartupHelper.GetPreference("WindowsNotifications", true)) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowTrayBalloon(title, text));
            return;
        }
        _notifyIcon?.ShowBalloonTip(1500, title, text, ToolTipIcon.Info);
    }

    // ---------------------------------------------------------------
    // Global hotkey: show or hide the window from anywhere.
    //
    // The app spends most of its life in the tray, and the tray menu is the only way back in.
    // A hotkey is the difference between "click the tray icon" and "press it and keep typing".
    // Ctrl+Alt+H is registered as a system-wide hotkey, so it only works while one instance is
    // running, which the single-instance mutex already guarantees.
    // ---------------------------------------------------------------
    private const int WmHotKey = 0x0312;
    private const int HotkeyId = 0x4C31;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkH = 0x48;
    private bool _globalHotkeyRegistered;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyGlobalHotkey();
    }

    /// <summary>
    /// Registers or unregisters the hotkey to match the stored preference.
    /// </summary>
    public void ApplyGlobalHotkey()
    {
        bool wantEnabled = StartupHelper.GetPreference("GlobalHotkey", true);

        if (wantEnabled && IsHandleCreated)
        {
            if (_globalHotkeyRegistered) UnregisterGlobalHotkey();

            // Registration can fail when another process already holds the combination. That is
            // not fatal: the window is still reachable from the tray.
            _globalHotkeyRegistered = RegisterHotKey(Handle, HotkeyId, ModControl | ModAlt, VkH);
        }
        else
        {
            UnregisterGlobalHotkey();
        }
    }

    private void UnregisterGlobalHotkey()
    {
        if (!_globalHotkeyRegistered || !IsHandleCreated) return;

        try
        {
            UnregisterHotKey(Handle, HotkeyId);
        }
        catch
        {
            // Unregistering a handle that is going away is not worth a crash.
        }

        _globalHotkeyRegistered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == HotkeyId)
        {
            ShowOrHideFromHotkey();
            return;
        }

        base.WndProc(ref m);
    }

    private void ShowOrHideFromHotkey()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ShowOrHideFromHotkey));
            return;
        }

        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Hide();
        }
        else
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            BringToFront();
            Activate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            var minimizeToTray = StartupHelper.GetPreference("MinimizeToTray", true);
            if (minimizeToTray)
            {
                Hide();
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)        {
            var minimizeToTray = StartupHelper.GetPreference("MinimizeToTray", true);
            if (minimizeToTray)
            {
                e.Cancel = true;
                Hide();
                ShowTrayBalloon("Agents Hub", "Agents Hub is running in the background.");
                return;
            }
            else
            {
                ExitApplication();
                return;
            }
        }

        base.OnFormClosing(e);
    }
}
