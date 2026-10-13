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
                return procName.Contains("devenv", StringComparison.OrdinalIgnoreCase) ||
                       procName.Contains("DesignToolsServer", StringComparison.OrdinalIgnoreCase);
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
    private ComboBox? _claudeEngineComboBox;
    private ComboBox? _claudeModelComboBox;
    private Button? _manageClisButton;
    private Button? _shellLauncherButton;
    private ContextMenuStrip? _shellContextMenu;
    private readonly List<Button> _dynamicTabButtons = [];
    private readonly ToolTip _actionToolTip = new() { ShowAlways = true };
    private bool _isUpdatingProfileCombo;
    private bool _isUpdatingClaudeControls;
    private bool _isLayoutRunning;

    // Empty-state workspace view, shown when no CLI workspace is enabled.
    private Panel? _emptyStatePanel;
    private Panel? _emptyStateCard;
    private Label? _emptyTitleLabel;
    private Label? _emptySubtitleLabel;
    private ComboBox? _emptyAddCombo;
    private Button? _emptyAddButton;
    private Label? _emptyHintLabel;

    // Rebuilt on every right-click so a stale tab id can never be actioned.
    private ContextMenuStrip? _tabContextMenu;

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

    private static bool IsAntigravityTab(string id)
    {
        string norm = AiConfigService.NormalizeCliId(id);
        return string.Equals(norm, "antigravity_cli", StringComparison.OrdinalIgnoreCase) ||
               CliManagerService.IsAntigravityProfileId(norm, out _);
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

        _claudeEngineComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Size = new Size(140, 28),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _claudeEngineComboBox.Items.AddRange(["OpenRouter (Free)", "Official Anthropic"]);
        _claudeEngineComboBox.DrawItem += ClaudeComboBox_DrawItem;
        _actionToolTip.SetToolTip(_claudeEngineComboBox, "Select Claude Engine: OpenRouter (Free) or Official Anthropic");

        _claudeModelComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.25F),
            Size = new Size(185, 28),
            Cursor = Cursors.Hand,
            Visible = false
        };
        var cachedModels = AiConfigService.Instance.GetCachedOpenRouterFreeModels();
        foreach (var m in cachedModels)
        {
            _claudeModelComboBox.Items.Add(m);
        }
        _claudeModelComboBox.DrawItem += ClaudeComboBox_DrawItem;
        _actionToolTip.SetToolTip(_claudeModelComboBox, "Select Free OpenRouter Model for Claude Code");

        toolsPanel.Controls.Add(_claudeEngineComboBox);
        toolsPanel.Controls.Add(_claudeModelComboBox);

        UpdateClaudeControlsUi();

        _ = Task.Run(async () =>
        {
            try
            {
                var liveModels = await AiConfigService.Instance.FetchOpenRouterFreeModelsAsync(forceRefresh: false);
                if (liveModels.Count > 0)
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(() =>
                        {
                            if (_claudeModelComboBox != null && !_isUpdatingClaudeControls)
                            {
                                var currentSelected = _claudeModelComboBox.SelectedItem as string ?? AiConfigService.Instance.GetClaudeSelectedModel();
                                _claudeModelComboBox.BeginUpdate();
                                _claudeModelComboBox.Items.Clear();
                                foreach (var m in liveModels)
                                {
                                    _claudeModelComboBox.Items.Add(m);
                                }
                                if (!string.IsNullOrWhiteSpace(currentSelected) && _claudeModelComboBox.Items.Contains(currentSelected))
                                {
                                    _claudeModelComboBox.SelectedItem = currentSelected;
                                }
                                else if (_claudeModelComboBox.Items.Count > 0)
                                {
                                    _claudeModelComboBox.SelectedIndex = 0;
                                }
                                _claudeModelComboBox.EndUpdate();
                            }
                        });
                    }
                }
            }
            catch { }
        });

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

        contentPanel.Padding = new Padding(0, 10, 0, 0);
        contentPanel.BackColor = ThemeManager.Colors.WindowBackground;

        topToolbar.Resize += (_, _) => LayoutToolbar();
        toolsPanel.Resize += (_, _) => LayoutToolbar();
        contentPanel.Resize += (_, _) => TriggerTerminalFit();
        // While the border is dragged Resize fires continuously and the
        // terminal can settle on a stale grid; refit once the drag ends.
        ResizeEnd += (_, _) => TriggerTerminalFit();
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

        using var brush = new SolidBrush(bg);
        e.Graphics.FillRectangle(brush, e.Bounds);

        var item = _profileComboBox.Items[e.Index] as ProfileItem;
        string text = item?.DisplayText ?? _profileComboBox.Items[e.Index]?.ToString() ?? "";

        var fg = item != null && item.IsRunning
            ? c.AccentGreen
            : (isSelected ? c.TextPrimary : c.TextSecondary);

        TextRenderer.DrawText(
            e.Graphics,
            text,
            e.Font ?? _profileComboBox.Font,
            new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height - 4),
            fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void ClaudeComboBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not ComboBox cb) return;

        var c = ThemeManager.Colors;
        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bg = isSelected ? c.ButtonHover : c.WellBackground;

        using var brush = new SolidBrush(bg);
        e.Graphics.FillRectangle(brush, e.Bounds);

        string text = cb.Items[e.Index]?.ToString() ?? "";
        var fg = isSelected ? c.TextPrimary : c.TextSecondary;

        TextRenderer.DrawText(
            e.Graphics,
            text,
            e.Font ?? cb.Font,
            new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height - 4),
            fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void UpdateClaudeControlsUi()
    {
        if (_claudeEngineComboBox == null || _claudeModelComboBox == null) return;

        try
        {
            _isUpdatingClaudeControls = true;

            string mode = AiConfigService.Instance.GetClaudeEngineMode();
            bool isOfficial = string.Equals(mode, "Official", StringComparison.OrdinalIgnoreCase);

            _claudeEngineComboBox.SelectedIndex = isOfficial ? 1 : 0;

            string selectedModel = AiConfigService.Instance.GetClaudeSelectedModel();
            int modelIdx = _claudeModelComboBox.Items.IndexOf(selectedModel);
            if (modelIdx >= 0)
            {
                _claudeModelComboBox.SelectedIndex = modelIdx;
            }
            else
            {
                _claudeModelComboBox.Items.Insert(0, selectedModel);
                _claudeModelComboBox.SelectedIndex = 0;
            }

            _claudeModelComboBox.Visible = !isOfficial && string.Equals(_activeTabId, "claude_code", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _isUpdatingClaudeControls = false;
        }
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
        bool isEmptyWorkspace = enabledWorkspaces.Count == 0;

        int curX = 8;
        if (!isEmptyWorkspace)
        {
            foreach (var ws in enabledWorkspaces)
            {
                string normId = AiConfigService.NormalizeCliId(ws.Id);
                if (string.Equals(normId, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
                {
                    var allProfiles = _cliManager?.ProfileService.GetProfiles() ?? [];
                    var runningProfiles = _cliManager?.GetRunningAntigravityProfiles() ?? [];
                    var activeProfile = _cliManager?.ProfileService.ActiveProfile ?? "Default";

                    var profileTabsToShow = new List<string>();
                    foreach (var prof in runningProfiles)
                    {
                        if (!profileTabsToShow.Contains(prof, StringComparer.OrdinalIgnoreCase))
                        {
                            profileTabsToShow.Add(prof);
                        }
                    }

                    if (!string.IsNullOrEmpty(activeProfile) && !profileTabsToShow.Contains(activeProfile, StringComparer.OrdinalIgnoreCase))
                    {
                        profileTabsToShow.Add(activeProfile);
                    }

                    foreach (var prof in allProfiles)
                    {
                        if (profileTabsToShow.Count >= 5) break;
                        if (!profileTabsToShow.Contains(prof, StringComparer.OrdinalIgnoreCase))
                        {
                            profileTabsToShow.Add(prof);
                        }
                    }

                    if (profileTabsToShow.Count == 0)
                    {
                        profileTabsToShow.Add("Default");
                    }

                    foreach (var profName in profileTabsToShow)
                    {
                        string profTabId = CliManagerService.FormatAntigravityProfileId(profName);
                        string displayName = $"agy: {profName}";
                        AddTabButton(profTabId, displayName, ref curX);
                    }
                    continue;
                }

                AddTabButton(normId, ws.Name, ref curX);
            }
        }

        _manageClisButton = new Button
        {
            Size = new Size(32, 34),
            MinimumSize = new Size(32, 34),
            MaximumSize = new Size(32, 34),
            Location = new Point(curX, 6),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Text = "",
            Margin = new Padding(2, 0, 4, 0),
            Padding = Padding.Empty
        };
        _manageClisButton.FlatAppearance.BorderSize = 1;
        _manageClisButton.Paint += DrawManageClisIcon;
        _manageClisButton.Click += (_, _) => OpenManageClisDialog();
        SetupButtonHover(_manageClisButton);
        _actionToolTip.SetToolTip(_manageClisButton, "Manage CLI Workspaces (Show/Hide, Add/Remove)");

        curX += _manageClisButton.Width + 8;
        tabsPanel.Controls.Add(_manageClisButton);

        tabsPanel.Tag = curX;
        tabsPanel.AutoScroll = true;
        tabsPanel.Width = curX;
        tabsPanel.ResumeLayout();

        if (isEmptyWorkspace)
        {
            _activeTabId = string.Empty;
        }
        else if (!_dynamicTabButtons.Any(b => string.Equals(b.Tag as string, _activeTabId, StringComparison.OrdinalIgnoreCase)))
        {
            if (_dynamicTabButtons.Count > 0 && _dynamicTabButtons[0].Tag is string firstId)
            {
                _activeTabId = firstId;
            }
        }

        SetTerminalContentVisible(!isEmptyWorkspace);
        ShowEmptyState(isEmptyWorkspace, workspaces);

        UpdateTabButtonsUi();
        UpdateToolbarUi();
        LayoutToolbar();
    }

    private void AddTabButton(string tabId, string displayName, ref int curX)
    {
        var btn = new Button
        {
            Tag = tabId,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = Padding.Empty,
            AutoEllipsis = true
        };
        btn.FlatAppearance.BorderSize = 1;

        int measuredWidth = TextRenderer.MeasureText($"● {displayName}", btn.Font).Width + 20;
        int targetWidth;
        if (displayName.StartsWith("agy: ", StringComparison.OrdinalIgnoreCase))
        {
            targetWidth = Math.Clamp(measuredWidth, 70, 110);
        }
        else
        {
            targetWidth = Math.Clamp(measuredWidth, 75, 130);
        }

        btn.Size = new Size(targetWidth, 34);
        btn.Location = new Point(curX, 6);
        curX += targetWidth + 4;

        btn.Click += (_, _) => SwitchTab(tabId);
        SetupTabHover(btn, tabId);

        btn.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowTabContextMenu(btn, tabId, btn.PointToScreen(e.Location));
            }
        };

        _dynamicTabButtons.Add(btn);
        tabsPanel.Controls.Add(btn);
    }

    private void ShowTabContextMenu(Button tabBtn, string tabId, Point screenLocation)
    {
        var c = ThemeManager.Colors;

        _tabContextMenu?.Dispose();
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new ShellColorTable()),
            BackColor = c.CardBackground,
            ForeColor = c.TextPrimary
        };
        _tabContextMenu = menu;

        bool isAgyProfile = CliManagerService.IsAntigravityProfileId(tabId, out var profileName);
        bool isBuiltIn = IsBuiltInCli(tabId);

        string displayName = isAgyProfile
            ? $"agy: {profileName}"
            : (tabBtn.Text ?? string.Empty).TrimStart('●', '○', '◐', '▲', ' ').Trim();

        menu.Items.Add(new ToolStripMenuItem(displayName)
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Open in External CMD (Real Terminal)", null, (_, _) =>
        {
            LaunchExternalCmdForTab(tabId);
        }));

        menu.Items.Add(new ToolStripSeparator());

        // Built-in CLIs have fixed ids, so only profiles and custom workspaces can be renamed.
        menu.Items.Add(new ToolStripMenuItem("Rename", null, (_, _) =>
        {
            HandleRenameTab(tabId);
        })
        {
            Enabled = isAgyProfile || !isBuiltIn
        });

        menu.Items.Add(new ToolStripMenuItem("Remove / Close Tab", null, async (_, _) =>
        {
            await HandleDeleteTabAsync(tabId);
        })
        {
            ForeColor = c.DangerRed
        });

        menu.Show(screenLocation);
    }

    /// <summary>
    /// Opens a real, interactive Windows console running the tab's CLI, with the same
    /// environment the embedded session would have been given.
    /// </summary>
    private void LaunchExternalCmdForTab(string tabId)
    {
        if (_cliManager == null) return;

        ExternalLaunchSpec? spec;
        try
        {
            spec = _cliManager.BuildExternalLaunchSpec(tabId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not prepare the external command:\n{ex.Message}", "Open in External CMD", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (spec == null || string.IsNullOrWhiteSpace(spec.Command))
        {
            MessageBox.Show(this, "This tab has no command configured.", "Open in External CMD", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string workDir = string.IsNullOrWhiteSpace(spec.WorkingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : spec.WorkingDirectory;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {spec.Command}",
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            foreach (var kvp in spec.EnvironmentVariables)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }

            // An inherited ANTHROPIC_API_KEY would shadow the injected token.
            if (psi.Environment.ContainsKey("ANTHROPIC_AUTH_TOKEN"))
            {
                psi.Environment.Remove("ANTHROPIC_API_KEY");
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to open an external console:\n{ex.Message}", "Open in External CMD", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HandleRenameTab(string tabId)
    {
        if (_cliManager == null) return;

        if (CliManagerService.IsAntigravityProfileId(tabId, out var profileName))
        {
            string? newName = PromptForName("Rename Antigravity Profile", "New profile name:", profileName);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, profileName, StringComparison.Ordinal)) return;

            if (!_cliManager.ProfileService.RenameProfile(profileName, newName))
            {
                MessageBox.Show(this, $"Could not rename \"{profileName}\". The name may already be in use.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RebuildTabs();
            SwitchTab(CliManagerService.FormatAntigravityProfileId(newName));
            return;
        }

        if (IsBuiltInCli(tabId))
        {
            MessageBox.Show(this, "Built-in CLIs cannot be renamed.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var ws = AiConfigService.Instance.GetCliWorkspaces()
            .FirstOrDefault(w => string.Equals(AiConfigService.NormalizeCliId(w.Id), tabId, StringComparison.OrdinalIgnoreCase));
        if (ws == null) return;

        string? newWorkspaceName = PromptForName("Rename Workspace", "New workspace name:", ws.Name);
        if (string.IsNullOrWhiteSpace(newWorkspaceName) || string.Equals(newWorkspaceName, ws.Name, StringComparison.Ordinal)) return;

        ws.Name = newWorkspaceName;
        AiConfigService.Instance.SaveCliWorkspace(ws);
        RebuildTabs();
    }

    private async Task HandleDeleteTabAsync(string tabId)
    {
        if (_cliManager == null) return;

        // An Antigravity tab exists only because the profile exists, so closing it removes the profile.
        if (CliManagerService.IsAntigravityProfileId(tabId, out var profileName))
        {
            var removeProfile = MessageBox.Show(
                this,
                $"Remove the Antigravity profile \"{profileName}\"?\n\nIts saved credentials and virtual home will be deleted. This cannot be undone.",
                "Remove Profile",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (removeProfile != DialogResult.Yes) return;

            try { await _cliManager.StopAsync(tabId); } catch { }

            if (!_cliManager.ProfileService.DeleteProfile(profileName))
            {
                MessageBox.Show(this, $"Could not remove profile \"{profileName}\".", "Remove Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _activeTabId = string.Empty;
            RebuildTabs();
            return;
        }

        // Workspace tab: stop it and hide it. Workspaces are re-added from Manage CLIs,
        // so closing a tab never deletes a user's configuration outright.
        string workspaceId = AiConfigService.NormalizeCliId(tabId);
        string label = _cliManager.GetInfo(tabId).ShortName;

        var closeTab = MessageBox.Show(
            this,
            $"Close the \"{label}\" tab?\n\nIt will be stopped and hidden from the workspace. Add it back any time from Manage CLIs.",
            "Remove / Close Tab",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (closeTab != DialogResult.Yes) return;

        try { await _cliManager.StopAsync(tabId); } catch { }

        var ws = AiConfigService.Instance.GetCliWorkspaces()
            .FirstOrDefault(w => string.Equals(AiConfigService.NormalizeCliId(w.Id), workspaceId, StringComparison.OrdinalIgnoreCase));
        if (ws != null)
        {
            AiConfigService.Instance.ToggleCliWorkspace(ws.Id, false);
        }

        _activeTabId = string.Empty;
        RebuildTabs();
    }

    private string? PromptForName(string title, string label, string initialValue)
    {
        var c = ThemeManager.Colors;

        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(400, 152),
            BackColor = c.WindowBackground,
            ForeColor = c.TextPrimary
        };

        var promptLabel = new Label
        {
            Text = label,
            Bounds = new Rectangle(16, 16, 368, 20),
            ForeColor = c.TextSecondary
        };

        var input = new TextBox
        {
            Text = initialValue,
            Bounds = new Rectangle(16, 42, 368, 26),
            BackColor = c.WellBackground,
            ForeColor = c.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        var okButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Bounds = new Rectangle(216, 92, 80, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = c.AccentGreen,
            ForeColor = c.AccentGreenText,
            Cursor = Cursors.Hand
        };
        okButton.FlatAppearance.BorderColor = c.AccentGreen;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(304, 92, 80, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = c.ButtonBg,
            ForeColor = c.ButtonText,
            Cursor = Cursors.Hand
        };
        cancelButton.FlatAppearance.BorderColor = c.ButtonBorder;

        dialog.Controls.Add(promptLabel);
        dialog.Controls.Add(input);
        dialog.Controls.Add(okButton);
        dialog.Controls.Add(cancelButton);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        dialog.Shown += (_, _) =>
        {
            ThemeManager.ApplyDwmTitleBarTheme(dialog.Handle);
            input.SelectAll();
            input.Focus();
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }

    private void OpenManageClisDialog()
    {
        using var form = new ManageClisForm();
        form.ConfigurationChanged += RebuildTabs;
        form.ShowDialog(this);
        RebuildTabs();
    }

    /// <summary>
    /// Shows or hides every hosted terminal WebView. Passing false leaves the content
    /// panel to the empty-state card.
    /// </summary>
    private void SetTerminalContentVisible(bool visible)
    {
        if (hermesWebView != null) hermesWebView.Visible = visible && string.Equals(_activeTabId, "hermes", StringComparison.OrdinalIgnoreCase);
        if (claudeWebView != null) claudeWebView.Visible = visible && string.Equals(_activeTabId, "claude_code", StringComparison.OrdinalIgnoreCase);
        if (antigravityWebView != null) antigravityWebView.Visible = visible && string.Equals(_activeTabId, "antigravity_cli", StringComparison.OrdinalIgnoreCase);

        foreach (var kvp in _customWebViews)
        {
            kvp.Value.Visible = visible && string.Equals(kvp.Key, _activeTabId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ShowEmptyState(bool show, List<CliWorkspaceConfig> workspaces)
    {
        if (!show)
        {
            if (_emptyStatePanel != null) _emptyStatePanel.Visible = false;
            return;
        }

        EnsureEmptyStatePanel();
        if (_emptyStatePanel == null) return;

        PopulateEmptyStateCombo(workspaces);
        _emptyStatePanel.Visible = true;
        _emptyStatePanel.BringToFront();
        LayoutEmptyState();
    }

    private void EnsureEmptyStatePanel()
    {
        if (_emptyStatePanel != null) return;

        _emptyStatePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeManager.Colors.WindowBackground,
            Visible = false
        };

        _emptyStateCard = new Panel
        {
            Size = new Size(520, 220),
            BackColor = ThemeManager.Colors.CardBackground
        };
        _emptyStateCard.Paint += (_, e) =>
        {
            if (_emptyStateCard == null) return;
            using var pen = new Pen(ThemeManager.Colors.CardBorder, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _emptyStateCard.Width - 1, _emptyStateCard.Height - 1);
        };

        _emptyTitleLabel = new Label
        {
            Text = "No CLI Terminals Active",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Bounds = new Rectangle(20, 26, 480, 28)
        };

        _emptySubtitleLabel = new Label
        {
            Text = "There are currently no background terminals configured in your workspace.",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Bounds = new Rectangle(20, 58, 480, 40)
        };

        _emptyAddCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F),
            Bounds = new Rectangle(20, 120, 340, 28)
        };
        _actionToolTip.SetToolTip(_emptyAddCombo, "Select a CLI workspace or shell to add");

        _emptyAddButton = new Button
        {
            Text = "+ Add to Workspace",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Bounds = new Rectangle(368, 119, 132, 30)
        };
        _emptyAddButton.FlatAppearance.BorderSize = 1;
        _emptyAddButton.Click += EmptyStateAddButton_Click;

        _emptyHintLabel = new Label
        {
            Text = "The selection is saved to your workspace and restored on next launch.",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Bounds = new Rectangle(20, 164, 480, 20)
        };

        _emptyStateCard.Controls.Add(_emptyTitleLabel);
        _emptyStateCard.Controls.Add(_emptySubtitleLabel);
        _emptyStateCard.Controls.Add(_emptyAddCombo);
        _emptyStateCard.Controls.Add(_emptyAddButton);
        _emptyStateCard.Controls.Add(_emptyHintLabel);

        _emptyStatePanel.Controls.Add(_emptyStateCard);
        _emptyStatePanel.Resize += (_, _) => LayoutEmptyState();

        contentPanel.Controls.Add(_emptyStatePanel);

        ApplyEmptyStateTheme();
    }

    private void LayoutEmptyState()
    {
        if (_emptyStatePanel == null || _emptyStateCard == null) return;

        int x = Math.Max(0, (_emptyStatePanel.ClientSize.Width - _emptyStateCard.Width) / 2);
        int y = Math.Max(0, (_emptyStatePanel.ClientSize.Height - _emptyStateCard.Height) / 2);
        _emptyStateCard.Location = new Point(x, y);
    }

    private void PopulateEmptyStateCombo(List<CliWorkspaceConfig> workspaces)
    {
        if (_emptyAddCombo == null) return;

        var previousId = (_emptyAddCombo.SelectedItem as EmptyStateOption)?.Id;

        _emptyAddCombo.BeginUpdate();
        _emptyAddCombo.Items.Clear();

        foreach (var ws in workspaces)
        {
            var label = string.IsNullOrWhiteSpace(ws.Name) ? ws.Id : ws.Name;
            _emptyAddCombo.Items.Add(new EmptyStateOption(ws.Id, label, false));
        }

        _emptyAddCombo.Items.Add(new EmptyStateOption("shell:cmd", "Command Prompt (embedded)", true));
        _emptyAddCombo.Items.Add(new EmptyStateOption("shell:powershell", "PowerShell (embedded)", true));

        if (_emptyAddCombo.Items.Count > 0)
        {
            int restore = previousId == null ? -1 : FindEmptyStateOption(previousId);
            _emptyAddCombo.SelectedIndex = restore >= 0 ? restore : 0;
        }

        _emptyAddCombo.EndUpdate();
    }

    private int FindEmptyStateOption(string id)
    {
        if (_emptyAddCombo == null) return -1;

        for (int i = 0; i < _emptyAddCombo.Items.Count; i++)
        {
            if (_emptyAddCombo.Items[i] is EmptyStateOption opt &&
                string.Equals(opt.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private void EmptyStateAddButton_Click(object? sender, EventArgs e)
    {
        if (_emptyAddCombo?.SelectedItem is not EmptyStateOption option) return;

        if (option.IsShell)
        {
            if (string.Equals(option.Id, "shell:powershell", StringComparison.OrdinalIgnoreCase))
            {
                OpenEmbeddedShell("powershell", "PowerShell", "powershell.exe");
            }
            else
            {
                OpenEmbeddedShell("cmd", "Command Prompt", "cmd.exe");
            }
            return;
        }

        // Persist first, then rebuild so the new tab exists before we switch to it.
        AiConfigService.Instance.ToggleCliWorkspace(option.Id, true);
        RebuildTabs();

        string workspaceId = AiConfigService.NormalizeCliId(option.Id);
        string targetTabId = string.Equals(workspaceId, "antigravity_cli", StringComparison.OrdinalIgnoreCase)
            ? CliManagerService.FormatAntigravityProfileId(_cliManager?.ProfileService.ActiveProfile ?? "Default")
            : workspaceId;

        SwitchTab(targetTabId);

        try
        {
            _cliManager?.Start(targetTabId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CliWebviewForm] Failed to start '{targetTabId}' after adding it: {ex.Message}");
        }
    }

    private void ApplyEmptyStateTheme()
    {
        if (_emptyStatePanel == null) return;

        var c = ThemeManager.Colors;

        _emptyStatePanel.BackColor = c.WindowBackground;
        if (_emptyStateCard != null) _emptyStateCard.BackColor = c.CardBackground;
        if (_emptyTitleLabel != null) _emptyTitleLabel.ForeColor = c.TextPrimary;
        if (_emptySubtitleLabel != null) _emptySubtitleLabel.ForeColor = c.TextMuted;
        if (_emptyHintLabel != null) _emptyHintLabel.ForeColor = c.TextMuted;

        if (_emptyAddCombo != null)
        {
            _emptyAddCombo.BackColor = c.WellBackground;
            _emptyAddCombo.ForeColor = c.TextPrimary;
        }

        if (_emptyAddButton != null)
        {
            _emptyAddButton.BackColor = c.AccentGreen;
            _emptyAddButton.ForeColor = c.AccentGreenText;
            _emptyAddButton.FlatAppearance.BorderColor = c.AccentGreen;
            _emptyAddButton.FlatAppearance.MouseOverBackColor = c.AccentGreenHover;
        }

        _emptyStateCard?.Invalidate();
    }

    private sealed class EmptyStateOption(string id, string label, bool isShell)
    {
        public string Id { get; } = id;
        public string Label { get; } = label;
        public bool IsShell { get; } = isShell;

        public override string ToString() => Label;
    }

    private void LayoutToolbar()
    {
        if (_isLayoutRunning || toolsPanel == null || topToolbar == null) return;

        try
        {
            _isLayoutRunning = true;

            bool isAntigravityActive = IsAntigravityTab(_activeTabId);
            bool isClaudeActive = string.Equals(_activeTabId, "claude_code", StringComparison.OrdinalIgnoreCase);
            bool isClaudeOpenRouter = isClaudeActive && string.Equals(AiConfigService.Instance.GetClaudeEngineMode(), "OpenRouter", StringComparison.OrdinalIgnoreCase);

            // The profile switcher only applies to Antigravity tabs; hide it elsewhere so the
            // toolbar does not crowd.
            if (_profileComboBox != null)
            {
                _profileComboBox.Visible = isAntigravityActive;
            }

            // 1. Compute Minimum Action Bar Width
            int actionControlsWidth = 10;
            if (openBrowserButton.Visible) actionControlsWidth += openBrowserButton.Width + 6;
            actionControlsWidth += clearTerminalButton.Width + 6;
            actionControlsWidth += restartCliButton.Width + 6;
            actionControlsWidth += toggleRunButton.Width + 6;
            if (_shellLauncherButton != null) actionControlsWidth += _shellLauncherButton.Width + 10;

            if (isClaudeActive)
            {
                actionControlsWidth += 110 + 8; // _claudeEngineComboBox (min 110px)
                if (isClaudeOpenRouter)
                {
                    actionControlsWidth += 130 + 6; // _claudeModelComboBox (min 130px)
                }
            }
            else if (isAntigravityActive)
            {
                if (_addProfileButton != null) actionControlsWidth += 52 + 8;
                // Profile switcher reserves its minimum width so the tabs panel clamps correctly.
                if (_profileComboBox != null) actionControlsWidth += 120 + 6;
            }

            // 2. Clamp tabsPanel.Width
            int preferredTabsWidth = tabsPanel.Tag is int w ? w : tabsPanel.Width;
            int maxAllowedTabsWidth = Math.Max(120, topToolbar.ClientSize.Width - actionControlsWidth - 40);
            tabsPanel.Width = Math.Min(preferredTabsWidth, maxAllowedTabsWidth);
            tabsPanel.AutoScroll = true;

            // 3. Responsive Claude Dropdowns & Controls Layout in toolsPanel
            int right = toolsPanel.ClientSize.Width - 10;

            // Action buttons laid out from right to left
            if (openBrowserButton.Visible)
            {
                openBrowserButton.Location = new Point(Math.Max(0, right - openBrowserButton.Width), 7);
                right = openBrowserButton.Left - 6;
            }

            clearTerminalButton.Location = new Point(Math.Max(0, right - clearTerminalButton.Width), 7);
            right = clearTerminalButton.Left - 6;

            restartCliButton.Location = new Point(Math.Max(0, right - restartCliButton.Width), 7);
            right = restartCliButton.Left - 6;

            toggleRunButton.Location = new Point(Math.Max(0, right - toggleRunButton.Width), 7);
            right = toggleRunButton.Left - 6;

            if (_shellLauncherButton != null)
            {
                _shellLauncherButton.Location = new Point(Math.Max(0, right - _shellLauncherButton.Width), 7);
                right = _shellLauncherButton.Left - 10;
            }

            // Antigravity-only toolbar controls: profile switcher + add-account button, laid out
            // right to left so the switcher sits immediately left of the add button.
            if (isAntigravityActive)
            {
                if (_addProfileButton != null)
                {
                    _addProfileButton.Visible = true;
                    _addProfileButton.Location = new Point(Math.Max(0, right - _addProfileButton.Width), 7);
                    right = _addProfileButton.Left - 8;
                }

                if (_profileComboBox != null)
                {
                    // Responsive: takes what is left, but never squashes below 120px or grows
                    // past 160px where it would push the action buttons off the bar.
                    int comboWidth = Math.Clamp(right - 12, 120, 160);
                    _profileComboBox.Width = comboWidth;
                    _profileComboBox.Location = new Point(Math.Max(0, right - comboWidth), 9);
                    right = _profileComboBox.Left - 8;
                }
            }
            else
            {
                if (_addProfileButton != null) _addProfileButton.Visible = false;
                if (_profileComboBox != null) _profileComboBox.Visible = false;
            }

            // Claude Engine & Model controls
            if (isClaudeActive && _claudeEngineComboBox != null)
            {
                _claudeEngineComboBox.Visible = true;
                if (isClaudeOpenRouter && _claudeModelComboBox != null)
                {
                    _claudeModelComboBox.Visible = true;

                    // Dynamically scale dropdown widths based on remaining room
                    int availableForClaude = Math.Max(0, right - 12);
                    int minRequired = 105 + 120 + 8; // 233px minimum sum

                    if (availableForClaude >= minRequired)
                    {
                        int extra = availableForClaude - minRequired;
                        _claudeEngineComboBox.Width = Math.Clamp(105 + (extra * 30 / 100), 105, 135);
                        _claudeModelComboBox.Width = Math.Clamp(120 + (extra * 70 / 100), 120, 190);
                    }
                    else
                    {
                        _claudeEngineComboBox.Width = 105;
                        _claudeModelComboBox.Width = Math.Max(95, availableForClaude - 105 - 8);
                    }

                    int modelX = Math.Max(0, right - _claudeModelComboBox.Width);
                    _claudeModelComboBox.Location = new Point(modelX, 9);
                    right = modelX - 6;

                    int engineX = Math.Max(0, right - _claudeEngineComboBox.Width);
                    _claudeEngineComboBox.Location = new Point(engineX, 9);
                    right = engineX - 8;
                }
                else
                {
                    if (_claudeModelComboBox != null) _claudeModelComboBox.Visible = false;
                    _claudeEngineComboBox.Width = Math.Clamp(Math.Min(135, right - 12), 105, 135);
                    int engineX = Math.Max(0, right - _claudeEngineComboBox.Width);
                    _claudeEngineComboBox.Location = new Point(engineX, 9);
                    right = engineX - 8;
                }
            }
            else
            {
                if (_claudeEngineComboBox != null) _claudeEngineComboBox.Visible = false;
                if (_claudeModelComboBox != null) _claudeModelComboBox.Visible = false;
            }

            // 4. Status label handling: hide when narrow (< 780px) or space constrained, ellipsis otherwise
            int leftMargin = 12;
            int availableForStatus = right - leftMargin - 6;
            if (topToolbar.ClientSize.Width >= 780 && availableForStatus >= 50)
            {
                activeStatusLabel.Visible = true;
                activeStatusLabel.Location = new Point(leftMargin, 14);
                activeStatusLabel.MaximumSize = new Size(availableForStatus, 24);
                activeStatusLabel.AutoEllipsis = true;
            }
            else
            {
                activeStatusLabel.Visible = false;
            }
        }
        finally
        {
            _isLayoutRunning = false;
        }
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
        contentPanel.Padding = new Padding(0, 10, 0, 0);

        ApplyEmptyStateTheme();

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

        if (_claudeEngineComboBox != null)
        {
            _claudeEngineComboBox.BackColor = c.WellBackground;
            _claudeEngineComboBox.ForeColor = c.TextPrimary;
            _claudeEngineComboBox.Invalidate();
        }

        if (_claudeModelComboBox != null)
        {
            _claudeModelComboBox.BackColor = c.WellBackground;
            _claudeModelComboBox.ForeColor = c.TextPrimary;
            _claudeModelComboBox.Invalidate();
        }

        UpdateTabButtonsUi();
        UpdateToolbarUi();
        topToolbar.Invalidate();
        toolsPanel.Invalidate();
        contentPanel.Invalidate();

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
            TriggerTerminalFit();
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
                    string targetTabId = CliManagerService.FormatAntigravityProfileId(item.ProfileName);
                    SwitchTab(targetTabId);

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
                            RebuildTabs();
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
                    string targetTabId = CliManagerService.FormatAntigravityProfileId(name);
                    RebuildTabs();
                    SwitchTab(targetTabId);
                    RefreshProfileCombo();
                }
            };
        }

        if (_claudeEngineComboBox != null)
        {
            _claudeEngineComboBox.SelectedIndexChanged += async (_, _) =>
            {
                if (_isUpdatingClaudeControls) return;
                string selected = _claudeEngineComboBox.SelectedIndex == 1 ? "Official" : "OpenRouter";
                string current = AiConfigService.Instance.GetClaudeEngineMode();
                if (!string.Equals(selected, current, StringComparison.OrdinalIgnoreCase))
                {
                    AiConfigService.Instance.SetClaudeEngineMode(selected);
                    UpdateClaudeControlsUi();
                    LayoutToolbar();

                    if (_cliManager != null)
                    {
                        var info = _cliManager.GetInfo("claude_code");
                        if (info.State == CliState.Running)
                        {
                            var res = MessageBox.Show(this,
                                $"Claude engine changed to {selected}. Restart Claude Code session to apply changes?",
                                "Restart Claude Session",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question);
                            if (res == DialogResult.Yes)
                            {
                                await _cliManager.RestartAsync("claude_code");
                            }
                        }
                    }
                }
            };
        }

        if (_claudeModelComboBox != null)
        {
            _claudeModelComboBox.SelectedIndexChanged += async (_, _) =>
            {
                if (_isUpdatingClaudeControls) return;
                if (_claudeModelComboBox.SelectedItem is string selectedModel && !string.IsNullOrWhiteSpace(selectedModel))
                {
                    string current = AiConfigService.Instance.GetClaudeSelectedModel();
                    if (!string.Equals(selectedModel, current, StringComparison.OrdinalIgnoreCase))
                    {
                        AiConfigService.Instance.SetClaudeSelectedModel(selectedModel);
                        if (string.Equals(AiConfigService.Instance.GetClaudeEngineMode(), "OpenRouter", StringComparison.OrdinalIgnoreCase))
                        {
                            CliManagerService.SyncClaudeOpenRouterSettingsJson(
                                AiConfigService.Instance.GetOpenRouterApiKey(),
                                selectedModel,
                                AiConfigService.Instance.GetClaudeCustomGatewayUrl());
                        }

                        if (_cliManager != null)
                        {
                            var info = _cliManager.GetInfo("claude_code");
                            if (info.State == CliState.Running)
                            {
                                var res = MessageBox.Show(this,
                                    $"Claude model changed to {selectedModel}. Restart Claude Code session to apply changes?",
                                    "Restart Claude Session",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question);
                                if (res == DialogResult.Yes)
                                {
                                    await _cliManager.RestartAsync("claude_code");
                                }
                            }
                        }
                    }
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
        if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            var activeProf = _cliManager?.ProfileService.ActiveProfile ?? "Default";
            id = CliManagerService.FormatAntigravityProfileId(activeProf);
        }
        else if (!CliManagerService.IsAntigravityProfileId(id, out _))
        {
            id = AiConfigService.NormalizeCliId(id);
        }

        _activeTabId = id;

        UpdateTabButtonsUi();
        UpdateToolbarUi();

        if (IsAntigravityTab(_activeTabId))
        {
            RefreshProfileCombo();
        }
        else if (string.Equals(_activeTabId, "claude_code", StringComparison.OrdinalIgnoreCase))
        {
            UpdateClaudeControlsUi();
        }

        LayoutToolbar();

        if (hermesWebView != null) hermesWebView.Visible = string.Equals(_activeTabId, "hermes", StringComparison.OrdinalIgnoreCase);
        if (claudeWebView != null) claudeWebView.Visible = string.Equals(_activeTabId, "claude_code", StringComparison.OrdinalIgnoreCase);
        if (antigravityWebView != null) antigravityWebView.Visible = string.Equals(_activeTabId, "antigravity_cli", StringComparison.OrdinalIgnoreCase);

        foreach (var kvp in _customWebViews)
        {
            kvp.Value.Visible = string.Equals(kvp.Key, _activeTabId, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(_activeTabId, "hermes", StringComparison.OrdinalIgnoreCase) && _hermesInitialized)
        {
            RefreshHermesNavigation();
        }
        else if (CliManagerService.IsAntigravityProfileId(_activeTabId, out _))
        {
            _ = EnsureProfileWebViewAsync(_activeTabId);
        }
        else if (!IsBuiltInCli(_activeTabId))
        {
            var ws = AiConfigService.Instance.GetCliWorkspaces().FirstOrDefault(w => string.Equals(AiConfigService.NormalizeCliId(w.Id), _activeTabId, StringComparison.OrdinalIgnoreCase));
            if (ws != null)
            {
                _ = EnsureCustomWebViewAsync(ws);
            }
        }

        TriggerTerminalFit();
    }

    private async Task EnsureProfileWebViewAsync(string profileId)
    {
        if (_customWebViews.ContainsKey(profileId)) return;

        if (_webViewEnv == null)
        {
            await _envInitTcs.Task;
        }

        if (_webViewEnv == null) return;

        try
        {
            var wv = CreateHostedWebView();
            wv.Visible = string.Equals(_activeTabId, profileId, StringComparison.OrdinalIgnoreCase);
            contentPanel.Controls.Add(wv);
            _customWebViews[profileId] = wv;

            await wv.EnsureCoreWebView2Async(_webViewEnv);
            ConfigureWebViewSettings(wv.CoreWebView2);
            wv.CoreWebView2.WebMessageReceived += (s, e) => TerminalWebView_WebMessageReceived(profileId, e);
            wv.CoreWebView2.NavigationCompleted += (_, _) => NotifyWebViewsTheme();

            if (File.Exists(_terminalHtmlPath))
            {
                wv.CoreWebView2.Navigate(new Uri(_terminalHtmlPath).AbsoluteUri);
            }

            NotifyWebViewsTheme();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CliWebviewForm] Failed to create WebView for profile '{profileId}': {ex.Message}");
        }
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
            var wv = CreateHostedWebView();
            wv.Visible = string.Equals(_activeTabId, ws.Id, StringComparison.OrdinalIgnoreCase);
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

    private static Microsoft.Web.WebView2.WinForms.WebView2 CreateHostedWebView()
    {
        // WebView2's default background is opaque white, which paints as a
        // white bar across the terminal while the control resizes with the form.
        return new Microsoft.Web.WebView2.WinForms.WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Transparent
        };
    }

    private async Task InitializeWebViewsAsync()
    {
        try
        {
            hermesWebView = CreateHostedWebView();
            claudeWebView = CreateHostedWebView();
            antigravityWebView = CreateHostedWebView();

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
        if (string.Equals(id, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            if (_cliManager != null)
            {
                var activeId = CliManagerService.FormatAntigravityProfileId(_cliManager.ProfileService.ActiveProfile);
                if (_customWebViews.TryGetValue(activeId, out var activeWv)) return activeWv;
            }
            return antigravityWebView;
        }
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
        if (CliManagerService.IsAntigravityProfileId(cliId, out _) || string.Equals(cliId, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            RebuildTabs();
            RefreshProfileCombo();
        }
        else
        {
            UpdateTabButtonsUi();
        }

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

            string title = CliManagerService.IsAntigravityProfileId(id, out var prof)
                ? $"agy: {prof}"
                : info.ShortName;

            btn.Text = $"{dotColor} {title}";

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

        // No workspace enabled: there is no active CLI, so hide its controls and say so.
        if (string.IsNullOrEmpty(_activeTabId))
        {
            activeStatusLabel.Text = "No terminals active";
            activeStatusLabel.ForeColor = c.TextMuted;

            toggleRunButton.Visible = false;
            restartCliButton.Visible = false;
            clearTerminalButton.Visible = false;
            openBrowserButton.Visible = false;

            LayoutToolbar();
            return;
        }

        toggleRunButton.Visible = true;
        restartCliButton.Visible = true;
        clearTerminalButton.Visible = true;

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

        if (string.Equals(_activeTabId, "claude_code", StringComparison.OrdinalIgnoreCase))
        {
            UpdateClaudeControlsUi();
        }

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
        if (CliManagerService.IsAntigravityProfileId(_activeTabId, out var prof))
        {
            return _cliManager?.GetPortForProfile(prof) ?? 0;
        }
        if (string.Equals(_activeTabId, "antigravity_cli", StringComparison.OrdinalIgnoreCase))
        {
            var activeProf = _cliManager?.ProfileService.ActiveProfile ?? string.Empty;
            return _cliManager?.GetPortForProfile(activeProf) ?? 0;
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
            string currentProfile = CliManagerService.IsAntigravityProfileId(_activeTabId, out var tabProf)
                ? tabProf
                : (_cliManager.ProfileService.ActiveProfile ?? "Default");

            ProfileItem? selectedItem = null;

            foreach (var p in profiles)
            {
                string? email = _cliManager.ProfileService.GetAccountEmail(p);
                bool isRunning = _cliManager.IsProfileRunning(p);
                int? port = isRunning ? _cliManager.GetPortForProfile(p) : null;

                string statusBadge = isRunning
                    ? (port.HasValue ? $"[Running : {port.Value}]" : "[Running]")
                    : "[Stopped]";

                string emailText = !string.IsNullOrWhiteSpace(email) ? $" ({email})" : "";
                string display = $"{p}{emailText} {statusBadge}";

                var item = new ProfileItem
                {
                    ProfileName = p,
                    DisplayText = display,
                    IsRunning = isRunning,
                    Port = port
                };
                _profileComboBox.Items.Add(item);

                if (string.Equals(p, currentProfile, StringComparison.OrdinalIgnoreCase))
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
        public bool IsRunning { get; set; }
        public int? Port { get; set; }
        public override string ToString() => DisplayText;
    }
}