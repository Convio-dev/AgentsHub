using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HermesHub.Services;
using Microsoft.Web.WebView2.Core;

namespace HermesHub;

public partial class CliWebviewForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    private readonly CliManagerService _cliManager;
    private CliType _activeTab = CliType.Hermes;
    private bool _hermesInitialized;
    private readonly string _terminalHtmlPath;
    private readonly string _hermesOfflineHtmlPath;

    public CliWebviewForm(CliManagerService cliManager, CliType initialTab = CliType.Hermes)
    {
        _cliManager = cliManager;
        _activeTab = initialTab;

        var baseDir = AppContext.BaseDirectory;
        _terminalHtmlPath = Path.Combine(baseDir, "assets", "web", "terminal.html");
        _hermesOfflineHtmlPath = Path.Combine(baseDir, "assets", "web", "hermes-offline.html");

        InitializeComponent();
        SetupStyles();
        HookEvents();

        _ = InitializeWebViewsAsync();
    }

    private void SetupStyles()
    {
        // Custom paint status dot in header
        titleDot.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(titleDot.BackColor);
            e.Graphics.FillEllipse(brush, 0, 0, titleDot.Width - 1, titleDot.Height - 1);
        };

        // Window drag handlers
        headerPanel.MouseDown += TitleBar_MouseDown;
        titleLabel.MouseDown += TitleBar_MouseDown;
        tabsPanel.MouseDown += TitleBar_MouseDown;
        toolsPanel.MouseDown += TitleBar_MouseDown;

        // Custom window buttons rendering
        SetupWindowButtons();

        // Style toolbar buttons
        SetupButtonHover(toggleRunButton, Color.FromArgb(16, 185, 129), Color.FromArgb(5, 150, 105));
        SetupButtonHover(restartCliButton, Color.FromArgb(39, 39, 42), Color.FromArgb(63, 63, 70));
        SetupButtonHover(clearTerminalButton, Color.FromArgb(39, 39, 42), Color.FromArgb(63, 63, 70));
        SetupButtonHover(openBrowserButton, Color.FromArgb(39, 39, 42), Color.FromArgb(63, 63, 70));

        restartCliButton.ForeColor = Color.FromArgb(244, 244, 245);
        clearTerminalButton.ForeColor = Color.FromArgb(244, 244, 245);
        openBrowserButton.ForeColor = Color.FromArgb(244, 244, 245);

        headerPanel.Resize += (_, _) => PositionHeaderControls();
        Resize += (_, _) => PositionHeaderControls();
        headerPanel.DoubleClick += (_, _) => ToggleMaximize();
        PositionHeaderControls();

        UpdateTabButtonsUi();
        UpdateToolbarUi();
    }

    private void PositionHeaderControls()
    {
        if (headerPanel == null || closeButton == null || maximizeButton == null || minimizeButton == null || toolsPanel == null)
        {
            return;
        }

        int headerWidth = headerPanel.ClientSize.Width;
        int topY = 8;

        closeButton.Location = new Point(headerWidth - 42, topY);
        maximizeButton.Location = new Point(headerWidth - 78, topY);
        minimizeButton.Location = new Point(headerWidth - 114, topY);

        closeButton.BringToFront();
        maximizeButton.BringToFront();
        minimizeButton.BringToFront();

        int toolsRight = headerWidth - 124;
        int toolsLeft = Math.Max(tabsPanel.Right + 8, toolsRight - toolsPanel.Width);
        toolsPanel.Location = new Point(toolsLeft, topY);
    }

    private void SetupWindowButtons()
    {
        minimizeButton.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool isHovered = minimizeButton.ClientRectangle.Contains(minimizeButton.PointToClient(Cursor.Position));
            Color strokeColor = isHovered ? Color.FromArgb(244, 244, 245) : Color.FromArgb(161, 161, 170);
            using var pen = new Pen(strokeColor, 2f);
            int midY = minimizeButton.Height / 2;
            int midX = minimizeButton.Width / 2;
            e.Graphics.DrawLine(pen, midX - 5, midY, midX + 5, midY);
        };

        maximizeButton.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool isHovered = maximizeButton.ClientRectangle.Contains(maximizeButton.PointToClient(Cursor.Position));
            Color strokeColor = isHovered ? Color.FromArgb(244, 244, 245) : Color.FromArgb(161, 161, 170);
            using var pen = new Pen(strokeColor, 1.5f);
            int midX = maximizeButton.Width / 2;
            int midY = maximizeButton.Height / 2;
            if (WindowState == FormWindowState.Maximized)
            {
                e.Graphics.DrawRectangle(pen, midX - 3, midY - 6, 8, 8);
                e.Graphics.DrawRectangle(pen, midX - 6, midY - 3, 8, 8);
            }
            else
            {
                e.Graphics.DrawRectangle(pen, midX - 5, midY - 5, 10, 10);
            }
        };

        closeButton.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool isHovered = closeButton.ClientRectangle.Contains(closeButton.PointToClient(Cursor.Position));
            Color strokeColor = isHovered ? Color.White : Color.FromArgb(161, 161, 170);
            using var pen = new Pen(strokeColor, 2f);
            int midX = closeButton.Width / 2;
            int midY = closeButton.Height / 2;
            int arm = 5;
            e.Graphics.DrawLine(pen, midX - arm, midY - arm, midX + arm, midY + arm);
            e.Graphics.DrawLine(pen, midX + arm, midY - arm, midX - arm, midY + arm);
        };

        minimizeButton.MouseEnter += (_, _) => minimizeButton.Invalidate();
        minimizeButton.MouseLeave += (_, _) => minimizeButton.Invalidate();
        maximizeButton.MouseEnter += (_, _) => maximizeButton.Invalidate();
        maximizeButton.MouseLeave += (_, _) => maximizeButton.Invalidate();
        closeButton.MouseEnter += (_, _) => closeButton.Invalidate();
        closeButton.MouseLeave += (_, _) => closeButton.Invalidate();

        SetupButtonHover(minimizeButton, Color.FromArgb(16, 16, 20), Color.FromArgb(39, 39, 42));
        SetupButtonHover(maximizeButton, Color.FromArgb(16, 16, 20), Color.FromArgb(39, 39, 42));
        SetupButtonHover(closeButton, Color.FromArgb(16, 16, 20), Color.FromArgb(220, 38, 38));

        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximizeButton.Click += (_, _) => ToggleMaximize();
        closeButton.Click += (_, _) => Hide();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        maximizeButton.Invalidate();
        PositionHeaderControls();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionHeaderControls();
        maximizeButton?.Invalidate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PositionHeaderControls();
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
        tabGeminiButton.Click += (_, _) => SwitchTab(CliType.GeminiCli);

        toggleRunButton.Click += async (_, _) => await HandleToggleRunClickAsync();
        restartCliButton.Click += async (_, _) => await _cliManager.RestartAsync(_activeTab);
        clearTerminalButton.Click += (_, _) => HandleClearTerminalClick();
        openBrowserButton.Click += (_, _) => HandleOpenBrowserClick();

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

    public void SwitchTab(CliType type)
    {
        _activeTab = type;
        UpdateTabButtonsUi();
        UpdateToolbarUi();

        hermesWebView.Visible = type == CliType.Hermes;
        claudeWebView.Visible = type == CliType.ClaudeCode;
        geminiWebView.Visible = type == CliType.GeminiCli;

        if (type == CliType.Hermes && _hermesInitialized)
        {
            RefreshHermesNavigation();
        }
    }

    private async Task InitializeWebViewsAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesHub", "WebView2Data");
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

            // Gemini CLI WebView
            await geminiWebView.EnsureCoreWebView2Async(env);
            ConfigureWebViewSettings(geminiWebView.CoreWebView2);
            geminiWebView.CoreWebView2.WebMessageReceived += (s, e) => TerminalWebView_WebMessageReceived(CliType.GeminiCli, e);
            if (File.Exists(_terminalHtmlPath))
            {
                geminiWebView.CoreWebView2.Navigate(new Uri(_terminalHtmlPath).AbsoluteUri);
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
        if (!_hermesInitialized)
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
                    _cliManager.Start(CliType.Hermes);
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
        var targetWv = type == CliType.ClaudeCode ? claudeWebView : geminiWebView;
        if (targetWv.CoreWebView2 == null)
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
        var targetWv = type == CliType.ClaudeCode ? claudeWebView : geminiWebView;
        if (targetWv.CoreWebView2 == null)
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
            var targetWv = type == CliType.ClaudeCode ? claudeWebView : geminiWebView;
            if (targetWv.CoreWebView2 != null)
            {
                var payload = new TerminalStatusPayload("status", state == CliState.Running, message);
                targetWv.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, CliJsonContext.Default.TerminalStatusPayload));
            }
        }
    }

    private void UpdateTabButtonsUi()
    {
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
                btn.BackColor = Color.FromArgb(39, 39, 42);
                btn.ForeColor = Color.FromArgb(250, 250, 250);
            }
            else
            {
                btn.BackColor = Color.FromArgb(16, 16, 20);
                btn.ForeColor = Color.FromArgb(161, 161, 170);
            }
        }

        FormatTab(tabHermesButton, CliType.Hermes, "Hermes");
        FormatTab(tabClaudeButton, CliType.ClaudeCode, "Claude Code");
        FormatTab(tabGeminiButton, CliType.GeminiCli, "Gemini CLI");
    }

    private void UpdateToolbarUi()
    {
        var info = _cliManager.GetInfo(_activeTab);
        activeStatusLabel.Text = $"{info.ShortName}: {info.StatusText}";

        if (info.State == CliState.Running)
        {
            toggleRunButton.Text = "Stop";
            toggleRunButton.BackColor = Color.FromArgb(239, 68, 68);
            toggleRunButton.ForeColor = Color.FromArgb(250, 250, 250);
            restartCliButton.Enabled = true;
            clearTerminalButton.Enabled = true;
        }
        else if (info.State == CliState.Starting)
        {
            toggleRunButton.Text = "Starting...";
            toggleRunButton.BackColor = Color.FromArgb(245, 158, 11);
            toggleRunButton.ForeColor = Color.FromArgb(9, 9, 11);
            restartCliButton.Enabled = false;
            clearTerminalButton.Enabled = true;
        }
        else
        {
            toggleRunButton.Text = "Start";
            toggleRunButton.BackColor = Color.FromArgb(16, 185, 129);
            toggleRunButton.ForeColor = Color.FromArgb(9, 9, 11);
            restartCliButton.Enabled = false;
            clearTerminalButton.Enabled = true;
        }

        openBrowserButton.Visible = _activeTab == CliType.Hermes;
    }

    private async Task HandleToggleRunClickAsync()
    {
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
        if (_activeTab == CliType.Hermes)
        {
            return;
        }

        _cliManager.ClearTerminalHistory(_activeTab);
        var targetWv = _activeTab == CliType.ClaudeCode ? claudeWebView : geminiWebView;
        if (targetWv.CoreWebView2 != null)
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
        try
        {
            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{_cliManager.HermesService.Port}/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _ = ReleaseCapture();
            _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    private static void SetupButtonHover(Button button, Color normalColor, Color hoverColor)
    {
        button.BackColor = normalColor;
        button.MouseEnter += (_, _) => button.BackColor = hoverColor;
        button.MouseLeave += (_, _) => button.BackColor = normalColor;
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
            // Cycle forward through tabs
            int next = ((int)_activeTab + 1) % 3;
            SwitchTab((CliType)next);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            // Cycle backward through tabs
            int prev = ((int)_activeTab + 2) % 3;
            SwitchTab((CliType)prev);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
