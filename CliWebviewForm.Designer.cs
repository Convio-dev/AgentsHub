#nullable enable

namespace HermesHub;

partial class CliWebviewForm
{
    private System.ComponentModel.IContainer? components = null;

    private Panel headerPanel = null!;
    private Panel titleDot = null!;
    private Label titleLabel = null!;

    private Panel tabsPanel = null!;
    private Button tabHermesButton = null!;
    private Button tabClaudeButton = null!;
    private Button tabGeminiButton = null!;

    private Panel toolsPanel = null!;
    private Label activeStatusLabel = null!;
    private Button toggleRunButton = null!;
    private Button restartCliButton = null!;
    private Button clearTerminalButton = null!;
    private Button openBrowserButton = null!;

    private Button minimizeButton = null!;
    private Button maximizeButton = null!;
    private Button closeButton = null!;

    private Panel contentPanel = null!;
    private Microsoft.Web.WebView2.WinForms.WebView2 hermesWebView = null!;
    private Microsoft.Web.WebView2.WinForms.WebView2 claudeWebView = null!;
    private Microsoft.Web.WebView2.WinForms.WebView2 geminiWebView = null!;

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
        titleDot = new Panel();
        titleLabel = new Label();
        tabsPanel = new Panel();
        tabHermesButton = new Button();
        tabClaudeButton = new Button();
        tabGeminiButton = new Button();
        toolsPanel = new Panel();
        activeStatusLabel = new Label();
        toggleRunButton = new Button();
        restartCliButton = new Button();
        clearTerminalButton = new Button();
        openBrowserButton = new Button();
        minimizeButton = new Button();
        maximizeButton = new Button();
        closeButton = new Button();

        contentPanel = new Panel();
        hermesWebView = new Microsoft.Web.WebView2.WinForms.WebView2();
        claudeWebView = new Microsoft.Web.WebView2.WinForms.WebView2();
        geminiWebView = new Microsoft.Web.WebView2.WinForms.WebView2();

        ((System.ComponentModel.ISupportInitialize)hermesWebView).BeginInit();
        ((System.ComponentModel.ISupportInitialize)claudeWebView).BeginInit();
        ((System.ComponentModel.ISupportInitialize)geminiWebView).BeginInit();
        SuspendLayout();

        // 
        // Header Panel
        // 
        headerPanel.BackColor = Color.FromArgb(16, 16, 20);
        headerPanel.Dock = DockStyle.Top;
        headerPanel.Height = 50;
        headerPanel.Controls.Add(titleDot);
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(tabsPanel);
        headerPanel.Controls.Add(toolsPanel);
        headerPanel.Controls.Add(minimizeButton);
        headerPanel.Controls.Add(maximizeButton);
        headerPanel.Controls.Add(closeButton);

        titleDot.Location = new Point(14, 18);
        titleDot.Size = new Size(10, 10);
        titleDot.BackColor = Color.FromArgb(16, 185, 129);

        titleLabel.AutoSize = true;
        titleLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        titleLabel.ForeColor = Color.FromArgb(250, 250, 250);
        titleLabel.Location = new Point(30, 14);
        titleLabel.Text = "CLI HUB";

        // 
        // Tabs Panel
        // 
        tabsPanel.Location = new Point(115, 7);
        tabsPanel.Size = new Size(390, 36);
        tabsPanel.Controls.Add(tabHermesButton);
        tabsPanel.Controls.Add(tabClaudeButton);
        tabsPanel.Controls.Add(tabGeminiButton);

        tabHermesButton.Location = new Point(0, 0);
        tabHermesButton.Size = new Size(124, 36);
        tabHermesButton.FlatStyle = FlatStyle.Flat;
        tabHermesButton.FlatAppearance.BorderSize = 0;
        tabHermesButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        tabHermesButton.Text = "● Hermes";
        tabHermesButton.Cursor = Cursors.Hand;

        tabClaudeButton.Location = new Point(128, 0);
        tabClaudeButton.Size = new Size(124, 36);
        tabClaudeButton.FlatStyle = FlatStyle.Flat;
        tabClaudeButton.FlatAppearance.BorderSize = 0;
        tabClaudeButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        tabClaudeButton.Text = "● Claude";
        tabClaudeButton.Cursor = Cursors.Hand;

        tabGeminiButton.Location = new Point(256, 0);
        tabGeminiButton.Size = new Size(124, 36);
        tabGeminiButton.FlatStyle = FlatStyle.Flat;
        tabGeminiButton.FlatAppearance.BorderSize = 0;
        tabGeminiButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        tabGeminiButton.Text = "● Gemini";
        tabGeminiButton.Cursor = Cursors.Hand;

