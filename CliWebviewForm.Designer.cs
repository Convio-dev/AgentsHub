#nullable enable

namespace HermesHub;

partial class CliWebviewForm
{
    private System.ComponentModel.IContainer? components = null;

    private Panel topToolbar = null!;
    private Panel tabsPanel = null!;

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
        if (disposing)
        {
            _tabContextMenu?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        topToolbar = new Panel();
        tabsPanel = new Panel();

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

        // 
        // Tools Panel (fills remainder of toolbar)
        // 
        toolsPanel.Dock = DockStyle.Fill;
        toolsPanel.Controls.Add(activeStatusLabel);
        toolsPanel.Controls.Add(toggleRunButton);
        toolsPanel.Controls.Add(restartCliButton);
        toolsPanel.Controls.Add(clearTerminalButton);
        toolsPanel.Controls.Add(openBrowserButton);

        activeStatusLabel.AutoSize = true;
        activeStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        activeStatusLabel.Location = new Point(8, 15);
        activeStatusLabel.Text = "Status: Offline";

        toggleRunButton.Size = new Size(32, 32);
        toggleRunButton.FlatStyle = FlatStyle.Flat;
        toggleRunButton.FlatAppearance.BorderSize = 1;
        toggleRunButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        toggleRunButton.Text = "";
        toggleRunButton.Cursor = Cursors.Hand;

        restartCliButton.Size = new Size(32, 32);
        restartCliButton.FlatStyle = FlatStyle.Flat;
        restartCliButton.FlatAppearance.BorderSize = 1;
        restartCliButton.Font = new Font("Segoe UI", 9F);
        restartCliButton.Text = "";
        restartCliButton.Cursor = Cursors.Hand;

        clearTerminalButton.Size = new Size(32, 32);
        clearTerminalButton.FlatStyle = FlatStyle.Flat;
        clearTerminalButton.FlatAppearance.BorderSize = 1;
        clearTerminalButton.Font = new Font("Segoe UI", 9F);
        clearTerminalButton.Text = "";
        clearTerminalButton.Cursor = Cursors.Hand;

        openBrowserButton.Size = new Size(32, 32);
        openBrowserButton.FlatStyle = FlatStyle.Flat;
        openBrowserButton.FlatAppearance.BorderSize = 1;
        openBrowserButton.Font = new Font("Segoe UI", 9F);
        openBrowserButton.Text = "";
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
        // The toolbar paints its own background, so it must stay above the
        // content panel or terminal content shows through behind it.
        topToolbar.BringToFront();
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