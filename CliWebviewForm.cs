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
    private CliType _activeTab = CliType.Hermes;
    private bool _hermesInitialized;
    private readonly string _terminalHtmlPath = string.Empty;
    private readonly string _hermesOfflineHtmlPath = string.Empty;
    private Color _toggleRunNormalColor = ThemeManager.Colors.AccentGreen;
    private Color _toggleRunHoverColor = ThemeManager.Colors.AccentGreenHover;

    // تم نقل تعريف عناصر WebView2 إلى هنا ليتم إنشاؤها وقت التشغيل بدلاً من الـ Designer
    private Microsoft.Web.WebView2.WinForms.WebView2 hermesWebView = null!;
    private Microsoft.Web.WebView2.WinForms.WebView2 claudeWebView = null!;
    private Microsoft.Web.WebView2.WinForms.WebView2 antigravityWebView = null!;

    private ComboBox? _profileComboBox;
    private Button? _addProfileButton;
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
            _activeTab = initialTab;

            var baseDir = AppContext.BaseDirectory;
            var uiTermPath = Path.Combine(baseDir, "ui", "terminal.html");
            _terminalHtmlPath = File.Exists(uiTermPath) ? uiTermPath : Path.Combine(baseDir, "assets", "web", "terminal.html");
            _hermesOfflineHtmlPath = Path.Combine(baseDir, "assets", "web", "hermes-offline.html");

            SetupStyles();
            HookEvents();

            _ = InitializeWebViewsAsync();
        }
    }

    private void SetupStyles()
    {
        _profileComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.75F),
            Size = new Size(160, 26),
            Cursor = Cursors.Hand,
            Visible = false
        };

        _addProfileButton = new Button
        {
            Text = "+ Add",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Size = new Size(52, 30),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _addProfileButton.FlatAppearance.BorderSize = 1;

        toolsPanel.Controls.Add(_profileComboBox);
        toolsPanel.Controls.Add(_addProfileButton);

        SetupButtonHover(_addProfileButton, ThemeManager.Colors.ButtonBg, ThemeManager.Colors.ButtonHover);

        ApplyTheme();

        toggleRunButton.MouseEnter += (_, _) =>
        {
            if (toggleRunButton.Enabled) toggleRunButton.BackColor = _toggleRunHoverColor;
        };
        toggleRunButton.MouseLeave += (_, _) =>
        {
            if (toggleRunButton.Enabled) toggleRunButton.BackColor = _toggleRunNormalColor;
        };

        SetupButtonHover(restartCliButton, ThemeManager.Colors.ButtonBg, ThemeManager.Colors.ButtonHover);
        SetupButtonHover(clearTerminalButton, ThemeManager.Colors.ButtonBg, ThemeManager.Colors.ButtonHover);
        SetupButtonHover(openBrowserButton, ThemeManager.Colors.ButtonBg, ThemeManager.Colors.ButtonHover);

        topToolbar.Resize += (_, _) => LayoutToolbar();
        toolsPanel.Resize += (_, _) => LayoutToolbar();
        topToolbar.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeManager.Colors.CardBorder, 1);
            e.Graphics.DrawLine(pen, 0, topToolbar.Height - 1, topToolbar.Width, topToolbar.Height - 1);
        };

        UpdateTabButtonsUi();
        UpdateToolbarUi();
    }

    private void LayoutToolbar()
    {
        if (toolsPanel == null || topToolbar == null) return;

        toolsPanel.Width = Math.Max(580, topToolbar.ClientSize.Width - tabsPanel.Width);

        int right = toolsPanel.ClientSize.Width - 10;

        openBrowserButton.Location = new Point(right - openBrowserButton.Width, 7);
        right = openBrowserButton.Left - 6;

        clearTerminalButton.Location = new Point(right - clearTerminalButton.Width, 7);
        right = clearTerminalButton.Left - 6;

        restartCliButton.Location = new Point(right - restartCliButton.Width, 7);
        right = restartCliButton.Left - 6;

        toggleRunButton.Location = new Point(right - toggleRunButton.Width, 7);
        right = toggleRunButton.Left - 10;

        if (_activeTab == CliType.AntigravityCli && _profileComboBox != null && _addProfileButton != null)
        {
            _profileComboBox.Visible = true;
            _addProfileButton.Visible = true;

            _addProfileButton.Location = new Point(right - _addProfileButton.Width, 8);
            right = _addProfileButton.Left - 6;

            _profileComboBox.Location = new Point(right - _profileComboBox.Width, 9);
            right = _profileComboBox.Left - 10;
        }
        else
        {
            if (_profileComboBox != null) _profileComboBox.Visible = false;
            if (_addProfileButton != null) _addProfileButton.Visible = false;
        }

        int maxStatusWidth = Math.Max(100, right - 10);
        activeStatusLabel.MaximumSize = new Size(maxStatusWidth, 24);
        activeStatusLabel.AutoEllipsis = true;
        activeStatusLabel.Location = new Point(Math.Max(10, right - activeStatusLabel.PreferredWidth), 14);
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
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!IsInDesignMode)
        {
            ThemeManager.ApplyDwmTitleBarTheme(Handle);
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
        tabHermesButton.Click += (_, _) => SwitchTab(CliType.Hermes);
        tabClaudeButton.Click += (_, _) => SwitchTab(CliType.ClaudeCode);
        tabAntigravityButton.Click += (_, _) => SwitchTab(CliType.AntigravityCli);

        toggleRunButton.Click += async (_, _) => await HandleToggleRunClickAsync();
        restartCliButton.Click += async (_, _) =>
        {
            if (_cliManager != null)
            {
                await _cliManager.RestartAsync(_activeTab);
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

            _cliManager.StatusChanged += (type, state, msg) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => OnCliStatusChanged(type, state, msg));
                }
                else
                {
                    OnCliStatusChanged(type, state, msg);
                }
            };

            _cliManager.TerminalDataReceived += (type, text) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => ForwardTerminalData(type, text));
                }
                else
                {
                    ForwardTerminalData(type, text);
                }
            };
        }

        RefreshProfileCombo();
    }

    public void SwitchTab(CliType type)
    {
        _activeTab = type;
        UpdateTabButtonsUi();
        UpdateToolbarUi();
        LayoutToolbar();

        if (type == CliType.AntigravityCli)
        {
            RefreshProfileCombo();
        }

        if (hermesWebView != null) hermesWebView.Visible = type == CliType.Hermes;
        if (claudeWebView != null) claudeWebView.Visible = type == CliType.ClaudeCode;
        if (antigravityWebView != null) antigravityWebView.Visible = type == CliType.AntigravityCli;

        if (type == CliType.Hermes && _hermesInitialized)
        {
            RefreshHermesNavigation();
        }
    }

    private async Task InitializeWebViewsAsync()
    {
        try
        {
            // بناء الـ WebViews برمجياً وإضافتها لـ contentPanel في الـ Runtime
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
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            // Hermes WebView
            await hermesWebView.EnsureCoreWebView2Async(env);
            ConfigureWebViewSettings(hermesWebView.CoreWebView2);
            hermesWebView.CoreWebView2.WebMessageReceived += HermesWebView_WebMessageReceived;
            _hermesInitialized = true;
            RefreshHermesNavigation();

            // Claude Code WebView
            await claudeWebView.EnsureCoreWebView2Async(env);
            ConfigureWebViewSettings(claudeWebView.CoreWebView2);
            claudeWebView.CoreWebView2.WebMessageReceived += (s, e) => TerminalWebView_WebMessageReceived(CliType.ClaudeCode, e);
            if (File.Exists(_terminalHtmlPath))
            {
                claudeWebView.CoreWebView2.Navigate(new Uri(_terminalHtmlPath).AbsoluteUri);
            }

            // Antigravity CLI WebView
            await antigravityWebView.EnsureCoreWebView2Async(env);
            ConfigureWebViewSettings(antigravityWebView.CoreWebView2);
            antigravityWebView.CoreWebView2.WebMessageReceived += (s, e) => TerminalWebView_WebMessageReceived(CliType.AntigravityCli, e);
            if (File.Exists(_terminalHtmlPath))
            {
                antigravityWebView.CoreWebView2.Navigate(new Uri(_terminalHtmlPath).AbsoluteUri);
            }

            SwitchTab(_activeTab);
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

        var hermesInfo = _cliManager.GetInfo(CliType.Hermes);
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
                    _cliManager?.Start(CliType.Hermes);
                }
            }
        }
        catch
        {
            // Suppress invalid JSON parsing errors
        }
    }

    private void TerminalWebView_WebMessageReceived(CliType cliType, CoreWebView2WebMessageReceivedEventArgs e)
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
                        SendTerminalInit(cliType);
                        break;

                    case "input":
                        if (doc.RootElement.TryGetProperty("data", out var dataEl))
                        {
                            var input = dataEl.GetString();
                            if (input != null)
                            {
                                _cliManager.SendInput(cliType, input);
                            }
                        }
                        break;

                    case "resize":
                        if (doc.RootElement.TryGetProperty("cols", out var colsEl) &&
                            doc.RootElement.TryGetProperty("rows", out var rowsEl))
                        {
                            short cols = colsEl.GetInt16();
                            short rows = rowsEl.GetInt16();
                            _cliManager.Resize(cliType, cols, rows);
                        }
                        break;

                    case "start":
                        _cliManager.Start(cliType);
                        break;
                }
            }
        }
        catch
        {
            // Suppress invalid JSON parsing errors
        }
    }

    private void SendTerminalInit(CliType type)
    {
        if (_cliManager == null) return;

        var targetWv = type == CliType.ClaudeCode ? claudeWebView : antigravityWebView;
        if (targetWv?.CoreWebView2 == null)
        {
            return;
        }

        var info = _cliManager.GetInfo(type);
        var history = _cliManager.GetTerminalHistory(type);

        var payload = new TerminalInitPayload("init", info.ShortName, info.State == CliState.Running, info.StatusText, history);
        targetWv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, CliJsonContext.Default.TerminalInitPayload));
    }

    private void ForwardTerminalData(CliType type, string text)
    {
        var targetWv = type == CliType.ClaudeCode ? claudeWebView : antigravityWebView;
        if (targetWv?.CoreWebView2 == null)
        {
            return;
        }

        var payload = new TerminalOutputPayload("output", text);
        targetWv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, CliJsonContext.Default.TerminalOutputPayload));
    }

    private void OnCliStatusChanged(CliType type, CliState state, string message)
    {
        UpdateTabButtonsUi();

        if (type == _activeTab)
        {
            UpdateToolbarUi();
        }

        if (type == CliType.Hermes)
        {
            RefreshHermesNavigation();
        }
        else
        {
            var targetWv = type == CliType.ClaudeCode ? claudeWebView : antigravityWebView;
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

        void FormatTab(Button btn, CliType type, string name)
        {
            var info = _cliManager.GetInfo(type);
            var isSelected = _activeTab == type;

            var dotColor = info.State switch
            {
                CliState.Running => "●",
                CliState.Starting => "◐",
                CliState.Error => "▲",
                _ => "○"
            };

            btn.Text = $"{dotColor} {name}";

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

        FormatTab(tabHermesButton, CliType.Hermes, "Hermes");
        FormatTab(tabClaudeButton, CliType.ClaudeCode, "Claude Code");
        FormatTab(tabAntigravityButton, CliType.AntigravityCli, "Antigravity CLI");
    }

    private void UpdateToolbarUi()
    {
        if (_cliManager == null) return;
        var c = ThemeManager.Colors;
        var info = _cliManager.GetInfo(_activeTab);
        activeStatusLabel.Text = $"{info.ShortName}: {info.StatusText}";

        if (info.State == CliState.Running)
        {
            activeStatusLabel.ForeColor = c.AccentGreen;
            toggleRunButton.Text = "Stop";
            _toggleRunNormalColor = c.DangerRed;
            _toggleRunHoverColor = Color.FromArgb(220, 38, 38);
            toggleRunButton.BackColor = _toggleRunNormalColor;
            toggleRunButton.ForeColor = Color.White;
            toggleRunButton.FlatAppearance.BorderColor = c.DangerRed;
            restartCliButton.Enabled = true;
            clearTerminalButton.Enabled = true;
        }
        else if (info.State == CliState.Starting)
        {
            activeStatusLabel.ForeColor = c.WarningAmber;
            toggleRunButton.Text = "Starting...";
            _toggleRunNormalColor = c.WarningAmber;
            _toggleRunHoverColor = Color.FromArgb(217, 119, 6);
            toggleRunButton.BackColor = _toggleRunNormalColor;
            toggleRunButton.ForeColor = Color.Black;
            toggleRunButton.FlatAppearance.BorderColor = c.WarningAmber;
            restartCliButton.Enabled = false;
            clearTerminalButton.Enabled = true;
        }
        else
        {
            activeStatusLabel.ForeColor = c.TextMuted;
            toggleRunButton.Text = "Start";
            _toggleRunNormalColor = c.AccentGreen;
            _toggleRunHoverColor = c.AccentGreenHover;
            toggleRunButton.BackColor = _toggleRunNormalColor;
            toggleRunButton.ForeColor = c.AccentGreenText;
            toggleRunButton.FlatAppearance.BorderColor = c.AccentGreen;
            restartCliButton.Enabled = false;
            clearTerminalButton.Enabled = true;
        }

        openBrowserButton.Visible = true;
        openBrowserButton.Text = _activeTab switch
        {
            CliType.Hermes => $"Browser :{_cliManager.HermesService.Port}",
            CliType.ClaudeCode => "Browser :9229",
            CliType.AntigravityCli => "Browser :9339",
            _ => "Browser"
        };

        LayoutToolbar();
    }

    private async Task HandleToggleRunClickAsync()
    {
        if (_cliManager == null) return;

        var info = _cliManager.GetInfo(_activeTab);
        if (info.State == CliState.Running || info.State == CliState.Starting)
        {
            await _cliManager.StopAsync(_activeTab);
        }
        else
        {
            _cliManager.Start(_activeTab);
        }
    }

    private void HandleClearTerminalClick()
    {
        if (_cliManager == null || _activeTab == CliType.Hermes)
        {
            return;
        }

        _cliManager.ClearTerminalHistory(_activeTab);
        var targetWv = _activeTab == CliType.ClaudeCode ? claudeWebView : antigravityWebView;
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
            int port = _activeTab switch
            {
                CliType.Hermes => _cliManager.HermesService.Port,
                CliType.ClaudeCode => 9229,
                CliType.AntigravityCli => 9339,
                _ => 9119
            };

            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void SetupButtonHover(Button button, Color normalColor, Color hoverColor)
    {
        button.MouseEnter += (_, _) =>
        {
            if (button.Enabled) button.BackColor = hoverColor;
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Enabled) button.BackColor = normalColor;
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
            int next = ((int)_activeTab + 1) % 3;
            SwitchTab((CliType)next);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            int prev = ((int)_activeTab + 2) % 3;
            SwitchTab((CliType)prev);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
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