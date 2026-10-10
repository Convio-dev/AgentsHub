#nullable enable

namespace HermesHub;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private Panel topBarPanel = null!;
    private Panel statusDot = null!;
    private Label titleLabel = null!;
    private Label subtitleLabel = null!;
    private Button themeToggleButton = null!;
    private Button topWebviewButton = null!;
    private Button topLogsButton = null!;

    private Panel scrollContainer = null!;

    private Panel hermesCard = null!;
    private Label hermesCardTitle = null!;
    private Label cliSummaryLabel = null!;
    private ComboBox cliSelectComboBox = null!;
    private Label hermesStatusLabel = null!;
    private Panel antigravityProfilePanel = null!;
    private Label antigravityProfileLabel = null!;
    private ComboBox antigravityProfileComboBox = null!;
    private Button addAntigravityProfileButton = null!;
    private Button renameAntigravityProfileButton = null!;
    private Button deleteAntigravityProfileButton = null!;
    private Button startHermesButton = null!;
    private Button stopHermesButton = null!;
    private Button openDashboardButton = null!;
    private Button openBrowserMainButton = null!;
    private Label cliDotsLabel = null!;

    private Panel quickAppsCard = null!;
    private Label quickAppsCardTitle = null!;
    private Label quickAppsSubtitleLabel = null!;
    private Button launchFreeBuffButton = null!;
    private Button launchClineButton = null!;
    private Button launchDeepSeekButton = null!;
    private Button launchOpenCodeButton = null!;
    private Button launchAntigravityIdeButton = null!;
    private Button launchClaudeButton = null!;
    private Button launchChatGptButton = null!;
    private Button resetOpenCodeQuotaButton = null!;
    private Label publicIpLabel = null!;
    private Button toggleIpMaskButton = null!;
    private Button refreshIpButton = null!;

    private Panel lanCard = null!;
    private Label lanCardTitle = null!;
    private CheckBox lanToggleCheckBox = null!;
    private Button firewallStatusButton = null!;
    private Label lanStatusLabel = null!;
    private Label lanClientsLabel = null!;
    private Panel lanUrlPanel = null!;
    private TextBox lanUrlTextBox = null!;
    private Button copyLanButton = null!;
    private Button openLanButton = null!;
    private Button toggleQrButton = null!;

    private Panel qrDrawerPanel = null!;
    private PictureBox qrPictureBox = null!;
    private Label qrTitleLabel = null!;
    private Label qrDescLabel = null!;
    private Label qrUrlLabel = null!;
    private Button closeQrButton = null!;

    private Panel settingsCard = null!;
    private Label settingsCardTitle = null!;
    private Label enginePortLabel = null!;
    private NumericUpDown enginePortNumeric = null!;
    private Label lanPortLabel = null!;
    private NumericUpDown lanPortNumeric = null!;
    private Button savePortsButton = null!;
    private CheckBox startWithWindowsCheckBox = null!;
    private CheckBox autoStartHermesCheckBox = null!;
    private CheckBox autoEnableLanCheckBox = null!;
    private CheckBox notifyRemoteClientsCheckBox = null!;

    private Panel logsPanel = null!;
    private Label logsTitleLabel = null!;
    private Label logsCountLabel = null!;
    private Button copyLogsButton = null!;
    private Button clearLogsButton = null!;
    private Button closeLogsButton = null!;
    private TextBox logsTextBox = null!;

    private Panel cardsSpacer = null!;

    private Panel bottomBarPanel = null!;
    private Label ramUsageLabel = null!;
    private Label versionLabel = null!;
    private Button footerLogsButton = null!;
    private Button quitAppButton = null!;

    private System.Windows.Forms.Timer telemetryTimer = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        topBarPanel = new Panel();
        statusDot = new Panel();
        titleLabel = new Label();
        subtitleLabel = new Label();
        themeToggleButton = new Button();
        topWebviewButton = new Button();
        topLogsButton = new Button();

        scrollContainer = new Panel();

        hermesCard = new Panel();
        hermesCardTitle = new Label();
        cliSummaryLabel = new Label();
        cliSelectComboBox = new ComboBox();
        hermesStatusLabel = new Label();
        antigravityProfilePanel = new Panel();
        antigravityProfileLabel = new Label();
        antigravityProfileComboBox = new ComboBox();
        addAntigravityProfileButton = new Button();
        renameAntigravityProfileButton = new Button();
        deleteAntigravityProfileButton = new Button();
        startHermesButton = new Button();
        stopHermesButton = new Button();
        openDashboardButton = new Button();
        openBrowserMainButton = new Button();
        cliDotsLabel = new Label();

        quickAppsCard = new Panel();
        quickAppsCardTitle = new Label();
        quickAppsSubtitleLabel = new Label();
        launchFreeBuffButton = new Button();
        launchClineButton = new Button();
        launchDeepSeekButton = new Button();
        launchOpenCodeButton = new Button();
        launchAntigravityIdeButton = new Button();
        launchClaudeButton = new Button();
        launchChatGptButton = new Button();
        resetOpenCodeQuotaButton = new Button();
        publicIpLabel = new Label();
        toggleIpMaskButton = new Button();
        refreshIpButton = new Button();

        lanCard = new Panel();
        lanCardTitle = new Label();
        lanToggleCheckBox = new CheckBox();
        firewallStatusButton = new Button();
        lanStatusLabel = new Label();
        lanClientsLabel = new Label();
        lanUrlPanel = new Panel();
        lanUrlTextBox = new TextBox();
        copyLanButton = new Button();
        openLanButton = new Button();
        toggleQrButton = new Button();

        qrDrawerPanel = new Panel();
        qrPictureBox = new PictureBox();
        qrTitleLabel = new Label();
        qrDescLabel = new Label();
        qrUrlLabel = new Label();
        closeQrButton = new Button();

        settingsCard = new Panel();
        settingsCardTitle = new Label();
        enginePortLabel = new Label();
        enginePortNumeric = new NumericUpDown();
        lanPortLabel = new Label();
        lanPortNumeric = new NumericUpDown();
        savePortsButton = new Button();
        startWithWindowsCheckBox = new CheckBox();
        autoStartHermesCheckBox = new CheckBox();
        autoEnableLanCheckBox = new CheckBox();
        notifyRemoteClientsCheckBox = new CheckBox();

        logsPanel = new Panel();
        logsTitleLabel = new Label();
        logsCountLabel = new Label();
        copyLogsButton = new Button();
        clearLogsButton = new Button();
        closeLogsButton = new Button();
        logsTextBox = new TextBox();

        cardsSpacer = new Panel();

        bottomBarPanel = new Panel();
        ramUsageLabel = new Label();
        versionLabel = new Label();
        footerLogsButton = new Button();
        quitAppButton = new Button();

        telemetryTimer = new System.Windows.Forms.Timer(components);

        ((System.ComponentModel.ISupportInitialize)qrPictureBox).BeginInit();
        ((System.ComponentModel.ISupportInitialize)enginePortNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)lanPortNumeric).BeginInit();
        SuspendLayout();

        // 
        // Top Bar Panel
        // 
        topBarPanel.Dock = DockStyle.Top;
        topBarPanel.Height = 44;
        topBarPanel.Controls.Add(statusDot);
        topBarPanel.Controls.Add(titleLabel);
        topBarPanel.Controls.Add(subtitleLabel);
        topBarPanel.Controls.Add(topLogsButton);
        topBarPanel.Controls.Add(topWebviewButton);
        topBarPanel.Controls.Add(themeToggleButton);

        statusDot.Location = new Point(14, 16);
        statusDot.Size = new Size(11, 11);

        titleLabel.AutoSize = true;
        titleLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        titleLabel.Location = new Point(32, 11);
        titleLabel.Text = "AGENTS HUB";

        subtitleLabel.AutoSize = true;
        subtitleLabel.Font = new Font("Segoe UI", 8.5F);
        subtitleLabel.Location = new Point(135, 14);
        subtitleLabel.Text = "Desktop Controller";

        themeToggleButton.FlatStyle = FlatStyle.Flat;
        themeToggleButton.FlatAppearance.BorderSize = 1;
        themeToggleButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        themeToggleButton.Location = new Point(240, 7);
        themeToggleButton.Size = new Size(74, 30);
        themeToggleButton.Text = "☀ Light";
        themeToggleButton.Cursor = Cursors.Hand;

        topWebviewButton.FlatStyle = FlatStyle.Flat;
        topWebviewButton.FlatAppearance.BorderSize = 1;
        topWebviewButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        topWebviewButton.Location = new Point(320, 7);
        topWebviewButton.Size = new Size(88, 30);
        topWebviewButton.Text = "🖥 Webview";
        topWebviewButton.Cursor = Cursors.Hand;

        topLogsButton.FlatStyle = FlatStyle.Flat;
        topLogsButton.FlatAppearance.BorderSize = 1;
        topLogsButton.Font = new Font("Segoe UI", 8.5F);
        topLogsButton.Location = new Point(414, 7);
        topLogsButton.Size = new Size(68, 30);
        topLogsButton.Text = "📜 Logs";
        topLogsButton.Cursor = Cursors.Hand;

        // 
        // Bottom Bar Panel
        // 
        bottomBarPanel.Dock = DockStyle.Bottom;
        bottomBarPanel.Height = 38;
        bottomBarPanel.Controls.Add(ramUsageLabel);
        bottomBarPanel.Controls.Add(versionLabel);
        bottomBarPanel.Controls.Add(footerLogsButton);
        bottomBarPanel.Controls.Add(quitAppButton);

        ramUsageLabel.AutoSize = true;
        ramUsageLabel.Font = new Font("Segoe UI", 8.5F);
        ramUsageLabel.Location = new Point(14, 11);
        ramUsageLabel.Text = "RAM: -- MB";

        versionLabel.AutoSize = true;
        versionLabel.Font = new Font("Segoe UI", 8.5F);
        versionLabel.Location = new Point(110, 11);
        versionLabel.Text = "v1.0.2";

        footerLogsButton.FlatStyle = FlatStyle.Flat;
        footerLogsButton.FlatAppearance.BorderSize = 1;
        footerLogsButton.Font = new Font("Segoe UI", 8F);
        footerLogsButton.Location = new Point(292, 6);
        footerLogsButton.Size = new Size(96, 26);
        footerLogsButton.Text = "Console (0)";
        footerLogsButton.Cursor = Cursors.Hand;

        quitAppButton.FlatStyle = FlatStyle.Flat;
        quitAppButton.FlatAppearance.BorderSize = 1;
        quitAppButton.Font = new Font("Segoe UI", 8.5F);
        quitAppButton.Location = new Point(396, 6);
        quitAppButton.Size = new Size(86, 26);
        quitAppButton.Text = "Quit App";
        quitAppButton.Cursor = Cursors.Hand;

        // 
        // Scroll Container (dock fill)
        // 
        scrollContainer.Dock = DockStyle.Fill;
        scrollContainer.AutoScroll = false;
        scrollContainer.Controls.Add(hermesCard);
        scrollContainer.Controls.Add(quickAppsCard);
        scrollContainer.Controls.Add(lanCard);
        scrollContainer.Controls.Add(settingsCard);
        scrollContainer.Controls.Add(logsPanel);
        scrollContainer.Controls.Add(cardsSpacer);

        // 
        // Hermes / CLI Services Card
        // 
        hermesCard.Controls.Add(hermesCardTitle);
        hermesCard.Controls.Add(cliSummaryLabel);
        hermesCard.Controls.Add(cliSelectComboBox);
        hermesCard.Controls.Add(hermesStatusLabel);
        hermesCard.Controls.Add(antigravityProfilePanel);
        hermesCard.Controls.Add(startHermesButton);
        hermesCard.Controls.Add(stopHermesButton);
        hermesCard.Controls.Add(openDashboardButton);
        hermesCard.Controls.Add(openBrowserMainButton);
        hermesCard.Controls.Add(cliDotsLabel);

        antigravityProfilePanel.Controls.Add(antigravityProfileLabel);
        antigravityProfilePanel.Controls.Add(antigravityProfileComboBox);
        antigravityProfilePanel.Controls.Add(addAntigravityProfileButton);
        antigravityProfilePanel.Controls.Add(renameAntigravityProfileButton);
        antigravityProfilePanel.Controls.Add(deleteAntigravityProfileButton);
        antigravityProfilePanel.Location = new Point(14, 72);
        antigravityProfilePanel.Size = new Size(350, 32);
        antigravityProfilePanel.Visible = false;

        antigravityProfileLabel.AutoSize = true;
        antigravityProfileLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        antigravityProfileLabel.Location = new Point(0, 6);
        antigravityProfileLabel.Text = "Account:";

        antigravityProfileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        antigravityProfileComboBox.FlatStyle = FlatStyle.Flat;
        antigravityProfileComboBox.Font = new Font("Segoe UI", 9F);
        antigravityProfileComboBox.Location = new Point(62, 2);
        antigravityProfileComboBox.Size = new Size(180, 26);
        antigravityProfileComboBox.Cursor = Cursors.Hand;

        addAntigravityProfileButton.FlatStyle = FlatStyle.Flat;
        addAntigravityProfileButton.FlatAppearance.BorderSize = 1;
        addAntigravityProfileButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        addAntigravityProfileButton.Location = new Point(246, 1);
        addAntigravityProfileButton.Size = new Size(50, 28);
        addAntigravityProfileButton.Text = "+ Add";
        addAntigravityProfileButton.Cursor = Cursors.Hand;

        renameAntigravityProfileButton.FlatStyle = FlatStyle.Flat;
        renameAntigravityProfileButton.FlatAppearance.BorderSize = 1;
        renameAntigravityProfileButton.Font = new Font("Segoe UI", 8.5F);
        renameAntigravityProfileButton.Location = new Point(298, 1);
        renameAntigravityProfileButton.Size = new Size(54, 28);
        renameAntigravityProfileButton.Text = "Rename";
        renameAntigravityProfileButton.Cursor = Cursors.Hand;

        deleteAntigravityProfileButton.FlatStyle = FlatStyle.Flat;
        deleteAntigravityProfileButton.FlatAppearance.BorderSize = 1;
        deleteAntigravityProfileButton.Font = new Font("Segoe UI", 8.5F);
        deleteAntigravityProfileButton.Location = new Point(354, 1);
        deleteAntigravityProfileButton.Size = new Size(38, 28);
        deleteAntigravityProfileButton.Text = "Del";
        deleteAntigravityProfileButton.Cursor = Cursors.Hand;

        hermesCardTitle.AutoSize = true;
        hermesCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        hermesCardTitle.Location = new Point(14, 12);
        hermesCardTitle.Text = "CLI SERVICES";

        cliSummaryLabel.AutoSize = true;
        cliSummaryLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        cliSummaryLabel.Location = new Point(96, 12);
        cliSummaryLabel.Text = "Active: 0/3";

        cliSelectComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        cliSelectComboBox.FlatStyle = FlatStyle.Flat;
        cliSelectComboBox.Font = new Font("Segoe UI", 9.5F);
        cliSelectComboBox.Location = new Point(14, 38);
        cliSelectComboBox.Size = new Size(200, 28);
        cliSelectComboBox.Cursor = Cursors.Hand;

        hermesStatusLabel.AutoSize = true;
        hermesStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        hermesStatusLabel.Location = new Point(224, 43);
        hermesStatusLabel.Text = "Engine: Offline";

        startHermesButton.FlatStyle = FlatStyle.Flat;
        startHermesButton.FlatAppearance.BorderSize = 0;
        startHermesButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        startHermesButton.Size = new Size(96, 36);
        startHermesButton.Text = "Start Hermes";
        startHermesButton.Cursor = Cursors.Hand;

        stopHermesButton.FlatStyle = FlatStyle.Flat;
        stopHermesButton.FlatAppearance.BorderSize = 1;
        stopHermesButton.Font = new Font("Segoe UI", 8.75F);
        stopHermesButton.Size = new Size(96, 36);
        stopHermesButton.Text = "Stop Hermes";
        stopHermesButton.Cursor = Cursors.Hand;

        openDashboardButton.FlatStyle = FlatStyle.Flat;
        openDashboardButton.FlatAppearance.BorderSize = 1;
        openDashboardButton.Font = new Font("Segoe UI", 8.75F);
        openDashboardButton.Size = new Size(96, 36);
        openDashboardButton.Text = "Webview";
        openDashboardButton.Cursor = Cursors.Hand;

        openBrowserMainButton.FlatStyle = FlatStyle.Flat;
        openBrowserMainButton.FlatAppearance.BorderSize = 1;
        openBrowserMainButton.Font = new Font("Segoe UI", 8.75F);
        openBrowserMainButton.Size = new Size(96, 36);
        openBrowserMainButton.Text = "Browser";
        openBrowserMainButton.Cursor = Cursors.Hand;

        cliDotsLabel.AutoSize = true;
        cliDotsLabel.Font = new Font("Segoe UI", 8.5F);
        cliDotsLabel.Location = new Point(14, 126);
        cliDotsLabel.Text = "Hermes: ○ Offline   Claude: ○ Offline   Antigravity: ○ Offline";

        // 
        // AI Dev Tools Card
        // 
        quickAppsCard.Controls.Add(quickAppsCardTitle);
        quickAppsCard.Controls.Add(quickAppsSubtitleLabel);
        quickAppsCard.Controls.Add(launchFreeBuffButton);
        quickAppsCard.Controls.Add(launchClineButton);
        quickAppsCard.Controls.Add(launchDeepSeekButton);
        quickAppsCard.Controls.Add(launchOpenCodeButton);
        quickAppsCard.Controls.Add(launchAntigravityIdeButton);
        quickAppsCard.Controls.Add(launchClaudeButton);
        quickAppsCard.Controls.Add(launchChatGptButton);
        quickAppsCard.Controls.Add(publicIpLabel);
        quickAppsCard.Controls.Add(toggleIpMaskButton);
        quickAppsCard.Controls.Add(refreshIpButton);

        quickAppsCardTitle.AutoSize = true;
        quickAppsCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        quickAppsCardTitle.Location = new Point(14, 12);
        quickAppsCardTitle.Text = "AI DEV TOOLS";

        quickAppsSubtitleLabel.AutoSize = true;
        quickAppsSubtitleLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        quickAppsSubtitleLabel.Location = new Point(104, 12);
        quickAppsSubtitleLabel.Text = "Quick Launch";

        launchFreeBuffButton.FlatStyle = FlatStyle.Flat;
        launchFreeBuffButton.FlatAppearance.BorderSize = 1;
        launchFreeBuffButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        launchFreeBuffButton.Text = "⚡ FreeBuff";
        launchFreeBuffButton.Cursor = Cursors.Hand;

        launchClineButton.FlatStyle = FlatStyle.Flat;
        launchClineButton.FlatAppearance.BorderSize = 1;
        launchClineButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        launchClineButton.Text = "🤖 Cline";
        launchClineButton.Cursor = Cursors.Hand;

        launchDeepSeekButton.FlatStyle = FlatStyle.Flat;
        launchDeepSeekButton.FlatAppearance.BorderSize = 1;
        launchDeepSeekButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        launchDeepSeekButton.Text = "🧠 DeepSeek";
        launchDeepSeekButton.Cursor = Cursors.Hand;

        launchOpenCodeButton.FlatStyle = FlatStyle.Flat;
        launchOpenCodeButton.FlatAppearance.BorderSize = 1;
        launchOpenCodeButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        launchOpenCodeButton.Text = "💻 OpenCode";
        launchOpenCodeButton.Cursor = Cursors.Hand;
        launchOpenCodeButton.Controls.Add(resetOpenCodeQuotaButton);

        launchAntigravityIdeButton.FlatStyle = FlatStyle.Flat;
        launchAntigravityIdeButton.FlatAppearance.BorderSize = 1;
        launchAntigravityIdeButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        launchAntigravityIdeButton.Text = "🪐 Antigravity-ide";
        launchAntigravityIdeButton.Cursor = Cursors.Hand;

        launchClaudeButton.FlatStyle = FlatStyle.Flat;
        launchClaudeButton.FlatAppearance.BorderSize = 1;
        launchClaudeButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        launchClaudeButton.Text = "✳️ Claude";
        launchClaudeButton.Cursor = Cursors.Hand;

        launchChatGptButton.FlatStyle = FlatStyle.Flat;
        launchChatGptButton.FlatAppearance.BorderSize = 1;
        launchChatGptButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        launchChatGptButton.Text = "💬 ChatGPT";
        launchChatGptButton.Cursor = Cursors.Hand;

        resetOpenCodeQuotaButton.FlatStyle = FlatStyle.Flat;
        resetOpenCodeQuotaButton.FlatAppearance.BorderSize = 0;
        resetOpenCodeQuotaButton.Font = new Font("Segoe UI", 9.5F);
        resetOpenCodeQuotaButton.Text = "🔄";
        resetOpenCodeQuotaButton.Cursor = Cursors.Hand;

        publicIpLabel.AutoSize = true;
        publicIpLabel.Font = new Font("Segoe UI", 8.25F);
        publicIpLabel.Location = new Point(14, 124);
        publicIpLabel.Text = "🌐 IP: Loading...";

        toggleIpMaskButton.FlatStyle = FlatStyle.Flat;
        toggleIpMaskButton.FlatAppearance.BorderSize = 0;
        toggleIpMaskButton.Font = new Font("Segoe UI", 8.5F);
        toggleIpMaskButton.Text = "👁";
        toggleIpMaskButton.Cursor = Cursors.Hand;

        refreshIpButton.FlatStyle = FlatStyle.Flat;
        refreshIpButton.FlatAppearance.BorderSize = 0;
        refreshIpButton.Font = new Font("Segoe UI", 8.5F);
        refreshIpButton.Text = "↻";
        refreshIpButton.Cursor = Cursors.Hand;

        // 
        // LAN Card
        // 
        lanCard.Controls.Add(lanCardTitle);
        lanCard.Controls.Add(lanToggleCheckBox);
        lanCard.Controls.Add(firewallStatusButton);
        lanCard.Controls.Add(lanStatusLabel);
        lanCard.Controls.Add(lanClientsLabel);
        lanCard.Controls.Add(lanUrlPanel);
        lanCard.Controls.Add(copyLanButton);
        lanCard.Controls.Add(openLanButton);
        lanCard.Controls.Add(toggleQrButton);
        lanCard.Controls.Add(qrDrawerPanel);

        lanCardTitle.AutoSize = true;
        lanCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lanCardTitle.Location = new Point(14, 12);
        lanCardTitle.Text = "NETWORK & LAN BROADCAST";

        lanToggleCheckBox.AutoSize = true;
        lanToggleCheckBox.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        lanToggleCheckBox.Location = new Point(14, 36);
        lanToggleCheckBox.Size = new Size(224, 21);
        lanToggleCheckBox.Text = "Broadcast to LAN (Port 9191)";
        lanToggleCheckBox.Cursor = Cursors.Hand;

        firewallStatusButton.FlatStyle = FlatStyle.Flat;
        firewallStatusButton.FlatAppearance.BorderSize = 1;
        firewallStatusButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        firewallStatusButton.Size = new Size(138, 26);
        firewallStatusButton.Text = "Checking Firewall...";
        firewallStatusButton.Cursor = Cursors.Hand;

        lanStatusLabel.AutoSize = true;
        lanStatusLabel.Font = new Font("Segoe UI", 8.5F);
        lanStatusLabel.Location = new Point(14, 60);
        lanStatusLabel.Text = "Status: Local Only (Isolated)";

        lanClientsLabel.AutoSize = false;
        lanClientsLabel.Font = new Font("Segoe UI", 8.5F);
        lanClientsLabel.Location = new Point(14, 82);
        lanClientsLabel.Size = new Size(390, 18);
        lanClientsLabel.Text = "Active Clients: 0 (No remote devices)";

        lanUrlPanel.Location = new Point(14, 104);
        lanUrlPanel.Size = new Size(200, 36);
        lanUrlPanel.Controls.Add(lanUrlTextBox);

        lanUrlTextBox.BorderStyle = BorderStyle.None;
        lanUrlTextBox.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        lanUrlTextBox.Location = new Point(8, 8);
        lanUrlTextBox.Size = new Size(184, 20);
        lanUrlTextBox.ReadOnly = true;
        lanUrlTextBox.Text = "http://0.0.0.0:9191";

        copyLanButton.FlatStyle = FlatStyle.Flat;
        copyLanButton.FlatAppearance.BorderSize = 1;
        copyLanButton.Font = new Font("Segoe UI", 8.5F);
        copyLanButton.Size = new Size(64, 36);
        copyLanButton.Text = "Copy";
        copyLanButton.Cursor = Cursors.Hand;

        openLanButton.FlatStyle = FlatStyle.Flat;
        openLanButton.FlatAppearance.BorderSize = 1;
        openLanButton.Font = new Font("Segoe UI", 8.5F);
        openLanButton.Size = new Size(64, 36);
        openLanButton.Text = "Open";
        openLanButton.Cursor = Cursors.Hand;

        toggleQrButton.FlatStyle = FlatStyle.Flat;
        toggleQrButton.FlatAppearance.BorderSize = 1;
        toggleQrButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        toggleQrButton.Size = new Size(64, 36);
        toggleQrButton.Text = "QR";
        toggleQrButton.Cursor = Cursors.Hand;

        // 
        // QR Drawer Panel (collapsible)
        // 
        qrDrawerPanel.Location = new Point(14, 148);
        qrDrawerPanel.Size = new Size(390, 168);
        qrDrawerPanel.Visible = false;
        qrDrawerPanel.Controls.Add(qrPictureBox);
        qrDrawerPanel.Controls.Add(qrTitleLabel);
        qrDrawerPanel.Controls.Add(qrDescLabel);
        qrDrawerPanel.Controls.Add(qrUrlLabel);
        qrDrawerPanel.Controls.Add(closeQrButton);

        qrPictureBox.Location = new Point(14, 14);
        qrPictureBox.Size = new Size(136, 136);
        qrPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        qrPictureBox.BackColor = Color.White;

        qrTitleLabel.AutoSize = true;
        qrTitleLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        qrTitleLabel.Location = new Point(164, 16);
        qrTitleLabel.Text = "Scan to Open on Mobile";

        qrDescLabel.Font = new Font("Segoe UI", 8.5F);
        qrDescLabel.Location = new Point(164, 40);
        qrDescLabel.Size = new Size(210, 42);
        qrDescLabel.Text = "Scan with your phone camera to open Hermes directly on the LAN.";

        qrUrlLabel.Font = new Font("Segoe UI", 8F);
        qrUrlLabel.Location = new Point(164, 88);
        qrUrlLabel.Size = new Size(210, 20);
        qrUrlLabel.Text = "http://127.0.0.1:9191";

        closeQrButton.FlatStyle = FlatStyle.Flat;
        closeQrButton.FlatAppearance.BorderSize = 1;
        closeQrButton.Font = new Font("Segoe UI", 8F);
        closeQrButton.Location = new Point(164, 118);
        closeQrButton.Size = new Size(80, 26);
        closeQrButton.Text = "Hide QR";
        closeQrButton.Cursor = Cursors.Hand;

        // 
        // Settings Card
        // 
        settingsCard.Controls.Add(settingsCardTitle);
        settingsCard.Controls.Add(enginePortLabel);
        settingsCard.Controls.Add(enginePortNumeric);
        settingsCard.Controls.Add(lanPortLabel);
        settingsCard.Controls.Add(lanPortNumeric);
        settingsCard.Controls.Add(savePortsButton);
        settingsCard.Controls.Add(startWithWindowsCheckBox);
        settingsCard.Controls.Add(autoStartHermesCheckBox);
        settingsCard.Controls.Add(autoEnableLanCheckBox);
        settingsCard.Controls.Add(notifyRemoteClientsCheckBox);

        settingsCardTitle.AutoSize = true;
        settingsCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        settingsCardTitle.Location = new Point(14, 12);
        settingsCardTitle.Text = "SETTINGS & AUTOMATION";

        enginePortLabel.AutoSize = true;
        enginePortLabel.Font = new Font("Segoe UI", 8.75F);
        enginePortLabel.Location = new Point(14, 36);
        enginePortLabel.Text = "Engine:";

        enginePortNumeric.BorderStyle = BorderStyle.FixedSingle;
        enginePortNumeric.Font = new Font("Segoe UI", 8.75F);
        enginePortNumeric.Location = new Point(64, 34);
        enginePortNumeric.Maximum = 65535;
        enginePortNumeric.Minimum = 1024;
        enginePortNumeric.Size = new Size(72, 23);
        enginePortNumeric.Value = 9119;

        lanPortLabel.AutoSize = true;
        lanPortLabel.Font = new Font("Segoe UI", 8.75F);
        lanPortLabel.Location = new Point(148, 36);
        lanPortLabel.Text = "LAN:";

        lanPortNumeric.BorderStyle = BorderStyle.FixedSingle;
        lanPortNumeric.Font = new Font("Segoe UI", 8.75F);
        lanPortNumeric.Location = new Point(182, 34);
        lanPortNumeric.Maximum = 65535;
        lanPortNumeric.Minimum = 1024;
        lanPortNumeric.Size = new Size(72, 23);
        lanPortNumeric.Value = 9191;

        savePortsButton.FlatStyle = FlatStyle.Flat;
        savePortsButton.FlatAppearance.BorderSize = 1;
        savePortsButton.Font = new Font("Segoe UI", 8.25F);
        savePortsButton.Location = new Point(268, 32);
        savePortsButton.Size = new Size(92, 26);
        savePortsButton.Text = "Apply Ports";
        savePortsButton.Cursor = Cursors.Hand;

        startWithWindowsCheckBox.AutoSize = true;
        startWithWindowsCheckBox.Font = new Font("Segoe UI", 8.5F);
        startWithWindowsCheckBox.Location = new Point(14, 66);
        startWithWindowsCheckBox.Text = "Start with Windows";
        startWithWindowsCheckBox.Cursor = Cursors.Hand;

        autoStartHermesCheckBox.AutoSize = true;
        autoStartHermesCheckBox.Font = new Font("Segoe UI", 8.5F);
        autoStartHermesCheckBox.Location = new Point(14, 91);
        autoStartHermesCheckBox.Text = "Auto-start Hermes on launch";
        autoStartHermesCheckBox.Cursor = Cursors.Hand;

        autoEnableLanCheckBox.AutoSize = true;
        autoEnableLanCheckBox.Font = new Font("Segoe UI", 8.5F);
        autoEnableLanCheckBox.Location = new Point(14, 116);
        autoEnableLanCheckBox.Text = "Auto-enable LAN Broadcast on launch";
        autoEnableLanCheckBox.Cursor = Cursors.Hand;

        notifyRemoteClientsCheckBox.AutoSize = true;
        notifyRemoteClientsCheckBox.Font = new Font("Segoe UI", 8.5F);
        notifyRemoteClientsCheckBox.Location = new Point(14, 141);
        notifyRemoteClientsCheckBox.Text = "Notify on remote device connection";
        notifyRemoteClientsCheckBox.Checked = true;
        notifyRemoteClientsCheckBox.Cursor = Cursors.Hand;

        // 
        // Logs Drawer Panel
        // 
        logsPanel.Visible = false;
        logsPanel.Controls.Add(logsTitleLabel);
        logsPanel.Controls.Add(logsCountLabel);
        logsPanel.Controls.Add(copyLogsButton);
        logsPanel.Controls.Add(clearLogsButton);
        logsPanel.Controls.Add(closeLogsButton);
        logsPanel.Controls.Add(logsTextBox);

        logsTitleLabel.AutoSize = true;
        logsTitleLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        logsTitleLabel.Location = new Point(14, 10);
        logsTitleLabel.Text = "SYSTEM LOGS";

        logsCountLabel.AutoSize = true;
        logsCountLabel.Font = new Font("Segoe UI", 8F);
        logsCountLabel.Location = new Point(110, 10);
        logsCountLabel.Text = "0 entries";

        copyLogsButton.FlatStyle = FlatStyle.Flat;
        copyLogsButton.FlatAppearance.BorderSize = 1;
        copyLogsButton.Font = new Font("Segoe UI", 8F);
        copyLogsButton.Size = new Size(50, 24);
        copyLogsButton.Text = "Copy";
        copyLogsButton.Cursor = Cursors.Hand;

        clearLogsButton.FlatStyle = FlatStyle.Flat;
        clearLogsButton.FlatAppearance.BorderSize = 1;
        clearLogsButton.Font = new Font("Segoe UI", 8F);
        clearLogsButton.Size = new Size(50, 24);
        clearLogsButton.Text = "Clear";
        clearLogsButton.Cursor = Cursors.Hand;

        closeLogsButton.FlatStyle = FlatStyle.Flat;
        closeLogsButton.FlatAppearance.BorderSize = 1;
        closeLogsButton.Font = new Font("Segoe UI", 8F);
        closeLogsButton.Size = new Size(48, 24);
        closeLogsButton.Text = "Hide";
        closeLogsButton.Cursor = Cursors.Hand;

        logsTextBox.Font = new Font("Consolas", 8.5F);
        logsTextBox.Location = new Point(12, 38);
        logsTextBox.Multiline = true;
        logsTextBox.ReadOnly = true;
        logsTextBox.ScrollBars = ScrollBars.Vertical;
        logsTextBox.BorderStyle = BorderStyle.None;
        logsTextBox.WordWrap = false;

        cardsSpacer.Height = 12;

        // 
        // Main Form Config
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(510, 780);
        MinimumSize = new Size(460, 560);
        MaximumSize = new Size(680, 0);
        Controls.Add(scrollContainer);
        Controls.Add(topBarPanel);
        Controls.Add(bottomBarPanel);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowIcon = true;
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Agents Hub";

        ((System.ComponentModel.ISupportInitialize)qrPictureBox).EndInit();
        ((System.ComponentModel.ISupportInitialize)enginePortNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)lanPortNumeric).EndInit();
        ResumeLayout(false);
    }
}
