using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HermesHub.Helpers;
using HermesHub.Services;
using Microsoft.Web.WebView2.Core;

namespace HermesHub;

public partial class CliWebviewForm : Form
{
    private static bool IsInDesignMode
    {
        get
        {
            if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
            {
                return true;
            }

            try
            {
                using var currentProc = Process.GetCurrentProcess();
                var procName = currentProc.ProcessName;
                return procName.IndexOf("devenv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       procName.IndexOf("DesignToolsServer", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }

    private readonly CliManagerService? _cliManager;
    private string _activeTabId = "hermes";
    private bool _hermesInitialized;
    private readonly string _terminalHtmlPath = string.Empty;
    private readonly string _hermesOfflineHtmlPath = string.Empty;
    private Color _toggleRunNormalColor = ThemeManager.Colors.AccentGreen;
    private Color _toggleRunHoverColor = ThemeManager.Colors.AccentGreenHover;

    private Microsoft.Web.WebView2.WinForms.WebView2 hermesWebView = null!;
    private Microsoft.Web.WebView2.WinForms.WebView2 claudeWebView = null!;
    private Microsoft.Web.WebView2.WinForms.WebView2 antigravityWebView = null!;
    private readonly Dictionary<string, Microsoft.Web.WebView2.WinForms.WebView2> _customWebViews = new(StringComparer.OrdinalIgnoreCase);
    private CoreWebView2Environment? _webViewEnv;
    private readonly TaskCompletionSource<bool> _envInitTcs = new();

    private ComboBox? _profileComboBox;
    private Button? _addProfileButton;
    private Button? _manageClisButton;
    private Button? _shellLauncherButton;
    private ContextMenuStrip? _shellContextMenu;
    private readonly List<Button> _dynamicTabButtons = new();
    private readonly ToolTip _actionToolTip = new() { ShowAlways = true };
    private bool _isUpdatingProfileCombo;

    public CliWebviewForm()
    {
        InitializeComponent();
    }

    public CliWebviewForm(CliManagerService? cliManager, CliType initialTab = CliType.Hermes) : this()
    {
        if (System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime && cliManager != null)
        {
            _cliManager = cliManager;
            _activeTabId = AiConfigService.NormalizeCliId(CliTypeToId(initialTab));

            var baseDir = AppContext.BaseDirectory;
            var uiTermPath = Path.Combine(baseDir, "ui", "terminal.html");
            _terminalHtmlPath = File.Exists(uiTermPath) ? uiTermPath : Path.Combine(baseDir, "assets", "web", "terminal.html");
            var uiOfflinePath = Path.Combine(baseDir, "ui", "hermes-offline.html");
            _hermesOfflineHtmlPath = File.Exists(uiOfflinePath) ? uiOfflinePath : Path.Combine(baseDir, "assets", "web", "hermes-offline.html");

            SetupStyles();
            HookEvents();

            _ = InitializeWebViewsAsync();
        }
    }

    public CliType ActiveTab => IdToCliType(_activeTabId);

    private static string CliTypeToId(CliType type) => type switch
    {
        CliType.ClaudeCode => "claude_code",
        CliType.AntigravityCli => "antigravity_cli",
        _ => "hermes"
    };

    private static CliType IdToCliType(string id)
    {
        string norm = AiConfigService.NormalizeCliId(id);
        return norm switch
        {
            "claude_code" => CliType.ClaudeCode,
            "antigravity_cli" => CliType.AntigravityCli,
            _ => CliType.Hermes
        };
    }

    private static bool IsBuiltInCli(string id)
    {
        string norm = AiConfigService.NormalizeCliId(id);
        return string.Equals(norm, "hermes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(norm, "claude_code", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(norm, "antigravity_cli", StringComparison.OrdinalIgnoreCase);
    }

    private void SetupStyles()
    {
        _profileComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.75F),
            Size = new Size(160, 28),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _profileComboBox.DrawItem += ProfileComboBox_DrawItem;

        _addProfileButton = new Button
        {
            Text = "+ Add",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Size = new Size(52, 32),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _addProfileButton.FlatAppearance.BorderSize = 1;
        _actionToolTip.SetToolTip(_addProfileButton, "Add new Antigravity account");

        toolsPanel.Controls.Add(_profileComboBox);
        toolsPanel.Controls.Add(_addProfileButton);

        SetupButtonHover(_addProfileButton);

        _shellLauncherButton = new Button
        {
            Size = new Size(32, 32),
            Text = "",
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _shellLauncherButton.FlatAppearance.BorderSize = 1;
        _shellLauncherButton.Paint += DrawShellLauncherIcon;
        _actionToolTip.SetToolTip(_shellLauncherButton, "Open Shell (CMD / PowerShell - Standard & Admin)");
        SetupButtonHover(_shellLauncherButton);
        _shellLauncherButton.Click += ShellLauncherButton_Click;
        toolsPanel.Controls.Add(_shellLauncherButton);

        toggleRunButton.Size = new Size(32, 32);
        toggleRunButton.Text = "";
        toggleRunButton.Paint += DrawToggleRunIcon;
        toggleRunButton.MouseEnter += (_, _) =>
        {
            if (toggleRunButton.Enabled) toggleRunButton.BackColor = _toggleRunHoverColor;
        };
        toggleRunButton.MouseLeave += (_, _) =>
        {
            if (toggleRunButton.Enabled) toggleRunButton.BackColor = _toggleRunNormalColor;
        };

        restartCliButton.Size = new Size(32, 32);
        restartCliButton.Text = "";
        restartCliButton.Paint += DrawRestartIcon;
        SetupButtonHover(restartCliButton);

        clearTerminalButton.Size = new Size(32, 32);
        clearTerminalButton.Text = "";
        clearTerminalButton.Paint += DrawClearIcon;
        SetupButtonHover(clearTerminalButton);

        openBrowserButton.Size = new Size(32, 32);
        openBrowserButton.Text = "";
        openBrowserButton.Paint += DrawBrowserIcon;
        SetupButtonHover(openBrowserButton);

        topToolbar.Resize += (_, _) => LayoutToolbar();
        toolsPanel.Resize += (_, _) => LayoutToolbar();
        contentPanel.Resize += (_, _) => TriggerTerminalFit();
        topToolbar.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeManager.Colors.CardBorder, 1);
            e.Graphics.DrawLine(pen, 0, topToolbar.Height - 1, topToolbar.Width, topToolbar.Height - 1);
        };

        RebuildTabs();
        ApplyTheme();
    }

    private void TriggerTerminalFit()
    {
        var targetWv = GetWebViewForId(_activeTabId);
        if (targetWv?.CoreWebView2 != null)
        {
            PostToWebView(targetWv, "{\"type\":\"fit\"}");
        }
    }

    private void DrawShellLauncherIcon(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color glyphColor = ThemeManager.Colors.TextPrimary;
        using var pen = new Pen(glyphColor, 1.8f);

        int cx = 16;
        int cy = 16;

        Point[] pts =
        [
            new Point(cx - 7, cy - 5),
            new Point(cx - 2, cy),
            new Point(cx - 7, cy + 5)
        ];
        g.DrawLines(pen, pts);

        g.DrawLine(pen, cx + 1, cy + 5, cx + 8, cy + 5);
    }

    private void ShellLauncherButton_Click(object? sender, EventArgs e)
    {
        if (_shellLauncherButton == null) return;
        BuildShellContextMenu();
        _shellContextMenu?.Show(_shellLauncherButton, new Point(0, _shellLauncherButton.Height + 2));
    }

    private void BuildShellContextMenu()
    {
        _shellContextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new ShellColorTable())
        };

        var c = ThemeManager.Colors;
        _shellContextMenu.BackColor = c.CardBackground;
        _shellContextMenu.ForeColor = c.TextPrimary;

        bool embed = StartupHelper.GetPreference("EmbedShellsInTerminalWorkspace", false);

        var cmdNormal = new ToolStripMenuItem("Command Prompt (Standard)", null, (_, _) =>
        {
            if (embed)
            {
                OpenEmbeddedShell("cmd", "Command Prompt", "cmd.exe");
            }
            else
            {
                ShellLauncher.OpenCommandPrompt(false);
            }
        })
        {
            ForeColor = c.TextPrimary
        };

        var cmdAdmin = new ToolStripMenuItem("Command Prompt (Administrator)", null, (_, _) => ShellLauncher.OpenCommandPrompt(true))
        {
            ForeColor = c.DangerRed
        };

        var psNormal = new ToolStripMenuItem("PowerShell (Standard)", null, (_, _) =>
        {
            if (embed)
            {
                OpenEmbeddedShell("powershell", "PowerShell", "powershell.exe");
            }
            else
            {
                ShellLauncher.OpenPowerShell(false);
            }
        })
        {
            ForeColor = c.TextPrimary
        };

        var psAdmin = new ToolStripMenuItem("PowerShell (Administrator)", null, (_, _) => ShellLauncher.OpenPowerShell(true))
        {
            ForeColor = c.DangerRed
        };

        _shellContextMenu.Items.Add(cmdNormal);
        _shellContextMenu.Items.Add(cmdAdmin);
        _shellContextMenu.Items.Add(new ToolStripSeparator());
        _shellContextMenu.Items.Add(psNormal);
        _shellContextMenu.Items.Add(psAdmin);
    }

    private sealed class ShellColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => ThemeManager.Colors.ButtonHover;
        public override Color MenuItemSelectedGradientBegin => ThemeManager.Colors.ButtonHover;
        public override Color MenuItemSelectedGradientEnd => ThemeManager.Colors.ButtonHover;
        public override Color MenuBorder => ThemeManager.Colors.CardBorder;
        public override Color ToolStripDropDownBackground => ThemeManager.Colors.CardBackground;
        public override Color ImageMarginGradientBegin => ThemeManager.Colors.CardBackground;
        public override Color ImageMarginGradientMiddle => ThemeManager.Colors.CardBackground;
        public override Color ImageMarginGradientEnd => ThemeManager.Colors.CardBackground;
        public override Color SeparatorDark => ThemeManager.Colors.CardBorder;
        public override Color SeparatorLight => ThemeManager.Colors.CardBorder;
    }

    private void ProfileComboBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _profileComboBox == null) return;

