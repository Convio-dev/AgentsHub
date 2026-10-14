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

    public DashboardWebBridge? DashboardBridge => _dashboardWebBridge;
    public HudTerminalForm? HudTerminal => _hudTerminal;

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
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

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
        RemoveKeyboardHook();
        UnregisterGlobalHotkey();
        UnregisterHudHotkey();
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
    // ---------------------------------------------------------------
    // Global hotkey: show or hide the window or Quake HUD/CLI from anywhere.
    // ---------------------------------------------------------------
    private const int WmHotKey = 0x0312;
    private const int HotkeyId = 0x4C31;
    private const int HotkeyHudId = 0x4C32;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkH = 0x48;
    private bool _globalHotkeyRegistered;
    private bool _hudHotkeyRegistered;
    private HudTerminalForm? _hudTerminal;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    private static IntPtr _hookHandle = IntPtr.Zero;
    private static LowLevelKeyboardProc? _keyboardProc;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyGlobalHotkey();
    }

    /// <summary>
    /// Registers or unregisters the hotkeys to match stored preferences.
    /// </summary>
    public void ApplyGlobalHotkey()
    {
        bool wantEnabled = StartupHelper.GetPreference("GlobalHotkey", true);

        if (wantEnabled && IsHandleCreated)
        {
            if (_globalHotkeyRegistered) UnregisterGlobalHotkey();
            _globalHotkeyRegistered = RegisterHotKey(Handle, HotkeyId, ModControl | ModAlt, VkH);
        }
        else
        {
            UnregisterGlobalHotkey();
        }

        bool wantHud = StartupHelper.GetPreference("GlobalHudHotkey", true);
        if (wantHud)
        {
            SetupKeyboardHook();

            // Also register via RegisterHotKey as secondary fallback if supported
            if (IsHandleCreated)
            {
                if (_hudHotkeyRegistered) UnregisterHudHotkey();
                uint targetVk = GetVkCodeFromName(StartupHelper.GetStringPreference("HudHotkeyKey", "F12"));
                uint modFlags = GetFsModifiersFromName(StartupHelper.GetStringPreference("HudHotkeyModifier", "None"));
                _hudHotkeyRegistered = RegisterHotKey(Handle, HotkeyHudId, modFlags, targetVk);
            }
        }
        else
        {
            RemoveKeyboardHook();
            UnregisterHudHotkey();
        }
    }

    private void SetupKeyboardHook()
    {
        if (_hookHandle != IntPtr.Zero) return;
        try
        {
            _keyboardProc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr modHandle = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, modHandle, 0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainForm] SetupKeyboardHook error: {ex.Message}");
        }
    }

    private void RemoveKeyboardHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            try
            {
                UnhookWindowsHookEx(_hookHandle);
            }
            catch { }
            _hookHandle = IntPtr.Zero;
            _keyboardProc = null;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            try
            {
                var kbd = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (IsQuakeHotkeyMatch(kbd.vkCode))
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(ExecuteQuakeHotkeyAction));
                    }
                    return (IntPtr)1;
                }
            }
            catch { }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool IsQuakeHotkeyMatch(uint vk)
    {
        bool wantHud = StartupHelper.GetPreference("GlobalHudHotkey", true);
        if (!wantHud) return false;

        string configuredKey = StartupHelper.GetStringPreference("HudHotkeyKey", "F12");
        uint targetVk = GetVkCodeFromName(configuredKey);
        if (vk != targetVk) return false;

        string mod = StartupHelper.GetStringPreference("HudHotkeyModifier", "None");

        bool ctrlDown = (GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
        bool altDown = (GetKeyState(0x12) & 0x8000) != 0;  // VK_MENU
        bool shiftDown = (GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT
        bool winDown = (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0; // VK_LWIN / VK_RWIN

        return mod.ToLowerInvariant() switch
        {
            "none" => !ctrlDown && !altDown && !shiftDown && !winDown,
            "alt" => altDown && !ctrlDown && !shiftDown && !winDown,
            "ctrl" => ctrlDown && !altDown && !shiftDown && !winDown,
            "shift" => shiftDown && !ctrlDown && !altDown && !winDown,
            "win" => winDown && !ctrlDown && !altDown && !shiftDown,
            "ctrl+alt" => ctrlDown && altDown && !shiftDown && !winDown,
            "ctrl+shift" => ctrlDown && shiftDown && !altDown && !winDown,
            "alt+shift" => altDown && shiftDown && !ctrlDown && !winDown,
            _ => true
        };
    }

    private static uint GetVkCodeFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0x7B; // VK_F12 default
        return name.Trim().ToUpperInvariant() switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "OEM3" or "~" or "`" => 0xC0,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "Q" => 0x51,
            "K" => 0x4B,
            "T" => 0x54,
            "H" => 0x48,
            _ => 0x7B
        };
    }

    private static uint GetFsModifiersFromName(string mod)
    {
        uint flags = 0x4000; // MOD_NOREPEAT
        return mod.ToLowerInvariant() switch
        {
            "alt" => flags | 0x0001,
            "ctrl" => flags | 0x0002,
            "shift" => flags | 0x0004,
            "win" => flags | 0x0008,
            "ctrl+alt" => flags | 0x0002 | 0x0001,
            "ctrl+shift" => flags | 0x0002 | 0x0004,
            "alt+shift" => flags | 0x0001 | 0x0004,
            _ => flags
        };
    }

    private void UnregisterGlobalHotkey()
    {
        if (!_globalHotkeyRegistered || !IsHandleCreated) return;

        try
        {
            UnregisterHotKey(Handle, HotkeyId);
        }
        catch { }

        _globalHotkeyRegistered = false;
    }

    private void UnregisterHudHotkey()
    {
        if (!_hudHotkeyRegistered || !IsHandleCreated) return;

        try
        {
            UnregisterHotKey(Handle, HotkeyHudId);
        }
        catch { }

        _hudHotkeyRegistered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey)
        {
            int hotkeyId = m.WParam.ToInt32();
            if (hotkeyId == HotkeyId)
            {
                ShowOrHideFromHotkey();
                return;
            }
            if (hotkeyId == HotkeyHudId)
            {
                ExecuteQuakeHotkeyAction();
                return;
            }
        }

        base.WndProc(ref m);
    }

    public void ExecuteQuakeHotkeyAction()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ExecuteQuakeHotkeyAction));
            return;
        }

        string target = StartupHelper.GetStringPreference("HudHotkeyTarget", "cliwebview");

        if (target.Equals("hud", StringComparison.OrdinalIgnoreCase))
        {
            ToggleHudDropdown();
        }
        else if (target.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            ToggleHudDropdown();
            _dashboardWebBridge?.ToggleOrShowCliWebview();
        }
        else // "cliwebview" - default: Standalone Terminals Window
        {
            _dashboardWebBridge?.ToggleOrShowCliWebview();
        }
    }

    public void ToggleHudDropdown()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ToggleHudDropdown));
            return;
        }

        if (_hudTerminal == null || _hudTerminal.IsDisposed)
        {
            _hudTerminal = new HudTerminalForm(_cliManager, this);
        }
        _hudTerminal.Toggle();
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
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
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