        // 
        // Tools Panel
        // 
        toolsPanel.Location = new Point(520, 8);
        toolsPanel.Size = new Size(470, 34);
        toolsPanel.Controls.Add(activeStatusLabel);
        toolsPanel.Controls.Add(toggleRunButton);
        toolsPanel.Controls.Add(restartCliButton);
        toolsPanel.Controls.Add(clearTerminalButton);
        toolsPanel.Controls.Add(openBrowserButton);

        activeStatusLabel.AutoSize = true;
        activeStatusLabel.Font = new Font("Segoe UI", 8.5F);
        activeStatusLabel.ForeColor = Color.FromArgb(161, 161, 170);
        activeStatusLabel.Location = new Point(6, 9);
        activeStatusLabel.Text = "Status: Offline";

        toggleRunButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        toggleRunButton.Location = new Point(190, 2);
        toggleRunButton.Size = new Size(68, 28);
        toggleRunButton.FlatStyle = FlatStyle.Flat;
        toggleRunButton.FlatAppearance.BorderSize = 0;
        toggleRunButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        toggleRunButton.Text = "Start";
        toggleRunButton.Cursor = Cursors.Hand;

        restartCliButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        restartCliButton.Location = new Point(262, 2);
        restartCliButton.Size = new Size(64, 28);
        restartCliButton.FlatStyle = FlatStyle.Flat;
        restartCliButton.FlatAppearance.BorderSize = 0;
        restartCliButton.Font = new Font("Segoe UI", 8.5F);
        restartCliButton.Text = "Restart";
        restartCliButton.Cursor = Cursors.Hand;

        clearTerminalButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        clearTerminalButton.Location = new Point(330, 2);
        clearTerminalButton.Size = new Size(54, 28);
        clearTerminalButton.FlatStyle = FlatStyle.Flat;
        clearTerminalButton.FlatAppearance.BorderSize = 0;
        clearTerminalButton.Font = new Font("Segoe UI", 8.5F);
        clearTerminalButton.Text = "Clear";
        clearTerminalButton.Cursor = Cursors.Hand;

        openBrowserButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openBrowserButton.Location = new Point(388, 2);
        openBrowserButton.Size = new Size(76, 28);
        openBrowserButton.FlatStyle = FlatStyle.Flat;
        openBrowserButton.FlatAppearance.BorderSize = 0;
        openBrowserButton.Font = new Font("Segoe UI", 8.5F);
        openBrowserButton.Text = "Browser";
        openBrowserButton.Cursor = Cursors.Hand;

        // 
        // Window Control Buttons
        // 
        minimizeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        minimizeButton.FlatStyle = FlatStyle.Flat;
        minimizeButton.FlatAppearance.BorderSize = 0;
        minimizeButton.BackColor = Color.FromArgb(16, 16, 20);
        minimizeButton.ForeColor = Color.FromArgb(161, 161, 170);
        minimizeButton.Location = new Point(996, 8);
        minimizeButton.Size = new Size(34, 34);
        minimizeButton.Text = "";
        minimizeButton.Cursor = Cursors.Hand;
        minimizeButton.TabStop = false;

        maximizeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        maximizeButton.FlatStyle = FlatStyle.Flat;
        maximizeButton.FlatAppearance.BorderSize = 0;
        maximizeButton.BackColor = Color.FromArgb(16, 16, 20);
        maximizeButton.ForeColor = Color.FromArgb(161, 161, 170);
        maximizeButton.Location = new Point(1032, 8);
        maximizeButton.Size = new Size(34, 34);
        maximizeButton.Text = "";
        maximizeButton.Cursor = Cursors.Hand;
        maximizeButton.TabStop = false;

        closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeButton.FlatStyle = FlatStyle.Flat;
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.BackColor = Color.FromArgb(16, 16, 20);
        closeButton.ForeColor = Color.FromArgb(161, 161, 170);
        closeButton.Location = new Point(1068, 8);
        closeButton.Size = new Size(34, 34);
        closeButton.Text = "";
        closeButton.Cursor = Cursors.Hand;
        closeButton.TabStop = false;

        // 
        // Content Panel
        // 
        contentPanel.BackColor = Color.FromArgb(9, 9, 11);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Controls.Add(hermesWebView);
        contentPanel.Controls.Add(claudeWebView);
        contentPanel.Controls.Add(geminiWebView);

        hermesWebView.Dock = DockStyle.Fill;
        claudeWebView.Dock = DockStyle.Fill;
        geminiWebView.Dock = DockStyle.Fill;

        // 
        // CliWebviewForm Form Properties
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(9, 9, 11);
        ClientSize = new Size(1110, 720);
        Controls.Add(contentPanel);
        Controls.Add(headerPanel);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(820, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Hermes Hub - CLI Workspace";

        ((System.ComponentModel.ISupportInitialize)hermesWebView).EndInit();
        ((System.ComponentModel.ISupportInitialize)claudeWebView).EndInit();
        ((System.ComponentModel.ISupportInitialize)geminiWebView).EndInit();
        ResumeLayout(false);
    }
}