        var c = ThemeManager.Colors;
        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bg = isSelected ? c.ButtonHover : c.WellBackground;
        var fg = isSelected ? c.AccentGreen : c.TextPrimary;

        using var brush = new SolidBrush(bg);
        e.Graphics.FillRectangle(brush, e.Bounds);

        var item = _profileComboBox.Items[e.Index] as ProfileItem;
        string text = item?.DisplayText ?? _profileComboBox.Items[e.Index]?.ToString() ?? "";

        TextRenderer.DrawText(
            e.Graphics,
            text,
            e.Font ?? _profileComboBox.Font,
            new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height - 4),
            fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawToggleRunIcon(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_cliManager == null) return;
        var info = _cliManager.GetInfo(_activeTabId);

        Color glyphColor = info.State switch
        {
            CliState.Running => Color.White,
            CliState.Starting => Color.Black,
            _ => ThemeManager.Colors.AccentGreenText
        };

        using var brush = new SolidBrush(glyphColor);

        int cx = toggleRunButton.Width / 2;
        int cy = toggleRunButton.Height / 2;

        if (info.State == CliState.Running)
        {
            // Stop icon (square)
            g.FillRectangle(brush, cx - 5, cy - 5, 10, 10);
        }
        else if (info.State == CliState.Starting)
        {
            // Starting icon (pause / dual bars)
            g.FillRectangle(brush, cx - 5, cy - 5, 3, 10);
            g.FillRectangle(brush, cx + 2, cy - 5, 3, 10);
        }
        else
        {
            // Start icon (play triangle)
            Point[] pts =
            [
                new Point(cx - 4, cy - 6),
                new Point(cx - 4, cy + 6),
                new Point(cx + 6, cy)
            ];
            g.FillPolygon(brush, pts);
        }
    }

    private void DrawRestartIcon(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color glyphColor = restartCliButton.Enabled ? ThemeManager.Colors.TextPrimary : ThemeManager.Colors.TextMuted;
        using var pen = new Pen(glyphColor, 1.8f);

        int cx = restartCliButton.Width / 2;
        int cy = restartCliButton.Height / 2;
        int r = 6;

        g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, 45, 270);

        // Arrow head pointing clockwise
        using var brush = new SolidBrush(glyphColor);
        Point[] pts =
        [
            new Point(cx + 5, cy - 7),
            new Point(cx + 8, cy - 2),
            new Point(cx + 2, cy - 3)
        ];
        g.FillPolygon(brush, pts);
    }

    private void DrawClearIcon(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color glyphColor = clearTerminalButton.Enabled ? ThemeManager.Colors.TextPrimary : ThemeManager.Colors.TextMuted;
        using var pen = new Pen(glyphColor, 1.6f);

        int cx = clearTerminalButton.Width / 2;
        int cy = clearTerminalButton.Height / 2;

        // Trash lid
        g.DrawLine(pen, cx - 7, cy - 4, cx + 7, cy - 4);
        g.DrawLine(pen, cx - 3, cy - 6, cx + 3, cy - 6);

        // Trash bin body
        Point[] pts =
        [
            new Point(cx - 5, cy - 3),
            new Point(cx - 4, cy + 7),
            new Point(cx + 4, cy + 7),
            new Point(cx + 5, cy - 3)
        ];
        g.DrawLines(pen, pts);

        // Slats inside bin
        g.DrawLine(pen, cx - 2, cy - 1, cx - 2, cy + 5);
        g.DrawLine(pen, cx + 2, cy - 1, cx + 2, cy + 5);
    }

    private void DrawBrowserIcon(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color glyphColor = openBrowserButton.Enabled ? ThemeManager.Colors.TextPrimary : ThemeManager.Colors.TextMuted;
        using var pen = new Pen(glyphColor, 1.6f);

        int cx = openBrowserButton.Width / 2;
        int cy = openBrowserButton.Height / 2;
        int r = 7;

        // Globe circle
        g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
        // Equator
        g.DrawLine(pen, cx - r, cy, cx + r, cy);
        // Meridian ellipse
        g.DrawEllipse(pen, cx - (r / 2), cy - r, r, 2 * r);
    }

    private void DrawManageClisIcon(object? sender, PaintEventArgs e)
    {
        if (sender is not Button btn) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color glyphColor = btn.Enabled ? ThemeManager.Colors.TextPrimary : ThemeManager.Colors.TextMuted;
        using var pen = new Pen(glyphColor, 1.6f);

        int cx = btn.Width / 2;
        int cy = btn.Height / 2;

        // Settings / Slider icon: 2 parallel slider tracks with knobs
        g.DrawLine(pen, cx - 6, cy - 3, cx + 6, cy - 3);
        g.DrawLine(pen, cx - 6, cy + 4, cx + 6, cy + 4);

        using var brush = new SolidBrush(glyphColor);
        g.FillRectangle(brush, cx - 3, cy - 5, 3, 5);
        g.FillRectangle(brush, cx + 1, cy + 2, 3, 5);
    }

    public void RebuildTabs()
    {
        tabsPanel.SuspendLayout();
        tabsPanel.Controls.Clear();
        _dynamicTabButtons.Clear();

        var workspaces = AiConfigService.Instance.GetCliWorkspaces();
        var enabledWorkspaces = workspaces.Where(w => w.IsEnabled).ToList();

        if (enabledWorkspaces.Count == 0)
        {
            // Fallback: at least enable Hermes
            AiConfigService.Instance.ToggleCliWorkspace("hermes", true);
            enabledWorkspaces = AiConfigService.Instance.GetCliWorkspaces().Where(w => w.IsEnabled).ToList();
        }

        int curX = 8;
        foreach (var ws in enabledWorkspaces)
        {
            string normId = AiConfigService.NormalizeCliId(ws.Id);
            var btn = new Button
            {
                Tag = normId,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = Padding.Empty
            };
            btn.FlatAppearance.BorderSize = 1;

            string wsName = ws.Name;
            int estimatedWidth = Math.Max(95, TextRenderer.MeasureText(wsName, btn.Font).Width + 36);
            btn.Size = new Size(estimatedWidth, 34);
            btn.Location = new Point(curX, 6);
            curX += estimatedWidth + 6;

            string idToSwitch = normId;
            btn.Click += (_, _) => SwitchTab(idToSwitch);
            SetupTabHover(btn, normId);

            _dynamicTabButtons.Add(btn);
            tabsPanel.Controls.Add(btn);
        }

        _manageClisButton = new Button
        {
            Size = new Size(32, 34),
            Location = new Point(curX, 6),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Text = "",
            Margin = Padding.Empty
        };
        _manageClisButton.FlatAppearance.BorderSize = 1;
        _manageClisButton.Paint += DrawManageClisIcon;
        _manageClisButton.Click += (_, _) => OpenManageClisDialog();
        SetupButtonHover(_manageClisButton);
        _actionToolTip.SetToolTip(_manageClisButton, "Manage CLI Workspaces (Show/Hide, Add/Remove)");

        curX += _manageClisButton.Width + 8;
        tabsPanel.Controls.Add(_manageClisButton);

        tabsPanel.Width = curX;
        tabsPanel.ResumeLayout();

        if (!enabledWorkspaces.Any(w => string.Equals(AiConfigService.NormalizeCliId(w.Id), _activeTabId, StringComparison.OrdinalIgnoreCase)))
        {
            _activeTabId = AiConfigService.NormalizeCliId(enabledWorkspaces[0].Id);
        }

        UpdateTabButtonsUi();
        UpdateToolbarUi();
        LayoutToolbar();
    }

    private void OpenManageClisDialog()
    {
        using var form = new ManageClisForm();
        form.ConfigurationChanged += RebuildTabs;
        form.ShowDialog(this);
        RebuildTabs();
    }

    private void LayoutToolbar()
    {
        if (toolsPanel == null || topToolbar == null) return;

        toolsPanel.Width = Math.Max(380, topToolbar.ClientSize.Width - tabsPanel.Width);

        int right = toolsPanel.ClientSize.Width - 10;

        // Action buttons laid out from right to left (each 32x32)
        openBrowserButton.Location = new Point(right - openBrowserButton.Width, 7);
        right = openBrowserButton.Left - 6;

        clearTerminalButton.Location = new Point(right - clearTerminalButton.Width, 7);
        right = clearTerminalButton.Left - 6;

        restartCliButton.Location = new Point(right - restartCliButton.Width, 7);
        right = restartCliButton.Left - 6;

        toggleRunButton.Location = new Point(right - toggleRunButton.Width, 7);
        right = toggleRunButton.Left - 6;

        if (_shellLauncherButton != null)
        {
            _shellLauncherButton.Location = new Point(right - _shellLauncherButton.Width, 7);
            right = _shellLauncherButton.Left - 10;
        }

        // Antigravity account switcher controls
        bool isAntigravity = string.Equals(AiConfigService.NormalizeCliId(_activeTabId), "antigravity_cli", StringComparison.OrdinalIgnoreCase);
        if (isAntigravity && _profileComboBox != null && _addProfileButton != null)
        {
            _profileComboBox.Visible = true;
            _addProfileButton.Visible = true;

            _addProfileButton.Location = new Point(right - _addProfileButton.Width, 7);
            right = _addProfileButton.Left - 6;

            _profileComboBox.Location = new Point(right - _profileComboBox.Width, 9);
            right = _profileComboBox.Left - 12;
        }
        else
        {
            if (_profileComboBox != null) _profileComboBox.Visible = false;
            if (_addProfileButton != null) _addProfileButton.Visible = false;
        }

        // Status label strictly left-aligned and bounded to never overlap right controls
        int leftMargin = 12;
        activeStatusLabel.Location = new Point(leftMargin, 14);
        int availableWidth = Math.Max(30, right - leftMargin - 10);
        activeStatusLabel.MaximumSize = new Size(availableWidth, 24);
        activeStatusLabel.AutoEllipsis = true;
    }

    public void ApplyTheme()
    {
        if (IsInDesignMode) return;

        var c = ThemeManager.Colors;
        if (IsHandleCreated)
        {
            ThemeManager.ApplyDwmTitleBarTheme(Handle);
        }

        BackColor = c.WindowBackground;
        topToolbar.BackColor = c.HeaderBackground;
        tabsPanel.BackColor = c.HeaderBackground;
        toolsPanel.BackColor = c.HeaderBackground;
        contentPanel.BackColor = c.WindowBackground;

        activeStatusLabel.ForeColor = c.TextSecondary;

        restartCliButton.BackColor = c.ButtonBg;
        restartCliButton.ForeColor = c.ButtonText;
        restartCliButton.FlatAppearance.BorderColor = c.ButtonBorder;

        clearTerminalButton.BackColor = c.ButtonBg;
        clearTerminalButton.ForeColor = c.ButtonText;
        clearTerminalButton.FlatAppearance.BorderColor = c.ButtonBorder;

        openBrowserButton.BackColor = c.ButtonBg;
        openBrowserButton.ForeColor = c.ButtonText;
        openBrowserButton.FlatAppearance.BorderColor = c.ButtonBorder;

        if (_manageClisButton != null)
        {
            _manageClisButton.BackColor = c.ButtonBg;
            _manageClisButton.ForeColor = c.ButtonText;
            _manageClisButton.FlatAppearance.BorderColor = c.ButtonBorder;
        }

        if (_shellLauncherButton != null)
        {
            _shellLauncherButton.BackColor = c.ButtonBg;
            _shellLauncherButton.ForeColor = c.ButtonText;
            _shellLauncherButton.FlatAppearance.BorderColor = c.ButtonBorder;
            _shellLauncherButton.Invalidate();
        }

        if (_profileComboBox != null)
        {
            _profileComboBox.BackColor = c.WellBackground;
            _profileComboBox.ForeColor = c.TextPrimary;
        }

        if (_addProfileButton != null)
        {
            _addProfileButton.BackColor = c.ButtonBg;
            _addProfileButton.ForeColor = c.AccentGreen;
            _addProfileButton.FlatAppearance.BorderColor = c.AccentGreen;
        }

        UpdateTabButtonsUi();
        UpdateToolbarUi();
        topToolbar.Invalidate();
        toolsPanel.Invalidate();

        NotifyWebViewsTheme();
    }

    public void NotifyWebViewsTheme()
    {
        var payload = new
        {
            type = "theme_changed",
            theme = ThemeManager.CurrentTheme == AppTheme.Light ? "light" : "dark",
            accentColor = ThemeManager.AccentHex
        };
        var json = JsonSerializer.Serialize(payload);

        PostToWebView(hermesWebView, json);
        PostToWebView(claudeWebView, json);
        PostToWebView(antigravityWebView, json);

        foreach (var kvp in _customWebViews)
        {
            PostToWebView(kvp.Value, json);
        }
    }

    private static void PostToWebView(Microsoft.Web.WebView2.WinForms.WebView2? wv, string json)
    {
        if (wv?.CoreWebView2 != null)
        {
            try
            {
                wv.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { }
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!IsInDesignMode)
        {
            ApplyTheme();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private void HookEvents()
    {
        toggleRunButton.Click += async (_, _) => await HandleToggleRunClickAsync();
        restartCliButton.Click += async (_, _) =>
        {
            if (_cliManager != null)
            {
                await _cliManager.RestartAsync(_activeTabId);
            }
        };
        clearTerminalButton.Click += (_, _) => HandleClearTerminalClick();
        openBrowserButton.Click += (_, _) => HandleOpenBrowserClick();

        if (_profileComboBox != null)
        {
            _profileComboBox.SelectedIndexChanged += async (_, _) =>
            {
                if (_isUpdatingProfileCombo || _cliManager == null) return;
                if (_profileComboBox.SelectedItem is ProfileItem item)
                {
                    if (!string.Equals(item.ProfileName, _cliManager.ProfileService.ActiveProfile, StringComparison.OrdinalIgnoreCase))
                    {
                        _profileComboBox.Enabled = false;
                        if (_addProfileButton != null) _addProfileButton.Enabled = false;
                        try
                        {
                            await _cliManager.SwitchAntigravityProfileAsync(item.ProfileName);
                        }
                        finally
                        {
                            _profileComboBox.Enabled = true;
                            if (_addProfileButton != null) _addProfileButton.Enabled = true;
                            RefreshProfileCombo();
                        }
                    }
                }
            };
        }

        if (_addProfileButton != null)
        {
            _addProfileButton.Click += async (_, _) =>
            {
                if (_cliManager == null) return;
                var existing = _cliManager.ProfileService.GetProfiles();
                string defaultName = $"Account_{existing.Count + 1}";
                string? name = PromptForProfileName(defaultName);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (existing.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(this, $"A profile named '{name}' already exists.", "Profile Exists", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    await _cliManager.CreateAndSwitchNewProfileAsync(name);
                    RefreshProfileCombo();
                }
            };
        }

        if (_cliManager != null)
        {
            _cliManager.ProfileService.ActiveProfileChanged += (_) =>
            {
                if (InvokeRequired) BeginInvoke(RefreshProfileCombo);
                else RefreshProfileCombo();
            };

            _cliManager.ProfileService.ProfileSaved += (_) =>
            {
                if (InvokeRequired) BeginInvoke(RefreshProfileCombo);
                else RefreshProfileCombo();
            };

            _cliManager.StatusChangedId += (id, state, msg) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => OnCliStatusChanged(id, state, msg));
                }
                else
                {
                    OnCliStatusChanged(id, state, msg);
                }
            };

            _cliManager.TerminalDataReceivedId += (id, text) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => ForwardTerminalData(id, text));
                }
                else
                {
                    ForwardTerminalData(id, text);
                }
            };
        }

        ThemeManager.ThemeChanged += () =>
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(ApplyTheme);
            else ApplyTheme();
        };

        RefreshProfileCombo();
    }

    public void SwitchTab(CliType type) => SwitchTab(CliTypeToId(type));

    public void SwitchTab(string id)
    {
        _activeTabId = AiConfigService.NormalizeCliId(id);
        id = _activeTabId;

        UpdateTabButtonsUi();
        UpdateToolbarUi();
        LayoutToolbar();

        if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            RefreshProfileCombo();
        }

        if (hermesWebView != null) hermesWebView.Visible = string.Equals(id, "hermes", StringComparison.OrdinalIgnoreCase);
        if (claudeWebView != null) claudeWebView.Visible = string.Equals(id, "claude_code", StringComparison.OrdinalIgnoreCase);
        if (antigravityWebView != null) antigravityWebView.Visible = string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase);

        foreach (var kvp in _customWebViews)
        {
            kvp.Value.Visible = string.Equals(AiConfigService.NormalizeCliId(kvp.Key), id, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(id, "hermes", StringComparison.OrdinalIgnoreCase) && _hermesInitialized)
        {
            RefreshHermesNavigation();
        }
        else if (!IsBuiltInCli(id))
        {
            var ws = AiConfigService.Instance.GetCliWorkspaces().FirstOrDefault(w => string.Equals(AiConfigService.NormalizeCliId(w.Id), id, StringComparison.OrdinalIgnoreCase));
            if (ws != null)
            {
                _ = EnsureCustomWebViewAsync(ws);
            }
        }

        TriggerTerminalFit();
    }

    public void OpenEmbeddedShell(string shellId, string name, string command)
    {
        if (InvokeRequired)
        {
            Invoke(() => OpenEmbeddedShell(shellId, name, command));
            return;
        }

        shellId = AiConfigService.NormalizeCliId(shellId);

        var cfg = AiConfigService.Instance.CurrentConfig;
        var existing = cfg.CliWorkspaces.FirstOrDefault(w => string.Equals(AiConfigService.NormalizeCliId(w.Id), shellId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            cfg.CliWorkspaces.Add(new CliWorkspaceConfig
            {
                Id = shellId,
                Name = name,
                Command = command,
                IsWebUi = false,
                IsDefault = false,
                IsEnabled = true
            });
            AiConfigService.Instance.SaveConfig(cfg);
        }
        else if (!existing.IsEnabled)
        {
            existing.IsEnabled = true;
            AiConfigService.Instance.SaveConfig(cfg);
        }

        RebuildTabs();
        SwitchTab(shellId);

        if (_cliManager != null)
        {
            var info = _cliManager.GetInfo(shellId);
            if (info.State != CliState.Running && info.State != CliState.Starting)
            {
                _cliManager.Start(shellId);
            }
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        ApplyTheme();
        Show();
        BringToFront();
        Activate();
    }

    private async Task EnsureCustomWebViewAsync(CliWorkspaceConfig ws)
    {
        if (_customWebViews.ContainsKey(ws.Id)) return;

        if (_webViewEnv == null)
        {
            await _envInitTcs.Task;
        }

        if (_webViewEnv == null) return;

        try
        {
            var wv = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Dock = DockStyle.Fill,
                Visible = string.Equals(_activeTabId, ws.Id, StringComparison.OrdinalIgnoreCase)
            };
            contentPanel.Controls.Add(wv);
            _customWebViews[ws.Id] = wv;

            await wv.EnsureCoreWebView2Async(_webViewEnv);
            ConfigureWebViewSettings(wv.CoreWebView2);
            wv.CoreWebView2.NavigationCompleted += (_, _) => NotifyWebViewsTheme();

            if (ws.IsWebUi && ws.Port.HasValue)
            {
                var targetUrl = $"http://127.0.0.1:{ws.Port.Value}/";
                wv.CoreWebView2.Navigate(targetUrl);
            }
            else
            {
                wv.CoreWebView2.WebMessageReceived += (s, e) => TerminalWebView_WebMessageReceived(ws.Id, e);
                if (File.Exists(_terminalHtmlPath))
                {
                    wv.CoreWebView2.Navigate(new Uri(_terminalHtmlPath).AbsoluteUri);
                }
            }

            NotifyWebViewsTheme();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CliWebviewForm] Failed to create WebView for custom CLI '{ws.Name}': {ex.Message}");
        }
    }

    private async Task InitializeWebViewsAsync()
    {
        try
        {
            hermesWebView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            claudeWebView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            antigravityWebView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };

            contentPanel.Controls.Add(hermesWebView);
            contentPanel.Controls.Add(claudeWebView);
            contentPanel.Controls.Add(antigravityWebView);

            var baseLocalApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var legacyUserFolder = Path.Combine(baseLocalApp, "HermesHub", "WebView2Data");
            var newUserFolder = Path.Combine(baseLocalApp, "AgentsHub", "WebView2Data");
            var userDataFolder = Directory.Exists(legacyUserFolder) && !Directory.Exists(newUserFolder) ? legacyUserFolder : newUserFolder;
            _webViewEnv = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            _envInitTcs.TrySetResult(true);

            // Hermes WebView
            await hermesWebView.EnsureCoreWebView2Async(_webViewEnv);
            ConfigureWebViewSettings(hermesWebView.CoreWebView2);
            hermesWebView.CoreWebView2.WebMessageReceived += HermesWebView_WebMessageReceived;
            hermesWebView.CoreWebView2.NavigationCompleted += (_, _) => NotifyWebViewsTheme();
            _hermesInitialized = true;
            RefreshHermesNavigation();

            // Claude Code WebView
            await claudeWebView.EnsureCoreWebView2Async(_webViewEnv);
            ConfigureWebViewSettings(claudeWebView.CoreWebView2);
            claudeWebView.CoreWebView2.WebMessageReceived += (s, e) => TerminalWebView_WebMessageReceived("claude_code", e);
            claudeWebView.CoreWebView2.NavigationCompleted += (_, _) => NotifyWebViewsTheme();
            if (File.Exists(_terminalHtmlPath))
            {
                claudeWebView.CoreWebView2.Navigate(new Uri(_terminalHtmlPath).AbsoluteUri);
            }

            // Antigravity CLI WebView
            await antigravityWebView.EnsureCoreWebView2Async(_webViewEnv);
            ConfigureWebViewSettings(antigravityWebView.CoreWebView2);
            antigravityWebView.CoreWebView2.WebMessageReceived += (s, e) => TerminalWebView_WebMessageReceived("antigravity_cli", e);
            antigravityWebView.CoreWebView2.NavigationCompleted += (_, _) => NotifyWebViewsTheme();
            if (File.Exists(_terminalHtmlPath))
            {
                antigravityWebView.CoreWebView2.Navigate(new Uri(_terminalHtmlPath).AbsoluteUri);
            }

            SwitchTab(_activeTabId);
            NotifyWebViewsTheme();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 failed to initialize: {ex.Message}", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ConfigureWebViewSettings(CoreWebView2 webView)
    {
        webView.Settings.AreDefaultContextMenusEnabled = true;
        webView.Settings.AreDevToolsEnabled = false;
        webView.Settings.IsStatusBarEnabled = false;
        webView.Settings.IsZoomControlEnabled = true;
    }

    private void RefreshHermesNavigation()
    {
        if (!_hermesInitialized || _cliManager == null || hermesWebView?.CoreWebView2 == null)
        {
            return;
        }

        var hermesInfo = _cliManager.GetInfo("hermes");
        if (hermesInfo.State == CliState.Running)
        {
            var targetUrl = $"http://127.0.0.1:{_cliManager.HermesService.Port}/";
            if (hermesWebView.Source == null || !hermesWebView.Source.AbsoluteUri.StartsWith(targetUrl))
            {
                hermesWebView.CoreWebView2.Navigate(targetUrl);
            }
        }
        else
        {
            if (File.Exists(_hermesOfflineHtmlPath))
            {
                hermesWebView.CoreWebView2.Navigate(new Uri(_hermesOfflineHtmlPath).AbsoluteUri);
            }
            else
            {
                hermesWebView.CoreWebView2.NavigateToString("<html><body style='background:#09090b;color:#fafafa;font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;'><h2>Hermes Engine Offline</h2></body></html>");
            }
        }
    }

    private void HermesWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                var typeStr = typeEl.GetString();
                if (typeStr == "start_hermes")
                {
                    _cliManager?.Start("hermes");
                }
            }
        }
        catch
        {
            // Suppress invalid JSON parsing errors
        }
    }

    private void TerminalWebView_WebMessageReceived(string cliId, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_cliManager == null) return;

        try
        {
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                var msgType = typeEl.GetString();
                switch (msgType)
                {
                    case "ready":
                        SendTerminalInit(cliId);
                        break;

                    case "input":
                        if (doc.RootElement.TryGetProperty("data", out var dataEl))
                        {
                            var input = dataEl.GetString();
                            if (input != null)
                            {
                                _cliManager.SendInput(cliId, input);
                            }
                        }
                        break;

                    case "resize":
                        if (doc.RootElement.TryGetProperty("cols", out var colsEl) &&
                            doc.RootElement.TryGetProperty("rows", out var rowsEl))
                        {
                            short cols = colsEl.GetInt16();
                            short rows = rowsEl.GetInt16();
                            _cliManager.Resize(cliId, cols, rows);
                        }
                        break;

                    case "start":
                        _cliManager.Start(cliId);
                        break;
                }
            }
        }
        catch
        {
            // Suppress invalid JSON parsing errors
        }
    }

    private Microsoft.Web.WebView2.WinForms.WebView2? GetWebViewForId(string id)
    {
        if (string.Equals(id, "hermes", StringComparison.OrdinalIgnoreCase)) return hermesWebView;
        if (string.Equals(id, "claude_code", StringComparison.OrdinalIgnoreCase)) return claudeWebView;
        if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase)) return antigravityWebView;
        if (_customWebViews.TryGetValue(id, out var wv)) return wv;
        return null;
    }

    private void SendTerminalInit(string cliId)
    {
        if (_cliManager == null) return;

        var targetWv = GetWebViewForId(cliId);
        if (targetWv?.CoreWebView2 == null) return;

        var info = _cliManager.GetInfo(cliId);
        var history = _cliManager.GetTerminalHistory(cliId);

        var payload = new TerminalInitPayload("init", info.ShortName, info.State == CliState.Running, info.StatusText, history);
        targetWv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, CliJsonContext.Default.TerminalInitPayload));
    }

    private void ForwardTerminalData(string cliId, string text)
    {
        var targetWv = GetWebViewForId(cliId);
        if (targetWv?.CoreWebView2 == null) return;

        var payload = new TerminalOutputPayload("output", text);
        targetWv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, CliJsonContext.Default.TerminalOutputPayload));
    }

    private void OnCliStatusChanged(string cliId, CliState state, string message)
    {
        UpdateTabButtonsUi();

        if (string.Equals(cliId, _activeTabId, StringComparison.OrdinalIgnoreCase))
        {
            UpdateToolbarUi();
        }

        if (string.Equals(cliId, "hermes", StringComparison.OrdinalIgnoreCase))
        {
            RefreshHermesNavigation();
        }
        else
        {
            var targetWv = GetWebViewForId(cliId);
            if (targetWv?.CoreWebView2 != null)
            {
                var payload = new TerminalStatusPayload("status", state == CliState.Running, message);
                targetWv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, CliJsonContext.Default.TerminalStatusPayload));
            }
        }
    }

    private void UpdateTabButtonsUi()
    {
        if (_cliManager == null) return;
        var c = ThemeManager.Colors;

        foreach (var btn in _dynamicTabButtons)
        {
            if (btn.Tag is not string id) continue;

            var info = _cliManager.GetInfo(id);
            bool isSelected = string.Equals(_activeTabId, id, StringComparison.OrdinalIgnoreCase);

            var dotColor = info.State switch
            {
                CliState.Running => "●",
                CliState.Starting => "◐",
                CliState.Error => "▲",
                _ => "○"
            };

            btn.Text = $"{dotColor} {info.ShortName}";

            if (isSelected)
            {
                btn.BackColor = c.ButtonHover;
                btn.ForeColor = c.TextPrimary;
                btn.FlatAppearance.BorderColor = c.AccentGreen;
            }
            else
            {
                btn.BackColor = c.ButtonBg;
                btn.ForeColor = c.TextSecondary;
                btn.FlatAppearance.BorderColor = c.ButtonBorder;
            }
        }

        if (_manageClisButton != null)
        {
            _manageClisButton.BackColor = c.ButtonBg;
            _manageClisButton.FlatAppearance.BorderColor = c.ButtonBorder;
            _manageClisButton.Invalidate();
        }
    }

    private void UpdateToolbarUi()
    {
        if (_cliManager == null) return;
        var c = ThemeManager.Colors;
        var info = _cliManager.GetInfo(_activeTabId);
        activeStatusLabel.Text = $"{info.ShortName}: {info.StatusText}";

        int browserPort = GetBrowserPortForActiveTab();

        if (info.State == CliState.Running)
        {
            activeStatusLabel.ForeColor = c.AccentGreen;
            _toggleRunNormalColor = c.DangerRed;
            _toggleRunHoverColor = Color.FromArgb(220, 38, 38);
            toggleRunButton.BackColor = _toggleRunNormalColor;
            toggleRunButton.FlatAppearance.BorderColor = c.DangerRed;
            _actionToolTip.SetToolTip(toggleRunButton, $"Stop {info.ShortName}");

            restartCliButton.Enabled = true;
            clearTerminalButton.Enabled = true;
        }
        else if (info.State == CliState.Starting)
        {
            activeStatusLabel.ForeColor = c.WarningAmber;
            _toggleRunNormalColor = c.WarningAmber;
            _toggleRunHoverColor = Color.FromArgb(217, 119, 6);
            toggleRunButton.BackColor = _toggleRunNormalColor;
            toggleRunButton.FlatAppearance.BorderColor = c.WarningAmber;
            _actionToolTip.SetToolTip(toggleRunButton, $"Starting {info.ShortName}...");

            restartCliButton.Enabled = false;
            clearTerminalButton.Enabled = true;
        }
        else
        {
            activeStatusLabel.ForeColor = c.TextMuted;
            _toggleRunNormalColor = c.AccentGreen;
            _toggleRunHoverColor = c.AccentGreenHover;
            toggleRunButton.BackColor = _toggleRunNormalColor;
            toggleRunButton.FlatAppearance.BorderColor = c.AccentGreen;
            _actionToolTip.SetToolTip(toggleRunButton, $"Start {info.ShortName}");

            restartCliButton.Enabled = false;
            clearTerminalButton.Enabled = true;
        }

        _actionToolTip.SetToolTip(restartCliButton, $"Restart {info.ShortName}");
        _actionToolTip.SetToolTip(clearTerminalButton, "Clear terminal screen");

        if (browserPort > 0)
        {
            openBrowserButton.Visible = true;
            _actionToolTip.SetToolTip(openBrowserButton, $"Open in browser (http://127.0.0.1:{browserPort}/)");
        }
        else
        {
            openBrowserButton.Visible = false;
        }

        toggleRunButton.Invalidate();
        restartCliButton.Invalidate();
        clearTerminalButton.Invalidate();
        openBrowserButton.Invalidate();

        LayoutToolbar();
    }

    private int GetBrowserPortForActiveTab()
    {
        if (string.Equals(_activeTabId, "hermes", StringComparison.OrdinalIgnoreCase))
        {
            return _cliManager?.HermesService.Port ?? 8000;
        }
        if (string.Equals(_activeTabId, "claude_code", StringComparison.OrdinalIgnoreCase))
        {
            return 9229;
        }
        if (string.Equals(_activeTabId, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            return 9339;
        }

        var ws = AiConfigService.Instance.GetCliWorkspaces().FirstOrDefault(w => string.Equals(w.Id, _activeTabId, StringComparison.OrdinalIgnoreCase));
        return ws?.Port ?? 0;
    }

    private async Task HandleToggleRunClickAsync()
    {
        if (_cliManager == null) return;

        var info = _cliManager.GetInfo(_activeTabId);
        if (info.State == CliState.Running || info.State == CliState.Starting)
        {
            await _cliManager.StopAsync(_activeTabId);
        }
        else
        {
            _cliManager.Start(_activeTabId);
        }
    }

    private void HandleClearTerminalClick()
    {
        if (_cliManager == null || string.Equals(_activeTabId, "hermes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _cliManager.ClearTerminalHistory(_activeTabId);
        var targetWv = GetWebViewForId(_activeTabId);
        if (targetWv?.CoreWebView2 != null)
        {
            var payload = new TerminalActionPayload("clear");
            targetWv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, CliJsonContext.Default.TerminalActionPayload));
        }
    }

    internal record TerminalInitPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("isRunning")] bool IsRunning,
        [property: JsonPropertyName("statusText")] string StatusText,
        [property: JsonPropertyName("history")] string History);

    internal record TerminalOutputPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("data")] string Data);

    internal record TerminalStatusPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("isRunning")] bool IsRunning,
        [property: JsonPropertyName("statusText")] string StatusText);

    internal record TerminalActionPayload(
        [property: JsonPropertyName("type")] string Type);

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(TerminalInitPayload))]
    [JsonSerializable(typeof(TerminalOutputPayload))]
    [JsonSerializable(typeof(TerminalStatusPayload))]
    [JsonSerializable(typeof(TerminalActionPayload))]
    internal partial class CliJsonContext : JsonSerializerContext
    {
    }

    private void HandleOpenBrowserClick()
    {
        if (_cliManager == null) return;

        try
        {
            int port = GetBrowserPortForActiveTab();
            if (port > 0)
            {
                Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void SetupButtonHover(Button button)
    {
        button.MouseEnter += (_, _) =>
        {
            if (button.Enabled) button.BackColor = ThemeManager.Colors.ButtonHover;
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Enabled) button.BackColor = ThemeManager.Colors.ButtonBg;
        };
    }

    private void SetupTabHover(Button btn, string id)
    {
        btn.MouseEnter += (_, _) =>
        {
            if (!string.Equals(_activeTabId, id, StringComparison.OrdinalIgnoreCase))
            {
                btn.BackColor = ThemeManager.Colors.ButtonHover;
            }
        };
        btn.MouseLeave += (_, _) =>
        {
            if (!string.Equals(_activeTabId, id, StringComparison.OrdinalIgnoreCase))
            {
                btn.BackColor = ThemeManager.Colors.ButtonBg;
            }
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Tab))
        {
            CycleTab(forward: true);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            CycleTab(forward: false);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void CycleTab(bool forward)
    {
        if (_dynamicTabButtons.Count == 0) return;

        int currentIndex = _dynamicTabButtons.FindIndex(b => string.Equals(b.Tag as string, _activeTabId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0) currentIndex = 0;

        int nextIndex = forward
            ? (currentIndex + 1) % _dynamicTabButtons.Count
            : (currentIndex - 1 + _dynamicTabButtons.Count) % _dynamicTabButtons.Count;

        if (_dynamicTabButtons[nextIndex].Tag is string nextId)
        {
            SwitchTab(nextId);
        }
    }

    private void RefreshProfileCombo()
    {
        if (IsInDesignMode || _cliManager == null || _profileComboBox == null) return;

        try
        {
            _isUpdatingProfileCombo = true;
            _profileComboBox.Items.Clear();

            var profiles = _cliManager.ProfileService.GetProfiles();
            var active = _cliManager.ProfileService.ActiveProfile;
            ProfileItem? selectedItem = null;

            foreach (var p in profiles)
            {
                string? email = _cliManager.ProfileService.GetAccountEmail(p);
                string display = !string.IsNullOrWhiteSpace(email) ? $"{p} ({email})" : p;
                var item = new ProfileItem { ProfileName = p, DisplayText = display };
                _profileComboBox.Items.Add(item);

                if (string.Equals(p, active, StringComparison.OrdinalIgnoreCase))
                {
                    selectedItem = item;
                }
            }

            if (selectedItem != null)
            {
                _profileComboBox.SelectedItem = selectedItem;
            }
            else if (_profileComboBox.Items.Count > 0)
            {
                _profileComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingProfileCombo = false;
        }
    }

    private string? PromptForProfileName(string defaultName)
    {
        using var form = new Form
        {
            Text = "New Antigravity Profile",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(330, 135),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = ThemeManager.Colors.WindowBackground,
            ForeColor = ThemeManager.Colors.TextPrimary
        };

        var lbl = new Label
        {
            Text = "Enter new profile / account name:",
            Location = new Point(16, 16),
            AutoSize = true,
            ForeColor = ThemeManager.Colors.TextSecondary
        };

        var txt = new TextBox
        {
            Text = defaultName,
            Location = new Point(16, 42),
            Width = 296,
            BackColor = ThemeManager.Colors.WellBackground,
            ForeColor = ThemeManager.Colors.TextPrimary
        };

        var btnOk = new Button
        {
            Text = "Create & Login",
            DialogResult = DialogResult.OK,
            Location = new Point(148, 82),
            Size = new Size(104, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.Colors.AccentGreen,
            ForeColor = ThemeManager.Colors.AccentGreenText,
            Cursor = Cursors.Hand
        };
        btnOk.FlatAppearance.BorderSize = 0;

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(258, 82),
            Size = new Size(54, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.Colors.ButtonBg,
            ForeColor = ThemeManager.Colors.TextSecondary,
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderColor = ThemeManager.Colors.ButtonBorder;

        form.Controls.AddRange([lbl, txt, btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        txt.SelectAll();
        return form.ShowDialog(this) == DialogResult.OK ? txt.Text.Trim() : null;
    }

    private sealed class ProfileItem
    {
        public string ProfileName { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public override string ToString() => DisplayText;
    }
}