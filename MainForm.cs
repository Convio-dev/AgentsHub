using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using HermesHub.Helpers;
using HermesHub.Services;

namespace HermesHub;

public partial class MainForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    private readonly HermesProcessService _hermesService;
    private readonly CliManagerService _cliManager;
    private readonly LanProxyService _lanProxyService;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _trayContextMenu;

    private ToolStripMenuItem _trayOpenWebUiItem = null!;
    private ToolStripMenuItem _trayStartItem = null!;
    private ToolStripMenuItem _trayStopItem = null!;
    private ToolStripMenuItem _trayBroadcastItem = null!;

    private bool _isExiting;
    private Icon? _appIcon;
    private Icon _trayIconOffline = null!;
    private Icon _trayIconStarting = null!;
    private Icon _trayIconOnline = null!;
    private Bitmap? _currentQrBitmap;
    private int _totalLogLines;
    private readonly ToolTip _clientsToolTip = new();
    private CliWebviewForm? _cliWebviewForm;
    private CliType _selectedCliType = CliType.Hermes;

    public MainForm(bool startMinimized = false)
    {
        var savedEnginePort = StartupHelper.GetIntPreference("EnginePort", HermesProcessService.DefaultHermesPort);
        var savedLanPort = StartupHelper.GetIntPreference("LanPort", 9191);

        _hermesService = new HermesProcessService(savedEnginePort);
        _cliManager = new CliManagerService(_hermesService);
        _lanProxyService = new LanProxyService
        {
            ListenPort = savedLanPort,
            TargetPort = savedEnginePort
        };
        _notifyIcon = new NotifyIcon();
        _trayContextMenu = new ContextMenuStrip();

        _lanProxyService.StartHermesAction = () =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => _cliManager.Start(CliType.Hermes));
            }
            else
            {
                _cliManager.Start(CliType.Hermes);
            }
            return Task.CompletedTask;
        };

        _lanProxyService.CheckHermesAliveAction = () =>
        {
            return HermesProcessService.CheckPortAliveAsync(_hermesService.Port, 400);
        };

        _lanProxyService.RemoteDeviceConnected += (client) =>
        {
            if (notifyRemoteClientsCheckBox.Checked)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => ShowRemoteConnectionBalloon(client));
                }
                else
                {
                    ShowRemoteConnectionBalloon(client);
                }
            }
        };

        _lanProxyService.ActiveClientsChanged += (clients) => UpdateClientsUi(clients);

        InitializeComponent();

        // Populate CLI selection dropdown
        cliSelectComboBox.Items.Clear();
        cliSelectComboBox.Items.Add("Hermes (Web Dashboard)");
        cliSelectComboBox.Items.Add("Claude Code (CLI)");
        cliSelectComboBox.Items.Add("Gemini CLI");

        var savedCliIndex = StartupHelper.GetIntPreference("SelectedCli", 0);
        if (savedCliIndex < 0 || savedCliIndex >= cliSelectComboBox.Items.Count)
        {
            savedCliIndex = 0;
        }
        cliSelectComboBox.SelectedIndex = savedCliIndex;
        _selectedCliType = (CliType)savedCliIndex;

        cliSelectComboBox.SelectedIndexChanged += (_, _) =>
        {
            _selectedCliType = (CliType)cliSelectComboBox.SelectedIndex;
            StartupHelper.SetIntPreference("SelectedCli", (int)_selectedCliType);
            UpdateCliStatusUi();
        };

        InitializeCustomStyles();
        InitializeTrayIcon();
        HookEvents();

        // Populate initial log lines from engine buffer
        foreach (var log in _hermesService.GetLogHistory())
        {
            logsTextBox.AppendText(log + "\r\n");
            _totalLogLines++;
        }
        logsCountLabel.Text = $"{_totalLogLines} entries";
        footerLogsButton.Text = $"Console ({_totalLogLines})";

        // Load persisted settings
        startWithWindowsCheckBox.Checked = StartupHelper.IsWindowsStartupEnabled();
        autoStartHermesCheckBox.Checked = StartupHelper.GetPreference("AutoStartHermes", false);
        autoEnableLanCheckBox.Checked = StartupHelper.GetPreference("AutoEnableLan", false);
        notifyRemoteClientsCheckBox.Checked = StartupHelper.GetPreference("NotifyRemoteClients", true);
        enginePortNumeric.Value = savedEnginePort;
        lanPortNumeric.Value = savedLanPort;

        // Verify Windows Defender Firewall inbound rule status
        _ = RefreshFirewallStatusAsync();

        // Apply automation if enabled
        if (autoStartHermesCheckBox.Checked)
        {
            _cliManager.Start(CliType.Hermes);
        }

        if (autoEnableLanCheckBox.Checked)
        {
            lanToggleCheckBox.Checked = true;
        }

        UpdateCliStatusUi();

        if (startMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    private void InitializeCustomStyles()
    {
        _appIcon = CreateAppIcon(Color.FromArgb(16, 185, 129));
        _trayIconOffline = CreateAppIcon(Color.FromArgb(239, 68, 68));
        _trayIconStarting = CreateAppIcon(Color.FromArgb(245, 158, 11));
        _trayIconOnline = CreateAppIcon(Color.FromArgb(16, 185, 129));
        Icon = _appIcon;

        // Custom borders for cards
        var cardBorderPen = new Pen(Color.FromArgb(39, 39, 42), 1);
        hermesCard.Paint += (s, e) => e.Graphics.DrawRectangle(cardBorderPen, 0, 0, hermesCard.Width - 1, hermesCard.Height - 1);
        lanCard.Paint += (s, e) => e.Graphics.DrawRectangle(cardBorderPen, 0, 0, lanCard.Width - 1, lanCard.Height - 1);
        settingsCard.Paint += (s, e) => e.Graphics.DrawRectangle(cardBorderPen, 0, 0, settingsCard.Width - 1, settingsCard.Height - 1);
        lanUrlPanel.Paint += (s, e) => e.Graphics.DrawRectangle(cardBorderPen, 0, 0, lanUrlPanel.Width - 1, lanUrlPanel.Height - 1);
        qrDrawerPanel.Paint += (s, e) => e.Graphics.DrawRectangle(cardBorderPen, 0, 0, qrDrawerPanel.Width - 1, qrDrawerPanel.Height - 1);
        logsPanel.Paint += (s, e) => e.Graphics.DrawRectangle(cardBorderPen, 0, 0, logsPanel.Width - 1, logsPanel.Height - 1);

        // Custom paint status dot as clean circle
        statusDot.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(statusDot.BackColor);
            e.Graphics.FillEllipse(brush, 0, 0, statusDot.Width - 1, statusDot.Height - 1);
        };

        // Window drag handlers
        headerPanel.MouseDown += TitleBar_MouseDown;
        titleLabel.MouseDown += TitleBar_MouseDown;
        subtitleLabel.MouseDown += TitleBar_MouseDown;

        // Dynamic alignment for header buttons (anchoring to the top right)
        void PositionHeaderButtons()
        {
            closeButton.Location = new Point(headerPanel.ClientSize.Width - 42, 9);
            minimizeButton.Location = new Point(headerPanel.ClientSize.Width - 78, 9);
        }
        headerPanel.Resize += (_, _) => PositionHeaderButtons();
        PositionHeaderButtons();

        // Dynamic alignment for footer buttons
        void PositionFooterButtons()
        {
            quitAppButton.Location = new Point(footerPanel.ClientSize.Width - 118, 8);
            footerLogsButton.Location = new Point(footerPanel.ClientSize.Width - 226, 8);
        }
        footerPanel.Resize += (_, _) => PositionFooterButtons();
        PositionFooterButtons();

        // High-DPI anti-aliased vector rendering for title bar buttons
        minimizeButton.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool isHovered = minimizeButton.ClientRectangle.Contains(minimizeButton.PointToClient(Cursor.Position));
            Color strokeColor = isHovered ? Color.FromArgb(244, 244, 245) : Color.FromArgb(161, 161, 170);
            using var pen = new Pen(strokeColor, 2f);
            int midY = minimizeButton.Height / 2;
            int midX = minimizeButton.Width / 2;
            e.Graphics.DrawLine(pen, midX - 5, midY, midX + 5, midY);
        };

        closeButton.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool isHovered = closeButton.ClientRectangle.Contains(closeButton.PointToClient(Cursor.Position));
            Color strokeColor = isHovered ? Color.White : Color.FromArgb(161, 161, 170);
            using var pen = new Pen(strokeColor, 2f);
            int midX = closeButton.Width / 2;
            int midY = closeButton.Height / 2;
            int arm = 5;
            e.Graphics.DrawLine(pen, midX - arm, midY - arm, midX + arm, midY + arm);
            e.Graphics.DrawLine(pen, midX + arm, midY - arm, midX - arm, midY + arm);
        };

        // Repaint vector icons on hover transitions
        minimizeButton.MouseEnter += (_, _) => minimizeButton.Invalidate();
        minimizeButton.MouseLeave += (_, _) => minimizeButton.Invalidate();
        closeButton.MouseEnter += (_, _) => closeButton.Invalidate();
        closeButton.MouseLeave += (_, _) => closeButton.Invalidate();

        // Tooltips for title bar buttons
        var titleBarToolTip = new ToolTip();
        titleBarToolTip.SetToolTip(minimizeButton, "Minimize to system tray");
        titleBarToolTip.SetToolTip(closeButton, "Exit completely");

        // Button hover states
        SetupButtonHover(minimizeButton, Color.FromArgb(16, 16, 20), Color.FromArgb(39, 39, 42));
        SetupButtonHover(closeButton, Color.FromArgb(16, 16, 20), Color.FromArgb(220, 38, 38));
        SetupButtonHover(startHermesButton, Color.FromArgb(16, 185, 129), Color.FromArgb(5, 150, 105));
        SetupButtonHover(stopHermesButton, Color.FromArgb(24, 24, 27), Color.FromArgb(40, 24, 27));
        SetupButtonHover(openDashboardButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(viewLogsButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(copyLanButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(openLanButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(toggleQrButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(closeQrButton, Color.FromArgb(39, 39, 42), Color.FromArgb(63, 63, 70));
        SetupButtonHover(footerLogsButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(copyLogsButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(clearLogsButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(closeLogsButton, Color.FromArgb(24, 24, 27), Color.FromArgb(39, 39, 42));
        SetupButtonHover(quitAppButton, Color.FromArgb(16, 16, 20), Color.FromArgb(39, 39, 42));
        SetupButtonHover(savePortsButton, Color.FromArgb(39, 39, 42), Color.FromArgb(63, 63, 70));
    }

    private void InitializeTrayIcon()
    {
        _trayContextMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
        _trayContextMenu.BackColor = Color.FromArgb(24, 24, 27);
        _trayContextMenu.ForeColor = Color.FromArgb(250, 250, 250);

        _trayOpenWebUiItem = new ToolStripMenuItem("Open CLI Webview Hub", null, (_, _) => OpenCliWebview())
        {
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _trayStartItem = new ToolStripMenuItem("Start CLI", null, (_, _) => _cliManager.Start(_selectedCliType));
        _trayStopItem = new ToolStripMenuItem("Stop CLI", null, async (_, _) => await _cliManager.StopAsync(_selectedCliType))
        {
            Enabled = false
        };

        _trayBroadcastItem = new ToolStripMenuItem($"Broadcast on :{_lanProxyService.ListenPort}", null, (_, _) =>
        {
            lanToggleCheckBox.Checked = !lanToggleCheckBox.Checked;
        })
        {
            CheckOnClick = false
        };

        var trayShowDashboardItem = new ToolStripMenuItem("Dashboard Window", null, (_, _) => ShowAndActivate());
        var trayExitItem = new ToolStripMenuItem("Exit Completely", null, (_, _) => ConfirmAndExitApplication());

        _trayContextMenu.Items.Add(_trayOpenWebUiItem);
        _trayContextMenu.Items.Add(new ToolStripSeparator());
        _trayContextMenu.Items.Add(_trayStartItem);
        _trayContextMenu.Items.Add(_trayStopItem);
        _trayContextMenu.Items.Add(_trayBroadcastItem);
        _trayContextMenu.Items.Add(new ToolStripSeparator());
        _trayContextMenu.Items.Add(trayShowDashboardItem);
        _trayContextMenu.Items.Add(trayExitItem);

        _notifyIcon.Icon = _trayIconOffline;
        _notifyIcon.Text = "Hermes Hub - Offline";
        _notifyIcon.Visible = true;
        _notifyIcon.ContextMenuStrip = _trayContextMenu;

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleVisibility();
            }
        };
    }

    private void HookEvents()
    {
        // Window buttons
        minimizeButton.Click += (_, _) => Hide();
        closeButton.Click += (_, _) => ConfirmAndExitApplication();

        // CLI controls
        startHermesButton.Click += (_, _) => _cliManager.Start(_selectedCliType);
        stopHermesButton.Click += async (_, _) => await _cliManager.StopAsync(_selectedCliType);
        openDashboardButton.Click += (_, _) => OpenCliWebview(_selectedCliType);
        viewLogsButton.Click += (_, _) => ToggleLogsDrawer();

        // LAN controls
        lanToggleCheckBox.CheckedChanged += LanToggleCheckBox_CheckedChanged;
        copyLanButton.Click += CopyLanButton_Click;
        openLanButton.Click += OpenLanButton_Click;
        toggleQrButton.Click += (_, _) => ToggleQrDrawer();
        closeQrButton.Click += (_, _) => ToggleQrDrawer(false);
        firewallStatusButton.Click += async (_, _) => await HandleFirewallButtonClickAsync();

        // Logs controls
        footerLogsButton.Click += (_, _) => ToggleLogsDrawer();
        closeLogsButton.Click += (_, _) => ToggleLogsDrawer(false);
        copyLogsButton.Click += CopyLogsButton_Click;
        clearLogsButton.Click += ClearLogsButton_Click;

        // Port and settings controls
        savePortsButton.Click += async (_, _) => await SaveAndApplyPortsAsync();

        startWithWindowsCheckBox.CheckedChanged += (_, _) =>
        {
            StartupHelper.SetWindowsStartup(startWithWindowsCheckBox.Checked);
        };

        autoStartHermesCheckBox.CheckedChanged += (_, _) =>
        {
            StartupHelper.SetPreference("AutoStartHermes", autoStartHermesCheckBox.Checked);
        };

        autoEnableLanCheckBox.CheckedChanged += (_, _) =>
        {
            StartupHelper.SetPreference("AutoEnableLan", autoEnableLanCheckBox.Checked);
        };

        notifyRemoteClientsCheckBox.CheckedChanged += (_, _) =>
        {
            StartupHelper.SetPreference("NotifyRemoteClients", notifyRemoteClientsCheckBox.Checked);
        };

        // Footer button
        quitAppButton.Click += (_, _) => ConfirmAndExitApplication();

        // Service callbacks
        _lanProxyService.LogMessage += (msg) => AppendLog("Proxy", msg);
        _cliManager.LogMessage += (cat, msg) => AppendLog(cat, msg);
        _cliManager.StatusChanged += (type, state, message) =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(UpdateCliStatusUi);
            }
            else
            {
                UpdateCliStatusUi();
            }
        };

        // Telemetry update timer
        telemetryTimer.Interval = 2000;
        telemetryTimer.Tick += (_, _) => UpdateTelemetry();
        telemetryTimer.Start();
        UpdateTelemetry();
    }

    private async Task RefreshFirewallStatusAsync()
    {
        var port = _lanProxyService.ListenPort;
        var allowed = await Task.Run(() => FirewallHelper.IsInboundRuleConfigured(port));

        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateFirewallUi(allowed));
        }
        else
        {
            UpdateFirewallUi(allowed);
        }
    }

    private void UpdateFirewallUi(bool allowed)
    {
        if (allowed)
        {
            firewallStatusButton.Text = "Firewall: Allowed";
            firewallStatusButton.ForeColor = Color.FromArgb(16, 185, 129);
            firewallStatusButton.FlatAppearance.BorderColor = Color.FromArgb(16, 185, 129);
            firewallStatusButton.BackColor = Color.FromArgb(24, 24, 27);
        }
        else
        {
            firewallStatusButton.Text = "Allow in Firewall";
            firewallStatusButton.ForeColor = Color.FromArgb(245, 158, 11);
            firewallStatusButton.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
            firewallStatusButton.BackColor = Color.FromArgb(39, 39, 42);
        }
    }

    private async Task HandleFirewallButtonClickAsync()
    {
        var port = _lanProxyService.ListenPort;
        var alreadyConfigured = FirewallHelper.IsInboundRuleConfigured(port);
        if (alreadyConfigured)
        {
            MessageBox.Show(
                $"Inbound TCP rule for port {port} is already active in Windows Defender Firewall.",
                "Firewall Verified",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        firewallStatusButton.Text = "Configuring...";
        var success = await FirewallHelper.RequestAddInboundRuleAsync(port);
        if (success)
        {
            UpdateFirewallUi(true);
            MessageBox.Show(
                $"Inbound rule for port {port} was successfully added to Windows Defender Firewall.",
                "Rule Configured",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        else
        {
            await RefreshFirewallStatusAsync();
        }
    }

    private async Task SaveAndApplyPortsAsync()
    {
        var newEnginePort = (int)enginePortNumeric.Value;
        var newLanPort = (int)lanPortNumeric.Value;

        if (newEnginePort == newLanPort)
        {
            MessageBox.Show(
                "Engine port and LAN broadcast port cannot be identical.",
                "Port Collision",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        StartupHelper.SetIntPreference("EnginePort", newEnginePort);
        StartupHelper.SetIntPreference("LanPort", newLanPort);

        _hermesService.Port = newEnginePort;
        _lanProxyService.TargetPort = newEnginePort;

        var lanPortChanged = _lanProxyService.ListenPort != newLanPort;
        _lanProxyService.ListenPort = newLanPort;

        if (lanPortChanged && _lanProxyService.IsRunning)
        {
            _lanProxyService.Stop();
            _lanProxyService.Start(newLanPort, newEnginePort);
            lanUrlTextBox.Text = NetworkHelper.GetLanUrl(newLanPort);
        }
        else if (!_lanProxyService.IsRunning)
        {
            lanUrlTextBox.Text = $"http://0.0.0.0:{newLanPort}";
        }

        if (qrDrawerPanel.Visible)
        {
            RefreshQrCodeImage();
        }

        _trayBroadcastItem.Text = $"Broadcast on :{newLanPort}";
        lanToggleCheckBox.Text = $"Broadcast to LAN (Port {newLanPort})";

        savePortsButton.Text = "Saved!";
        savePortsButton.ForeColor = Color.FromArgb(16, 185, 129);
        await Task.Delay(1200);
        savePortsButton.Text = "Apply Ports";
        savePortsButton.ForeColor = Color.FromArgb(250, 250, 250);

        _ = RefreshFirewallStatusAsync();
    }

    private void ShowRemoteConnectionBalloon(ConnectedClientInfo client)
    {
        _notifyIcon.ShowBalloonTip(
            3000,
            "Remote Device Connected",
            $"Device {client.IpAddress} ({client.DeviceName}) connected to Hermes on port {_lanProxyService.ListenPort}.",
            ToolTipIcon.Info
        );
    }

    private void ShowRemoteConnectionBalloon(string clientIp)
    {
        _notifyIcon.ShowBalloonTip(
            3000,
            "Remote Device Connected",
            $"Device {clientIp} connected to Hermes LAN broadcast on port {_lanProxyService.ListenPort}.",
            ToolTipIcon.Info
        );
    }

    private void UpdateCliStatusUi()
    {
        var info = _cliManager.GetInfo(_selectedCliType);

        startHermesButton.Text = $"Start {info.ShortName}";
        stopHermesButton.Text = $"Stop {info.ShortName}";
        openDashboardButton.Text = "Open Webview";

        hermesStatusLabel.Text = info.StatusText;

        switch (info.State)
        {
            case CliState.Running:
                hermesStatusLabel.ForeColor = Color.FromArgb(16, 185, 129);
                startHermesButton.Enabled = false;
                startHermesButton.BackColor = Color.FromArgb(39, 39, 42);
                startHermesButton.ForeColor = Color.FromArgb(113, 113, 122);
                stopHermesButton.Enabled = true;
                stopHermesButton.ForeColor = Color.FromArgb(239, 68, 68);
                break;

            case CliState.Starting:
                hermesStatusLabel.ForeColor = Color.FromArgb(245, 158, 11);
                startHermesButton.Enabled = false;
                startHermesButton.BackColor = Color.FromArgb(39, 39, 42);
                startHermesButton.ForeColor = Color.FromArgb(113, 113, 122);
                stopHermesButton.Enabled = true;
                stopHermesButton.ForeColor = Color.FromArgb(161, 161, 170);
                break;

            case CliState.Offline:
            case CliState.Error:
            default:
                hermesStatusLabel.ForeColor = Color.FromArgb(239, 68, 68);
                startHermesButton.Enabled = true;
                startHermesButton.BackColor = Color.FromArgb(16, 185, 129);
                startHermesButton.ForeColor = Color.FromArgb(9, 9, 11);
                stopHermesButton.Enabled = false;
                stopHermesButton.ForeColor = Color.FromArgb(113, 113, 122);
                break;
        }

        // Multi-CLI Overview summary
        var activeCount = _cliManager.GetActiveCount();
        cliSummaryLabel.Text = $"Active: {activeCount}/3";
        cliSummaryLabel.ForeColor = activeCount > 0 ? Color.FromArgb(16, 185, 129) : Color.FromArgb(113, 113, 122);

        var hInfo = _cliManager.GetInfo(CliType.Hermes);
        var cInfo = _cliManager.GetInfo(CliType.ClaudeCode);
        var gInfo = _cliManager.GetInfo(CliType.GeminiCli);

        static string Dot(CliState s) => s == CliState.Running ? "●" : (s == CliState.Starting ? "◐" : "○");
        cliDotsLabel.Text = $"Hermes: {Dot(hInfo.State)}   Claude: {Dot(cInfo.State)}   Gemini: {Dot(gInfo.State)}";

        // Status dot in title bar
        statusDot.BackColor = activeCount > 0 ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
        statusDot.Invalidate();

        // System tray
        _notifyIcon.Icon = activeCount > 0 ? _trayIconOnline : _trayIconOffline;
        _notifyIcon.Text = $"Hermes Hub - {activeCount} of 3 Active";

        _trayStartItem.Text = $"Start {info.ShortName}";
        _trayStartItem.Enabled = info.State == CliState.Offline;
        _trayStopItem.Text = $"Stop {info.ShortName}";
        _trayStopItem.Enabled = info.State == CliState.Running || info.State == CliState.Starting;
    }

    private void LanToggleCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (lanToggleCheckBox.Checked)
        {
            var success = _lanProxyService.Start(_lanProxyService.ListenPort, _hermesService.Port);
            if (success)
            {
                var url = NetworkHelper.GetLanUrl(_lanProxyService.ListenPort);
                lanUrlTextBox.Text = url;
                lanStatusLabel.Text = "Status: Active and Broadcasting";
                lanStatusLabel.ForeColor = Color.FromArgb(16, 185, 129);
                _trayBroadcastItem.Checked = true;

                if (qrDrawerPanel.Visible)
                {
                    RefreshQrCodeImage();
                }

                _notifyIcon.ShowBalloonTip(
                    2500,
                    "LAN Broadcast Active",
                    $"Hermes is accessible across your network at {url}",
                    ToolTipIcon.Info
                );
            }
            else
            {
                lanToggleCheckBox.Checked = false;
                MessageBox.Show(
                    $"Failed to bind port {_lanProxyService.ListenPort}. Please check if another process is using this port.",
                    "Port Conflict",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
        else
        {
            _lanProxyService.Stop();
            lanStatusLabel.Text = "Status: Local Only (Isolated)";
            lanStatusLabel.ForeColor = Color.FromArgb(113, 113, 122);
            lanClientsLabel.Text = "Active Clients: 0 (No remote devices)";
            lanClientsLabel.ForeColor = Color.FromArgb(113, 113, 122);
            lanUrlTextBox.Text = $"http://0.0.0.0:{_lanProxyService.ListenPort}";
            _trayBroadcastItem.Checked = false;

            if (qrDrawerPanel.Visible)
            {
                RefreshQrCodeImage();
            }
        }
    }

    private void UpdateFormLayout()
    {
        lanCard.Top = hermesCard.Bottom + 12;
        var lanHeight = qrDrawerPanel.Visible ? 340 : 152;
        lanCard.Height = lanHeight;

        var currentTop = lanCard.Bottom + 12;
        settingsCard.Top = currentTop;
        currentTop = settingsCard.Bottom + 12;

        if (logsPanel.Visible)
        {
            logsPanel.Top = currentTop;
            logsPanel.Height = 180;
            currentTop = logsPanel.Bottom + 14;
        }
        else
        {
            currentTop = settingsCard.Bottom + 14;
        }

        ClientSize = new Size(450, currentTop + footerPanel.Height);
    }

    private void ToggleQrDrawer(bool? forceShow = null)
    {
        var shouldShow = forceShow ?? !qrDrawerPanel.Visible;
        qrDrawerPanel.Visible = shouldShow;
        toggleQrButton.Text = shouldShow ? "Hide QR" : "QR";
        toggleQrButton.BackColor = shouldShow ? Color.FromArgb(39, 39, 42) : Color.FromArgb(24, 24, 27);

        UpdateFormLayout();

        if (shouldShow)
        {
            RefreshQrCodeImage();
        }
    }

    private void ToggleLogsDrawer(bool? forceShow = null)
    {
        var shouldShow = forceShow ?? !logsPanel.Visible;
        logsPanel.Visible = shouldShow;
        viewLogsButton.Text = shouldShow ? "Hide Logs" : "Logs";
        viewLogsButton.BackColor = shouldShow ? Color.FromArgb(39, 39, 42) : Color.FromArgb(24, 24, 27);
        footerLogsButton.BackColor = shouldShow ? Color.FromArgb(39, 39, 42) : Color.FromArgb(24, 24, 27);

        UpdateFormLayout();

        if (shouldShow)
        {
            logsTextBox.SelectionStart = logsTextBox.TextLength;
            logsTextBox.ScrollToCaret();
        }
    }

    private void AppendLog(string category, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(category, message));
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] [{category}] {message}\r\n";
        logsTextBox.AppendText(line);
        _totalLogLines++;

        logsCountLabel.Text = $"{_totalLogLines} entries";
        footerLogsButton.Text = $"Console ({_totalLogLines})";

        if (logsPanel.Visible)
        {
            logsTextBox.SelectionStart = logsTextBox.TextLength;
            logsTextBox.ScrollToCaret();
        }
    }

    private async void CopyLogsButton_Click(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(logsTextBox.Text);
            copyLogsButton.Text = "Copied!";
            copyLogsButton.ForeColor = Color.FromArgb(16, 185, 129);
            await Task.Delay(1200);
            copyLogsButton.Text = "Copy";
            copyLogsButton.ForeColor = Color.FromArgb(250, 250, 250);
        }
        catch
        {
            // Suppress clipboard access exceptions
        }
    }

    private void ClearLogsButton_Click(object? sender, EventArgs e)
    {
        logsTextBox.Clear();
        _hermesService.ClearLogs();
        _totalLogLines = 0;
        logsCountLabel.Text = "0 entries";
        footerLogsButton.Text = "Console (0)";
    }

    private void UpdateClientsUi(IReadOnlyList<ConnectedClientInfo> clients)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateClientsUi(clients));
            return;
        }

        if (clients.Count == 0)
        {
            lanClientsLabel.Text = "Active Clients: 0 (No remote devices)";
            lanClientsLabel.ForeColor = Color.FromArgb(113, 113, 122);
            _clientsToolTip.SetToolTip(lanClientsLabel, "No remote devices are currently connected.");
        }
        else
        {
            lanClientsLabel.ForeColor = Color.FromArgb(16, 185, 129);
            lanClientsLabel.Text = _lanProxyService.GetConnectedClientsSummary();

            var tooltipText = new StringBuilder();
            tooltipText.AppendLine("Connected Remote Devices:");
            foreach (var client in clients)
            {
                tooltipText.AppendLine($"• {client.IpAddress}: {client.DeviceName} (Active sockets: {client.ActiveConnections})");
            }
            _clientsToolTip.SetToolTip(lanClientsLabel, tooltipText.ToString().TrimEnd());
        }
    }

    private void RefreshQrCodeImage()
    {
        var url = lanUrlTextBox.Text;
        if (string.IsNullOrWhiteSpace(url) || url.Contains("0.0.0.0"))
        {
            url = NetworkHelper.GetLanUrl(_lanProxyService.ListenPort);
        }

        qrUrlLabel.Text = url;

        try
        {
            var newBmp = QrCodeHelper.GenerateQrCode(url, 4, 2);
            var oldBmp = _currentQrBitmap;
            _currentQrBitmap = newBmp;
            qrPictureBox.Image = newBmp;
            oldBmp?.Dispose();
        }
        catch
        {
            // Suppress QR rendering errors
        }
    }

    private async void CopyLanButton_Click(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(lanUrlTextBox.Text);
            copyLanButton.Text = "Copied!";
            copyLanButton.ForeColor = Color.FromArgb(16, 185, 129);
            await Task.Delay(1500);
            copyLanButton.Text = "Copy Link";
            copyLanButton.ForeColor = Color.FromArgb(250, 250, 250);
        }
        catch
        {
            // Suppress clipboard access exceptions
        }
    }

    private void OpenLanButton_Click(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(lanUrlTextBox.Text) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenCliWebview(CliType? initialTab = null)
    {
        var tab = initialTab ?? _selectedCliType;
        if (_cliWebviewForm == null || _cliWebviewForm.IsDisposed)
        {
            _cliWebviewForm = new CliWebviewForm(_cliManager, tab);
            _cliWebviewForm.FormClosed += (_, _) => _cliWebviewForm = null;
        }
        else
        {
            _cliWebviewForm.SwitchTab(tab);
        }

        _cliWebviewForm.Show();
        _cliWebviewForm.WindowState = FormWindowState.Normal;
        _cliWebviewForm.Activate();
        _cliWebviewForm.BringToFront();
    }

    private void OpenDashboard()
    {
        OpenCliWebview(CliType.Hermes);
    }

    private void UpdateTelemetry()
    {
        try
        {
            using var currentProc = Process.GetCurrentProcess();
            var memoryMb = currentProc.WorkingSet64 / (1024 * 1024);
            ramUsageLabel.Text = $"RAM: {memoryMb} MB";
        }
        catch
        {
            // Ignore telemetry read issues
        }
    }

    private void ToggleVisibility()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Hide();
        }
        else
        {
            ShowAndActivate();
        }
    }

    internal void ShowAndActivate()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void ConfirmAndExitApplication()
    {
        var result = MessageBox.Show(
            "Are you sure you want to stop Hermes and exit completely?",
            "Exit Hermes Hub",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2
        );

        if (result == DialogResult.Yes)
        {
            ExitApplication();
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _cliWebviewForm?.Dispose();
        _cliManager.Dispose();
        _lanProxyService.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIconOffline.Dispose();
        _trayIconStarting.Dispose();
        _trayIconOnline.Dispose();
        _currentQrBitmap?.Dispose();
        _clientsToolTip.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            var result = MessageBox.Show(
                "Are you sure you want to stop Hermes and exit completely?",
                "Exit Hermes Hub",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2
            );

            if (result == DialogResult.Yes)
            {
                ExitApplication();
                return;
            }

            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _ = ReleaseCapture();
            _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    private static void SetupButtonHover(Button button, Color normalColor, Color hoverColor)
    {
        button.MouseEnter += (_, _) => button.BackColor = hoverColor;
        button.MouseLeave += (_, _) => button.BackColor = normalColor;
    }

    private static Icon CreateAppIcon(Color accent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

        // Dark rounded background
        using var bgBrush = new SolidBrush(Color.FromArgb(9, 9, 11));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        // Status-colored border ring
        using var borderPen = new Pen(accent, 2);
        g.DrawEllipse(borderPen, 2, 2, 28, 28);

        // Status-colored "H" glyph
        using var font = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(accent);
            var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("H", font, textBrush, new RectangleF(0, 0, 32, 32), stringFormat);
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}

internal class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Color.FromArgb(39, 39, 42);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(39, 39, 42);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(39, 39, 42);
    public override Color MenuBorder => Color.FromArgb(39, 39, 42);
    public override Color ToolStripDropDownBackground => Color.FromArgb(24, 24, 27);
    public override Color ImageMarginGradientBegin => Color.FromArgb(24, 24, 27);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(24, 24, 27);
    public override Color ImageMarginGradientEnd => Color.FromArgb(24, 24, 27);
    public override Color SeparatorDark => Color.FromArgb(39, 39, 42);
    public override Color SeparatorLight => Color.FromArgb(39, 39, 42);
}
