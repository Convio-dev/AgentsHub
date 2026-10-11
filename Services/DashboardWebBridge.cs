#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HermesHub.Helpers;
using Microsoft.Web.WebView2.Core;

namespace HermesHub.Services;

public sealed class DashboardWebBridge : IDisposable
{
    private readonly Form _mainForm;
    private readonly CliManagerService _cliManager;
    private readonly LanProxyService? _lanProxyService;
    private CoreWebView2? _coreWebView;
    private CliWebviewForm? _cliWebviewForm;

    // Debounced terminal output buffers per CLI type (Constraint 3)
    private readonly object _streamLock = new();
    private readonly Dictionary<CliType, System.Text.StringBuilder> _terminalBuffers = new()
    {
        [CliType.Hermes] = new System.Text.StringBuilder(),
        [CliType.ClaudeCode] = new System.Text.StringBuilder(),
        [CliType.AntigravityCli] = new System.Text.StringBuilder()
    };
    private readonly System.Windows.Forms.Timer _streamFlushTimer;
    private readonly System.Windows.Forms.Timer _metricsTimer;

    private readonly PerformanceCounter? _cpuCounter;
    private CancellationTokenSource? _promptEnhanceCts;

    public DashboardWebBridge(Form mainForm, CliManagerService cliManager, LanProxyService? lanProxyService)
    {
        _mainForm = mainForm;
        _cliManager = cliManager;
        _lanProxyService = lanProxyService;

        // Initialize debounced terminal stream timer (flushes every 20ms)
        _streamFlushTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _streamFlushTimer.Tick += OnStreamFlushTimerTick;
        _streamFlushTimer.Start();

        // Metrics timer (CPU / RAM updates every 2.5s)
        _metricsTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _metricsTimer.Tick += OnMetricsTimerTick;
        _metricsTimer.Start();

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _cpuCounter.NextValue(); // First value is always 0
        }
        catch
        {
            // Performance counters may require elevated permissions on some systems
        }

