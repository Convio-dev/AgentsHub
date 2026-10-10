#nullable enable

namespace HermesHub;

partial class CliWebviewForm
{
    private System.ComponentModel.IContainer? components = null;

    private Panel topToolbar = null!;
    private Panel tabsPanel = null!;
    private Button tabHermesButton = null!;
    private Button tabClaudeButton = null!;
    private Button tabAntigravityButton = null!;

    private Panel toolsPanel = null!;
    private Label activeStatusLabel = null!;
    private Button toggleRunButton = null!;
    private Button restartCliButton = null!;
    private Button clearTerminalButton = null!;
    private Button openBrowserButton = null!;

    private Panel contentPanel = null!;

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
        topToolbar = new Panel();
        tabsPanel = new Panel();
        tabHermesButton = new Button();
        tabClaudeButton = new Button();
        tabAntigravityButton = new Button();

        toolsPanel = new Panel();
        activeStatusLabel = new Label();
        toggleRunButton = new Button();
        restartCliButton = new Button();
        clearTerminalButton = new Button();
        openBrowserButton = new Button();

        contentPanel = new Panel();

        SuspendLayout();

        // 
        // Top Toolbar Panel
        // 
        topToolbar.Dock = DockStyle.Top;
        topToolbar.Height = 46;
        topToolbar.Controls.Add(toolsPanel);
        topToolbar.Controls.Add(tabsPanel);

        // 
        // Tabs Panel (docked left)
        // 
        tabsPanel.Dock = DockStyle.Left;
        tabsPanel.Width = 380;
        tabsPanel.Controls.Add(tabHermesButton);
        tabsPanel.Controls.Add(tabClaudeButton);
        tabsPanel.Controls.Add(tabAntigravityButton);

        tabHermesButton.Location = new Point(8, 6);
        tabHermesButton.Size = new Size(110, 34);
        tabHermesButton.FlatStyle = FlatStyle.Flat;
        tabHermesButton.FlatAppearance.BorderSize = 1;
        tabHermesButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        tabHermesButton.Text = "⚡ Hermes";
        tabHermesButton.Cursor = Cursors.Hand;

        tabClaudeButton.Location = new Point(122, 6);
        tabClaudeButton.Size = new Size(114, 34);
        tabClaudeButton.FlatStyle = FlatStyle.Flat;
        tabClaudeButton.FlatAppearance.BorderSize = 1;
        tabClaudeButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        tabClaudeButton.Text = "🤖 Claude";
        tabClaudeButton.Cursor = Cursors.Hand;

        tabAntigravityButton.Location = new Point(240, 6);
        tabAntigravityButton.Size = new Size(126, 34);
        tabAntigravityButton.FlatStyle = FlatStyle.Flat;
        tabAntigravityButton.FlatAppearance.BorderSize = 1;
        tabAntigravityButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        tabAntigravityButton.Text = "🚀 Antigravity";
        tabAntigravityButton.Cursor = Cursors.Hand;

        // 
        // Tools Panel (docked right)
        // 
        toolsPanel.Dock = DockStyle.Right;
        toolsPanel.Width = 540;
        toolsPanel.Controls.Add(activeStatusLabel);
        toolsPanel.Controls.Add(toggleRunButton);
        toolsPanel.Controls.Add(restartCliButton);
        toolsPanel.Controls.Add(clearTerminalButton);
        toolsPanel.Controls.Add(openBrowserButton);

        activeStatusLabel.AutoSize = true;
        activeStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        activeStatusLabel.Location = new Point(8, 15);
        activeStatusLabel.Text = "Status: Offline";

        toggleRunButton.Location = new Point(180, 7);
        toggleRunButton.Size = new Size(74, 32);
        toggleRunButton.FlatStyle = FlatStyle.Flat;
        toggleRunButton.FlatAppearance.BorderSize = 1;
        toggleRunButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        toggleRunButton.Text = "Start";
        toggleRunButton.Cursor = Cursors.Hand;

        restartCliButton.Location = new Point(260, 7);
        restartCliButton.Size = new Size(68, 32);
        restartCliButton.FlatStyle = FlatStyle.Flat;
        restartCliButton.FlatAppearance.BorderSize = 1;
        restartCliButton.Font = new Font("Segoe UI", 8.5F);
        restartCliButton.Text = "Restart";
        restartCliButton.Cursor = Cursors.Hand;

        clearTerminalButton.Location = new Point(334, 7);
        clearTerminalButton.Size = new Size(58, 32);
        clearTerminalButton.FlatStyle = FlatStyle.Flat;
        clearTerminalButton.FlatAppearance.BorderSize = 1;
        clearTerminalButton.Font = new Font("Segoe UI", 8.5F);
        clearTerminalButton.Text = "Clear";
        clearTerminalButton.Cursor = Cursors.Hand;

        openBrowserButton.Location = new Point(398, 7);
        openBrowserButton.Size = new Size(116, 32);
        openBrowserButton.FlatStyle = FlatStyle.Flat;
        openBrowserButton.FlatAppearance.BorderSize = 1;
        openBrowserButton.Font = new Font("Segoe UI", 8.5F);
        openBrowserButton.Text = "Browser :9119";
        openBrowserButton.Cursor = Cursors.Hand;

        // 
        // Content Panel (docked fill)
        // 
        contentPanel.Dock = DockStyle.Fill;

        // 
        // Form Properties
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1020, 680);
        Controls.Add(contentPanel);
        Controls.Add(topToolbar);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowIcon = true;
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Agents Hub - CLI Terminal Workspace";

        ResumeLayout(false);
    }
}