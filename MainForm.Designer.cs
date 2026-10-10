#nullable enable

namespace HermesHub;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private Panel headerPanel = null!;
    private Panel statusDot = null!;
    private Label titleLabel = null!;
    private Label subtitleLabel = null!;
    private Button minimizeButton = null!;
    private Button closeButton = null!;

    private Panel hermesCard = null!;
    private Label hermesCardTitle = null!;
    private Label cliSummaryLabel = null!;
    private ComboBox cliSelectComboBox = null!;
    private Button viewLogsButton = null!;
    private Label hermesStatusLabel = null!;
    private Button startHermesButton = null!;
    private Button stopHermesButton = null!;
    private Button openDashboardButton = null!;
    private Label cliDotsLabel = null!;

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

    private Panel logsPanel = null!;
    private Label logsTitleLabel = null!;
    private Label logsCountLabel = null!;
    private Button copyLogsButton = null!;
    private Button clearLogsButton = null!;
    private Button closeLogsButton = null!;
    private TextBox logsTextBox = null!;

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

    private Panel footerPanel = null!;
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
        headerPanel = new Panel();
        statusDot = new Panel();
        titleLabel = new Label();
        subtitleLabel = new Label();
        minimizeButton = new Button();
        closeButton = new Button();

        hermesCard = new Panel();
        hermesCardTitle = new Label();
        cliSummaryLabel = new Label();
        cliSelectComboBox = new ComboBox();
        viewLogsButton = new Button();
        hermesStatusLabel = new Label();
        startHermesButton = new Button();
        stopHermesButton = new Button();
        openDashboardButton = new Button();
        cliDotsLabel = new Label();

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

        logsPanel = new Panel();
        logsTitleLabel = new Label();
        logsCountLabel = new Label();
        copyLogsButton = new Button();
        clearLogsButton = new Button();
        closeLogsButton = new Button();
        logsTextBox = new TextBox();

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

        footerPanel = new Panel();
        ramUsageLabel = new Label();
        versionLabel = new Label();
        footerLogsButton = new Button();
        quitAppButton = new Button();

        telemetryTimer = new System.Windows.Forms.Timer();

        ((System.ComponentModel.ISupportInitialize)qrPictureBox).BeginInit();
        ((System.ComponentModel.ISupportInitialize)enginePortNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)lanPortNumeric).BeginInit();
        SuspendLayout();

        // 
        // Header Panel
        // 
        headerPanel.BackColor = Color.FromArgb(16, 16, 20);
        headerPanel.Dock = DockStyle.Top;
        headerPanel.Height = 52;
        headerPanel.Controls.Add(statusDot);
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(subtitleLabel);
        headerPanel.Controls.Add(minimizeButton);
        headerPanel.Controls.Add(closeButton);

        statusDot.Location = new Point(16, 18);
        statusDot.Size = new Size(10, 10);
        statusDot.BackColor = Color.FromArgb(239, 68, 68);

        titleLabel.AutoSize = true;
        titleLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        titleLabel.ForeColor = Color.FromArgb(250, 250, 250);
        titleLabel.Location = new Point(34, 13);
        titleLabel.Text = "HERMES HUB";

        subtitleLabel.AutoSize = true;
        subtitleLabel.Font = new Font("Segoe UI", 8.5F);
        subtitleLabel.ForeColor = Color.FromArgb(113, 113, 122);
        subtitleLabel.Location = new Point(148, 16);
        subtitleLabel.Text = "Desktop Controller";

        minimizeButton.FlatStyle = FlatStyle.Flat;
        minimizeButton.FlatAppearance.BorderSize = 0;
        minimizeButton.BackColor = Color.FromArgb(16, 16, 20);
        minimizeButton.ForeColor = Color.FromArgb(161, 161, 170);
        minimizeButton.Location = new Point(372, 9);
        minimizeButton.Size = new Size(34, 34);
        minimizeButton.Text = "";
        minimizeButton.Cursor = Cursors.Hand;
        minimizeButton.TabStop = false;

        closeButton.FlatStyle = FlatStyle.Flat;
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.BackColor = Color.FromArgb(16, 16, 20);
        closeButton.ForeColor = Color.FromArgb(161, 161, 170);
        closeButton.Location = new Point(408, 9);
        closeButton.Size = new Size(34, 34);
        closeButton.Text = "";
        closeButton.Cursor = Cursors.Hand;
        closeButton.TabStop = false;

        // 
        // Hermes / CLI Services Card
        // 
        hermesCard.BackColor = Color.FromArgb(24, 24, 27);
        hermesCard.Location = new Point(16, 68);
        hermesCard.Size = new Size(418, 150);
        hermesCard.Controls.Add(hermesCardTitle);
        hermesCard.Controls.Add(cliSummaryLabel);
        hermesCard.Controls.Add(viewLogsButton);
        hermesCard.Controls.Add(cliSelectComboBox);
        hermesCard.Controls.Add(hermesStatusLabel);
        hermesCard.Controls.Add(startHermesButton);
        hermesCard.Controls.Add(stopHermesButton);
        hermesCard.Controls.Add(openDashboardButton);
        hermesCard.Controls.Add(cliDotsLabel);

        hermesCardTitle.AutoSize = true;
        hermesCardTitle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        hermesCardTitle.ForeColor = Color.FromArgb(113, 113, 122);
        hermesCardTitle.Location = new Point(14, 12);
        hermesCardTitle.Text = "CLI SERVICES";

        cliSummaryLabel.AutoSize = true;
        cliSummaryLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        cliSummaryLabel.ForeColor = Color.FromArgb(16, 185, 129);
        cliSummaryLabel.Location = new Point(96, 12);
        cliSummaryLabel.Text = "Active: 0/3";

        viewLogsButton.FlatStyle = FlatStyle.Flat;
        viewLogsButton.FlatAppearance.BorderSize = 1;
        viewLogsButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        viewLogsButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        viewLogsButton.BackColor = Color.FromArgb(24, 24, 27);
        viewLogsButton.ForeColor = Color.FromArgb(161, 161, 170);
        viewLogsButton.Location = new Point(334, 8);
        viewLogsButton.Size = new Size(70, 24);
        viewLogsButton.Text = "Logs";
        viewLogsButton.Cursor = Cursors.Hand;

        cliSelectComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        cliSelectComboBox.FlatStyle = FlatStyle.Flat;
        cliSelectComboBox.BackColor = Color.FromArgb(16, 16, 20);
        cliSelectComboBox.ForeColor = Color.FromArgb(250, 250, 250);
        cliSelectComboBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        cliSelectComboBox.Location = new Point(14, 38);
        cliSelectComboBox.Size = new Size(188, 26);
        cliSelectComboBox.Cursor = Cursors.Hand;

        hermesStatusLabel.AutoSize = true;
        hermesStatusLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        hermesStatusLabel.ForeColor = Color.FromArgb(239, 68, 68);
        hermesStatusLabel.Location = new Point(208, 42);
        hermesStatusLabel.Text = "Engine: Offline";

        startHermesButton.FlatStyle = FlatStyle.Flat;
        startHermesButton.FlatAppearance.BorderSize = 0;
        startHermesButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        startHermesButton.BackColor = Color.FromArgb(16, 185, 129);
        startHermesButton.ForeColor = Color.FromArgb(9, 9, 11);
        startHermesButton.Location = new Point(14, 74);
        startHermesButton.Size = new Size(122, 38);
        startHermesButton.Text = "Start Hermes";
        startHermesButton.Cursor = Cursors.Hand;

        stopHermesButton.FlatStyle = FlatStyle.Flat;
        stopHermesButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        stopHermesButton.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        stopHermesButton.BackColor = Color.FromArgb(24, 24, 27);
        stopHermesButton.ForeColor = Color.FromArgb(161, 161, 170);
        stopHermesButton.Location = new Point(144, 74);
        stopHermesButton.Size = new Size(118, 38);
        stopHermesButton.Text = "Stop Hermes";
        stopHermesButton.Cursor = Cursors.Hand;

        openDashboardButton.FlatStyle = FlatStyle.Flat;
        openDashboardButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        openDashboardButton.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        openDashboardButton.BackColor = Color.FromArgb(24, 24, 27);
        openDashboardButton.ForeColor = Color.FromArgb(250, 250, 250);
        openDashboardButton.Location = new Point(270, 74);
        openDashboardButton.Size = new Size(134, 38);
        openDashboardButton.Text = "Open Webview";
        openDashboardButton.Cursor = Cursors.Hand;

        cliDotsLabel.AutoSize = true;
        cliDotsLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
        cliDotsLabel.ForeColor = Color.FromArgb(113, 113, 122);
        cliDotsLabel.Location = new Point(14, 124);
        cliDotsLabel.Text = "Hermes: ○ Offline   Claude: ○ Offline   Gemini: ○ Offline";

        // 
        // LAN Card
        // 
        lanCard.BackColor = Color.FromArgb(24, 24, 27);
        lanCard.Location = new Point(16, 230);
        lanCard.Size = new Size(418, 152);
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
        lanCardTitle.ForeColor = Color.FromArgb(113, 113, 122);
        lanCardTitle.Location = new Point(14, 12);
        lanCardTitle.Text = "NETWORK & LAN BROADCAST";

        lanToggleCheckBox.AutoSize = true;
        lanToggleCheckBox.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        lanToggleCheckBox.ForeColor = Color.FromArgb(250, 250, 250);
        lanToggleCheckBox.Location = new Point(14, 34);
        lanToggleCheckBox.Size = new Size(230, 21);
        lanToggleCheckBox.Text = "Broadcast to LAN (Port 9191)";
        lanToggleCheckBox.Cursor = Cursors.Hand;

        firewallStatusButton.FlatStyle = FlatStyle.Flat;
        firewallStatusButton.FlatAppearance.BorderSize = 1;
        firewallStatusButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        firewallStatusButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        firewallStatusButton.BackColor = Color.FromArgb(24, 24, 27);
        firewallStatusButton.ForeColor = Color.FromArgb(161, 161, 170);
        firewallStatusButton.Location = new Point(266, 8);
        firewallStatusButton.Size = new Size(138, 25);
        firewallStatusButton.Text = "Checking Firewall...";
        firewallStatusButton.Cursor = Cursors.Hand;

        lanStatusLabel.AutoSize = true;
        lanStatusLabel.Font = new Font("Segoe UI", 8.5F);
        lanStatusLabel.ForeColor = Color.FromArgb(113, 113, 122);
        lanStatusLabel.Location = new Point(14, 58);
        lanStatusLabel.Text = "Status: Local Only (Isolated)";

        lanClientsLabel.AutoSize = false;
        lanClientsLabel.Font = new Font("Segoe UI", 8.25F);
        lanClientsLabel.ForeColor = Color.FromArgb(113, 113, 122);
        lanClientsLabel.Location = new Point(14, 80);
        lanClientsLabel.Size = new Size(390, 18);
        lanClientsLabel.Text = "Active Clients: 0 (No remote devices)";

        lanUrlPanel.BackColor = Color.FromArgb(9, 9, 11);
        lanUrlPanel.Location = new Point(14, 102);
        lanUrlPanel.Size = new Size(176, 36);
        lanUrlPanel.Controls.Add(lanUrlTextBox);

        lanUrlTextBox.BorderStyle = BorderStyle.None;
        lanUrlTextBox.BackColor = Color.FromArgb(9, 9, 11);
        lanUrlTextBox.ForeColor = Color.FromArgb(16, 185, 129);
        lanUrlTextBox.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        lanUrlTextBox.Location = new Point(10, 8);
        lanUrlTextBox.Size = new Size(156, 20);
        lanUrlTextBox.ReadOnly = true;
        lanUrlTextBox.Text = "http://0.0.0.0:9191";

        copyLanButton.FlatStyle = FlatStyle.Flat;
        copyLanButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        copyLanButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        copyLanButton.BackColor = Color.FromArgb(24, 24, 27);
        copyLanButton.ForeColor = Color.FromArgb(250, 250, 250);
        copyLanButton.Location = new Point(196, 102);
        copyLanButton.Size = new Size(70, 36);
        copyLanButton.Text = "Copy";
        copyLanButton.Cursor = Cursors.Hand;

        openLanButton.FlatStyle = FlatStyle.Flat;
        openLanButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        openLanButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        openLanButton.BackColor = Color.FromArgb(24, 24, 27);
        openLanButton.ForeColor = Color.FromArgb(250, 250, 250);
        openLanButton.Location = new Point(270, 102);
        openLanButton.Size = new Size(62, 36);
        openLanButton.Text = "Open";
        openLanButton.Cursor = Cursors.Hand;

        toggleQrButton.FlatStyle = FlatStyle.Flat;
        toggleQrButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        toggleQrButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        toggleQrButton.BackColor = Color.FromArgb(24, 24, 27);
        toggleQrButton.ForeColor = Color.FromArgb(250, 250, 250);
        toggleQrButton.Location = new Point(336, 102);
        toggleQrButton.Size = new Size(68, 36);
        toggleQrButton.Text = "QR";
        toggleQrButton.Cursor = Cursors.Hand;

        // 
        // QR Drawer Panel (collapsible)
        // 
        qrDrawerPanel.BackColor = Color.FromArgb(16, 16, 20);
        qrDrawerPanel.Location = new Point(14, 148);
        qrDrawerPanel.Size = new Size(390, 178);
        qrDrawerPanel.Visible = false;
        qrDrawerPanel.Controls.Add(qrPictureBox);
        qrDrawerPanel.Controls.Add(qrTitleLabel);
        qrDrawerPanel.Controls.Add(qrDescLabel);
        qrDrawerPanel.Controls.Add(qrUrlLabel);
        qrDrawerPanel.Controls.Add(closeQrButton);

        qrPictureBox.Location = new Point(16, 19);
        qrPictureBox.Size = new Size(140, 140);
        qrPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        qrPictureBox.BackColor = Color.White;

        qrTitleLabel.AutoSize = true;
        qrTitleLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        qrTitleLabel.ForeColor = Color.FromArgb(16, 185, 129);
        qrTitleLabel.Location = new Point(168, 22);
        qrTitleLabel.Text = "Scan to Open on Mobile";

        qrDescLabel.Font = new Font("Segoe UI", 8.5F);
        qrDescLabel.ForeColor = Color.FromArgb(161, 161, 170);
        qrDescLabel.Location = new Point(168, 48);
        qrDescLabel.Size = new Size(205, 42);
        qrDescLabel.Text = "Scan with your phone camera to open Hermes directly on the LAN.";

        qrUrlLabel.Font = new Font("Segoe UI", 8F);
        qrUrlLabel.ForeColor = Color.FromArgb(113, 113, 122);
        qrUrlLabel.Location = new Point(168, 98);
        qrUrlLabel.Size = new Size(205, 20);
        qrUrlLabel.Text = "http://127.0.0.1:9191";

        closeQrButton.FlatStyle = FlatStyle.Flat;
        closeQrButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        closeQrButton.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
        closeQrButton.BackColor = Color.FromArgb(39, 39, 42);
        closeQrButton.ForeColor = Color.FromArgb(250, 250, 250);
        closeQrButton.Location = new Point(168, 128);
        closeQrButton.Size = new Size(84, 26);
        closeQrButton.Text = "Hide QR";
        closeQrButton.Cursor = Cursors.Hand;

        // 
        // Settings Card
        // 
        settingsCard.BackColor = Color.FromArgb(24, 24, 27);
        settingsCard.Location = new Point(16, 364);
        settingsCard.Size = new Size(418, 178);
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
        settingsCardTitle.ForeColor = Color.FromArgb(113, 113, 122);
        settingsCardTitle.Location = new Point(14, 10);
        settingsCardTitle.Text = "SETTINGS & AUTOMATION";

        enginePortLabel.AutoSize = true;
        enginePortLabel.Font = new Font("Segoe UI", 8.5F);
        enginePortLabel.ForeColor = Color.FromArgb(161, 161, 170);
        enginePortLabel.Location = new Point(14, 32);
        enginePortLabel.Text = "Engine:";

        enginePortNumeric.BackColor = Color.FromArgb(16, 16, 20);
        enginePortNumeric.BorderStyle = BorderStyle.FixedSingle;
        enginePortNumeric.Font = new Font("Segoe UI", 8.5F);
        enginePortNumeric.ForeColor = Color.FromArgb(250, 250, 250);
        enginePortNumeric.Location = new Point(62, 30);
        enginePortNumeric.Maximum = 65535;
        enginePortNumeric.Minimum = 1024;
        enginePortNumeric.Size = new Size(70, 23);
        enginePortNumeric.Value = 9119;

        lanPortLabel.AutoSize = true;
        lanPortLabel.Font = new Font("Segoe UI", 8.5F);
        lanPortLabel.ForeColor = Color.FromArgb(161, 161, 170);
        lanPortLabel.Location = new Point(152, 32);
        lanPortLabel.Text = "LAN:";

        lanPortNumeric.BackColor = Color.FromArgb(16, 16, 20);
        lanPortNumeric.BorderStyle = BorderStyle.FixedSingle;
        lanPortNumeric.Font = new Font("Segoe UI", 8.5F);
        lanPortNumeric.ForeColor = Color.FromArgb(250, 250, 250);
        lanPortNumeric.Location = new Point(184, 30);
        lanPortNumeric.Maximum = 65535;
        lanPortNumeric.Minimum = 1024;
        lanPortNumeric.Size = new Size(70, 23);
        lanPortNumeric.Value = 9191;

        savePortsButton.FlatStyle = FlatStyle.Flat;
        savePortsButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        savePortsButton.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
        savePortsButton.BackColor = Color.FromArgb(39, 39, 42);
        savePortsButton.ForeColor = Color.FromArgb(250, 250, 250);
        savePortsButton.Location = new Point(272, 29);
        savePortsButton.Size = new Size(84, 25);
        savePortsButton.Text = "Apply Ports";
        savePortsButton.Cursor = Cursors.Hand;

        startWithWindowsCheckBox.AutoSize = true;
        startWithWindowsCheckBox.Font = new Font("Segoe UI", 8.5F);
        startWithWindowsCheckBox.ForeColor = Color.FromArgb(212, 212, 216);
        startWithWindowsCheckBox.Location = new Point(14, 62);
        startWithWindowsCheckBox.Text = "Start with Windows";
        startWithWindowsCheckBox.Cursor = Cursors.Hand;

        autoStartHermesCheckBox.AutoSize = true;
        autoStartHermesCheckBox.Font = new Font("Segoe UI", 8.5F);
        autoStartHermesCheckBox.ForeColor = Color.FromArgb(212, 212, 216);
        autoStartHermesCheckBox.Location = new Point(14, 86);
        autoStartHermesCheckBox.Text = "Auto-start Hermes on launch";
        autoStartHermesCheckBox.Cursor = Cursors.Hand;

        autoEnableLanCheckBox.AutoSize = true;
        autoEnableLanCheckBox.Font = new Font("Segoe UI", 8.5F);
        autoEnableLanCheckBox.ForeColor = Color.FromArgb(212, 212, 216);
        autoEnableLanCheckBox.Location = new Point(14, 110);
        autoEnableLanCheckBox.Text = "Auto-enable LAN Broadcast on launch";
        autoEnableLanCheckBox.Cursor = Cursors.Hand;

        notifyRemoteClientsCheckBox.AutoSize = true;
        notifyRemoteClientsCheckBox.Font = new Font("Segoe UI", 8.5F);
        notifyRemoteClientsCheckBox.ForeColor = Color.FromArgb(212, 212, 216);
        notifyRemoteClientsCheckBox.Location = new Point(14, 134);
        notifyRemoteClientsCheckBox.Text = "Notify on remote device connection";
        notifyRemoteClientsCheckBox.Checked = true;
        notifyRemoteClientsCheckBox.Cursor = Cursors.Hand;

        // 
        // Logs Drawer Panel (collapsible console)
        // 
        logsPanel.BackColor = Color.FromArgb(16, 16, 20);
        logsPanel.Location = new Point(16, 554);
        logsPanel.Size = new Size(418, 180);
        logsPanel.Visible = false;
        logsPanel.Controls.Add(logsTitleLabel);
        logsPanel.Controls.Add(logsCountLabel);
        logsPanel.Controls.Add(copyLogsButton);
        logsPanel.Controls.Add(clearLogsButton);
        logsPanel.Controls.Add(closeLogsButton);
        logsPanel.Controls.Add(logsTextBox);

        logsTitleLabel.AutoSize = true;
        logsTitleLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        logsTitleLabel.ForeColor = Color.FromArgb(113, 113, 122);
        logsTitleLabel.Location = new Point(12, 10);
        logsTitleLabel.Text = "ENGINE & PROXY LOGS";

        logsCountLabel.AutoSize = true;
        logsCountLabel.Font = new Font("Segoe UI", 8F);
        logsCountLabel.ForeColor = Color.FromArgb(16, 185, 129);
        logsCountLabel.Location = new Point(146, 10);
        logsCountLabel.Text = "0 entries";

        copyLogsButton.FlatStyle = FlatStyle.Flat;
        copyLogsButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        copyLogsButton.Font = new Font("Segoe UI", 8F);
        copyLogsButton.BackColor = Color.FromArgb(24, 24, 27);
        copyLogsButton.ForeColor = Color.FromArgb(250, 250, 250);
        copyLogsButton.Location = new Point(246, 6);
        copyLogsButton.Size = new Size(50, 24);
        copyLogsButton.Text = "Copy";
        copyLogsButton.Cursor = Cursors.Hand;

        clearLogsButton.FlatStyle = FlatStyle.Flat;
        clearLogsButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        clearLogsButton.Font = new Font("Segoe UI", 8F);
        clearLogsButton.BackColor = Color.FromArgb(24, 24, 27);
        clearLogsButton.ForeColor = Color.FromArgb(250, 250, 250);
        clearLogsButton.Location = new Point(302, 6);
        clearLogsButton.Size = new Size(50, 24);
        clearLogsButton.Text = "Clear";
        clearLogsButton.Cursor = Cursors.Hand;

        closeLogsButton.FlatStyle = FlatStyle.Flat;
        closeLogsButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        closeLogsButton.Font = new Font("Segoe UI", 8F);
        closeLogsButton.BackColor = Color.FromArgb(24, 24, 27);
        closeLogsButton.ForeColor = Color.FromArgb(250, 250, 250);
        closeLogsButton.Location = new Point(358, 6);
        closeLogsButton.Size = new Size(48, 24);
        closeLogsButton.Text = "Hide";
        closeLogsButton.Cursor = Cursors.Hand;

        logsTextBox.BackColor = Color.FromArgb(9, 9, 11);
        logsTextBox.ForeColor = Color.FromArgb(212, 212, 216);
        logsTextBox.Font = new Font("Consolas", 8.5F, FontStyle.Regular);
        logsTextBox.Location = new Point(10, 36);
        logsTextBox.Size = new Size(398, 134);
        logsTextBox.Multiline = true;
        logsTextBox.ReadOnly = true;
        logsTextBox.ScrollBars = ScrollBars.Vertical;
        logsTextBox.BorderStyle = BorderStyle.None;
        logsTextBox.WordWrap = false;

        // 
        // Footer Panel
        // 
        footerPanel.BackColor = Color.FromArgb(16, 16, 20);
        footerPanel.Dock = DockStyle.Bottom;
        footerPanel.Height = 44;
        footerPanel.Controls.Add(ramUsageLabel);
        footerPanel.Controls.Add(versionLabel);
        footerPanel.Controls.Add(footerLogsButton);
        footerPanel.Controls.Add(quitAppButton);

        ramUsageLabel.AutoSize = true;
        ramUsageLabel.Font = new Font("Segoe UI", 8.5F);
        ramUsageLabel.ForeColor = Color.FromArgb(113, 113, 122);
        ramUsageLabel.Location = new Point(16, 14);
        ramUsageLabel.Text = "RAM: -- MB";

        versionLabel.AutoSize = true;
        versionLabel.Font = new Font("Segoe UI", 8.5F);
        versionLabel.ForeColor = Color.FromArgb(113, 113, 122);
        versionLabel.Location = new Point(108, 14);
        versionLabel.Text = "v1.0.2";

        footerLogsButton.FlatStyle = FlatStyle.Flat;
        footerLogsButton.FlatAppearance.BorderColor = Color.FromArgb(39, 39, 42);
        footerLogsButton.Font = new Font("Segoe UI", 8F);
        footerLogsButton.BackColor = Color.FromArgb(24, 24, 27);
        footerLogsButton.ForeColor = Color.FromArgb(212, 212, 216);
        footerLogsButton.Location = new Point(216, 8);
        footerLogsButton.Size = new Size(100, 28);
        footerLogsButton.Text = "Console (0)";
        footerLogsButton.Cursor = Cursors.Hand;

        quitAppButton.FlatStyle = FlatStyle.Flat;
        quitAppButton.FlatAppearance.BorderSize = 0;
        quitAppButton.Font = new Font("Segoe UI", 8.5F);
        quitAppButton.ForeColor = Color.FromArgb(161, 161, 170);
        quitAppButton.Location = new Point(328, 8);
        quitAppButton.Size = new Size(106, 28);
        quitAppButton.Text = "Quit App";
        quitAppButton.Cursor = Cursors.Hand;

        // 
        // Main Form Config
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(9, 9, 11);
        ClientSize = new Size(450, 606);
        Controls.Add(headerPanel);
        Controls.Add(hermesCard);
        Controls.Add(lanCard);
        Controls.Add(settingsCard);
        Controls.Add(logsPanel);
        Controls.Add(footerPanel);
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MaximizeBox = false;
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Hermes Hub";

        ((System.ComponentModel.ISupportInitialize)qrPictureBox).EndInit();
        ((System.ComponentModel.ISupportInitialize)enginePortNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)lanPortNumeric).EndInit();
        ResumeLayout(false);
    }
}