        HookBackendEvents();
    }

    public void AttachWebView(CoreWebView2 coreWebView)
    {
        _coreWebView = coreWebView;
        _coreWebView.WebMessageReceived += OnWebMessageReceived;
    }

    private void HookBackendEvents()
    {
        _cliManager.StatusChanged += (type, state, msg) =>
        {
            PostEventToUi("cli_status_changed", new
            {
                cli = type.ToString(),
                state = state.ToString(),
                message = msg,
                port = GetCliPort(type),
                pid = _cliManager.GetInfo(type).ProcessId
            });
        };

        _cliManager.TerminalDataReceived += (type, data) =>
        {
            // Append incoming stream data into debounced buffer
            lock (_streamLock)
            {
                if (_terminalBuffers.TryGetValue(type, out var sb))
                {
                    sb.Append(data);
                }
            }
        };

        _cliManager.ProfileService.ActiveProfileChanged += (activeProfile) =>
        {
            PostEventToUi("profile_changed", new
            {
                activeProfile,
                profiles = GetProfilesWithEmail()
            });
        };

        _cliManager.ProfileService.ProfileSaved += (profile) =>
        {
            PostEventToUi("profile_saved", new
            {
                profile,
                profiles = GetProfilesWithEmail()
            });
        };

        AiConfigService.Instance.ConfigChanged += () =>
        {
            PostEventToUi("ai_config_changed", GetAiConfigDto());
        };

        if (_lanProxyService != null)
        {
            _lanProxyService.StateChanged += (isRunning) =>
            {
                var lanIp = NetworkHelper.GetLocalIPv4();
                var rPorts = GetRunningWebPorts(lanIp, _cliManager.HermesService.Port, _lanProxyService.ListenPort);
                PostEventToUi("lan_proxy_state_changed", new
                {
                    lanProxyEnabled = isRunning,
                    lanProxyRunning = isRunning,
                    runningPorts = rPorts
                });
            };
        }
    }

    private void OnStreamFlushTimerTick(object? sender, EventArgs e)
    {
        if (_coreWebView == null) return;

        lock (_streamLock)
        {
            foreach (var (cliType, sb) in _terminalBuffers)
            {
                if (sb.Length > 0)
                {
                    var chunk = sb.ToString();
                    sb.Clear();

                    PostEventToUi("terminal_data", new
                    {
                        cli = cliType.ToString(),
                        data = chunk
                    });
                }
            }
        }
    }

    private void OnMetricsTimerTick(object? sender, EventArgs e)
    {
        if (_coreWebView == null) return;

        float cpuPercent = 0f;
        try
        {
            if (_cpuCounter != null)
            {
                cpuPercent = _cpuCounter.NextValue();
            }
        }
        catch { }

        long totalRamMb = 16384;
        long usedRamMb = 4096;
        try
        {
            using var proc = Process.GetCurrentProcess();
            usedRamMb = proc.WorkingSet64 / (1024 * 1024);
        }
        catch { }

        PostEventToUi("hardware_metrics", new
        {
            cpu = Math.Round(cpuPercent, 1),
            ramMb = usedRamMb,
            totalRamMb = totalRamMb
        });
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var rawJson = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var action = root.TryGetProperty("action", out var actElem) ? actElem.GetString() : string.Empty;
            var requestId = root.TryGetProperty("requestId", out var reqElem) ? reqElem.GetString() : null;

            await HandleActionAsync(action, root, requestId);
        }
        catch (Exception ex)
        {
            SendResponse(null, false, ex.Message);
        }
    }

    private async Task HandleActionAsync(string? action, JsonElement data, string? requestId)
    {
        switch (action)
        {
            case "get_initial_state":
                SendResponse(requestId, true, GetInitialStateDto());
                break;

            case "toggle_lan_proxy":
                if (_lanProxyService != null)
                {
                    bool enable;
                    if (data.TryGetProperty("enabled", out var enProp))
                    {
                        enable = enProp.GetBoolean();
                    }
                    else
                    {
                        enable = !_lanProxyService.IsRunning;
                    }

                    if (enable)
                    {
                        var savedEnginePort = StartupHelper.GetIntPreference("EnginePort", HermesProcessService.DefaultHermesPort);
                        var savedLanPort = StartupHelper.GetIntPreference("LanPort", 9191);
                        _lanProxyService.Start(savedLanPort, savedEnginePort);
                        StartupHelper.SetPreference("LanProxyEnabled", true);
                    }
                    else
                    {
                        _lanProxyService.Stop();
                        StartupHelper.SetPreference("LanProxyEnabled", false);
                    }

                    var lanIp = NetworkHelper.GetLocalIPv4();
                    var isRunning = _lanProxyService.IsRunning;
                    var rPorts = GetRunningWebPorts(lanIp, _cliManager.HermesService.Port, _lanProxyService.ListenPort);

                    SendResponse(requestId, true, new
                    {
                        lanProxyEnabled = isRunning,
                        lanProxyRunning = isRunning,
                        runningPorts = rPorts
                    });
                }
                else
                {
                    SendResponse(requestId, false, "LAN Proxy service not initialized");
                }
                break;

            case "toggle_cli":
                if (data.TryGetProperty("cli", out var cliProp) && Enum.TryParse<CliType>(cliProp.GetString(), out var cliType))
                {
                    var info = _cliManager.GetInfo(cliType);
                    if (info.State == CliState.Running)
                    {
                        await _cliManager.StopAsync(cliType);
                    }
                    else
                    {
                        _cliManager.Start(cliType);
                    }
                    SendResponse(requestId, true, new { state = _cliManager.GetInfo(cliType).State.ToString() });
                }
                break;

            case "restart_cli":
                if (data.TryGetProperty("cli", out var rCli) && Enum.TryParse<CliType>(rCli.GetString(), out var rType))
                {
                    await _cliManager.RestartAsync(rType);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "send_terminal_input":
                if (data.TryGetProperty("cli", out var inCli) && Enum.TryParse<CliType>(inCli.GetString(), out var inType) &&
                    data.TryGetProperty("input", out var inData))
                {
                    _cliManager.SendInput(inType, inData.GetString() ?? string.Empty);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "resize_terminal":
                if (data.TryGetProperty("cli", out var szCli) && Enum.TryParse<CliType>(szCli.GetString(), out var szType) &&
                    data.TryGetProperty("cols", out var cols) && data.TryGetProperty("rows", out var rows))
                {
                    _cliManager.Resize(szType, (short)cols.GetInt32(), (short)rows.GetInt32());
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "switch_antigravity_profile":
                if (data.TryGetProperty("profile", out var profProp))
                {
                    var pName = profProp.GetString();
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        await _cliManager.SwitchAntigravityProfileAsync(pName);
                        SendResponse(requestId, true, new { activeProfile = _cliManager.ProfileService.ActiveProfile });
                    }
                }
                break;

            case "create_antigravity_profile":
                if (data.TryGetProperty("profileName", out var newNameProp))
                {
                    var newName = newNameProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        var profiles = _cliManager.ProfileService.GetProfiles();
                        if (profiles.Contains(newName, StringComparer.OrdinalIgnoreCase))
                        {
                            SendResponse(requestId, false, $"A profile named '{newName}' already exists.");
                            return;
                        }
                        await _cliManager.CreateAndSwitchNewProfileAsync(newName);
                        SendResponse(requestId, true, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
                    }
                }
                break;

            case "rename_antigravity_profile":
                if (data.TryGetProperty("oldName", out var oldProp) && data.TryGetProperty("newName", out var newProp))
                {
                    var oldName = oldProp.GetString()?.Trim();
                    var newName = newProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(oldName) && !string.IsNullOrWhiteSpace(newName))
                    {
                        var ok = _cliManager.ProfileService.RenameProfile(oldName, newName);
                        SendResponse(requestId, ok, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
                    }
                }
                break;

            case "delete_antigravity_profile":
                if (data.TryGetProperty("profileName", out var delProp))
                {
                    var pName = delProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        var ok = _cliManager.ProfileService.DeleteProfile(pName);
                        SendResponse(requestId, ok, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
                    }
                }
                break;

            case "open_profile_folder":
                if (data.TryGetProperty("profileName", out var folderProp))
                {
                    var pName = folderProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        var profileDir = Path.Combine(AntigravityProfileService.ProfilesBaseDir, pName);
                        if (Directory.Exists(profileDir))
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", profileDir) { UseShellExecute = true });
                            SendResponse(requestId, true, null);
                        }
                        else
                        {
                            SendResponse(requestId, false, "Profile folder not found.");
                        }
                    }
                }
                break;

            case "enhance_prompt":
                await HandleEnhancePromptAsync(data, requestId);
                break;

            case "fetch_models":
                await HandleFetchModelsAsync(data, requestId);
                break;

            case "save_ai_config":
                if (data.TryGetProperty("config", out var cfgProp))
                {
                    var deserialized = JsonSerializer.Deserialize<AiConfig>(cfgProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (deserialized != null)
                    {
                        AiConfigService.Instance.SaveConfig(deserialized);
                        SendResponse(requestId, true, new { success = true });
                    }
                }
                break;

            case "launch_tool":
                if (data.TryGetProperty("toolName", out var toolProp))
                {
                    var tool = toolProp.GetString() ?? string.Empty;
                    LaunchToolApp(tool);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "browse_tool_target":
                if (data.TryGetProperty("toolName", out var brToolProp))
                {
                    var tool = brToolProp.GetString() ?? string.Empty;
                    string? selectedPath = null;
                    _mainForm.Invoke(() =>
                    {
                        using var ofd = new OpenFileDialog
                        {
                            Title = $"Select Target Executable for {tool}",
                            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
                            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                        };
                        if (ofd.ShowDialog(_mainForm) == DialogResult.OK)
                        {
                            selectedPath = ofd.FileName;
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        var cfg = AiConfigService.Instance.CurrentConfig;
                        cfg.ToolTargets[tool] = selectedPath;
                        AiConfigService.Instance.SaveConfig(cfg);
                        SendResponse(requestId, true, new { toolName = tool, path = selectedPath });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Selection cancelled");
                    }
                }
                break;

            case "reset_tool_target":
                if (data.TryGetProperty("toolName", out var rsToolProp))
                {
                    var tool = rsToolProp.GetString() ?? string.Empty;
                    var defaults = AiConfigService.Instance.GetDefaultToolTargets();
                    var defPath = defaults.TryGetValue(tool, out var p) ? p : string.Empty;
                    var cfg = AiConfigService.Instance.CurrentConfig;
                    cfg.ToolTargets[tool] = defPath;
                    AiConfigService.Instance.SaveConfig(cfg);
                    SendResponse(requestId, true, new { toolName = tool, path = defPath });
                }
                break;

            case "open_standalone_terminals":
                _mainForm.Invoke(() =>
                {
                    if (_cliWebviewForm == null || _cliWebviewForm.IsDisposed)
                    {
                        _cliWebviewForm = new CliWebviewForm(_cliManager);
                    }
                    _cliWebviewForm.ApplyTheme();
                    _cliWebviewForm.Show();
                    _cliWebviewForm.BringToFront();
                    _cliWebviewForm.Activate();
                });
                SendResponse(requestId, true, new { success = true });
                break;

            case "open_system_shell":
                {
                    string shellName = data.TryGetProperty("shell", out var shEl) ? shEl.GetString() ?? "cmd" : "cmd";
                    bool asAdmin = data.TryGetProperty("asAdmin", out var adEl) && adEl.GetBoolean();
                    bool embedShells = StartupHelper.GetPreference("EmbedShellsInTerminalWorkspace", false);

                    if (embedShells && !asAdmin)
                    {
                        _mainForm.Invoke(() =>
                        {
                            if (_cliWebviewForm == null || _cliWebviewForm.IsDisposed)
                            {
                                _cliWebviewForm = new CliWebviewForm(_cliManager);
                            }
                            if (shellName.Equals("powershell", StringComparison.OrdinalIgnoreCase))
                            {
                                _cliWebviewForm.OpenEmbeddedShell("powershell", "PowerShell", "powershell.exe");
                            }
                            else
                            {
                                _cliWebviewForm.OpenEmbeddedShell("cmd", "Command Prompt", "cmd.exe");
                            }
                        });
                    }
                    else
                    {
                        if (shellName.Equals("powershell", StringComparison.OrdinalIgnoreCase))
                        {
                            ShellLauncher.OpenPowerShell(asAdmin);
                        }
                        else
                        {
                            ShellLauncher.OpenCommandPrompt(asAdmin);
                        }
                    }
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "minimize_to_tray":
                _mainForm.Invoke(() =>
                {
                    var allowTray = StartupHelper.GetPreference("MinimizeToTray", true);
                    if (allowTray)
                    {
                        _mainForm.Hide();
                        (_mainForm as MainForm)?.ShowTrayBalloon("Agents Hub", "Agents Hub is running in the background.");
                    }
                    else
                    {
                        _mainForm.WindowState = FormWindowState.Minimized;
                    }
                });
                SendResponse(requestId, true, new { success = true });
                break;

            case "save_general_settings":
                if (data.TryGetProperty("settings", out var stProp))
                {
                    if (stProp.TryGetProperty("autostart", out var autoEl))
                    {
                        StartupHelper.SetWindowsStartup(autoEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("startMinimized", out var minEl))
                    {
                        StartupHelper.SetPreference("StartMinimized", minEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("minimizeToTray", out var mttEl))
                    {
                        StartupHelper.SetPreference("MinimizeToTray", mttEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("notifications", out var notifEl))
                    {
                        StartupHelper.SetPreference("WindowsNotifications", notifEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("maskPublicIp", out var maskEl))
                    {
                        StartupHelper.SetPreference("MaskPublicIp", maskEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("maskShortcutPaths", out var maskPathsEl))
                    {
                        StartupHelper.SetPreference("MaskShortcutPaths", maskPathsEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("embedShellsInTerminalWorkspace", out var embedEl))
                    {
                        StartupHelper.SetPreference("EmbedShellsInTerminalWorkspace", embedEl.GetBoolean());
                    }
                    string? themeStr = null;
                    if (stProp.TryGetProperty("theme", out var themeEl))
                    {
                        themeStr = themeEl.GetString();
                        StartupHelper.SetStringPreference("Theme", themeStr ?? "dark");
                    }
                    string? accentStr = null;
                    if (stProp.TryGetProperty("accentColor", out var colEl))
                    {
                        accentStr = colEl.GetString();
                        StartupHelper.SetStringPreference("AccentColor", accentStr ?? "#4049B9");
                    }

                    var appTheme = (themeStr != null && themeStr.Equals("light", StringComparison.OrdinalIgnoreCase))
                        ? AppTheme.Light
                        : AppTheme.Dark;
                    ThemeManager.SetTheme(appTheme, accentStr);

                    _mainForm.Invoke(() =>
                    {
                        if (_cliWebviewForm != null && !_cliWebviewForm.IsDisposed)
                        {
                            _cliWebviewForm.ApplyTheme();
                        }
                    });

                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "save_shortcuts":
                if (data.TryGetProperty("shortcuts", out var scProp))
                {
                    var scList = JsonSerializer.Deserialize<List<DashboardShortcut>>(scProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (scList != null)
                    {
                        var cfg = AiConfigService.Instance.CurrentConfig;
                        cfg.Shortcuts = scList;
                        AiConfigService.Instance.SaveConfig(cfg);
                        SendResponse(requestId, true, new { shortcuts = cfg.Shortcuts });
                    }
                }
                break;

            case "launch_shortcut":
                if (data.TryGetProperty("target", out var tgProp))
                {
                    var target = tgProp.GetString() ?? string.Empty;
                    var args = data.TryGetProperty("arguments", out var argProp) ? argProp.GetString() ?? string.Empty : string.Empty;
                    LaunchShortcutApp(target, args);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "browse_shortcut_target":
                string? selectedFile = null;
                _mainForm.Invoke(() =>
                {
                    using var ofd = new OpenFileDialog
                    {
                        Title = "Select Application or Script Executable",
                        Filter = "Executable & Scripts (*.exe;*.cmd;*.bat;*.ps1)|*.exe;*.cmd;*.bat;*.ps1|All Files (*.*)|*.*",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                    };
                    if (ofd.ShowDialog(_mainForm) == DialogResult.OK)
                    {
                        selectedFile = ofd.FileName;
                    }
                });
                if (!string.IsNullOrWhiteSpace(selectedFile))
                {
                    SendResponse(requestId, true, new { path = selectedFile });
                }
                else
                {
                    SendResponse(requestId, false, "Selection cancelled");
                }
                break;

            case "get_public_ip":
                var currentIp = await NetworkHelper.FetchPublicIpAsync();
                SendResponse(requestId, true, new { ip = currentIp ?? "Unavailable" });
                break;

            case "rotate_warp_ip":
                var rotateMsg = await NetworkHelper.RotateCloudflareWarpIpAsync();
                var freshIp = await NetworkHelper.FetchPublicIpAsync();
                SendResponse(requestId, true, new
                {
                    success = !rotateMsg.StartsWith("Failed", StringComparison.OrdinalIgnoreCase),
                    message = rotateMsg,
                    ip = freshIp ?? rotateMsg
                });
                break;

            case "generate_qr_code":
                if (data.TryGetProperty("url", out var qrUrlEl))
                {
                    var u = qrUrlEl.GetString() ?? string.Empty;
                    var qr = QrCodeHelper.GenerateQrCodeBase64(u);
                    SendResponse(requestId, true, new { qrBase64 = qr });
                }
                break;

            case "open_url_external":
                if (data.TryGetProperty("url", out var urlProp))
                {
                    var targetUrl = urlProp.GetString();
                    if (!string.IsNullOrWhiteSpace(targetUrl))
                    {
                        if (targetUrl.Contains(":9119") || targetUrl.Contains(":9191"))
                        {
                            var hermesInfo = _cliManager.GetInfo(CliType.Hermes);
                            if (hermesInfo.State != CliState.Running)
                            {
                                if (hermesInfo.State != CliState.Starting)
                                {
                                    _cliManager.Start(CliType.Hermes);
                                }

                                for (int i = 0; i < 35; i++)
                                {
                                    await Task.Delay(100);
                                    try
                                    {
                                        using var testClient = new System.Net.Sockets.TcpClient();
                                        using var cts = new CancellationTokenSource(200);
                                        await testClient.ConnectAsync(System.Net.IPAddress.Loopback, _cliManager.HermesService.Port, cts.Token);
                                        if (testClient.Connected) break;
                                    }
                                    catch { }
                                }
                            }
                        }

                        var localTarget = targetUrl;
                        var lanIp = NetworkHelper.GetLocalIPv4();
                        if (!string.IsNullOrEmpty(lanIp) && localTarget.Contains(lanIp))
                        {
                            localTarget = localTarget.Replace(lanIp, "127.0.0.1");
                        }

                        if (!localTarget.Contains("theme=") && (localTarget.Contains(":9229") || localTarget.Contains(":9339") || localTarget.Contains(":9191") || localTarget.Contains(":9119")))
                        {
                            string sep = localTarget.Contains('?') ? "&" : "?";
                            string themeParam = $"theme={(ThemeManager.CurrentTheme == AppTheme.Light ? "light" : "dark")}&accent={Uri.EscapeDataString(ThemeManager.AccentHex)}";
                            localTarget = $"{localTarget}{sep}{themeParam}";
                        }

                        Process.Start(new ProcessStartInfo(localTarget) { UseShellExecute = true });
                        SendResponse(requestId, true, new { success = true });
                    }
                }
                break;

            default:
                SendResponse(requestId, false, $"Unknown action: {action}");
                break;
        }
    }

    private async Task HandleEnhancePromptAsync(JsonElement data, string? requestId)
    {
        var providerId = data.TryGetProperty("providerId", out var pId) ? pId.GetString() : null;
        var modelName = data.TryGetProperty("model", out var mName) ? mName.GetString() : null;
        var rawPrompt = data.TryGetProperty("prompt", out var rPrompt) ? rPrompt.GetString() : string.Empty;
        var style = data.TryGetProperty("style", out var sProp) ? sProp.GetString() : "General & Balanced";

        if (string.IsNullOrWhiteSpace(rawPrompt))
        {
            SendResponse(requestId, false, "Please enter a prompt to enhance.");
            return;
        }

        var config = AiConfigService.Instance.CurrentConfig;
        var provider = config.Providers.FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
        {
            SendResponse(requestId, false, "Provider not found.");
            return;
        }

        _promptEnhanceCts?.Cancel();
        _promptEnhanceCts = new CancellationTokenSource();

        try
        {
            var enhanced = await PromptEnhancerService.Instance.EnhancePromptAsync(
                provider,
                modelName ?? provider.Models.FirstOrDefault() ?? "gpt-4o-mini",
                rawPrompt,
                style ?? "General & Balanced",
                _promptEnhanceCts.Token
            );

            SendResponse(requestId, true, new { enhancedText = enhanced });
        }
        catch (OperationCanceledException)
        {
            SendResponse(requestId, false, "Enhancement cancelled.");
        }
        catch (Exception ex)
        {
            SendResponse(requestId, false, ex.Message);
        }
    }

    private async Task HandleFetchModelsAsync(JsonElement data, string? requestId)
    {
        var providerId = data.TryGetProperty("providerId", out var pId) ? pId.GetString() : null;
        var config = AiConfigService.Instance.CurrentConfig;
        var provider = config.Providers.FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            SendResponse(requestId, false, "Provider not found.");
            return;
        }

        try
        {
            var models = await PromptEnhancerService.Instance.FetchAvailableModelsAsync(provider);
            int addedCount = 0;
            foreach (var m in models)
            {
                if (!provider.Models.Contains(m, StringComparer.OrdinalIgnoreCase))
                {
                    provider.Models.Add(m);
                    addedCount++;
                }
            }
            AiConfigService.Instance.SaveConfig(config);
            SendResponse(requestId, true, new { models = provider.Models, total = models.Count, added = addedCount });
        }
        catch (Exception ex)
        {
            SendResponse(requestId, false, ex.Message);
        }
    }

    private void LaunchToolApp(string toolName)
    {
        var defaults = AiConfigService.Instance.GetDefaultToolTargets();
        var def = defaults.TryGetValue(toolName, out var defVal) ? defVal : string.Empty;
        var target = AiConfigService.Instance.GetToolTarget(toolName, def);

        if (string.IsNullOrWhiteSpace(target)) return;

        try
        {
            if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch {toolName}: {ex.Message}", "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LaunchShortcutApp(string target, string arguments)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            if (target.Equals("cli:hermes", StringComparison.OrdinalIgnoreCase))
            {
                ToggleCliInternal(CliType.Hermes);
                return;
            }
            if (target.Equals("cli:claude", StringComparison.OrdinalIgnoreCase))
            {
                ToggleCliInternal(CliType.ClaudeCode);
                return;
            }
            if (target.Equals("cli:antigravity", StringComparison.OrdinalIgnoreCase))
            {
                ToggleCliInternal(CliType.AntigravityCli);
                return;
            }

            if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            }
            else
            {
                var psi = new ProcessStartInfo(target, arguments ?? string.Empty) { UseShellExecute = true };
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch shortcut '{target}': {ex.Message}", "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleCliInternal(CliType cliType)
    {
        var info = _cliManager.GetInfo(cliType);
        if (info.State == CliState.Running)
        {
            _ = _cliManager.StopAsync(cliType);
        }
        else
        {
            _cliManager.Start(cliType);
        }
    }

    private object GetInitialStateDto()
    {
        var lanIp = NetworkHelper.GetLocalIPv4();
        int hermesPort = _cliManager.HermesService.Port;
        int lanProxyPort = _lanProxyService?.ListenPort ?? 9191;
        bool lanProxyRunning = _lanProxyService?.IsRunning ?? false;
        string lanUrl = $"http://{lanIp}:{lanProxyPort}/";
        string qrBase64 = QrCodeHelper.GenerateQrCodeBase64(lanUrl);
        var runningPorts = GetRunningWebPorts(lanIp, hermesPort, lanProxyPort);

        return new
        {
            version = "v1.0.3",
            lanIp,
            lanUrl,
            lanProxyPort,
            lanProxyRunning,
            lanProxyEnabled = lanProxyRunning,
            qrBase64,
            runningPorts,
            settings = new
            {
                autostart = StartupHelper.IsWindowsStartupEnabled(),
                startMinimized = StartupHelper.GetPreference("StartMinimized", false),
                minimizeToTray = StartupHelper.GetPreference("MinimizeToTray", true),
                notifications = StartupHelper.GetPreference("WindowsNotifications", true),
                maskPublicIp = StartupHelper.GetPreference("MaskPublicIp", false),
                maskShortcutPaths = StartupHelper.GetPreference("MaskShortcutPaths", false),
                embedShellsInTerminalWorkspace = StartupHelper.GetPreference("EmbedShellsInTerminalWorkspace", false),
                theme = StartupHelper.GetStringPreference("Theme", "dark"),
                accentColor = StartupHelper.GetStringPreference("AccentColor", "#10b981")
            },
            clis = new[]
            {
                new { name = "Hermes", type = CliType.Hermes.ToString(), state = _cliManager.GetInfo(CliType.Hermes).State.ToString(), port = hermesPort, pid = _cliManager.GetInfo(CliType.Hermes).ProcessId },
                new { name = "Claude Code", type = CliType.ClaudeCode.ToString(), state = _cliManager.GetInfo(CliType.ClaudeCode).State.ToString(), port = 9229, pid = _cliManager.GetInfo(CliType.ClaudeCode).ProcessId },
                new { name = "Antigravity CLI", type = CliType.AntigravityCli.ToString(), state = _cliManager.GetInfo(CliType.AntigravityCli).State.ToString(), port = 9339, pid = _cliManager.GetInfo(CliType.AntigravityCli).ProcessId }
            },
            antigravity = new
            {
                activeProfile = _cliManager.ProfileService.ActiveProfile,
                profiles = GetProfilesWithEmail()
            },
            aiConfig = GetAiConfigDto()
        };
    }

    private object GetRunningWebPorts(string lanIp, int hermesPort, int lanProxyPort)
    {
        string themeParam = $"?theme={(ThemeManager.CurrentTheme == AppTheme.Light ? "light" : "dark")}&accent={Uri.EscapeDataString(ThemeManager.AccentHex)}";
        var list = new List<object>
        {
            new
            {
                id = "hermes",
                name = "Hermes Dashboard",
                port = hermesPort,
                lanPort = lanProxyPort,
                localUrl = $"http://127.0.0.1:{hermesPort}/{themeParam}",
                lanUrl = $"http://{lanIp}:{lanProxyPort}/{themeParam}",
                state = _cliManager.GetInfo(CliType.Hermes).State.ToString(),
                isRunning = _cliManager.GetInfo(CliType.Hermes).State == CliState.Running
            },
            new
            {
                id = "claude_code",
                name = "Claude Code Web",
                port = 9229,
                lanPort = 9229,
                localUrl = $"http://127.0.0.1:9229/{themeParam}",
                lanUrl = $"http://{lanIp}:9229/{themeParam}",
                state = "Running",
                isRunning = true
            },
            new
            {
                id = "antigravity_cli",
                name = "Antigravity CLI Web",
                port = 9339,
                lanPort = 9339,
                localUrl = $"http://127.0.0.1:9339/{themeParam}",
                lanUrl = $"http://{lanIp}:9339/{themeParam}",
                state = "Running",
                isRunning = true
            },
            new
            {
                id = "lan_proxy",
                name = "LAN Remote Gateway",
                port = lanProxyPort,
                lanPort = lanProxyPort,
                localUrl = $"http://127.0.0.1:{lanProxyPort}/{themeParam}",
                lanUrl = $"http://{lanIp}:{lanProxyPort}/{themeParam}",
                state = (_lanProxyService?.IsRunning ?? false) ? "Running" : "Offline",
                isRunning = _lanProxyService?.IsRunning ?? false
            }
        };

        var customWs = AiConfigService.Instance.CurrentConfig.CliWorkspaces;
        foreach (var ws in customWs)
        {
            if (ws.IsWebUi && ws.Port.HasValue && ws.Port.Value > 0)
            {
                int p = ws.Port.Value;
                list.Add(new
                {
                    id = ws.Id,
                    name = ws.Name,
                    port = p,
                    lanPort = p,
                    localUrl = $"http://127.0.0.1:{p}/",
                    lanUrl = $"http://{lanIp}:{p}/",
                    state = _cliManager.GetInfo(ws.Id).State.ToString(),
                    isRunning = _cliManager.GetInfo(ws.Id).State == CliState.Running
                });
            }
        }

        return list;
    }

    private object GetAiConfigDto()
    {
        var cfg = AiConfigService.Instance.CurrentConfig;
        return new
        {
            selectedProviderId = cfg.SelectedProviderId,
            selectedModel = cfg.SelectedModel,
            toolTargets = cfg.ToolTargets,
            shortcuts = cfg.Shortcuts.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                target = s.Target,
                arguments = s.Arguments,
                category = s.Category
            }).ToList(),
            providers = cfg.Providers.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                baseUrl = p.BaseUrl,
                apiKey = p.ApiKey,
                isEnabled = p.IsEnabled,
                models = p.Models
            }).ToList()
        };
    }

    private int GetCliPort(CliType type) => type switch
    {
        CliType.Hermes => _cliManager.HermesService.Port,
        CliType.ClaudeCode => 9229,
        CliType.AntigravityCli => 9339,
        _ => 0
    };

    private void PostEventToUi(string eventType, object payload)
    {
        if (_coreWebView == null) return;
        try
        {
            var envelope = JsonSerializer.Serialize(new
            {
                @event = eventType,
                payload
            });
            _coreWebView.PostWebMessageAsJson(envelope);
        }
        catch { }
    }

    private void SendResponse(string? requestId, bool success, object? data)
    {
        if (_coreWebView == null) return;
        try
        {
            var envelope = JsonSerializer.Serialize(new
            {
                responseId = requestId,
                success,
                data
            });
            _coreWebView.PostWebMessageAsJson(envelope);
        }
        catch { }
    }

    private object[] GetProfilesWithEmail()
    {
        return _cliManager.ProfileService.GetProfiles()
            .Select(name => (object)new
            {
                name,
                email = _cliManager.ProfileService.GetAccountEmail(name) ?? string.Empty
            })
            .ToArray();
    }

    public void Dispose()
    {
        _streamFlushTimer.Stop();
        _streamFlushTimer.Dispose();
        _metricsTimer.Stop();
        _metricsTimer.Dispose();
        _cpuCounter?.Dispose();
        _promptEnhanceCts?.Dispose();
    }
}
