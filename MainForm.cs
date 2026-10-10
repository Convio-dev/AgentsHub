using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using HermesHub.Helpers;
using HermesHub.Services;

namespace HermesHub;

public partial class MainForm : Form
{
    private HermesProcessService? _hermesService;
    private CliManagerService? _cliManager;
    private LanProxyService? _lanProxyService;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _trayContextMenu;

    private ToolStripMenuItem? _trayOpenWebUiItem;
    private ToolStripMenuItem? _trayStartItem;
    private ToolStripMenuItem? _trayStopItem;
    private ToolStripMenuItem? _trayBroadcastItem;

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
    private bool _firewallAllowed = false;
    private int _scrollOffsetY;
    private int _maxScrollY;
    private string? _currentPublicIp;
    private bool _isIpMasked = false;
    private static readonly HttpClient _publicIpHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

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
                BeginInvoke(() => _cliManager?.Start(CliType.Hermes));
            }
            else
            {
                _cliManager?.Start(CliType.Hermes);
            }
            return Task.CompletedTask;
        };

        _lanProxyService.CheckHermesAliveAction = () =>
        {
            return HermesProcessService.CheckPortAliveAsync(_hermesService?.Port ?? HermesProcessService.DefaultHermesPort, 400);
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

        // Populate CLI selection dropdown
        cliSelectComboBox.Items.Clear();
        cliSelectComboBox.Items.Add("Hermes (Web Dashboard)");
        cliSelectComboBox.Items.Add("Claude Code (CLI)");
        cliSelectComboBox.Items.Add("Antigravity CLI");

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

        InitializeTrayIcon();
        InitializeCustomStyles();
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

        _isIpMasked = StartupHelper.GetPreference("MaskPublicIp", false);
        _ = RefreshPublicIpAsync();

        // Apply theme initially
        ApplyTheme();

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

        HookMouseWheelRecursively(this);
        SetFormHeightToFitContent();
    }

    private void InitializeCustomStyles()
    {
        _appIcon = CreateAppIcon(Color.FromArgb(16, 185, 129));
        _trayIconOffline = CreateAppIcon(Color.FromArgb(239, 68, 68));
        _trayIconStarting = CreateAppIcon(Color.FromArgb(245, 158, 11));
        _trayIconOnline = CreateAppIcon(Color.FromArgb(16, 185, 129));
        Icon = _appIcon;

        // Custom borders for cards
        static void PaintBorder(Panel p, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var pen = new Pen(ThemeManager.Colors.CardBorder, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        }

        hermesCard.Paint += (s, e) => PaintBorder((Panel)s!, e);
        quickAppsCard.Paint += (s, e) => PaintBorder((Panel)s!, e);
        lanCard.Paint += (s, e) => PaintBorder((Panel)s!, e);
        settingsCard.Paint += (s, e) => PaintBorder((Panel)s!, e);
        logsPanel.Paint += (s, e) => PaintBorder((Panel)s!, e);
        qrDrawerPanel.Paint += (s, e) => PaintBorder((Panel)s!, e);
        lanUrlPanel.Paint += (s, e) => PaintBorder((Panel)s!, e);

        // Status dot antialiased circle
        statusDot.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(statusDot.BackColor);
            e.Graphics.FillEllipse(brush, 0, 0, statusDot.Width - 1, statusDot.Height - 1);
        };

        topBarPanel.Resize += (_, _) => LayoutTopBar();
        bottomBarPanel.Resize += (_, _) => LayoutBottomBar();
        scrollContainer.Resize += (_, _) => LayoutCards();
        Resize += (_, _) =>
        {
            LayoutTopBar();
            LayoutCards();
            LayoutBottomBar();
        };

        // Tooltips
        var toolTip = new ToolTip();
        toolTip.SetToolTip(themeToggleButton, "Toggle between Light and Dark mode");
        toolTip.SetToolTip(topWebviewButton, "Open CLI Workspace in embedded Webview");
        toolTip.SetToolTip(topLogsButton, "Show or hide system logs");
        toolTip.SetToolTip(launchFreeBuffButton, "Launch FreeBuff Desktop");
        toolTip.SetToolTip(launchClineButton, "Launch Cline Desktop App");
        toolTip.SetToolTip(launchDeepSeekButton, "Launch DeepSeek Harness");
        toolTip.SetToolTip(launchOpenCodeButton, "Launch OpenCode Desktop");
        toolTip.SetToolTip(resetOpenCodeQuotaButton, "Reset OpenCode Quota (Rotate Cloudflare WARP IP)");
        toolTip.SetToolTip(toggleIpMaskButton, "Show / Hide IP Address");
        toolTip.SetToolTip(refreshIpButton, "Refresh Public IP");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
        {
            ThemeManager.ApplyDwmTitleBarTheme(Handle);
            SetFormHeightToFitContent();
            LayoutCards();
            LayoutTopBar();
            LayoutBottomBar();
            CenterOnCurrentScreen();
        }
    }

    private void CenterOnCurrentScreen()
    {
        var screen = Screen.FromPoint(Cursor.Position) ?? Screen.FromControl(this) ?? Screen.PrimaryScreen;
        if (screen != null)
        {
            var area = screen.WorkingArea;
            Location = new Point(
                area.X + Math.Max(0, (area.Width - Width) / 2),
                area.Y + Math.Max(0, (area.Height - Height) / 2)
            );
        }
        else
        {
            CenterToScreen();
        }
    }

    private void LayoutTopBar()
    {
        if (topBarPanel == null) return;

        int clientWidth = topBarPanel.ClientSize.Width;
        int cardWidth = Math.Clamp(clientWidth - 28, 380, 480);
        int margin = Math.Max(14, (clientWidth - cardWidth) / 2);
        int cardRight = margin + cardWidth;

        statusDot.Location = new Point(margin, 16);
        titleLabel.Location = new Point(margin + 18, 11);
        subtitleLabel.Location = new Point(titleLabel.Right + 8, 14);

        int right = cardRight;
        topLogsButton.Location = new Point(right - topLogsButton.Width, 7);
        right = topLogsButton.Left - 6;

        topWebviewButton.Location = new Point(right - topWebviewButton.Width, 7);
        right = topWebviewButton.Left - 6;

        themeToggleButton.Location = new Point(right - themeToggleButton.Width, 7);

        // Hide subtitle on narrow window to prevent text collisions
        subtitleLabel.Visible = themeToggleButton.Left > (subtitleLabel.Location.X + subtitleLabel.PreferredWidth + 8);
    }

    private void LayoutBottomBar()
    {
        if (bottomBarPanel == null) return;

        int clientWidth = bottomBarPanel.ClientSize.Width;
        int cardWidth = Math.Clamp(clientWidth - 28, 380, 480);
        int margin = Math.Max(14, (clientWidth - cardWidth) / 2);
        int cardRight = margin + cardWidth;

        ramUsageLabel.Location = new Point(margin, 11);
        versionLabel.Location = new Point(ramUsageLabel.Right + 12, 11);

        quitAppButton.Location = new Point(cardRight - quitAppButton.Width, 6);
        footerLogsButton.Location = new Point(quitAppButton.Left - 6 - footerLogsButton.Width, 6);
    }

    private void HookMouseWheelRecursively(Control control)
    {
        if (control is not NumericUpDown && control is not TextBox)
        {
            control.MouseWheel += OnScrollContainerMouseWheel;
        }

        foreach (Control child in control.Controls)
        {
            HookMouseWheelRecursively(child);
        }
    }

    private void OnScrollContainerMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_maxScrollY <= 0) return;

        int scrollStep = 56;
        int newOffset = _scrollOffsetY - (Math.Sign(e.Delta) * scrollStep);
        newOffset = Math.Clamp(newOffset, 0, _maxScrollY);

        if (newOffset != _scrollOffsetY)
        {
            _scrollOffsetY = newOffset;
            LayoutCards();
        }
    }

    private void SetFormHeightToFitContent()
    {
        int extraQr = qrDrawerPanel.Visible ? 180 : 0;
        int logsH = logsPanel.Visible ? 212 : 0;

        // Base height accommodating AI dev tools card (780px), expanding for drawers
        int targetClientHeight = 780 + extraQr + logsH;

        var screen = Screen.FromControl(this) ?? Screen.PrimaryScreen;
        int maxAvailableHeight = (screen?.WorkingArea.Height ?? 1080) - 40;
        int finalHeight = Math.Min(targetClientHeight, maxAvailableHeight);

        MinimumSize = new Size(460, 560);
        ClientSize = new Size(ClientSize.Width, finalHeight);

        scrollContainer.AutoScroll = false;

        LayoutCards();
        LayoutTopBar();
        LayoutBottomBar();
    }

    private void LayoutCards()
    {
        if (scrollContainer == null) return;

        scrollContainer.SuspendLayout();

        int spacing = 12;
        int clientWidth = scrollContainer.ClientSize.Width;
        int cardWidth = Math.Clamp(clientWidth - 28, 380, 480);
        int margin = Math.Max(14, (clientWidth - cardWidth) / 2);

        // Precompute total content height for scroll bounds
        int lanH = qrDrawerPanel.Visible ? 330 : 150;
        int totalContentHeight = 10 + 162 + spacing + 152 + spacing + lanH + spacing + 176;
        if (logsPanel.Visible)
        {
            totalContentHeight += spacing + 200;
        }
        totalContentHeight += spacing + cardsSpacer.Height + 14;

        _maxScrollY = Math.Max(0, totalContentHeight - scrollContainer.ClientSize.Height);
        _scrollOffsetY = Math.Clamp(_scrollOffsetY, 0, _maxScrollY);

        int currentY = 10 - _scrollOffsetY;

        // 1. CLI Services Card
        hermesCard.Location = new Point(margin, currentY);
        hermesCard.Width = cardWidth;
        hermesCard.Height = 162;

        int btnCount = 4;
        int btnGap = 8;
        int availBtnWidth = cardWidth - 28 - ((btnCount - 1) * btnGap);
        int singleBtnW = availBtnWidth / btnCount;

        startHermesButton.Location = new Point(14, 76);
        startHermesButton.Size = new Size(singleBtnW, 36);

        stopHermesButton.Location = new Point(14 + singleBtnW + btnGap, 76);
        stopHermesButton.Size = new Size(singleBtnW, 36);

        openDashboardButton.Location = new Point(14 + (singleBtnW + btnGap) * 2, 76);
        openDashboardButton.Size = new Size(singleBtnW, 36);

        openBrowserMainButton.Location = new Point(14 + (singleBtnW + btnGap) * 3, 76);
        openBrowserMainButton.Size = new Size(singleBtnW, 36);

        currentY += hermesCard.Height + spacing;

        // 2. AI Dev Tools / Quick Apps Card
        quickAppsCard.Location = new Point(margin, currentY);
        quickAppsCard.Width = cardWidth;
        quickAppsCard.Height = 152;

        int innerW = cardWidth - 28;
        int row1Gap = 8;
        int row1BtnW = (innerW - (2 * row1Gap)) / 3;
        int row1Remaining = innerW - (row1BtnW * 2 + (2 * row1Gap));

        launchFreeBuffButton.Location = new Point(14, 38);
        launchFreeBuffButton.Size = new Size(row1BtnW, 34);

        launchClineButton.Location = new Point(14 + row1BtnW + row1Gap, 38);
        launchClineButton.Size = new Size(row1BtnW, 34);

        launchDeepSeekButton.Location = new Point(14 + (row1BtnW + row1Gap) * 2, 38);
        launchDeepSeekButton.Size = new Size(row1Remaining, 34);

        launchOpenCodeButton.Location = new Point(14, 80);
        launchOpenCodeButton.Size = new Size(innerW, 34);
        launchOpenCodeButton.Padding = new Padding(34, 0, 34, 0);

        resetOpenCodeQuotaButton.Location = new Point(launchOpenCodeButton.Width - 34, 3);
        resetOpenCodeQuotaButton.Size = new Size(30, 28);

        publicIpLabel.Location = new Point(14, 124);
        refreshIpButton.Location = new Point(cardWidth - 14 - 26, 121);
        refreshIpButton.Size = new Size(26, 22);

        toggleIpMaskButton.Location = new Point(refreshIpButton.Left - 4 - 26, 121);
        toggleIpMaskButton.Size = new Size(26, 22);

        currentY += quickAppsCard.Height + spacing;

        // 3. LAN Card
        lanCard.Location = new Point(margin, currentY);
        lanCard.Width = cardWidth;

        firewallStatusButton.Location = new Point(cardWidth - 146, 10);
        int actionBtnsW = 66 * 3 + 12;
        int urlWellW = cardWidth - 28 - actionBtnsW - 8;
        lanUrlPanel.Location = new Point(14, 104);
        lanUrlPanel.Size = new Size(urlWellW, 36);
        lanUrlTextBox.Width = urlWellW - 16;

        copyLanButton.Location = new Point(lanUrlPanel.Right + 8, 104);
        openLanButton.Location = new Point(copyLanButton.Right + 6, 104);
        toggleQrButton.Location = new Point(openLanButton.Right + 6, 104);

        if (qrDrawerPanel.Visible)
        {
            qrDrawerPanel.Location = new Point(14, 148);
            qrDrawerPanel.Width = cardWidth - 28;
            lanCard.Height = 330;
        }
        else
        {
            lanCard.Height = 150;
        }
        currentY += lanCard.Height + spacing;

        // 3. Settings Card
        settingsCard.Location = new Point(margin, currentY);
        settingsCard.Width = cardWidth;
        settingsCard.Height = 176;
        currentY += settingsCard.Height + spacing;

        // 4. Logs Panel (if visible)
        if (logsPanel.Visible)
        {
            logsPanel.Location = new Point(margin, currentY);
            logsPanel.Width = cardWidth;
            logsPanel.Height = 200;

            closeLogsButton.Location = new Point(cardWidth - 56, 8);
            clearLogsButton.Location = new Point(closeLogsButton.Left - 56, 8);
            copyLogsButton.Location = new Point(clearLogsButton.Left - 56, 8);
            logsTextBox.Location = new Point(12, 38);
            logsTextBox.Size = new Size(cardWidth - 24, 150);

            currentY += logsPanel.Height + spacing;
        }

        cardsSpacer.Location = new Point(margin, currentY);
        cardsSpacer.Size = new Size(cardWidth, 8);

        scrollContainer.ResumeLayout(true);
    }

    private void ApplyTheme()
    {
        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
        {
            return;
        }

        var c = ThemeManager.Colors;
        bool isLight = ThemeManager.CurrentTheme == AppTheme.Light;
        ThemeManager.ApplyDwmTitleBarTheme(Handle);

        BackColor = c.WindowBackground;
        topBarPanel.BackColor = c.HeaderBackground;
        bottomBarPanel.BackColor = c.FooterBackground;
        scrollContainer.BackColor = c.WindowBackground;

        titleLabel.ForeColor = c.TextPrimary;
        subtitleLabel.ForeColor = c.TextMuted;

        themeToggleButton.Text = isLight ? "☾ Dark" : "☀ Light";
        themeToggleButton.BackColor = c.ButtonBg;
        themeToggleButton.ForeColor = c.ButtonText;
        themeToggleButton.FlatAppearance.BorderColor = c.ButtonBorder;

        topWebviewButton.BackColor = c.ButtonBg;
        topWebviewButton.ForeColor = c.ButtonText;
        topWebviewButton.FlatAppearance.BorderColor = c.ButtonBorder;

        topLogsButton.BackColor = c.ButtonBg;
        topLogsButton.ForeColor = c.ButtonText;
        topLogsButton.FlatAppearance.BorderColor = c.ButtonBorder;

        // Cards backgrounds
        hermesCard.BackColor = c.CardBackground;
        quickAppsCard.BackColor = c.CardBackground;
        lanCard.BackColor = c.CardBackground;
        settingsCard.BackColor = c.CardBackground;
        logsPanel.BackColor = c.CardBackground;
        qrDrawerPanel.BackColor = c.WellBackground;
        lanUrlPanel.BackColor = c.WellBackground;

        // Headers and labels
        hermesCardTitle.ForeColor = c.TextMuted;
        quickAppsCardTitle.ForeColor = c.TextMuted;
        quickAppsSubtitleLabel.ForeColor = c.AccentGreen;
        lanCardTitle.ForeColor = c.TextMuted;
        settingsCardTitle.ForeColor = c.TextMuted;
        logsTitleLabel.ForeColor = c.TextMuted;
        logsCountLabel.ForeColor = c.AccentGreen;

        cliSelectComboBox.BackColor = c.WellBackground;
        cliSelectComboBox.ForeColor = c.TextPrimary;

        lanToggleCheckBox.ForeColor = c.TextPrimary;
        lanStatusLabel.ForeColor = c.TextSecondary;
        lanClientsLabel.ForeColor = c.TextSecondary;
        lanUrlTextBox.BackColor = c.WellBackground;
        lanUrlTextBox.ForeColor = c.AccentGreen;

        enginePortLabel.ForeColor = c.TextSecondary;
        lanPortLabel.ForeColor = c.TextSecondary;
        enginePortNumeric.BackColor = c.WellBackground;
        enginePortNumeric.ForeColor = c.TextPrimary;
        lanPortNumeric.BackColor = c.WellBackground;
        lanPortNumeric.ForeColor = c.TextPrimary;

        startWithWindowsCheckBox.ForeColor = c.CheckBoxText;
        autoStartHermesCheckBox.ForeColor = c.CheckBoxText;
        autoEnableLanCheckBox.ForeColor = c.CheckBoxText;
        notifyRemoteClientsCheckBox.ForeColor = c.CheckBoxText;

        // Console
        logsTextBox.BackColor = c.ConsoleBg;
        logsTextBox.ForeColor = c.ConsoleText;

        // Footer
        ramUsageLabel.ForeColor = c.TextMuted;
        versionLabel.ForeColor = c.TextMuted;
        quitAppButton.ForeColor = c.TextSecondary;
        quitAppButton.BackColor = c.ButtonBg;
        quitAppButton.FlatAppearance.BorderColor = c.ButtonBorder;

        // Buttons styling
        StyleStandardButton(launchFreeBuffButton);
        StyleStandardButton(launchClineButton);
        StyleStandardButton(launchDeepSeekButton);
        StyleStandardButton(launchOpenCodeButton);

        resetOpenCodeQuotaButton.FlatStyle = FlatStyle.Flat;
        resetOpenCodeQuotaButton.FlatAppearance.BorderSize = 0;
        resetOpenCodeQuotaButton.BackColor = Color.Transparent;
        resetOpenCodeQuotaButton.ForeColor = c.TextMuted;
        resetOpenCodeQuotaButton.Cursor = Cursors.Hand;
        resetOpenCodeQuotaButton.Font = new Font("Segoe UI", 9.5F);
        resetOpenCodeQuotaButton.MouseEnter -= OnResetButtonMouseEnter;
        resetOpenCodeQuotaButton.MouseLeave -= OnResetButtonMouseLeave;
        resetOpenCodeQuotaButton.MouseEnter += OnResetButtonMouseEnter;
        resetOpenCodeQuotaButton.MouseLeave += OnResetButtonMouseLeave;

        publicIpLabel.ForeColor = c.TextSecondary;
        toggleIpMaskButton.BackColor = Color.Transparent;
        toggleIpMaskButton.ForeColor = c.TextMuted;
        toggleIpMaskButton.FlatAppearance.BorderSize = 0;
        refreshIpButton.BackColor = Color.Transparent;
        refreshIpButton.ForeColor = c.TextMuted;
        refreshIpButton.FlatAppearance.BorderSize = 0;

        toggleIpMaskButton.MouseEnter -= OnResetButtonMouseEnter;
        toggleIpMaskButton.MouseLeave -= OnResetButtonMouseLeave;
        toggleIpMaskButton.MouseEnter += OnResetButtonMouseEnter;
        toggleIpMaskButton.MouseLeave += OnResetButtonMouseLeave;

        refreshIpButton.MouseEnter -= OnResetButtonMouseEnter;
        refreshIpButton.MouseLeave -= OnResetButtonMouseLeave;
        refreshIpButton.MouseEnter += OnResetButtonMouseEnter;
        refreshIpButton.MouseLeave += OnResetButtonMouseLeave;

        UpdatePublicIpDisplay();

        StyleStandardButton(copyLanButton);
        StyleStandardButton(openLanButton);
        StyleStandardButton(toggleQrButton);
        StyleStandardButton(closeQrButton);
        StyleStandardButton(savePortsButton);
        StyleStandardButton(copyLogsButton);
        StyleStandardButton(clearLogsButton);
        StyleStandardButton(closeLogsButton);
        StyleStandardButton(footerLogsButton);
        StyleStandardButton(openDashboardButton);
        StyleStandardButton(openBrowserMainButton);

        // QR labels
        qrTitleLabel.ForeColor = c.AccentGreen;
        qrDescLabel.ForeColor = c.TextSecondary;
        qrUrlLabel.ForeColor = c.TextMuted;

        // Tray Context Menu
        if (_trayContextMenu != null)
        {
            _trayContextMenu.BackColor = c.CardBackground;
            _trayContextMenu.ForeColor = c.TextPrimary;
            _trayContextMenu.Renderer = new ToolStripProfessionalRenderer(new CustomThemeColorTable(c));
        }

        UpdateCliStatusUi();
        UpdateFirewallUi(_firewallAllowed);

        // Also update child webview if open
        _cliWebviewForm?.ApplyTheme();

        hermesCard.Invalidate();
        quickAppsCard.Invalidate();
        lanCard.Invalidate();
        settingsCard.Invalidate();
        logsPanel.Invalidate();
        qrDrawerPanel.Invalidate();
        lanUrlPanel.Invalidate();
        statusDot.Invalidate();
    }

    private static void StyleStandardButton(Button btn)
    {
        var c = ThemeManager.Colors;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = c.ButtonBorder;
        btn.BackColor = c.ButtonBg;
        btn.ForeColor = c.ButtonText;
        btn.Cursor = Cursors.Hand;

        btn.MouseEnter -= OnButtonMouseEnter;
        btn.MouseLeave -= OnButtonMouseLeave;
        btn.MouseEnter += OnButtonMouseEnter;
        btn.MouseLeave += OnButtonMouseLeave;
    }

    private static void OnButtonMouseEnter(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            btn.BackColor = ThemeManager.Colors.ButtonHover;
        }
    }

    private static void OnButtonMouseLeave(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            btn.BackColor = ThemeManager.Colors.ButtonBg;
        }
    }

    private static void OnResetButtonMouseEnter(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            btn.BackColor = ThemeManager.Colors.ButtonHover;
            btn.ForeColor = ThemeManager.Colors.AccentGreen;
        }
    }

    private static void OnResetButtonMouseLeave(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            btn.BackColor = Color.Transparent;
            btn.ForeColor = ThemeManager.Colors.TextMuted;
        }
    }

    private void InitializeTrayIcon()
    {
        if (_trayContextMenu == null || _notifyIcon == null) return;

        var c = ThemeManager.Colors;
        _trayContextMenu.Renderer = new ToolStripProfessionalRenderer(new CustomThemeColorTable(c));
        _trayContextMenu.BackColor = c.CardBackground;
        _trayContextMenu.ForeColor = c.TextPrimary;

        _trayOpenWebUiItem = new ToolStripMenuItem("Open CLI Webview Hub", null, (_, _) => OpenCliWebview())
        {
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _trayStartItem = new ToolStripMenuItem("Start CLI", null, (_, _) => _cliManager?.Start(_selectedCliType));
        _trayStopItem = new ToolStripMenuItem("Stop CLI", null, async (_, _) =>
        {
            if (_cliManager != null)
            {
                await _cliManager.StopAsync(_selectedCliType);
            }
        })
        {
            Enabled = false
        };

        _trayBroadcastItem = new ToolStripMenuItem($"Broadcast on :{_lanProxyService?.ListenPort ?? 9191}", null, (_, _) =>
        {
            lanToggleCheckBox.Checked = !lanToggleCheckBox.Checked;
        })
        {
            CheckOnClick = false
        };

        var trayShowDashboardItem = new ToolStripMenuItem("Dashboard Window", null, (_, _) => ShowAndActivate());
        var trayExitItem = new ToolStripMenuItem("Exit Completely", null, (_, _) => ConfirmAndExitApplication());

        var trayToolsMenu = new ToolStripMenuItem("Launch AI Tools");
        trayToolsMenu.DropDownItems.Add(new ToolStripMenuItem("⚡ FreeBuff", null, (_, _) => LaunchApp("FreeBuff", @"C:\Users\Gaming\AppData\Local\Programs\@codebufffreebuff-desktop\Freebuff.exe")));
        trayToolsMenu.DropDownItems.Add(new ToolStripMenuItem("💻 OpenCode", null, (_, _) => LaunchApp("OpenCode", @"C:\Users\Gaming\AppData\Local\Programs\@opencodedesktop\OpenCode.exe")));
        trayToolsMenu.DropDownItems.Add(new ToolStripMenuItem("🔄 OpenCode Quota Reset", null, async (_, _) => await ResetOpenCodeQuotaAsync()));
        trayToolsMenu.DropDownItems.Add(new ToolStripMenuItem("🤖 Cline", null, (_, _) => LaunchApp("Cline", @"C:\Users\Gaming\AppData\Local\Cline\cline-app.exe")));
        trayToolsMenu.DropDownItems.Add(new ToolStripMenuItem("🧠 DeepSeek", null, (_, _) => LaunchApp("DeepSeek", @"E:\DeepSeek Harness\DeepSeek Harness.exe")));

        _trayContextMenu.Items.Add(_trayOpenWebUiItem);
        _trayContextMenu.Items.Add(trayToolsMenu);
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
        themeToggleButton.Click += (_, _) =>
        {
            ThemeManager.ToggleTheme();
            ApplyTheme();
        };

        topWebviewButton.Click += (_, _) => OpenCliWebview();
        topLogsButton.Click += (_, _) => ToggleLogsDrawer();

        // CLI controls
        startHermesButton.Click += (_, _) => _cliManager?.Start(_selectedCliType);
        stopHermesButton.Click += async (_, _) =>
        {
            if (_cliManager != null)
            {
                await _cliManager.StopAsync(_selectedCliType);
            }
        };
        openDashboardButton.Click += (_, _) => OpenCliWebview(_selectedCliType);
        openBrowserMainButton.Click += (_, _) => OpenBrowserForSelectedCli();

        // AI Dev Tools controls
        launchFreeBuffButton.Click += (_, _) => LaunchApp("FreeBuff", @"C:\Users\Gaming\AppData\Local\Programs\@codebufffreebuff-desktop\Freebuff.exe");
        launchClineButton.Click += (_, _) => LaunchApp("Cline", @"C:\Users\Gaming\AppData\Local\Cline\cline-app.exe");
        launchDeepSeekButton.Click += (_, _) => LaunchApp("DeepSeek", @"E:\DeepSeek Harness\DeepSeek Harness.exe");
        launchOpenCodeButton.Click += (_, _) => LaunchApp("OpenCode", @"C:\Users\Gaming\AppData\Local\Programs\@opencodedesktop\OpenCode.exe");
        resetOpenCodeQuotaButton.Click += async (_, _) => await ResetOpenCodeQuotaAsync();
        toggleIpMaskButton.Click += (_, _) =>
        {
            _isIpMasked = !_isIpMasked;
            StartupHelper.SetPreference("MaskPublicIp", _isIpMasked);
            UpdatePublicIpDisplay();
        };
        refreshIpButton.Click += async (_, _) => await RefreshPublicIpAsync();

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
        if (_lanProxyService != null)
        {
            _lanProxyService.LogMessage += (msg) => AppendLog("Proxy", msg);
        }

        if (_cliManager != null)
        {
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
        }

        // Telemetry update timer
        telemetryTimer.Interval = 2000;
        telemetryTimer.Tick += (_, _) => UpdateTelemetry();
        telemetryTimer.Start();
        UpdateTelemetry();
    }

    private async Task RefreshFirewallStatusAsync()
    {
        var port = _lanProxyService?.ListenPort ?? 9191;
        var allowed = await Task.Run(() => FirewallHelper.IsInboundRuleConfigured(port));
        _firewallAllowed = allowed;

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
        _firewallAllowed = allowed;
        var c = ThemeManager.Colors;

        if (allowed)
        {
            firewallStatusButton.Text = "Firewall: Allowed";
            firewallStatusButton.ForeColor = c.AccentGreen;
            firewallStatusButton.FlatAppearance.BorderColor = c.AccentGreen;
            firewallStatusButton.BackColor = c.ButtonBg;
        }
        else
        {
            firewallStatusButton.Text = "Allow in Firewall";
            firewallStatusButton.ForeColor = c.WarningAmber;
            firewallStatusButton.FlatAppearance.BorderColor = c.WarningAmber;
            firewallStatusButton.BackColor = c.ButtonBg;
        }
    }

    private async Task HandleFirewallButtonClickAsync()
    {
        var port = _lanProxyService?.ListenPort ?? 9191;
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

        if (_hermesService != null) _hermesService.Port = newEnginePort;
        if (_lanProxyService != null)
        {
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
        }

        if (qrDrawerPanel.Visible)
        {
            RefreshQrCodeImage();
        }

        if (_trayBroadcastItem != null)
        {
            _trayBroadcastItem.Text = $"Broadcast on :{newLanPort}";
        }
        lanToggleCheckBox.Text = $"Broadcast to LAN (Port {newLanPort})";

        savePortsButton.Text = "Saved!";
        savePortsButton.ForeColor = ThemeManager.Colors.AccentGreen;
        await Task.Delay(1200);
        savePortsButton.Text = "Apply Ports";
        savePortsButton.ForeColor = ThemeManager.Colors.ButtonText;

        _ = RefreshFirewallStatusAsync();
    }

    private void ShowRemoteConnectionBalloon(ConnectedClientInfo client)
    {
        _notifyIcon?.ShowBalloonTip(
            3000,
            "Remote Device Connected",
            $"Device {client.IpAddress} ({client.DeviceName}) connected to Hermes on port {_lanProxyService?.ListenPort ?? 9191}.",
            ToolTipIcon.Info
        );
    }

    private void UpdateCliStatusUi()
    {
        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime || _cliManager == null)
        {
            return;
        }

        var c = ThemeManager.Colors;
        var info = _cliManager.GetInfo(_selectedCliType);

        startHermesButton.Text = $"Start {info.ShortName}";
        stopHermesButton.Text = $"Stop {info.ShortName}";
        openDashboardButton.Text = "Webview";
        openBrowserMainButton.Text = $"Browser (:{GetCliPort(_selectedCliType)})";

        hermesStatusLabel.Text = info.StatusText;

        switch (info.State)
        {
            case CliState.Running:
                hermesStatusLabel.ForeColor = c.AccentGreen;
                startHermesButton.Enabled = false;
                startHermesButton.BackColor = c.ButtonBg;
                startHermesButton.ForeColor = c.TextMuted;
                stopHermesButton.Enabled = true;
                stopHermesButton.ForeColor = c.DangerRed;
                stopHermesButton.FlatAppearance.BorderColor = c.DangerRed;
                break;

            case CliState.Starting:
                hermesStatusLabel.ForeColor = c.WarningAmber;
                startHermesButton.Enabled = false;
                startHermesButton.BackColor = c.ButtonBg;
                startHermesButton.ForeColor = c.TextMuted;
                stopHermesButton.Enabled = true;
                stopHermesButton.ForeColor = c.TextSecondary;
                stopHermesButton.FlatAppearance.BorderColor = c.ButtonBorder;
                break;

            case CliState.Offline:
            case CliState.Error:
            default:
                hermesStatusLabel.ForeColor = c.DangerRed;
                startHermesButton.Enabled = true;
                startHermesButton.BackColor = c.AccentGreen;
                startHermesButton.ForeColor = c.AccentGreenText;
                stopHermesButton.Enabled = false;
                stopHermesButton.ForeColor = c.TextMuted;
                stopHermesButton.FlatAppearance.BorderColor = c.ButtonBorder;
                break;
        }

        // Multi-CLI Overview summary
        var activeCount = _cliManager.GetActiveCount();
        cliSummaryLabel.Text = $"Active: {activeCount}/3";
        cliSummaryLabel.ForeColor = activeCount > 0 ? c.AccentGreen : c.TextMuted;

        var hInfo = _cliManager.GetInfo(CliType.Hermes);
        var cInfo = _cliManager.GetInfo(CliType.ClaudeCode);
        var aInfo = _cliManager.GetInfo(CliType.AntigravityCli);

        static string Dot(CliState s) => s == CliState.Running ? "●" : (s == CliState.Starting ? "◐" : "○");
        cliDotsLabel.Text = $"Hermes: {Dot(hInfo.State)}   Claude: {Dot(cInfo.State)}   Antigravity: {Dot(aInfo.State)}";
        cliDotsLabel.ForeColor = c.TextSecondary;

        // Status dot in top bar
        statusDot.BackColor = activeCount > 0 ? c.StatusDotOnline : c.StatusDotOffline;
        statusDot.Invalidate();

        // System tray
        if (_notifyIcon != null)
        {
            _notifyIcon.Icon = activeCount > 0 ? _trayIconOnline : _trayIconOffline;
            _notifyIcon.Text = $"Hermes Hub - {activeCount} of 3 Active";
        }

        if (_trayStartItem != null)
        {
            _trayStartItem.Text = $"Start {info.ShortName}";
            _trayStartItem.Enabled = info.State == CliState.Offline;
        }

        if (_trayStopItem != null)
        {
            _trayStopItem.Text = $"Stop {info.ShortName}";
            _trayStopItem.Enabled = info.State == CliState.Running || info.State == CliState.Starting;
        }
    }

    private void LanToggleCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        var c = ThemeManager.Colors;
        if (lanToggleCheckBox.Checked)
        {
            if (_lanProxyService == null || _hermesService == null) return;

            var success = _lanProxyService.Start(_lanProxyService.ListenPort, _hermesService.Port);
            if (success)
            {
                var url = NetworkHelper.GetLanUrl(_lanProxyService.ListenPort);
                lanUrlTextBox.Text = url;
                lanStatusLabel.Text = "Status: Active and Broadcasting";
                lanStatusLabel.ForeColor = c.AccentGreen;
                if (_trayBroadcastItem != null) _trayBroadcastItem.Checked = true;

                if (qrDrawerPanel.Visible)
                {
                    RefreshQrCodeImage();
                }

                _notifyIcon?.ShowBalloonTip(
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
            _lanProxyService?.Stop();
            lanStatusLabel.Text = "Status: Local Only (Isolated)";
            lanStatusLabel.ForeColor = c.TextMuted;
            lanClientsLabel.Text = "Active Clients: 0 (No remote devices)";
            lanClientsLabel.ForeColor = c.TextMuted;
            lanUrlTextBox.Text = $"http://0.0.0.0:{_lanProxyService?.ListenPort ?? 9191}";
            if (_trayBroadcastItem != null) _trayBroadcastItem.Checked = false;

            if (qrDrawerPanel.Visible)
            {
                RefreshQrCodeImage();
            }
        }
    }

    private void ToggleQrDrawer(bool? forceShow = null)
    {
        var shouldShow = forceShow ?? !qrDrawerPanel.Visible;
        qrDrawerPanel.Visible = shouldShow;
        toggleQrButton.Text = shouldShow ? "Hide QR" : "QR";

        SetFormHeightToFitContent();

        if (shouldShow)
        {
            RefreshQrCodeImage();
        }
    }

    private void ToggleLogsDrawer(bool? forceShow = null)
    {
        var shouldShow = forceShow ?? !logsPanel.Visible;
        logsPanel.Visible = shouldShow;
        topLogsButton.Text = shouldShow ? "📜 Hide" : "📜 Logs";

        SetFormHeightToFitContent();

        if (shouldShow)
        {
            _scrollOffsetY = _maxScrollY;
            LayoutCards();
            logsTextBox.SelectionStart = logsTextBox.TextLength;
            logsTextBox.ScrollToCaret();
        }
        else
        {
            _scrollOffsetY = 0;
            LayoutCards();
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
            copyLogsButton.ForeColor = ThemeManager.Colors.AccentGreen;
            await Task.Delay(1200);
            copyLogsButton.Text = "Copy";
            copyLogsButton.ForeColor = ThemeManager.Colors.ButtonText;
        }
        catch
        {
            // Suppress clipboard access exceptions
        }
    }

    private void ClearLogsButton_Click(object? sender, EventArgs e)
    {
        logsTextBox.Clear();
        _hermesService?.ClearLogs();
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

        var c = ThemeManager.Colors;
        if (clients.Count == 0)
        {
            lanClientsLabel.Text = "Active Clients: 0 (No remote devices)";
            lanClientsLabel.ForeColor = c.TextMuted;
            _clientsToolTip.SetToolTip(lanClientsLabel, "No remote devices are currently connected.");
        }
        else
        {
            lanClientsLabel.ForeColor = c.AccentGreen;
            lanClientsLabel.Text = _lanProxyService?.GetConnectedClientsSummary() ?? "0 Clients";

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
            url = NetworkHelper.GetLanUrl(_lanProxyService?.ListenPort ?? 9191);
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
            copyLanButton.ForeColor = ThemeManager.Colors.AccentGreen;
            await Task.Delay(1500);
            copyLanButton.Text = "Copy";
            copyLanButton.ForeColor = ThemeManager.Colors.ButtonText;
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

    private void OpenBrowserForSelectedCli()
    {
        try
        {
            int port = GetCliPort(_selectedCliType);
            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private int GetCliPort(CliType type) => type switch
    {
        CliType.Hermes => _cliManager?.HermesService.Port ?? HermesProcessService.DefaultHermesPort,
        CliType.ClaudeCode => 9229,
        CliType.AntigravityCli => 9339,
        _ => 9119
    };

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
        ShowInTaskbar = true;
        if (!Visible)
        {
            Show();
        }
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        Focus();
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
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        _lanProxyService?.Dispose();
        _cliManager?.Dispose();
        telemetryTimer?.Stop();
        telemetryTimer?.Dispose();
        _clientsToolTip.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
        {
            base.OnFormClosing(e);
            return;
        }

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

        if (keyData == Keys.PageDown)
        {
            if (_maxScrollY > 0)
            {
                _scrollOffsetY = Math.Clamp(_scrollOffsetY + 120, 0, _maxScrollY);
                LayoutCards();
                return true;
            }
        }
        else if (keyData == Keys.PageUp)
        {
            if (_maxScrollY > 0)
            {
                _scrollOffsetY = Math.Clamp(_scrollOffsetY - 120, 0, _maxScrollY);
                LayoutCards();
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void LaunchApp(string name, string targetPath, string? arguments = null)
    {
        try
        {
            var cleanPath = targetPath.Trim('"');
            var startInfo = new ProcessStartInfo
            {
                FileName = cleanPath,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                startInfo.Arguments = arguments;
            }

            var dir = Path.GetDirectoryName(cleanPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                startInfo.WorkingDirectory = dir;
            }

            Process.Start(startInfo);
            AppendLog("Launcher", $"Started {name}");
        }
        catch (Exception ex)
        {
            AppendLog("Launcher", $"Failed to start {name}: {ex.Message}");
            MessageBox.Show(
                $"Failed to launch {name}:\n\n{ex.Message}\n\nTarget: {targetPath}",
                $"Launch Error - {name}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
    }

    private void UpdatePublicIpDisplay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(UpdatePublicIpDisplay);
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentPublicIp))
        {
            publicIpLabel.Text = "🌐 IP: Loading...";
            return;
        }

        publicIpLabel.Text = _isIpMasked ? "🌐 IP: ****" : $"🌐 IP: {_currentPublicIp}";
    }

    private async Task RefreshPublicIpAsync()
    {
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => publicIpLabel.Text = "🌐 IP: Fetching...");
            }
            else
            {
                publicIpLabel.Text = "🌐 IP: Fetching...";
            }

            var ip = await FetchPublicIpAsync();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                _currentPublicIp = ip;
            }
        }
        catch
        {
            // Ignore fetch issues
        }
        finally
        {
            UpdatePublicIpDisplay();
        }
    }

    private static async Task<string?> FetchPublicIpAsync(CancellationToken ct = default)
    {
        string[] endpoints =
        [
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://ifconfig.me/ip"
        ];

        foreach (var url in endpoints)
        {
            try
            {
                using var response = await _publicIpHttpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length <= 45 && !text.Contains('<'))
                    {
                        return text;
                    }
                }
            }
            catch
            {
                // Fallback to next endpoint
            }
        }

        return null;
    }

    private static async Task<bool> RunWarpCliCommandAsync(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "warp-cli.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task ResetOpenCodeQuotaAsync()
    {
        try
        {
            resetOpenCodeQuotaButton.Enabled = false;
            resetOpenCodeQuotaButton.Text = "⏳";
            publicIpLabel.Text = "🌐 IP: Rotating WARP...";
            AppendLog("QuotaReset", "Initiating Cloudflare WARP IP rotation for OpenCode...");

            string oldIp = _currentPublicIp ?? (await FetchPublicIpAsync()) ?? "unknown";

            // 1. Disconnect WARP
            await RunWarpCliCommandAsync("disconnect");
            await Task.Delay(1500);

            // 2. Connect WARP
            var connected = await RunWarpCliCommandAsync("connect");
            if (!connected)
            {
                await Task.Delay(1000);
                await RunWarpCliCommandAsync("connect");
            }

            // 3. Retry loop to wait until internet connection stabilizes
            string? newIp = null;
            for (int i = 0; i < 12; i++)
            {
                await Task.Delay(1800);
                var ip = await FetchPublicIpAsync();
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    newIp = ip;
                    if (oldIp == "unknown" || ip != oldIp)
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(newIp))
            {
                throw new Exception("Internet connection did not stabilize after Cloudflare WARP reconnect.");
            }

            // Key rotation fallback if IP did not change
            if (oldIp != "unknown" && newIp == oldIp)
            {
                await RunWarpCliCommandAsync("tunnel rotate-keys");
                await Task.Delay(2000);
                var retryIp = await FetchPublicIpAsync();
                if (!string.IsNullOrWhiteSpace(retryIp))
                {
                    newIp = retryIp;
                }
            }

            _currentPublicIp = newIp;
            UpdatePublicIpDisplay();

            string msg = oldIp != "unknown" && oldIp != newIp
                ? $"Public IP successfully rotated from {oldIp} to {newIp}!\n\nOpenCode quota refreshed."
                : $"WARP reconnected with active IP {newIp}!\n\nOpenCode quota refreshed.";

            AppendLog("QuotaReset", $"SUCCESS: {msg.Replace("\n\n", " ")}");
            MessageBox.Show(
                msg,
                "OpenCode Quota Reset Succeeded",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        catch (Exception ex)
        {
            AppendLog("QuotaReset", $"ERROR: {ex.Message}");
            _ = RefreshPublicIpAsync();
            MessageBox.Show(
                $"Quota Reset Failed:\n\n{ex.Message}",
                "Quota Reset Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
        finally
        {
            resetOpenCodeQuotaButton.Text = "🔄";
            resetOpenCodeQuotaButton.Enabled = true;
        }
    }

    private static Icon CreateAppIcon(Color accent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bgBrush = new SolidBrush(Color.FromArgb(16, 18, 24));
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            using var borderPen = new Pen(accent, 2);
            g.DrawEllipse(borderPen, 2, 2, 28, 28);

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

internal class CustomThemeColorTable(ThemeColors colors) : ProfessionalColorTable
{
    public override Color MenuItemSelected => colors.ButtonHover;
    public override Color MenuItemSelectedGradientBegin => colors.ButtonHover;
    public override Color MenuItemSelectedGradientEnd => colors.ButtonHover;
    public override Color MenuBorder => colors.CardBorder;
    public override Color ToolStripDropDownBackground => colors.CardBackground;
    public override Color ImageMarginGradientBegin => colors.CardBackground;
    public override Color ImageMarginGradientMiddle => colors.CardBackground;
    public override Color ImageMarginGradientEnd => colors.CardBackground;
    public override Color SeparatorDark => colors.CardBorder;
    public override Color SeparatorLight => colors.CardBorder;
}
