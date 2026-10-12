#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
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
    private readonly OpenCodeWatcherService? _openCodeWatcher;
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

    // Smart Cooldown rate limiter for VPN quota reset (backend guard)
    private static DateTime _lastQuotaResetTime = DateTime.MinValue;
    private static readonly object _quotaResetLock = new();
    private readonly ProtonVpnService _protonVpnService;

    public DashboardWebBridge(Form mainForm, CliManagerService cliManager, LanProxyService? lanProxyService, OpenCodeWatcherService? openCodeWatcher = null)
    {
        _mainForm = mainForm;
        _cliManager = cliManager;
        _lanProxyService = lanProxyService;
        _openCodeWatcher = openCodeWatcher;
        _protonVpnService = new ProtonVpnService(_openCodeWatcher);

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

        if (_openCodeWatcher != null)
        {
            var stateDto = _openCodeWatcher.GetStateDto();
            PostOpenCodeUpdate(stateDto);
        }
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

        _cliManager.StatusChangedId += (id, state, msg) =>
        {
            if (CliManagerService.IsAntigravityProfileId(id, out var profileName))
            {
                PostEventToUi("antigravity_instance_status_changed", new
                {
                    id,
                    profile = profileName,
                    state = state.ToString(),
                    message = msg,
                    port = _cliManager.GetPortForProfile(profileName),
                    isRunning = state == CliState.Running
                });

                var lanIp = NetworkHelper.GetLocalIPv4();
                var lanProxyPort = _lanProxyService?.ListenPort ?? 8080;
                var rPorts = GetRunningWebPorts(lanIp, _cliManager.HermesService.Port, lanProxyPort);
                PostEventToUi("running_ports_updated", new
                {
                    runningPorts = rPorts
                });
            }
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

        if (_openCodeWatcher != null)
        {
            _openCodeWatcher.OnStatusChanged += (isOnline, port) =>
            {
                var stateDto = _openCodeWatcher.GetStateDto();
                PostOpenCodeUpdate(stateDto);
                EmitLog("OpenCode", isOnline ? "info" : "warn", isOnline ? $"Sidecar Online on port {port}" : "Sidecar Offline");
            };

            _openCodeWatcher.OnSessionsUpdated += (sessions) =>
            {
                var stateDto = _openCodeWatcher.GetStateDto();
                PostOpenCodeUpdate(stateDto);
                EmitLog("OpenCode", "info", $"Active sessions updated ({sessions.Count} session(s))");
            };

            _openCodeWatcher.OnInteractivePrompt += (prompt) =>
            {
                PostEventToUi("opencode_interactive_prompt", prompt);
                EmitLog("OpenCode", "warn", $"Interactive prompt: {prompt.Title} - {prompt.Prompt}");
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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

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
        long hermesMemMb = 0;
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref mem))
            {
                totalRamMb = (long)(mem.ullTotalPhys / (1024 * 1024));
                long availMb = (long)(mem.ullAvailPhys / (1024 * 1024));
                usedRamMb = totalRamMb - availMb;
            }
        }
        catch { }
        try
        {
            using var proc = Process.GetCurrentProcess();
            hermesMemMb = proc.WorkingSet64 / (1024 * 1024);
        }
        catch { }

        PostEventToUi("hardware_metrics", GetSystemTelemetryDto());
    }

    private object GetSystemTelemetryDto()
    {
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
        long hermesMemMb = 0;
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref mem))
            {
                totalRamMb = (long)(mem.ullTotalPhys / (1024 * 1024));
                long availMb = (long)(mem.ullAvailPhys / (1024 * 1024));
                usedRamMb = totalRamMb - availMb;
            }
        }
        catch { }
        try
        {
            using var proc = Process.GetCurrentProcess();
            hermesMemMb = proc.WorkingSet64 / (1024 * 1024);
        }
        catch { }

        double memUsedGb = Math.Round(usedRamMb / 1024.0, 1);
        double memTotalGb = Math.Round(totalRamMb / 1024.0, 1);

        return new
        {
            cpu = Math.Round(cpuPercent, 1),
            cpuPercent = Math.Round(cpuPercent, 1),
            ramMb = usedRamMb,
            totalRamMb = totalRamMb,
            memoryUsedGb = memUsedGb,
            memoryTotalGb = memTotalGb,
            hermesMemMb = hermesMemMb
        };
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

            case "get_system_telemetry":
                SendResponse(requestId, true, GetSystemTelemetryDto());
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
                        SendResponse(requestId, true, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
                    }
                }
                break;

            case "start_antigravity_instance":
                if (data.TryGetProperty("profile", out var startProfProp))
                {
                    var pName = startProfProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        _cliManager.StartAntigravityProfile(pName);
                        SendResponse(requestId, true, new { success = true, profile = pName, port = _cliManager.GetPortForProfile(pName), profiles = GetProfilesWithEmail() });
                    }
                }
                break;

            case "stop_antigravity_instance":
                if (data.TryGetProperty("profile", out var stopProfProp))
                {
                    var pName = stopProfProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        await _cliManager.StopAntigravityProfileAsync(pName);
                        SendResponse(requestId, true, new { success = true, profile = pName, profiles = GetProfilesWithEmail() });
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

            case "get_claude_gateway_config":
                {
                    var cfg = AiConfigService.Instance;
                    var providers = cfg.GetClaudeGatewayProviders();
                    var active = cfg.GetActiveClaudeGatewayProvider();
                    SendResponse(requestId, true, new
                    {
                        injectionEnabled = cfg.GetClaudeInjectionEnabled(),
                        selectedGatewayId = cfg.GetClaudeSelectedGatewayId(),
                        selectedModel = cfg.GetClaudeSelectedModel(),
                        providers = providers.Select(p => new
                        {
                            id = p.Id,
                            name = p.Name,
                            baseUrl = p.BaseUrl,
                            apiKey = p.ApiKey,
                            isPreset = p.IsPreset
                        }),
                        activeProvider = new
                        {
                            id = active.Id,
                            name = active.Name,
                            baseUrl = active.BaseUrl,
                            apiKey = active.ApiKey,
                            isPreset = active.IsPreset
                        }
                    });
                }
                break;

            case "save_claude_gateway_config":
                {
                    string provId = data.TryGetProperty("providerId", out var pIdEl) ? (pIdEl.GetString() ?? "") : "";
                    string baseUrl = data.TryGetProperty("baseUrl", out var bEl) ? (bEl.GetString() ?? "") : "";
                    string apiKey = data.TryGetProperty("apiKey", out var kEl) ? (kEl.GetString()?.Trim() ?? "") : "";
                    string name = data.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";

                    var provider = new ClaudeGatewayProvider
                    {
                        Id = string.IsNullOrWhiteSpace(provId) ? "preset_local_proxy" : provId,
                        Name = name,
                        BaseUrl = baseUrl,
                        ApiKey = apiKey,
                        IsPreset = provId.StartsWith("preset_", StringComparison.OrdinalIgnoreCase)
                    };
                    AiConfigService.Instance.SaveClaudeGatewayProvider(provider);

                    if (string.Equals(AiConfigService.Instance.GetClaudeSelectedGatewayId(), provider.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        CliManagerService.SyncClaudeGatewaySettingsJson(apiKey, AiConfigService.Instance.GetClaudeSelectedModel(), baseUrl);
                    }

                    var info = _cliManager.GetInfo("claude_code");
                    if (info.State == CliState.Running || info.State == CliState.Starting)
                    {
                        await _cliManager.RestartAsync("claude_code");
                    }

                    SendResponse(requestId, true, new
                    {
                        success = true,
                        provider = new
                        {
                            id = provider.Id,
                            name = provider.Name,
                            baseUrl = provider.BaseUrl,
                            apiKey = provider.ApiKey,
                            isPreset = provider.IsPreset
                        }
                    });
                }
                break;

            case "add_claude_gateway_provider":
                {
                    string name = data.TryGetProperty("name", out var nEl) ? (nEl.GetString()?.Trim() ?? "Custom Provider") : "Custom Provider";
                    string baseUrl = data.TryGetProperty("baseUrl", out var bEl) ? (bEl.GetString()?.Trim() ?? "https://openrouter.ai/api/v1") : "https://openrouter.ai/api/v1";
                    string apiKey = data.TryGetProperty("apiKey", out var kEl) ? (kEl.GetString()?.Trim() ?? "") : "";

                    var newProv = new ClaudeGatewayProvider
                    {
                        Id = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = string.IsNullOrWhiteSpace(name) ? "Custom Gateway" : name,
                        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://openrouter.ai/api/v1" : baseUrl,
                        ApiKey = apiKey,
                        IsPreset = false
                    };
                    AiConfigService.Instance.SaveClaudeGatewayProvider(newProv);
                    AiConfigService.Instance.SetClaudeSelectedGatewayId(newProv.Id);
                    SendResponse(requestId, true, new
                    {
                        success = true,
                        provider = new
                        {
                            id = newProv.Id,
                            name = newProv.Name,
                            baseUrl = newProv.BaseUrl,
                            apiKey = newProv.ApiKey,
                            isPreset = newProv.IsPreset
                        }
                    });
                }
                break;

            case "delete_claude_gateway_provider":
                {
                    string provId = data.TryGetProperty("providerId", out var pIdEl) ? (pIdEl.GetString() ?? "") : "";
                    bool deleted = AiConfigService.Instance.DeleteClaudeGatewayProvider(provId);
                    SendResponse(requestId, deleted, new { success = deleted, selectedGatewayId = AiConfigService.Instance.GetClaudeSelectedGatewayId() });
                }
                break;

            case "toggle_claude_injection":
                {
                    bool enabled = data.TryGetProperty("enabled", out var enProp) ? enProp.GetBoolean() : !AiConfigService.Instance.GetClaudeInjectionEnabled();
                    AiConfigService.Instance.SetClaudeInjectionEnabled(enabled);

                    if (!enabled)
                    {
                        CliManagerService.RestoreClaudeOfficialLoginIfBackedUp();
                    }
                    else
                    {
                        var act = AiConfigService.Instance.GetActiveClaudeGatewayProvider();
                        CliManagerService.SyncClaudeGatewaySettingsJson(act.ApiKey, AiConfigService.Instance.GetClaudeSelectedModel(), act.BaseUrl);
                    }

                    var info = _cliManager.GetInfo("claude_code");
                    if (info.State == CliState.Running || info.State == CliState.Starting)
                    {
                        await _cliManager.RestartAsync("claude_code");
                    }

                    SendResponse(requestId, true, new { enabled, success = true });
                }
                break;

            case "select_claude_gateway_provider":
                {
                    string provId = data.TryGetProperty("providerId", out var pIdEl) ? (pIdEl.GetString() ?? "") : "";
                    if (!string.IsNullOrWhiteSpace(provId))
                    {
                        AiConfigService.Instance.SetClaudeSelectedGatewayId(provId);
                        var act = AiConfigService.Instance.GetActiveClaudeGatewayProvider();
                        if (AiConfigService.Instance.GetClaudeInjectionEnabled())
                        {
                            CliManagerService.SyncClaudeGatewaySettingsJson(act.ApiKey, AiConfigService.Instance.GetClaudeSelectedModel(), act.BaseUrl);
                            if (string.Equals(act.Id, "preset_local_proxy", StringComparison.OrdinalIgnoreCase) || act.BaseUrl.Contains(":9230"))
                            {
                                ClaudeOpenRouterProxyService.Instance.Start(9230);
                            }
                        }
                    }
                    var activeAct = AiConfigService.Instance.GetActiveClaudeGatewayProvider();
                    SendResponse(requestId, true, new
                    {
                        selectedGatewayId = AiConfigService.Instance.GetClaudeSelectedGatewayId(),
                        provider = new
                        {
                            id = activeAct.Id,
                            name = activeAct.Name,
                            baseUrl = activeAct.BaseUrl,
                            apiKey = activeAct.ApiKey,
                            isPreset = activeAct.IsPreset
                        }
                    });
                }
                break;

            case "get_claude_settings":
                {
                    var cfg = AiConfigService.Instance;
                    var apiKey = cfg.GetOpenRouterApiKey();
                    SendResponse(requestId, true, new
                    {
                        mode = cfg.GetClaudeEngineMode(),
                        model = cfg.GetClaudeSelectedModel(),
                        gatewayUrl = cfg.GetClaudeCustomGatewayUrl(),
                        hasApiKey = !string.IsNullOrWhiteSpace(apiKey),
                        apiKey = apiKey
                    });
                }
                break;

            case "save_claude_settings":
                {
                    var cfg = AiConfigService.Instance;
                    if (data.TryGetProperty("mode", out var modeProp))
                    {
                        var m = modeProp.GetString();
                        if (!string.IsNullOrWhiteSpace(m)) cfg.SetClaudeEngineMode(m);
                    }
                    if (data.TryGetProperty("model", out var modelProp))
                    {
                        var mdl = modelProp.GetString();
                        if (!string.IsNullOrWhiteSpace(mdl)) cfg.SetClaudeSelectedModel(mdl);
                    }
                    if (data.TryGetProperty("gatewayUrl", out var gwProp))
                    {
                        var gw = gwProp.GetString();
                        if (!string.IsNullOrWhiteSpace(gw)) cfg.SetClaudeCustomGatewayUrl(gw);
                    }
                    if (data.TryGetProperty("apiKey", out var keyProp))
                    {
                        var k = keyProp.GetString()?.Trim();
                        if (k != null) cfg.SetClaudeGatewayApiKey(k);
                    }
                    var apiKey = cfg.GetOpenRouterApiKey();
                    var mode = cfg.GetClaudeEngineMode();
                    if (string.Equals(mode, "OpenRouter", StringComparison.OrdinalIgnoreCase))
                    {
                        CliManagerService.SyncClaudeOpenRouterSettingsJson(apiKey, cfg.GetClaudeSelectedModel(), cfg.GetClaudeCustomGatewayUrl());
                    }

                    var info = _cliManager.GetInfo("claude_code");
                    if (info.State == CliState.Running || info.State == CliState.Starting)
                    {
                        await _cliManager.RestartAsync("claude_code");
                    }

                    SendResponse(requestId, true, new
                    {
                        success = true,
                        mode = cfg.GetClaudeEngineMode(),
                        model = cfg.GetClaudeSelectedModel(),
                        gatewayUrl = cfg.GetClaudeCustomGatewayUrl(),
                        hasApiKey = !string.IsNullOrWhiteSpace(apiKey),
                        apiKey = apiKey
                    });
                }
                break;

            case "get_openrouter_free_models":
                {
                    bool force = data.TryGetProperty("force", out var fProp) && fProp.GetBoolean();
                    var models = await AiConfigService.Instance.FetchOpenRouterFreeModelsAsync(force);
                    SendResponse(requestId, true, new { models, count = models.Count });
                }
                break;

            case "refresh_openrouter_free_models":
                {
                    var models = await AiConfigService.Instance.FetchOpenRouterFreeModelsAsync(true);
                    SendResponse(requestId, true, new { models, count = models.Count });
                }
                break;

            case "save_openrouter_key":
                {
                    var key = data.TryGetProperty("apiKey", out var kProp) ? (kProp.GetString()?.Trim() ?? string.Empty) : string.Empty;
                    AiConfigService.Instance.SetClaudeGatewayApiKey(key);

                    var engineMode = AiConfigService.Instance.GetClaudeEngineMode();
                    if (string.Equals(engineMode, "OpenRouter", StringComparison.OrdinalIgnoreCase))
                    {
                        CliManagerService.SyncClaudeOpenRouterSettingsJson(
                            key,
                            AiConfigService.Instance.GetClaudeSelectedModel(),
                            AiConfigService.Instance.GetClaudeCustomGatewayUrl());
                    }

                    var info = _cliManager.GetInfo("claude_code");
                    if (info.State == CliState.Running || info.State == CliState.Starting)
                    {
                        await _cliManager.RestartAsync("claude_code");
                    }

                    SendResponse(requestId, true, new { success = true, hasApiKey = !string.IsNullOrWhiteSpace(key) });
                }
                break;

            case "check_openrouter_key":
                {
                    var key = data.TryGetProperty("apiKey", out var kProp) ? kProp.GetString()?.Trim() : null;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        key = AiConfigService.Instance.GetOpenRouterApiKey();
                    }
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        SendResponse(requestId, true, new { valid = false, message = "No API key provided" });
                        break;
                    }

                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
                        var resp = await http.GetAsync("https://openrouter.ai/api/v1/auth/key");
                        if (resp.IsSuccessStatusCode)
                        {
                            var body = await resp.Content.ReadAsStringAsync();
                            using var kDoc = JsonDocument.Parse(body);
                            var dataObj = kDoc.RootElement.TryGetProperty("data", out var dEl) ? dEl : default;
                            double usage = dataObj.ValueKind == JsonValueKind.Object && dataObj.TryGetProperty("usage", out var uEl) ? uEl.GetDouble() : 0;
                            double? limit = dataObj.ValueKind == JsonValueKind.Object && dataObj.TryGetProperty("limit", out var lEl) && lEl.ValueKind == JsonValueKind.Number ? lEl.GetDouble() : null;
                            var label = dataObj.ValueKind == JsonValueKind.Object && dataObj.TryGetProperty("label", out var lbEl) ? lbEl.GetString() : "OpenRouter Key";
                            SendResponse(requestId, true, new
                            {
                                valid = true,
                                usage = Math.Round(usage, 4),
                                limit = limit,
                                label = label,
                                message = limit.HasValue ? $"Limit: ${limit.Value:F2} | Usage: ${usage:F4}" : $"Usage: ${usage:F4}"
                            });
                        }
                        else
                        {
                            SendResponse(requestId, true, new { valid = false, message = $"Invalid key (HTTP {(int)resp.StatusCode})" });
                        }
                    }
                    catch (Exception ex)
                    {
                        SendResponse(requestId, true, new { valid = false, message = $"Check failed: {ex.Message}" });
                    }
                }
                break;

            case "run_claude_logout":
            case "claude_logout":
                {
                    bool ok = await _cliManager.ClaudeLogoutAsync();
                    SendResponse(requestId, ok, new { success = ok, message = ok ? "Claude credentials logged out." : "Logout failed." });
                }
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
                {
                    string? target = null;
                    string? toolName = null;
                    if (data.TryGetProperty("target", out var targetProp))
                    {
                        target = targetProp.GetString();
                    }
                    if (data.TryGetProperty("toolName", out var toolProp))
                    {
                        toolName = toolProp.GetString();
                    }
                    if (data.TryGetProperty("toolId", out var idProp))
                    {
                        var toolId = idProp.GetString();
                        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(toolId))
                        {
                            var cfg = AiConfigService.Instance.CurrentConfig;
                            var item = cfg.DevTools?.FirstOrDefault(d => string.Equals(d.Id, toolId, StringComparison.OrdinalIgnoreCase));
                            target = item?.Target;
                            if (string.IsNullOrWhiteSpace(toolName)) toolName = item?.Name;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(toolName))
                    {
                        target = AiConfigService.Instance.GetToolTarget(toolName, string.Empty);
                    }

                    if (string.IsNullOrWhiteSpace(target))
                    {
                        SendResponse(requestId, false, "Target path is empty. Please click 'Browse' to set an executable path first.");
                        break;
                    }

                    bool launched = LaunchExecutableTarget(target, toolName ?? "Tool");
                    SendResponse(requestId, launched, new { success = launched, target });
                }
                break;

            case "browse_tool_target":
                {
                    string tool = string.Empty;
                    string? toolId = null;
                    if (data.TryGetProperty("toolName", out var brToolProp))
                    {
                        tool = brToolProp.GetString() ?? string.Empty;
                    }
                    if (data.TryGetProperty("toolId", out var brToolIdProp))
                    {
                        toolId = brToolIdProp.GetString();
                    }

                    string? selectedPath = null;
                    _mainForm.Invoke(() =>
                    {
                        using var ofd = new OpenFileDialog
                        {
                            Title = string.IsNullOrWhiteSpace(tool) ? "Select Executable Target" : $"Select Target Executable for {tool}",
                            Filter = "Executables (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All Files (*.*)|*.*",
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
                        if (!string.IsNullOrWhiteSpace(toolId) && cfg.DevTools != null)
                        {
                            var devTool = cfg.DevTools.FirstOrDefault(d => string.Equals(d.Id, toolId, StringComparison.OrdinalIgnoreCase));
                            if (devTool != null)
                            {
                                devTool.Target = selectedPath;
                                if (string.IsNullOrWhiteSpace(tool)) tool = devTool.Name;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(tool))
                        {
                            cfg.ToolTargets[tool] = selectedPath;
                            var devTool = cfg.DevTools?.FirstOrDefault(d => string.Equals(d.Name, tool, StringComparison.OrdinalIgnoreCase));
                            if (devTool != null) devTool.Target = selectedPath;
                        }
                        AiConfigService.Instance.SaveConfig(cfg);
                        SendResponse(requestId, true, new { toolName = tool, toolId = toolId, path = selectedPath });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Selection cancelled");
                    }
                }
                break;

            case "reset_tool_target":
                {
                    string tool = string.Empty;
                    string? toolId = null;
                    if (data.TryGetProperty("toolName", out var rsToolProp))
                    {
                        tool = rsToolProp.GetString() ?? string.Empty;
                    }
                    if (data.TryGetProperty("toolId", out var rsToolIdProp))
                    {
                        toolId = rsToolIdProp.GetString();
                    }

                    var defaults = AiConfigService.Instance.GetDefaultToolTargets();
                    var defaultTools = AiConfigService.Instance.GetDefaultDevTools();

                    string defPath = string.Empty;
                    if (!string.IsNullOrWhiteSpace(toolId))
                    {
                        var defItem = defaultTools.FirstOrDefault(d => string.Equals(d.Id, toolId, StringComparison.OrdinalIgnoreCase));
                        if (defItem != null) defPath = defItem.Target;
                    }
                    if (string.IsNullOrWhiteSpace(defPath) && !string.IsNullOrWhiteSpace(tool))
                    {
                        if (defaults.TryGetValue(tool, out var p)) defPath = p;
                    }

                    var cfg = AiConfigService.Instance.CurrentConfig;
                    if (!string.IsNullOrWhiteSpace(toolId) && cfg.DevTools != null)
                    {
                        var devTool = cfg.DevTools.FirstOrDefault(d => string.Equals(d.Id, toolId, StringComparison.OrdinalIgnoreCase));
                        if (devTool != null) devTool.Target = defPath;
                    }
                    if (!string.IsNullOrWhiteSpace(tool))
                    {
                        cfg.ToolTargets[tool] = defPath;
                        var devTool = cfg.DevTools?.FirstOrDefault(d => string.Equals(d.Name, tool, StringComparison.OrdinalIgnoreCase));
                        if (devTool != null) devTool.Target = defPath;
                    }
                    AiConfigService.Instance.SaveConfig(cfg);
                    SendResponse(requestId, true, new { toolName = tool, toolId = toolId, path = defPath });
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
                    if (stProp.TryGetProperty("enableProtonVpnProvider", out var protonEl))
                    {
                        StartupHelper.SetPreference("EnableProtonVpnProvider", protonEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("enableHardwareMonitor", out var hwEl))
                    {
                        StartupHelper.SetPreference("EnableHardwareMonitor", hwEl.GetBoolean());
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
                var ipDetails = await NetworkHelper.FetchPublicIpDetailsAsync();
                SendResponse(requestId, true, ipDetails);
                break;

            case "purge_idle_sessions":
            case "PurgeIdleSessions":
                if (_openCodeWatcher != null)
                {
                    var deleted = await _openCodeWatcher.PurgeIdleSessionsAsync();
                    SendResponse(requestId, true, new { deletedCount = deleted, success = true });
                }
                else
                {
                    SendResponse(requestId, false, "OpenCode watcher service not initialized");
                }
                break;

            case "reset_opencode_quota":
            case "rotate_warp_ip":
            case "rotate_vpn_ip":
            case "ResetOpenCodeQuota":
                {
                    string provider = data.TryGetProperty("provider", out var provProp) ? provProp.GetString() ?? "warp" : "warp";
                    await ResetOpenCodeQuotaAsync(requestId, provider);
                }
                break;

            case "disconnect_vpn":
            case "DisconnectVpn":
                await DisconnectVpnAsync(requestId);
                break;

            case "ensure_proton_vpn_running":
            case "launch_proton_vpn":
                {
                    bool isRunning = _protonVpnService.IsClientRunning();
                    bool launched = false;
                    if (!isRunning)
                    {
                        launched = await _protonVpnService.LaunchClientAsync();
                    }
                    SendResponse(requestId, true, new { isRunning = true, launched });
                }
                break;

            case "get_opencode_state":
            case "GetOpenCodeState":
                SendResponse(requestId, true, GetOpenCodeState());
                break;

            case "focus_opencode_session":
            case "FocusOpenCodeSession":
                {
                    string? sId = data.TryGetProperty("sessionId", out var sEl) ? sEl.GetString() : (data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null);
                    string? sDir = data.TryGetProperty("directory", out var dEl) ? dEl.GetString() : (data.TryGetProperty("path", out var pEl) ? pEl.GetString() : null);
                    var ok = FocusOpenCodeSession(sId, sDir);
                    SendResponse(requestId, ok, new { success = ok });
                }
                break;

            case "refresh_opencode_sidecar":
            case "RefreshOpenCodeSidecar":
                {
                    var freshState = await RefreshOpenCodeSidecarAsync();
                    SendResponse(requestId, true, freshState);
                }
                break;

            case "start_opencode_service":
            case "StartOpenCodeService":
                if (_openCodeWatcher != null)
                {
                    var (ok, msg) = await _openCodeWatcher.StartOpenCodeServiceAsync();
                    EmitLog("OpenCode", ok ? "info" : "error", msg);
                    SendResponse(requestId, ok, new { success = ok, message = msg, state = _openCodeWatcher.GetStateDto() });
                }
                else
                {
                    SendResponse(requestId, false, "OpenCode watcher service not initialized");
                }
                break;

            case "stop_opencode_service":
            case "StopOpenCodeService":
                if (_openCodeWatcher != null)
                {
                    var (ok, msg) = await _openCodeWatcher.StopOpenCodeServiceAsync();
                    EmitLog("OpenCode", "info", msg);
                    SendResponse(requestId, ok, new { success = ok, message = msg, state = _openCodeWatcher.GetStateDto() });
                }
                else
                {
                    SendResponse(requestId, false, "OpenCode watcher service not initialized");
                }
                break;

            case "recheck_opencode_service":
            case "RecheckOpenCodeService":
                if (_openCodeWatcher != null)
                {
                    var freshState = await _openCodeWatcher.RefreshAsync();
                    EmitLog("OpenCode", "info", $"Rechecked service status: {(freshState.IsOnline ? "Online (Port " + freshState.Port + ")" : "Offline")}");
                    SendResponse(requestId, true, freshState);
                }
                else
                {
                    SendResponse(requestId, false, "OpenCode watcher service not initialized");
                }
                break;

            case "reply_opencode_prompt":
            case "ReplyOpenCodePrompt":
                if (_openCodeWatcher != null)
                {
                    string pId = data.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                    string pType = data.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "permission" : "permission";
                    bool pApprove = data.TryGetProperty("approved", out var appProp) && appProp.GetBoolean();
                    string? pAnswer = data.TryGetProperty("answer", out var ansProp) ? ansProp.GetString() : null;

                    var ok = await _openCodeWatcher.ReplyPromptAsync(pId, pType, pApprove, pAnswer);
                    EmitLog("OpenCode", ok ? "info" : "error", $"Replied to prompt {pId}: {(pApprove ? "Approved" : "Rejected")} (success: {ok})");
                    SendResponse(requestId, ok, new { success = ok });
                }
                else
                {
                    SendResponse(requestId, false, "OpenCode watcher service not initialized");
                }
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

    private bool LaunchExecutableTarget(string target, string toolName)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
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
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch {toolName}: {ex.Message}", "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void LaunchToolApp(string toolName)
    {
        var target = AiConfigService.Instance.GetToolTarget(toolName, string.Empty);
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show($"Target path for '{toolName}' is not configured. Please click Browse to set its executable path.", "Target Empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        LaunchExecutableTarget(target, toolName);
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
                enableProtonVpnProvider = StartupHelper.GetPreference("EnableProtonVpnProvider", false),
                enableHardwareMonitor = StartupHelper.GetPreference("EnableHardwareMonitor", false),
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
            opencode = GetOpenCodeState(),
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
            }
        };

        var runningProfiles = _cliManager.GetRunningAntigravityProfiles();
        if (runningProfiles.Count > 0)
        {
            foreach (var prof in runningProfiles)
            {
                int pPort = _cliManager.GetPortForProfile(prof) ?? 9339;
                list.Add(new
                {
                    id = CliManagerService.FormatAntigravityProfileId(prof),
                    name = $"Antigravity CLI ({prof})",
                    port = pPort,
                    lanPort = pPort,
                    localUrl = $"http://127.0.0.1:{pPort}/{themeParam}",
                    lanUrl = $"http://{lanIp}:{pPort}/{themeParam}",
                    state = "Running",
                    isRunning = true
                });
            }
        }
        else
        {
            var activeProf = _cliManager.ProfileService.ActiveProfile ?? "Default";
            var activeInfo = _cliManager.GetInfo(CliType.AntigravityCli);
            int basePort = activeInfo.Port ?? 9339;
            bool isRunning = activeInfo.State == CliState.Running;
            list.Add(new
            {
                id = "antigravity_cli",
                name = $"Antigravity CLI ({activeProf})",
                port = basePort,
                lanPort = basePort,
                localUrl = $"http://127.0.0.1:{basePort}/{themeParam}",
                lanUrl = $"http://{lanIp}:{basePort}/{themeParam}",
                state = isRunning ? "Running" : "Offline",
                isRunning = isRunning
            });
        }

        list.Add(new
        {
            id = "lan_proxy",
            name = "LAN Remote Gateway",
            port = lanProxyPort,
            lanPort = lanProxyPort,
            localUrl = $"http://127.0.0.1:{lanProxyPort}/{themeParam}",
            lanUrl = $"http://{lanIp}:{lanProxyPort}/{themeParam}",
            state = (_lanProxyService?.IsRunning ?? false) ? "Running" : "Offline",
            isRunning = _lanProxyService?.IsRunning ?? false
        });

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
            devTools = (cfg.DevTools ?? new List<DevToolItem>()).Select(d => new
            {
                id = d.Id,
                name = d.Name,
                description = d.Description,
                target = d.Target,
                isEnabled = d.IsEnabled
            }).ToList(),
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
                type = eventType == "opencode_interactive_prompt" ? "OPENCODE_INTERACTIVE_PROMPT" : eventType,
                @event = eventType,
                payload
            });
            _coreWebView.PostWebMessageAsJson(envelope);
        }
        catch { }
    }

    public void EmitLog(string source, string level, string message)
    {
        PostEventToUi("live_log_entry", new
        {
            timestamp = DateTime.Now.ToString("HH:mm:ss"),
            tag = source,
            level = level,
            text = $"[{source}] {message}"
        });
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

    private void PostOpenCodeUpdate(OpenCodeStateDto stateDto)
    {
        if (_coreWebView == null) return;
        try
        {
            var envelope = JsonSerializer.Serialize(new
            {
                type = "OPENCODE_UPDATE",
                data = stateDto,
                @event = "opencode_status_changed",
                payload = stateDto
            });
            _coreWebView.PostWebMessageAsJson(envelope);
        }
        catch { }
    }

    public OpenCodeStateDto GetOpenCodeState()
    {
        return _openCodeWatcher?.GetStateDto() ?? new OpenCodeStateDto();
    }

    public bool FocusOpenCodeSession(string? sessionId, string? directory)
    {
        return _openCodeWatcher?.FocusOpenCodeSession(sessionId, directory) ?? false;
    }

    public async Task<OpenCodeStateDto> RefreshOpenCodeSidecarAsync()
    {
        if (_openCodeWatcher != null)
        {
            return await _openCodeWatcher.RefreshAsync();
        }
        return new OpenCodeStateDto();
    }

    public async Task<object> ResetOpenCodeQuotaAsync(string? requestId = null, string provider = "warp")
    {
        bool isProton = provider.Equals("proton", StringComparison.OrdinalIgnoreCase) || provider.Equals("protonvpn", StringComparison.OrdinalIgnoreCase);
        double cooldownTarget = isProton ? 60.0 : 12.0;
        string logTag = isProton ? "ProtonVPN" : "WARP";
        string providerName = isProton ? "ProtonVPN" : "CloudflareWARP";

        // Provider-specific backend cooldown guard
        lock (_quotaResetLock)
        {
            var elapsed = (DateTime.UtcNow - _lastQuotaResetTime).TotalSeconds;
            if (elapsed < cooldownTarget)
            {
                int remaining = (int)Math.Ceiling(cooldownTarget - elapsed);
                var cooldownResult = new
                {
                    success = false,
                    provider = providerName,
                    message = $"{providerName} cooldown active. Please wait {remaining}s before switching servers.",
                    cooldownRemaining = remaining,
                    cooldownSeconds = remaining
                };
                if (requestId != null) SendResponse(requestId, false, cooldownResult);
                return cooldownResult;
            }
            _lastQuotaResetTime = DateTime.UtcNow;
        }

        string baseDir = AppContext.BaseDirectory;
        string scriptPath = Path.Combine(baseDir, "dist", "fixquota-command.js");
        if (!File.Exists(scriptPath))
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "dist", "fixquota-command.js");
            if (File.Exists(fallback)) scriptPath = fallback;
        }

        bool nodeExecuted = false;
        string outputJson = string.Empty;
        var outputLines = new List<string>();

        if (File.Exists(scriptPath))
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "node.exe",
                    Arguments = $"\"{scriptPath}\" --provider={(isProton ? "proton" : "warp")}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        PostEventToUi("terminal_data", new { cli = "QuotaReset", data = e.Data + "\r\n" });
                        PostEventToUi("quota_reset_progress", new { line = e.Data });
                        EmitLog(logTag, "info", e.Data);
                    }
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        PostEventToUi("terminal_data", new { cli = "QuotaReset", data = e.Data + "\r\n" });
                        EmitLog(logTag, "warn", e.Data);
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(22));
                try
                {
                    await proc.WaitForExitAsync(cts.Token);
                    nodeExecuted = true;
                    outputJson = string.Join(Environment.NewLine, outputLines);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(true); } catch { }
                    nodeExecuted = false;
                }
            }
            catch
            {
                nodeExecuted = false;
            }
        }

        // If node was executed, try parsing clean JSON output
        if (nodeExecuted && !string.IsNullOrWhiteSpace(outputJson))
        {
            try
            {
                int start = outputJson.IndexOf('{');
                int end = outputJson.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    var jsonBlock = outputJson.Substring(start, end - start + 1);
                    using var doc = JsonDocument.Parse(jsonBlock);
                    var root = doc.RootElement;
                    bool success = root.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
                    string oldIp = root.TryGetProperty("oldIp", out var oiProp) ? oiProp.GetString() ?? "unknown" : "unknown";
                    string newIp = root.TryGetProperty("newIp", out var niProp) ? niProp.GetString() ?? "unknown" : "unknown";

                    string oldIpv4 = "unknown";
                    string oldIpv6 = "Not assigned";
                    if (root.TryGetProperty("old", out var oldProp))
                    {
                        if (oldProp.TryGetProperty("ipv4", out var o4)) oldIpv4 = o4.GetString() ?? "unknown";
                        if (oldProp.TryGetProperty("ipv6", out var o6)) oldIpv6 = o6.GetString() ?? "Not assigned";
                    }
                    else if (root.TryGetProperty("oldIpv4", out var o4p))
                    {
                        oldIpv4 = o4p.GetString() ?? "unknown";
                    }
                    else
                    {
                        oldIpv4 = oldIp;
                    }

                    string newIpv4 = "unknown";
                    string newIpv6 = "Not assigned";
                    if (root.TryGetProperty("new", out var newProp))
                    {
                        if (newProp.TryGetProperty("ipv4", out var n4)) newIpv4 = n4.GetString() ?? "unknown";
                        if (newProp.TryGetProperty("ipv6", out var n6)) newIpv6 = n6.GetString() ?? "Not assigned";
                    }
                    else if (root.TryGetProperty("newIpv4", out var n4p))
                    {
                        newIpv4 = n4p.GetString() ?? "unknown";
                    }
                    else
                    {
                        newIpv4 = newIp;
                    }

                    bool dnsFlushed = root.TryGetProperty("dnsFlushed", out var dfProp) && dfProp.GetBoolean();
                    bool sidecarSynced = root.TryGetProperty("sidecarSynced", out var ssProp) && ssProp.GetBoolean();
                    string message = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? string.Empty : string.Empty;
                    string countryCode = root.TryGetProperty("countryCode", out var ccProp) ? ccProp.GetString() ?? string.Empty : string.Empty;
                    string country = root.TryGetProperty("country", out var cProp) ? cProp.GetString() ?? string.Empty : string.Empty;
                    string countryEmoji = root.TryGetProperty("countryEmoji", out var ceProp) ? ceProp.GetString() ?? string.Empty : string.Empty;
                    string city = root.TryGetProperty("city", out var ctProp) ? ctProp.GetString() ?? string.Empty : string.Empty;
                    int cooldownSeconds = root.TryGetProperty("cooldownSeconds", out var cdProp) ? cdProp.GetInt32() : (int)cooldownTarget;

                    if (string.IsNullOrWhiteSpace(countryEmoji) && !string.IsNullOrWhiteSpace(countryCode))
                    {
                        countryEmoji = NetworkHelper.CountryCodeToEmoji(countryCode);
                    }

                    if (success && _openCodeWatcher != null)
                    {
                        _ = _openCodeWatcher.RefreshAsync();
                    }

                    var resultObj = new
                    {
                        success,
                        provider = providerName,
                        cooldownSeconds,
                        old = new { ipv4 = oldIpv4, ipv6 = oldIpv6, country = country },
                        @new = new { ipv4 = newIpv4, ipv6 = newIpv6, country = country, flag = countryCode },
                        oldIp = oldIpv4 != "unknown" ? oldIpv4 : oldIp,
                        newIp = newIpv4 != "unknown" ? newIpv4 : newIp,
                        oldIpv4,
                        oldIpv6,
                        newIpv4,
                        newIpv6,
                        dnsFlushed,
                        sidecarSynced,
                        message,
                        ip = newIpv4 != "unknown" ? newIpv4 : (newIp != "unknown" ? newIp : null),
                        ipv4 = newIpv4,
                        ipv6 = newIpv6,
                        countryCode,
                        country,
                        countryEmoji,
                        city
                    };

                    EmitLog(logTag, success ? "info" : "error", !string.IsNullOrWhiteSpace(message) ? message : $"{providerName} rotated successfully");
                    if (requestId != null) SendResponse(requestId, success, resultObj);
                    return resultObj;
                }
            }
            catch { }
        }

        // Fallback execution if node script fails
        if (isProton)
        {
            var pRes = await _protonVpnService.RotateServerAsync(msg => EmitLog("ProtonVPN", "info", msg));
            var pFresh = await NetworkHelper.FetchPublicIpDetailsAsync();

            var pObj = new
            {
                success = pRes.Success,
                provider = "ProtonVPN",
                cooldownSeconds = 60,
                old = new { ipv4 = pRes.Old.Ipv4 ?? "unknown", ipv6 = pRes.Old.Ipv6 ?? "Not assigned" },
                @new = new { ipv4 = pRes.New.Ipv4 ?? pFresh.Ip, ipv6 = pRes.New.Ipv6 ?? pFresh.Ipv6 ?? "Not assigned", country = pRes.Country, flag = pRes.CountryCode },
                oldIp = pRes.OldIp,
                newIp = pRes.NewIp,
                oldIpv4 = pRes.Old.Ipv4 ?? "unknown",
                oldIpv6 = pRes.Old.Ipv6 ?? "Not assigned",
                newIpv4 = pRes.New.Ipv4 ?? pFresh.Ip,
                newIpv6 = pRes.New.Ipv6 ?? pFresh.Ipv6 ?? "Not assigned",
                dnsFlushed = true,
                sidecarSynced = _openCodeWatcher?.IsOnline ?? false,
                message = pRes.Message,
                ip = pRes.NewIp,
                ipv4 = pRes.New.Ipv4 ?? pFresh.Ip,
                ipv6 = pRes.New.Ipv6 ?? pFresh.Ipv6 ?? "Not assigned",
                countryCode = pRes.CountryCode,
                country = pRes.Country,
                countryEmoji = pRes.CountryEmoji,
                city = pRes.City
            };

            EmitLog("ProtonVPN", pRes.Success ? "info" : "error", pRes.Message);
            if (requestId != null) SendResponse(requestId, pRes.Success, pObj);
            return pObj;
        }

        var (fallbackSuccess, rotateMsg, oldIps, newIps) = await NetworkHelper.RotateWarpDualStackAsync();
        var freshIpDetails = await NetworkHelper.FetchPublicIpDetailsAsync();

        if (fallbackSuccess && _openCodeWatcher != null)
        {
            _ = _openCodeWatcher.RefreshAsync();
        }

        var fallbackResult = new
        {
            success = fallbackSuccess,
            provider = "CloudflareWARP",
            cooldownSeconds = 12,
            old = new { ipv4 = oldIps.Ipv4 ?? "unknown", ipv6 = oldIps.Ipv6 ?? "Not assigned" },
            @new = new { ipv4 = newIps.Ipv4 ?? freshIpDetails.Ip, ipv6 = newIps.Ipv6 ?? freshIpDetails.Ipv6 ?? "Not assigned", country = freshIpDetails.Country, flag = freshIpDetails.CountryCode },
            oldIp = oldIps.Ipv4 ?? "unknown",
            newIp = newIps.Ipv4 ?? freshIpDetails.Ip,
            oldIpv4 = oldIps.Ipv4 ?? "unknown",
            oldIpv6 = oldIps.Ipv6 ?? "Not assigned",
            newIpv4 = newIps.Ipv4 ?? freshIpDetails.Ip,
            newIpv6 = newIps.Ipv6 ?? freshIpDetails.Ipv6 ?? "Not assigned",
            dnsFlushed = false,
            sidecarSynced = _openCodeWatcher?.IsOnline ?? false,
            message = rotateMsg,
            ip = freshIpDetails.Ip,
            ipv4 = freshIpDetails.Ipv4 ?? freshIpDetails.Ip,
            ipv6 = freshIpDetails.Ipv6 ?? "Not assigned",
            countryCode = freshIpDetails.CountryCode,
            country = freshIpDetails.Country,
            countryEmoji = freshIpDetails.CountryEmoji,
            city = freshIpDetails.City
        };

        EmitLog("WARP", fallbackSuccess ? "info" : "error", rotateMsg);
        if (requestId != null) SendResponse(requestId, fallbackSuccess, fallbackResult);
        return fallbackResult;
    }

    public async Task<object> DisconnectVpnAsync(string? requestId = null)
    {
        EmitLog("System", "info", "Disconnecting active VPN tunnels (WARP / Proton)...");

        string baseDir = AppContext.BaseDirectory;
        string scriptPath = Path.Combine(baseDir, "dist", "fixquota-command.js");
        if (!File.Exists(scriptPath))
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "dist", "fixquota-command.js");
            if (File.Exists(fallback)) scriptPath = fallback;
        }

        bool nodeExecuted = false;
        string outputJson = string.Empty;

        if (File.Exists(scriptPath))
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "node.exe",
                    Arguments = $"\"{scriptPath}\" --disconnect",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                proc.Start();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var readTask = proc.StandardOutput.ReadToEndAsync();
                await Task.WhenAll(readTask, proc.WaitForExitAsync(cts.Token));
                outputJson = await readTask;
                nodeExecuted = true;
            }
            catch { }
        }

        if (nodeExecuted && !string.IsNullOrWhiteSpace(outputJson))
        {
            try
            {
                int start = outputJson.IndexOf('{');
                int end = outputJson.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    var jsonBlock = outputJson.Substring(start, end - start + 1);
                    using var doc = JsonDocument.Parse(jsonBlock);
                    var root = doc.RootElement;
                    bool success = root.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
                    var directIp = root.TryGetProperty("newIp", out var niProp) ? niProp.GetString() : "unknown";
                    var country = root.TryGetProperty("country", out var cProp) ? cProp.GetString() : "Direct ISP";

                    var res = new
                    {
                        success,
                        provider = "Direct",
                        message = $"Disconnected from VPN. Network returned to direct ISP connection ({directIp}).",
                        newIp = directIp,
                        country,
                        countryEmoji = root.TryGetProperty("countryEmoji", out var ceProp) ? ceProp.GetString() : "🌐",
                        countryCode = root.TryGetProperty("countryCode", out var ccProp) ? ccProp.GetString() : "",
                        ipv4 = root.TryGetProperty("newIpv4", out var n4) ? n4.GetString() : directIp,
                        ipv6 = root.TryGetProperty("newIpv6", out var n6) ? n6.GetString() : "Not assigned"
                    };

                    EmitLog("System", "info", res.message);
                    if (requestId != null) SendResponse(requestId, true, res);
                    return res;
                }
            }
            catch { }
        }

        // Native fallback disconnect
        await _protonVpnService.DisconnectAsync();
        await NetworkHelper.DisconnectWarpAsync();
        await NetworkHelper.FlushDnsAsync();
        var freshDetails = await NetworkHelper.FetchPublicIpDetailsAsync();

        if (_openCodeWatcher != null)
        {
            _ = _openCodeWatcher.RefreshAsync();
        }

        var fallbackDisconnect = new
        {
            success = true,
            provider = "Direct",
            message = $"VPN disconnected. Direct network restored to {freshDetails.Ip} ({freshDetails.Country}).",
            newIp = freshDetails.Ip,
            country = freshDetails.Country,
            countryEmoji = freshDetails.CountryEmoji,
            countryCode = freshDetails.CountryCode,
            ipv4 = freshDetails.Ipv4 ?? freshDetails.Ip,
            ipv6 = freshDetails.Ipv6 ?? "Not assigned"
        };

        EmitLog("System", "info", fallbackDisconnect.message);
        if (requestId != null) SendResponse(requestId, true, fallbackDisconnect);
        return fallbackDisconnect;
    }

    private object[] GetProfilesWithEmail()
    {
        return _cliManager.ProfileService.GetProfiles()
            .Select(name => (object)new
            {
                name,
                email = _cliManager.ProfileService.GetAccountEmail(name) ?? string.Empty,
                isRunning = _cliManager.IsProfileRunning(name),
                port = _cliManager.GetPortForProfile(name)
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
