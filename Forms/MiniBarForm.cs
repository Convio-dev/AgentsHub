#nullable enable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AgentsHub.Helpers;

namespace AgentsHub.Forms;

public sealed partial class MiniBarForm : Form
{
    private readonly Form _mainForm;
    private readonly Action<string> _onSendQuickPrompt;
    private readonly Func<string> _getActiveWorkspaceName;
    private readonly Func<int> _getActiveAgentCount;

    private Point _dragStart;
    private bool _isDragging;
    private TextBox? _promptBox;
    private Label? _workspaceLabel;
    private Label? _statusLabel;
    private Button? _btnQuickPrompt;

    public MiniBarForm(Form mainForm, Action<string> onSendQuickPrompt, Func<string> getActiveWorkspaceName, Func<int> getActiveAgentCount)
    {
        _mainForm = mainForm;
        _onSendQuickPrompt = onSendQuickPrompt;
        _getActiveWorkspaceName = getActiveWorkspaceName;
        _getActiveAgentCount = getActiveAgentCount;

        InitializeMiniBar();
    }

    private void InitializeMiniBar()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Size = new Size(380, 44);
        BackColor = Color.FromArgb(11, 15, 25);
        ForeColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);

        // Position top-center of primary screen by default
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.X + (screen.Width - Width) / 2, screen.Y + 16);

        // Enable DWM rounded corners
        ThemeManager.ApplyDwmTitleBarTheme(Handle);

        BuildControls();
        ApplyRoundedRegion();

        MouseDown += OnFormMouseDown;
        MouseMove += OnFormMouseMove;
        MouseUp += OnFormMouseUp;
    }

    private void BuildControls()
    {
        Controls.Clear();

        // 1. Grip / Hub Indicator Dot
        var dot = new Panel
        {
            Size = new Size(10, 10),
            Location = new Point(14, 17),
            BackColor = Color.FromArgb(16, 185, 129), // Emerald
            Cursor = Cursors.SizeAll
        };
        ApplyCircleRegion(dot);
        dot.MouseDown += OnFormMouseDown;
        dot.MouseMove += OnFormMouseMove;
        dot.MouseUp += OnFormMouseUp;
        Controls.Add(dot);

        // 2. Workspace & Agent Count Status
        _workspaceLabel = new Label
        {
            Text = _getActiveWorkspaceName(),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            Location = new Point(32, 13),
            AutoSize = true,
            Cursor = Cursors.SizeAll
        };
        _workspaceLabel.MouseDown += OnFormMouseDown;
        _workspaceLabel.MouseMove += OnFormMouseMove;
        _workspaceLabel.MouseUp += OnFormMouseUp;
        Controls.Add(_workspaceLabel);

        _statusLabel = new Label
        {
            Text = $"· {_getActiveAgentCount()} active",
            Font = new Font("Segoe UI", 8F, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(32 + _workspaceLabel.PreferredWidth + 4, 14),
            AutoSize = true,
            Cursor = Cursors.SizeAll
        };
        _statusLabel.MouseDown += OnFormMouseDown;
        _statusLabel.MouseMove += OnFormMouseMove;
        _statusLabel.MouseUp += OnFormMouseUp;
        Controls.Add(_statusLabel);

        // 3. Quick Prompt Input Box (hidden by default)
        _promptBox = new TextBox
        {
            Location = new Point(160, 10),
            Size = new Size(140, 24),
            BackColor = Color.FromArgb(17, 24, 39),
            ForeColor = Color.FromArgb(248, 250, 252),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            PlaceholderText = "Send prompt..."
        };
        _promptBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter && !string.IsNullOrWhiteSpace(_promptBox.Text))
            {
                var text = _promptBox.Text.Trim();
                _promptBox.Clear();
                _promptBox.Visible = false;
                _btnQuickPrompt!.Visible = true;
                _onSendQuickPrompt(text);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _promptBox.Visible = false;
                _btnQuickPrompt!.Visible = true;
                e.Handled = true;
            }
        };
        Controls.Add(_promptBox);

        // 4. Quick Prompt Trigger Button
        _btnQuickPrompt = new Button
        {
            Text = "Prompt",
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            Size = new Size(54, 22),
            Location = new Point(246, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(56, 189, 248),
            Cursor = Cursors.Hand
        };
        _btnQuickPrompt.FlatAppearance.BorderSize = 0;
        _btnQuickPrompt.Click += (s, e) =>
        {
            _btnQuickPrompt.Visible = false;
            _promptBox.Visible = true;
            _promptBox.Focus();
        };
        Controls.Add(_btnQuickPrompt);

        // 5. Open Hub Button
        var btnOpen = new Button
        {
            Text = "Open",
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            Size = new Size(44, 22),
            Location = new Point(304, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(248, 250, 252),
            Cursor = Cursors.Hand
        };
        btnOpen.FlatAppearance.BorderSize = 0;
        btnOpen.Click += (s, e) =>
        {
            if (_mainForm.WindowState == FormWindowState.Minimized) _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Show();
            _mainForm.Activate();
        };
        Controls.Add(btnOpen);

        // 6. Close / Hide Button
        var btnClose = new Button
        {
            Text = "x",
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            Size = new Size(20, 22),
            Location = new Point(352, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(148, 163, 184),
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (s, e) => Hide();
        Controls.Add(btnClose);
    }

    public void RefreshState()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            if (_workspaceLabel != null)
            {
                _workspaceLabel.Text = _getActiveWorkspaceName();
            }
            if (_statusLabel != null && _workspaceLabel != null)
            {
                _statusLabel.Text = $"· {_getActiveAgentCount()} active";
                _statusLabel.Location = new Point(32 + _workspaceLabel.PreferredWidth + 4, 14);
            }
        }));
    }

    private void ApplyRoundedRegion()
    {
        var path = new GraphicsPath();
        int r = 16;
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(Width - r, 0, r, r, 270, 90);
        path.AddArc(Width - r, Height - r, r, r, 0, 90);
        path.AddArc(0, Height - r, r, r, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    private static void ApplyCircleRegion(Control c)
    {
        var path = new GraphicsPath();
        path.AddEllipse(0, 0, c.Width, c.Height);
        c.Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(51, 65, 85), 1.5f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawRoundedRectangle(pen, new Rectangle(1, 1, Width - 2, Height - 2), new Size(16, 16));
    }

    private void OnFormMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _dragStart = e.Location;
        }
    }

    private void OnFormMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var cur = PointToScreen(e.Location);
            Location = new Point(cur.X - _dragStart.X, cur.Y - _dragStart.Y);
        }
    }

    private void OnFormMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
    }
}
