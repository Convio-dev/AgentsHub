#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentsHub.Helpers;
using AgentsHub.Models;
#if WINDOWS
using System.Windows.Forms;
using AgentsHub.Forms;
using Microsoft.Web.WebView2.Core;
#endif

namespace AgentsHub.Services;

public sealed partial class DashboardWebBridge : IDisposable
{
#if WINDOWS
    private readonly Form? _mainForm;
    private CoreWebView2? _coreWebView;
    private CliWebviewForm? _cliWebviewForm;
    private readonly System.Windows.Forms.Timer _streamFlushTimer;
    private readonly System.Windows.Forms.Timer _metricsTimer;
#else
    private readonly object? _mainForm;
    private readonly System.Threading.Timer _streamFlushTimer;
    private readonly System.Threading.Timer _metricsTimer;
#endif

    private readonly CliManagerService _cliManager;
    private readonly LanProxyService? _lanProxyService;
    private readonly OpenCodeWatcherService? _openCodeWatcher;

    public event Action<string>? WebMessageBroadcast;

    // Debounced terminal output buffers, keyed by session id. A CliType key collapses every
    // Antigravity profile onto CliType.AntigravityCli, so a profile session's output was either
    // shown under the built-in tab or dropped entirely.
    private readonly Lock _streamLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.StringBuilder> _terminalStreamBuffers = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _promptEnhanceCts;

    // The firewall probe spawns PowerShell, so the result is cached briefly. The LAN view can
    // open several times in a row and each probe would otherwise cost a process launch.
    private FirewallStatus? _cachedFirewall;
    private DateTime _cachedFirewallAt = DateTime.MinValue;
    private readonly SemaphoreSlim _firewallGate = new(1, 1);
    private const int FirewallCacheMs = 20000;

    // Smart Cooldown rate limiter for VPN quota reset (backend guard)
    private static DateTime _lastQuotaResetTime = DateTime.MinValue;
    private static readonly Lock _quotaResetLock = new();

    // Cached so payload parsing does not allocate a new options instance on every message.
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly ProtonVpnService _protonVpnService;

#if WINDOWS
    public DashboardWebBridge(Form? mainForm, CliManagerService cliManager, LanProxyService? lanProxyService, OpenCodeWatcherService? openCodeWatcher = null)
#else
    public DashboardWebBridge(object? mainForm, CliManagerService cliManager, LanProxyService? lanProxyService, OpenCodeWatcherService? openCodeWatcher = null)
#endif
    {
        _mainForm = mainForm;
        _cliManager = cliManager;
        _lanProxyService = lanProxyService;
        _openCodeWatcher = openCodeWatcher;
        _protonVpnService = new ProtonVpnService(_openCodeWatcher);

#if WINDOWS
        // Initialize debounced terminal stream timer (flushes every 20ms)
        _streamFlushTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _streamFlushTimer.Tick += OnStreamFlushTimerTick;
        _streamFlushTimer.Start();

        // Metrics timer (CPU / RAM updates every 2.5s)
        _metricsTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _metricsTimer.Tick += OnMetricsTimerTick;
        _metricsTimer.Start();
#else
        _streamFlushTimer = new System.Threading.Timer(_ => OnStreamFlushTimerTick(null, EventArgs.Empty), null, 20, 20);
        _metricsTimer = new System.Threading.Timer(_ => OnMetricsTimerTick(null, EventArgs.Empty), null, 2500, 2500);
#endif

        HookBackendEvents();
    }

#if WINDOWS
    public void AttachWebView(CoreWebView2 coreWebView)
    {
        _coreWebView = coreWebView;
        _coreWebView.WebMessageReceived += OnWebMessageReceived;

        if (_openCodeWatcher != null)
        {
            var stateDto = _openCodeWatcher.GetStateDto();
            PostOpenCodeUpdate(stateDto);
        }

        // Warm up firewall check in background at launch so cache is ready without blocking the UI
        _ = Task.Run(async () =>
        {
            try
            {
                var req = GetRequiredLanPorts();
                await GetFirewallStatusAsync(req, force: false).ConfigureAwait(false);
            }
            catch
            {
                // Background warmup failure is non-fatal
            }
        });

        // Silent background update check after 6 seconds
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(6000).ConfigureAwait(false);
                var update = await UpdateService.Instance.CheckForUpdatesAsync().ConfigureAwait(false);
                if (update.IsUpdateAvailable)
                {
                    PostEventToUi("update_available", update);
                }
            }
            catch
            {
                // Silent check failure
            }
        });
    }
#endif

    private void HookBackendEvents()
    {
        _cliManager.StatusChanged += (type, state, msg) =>
        {
            UpdateDiscordRpcAgents();
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
            UpdateDiscordRpcAgents();
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

        _cliManager.TerminalDataReceivedId += (id, data) =>
        {
            // Append incoming stream data into debounced buffer
            lock (_streamLock)
            {
                _terminalStreamBuffers.GetOrAdd(id, _ => new System.Text.StringBuilder()).Append(data);
            }
        };

        if (_lanProxyService != null)
        {
            // The device list was already being tracked but never reached the UI.
            _lanProxyService.ActiveClientsChanged += clients =>
            {
                PostEventToUi("lan_clients_changed", new { clients = ToClientDtos(clients) });
            };

            _lanProxyService.StateChanged += running =>
            {
                // A bind can fail, so the UI re-reads the real state rather than trusting the toggle.
                InvalidateFirewallCache();
                PostEventToUi("lan_state_changed", new
                {
                    running,
                    bindError = _lanProxyService.LastBindError ?? ""
                });
            };
        }

        FileWatchdogService.Instance.FileChanged += (fileEvent) =>
        {
            PostEventToUi("workspace_file_changed", fileEvent);
        };

        CustomCliService.Instance.CustomClisChanged += () =>
        {
            PostEventToUi("custom_clis_changed", new
            {
                clis = CustomCliService.Instance.GetCustomClis()
            });
        };

        var curWs = AiConfigService.Instance.GetActiveWorkspace();
        if (curWs != null && !string.IsNullOrWhiteSpace(curWs.WorkingDirectory))
        {
            FileWatchdogService.Instance.WatchDirectory(curWs.WorkingDirectory);
        }

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

        ProxyTelemetryService.Instance.TelemetryUpdated += () =>
        {
            PostEventToUi("token_telemetry_updated", ProxyTelemetryService.Instance.GetSummary());
        };

        // Initialize automated agent CLI completion and Discord webhook dispatcher
        AgentCompletionWatcherService.Instance.Initialize(_cliManager, (evtName, payload) =>
        {
            PostEventToUi(evtName, payload);
        });

        // Initialize Pocket Commander mobile remote bot gateway
        PocketCommanderService.Instance.Initialize(_cliManager);

        // Initialize Git Repository Manager progress and state events
        GitRepositoryManagerService.Instance.ScanProgressChanged += (progress) =>
        {
            PostEventToUi("repo_scan_progress", progress);
        };

        GitRepositoryManagerService.Instance.RepositoriesChanged += () =>
        {
            PostEventToUi("repos_updated", new
            {
                repositories = GitRepositoryManagerService.Instance.GetRepositories()
            });
        };

        DiscordRpcService.Instance.StatusChanged += (status) =>
        {
            PostEventToUi("discord_rpc_status_changed", status);
        };

        PocketCommanderService.Instance.StatusChanged += (evt, payload) =>
        {
            PostEventToUi("pocket_commander_status_changed", new { evt, payload });
        };

        PocketCommanderService.Instance.CommandReceived += (cmd, user) =>
        {
            PostEventToUi("pocket_commander_command_received", new { cmd, user, timestamp = DateTime.UtcNow });
        };

        // Wire native Bun Sidecar events directly to WebView2 UI
        // Interactive install console: stream output and exit straight to the webview.
        NpxInstallHost.OutputReceived += text => PostEventToUi("npx_install_data", new { text });
        NpxInstallHost.Exited += code => PostEventToUi("npx_install_exited", new { exitCode = code });

        Sidecar.SidecarHostService.Instance.EventDispatched += (evt, payload) =>
        {
            if (payload != null)
            {
                PostEventToUi(evt, payload);
            }
        };
    }

    private void UpdateDiscordRpcAgents()
    {
        try
        {
            int count = 0;
            string primaryName = "Claude Code";
            if (_cliManager.GetInfo(CliType.ClaudeCode).State == CliState.Running)
            {
                count++;
                primaryName = "Claude Code";
            }
            if (_cliManager.GetInfo(CliType.AntigravityCli).State == CliState.Running)
            {
                count++;
                primaryName = "Antigravity";
            }
            if (_cliManager.GetInfo(CliType.Hermes).State == CliState.Running)
            {
                count++;
            }
            var runningProfiles = _cliManager.GetRunningAntigravityProfiles();
            count += runningProfiles.Count;

            DiscordRpcService.Instance.SetActiveAgents(count, primaryName);
        }
        catch { }
    }

    private void OnStreamFlushTimerTick(object? sender, EventArgs e)
    {
#if WINDOWS
        if (_coreWebView == null && WebMessageBroadcast == null) return;
#else
        if (WebMessageBroadcast == null) return;
#endif

        lock (_streamLock)
        {
            foreach (var (sessionId, sb) in _terminalStreamBuffers)
            {
                if (sb.Length > 0)
                {
                    var chunk = sb.ToString();
                    sb.Clear();

                    PostEventToUi("terminal_data", new
                    {
                        cli = sessionId,
                        data = chunk
                    });
                }
            }
        }
    }

    // The dashboard exposes one Antigravity tab for whichever profile is active, while terminal
    // sessions are keyed by profile id. Resolving here keeps history, input, and resize pointed at
    // the session that is actually running instead of at an empty "antigravity_cli" buffer.
    private string ResolveTerminalSessionId(string cli)
    {
        return _cliManager.ResolveSessionId(cli);
    }

    // The dashboard terminal and the standalone workspace show the same sessions, so a tab change
    // in the standalone window is pushed to the dashboard to keep the two views on one CLI.
    private void NotifyStandaloneTabSwitched(string cliId)
    {
        PostEventToUi("terminal_tab_switched", new { cli = AiConfigService.NormalizeCliId(cliId) });
    }

#if WINDOWS
    private CliWebviewForm EnsureStandaloneTerminalForm()
    {
        if (_cliWebviewForm == null || _cliWebviewForm.IsDisposed)
        {
            var workspaces = AiConfigService.Instance.GetCliWorkspaces();
            var firstEnabled = workspaces.FirstOrDefault(w => w.IsEnabled);
            CliType initType = CliType.Hermes;
            if (firstEnabled != null)
            {
                string norm = AiConfigService.NormalizeCliId(firstEnabled.Id);
                if (norm == "claude_code") initType = CliType.ClaudeCode;
                else if (norm == "antigravity_cli") initType = CliType.AntigravityCli;
            }
            _cliWebviewForm = new CliWebviewForm(_cliManager, initType, _mainForm);
            _cliWebviewForm.ActiveTabChanged += NotifyStandaloneTabSwitched;
        }
        else
        {
            _cliWebviewForm.SetMainForm(_mainForm);
        }
        return _cliWebviewForm;
    }
#endif

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

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private void OnMetricsTimerTick(object? sender, EventArgs e)
    {
#if WINDOWS
        if (_coreWebView == null && WebMessageBroadcast == null) return;
#else
        if (WebMessageBroadcast == null) return;
#endif

        // Sampling lives entirely inside GetSystemTelemetryDto, which owns the CPU counter.
        // Reading the counter here as well would re-read the delta back-to-back and pin it at 100%.
        PostEventToUi("hardware_metrics", GetSystemTelemetryDto());
    }

    private static object GetSystemTelemetryDto()
    {
        // Sampling runs on NetworkTelemetryService's own thread; this read is cached and cheap.
        NetworkTelemetryService.EnsureStarted();
        double netKiloBytesPerSec = NetworkTelemetryService.NetworkKiloBytesPerSec;

        long totalRamMb = 16384;
        long usedRamMb = 4096;
        long hermesMemMb = 0;
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
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
            netKbps = Math.Round(netKiloBytesPerSec, 1),
            netMbps = Math.Round(netKiloBytesPerSec / 1024.0, 3),
            ramMb = usedRamMb,
            totalRamMb,
            memoryUsedGb = memUsedGb,
            memoryTotalGb = memTotalGb,
            hermesMemMb
        };
    }

#if WINDOWS
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        await HandleIncomingJsonAsync(e.WebMessageAsJson);
    }
#endif

    public async Task HandleIncomingJsonAsync(string rawJson)
    {
        try
        {
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

            case "send_chat":
                {
                    string sessionId = data.TryGetProperty("sessionId", out var sElem) ? sElem.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                    string prompt = data.TryGetProperty("prompt", out var pElem) ? pElem.GetString() ?? string.Empty : string.Empty;
                    string? model = data.TryGetProperty("model", out var mElem) ? mElem.GetString() : null;
                    string? profile = data.TryGetProperty("profile", out var prElem) ? prElem.GetString() : null;
                    string? cwd = data.TryGetProperty("workingDirectory", out var wdElem) ? wdElem.GetString() : null;
                    string? effort = data.TryGetProperty("reasoningEffort", out var efElem) ? efElem.GetString() : null;

                    string activeFallback = _cliManager.ProfileService.ActiveProfile ?? "default";
                    if (string.IsNullOrWhiteSpace(profile) || string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        profile = activeFallback;
                    }

                    await Sidecar.SidecarHostService.Instance.SendChatAsync(sessionId, prompt, model, profile, cwd, effort).ConfigureAwait(false);
                    SendResponse(requestId, true, new { sent = true, sessionId });
                }
                break;

            // On-demand quota read for one profile. Never touches other profiles: the sidecar polls
            // with that profile's own virtual home, so each account keeps its own pool.
            case "check_profile_quota":
                {
                    string? profile = data.TryGetProperty("profile", out var pqElem) ? pqElem.GetString() : null;
                    if (string.IsNullOrWhiteSpace(profile))
                    {
                        profile = _cliManager.ProfileService.ActiveProfile;
                    }

                    if (string.IsNullOrWhiteSpace(profile) || string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        SendResponse(requestId, false, "No profile is configured to check.");
                        break;
                    }

                    await Sidecar.SidecarHostService.Instance.CheckProfileQuotaAsync(profile).ConfigureAwait(false);
                    SendResponse(requestId, true, new { requested = true, profile });
                }
                break;

            // Model list for the composer's selector. The engine reports its own catalogue; nothing
            // here is a static table.
            case "get_model_catalog":
                {
                    var activeWs = AiConfigService.Instance.GetActiveWorkspace();

                    string? cli = data.TryGetProperty("cli", out var mcElem) ? mcElem.GetString() : null;
                    if (string.IsNullOrWhiteSpace(cli)) cli = activeWs?.PrimaryCli;

                    var catalog = await ModelCatalogService.Instance.GetCatalogAsync(cli).ConfigureAwait(false);
                    SendResponse(requestId, true, catalog);
                }
                break;

            // Stops the in-flight turn. The sidecar aborts the child process and the session's
            // AbortController; the UI then settles its own state from the session_status event.
            case "abort_task":
                {
                    string? taskId = data.TryGetProperty("taskId", out var tElem) ? tElem.GetString() : null;
                    if (string.IsNullOrWhiteSpace(taskId) && data.TryGetProperty("sessionId", out var sElem2))
                    {
                        taskId = sElem2.GetString();
                    }

                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        SendResponse(requestId, false, "taskId or sessionId is required");
                        break;
                    }

                    string? reason = data.TryGetProperty("reason", out var rElem) ? rElem.GetString() : null;
                    await Sidecar.SidecarHostService.Instance.AbortTaskAsync(taskId, reason).ConfigureAwait(false);
                    SendResponse(requestId, true, new { aborted = true, taskId });
                }
                break;

            // Slash command discovery for the composer's palette. The CLI and directory come from the
            // request when the UI knows them, otherwise from the active workspace.
            case "get_slash_commands":
                {
                    var activeWs = AiConfigService.Instance.GetActiveWorkspace();

                    string? cli = data.TryGetProperty("cli", out var cElem) ? cElem.GetString() : null;
                    if (string.IsNullOrWhiteSpace(cli)) cli = activeWs?.PrimaryCli;

                    string? cwd = data.TryGetProperty("cwd", out var dElem) ? dElem.GetString() : null;
                    if (string.IsNullOrWhiteSpace(cwd)) cwd = activeWs?.WorkingDirectory;

                    var profiles = _cliManager.ProfileService.GetProfiles();
                    var commands = await SlashCommandService.Instance.GetCommandsAsync(cli, cwd, profiles).ConfigureAwait(false);

                    SendResponse(requestId, true, new { commands, engine = cli });
                }
                break;

            case "switch_account":
                {
                    string activeFallback = _cliManager.ProfileService.ActiveProfile ?? "default";
                    string profile = data.TryGetProperty("profile", out var pElem) ? pElem.GetString() ?? activeFallback : activeFallback;
                    if (string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        profile = activeFallback;
                    }
                    string? provider = data.TryGetProperty("provider", out var prElem) ? prElem.GetString() : null;

                    await Sidecar.SidecarHostService.Instance.SwitchAccountAsync(profile, provider).ConfigureAwait(false);
                    SendResponse(requestId, true, new { success = true, profile });
                }
                break;

            case "list_accounts":
                {
                    await Sidecar.SidecarHostService.Instance.ListAccountsAsync().ConfigureAwait(false);
                    SendResponse(requestId, true, new { requested = true });
                }
                break;

            case "refresh_token":
                {
                    string profile = data.TryGetProperty("profile", out var pElem) ? pElem.GetString() ?? "Default" : "Default";
                    await Sidecar.SidecarHostService.Instance.RefreshTokenAsync(profile).ConfigureAwait(false);
                    SendResponse(requestId, true, new { requested = true, profile });
                }
                break;

            case "get_sidecar_status":
                {
                    SendResponse(requestId, true, Sidecar.SidecarHostService.Instance.GetStatusDto());
                }
                break;

            case "get_system_telemetry":
                SendResponse(requestId, true, GetSystemTelemetryDto());
                break;

            case "check_for_updates":
                {
                    var updateInfo = await UpdateService.Instance.CheckForUpdatesAsync().ConfigureAwait(false);
                    SendResponse(requestId, true, new
                    {
                        result = updateInfo.Result.ToString(),
                        isUpdateAvailable = updateInfo.IsUpdateAvailable,
                        latestVersion = updateInfo.LatestVersion,
                        currentVersion = updateInfo.CurrentVersion,
                        releaseName = updateInfo.ReleaseName,
                        releaseNotes = updateInfo.ReleaseNotes,
                        downloadUrl = updateInfo.DownloadUrl,
                        assetSizeBytes = updateInfo.AssetSizeBytes,
                        publishedAtUtc = updateInfo.PublishedAtUtc
                    });
                }
                break;

            case "install_update":
                {
                    string? dlUrl = data.TryGetProperty("downloadUrl", out var dlProp) ? dlProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(dlUrl))
                    {
                        SendResponse(requestId, false, "Download URL is required");
                        break;
                    }

                    SendResponse(requestId, true, new { status = "downloading" });

                    var progress = new Progress<double>(p =>
                    {
                        PostEventToUi("update_download_progress", new { progress = p });
                    });

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await UpdateService.Instance.DownloadAndInstallAsync(dlUrl, progress).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            PostEventToUi("update_download_failed", new { error = ex.Message });
                        }
                    });
                }
                break;

            case "get_mcp_and_skills":
                {
                    var servers = await McpSkillsService.Instance.GetMcpServersAsync().ConfigureAwait(false);
                    var activeWs = AiConfigService.Instance.GetActiveWorkspace();
                    var skills = await McpSkillsService.Instance.GetAgentSkillsAsync(activeWs?.WorkingDirectory).ConfigureAwait(false);
                    SendResponse(requestId, true, new { servers, skills });
                }
                break;

            case "save_mcp_server":
                {
                    if (data.TryGetProperty("server", out var sProp))
                    {
                        var s = JsonSerializer.Deserialize<McpServerItem>(sProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (s != null)
                        {
                            bool saved = await McpSkillsService.Instance.SaveMcpServerAsync(s).ConfigureAwait(false);
                            SendResponse(requestId, true, new { success = saved });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid MCP server payload");
                }
                break;

            case "delete_mcp_server":
                {
                    string? id = data.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id))
                    {
                        bool deleted = await McpSkillsService.Instance.DeleteMcpServerAsync(id).ConfigureAwait(false);
                        SendResponse(requestId, true, new { success = deleted });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Server ID is required");
                    }
                }
                break;

            case "toggle_skill":
                {
                    string? dir = data.TryGetProperty("directoryPath", out var dProp) ? dProp.GetString() : null;
                    bool enabled = data.TryGetProperty("enabled", out var eProp) && eProp.ValueKind == JsonValueKind.True;

                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        SendResponse(requestId, false, "Skill directory is required");
                        break;
                    }

                    bool ok = McpSkillsService.SetSkillEnabled(dir, enabled);
                    var activeWs = AiConfigService.Instance.GetActiveWorkspace();
                    var skills = ok
                        ? await McpSkillsService.Instance.GetAgentSkillsAsync(activeWs?.WorkingDirectory).ConfigureAwait(false)
                        : null;

                    SendResponse(requestId, ok, new { success = ok, skills });
                }
                break;

            case "save_mcp_auth":
                {
                    string? serverName = data.TryGetProperty("serverName", out var sProp) ? sProp.GetString() : null;
                    string? keyName = data.TryGetProperty("keyName", out var kProp) ? kProp.GetString() : null;
                    string? keyValue = data.TryGetProperty("keyValue", out var vProp) ? vProp.GetString() : null;

                    if (!string.IsNullOrEmpty(serverName) && !string.IsNullOrEmpty(keyName))
                    {
                        bool saved = await McpSkillsService.Instance.SaveMcpAuthKeyAsync(serverName, keyName, keyValue ?? "").ConfigureAwait(false);
                        var servers = await McpSkillsService.Instance.GetMcpServersAsync().ConfigureAwait(false);
                        SendResponse(requestId, true, new { success = saved, servers });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Server name and key name are required");
                    }
                }
                break;

            case "test_mcp_auth":
                {
                    string? serverName = data.TryGetProperty("serverName", out var sProp) ? sProp.GetString() : null;
                    string? keyValue = data.TryGetProperty("keyValue", out var vProp) ? vProp.GetString() : null;

                    var testRes = await McpSkillsService.Instance.TestMcpAuthAsync(serverName ?? "", keyValue ?? "").ConfigureAwait(false);
                    SendResponse(requestId, true, testRes);
                }
                break;

            case "get_skill_content":
                {
                    string? skillPath = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                    if (!string.IsNullOrEmpty(skillPath))
                    {
                        var (content, name, description, scope, tokenCount) = await McpSkillsService.Instance.GetSkillContentAsync(skillPath).ConfigureAwait(false);
                        SendResponse(requestId, true, new { content, name, description, scope, tokenCount, path = skillPath });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Path is required");
                    }
                }
                break;

            case "save_skill_content":
                {
                    string? skillPath = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                    string? name = data.TryGetProperty("name", out var nProp) ? nProp.GetString() : "skill";
                    string? description = data.TryGetProperty("description", out var dProp) ? dProp.GetString() : "";
                    string? content = data.TryGetProperty("content", out var cProp) ? cProp.GetString() : "";

                    if (!string.IsNullOrEmpty(skillPath))
                    {
                        bool saved = await McpSkillsService.Instance.SaveSkillContentAsync(skillPath, name ?? "skill", description ?? "", content ?? "").ConfigureAwait(false);
                        SendResponse(requestId, saved, new { success = saved });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Path is required");
                    }
                }
                break;

            case "create_project_skill":
                {
                    string? wsId = data.TryGetProperty("workspaceId", out var wProp) ? wProp.GetString() : null;
                    string? name = data.TryGetProperty("name", out var nProp) ? nProp.GetString() : "custom-skill";
                    string? description = data.TryGetProperty("description", out var dProp) ? dProp.GetString() : "Agent capability skill";
                    string? content = data.TryGetProperty("content", out var cProp) ? cProp.GetString() : "";
                    string? skillScope = data.TryGetProperty("scope", out var skillScopeProp) ? skillScopeProp.GetString() : null;

                    var skill = await McpSkillsService.Instance.CreateProjectSkillAsync(
                        wsId, name ?? "custom-skill", description ?? "", content ?? "", skillScope).ConfigureAwait(false);
                    if (skill != null)
                    {
                        SendResponse(requestId, true, new { success = true, skill });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Failed to create project skill");
                    }
                }
                break;

            case "fork_skill_to_workspace":
                {
                    string? sourcePath = data.TryGetProperty("sourcePath", out var spProp) ? spProp.GetString() : null;
                    string? targetWsId = data.TryGetProperty("workspaceId", out var twProp) ? twProp.GetString() : null;

                    if (!string.IsNullOrEmpty(sourcePath))
                    {
                        var skill = await McpSkillsService.Instance.ForkSkillToWorkspaceAsync(sourcePath, targetWsId).ConfigureAwait(false);
                        if (skill != null)
                        {
                            SendResponse(requestId, true, new { success = true, skill });
                        }
                        else
                        {
                            SendResponse(requestId, false, "Failed to fork skill to workspace");
                        }
                    }
                    else
                    {
                        SendResponse(requestId, false, "Source path is required");
                    }
                }
                break;

            case "delete_project_skill":
                {
                    string? skillPath = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                    if (!string.IsNullOrEmpty(skillPath))
                    {
                        bool deleted = McpSkillsService.Instance.DeleteProjectSkill(skillPath);
                        SendResponse(requestId, deleted, new { success = deleted });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Path is required");
                    }
                }
                break;

            case "run_npx_command":
                {
                    string? command = data.TryGetProperty("command", out var cProp) ? cProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        SendResponse(requestId, false, "A command is required");
                        break;
                    }

                    // Already running: report it instead of silently replacing the user's session.
                    if (NpxInstallHost.IsRunning)
                    {
                        SendResponse(requestId, false, $"'{NpxInstallHost.CurrentCommand}' is still running");
                        break;
                    }

                    // Scope decides where the installer runs, and therefore what it writes to:
                    // "global" targets the user profile, "workspace" targets the active project.
                    string? scope = data.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() : null;
                    bool isGlobal = string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase);

                    string? cwd = isGlobal
                        ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                        : AiConfigService.Instance.GetActiveWorkspace()?.WorkingDirectory;

                    bool started = NpxInstallHost.Start(command, string.IsNullOrWhiteSpace(cwd) ? null : cwd);

                    // A failure must come back as text: an object payload reaches the UI as
                    // "[object Object]", which is exactly the useless error that was showing.
                    if (!started)
                    {
                        SendResponse(requestId, false, "Could not start the install console.");
                        break;
                    }

                    SendResponse(requestId, true, new { started = true, command });
                }
                break;

            case "send_npx_input":
                {
                    string? input = data.TryGetProperty("input", out var iProp) ? iProp.GetString() : null;
                    NpxInstallHost.Write(input ?? string.Empty);
                    SendResponse(requestId, true, new { sent = true });
                }
                break;

            case "start_shell":
                {
                    if (NpxInstallHost.IsRunning)
                    {
                        SendResponse(requestId, false, "A console session is already running.");
                        break;
                    }

                    string? cwd = data.TryGetProperty("cwd", out var cwdProp) ? cwdProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(cwd))
                    {
                        cwd = AiConfigService.Instance.GetActiveWorkspace()?.WorkingDirectory;
                    }

                    string shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
                    bool started = NpxInstallHost.StartRaw(shell, string.IsNullOrWhiteSpace(cwd) ? null : cwd);

                    if (!started)
                    {
                        SendResponse(requestId, false, "Could not start the terminal.");
                        break;
                    }

                    SendResponse(requestId, true, new { started = true, cwd, shell });
                }
                break;

            case "send_shell_input":
                {
                    string? input = data.TryGetProperty("input", out var inputProp) ? inputProp.GetString() : null;
                    NpxInstallHost.Write(input ?? string.Empty);
                    SendResponse(requestId, true, new { sent = true });
                }
                break;

            case "stop_shell":
                NpxInstallHost.Stop();
                SendResponse(requestId, true, new { stopped = true });
                break;

            case "stop_npx_command":
                NpxInstallHost.Stop();
                SendResponse(requestId, true, new { stopped = true });
                break;

            case "get_preference_debug":
                {
                    // Reports what is actually in the preference store, so a UI that disagrees with
                    // the registry can be diagnosed instead of guessed at.
                    var keys = new[]
                    {
                        "StartMinimized", "MinimizeToTray", "WindowsNotifications",
                        "GlobalHotkey", "GlobalHotkeyModifier", "GlobalHotkeyKey",
                        "GlobalHudHotkey", "HudHotkeyModifier", "HudHotkeyKey",
                        "EmbedShellsInTerminalWorkspace", "HideMainOnCliOpen"
                    };

                    var values = new Dictionary<string, string>();
                    foreach (var key in keys)
                    {
                        values[key] = key is "GlobalHotkeyModifier" or "GlobalHotkeyKey"
                            or "HudHotkeyModifier" or "HudHotkeyKey"
                            ? StartupHelper.GetStringPreference(key, "(unset)")
                            : StartupHelper.GetPreference(key, false).ToString();
                    }

                    SendResponse(requestId, true, new { values });
                }
                break;

            case "open_mcp_config":
                {
                    // The servers the UI lists come from the CLI's own config, so that is the file to
                    // open — the AgentsHub copy is only a fallback.
                    var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var candidates = new[]
                    {
                        Path.Combine(userHome, ".gemini", "config", "mcp_config.json"),
                        Path.Combine(userHome, ".gemini", "antigravity", "mcp_config.json")
                    };

                    string? configPath = candidates.FirstOrDefault(File.Exists);
                    if (configPath == null)
                    {
                        SendResponse(requestId, false, "No MCP config file found to open.");
                        break;
                    }

                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(configPath)
                        {
                            UseShellExecute = true
                        });
                        SendResponse(requestId, true, new { opened = true, path = configPath });
                    }
                    catch (Exception ex)
                    {
                        SendResponse(requestId, false, $"Could not open the config: {ex.Message}");
                    }
                }
                break;

            case "reveal_skill_in_explorer":
                {
                    string? path = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                    bool opened = !string.IsNullOrEmpty(path) && McpSkillsService.RevealInExplorer(path);
                    SendResponse(requestId, opened, new { success = opened });
                }
                break;

            case "open_skill_in_editor":
                {
                    string? path = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                    bool opened = !string.IsNullOrEmpty(path) && McpSkillsService.OpenInEditor(path);
                    SendResponse(requestId, opened, new { success = opened });
                }
                break;

            case "get_agent_sessions":
                {
                    string? wsDir = data.TryGetProperty("workspaceDir", out var wdProp) ? wdProp.GetString() : null;
                    var sessions = await TranscriptInspectorService.Instance.GetSessionsAsync(wsDir).ConfigureAwait(false);
                    SendResponse(requestId, true, new { sessions });
                }
                break;

            case "get_session_steps":
            case "get_transcript_steps":
                {
                    string? transcriptPath = data.TryGetProperty("transcriptPath", out var tpProp) ? tpProp.GetString() : null;
                    if (!string.IsNullOrEmpty(transcriptPath))
                    {
                        var steps = await TranscriptInspectorService.Instance.GetSessionStepsAsync(transcriptPath).ConfigureAwait(false);
                        SendResponse(requestId, true, new { steps });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Transcript path is required");
                    }
                }
                break;

            case "delete_agent_session":
                {
                    string? transcriptPath = data.TryGetProperty("transcriptPath", out var delTpProp) ? delTpProp.GetString() : null;
                    if (!string.IsNullOrEmpty(transcriptPath))
                    {
                        bool deleted = TranscriptInspectorService.Instance.DeleteSession(transcriptPath);
                        SendResponse(requestId, deleted, new { success = deleted });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Transcript path is required");
                    }
                }
                break;

            case "get_workspace_snapshots":
                {
                    string? wsId = data.TryGetProperty("workspaceId", out var snapWsProp) ? snapWsProp.GetString() : null;
                    var snapshots = await WorkspaceSnapshotService.Instance.GetSnapshotsAsync(wsId).ConfigureAwait(false);
                    SendResponse(requestId, true, new { snapshots });
                }
                break;

            case "create_workspace_snapshot":
                {
                    string? wsId = data.TryGetProperty("workspaceId", out var crSnapWsProp) ? crSnapWsProp.GetString() : null;
                    string? title = data.TryGetProperty("title", out var tProp) ? tProp.GetString() : "Manual Snapshot";
                    string? trigger = data.TryGetProperty("trigger", out var trProp) ? trProp.GetString() : "Manual";

                    var snapshot = await WorkspaceSnapshotService.Instance.CreateSnapshotAsync(wsId, title ?? "Manual Snapshot", trigger ?? "Manual").ConfigureAwait(false);
                    if (snapshot != null)
                    {
                        SoundpackAudioService.Instance.PlaySoundEvent("checkpoint_created", "modern", 50);
                        SendResponse(requestId, true, new { success = true, snapshot });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Failed to create snapshot");
                    }
                }
                break;

            case "rollback_workspace_snapshot":
                {
                    string? snapshotId = data.TryGetProperty("snapshotId", out var sIdProp) ? sIdProp.GetString() : null;
                    string? wsId = data.TryGetProperty("workspaceId", out var rbSnapWsProp) ? rbSnapWsProp.GetString() : null;

                    if (!string.IsNullOrEmpty(snapshotId))
                    {
                        var (success, restoredCount) = await WorkspaceSnapshotService.Instance.RollbackSnapshotAsync(snapshotId, wsId).ConfigureAwait(false);
                        if (success)
                        {
                            SoundpackAudioService.Instance.PlaySoundEvent("checkpoint_created", "modern", 60);
                            SendResponse(requestId, true, new { success = true, restoredCount });
                        }
                        else
                        {
                            SendResponse(requestId, false, "Failed to rollback snapshot");
                        }
                    }
                    else
                    {
                        SendResponse(requestId, false, "Snapshot ID is required");
                    }
                }
                break;

            case "delete_workspace_snapshot":
                {
                    string? snapshotId = data.TryGetProperty("snapshotId", out var sIdProp) ? sIdProp.GetString() : null;
                    string? wsId = data.TryGetProperty("workspaceId", out var delWsProp) ? delWsProp.GetString() : null;

                    if (!string.IsNullOrEmpty(snapshotId))
                    {
                        bool deleted = WorkspaceSnapshotService.Instance.DeleteSnapshot(snapshotId, wsId);
                        SendResponse(requestId, deleted, new { success = deleted });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Snapshot ID is required");
                    }
                }
                break;

            case "get_soundpack_config":
                {
                    var cfg = SoundpackAudioService.Instance.GetConfig();
                    SendResponse(requestId, true, cfg);
                }
                break;

            case "save_soundpack_config":
                {
                    if (data.TryGetProperty("config", out var spCfgProp))
                    {
                        var cfg = JsonSerializer.Deserialize<SoundpackAudioConfig>(spCfgProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (cfg != null)
                        {
                            SoundpackAudioService.Instance.SaveConfig(cfg);
                            SendResponse(requestId, true, new { success = true, config = cfg });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid soundpack config payload");
                }
                break;

            case "play_sound_event":
                {
                    string? eventName = data.TryGetProperty("eventName", out var evProp) ? evProp.GetString() : "task_done";
                    string? theme = data.TryGetProperty("theme", out var thProp) ? thProp.GetString() : null;
                    int? volume = data.TryGetProperty("volume", out var volProp) ? volProp.GetInt32() : null;
                    bool force = data.TryGetProperty("force", out var fProp) && fProp.GetBoolean();

                    SoundpackAudioService.Instance.PlaySoundEvent(eventName ?? "task_done", theme, volume, force);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "inject_voice_command":
                {
                    string? text = data.TryGetProperty("text", out var txtProp) ? txtProp.GetString() : null;
                    string? sessionName = data.TryGetProperty("sessionName", out var sNameProp) ? sNameProp.GetString() : null;

                    if (!string.IsNullOrEmpty(text))
                    {
                        bool injected = await VoiceCommanderService.Instance.InjectCommandToActiveCliAsync(_cliManager, text, sessionName).ConfigureAwait(false);
                        SendResponse(requestId, injected, new { success = injected });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Text is required");
                    }
                }
                break;

            case "get_scheduled_automations":
                {
                    string? wsId = data.TryGetProperty("workspaceId", out var sAutoWsProp) ? sAutoWsProp.GetString() : null;
                    var tasks = ScheduledAutomationService.Instance.GetTasks(wsId);
                    SendResponse(requestId, true, new { tasks });
                }
                break;

            case "save_scheduled_automation":
                {
                    if (data.TryGetProperty("task", out var taskProp))
                    {
                        var task = JsonSerializer.Deserialize<ScheduledAutomationTask>(taskProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (task != null)
                        {
                            ScheduledAutomationService.Instance.SaveTask(task);
                            SendResponse(requestId, true, new { success = true, task });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid automation task payload");
                }
                break;

            case "delete_scheduled_automation":
                {
                    string? taskId = data.TryGetProperty("taskId", out var tIdProp) ? tIdProp.GetString() : null;
                    if (!string.IsNullOrEmpty(taskId))
                    {
                        bool deleted = ScheduledAutomationService.Instance.DeleteTask(taskId);
                        SendResponse(requestId, deleted, new { success = deleted });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Task ID is required");
                    }
                }
                break;

            case "trigger_scheduled_automation":
                {
                    string? taskId = data.TryGetProperty("taskId", out var tIdProp) ? tIdProp.GetString() : null;
                    if (!string.IsNullOrEmpty(taskId))
                    {
                        bool triggered = await ScheduledAutomationService.Instance.TriggerTaskNowAsync(taskId).ConfigureAwait(false);
                        SendResponse(requestId, triggered, new { success = triggered });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Task ID is required");
                    }
                }
                break;

            case "audit_workspace_security":
                {
                    string? wsDir = data.TryGetProperty("workspaceDir", out var wsDirProp) ? wsDirProp.GetString() : null;
                    var audit = await PreCommitSecurityService.Instance.AuditWorkspaceAsync(wsDir).ConfigureAwait(false);
                    SendResponse(requestId, true, audit);
                }
                break;

            case "get_remote_webhook_config":
                {
                    var cfg = RemoteWebhookService.Instance.GetConfig();
                    SendResponse(requestId, true, cfg);
                }
                break;

            case "save_remote_webhook_config":
                {
                    if (data.TryGetProperty("config", out var rCfgProp))
                    {
                        var cfg = JsonSerializer.Deserialize<RemoteWebhookConfig>(rCfgProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (cfg != null)
                        {
                            RemoteWebhookService.Instance.SaveConfig(cfg);
                            SendResponse(requestId, true, new { success = true, config = cfg });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid webhook config payload");
                }
                break;

            case "test_remote_webhook":
                {
                    string? title = data.TryGetProperty("title", out var titProp) ? titProp.GetString() : "Test Notification";
                    string? desc = data.TryGetProperty("description", out var descProp) ? descProp.GetString() : "Agents Hub webhook test.";

                    bool sent = await RemoteWebhookService.Instance.SendDiscordNotificationAsync(title ?? "Test", desc ?? "Test").ConfigureAwait(false);
                    SendResponse(requestId, sent, new { success = sent });
                }
                break;

            case "get_pocket_commander_config":
                {
                    var cfg = PocketCommanderService.Instance.GetConfig();
                    SendResponse(requestId, true, cfg);
                }
                break;

            case "save_pocket_commander_config":
                {
                    if (data.TryGetProperty("config", out var pCfgProp))
                    {
                        var cfg = JsonSerializer.Deserialize<PocketCommanderConfig>(pCfgProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (cfg != null)
                        {
                            PocketCommanderService.Instance.SaveConfig(cfg);
                            SendResponse(requestId, true, new { success = true, config = cfg });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid pocket commander config payload");
                }
                break;

            case "test_pocket_commander_token":
                {
                    string token = data.TryGetProperty("token", out var tProp) ? tProp.GetString() ?? "" : "";
                    string platform = data.TryGetProperty("platform", out var pProp) ? pProp.GetString() ?? "telegram" : "telegram";
                    
                    var (success, username, error) = platform.Equals("discord", StringComparison.OrdinalIgnoreCase)
                        ? await PocketCommanderService.Instance.TestDiscordBotConnectionAsync(token).ConfigureAwait(false)
                        : await PocketCommanderService.Instance.TestTelegramBotConnectionAsync(token).ConfigureAwait(false);

                    SendResponse(requestId, success, new { success, username, error, platform });
                }
                break;

            case "generate_pocket_pairing_pin":
                {
                    string platform = data.TryGetProperty("platform", out var pProp) ? pProp.GetString() ?? "telegram" : "telegram";
                    string newPin = PocketCommanderService.Instance.GenerateNewPairingPin(platform);
                    SendResponse(requestId, true, new { pin = newPin, platform });
                }
                break;

            case "unpair_pocket_commander":
                {
                    string platform = data.TryGetProperty("platform", out var pProp) ? pProp.GetString() ?? "all" : "all";
                    PocketCommanderService.Instance.UnpairDevice(platform);
                    var cfg = PocketCommanderService.Instance.GetConfig();
                    SendResponse(requestId, true, new { success = true, config = cfg });
                }
                break;

            case "get_discord_rpc_status":
                {
                    var status = DiscordRpcService.Instance.GetStatus();
                    SendResponse(requestId, true, status);
                }
                break;

            case "save_discord_rpc_settings":
                {
                    if (data.TryGetProperty("settings", out var sProp))
                    {
                        var s = JsonSerializer.Deserialize<DiscordRpcSettings>(sProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (s != null)
                        {
                            await DiscordRpcService.Instance.SaveSettingsAsync(s).ConfigureAwait(false);
                            SendResponse(requestId, true, new { success = true, status = DiscordRpcService.Instance.GetStatus() });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid Discord RPC settings payload");
                }
                break;

            case "toggle_discord_rpc":
                {
                    var status = DiscordRpcService.Instance.GetStatus();
                    var current = status.Settings;
                    bool newEnabled = data.TryGetProperty("enabled", out var enProp) ? enProp.GetBoolean() : !current.Enabled;
                    current.Enabled = newEnabled;
                    await DiscordRpcService.Instance.SaveSettingsAsync(current).ConfigureAwait(false);
                    SendResponse(requestId, true, new { success = true, enabled = newEnabled, status = DiscordRpcService.Instance.GetStatus() });
                }
                break;

            case "test_discord_rpc":
                {
                    await DiscordRpcService.Instance.UpdateActivityAsync().ConfigureAwait(false);
                    SendResponse(requestId, true, DiscordRpcService.Instance.GetStatus());
                }
                break;

            case "save_timelapse_clip":
                {
                    string? base64 = data.TryGetProperty("base64Data", out var b64Prop) ? b64Prop.GetString() : null;
                    string? wsId = data.TryGetProperty("workspaceId", out var tlWsProp) ? tlWsProp.GetString() : null;
                    string? clipName = data.TryGetProperty("clipName", out var cnProp) ? cnProp.GetString() : null;

                    if (!string.IsNullOrEmpty(base64))
                    {
                        var filePath = await TimelapseCaptureService.Instance.SaveTimelapseClipAsync(base64, wsId, clipName).ConfigureAwait(false);
                        SendResponse(requestId, !string.IsNullOrEmpty(filePath), new { success = !string.IsNullOrEmpty(filePath), filePath });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Base64 data is required");
                    }
                }
                break;

            case "get_repo_manager_state":
                {
                    var repos = GitRepositoryManagerService.Instance.GetRepositories();
                    var settings = GitRepositoryManagerService.Instance.GetSettings();
                    var workingProjectId = GitRepositoryManagerService.Instance.GetWorkingProjectId();
                    var isScanning = GitRepositoryManagerService.Instance.IsScanning();
                    var lastProgress = GitRepositoryManagerService.Instance.GetLastProgress();
                    SendResponse(requestId, true, new
                    {
                        repositories = repos,
                        settings,
                        workingProjectId,
                        isScanning,
                        lastProgress
                    });
                }
                break;

            case "scan_repositories":
                {
                    bool started = await GitRepositoryManagerService.Instance.StartScanAsync().ConfigureAwait(false);
                    SendResponse(requestId, true, new { success = started });
                }
                break;

            case "cancel_repo_scan":
                {
                    GitRepositoryManagerService.Instance.CancelScan();
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "save_repo_settings":
                {
                    if (data.TryGetProperty("settings", out var sProp))
                    {
                        var cfg = JsonSerializer.Deserialize<RepositoryManagerSettings>(sProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (cfg != null)
                        {
                            GitRepositoryManagerService.Instance.SaveSettings(cfg);
                            SendResponse(requestId, true, new { success = true, settings = cfg });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid settings payload");
                }
                break;

            case "set_working_project":
                {
                    string? projId = data.TryGetProperty("projectId", out var pProp) ? pProp.GetString() : null;
                    GitRepositoryManagerService.Instance.SetWorkingProject(projId);
                    SendResponse(requestId, true, new { success = true, workingProjectId = projId });
                }
                break;

            case "update_repo_metadata":
                {
                    string? id = data.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id))
                    {
                        string? status = data.TryGetProperty("status", out var repoStatusProp) ? repoStatusProp.GetString() : null;
                        string? focus = data.TryGetProperty("focus", out var fcProp) ? fcProp.GetString() : null;
                        bool? pinned = data.TryGetProperty("pinned", out var pinProp) ? pinProp.GetBoolean() : null;
                        string? notes = data.TryGetProperty("notes", out var ntProp) ? ntProp.GetString() : null;
                        List<string>? tags = data.TryGetProperty("tags", out var repoTagsProp) && repoTagsProp.ValueKind == JsonValueKind.Array
                            ? JsonSerializer.Deserialize<List<string>>(repoTagsProp.GetRawText(), CaseInsensitiveJsonOptions)
                            : null;

                        GitRepositoryManagerService.Instance.UpdateRepositoryMetadata(id, status, focus, pinned, tags, notes);
                        SendResponse(requestId, true, new { success = true });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository ID is required");
                    }
                }
                break;

            case "get_repo_notes":
                {
                    string? repoId = data.TryGetProperty("repoId", out var rIdProp) ? rIdProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoId))
                    {
                        var notes = GitRepositoryManagerService.Instance.GetRepoNotes(repoId);
                        SendResponse(requestId, true, new { notes });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository ID is required");
                    }
                }
                break;

            case "save_repo_notes":
                {
                    string? repoId = data.TryGetProperty("repoId", out var rIdProp) ? rIdProp.GetString() : null;
                    string? notes = data.TryGetProperty("notes", out var ntProp) ? ntProp.GetString() : "";
                    if (!string.IsNullOrEmpty(repoId))
                    {
                        GitRepositoryManagerService.Instance.SaveRepoNotes(repoId, notes ?? "");
                        SendResponse(requestId, true, new { success = true });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository ID is required");
                    }
                }
                break;

            case "get_repo_changes":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var changes = await GitRepositoryManagerService.Instance.GetWorkingTreeChangesAsync(repoPath).ConfigureAwait(false);
                        SendResponse(requestId, true, new { changes });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path is required");
                    }
                }
                break;

            case "get_repo_diff":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    string? relativePath = data.TryGetProperty("relativePath", out var relProp) ? relProp.GetString() : null;
                    bool staged = data.TryGetProperty("staged", out var stgProp) && stgProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(relativePath))
                    {
                        var diff = await GitRepositoryManagerService.Instance.GetFileDiffAsync(repoPath, relativePath, staged).ConfigureAwait(false);
                        SendResponse(requestId, true, diff);
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path and relative file path are required");
                    }
                }
                break;

            case "stage_repo_changes":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    bool unstage = data.TryGetProperty("unstage", out var unstgProp) && unstgProp.GetBoolean();
                    var files = data.TryGetProperty("files", out var fProp) && fProp.ValueKind == JsonValueKind.Array
                        ? JsonSerializer.Deserialize<List<string>>(fProp.GetRawText(), CaseInsensitiveJsonOptions) ?? []
                        : [];

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (success, output) = await GitRepositoryManagerService.Instance.StageChangesAsync(repoPath, files, unstage).ConfigureAwait(false);
                        SendResponse(requestId, success, new { success, output });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path is required");
                    }
                }
                break;

            case "create_repo_commit":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    string? message = data.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : null;
                    bool stageAll = data.TryGetProperty("stageAll", out var saProp) && saProp.GetBoolean();
                    bool pushToRemote = data.TryGetProperty("pushToRemote", out var ptrProp) && ptrProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(message))
                    {
                        var (success, output, pushed, pushOutput) = await GitRepositoryManagerService.Instance.CreateCommitAsync(repoPath, message, stageAll, pushToRemote).ConfigureAwait(false);
                        SendResponse(requestId, success, new { success, output, pushed, pushOutput });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path and commit message are required");
                    }
                }
                break;

            case "repo_network_action":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    string? op = data.TryGetProperty("operation", out var opProp) ? opProp.GetString() : "fetch";
                    string? remote = data.TryGetProperty("remote", out var remProp) ? remProp.GetString() : "origin";
                    string? branch = data.TryGetProperty("branch", out var brProp) ? brProp.GetString() : null;
                    bool setUpstream = data.TryGetProperty("setUpstream", out var suProp) && suProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (success, output) = await GitRepositoryManagerService.Instance.NetworkActionAsync(repoPath, op ?? "fetch", remote ?? "origin", branch, setUpstream).ConfigureAwait(false);
                        SendResponse(requestId, success, new { success, output });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path is required");
                    }
                }
                break;

            case "switch_repo_branch":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    string? branch = data.TryGetProperty("branch", out var brProp) ? brProp.GetString() : null;

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(branch))
                    {
                        var (success, output) = await GitRepositoryManagerService.Instance.SwitchBranchAsync(repoPath, branch).ConfigureAwait(false);
                        SendResponse(requestId, success, new { success, output });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path and branch are required");
                    }
                }
                break;

            case "list_repo_branches":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var branches = await GitRepositoryManagerService.Instance.ListBranchesAsync(repoPath).ConfigureAwait(false);
                        SendResponse(requestId, true, new { branches });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path is required");
                    }
                }
                break;

            case "detect_repo_launchers":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var launchers = GitRepositoryManagerService.Instance.DetectLaunchers(repoPath);
                        SendResponse(requestId, true, new { launchers });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path is required");
                    }
                }
                break;

            case "run_repo_launcher":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath) && data.TryGetProperty("launcher", out var lProp))
                    {
                        var item = JsonSerializer.Deserialize<ProjectLauncherItem>(lProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (item != null)
                        {
                            bool executed = GitRepositoryManagerService.Instance.ExecuteLauncher(repoPath, item);
                            SendResponse(requestId, executed, new { success = executed });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid launcher payload");
                }
                break;

            case "evaluate_repo_health":
                {
                    string? idOrPath = data.TryGetProperty("idOrPath", out var idpProp) ? idpProp.GetString() : null;
                    if (!string.IsNullOrEmpty(idOrPath))
                    {
                        var repo = GitRepositoryManagerService.Instance.GetRepository(idOrPath);
                        if (repo != null)
                        {
                            var health = GitRepositoryManagerService.Instance.EvaluateHealth(repo);
                            SendResponse(requestId, true, health);
                        }
                        else
                        {
                            SendResponse(requestId, false, "Repository not found in registry");
                        }
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository id or path is required");
                    }
                }
                break;

            case "export_repo_inventory":
                {
                    string? format = data.TryGetProperty("format", out var fmtProp) ? fmtProp.GetString() : "markdown";
                    var exported = GitRepositoryManagerService.Instance.ExportInventory(format ?? "markdown");
                    SendResponse(requestId, true, new { format = format ?? "markdown", content = exported });
                }
                break;

            case "open_repo_in_editor":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    string? target = data.TryGetProperty("target", out var tgtProp) ? tgtProp.GetString() : "editor";

                    if (!string.IsNullOrEmpty(repoPath) && Directory.Exists(repoPath))
                    {
                        try
                        {
                            if (target == "explorer")
                            {
                                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{repoPath}\"", UseShellExecute = true });
                                SendResponse(requestId, true, new { success = true, message = "Opened in Explorer" });
                            }
                            else if (target == "terminal")
                            {
                                Process.Start(new ProcessStartInfo { FileName = "powershell.exe", WorkingDirectory = repoPath, UseShellExecute = true });
                                SendResponse(requestId, true, new { success = true, message = "Opened in Terminal" });
                            }
                            else
                            {
                                string? specificEditor = (string.Equals(target, "editor", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "ask", StringComparison.OrdinalIgnoreCase)) ? null : target;
                                var (success, msg) = GitRepositoryManagerService.Instance.OpenInEditor(repoPath, specificEditor);
                                SendResponse(requestId, success, new { success, message = msg });
                            }
                        }
                        catch (Exception ex)
                        {
                            SendResponse(requestId, false, $"Failed to launch {target}: {ex.Message}");
                        }
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path is required or does not exist");
                    }
                }
                break;

            case "browse_editor_path":
                {
                    string? chosenPath = null;
#if WINDOWS
                    if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                    {
                        f.Invoke(() =>
                        {
                            using var ofd = new OpenFileDialog
                            {
                                Title = "Select External Code Editor Executable",
                                Filter = "Executable files (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*",
                                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                            };
                            if (ofd.ShowDialog(f) == DialogResult.OK)
                            {
                                chosenPath = ofd.FileName;
                            }
                        });
                    }
#endif

                    if (!string.IsNullOrWhiteSpace(chosenPath))
                    {
                        SendResponse(requestId, true, new { path = chosenPath });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Selection cancelled");
                    }
                }
                break;

            case "get_installed_editors":
                {
                    var editors = GitRepositoryManagerService.Instance.GetInstalledEditors();
                    var preferred = GitRepositoryManagerService.Instance.GetSettings().PreferredEditor;
                    SendResponse(requestId, true, new { editors, preferred });
                }
                break;

            case "launch_agent_in_repo":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rPathProp) ? rPathProp.GetString() : null;
                    string? cli = data.TryGetProperty("cliType", out var targetCliProp) ? targetCliProp.GetString() : "claude";

                    if (!string.IsNullOrEmpty(repoPath) && Directory.Exists(repoPath))
                    {
                        try
                        {
                            var cmd = cli?.ToLowerInvariant() switch
                            {
                                "antigravity" => "agy",
                                "claude" => "claude",
                                _ => "claude"
                            };

                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/k \"cd /d \"{repoPath}\" && {cmd}\"",
                                UseShellExecute = true
                            });

                            SendResponse(requestId, true, new { success = true });
                        }
                        catch (Exception ex)
                        {
                            SendResponse(requestId, false, $"Failed to start agent in repo: {ex.Message}");
                        }
                    }
                    else
                    {
                        SendResponse(requestId, false, "Repository path is required");
                    }
                }
                break;

            case "get_commit_graph":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    int maxCount = data.TryGetProperty("maxCount", out var mProp) && mProp.TryGetInt32(out var mc) ? mc : 100;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var graph = await GitRepositoryManagerService.Instance.GetCommitGraphAsync(repoPath, maxCount).ConfigureAwait(false);
                        SendResponse(requestId, true, new { commits = graph });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "get_commit_diff":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? hash = data.TryGetProperty("commitHash", out var hProp) ? hProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(hash))
                    {
                        var diff = await GitRepositoryManagerService.Instance.GetCommitDiffDetailAsync(repoPath, hash).ConfigureAwait(false);
                        SendResponse(requestId, diff != null, diff ?? (object)new { error = "Commit not found" });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and commitHash are required");
                    }
                }
                break;

            case "revert_commit":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? commitHash = data.TryGetProperty("commitHash", out var hProp) ? hProp.GetString() : null;
                    string mode = data.TryGetProperty("mode", out var mProp) ? mProp.GetString() ?? "new_branch" : "new_branch";
                    string? newBranchName = data.TryGetProperty("newBranchName", out var nbProp) ? nbProp.GetString() : null;

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(commitHash))
                    {
                        var (ok, msg, newHash) = await GitRepositoryManagerService.Instance.RevertCommitAsync(repoPath, commitHash, mode, newBranchName).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg, newCommitHash = newHash });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and commitHash are required");
                    }
                }
                break;

            case "create_branch_at_commit":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? commitHash = data.TryGetProperty("commitHash", out var hProp) ? hProp.GetString() : null;
                    string? branchName = data.TryGetProperty("branchName", out var bProp) ? bProp.GetString() : null;
                    bool checkout = !data.TryGetProperty("checkout", out var cProp) || cProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(commitHash) && !string.IsNullOrEmpty(branchName))
                    {
                        var (ok, msg) = await GitRepositoryManagerService.Instance.CreateBranchAtCommitAsync(repoPath, commitHash, branchName, checkout).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath, commitHash, and branchName are required");
                    }
                }
                break;

            case "get_worktrees":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var wts = await GitRepositoryManagerService.Instance.GetWorktreesAsync(repoPath).ConfigureAwait(false);
                        SendResponse(requestId, true, new { worktrees = wts });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "add_worktree":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? worktreePath = data.TryGetProperty("worktreePath", out var wProp) ? wProp.GetString() : null;
                    string? branch = data.TryGetProperty("branch", out var bProp) ? bProp.GetString() : null;
                    bool createBranch = data.TryGetProperty("createBranch", out var cbProp) && cbProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(worktreePath) && !string.IsNullOrEmpty(branch))
                    {
                        var (ok, msg) = await GitRepositoryManagerService.Instance.AddWorktreeAsync(repoPath, worktreePath, branch, createBranch).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath, worktreePath, and branch are required");
                    }
                }
                break;

            case "remove_worktree":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? worktreePath = data.TryGetProperty("worktreePath", out var wProp) ? wProp.GetString() : null;
                    bool force = data.TryGetProperty("force", out var fProp) && fProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(worktreePath))
                    {
                        var (ok, msg) = await GitRepositoryManagerService.Instance.RemoveWorktreeAsync(repoPath, worktreePath, force).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and worktreePath are required");
                    }
                }
                break;

            case "get_stashes":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var stashes = await GitRepositoryManagerService.Instance.GetStashesAsync(repoPath).ConfigureAwait(false);
                        SendResponse(requestId, true, new { stashes });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "get_stash_diff":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    int idx = data.TryGetProperty("stashIndex", out var iProp) && iProp.TryGetInt32(out var indexVal) ? indexVal : 0;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (ok, diff) = await GitRepositoryManagerService.Instance.GetStashDiffAsync(repoPath, idx).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, diffContent = diff });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "create_stash":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? msg = data.TryGetProperty("message", out var mProp) ? mProp.GetString() : null;
                    bool untracked = data.TryGetProperty("includeUntracked", out var uProp) && uProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (ok, resMsg) = await GitRepositoryManagerService.Instance.CreateStashAsync(repoPath, msg, untracked).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = resMsg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "apply_stash":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    int idx = data.TryGetProperty("stashIndex", out var iProp) && iProp.TryGetInt32(out var indexVal) ? indexVal : 0;
                    bool pop = data.TryGetProperty("pop", out var pProp) && pProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (ok, resMsg) = await GitRepositoryManagerService.Instance.ApplyStashAsync(repoPath, idx, pop).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = resMsg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "drop_stash":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    int idx = data.TryGetProperty("stashIndex", out var iProp) && iProp.TryGetInt32(out var indexVal) ? indexVal : 0;

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (ok, resMsg) = await GitRepositoryManagerService.Instance.DropStashAsync(repoPath, idx).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = resMsg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "preview_git_merge":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? sourceBranch = data.TryGetProperty("sourceBranch", out var sbProp) ? sbProp.GetString() : null;
                    string? targetBranch = data.TryGetProperty("targetBranch", out var tbProp) ? tbProp.GetString() : null;

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(sourceBranch))
                    {
                        var preview = await GitRepositoryManagerService.Instance.PreviewMergeAsync(repoPath, sourceBranch, targetBranch).ConfigureAwait(false);
                        SendResponse(requestId, preview.Success, preview);
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and sourceBranch are required");
                    }
                }
                break;

            case "execute_git_merge":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? sourceBranch = data.TryGetProperty("sourceBranch", out var sbProp) ? sbProp.GetString() : null;
                    bool ffOnly = data.TryGetProperty("fastForwardOnly", out var ffProp) && ffProp.GetBoolean();
                    string? msg = data.TryGetProperty("message", out var mProp) ? mProp.GetString() : null;

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(sourceBranch))
                    {
                        var res = await GitRepositoryManagerService.Instance.ExecuteMergeAsync(repoPath, sourceBranch, ffOnly, msg).ConfigureAwait(false);
                        SendResponse(requestId, res.Success, res);
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and sourceBranch are required");
                    }
                }
                break;

            case "get_workspace_memories":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var memories = await AgentSessionMemoryService.Instance.GetMemoriesAsync(repoPath).ConfigureAwait(false);
                        SendResponse(requestId, true, new { memories });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "search_workspace_memories":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string query = data.TryGetProperty("query", out var qProp) ? qProp.GetString() ?? "" : "";
                    int topK = data.TryGetProperty("topK", out var kProp) && kProp.TryGetInt32(out var kVal) ? kVal : 10;

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var matches = await AgentSessionMemoryService.Instance.SearchMemoriesAsync(repoPath, query, topK).ConfigureAwait(false);
                        SendResponse(requestId, true, new { memories = matches });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "delete_workspace_memory":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? memoryId = data.TryGetProperty("memoryId", out var mProp) ? mProp.GetString() : null;

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(memoryId))
                    {
                        var (ok, msg) = await AgentSessionMemoryService.Instance.DeleteMemoryAsync(repoPath, memoryId).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and memoryId are required");
                    }
                }
                break;

            case "clear_workspace_memories":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (ok, count, msg) = await AgentSessionMemoryService.Instance.ClearAllMemoriesAsync(repoPath).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, count, message = msg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "add_workspace_memory":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string? title = data.TryGetProperty("title", out var tProp) ? tProp.GetString() : null;
                    string summary = data.TryGetProperty("summary", out var sProp) ? sProp.GetString() ?? "" : "";
                    var tags = new List<string>();
                    if (data.TryGetProperty("tags", out var tagListProp) && tagListProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in tagListProp.EnumerateArray())
                        {
                            var s = el.GetString();
                            if (!string.IsNullOrEmpty(s)) tags.Add(s);
                        }
                    }

                    if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(title))
                    {
                        var (ok, entry, msg) = await AgentSessionMemoryService.Instance.AddManualMemoryAsync(repoPath, title, summary, tags).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, entry, message = msg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and title are required");
                    }
                }
                break;

            case "inject_session_context":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    string prompt = data.TryGetProperty("prompt", out var pProp) ? pProp.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var snippet = await AgentSessionMemoryService.Instance.GetContextInjectionSnippetAsync(repoPath, prompt).ConfigureAwait(false);
                        SendResponse(requestId, true, new { snippet });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "get_codebase_graph":
                {
                    string? wsPath = null;
                    if (data.TryGetProperty("workspacePath", out var pProp)) wsPath = pProp.GetString();
                    if (string.IsNullOrWhiteSpace(wsPath))
                    {
                        var curWs = AiConfigService.Instance.GetActiveWorkspace();
                        wsPath = curWs?.WorkingDirectory;
                    }
                    if (string.IsNullOrWhiteSpace(wsPath))
                    {
                        wsPath = AppDomain.CurrentDomain.BaseDirectory;
                    }

                    int maxFiles = 300;
                    if (data.TryGetProperty("maxFiles", out var mfProp) && mfProp.TryGetInt32(out var parsedMaxFiles))
                    {
                        maxFiles = parsedMaxFiles;
                    }
                    bool forceRefresh = data.TryGetProperty("forceRefresh", out var frProp) && frProp.GetBoolean();

                    var graph = await CodebaseGraphAnalysisService.Instance.AnalyzeWorkspaceAsync(wsPath, maxFiles, forceRefresh).ConfigureAwait(false);
                    SendResponse(requestId, true, new { graph });
                }
                break;

            case "calculate_blast_radius":
                {
                    string? wsPath = null;
                    if (data.TryGetProperty("workspacePath", out var pProp)) wsPath = pProp.GetString();
                    if (string.IsNullOrWhiteSpace(wsPath))
                    {
                        var curWs = AiConfigService.Instance.GetActiveWorkspace();
                        wsPath = curWs?.WorkingDirectory;
                    }

                    string fileId = data.TryGetProperty("fileId", out var fProp) ? fProp.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(fileId))
                    {
                        var report = await CodebaseGraphAnalysisService.Instance.CalculateBlastRadiusAsync(wsPath, fileId).ConfigureAwait(false);
                        SendResponse(requestId, true, new { report });
                    }
                    else
                    {
                        SendResponse(requestId, false, "fileId is required");
                    }
                }
                break;

            case "batch_repo_action":
                {
                    var idList = new List<string>();
                    if (data.TryGetProperty("repoIds", out var idsProp) && idsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in idsProp.EnumerateArray())
                        {
                            var s = el.GetString();
                            if (!string.IsNullOrEmpty(s)) idList.Add(s);
                        }
                    }
                    string batchAction = data.TryGetProperty("batchAction", out var actProp)
                        ? actProp.GetString() ?? "fetch"
                        : (data.TryGetProperty("action", out var aProp) ? aProp.GetString() ?? "fetch" : "fetch");

                    var results = await GitRepositoryManagerService.Instance.BatchActionAsync(idList, batchAction).ConfigureAwait(false);
                    SendResponse(requestId, true, new { results });
                }
                break;

            case "get_github_auth_status":
                {
                    var status = await GitHubService.Instance.GetAuthStatusAsync().ConfigureAwait(false);
                    SendResponse(requestId, true, status);
                }
                break;

            case "auto_detect_github_git_credentials":
                {
                    var (ok, msg, status) = await GitHubService.Instance.AutoConnectFromGitAsync().ConfigureAwait(false);
                    SendResponse(requestId, ok, new { success = ok, message = msg, status });
                }
                break;

            case "save_github_pat":
                {
                    string? token = data.TryGetProperty("token", out var tokProp) ? tokProp.GetString() : null;
                    if (!string.IsNullOrEmpty(token))
                    {
                        var (ok, msg, status) = await GitHubService.Instance.SavePatAsync(token).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg, status });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Token is required");
                    }
                }
                break;

            case "disconnect_github":
                {
                    bool disconnected = GitHubService.Instance.Disconnect();
                    SendResponse(requestId, disconnected, new { success = disconnected });
                }
                break;

            case "publish_repo_to_github":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
                    string? repoName = data.TryGetProperty("repoName", out var rnProp) ? rnProp.GetString() : null;
                    string? desc = data.TryGetProperty("description", out var dProp) ? dProp.GetString() : "";
                    bool isPrivate = data.TryGetProperty("isPrivate", out var ipProp) && ipProp.GetBoolean();

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var result = await GitHubService.Instance.PublishRepositoryAsync(repoPath, repoName ?? "", desc ?? "", isPrivate).ConfigureAwait(false);
                        SendResponse(requestId, result.Success, result);
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "get_github_pull_requests":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
                    string state = data.TryGetProperty("state", out var prStateProp) ? prStateProp.GetString() ?? "open" : "open";
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var res = await GitHubService.Instance.GetPullRequestsAsync(repoPath, state).ConfigureAwait(false);
                        SendResponse(requestId, res.Success, res);
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "checkout_github_pull_request":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
                    int prNumber = data.TryGetProperty("prNumber", out var pnProp) ? pnProp.GetInt32() : 0;
                    if (!string.IsNullOrEmpty(repoPath) && prNumber > 0)
                    {
                        var (ok, msg, branch) = await GitHubService.Instance.CheckoutPullRequestAsync(repoPath, prNumber).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg, branchName = branch });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and valid prNumber are required");
                    }
                }
                break;

            case "merge_github_pull_request":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
                    int prNumber = data.TryGetProperty("prNumber", out var pnProp) ? pnProp.GetInt32() : 0;
                    string mergeMethod = data.TryGetProperty("mergeMethod", out var mmProp) ? mmProp.GetString() ?? "merge" : "merge";
                    string? commitTitle = data.TryGetProperty("commitTitle", out var ctProp) ? ctProp.GetString() : null;
                    string? commitMessage = data.TryGetProperty("commitMessage", out var cmProp) ? cmProp.GetString() : null;

                    if (!string.IsNullOrEmpty(repoPath) && prNumber > 0)
                    {
                        var (ok, msg, sha) = await GitHubService.Instance.MergePullRequestAsync(repoPath, prNumber, mergeMethod, commitTitle, commitMessage).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, message = msg, sha });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath and valid prNumber are required");
                    }
                }
                break;

            case "generate_ai_commit_message":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var (ok, subject, body, fullMsg) = await GitHubService.Instance.GenerateCommitMessageFromDiffAsync(repoPath).ConfigureAwait(false);
                        SendResponse(requestId, ok, new { success = ok, subject, body, fullMessage = fullMsg });
                    }
                    else
                    {
                        SendResponse(requestId, false, "repoPath is required");
                    }
                }
                break;

            case "get_agent_rules":
                {
                    string? wsId = data.TryGetProperty("workspaceId", out var gWsProp) ? gWsProp.GetString() : null;
                    var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(wsId).ConfigureAwait(false);
                    SendResponse(requestId, true, overview);
                }
                break;

            case "save_agent_rule":
                {
                    if (data.TryGetProperty("rule", out var rProp))
                    {
                        var rule = JsonSerializer.Deserialize<AgentRuleItem>(rProp.GetRawText(), CaseInsensitiveJsonOptions);
                        string? targetWs = data.TryGetProperty("workspaceId", out var sWsProp) ? sWsProp.GetString() : null;
                        if (rule != null)
                        {
                            bool saved = await AgentRulesService.Instance.SaveRuleAsync(rule, targetWs).ConfigureAwait(false);
                            var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
                            SendResponse(requestId, true, new { success = saved, overview });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid rule payload");
                }
                break;

            case "delete_agent_rule":
                {
                    string? ruleId = data.TryGetProperty("ruleId", out var rProp) ? rProp.GetString() : null;
                    string? ruleName = data.TryGetProperty("ruleName", out var rnProp) ? rnProp.GetString() : null;
                    string? scope = data.TryGetProperty("scope", out var delScopeProp) ? delScopeProp.GetString() : null;
                    string? targetWs = data.TryGetProperty("workspaceId", out var dWsProp) ? dWsProp.GetString() : null;

                    if (!string.IsNullOrEmpty(ruleId) || !string.IsNullOrEmpty(ruleName))
                    {
                        var id = ruleId ?? ruleName ?? "";
                        var name = ruleName ?? ruleId ?? "";
                        bool deleted = await AgentRulesService.Instance.DeleteRuleAsync(id, name, scope ?? "Project", targetWs).ConfigureAwait(false);
                        var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
                        SendResponse(requestId, true, new { success = deleted, overview });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Rule ID and name are required");
                    }
                }
                break;

            case "toggle_workspace_rule":
                {
                    string? wsId = data.TryGetProperty("workspaceId", out var tWsProp) ? tWsProp.GetString() : null;
                    string? ruleId = data.TryGetProperty("ruleId", out var rProp) ? rProp.GetString() : null;
                    bool enable = data.TryGetProperty("enable", out var enProp) && enProp.GetBoolean();

                    if (!string.IsNullOrEmpty(ruleId))
                    {
                        var targetWs = !string.IsNullOrEmpty(wsId) ? wsId : AiConfigService.Instance.GetActiveWorkspaceId();
                        bool toggled = AgentRulesService.Instance.ToggleRuleForWorkspace(targetWs, ruleId, enable);
                        var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
                        SendResponse(requestId, true, new { success = toggled, overview });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Rule identifier is required");
                    }
                }
                break;

            case "set_workspace_rule_isolation":
                {
                    string? wsId = data.TryGetProperty("workspaceId", out var iWsProp) ? iWsProp.GetString() : null;
                    bool isolate = data.TryGetProperty("isolate", out var isoProp) && isoProp.GetBoolean();

                    var targetWs = !string.IsNullOrEmpty(wsId) ? wsId : AiConfigService.Instance.GetActiveWorkspaceId();
                    bool set = AgentRulesService.Instance.SetWorkspaceRuleIsolation(targetWs, isolate);
                    var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
                    SendResponse(requestId, true, new { success = set, overview });
                }
                break;

            case "launch_interactive_npx_installer":
                {
                    string? command = data.TryGetProperty("command", out var cProp) ? cProp.GetString() : null;
                    string? targetWsId = data.TryGetProperty("workspaceId", out var npxWsProp) ? npxWsProp.GetString() : null;
                    string? mode = data.TryGetProperty("mode", out var mProp) ? mProp.GetString() : "terminal";

                    var workspaces = AiConfigService.Instance.GetWorkspaces();
                    var ws = !string.IsNullOrEmpty(targetWsId)
                        ? workspaces.FirstOrDefault(w => string.Equals(w.Id, targetWsId, StringComparison.OrdinalIgnoreCase))
                        : AiConfigService.Instance.GetActiveWorkspace();

                    var workDir = ws?.WorkingDirectory;
                    if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
                    {
                        workDir = Environment.CurrentDirectory;
                    }

                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        if (mode == "window")
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/k \"cd /d \"{workDir}\" && {command}\"",
                                WorkingDirectory = workDir,
                                UseShellExecute = true
                            };
                            Process.Start(psi);
                            SendResponse(requestId, true, new { success = true, mode = "window" });
                        }
                        else
                        {
                            var targetSession = ResolveTerminalSessionId(ws?.SecondaryCli ?? "powershell");
                            _cliManager.SendInput(targetSession, $"cd \"{workDir}\"\r\n{command}\r\n");
                            SendResponse(requestId, true, new { success = true, mode = "terminal", session = targetSession });
                        }
                    }
                    else
                    {
                        SendResponse(requestId, false, "Command is required");
                    }
                }
                break;

            case "get_custom_clis":
                {
                    var clis = CustomCliService.Instance.GetCustomClis();
                    SendResponse(requestId, true, new { clis });
                }
                break;

            case "save_custom_cli":
                {
                    if (data.TryGetProperty("cli", out var cProp))
                    {
                        var c = JsonSerializer.Deserialize<CustomCliConfig>(cProp.GetRawText(), CaseInsensitiveJsonOptions);
                        if (c != null)
                        {
                            CustomCliService.Instance.SaveCustomCli(c);
                            SendResponse(requestId, true, new { success = true, cli = c, clis = CustomCliService.Instance.GetCustomClis() });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Invalid custom CLI payload");
                }
                break;

            case "delete_custom_cli":
                {
                    string? id = data.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id))
                    {
                        bool deleted = CustomCliService.Instance.DeleteCustomCli(id);
                        SendResponse(requestId, true, new { success = deleted, clis = CustomCliService.Instance.GetCustomClis() });
                    }
                    else
                    {
                        SendResponse(requestId, false, "CLI ID is required");
                    }
                }
                break;

            case "get_recent_file_changes":
                {
                    var changes = FileWatchdogService.Instance.GetRecentChanges();
                    SendResponse(requestId, true, new { changes });
                }
                break;

            case "get_file_diff":
                {
                    string? path = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        var diff = await FileWatchdogService.Instance.GetFileDiffAsync(path).ConfigureAwait(false);
                        SendResponse(requestId, true, new { diff });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Path is required");
                    }
                }
                break;

            case "revert_file_change":
                {
                    string? path = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        bool reverted = await FileWatchdogService.Instance.RevertFileAsync(path).ConfigureAwait(false);
                        SendResponse(requestId, true, new { success = reverted });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Path is required");
                    }
                }
                break;

            case "clear_file_changes":
                {
                    FileWatchdogService.Instance.ClearRecentChanges();
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "run_system_diagnostics":
                {
                    var diagnostics = await SystemDoctorService.Instance.RunDiagnosticsAsync().ConfigureAwait(false);
                    SendResponse(requestId, true, new { diagnostics });
                }
                break;

            case "autofix_diagnostic_issue":
                {
                    string? issueId = data.TryGetProperty("issueId", out var issProp) ? issProp.GetString() : null;
                    if (!string.IsNullOrEmpty(issueId))
                    {
                        bool fixedOk = await SystemDoctorService.Instance.AutoFixIssueAsync(issueId).ConfigureAwait(false);
                        var diagnostics = await SystemDoctorService.Instance.RunDiagnosticsAsync().ConfigureAwait(false);
                        SendResponse(requestId, true, new { success = fixedOk, diagnostics });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Issue ID is required");
                    }
                }
                break;

            case "get_session_state":
                {
                    var stateData = SessionStateService.Instance.LoadSnapshot();
                    SendResponse(requestId, true, new { state = stateData });
                }
                break;

            case "save_session_state":
                {
                    string? activeWs = data.TryGetProperty("activeWorkspaceId", out var actWsProp) ? actWsProp.GetString() : null;
                    string? activeView = data.TryGetProperty("activeView", out var vProp) ? vProp.GetString() : null;
                    string? activeTab = data.TryGetProperty("activeCliTab", out var tProp) ? tProp.GetString() : null;
                    SessionStateService.Instance.SaveSnapshot(activeWs ?? "", activeView ?? "dashboard", activeTab ?? "hermes");
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "get_workspaces":
                SendResponse(requestId, true, new
                {
                    workspaces = AiConfigService.Instance.GetWorkspaces(),
                    activeWorkspaceId = AiConfigService.Instance.GetActiveWorkspaceId()
                });
                break;

            case "save_workspace":
                if (data.TryGetProperty("workspace", out var wsProp))
                {
                    var ws = JsonSerializer.Deserialize<ProjectWorkspace>(wsProp.GetRawText(), CaseInsensitiveJsonOptions);
                    if (ws != null)
                    {
                        AiConfigService.Instance.SaveWorkspace(ws);
                        SendResponse(requestId, true, new
                        {
                            success = true,
                            workspace = ws,
                            workspaces = AiConfigService.Instance.GetWorkspaces(),
                            activeWorkspaceId = AiConfigService.Instance.GetActiveWorkspaceId()
                        });
                        PostEventToUi("workspaces_updated", new
                        {
                            workspaces = AiConfigService.Instance.GetWorkspaces(),
                            activeWorkspaceId = AiConfigService.Instance.GetActiveWorkspaceId()
                        });
                        break;
                    }
                }
                SendResponse(requestId, false, "Invalid workspace data");
                break;

            case "delete_workspace":
                {
                    string? delId = data.TryGetProperty("id", out var dIdProp) ? dIdProp.GetString() : null;
                    if (!string.IsNullOrEmpty(delId))
                    {
                        bool deleted = AiConfigService.Instance.DeleteWorkspace(delId);
                        SendResponse(requestId, true, new
                        {
                            success = deleted,
                            workspaces = AiConfigService.Instance.GetWorkspaces(),
                            activeWorkspaceId = AiConfigService.Instance.GetActiveWorkspaceId()
                        });
                        PostEventToUi("workspaces_updated", new
                        {
                            workspaces = AiConfigService.Instance.GetWorkspaces(),
                            activeWorkspaceId = AiConfigService.Instance.GetActiveWorkspaceId()
                        });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Workspace ID required");
                    }
                }
                break;

            case "activate_workspace":
                {
                    string? actId = data.TryGetProperty("id", out var aIdProp)
                        ? aIdProp.GetString()
                        : (data.TryGetProperty("workspaceId", out var wIdProp) ? wIdProp.GetString() : null);

                    if (!string.IsNullOrEmpty(actId))
                    {
                        var all = AiConfigService.Instance.GetWorkspaces();
                        var target = all.FirstOrDefault(w => string.Equals(w.Id, actId, StringComparison.OrdinalIgnoreCase));
                        if (target != null)
                        {
                            AiConfigService.Instance.SetActiveWorkspaceId(target.Id);
                            if (!string.IsNullOrWhiteSpace(target.WorkingDirectory))
                            {
                                FileWatchdogService.Instance.WatchDirectory(target.WorkingDirectory);
                            }
                            if (!string.IsNullOrWhiteSpace(target.AntigravityProfile) &&
                                !string.Equals(target.AntigravityProfile, "default", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(_cliManager.ProfileService.ActiveProfile, target.AntigravityProfile, StringComparison.OrdinalIgnoreCase))
                            {
                                await _cliManager.SwitchAntigravityProfileAsync(target.AntigravityProfile);
                            }
                            var currentWorkspaces = AiConfigService.Instance.GetWorkspaces();
                            SendResponse(requestId, true, new
                            {
                                success = true,
                                activeWorkspace = target,
                                activeWorkspaceId = target.Id,
                                workspaces = currentWorkspaces
                            });
                            PostEventToUi("workspace_activated", new
                            {
                                activeWorkspace = target,
                                activeWorkspaceId = target.Id,
                                workspaces = currentWorkspaces
                            });
                            break;
                        }
                    }
                    SendResponse(requestId, false, "Workspace not found");
                }
                break;

            case "get_git_status":
                {
                    string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
                    if (string.IsNullOrEmpty(repoPath))
                    {
                        var ws = AiConfigService.Instance.GetActiveWorkspace();
                        repoPath = ws?.WorkingDirectory;
                    }
                    if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
                    {
                        repoPath = Environment.CurrentDirectory;
                    }

                    var changes = await GitRepositoryManagerService.Instance.GetWorkingTreeChangesAsync(repoPath).ConfigureAwait(false);
                    var (rc, rawDiff) = await GitRepositoryManagerService.RunGitAsync(repoPath, ["diff", "--no-color"]).ConfigureAwait(false);
                    var files = changes.Select(c => c.Path).ToList();

                    SendResponse(requestId, true, new
                    {
                        repoPath,
                        files,
                        changes,
                        rawDiff = !string.IsNullOrWhiteSpace(rawDiff) ? rawDiff : "No uncommitted diff detected in active workspace.",
                        diff = !string.IsNullOrWhiteSpace(rawDiff) ? rawDiff : "No uncommitted diff detected in active workspace."
                    });
                }
                break;

            case "get_workspace_files":
                {
                    string? targetDir = null;
                    if (data.TryGetProperty("path", out var targetPathProp))
                    {
                        targetDir = targetPathProp.GetString();
                    }
                    if (string.IsNullOrWhiteSpace(targetDir))
                    {
                        var activeWs = AiConfigService.Instance.GetActiveWorkspace();
                        targetDir = activeWs?.WorkingDirectory;
                    }
                    if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
                    {
                        targetDir = Directory.GetCurrentDirectory();
                    }

                    var branch = "main";
                    try
                    {
                        var repo = GitRepositoryManagerService.Instance.GetRepository(targetDir);
                        if (repo != null && !string.IsNullOrWhiteSpace(repo.Branch))
                        {
                            branch = repo.Branch;
                        }
                    }
                    catch { }

                    var fileEntries = new List<object>();
                    try
                    {
                        var dirInfo = new DirectoryInfo(targetDir);
                        var ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ".git", ".vs", "bin", "obj", "node_modules", ".agents", "dist", "build"
                        };

                        foreach (var subDir in dirInfo.GetDirectories().OrderBy(d => d.Name))
                        {
                            if (ignoredNames.Contains(subDir.Name) || subDir.Name.StartsWith('.')) continue;
                            fileEntries.Add(new
                            {
                                name = subDir.Name + "/",
                                path = subDir.Name,
                                isDirectory = true,
                                extension = ""
                            });

                            try
                            {
                                foreach (var childFile in subDir.GetFiles().Take(6).OrderBy(f => f.Name))
                                {
                                    fileEntries.Add(new
                                    {
                                        name = childFile.Name,
                                        path = Path.Combine(subDir.Name, childFile.Name).Replace('\\', '/'),
                                        isDirectory = false,
                                        extension = childFile.Extension.ToLowerInvariant()
                                    });
                                }
                            }
                            catch { }
                        }

                        foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name))
                        {
                            if (file.Name.StartsWith('.') || file.Name.EndsWith(".user")) continue;
                            fileEntries.Add(new
                            {
                                name = file.Name,
                                path = file.Name,
                                isDirectory = false,
                                extension = file.Extension.ToLowerInvariant()
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        SendResponse(requestId, false, ex.Message);
                        break;
                    }

                    SendResponse(requestId, true, new
                    {
                        rootPath = targetDir,
                        branch,
                        files = fileEntries
                    });
                }
                break;

            case "get_token_telemetry":
                SendResponse(requestId, true, ProxyTelemetryService.Instance.GetSummary());
                break;

            case "reset_token_telemetry":
                ProxyTelemetryService.Instance.Reset();
                SendResponse(requestId, true, ProxyTelemetryService.Instance.GetSummary());
                PostEventToUi("token_telemetry_updated", ProxyTelemetryService.Instance.GetSummary());
                break;

            case "toggle_quake_hud":
#if WINDOWS
                if (_mainForm is MainForm mf)
                {
                    mf.ExecuteQuakeHotkeyAction();
                    SendResponse(requestId, true, new { success = true });
                }
                else
                {
                    SendResponse(requestId, false, "MainForm reference unavailable");
                }
#else
                SendResponse(requestId, false, "Quake HUD is only available on Windows desktop");
#endif
                break;

            case "toggle_main_window":
#if WINDOWS
                if (_mainForm is MainForm mainFormToggle)
                {
                    mainFormToggle.ShowOrHideFromHotkey();
                    SendResponse(requestId, true, new { success = true });
                }
                else
                {
                    SendResponse(requestId, false, "MainForm reference unavailable");
                }
#else
                SendResponse(requestId, false, "Main window toggle is only available on Windows desktop");
#endif
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

            case "get_lan_state":
                {
                    // The firewall probe spawns PowerShell, so it is cached and only forced
                    // when the UI asks for an explicit recheck.
                    bool forceFirewall = data.TryGetProperty("refresh", out var refreshEl)
                        && refreshEl.ValueKind == JsonValueKind.True;

                    var required = GetRequiredLanPorts();

                    if (!forceFirewall && _cachedFirewall != null)
                    {
                        SendResponse(requestId, true, new
                        {
                            lan = BuildLanStateDto(required),
                            firewall = ToFirewallDto(_cachedFirewall, required),
                            clients = ToClientDtos(_lanProxyService?.GetActiveClients())
                        });
                    }
                    else if (!forceFirewall)
                    {
                        // Return LAN networking details immediately so WebView2 does not block
                        SendResponse(requestId, true, new
                        {
                            lan = BuildLanStateDto(required),
                            firewall = new
                            {
                                querySucceeded = true,
                                state = "checking",
                                detail = "Probing Windows Firewall in background...",
                                requiredPorts = required
                            },
                            clients = ToClientDtos(_lanProxyService?.GetActiveClients())
                        });

                        // Run firewall probe in background and push update
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var fw = await GetFirewallStatusAsync(required, force: false).ConfigureAwait(false);
                                PostEventToUi("lan_firewall_updated", ToFirewallDto(fw, required));
                            }
                            catch { }
                        });
                    }
                    else
                    {
                        // User explicitly clicked "Recheck"
                        var firewall = await GetFirewallStatusAsync(required, force: true).ConfigureAwait(false);

                        SendResponse(requestId, true, new
                        {
                            lan = BuildLanStateDto(required),
                            firewall = ToFirewallDto(firewall, required),
                            clients = ToClientDtos(_lanProxyService?.GetActiveClients())
                        });
                    }
                }
                break;

            case "get_lan_clients":
                SendResponse(requestId, true, new
                {
                    clients = ToClientDtos(_lanProxyService?.GetActiveClients())
                });
                break;

            case "request_firewall_rule":
                {
                    var ports = GetRequiredLanPorts();
                    if (ports.Count == 0)
                    {
                        SendResponse(requestId, false, "Turn LAN access on first, so there are ports to allow.");
                        break;
                    }

                    var result = await FirewallHelper.RequestInboundRuleAsync(ports);
                    var fresh = await GetFirewallStatusAsync(ports, force: true);

                    SendResponse(requestId, true, new
                    {
                        succeeded = result.Succeeded,
                        userCancelled = result.UserCancelled,
                        message = result.Message,
                        lan = BuildLanStateDto(ports),
                        firewall = ToFirewallDto(fresh, ports)
                    });
                }
                break;

            case "remove_firewall_rule":
                {
                    var result = await FirewallHelper.RemoveInboundRuleAsync();
                    var ports = GetRequiredLanPorts();
                    var fresh = await GetFirewallStatusAsync(ports, force: true);

                    SendResponse(requestId, true, new
                    {
                        succeeded = result.Succeeded,
                        userCancelled = result.UserCancelled,
                        message = result.Message,
                        lan = BuildLanStateDto(ports),
                        firewall = ToFirewallDto(fresh, ports)
                    });
                }
                break;

            // Ids rather than CliType names: the dashboard sends normalized workspace ids
            // ("claude_code"), which Enum.TryParse cannot read.
            case "toggle_cli":
                if (data.TryGetProperty("cli", out var cliProp))
                {
                    string targetCli = cliProp.GetString() ?? string.Empty;
                    var info = _cliManager.GetInfo(targetCli);
                    if (info.State == CliState.Running)
                    {
                        await _cliManager.StopAsync(targetCli);
                    }
                    else
                    {
                        _cliManager.Start(targetCli);
                    }
                    SendResponse(requestId, true, new { state = _cliManager.GetInfo(targetCli).State.ToString() });
                }
                break;

            case "restart_cli":
                if (data.TryGetProperty("cli", out var rCli))
                {
                    await _cliManager.RestartAsync(rCli.GetString() ?? string.Empty);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            // The dashboard terminal renders a CLI session, so it asks for that session's
            // scrollback and running state before it attaches. Hermes is a web dashboard rather
            // than a ConPTY session, so its tab answers with the Hermes port alone.
            case "get_terminal_session":
                {
                    string reqCli = data.TryGetProperty("cli", out var gCli) ? (gCli.GetString() ?? "hermes") : "hermes";
                    string normId = AiConfigService.NormalizeCliId(reqCli);

                    if (string.Equals(normId, "hermes", StringComparison.OrdinalIgnoreCase))
                    {
                        var hermesInfo = _cliManager.GetInfo("hermes");
                        int hermesPort = _cliManager.HermesService.Port;
                        SendResponse(requestId, true, new
                        {
                            cli = "hermes",
                            sessionId = "hermes",
                            isRunning = hermesInfo.State == CliState.Running,
                            status = hermesInfo.StatusText,
                            history = string.Empty,
                            isWebUi = true,
                            webUrl = $"http://127.0.0.1:{hermesPort}/",
                            port = hermesPort,
                            displayName = "Hermes Web"
                        });
                    }
                    else
                    {
                        string sessionId = ResolveTerminalSessionId(normId);
                        var info = _cliManager.GetInfo(sessionId);
                        SendResponse(requestId, true, new
                        {
                            cli = normId,
                            sessionId,
                            isRunning = info.State == CliState.Running,
                            status = info.StatusText,
                            history = _cliManager.GetTerminalHistory(sessionId),
                            isWebUi = false,
                            webUrl = info.WebUrl ?? (info.Port != null ? $"http://127.0.0.1:{info.Port}" : string.Empty),
                            port = info.Port,
                            displayName = info.DisplayName
                        });
                    }
                }
                break;

            case "clear_terminal_history":
                if (data.TryGetProperty("cli", out var clrCli))
                {
                    _cliManager.ClearTerminalHistory(ResolveTerminalSessionId(clrCli.GetString() ?? string.Empty));
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "send_terminal_input":
                if (data.TryGetProperty("cli", out var inCli) && data.TryGetProperty("input", out var inData))
                {
                    string targetCli = ResolveTerminalSessionId(inCli.GetString() ?? string.Empty);
                    string rawInput = inData.GetString() ?? string.Empty;

                    if (CliManagerService.IsAntigravityProfileId(targetCli, out _))
                    {
                        var cliInfo = _cliManager.GetInfo(targetCli);
                        if (cliInfo.State != CliState.Running && cliInfo.State != CliState.Starting)
                        {
                            _cliManager.Start(targetCli);
                        }
                    }

                    _cliManager.SendInput(targetCli, rawInput);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "resize_terminal":
                if (data.TryGetProperty("cli", out var szCli) &&
                    data.TryGetProperty("cols", out var cols) && data.TryGetProperty("rows", out var rows))
                {
                    _cliManager.Resize(ResolveTerminalSessionId(szCli.GetString() ?? string.Empty), (short)cols.GetInt32(), (short)rows.GetInt32());
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "get_active_profile_email":
                {
                    var actProf = _cliManager.ProfileService.ActiveProfile;
                    var actEmail = AntigravityProfileService.GetAccountEmail(actProf) ?? string.Empty;
                    SendResponse(requestId, true, new { profile = actProf, email = actEmail });
                }
                break;

            case "switch_antigravity_profile":
                {
                    string? pName = null;
                    if (data.TryGetProperty("profile", out var profProp))
                        pName = profProp.GetString();
                    else if (data.TryGetProperty("profileName", out var profNameProp))
                        pName = profNameProp.GetString();

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
                        bool isRawTerminal = data.TryGetProperty("rawTerminal", out var rtProp) && rtProp.GetBoolean();
                        if (isRawTerminal)
                        {
                            _cliManager.StartAntigravityProfile(pName);
                            SendResponse(requestId, true, new { success = true, profile = pName, isSidecar = false, port = _cliManager.GetPortForProfile(pName), profiles = GetProfilesWithEmail() });
                        }
                        else
                        {
                            var curWs = AiConfigService.Instance.GetActiveWorkspace();
                            string? cwd = curWs?.WorkingDirectory;
                            string model = "Gemini 3.8 Flash (High)";
                            await _cliManager.SwitchAntigravityProfileAsync(pName).ConfigureAwait(false);
                            await Sidecar.SidecarHostService.Instance.StartProfileSessionAsync(pName, cwd, "gemini-3.8-flash-high").ConfigureAwait(false);
                            PostEventToUi("antigravity_instance_status_changed", new { profile = pName, isRunning = true, model });
                            PostEventToUi("profile_changed", new { activeProfile = pName, model, profiles = GetProfilesWithEmail() });
                            SendResponse(requestId, true, new { success = true, profile = pName, isSidecar = true, model, profiles = GetProfilesWithEmail() });
                        }
                    }
                }
                break;

            case "stop_antigravity_instance":
                if (data.TryGetProperty("profile", out var stopProfProp))
                {
                    var pName = stopProfProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        await Sidecar.SidecarHostService.Instance.StopProfileSessionAsync(pName).ConfigureAwait(false);
                        await _cliManager.StopAntigravityProfileAsync(pName).ConfigureAwait(false);
                        PostEventToUi("antigravity_instance_status_changed", new { profile = pName, isRunning = false });
                        PostEventToUi("profile_changed", new { activeProfile = pName, profiles = GetProfilesWithEmail() });
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

            case "open_folder":
                if (data.TryGetProperty("path", out var pathProp))
                {
                    var folderPath = pathProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
                        SendResponse(requestId, true, null);
                    }
                    else
                    {
                        SendResponse(requestId, false, "Folder does not exist.");
                    }
                }
                else
                {
                    SendResponse(requestId, false, "Path not specified.");
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
                            apiKeys = p.GetEffectiveApiKeys(),
                            isPreset = p.IsPreset
                        }),
                        activeProvider = new
                        {
                            id = active.Id,
                            name = active.Name,
                            baseUrl = active.BaseUrl,
                            apiKey = active.ApiKey,
                            apiKeys = active.GetEffectiveApiKeys(),
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

                    var apiKeysList = new List<string>();
                    if (data.TryGetProperty("apiKeys", out var keysArr) && keysArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in keysArr.EnumerateArray())
                        {
                            var s = item.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(s) && !apiKeysList.Contains(s))
                            {
                                apiKeysList.Add(s);
                            }
                        }
                    }
                    if (apiKeysList.Count == 0 && !string.IsNullOrWhiteSpace(apiKey))
                    {
                        apiKeysList.Add(apiKey);
                    }

                    string primaryKey = apiKeysList.FirstOrDefault() ?? apiKey;

                    var provider = new ClaudeGatewayProvider
                    {
                        Id = string.IsNullOrWhiteSpace(provId) ? "preset_local_proxy" : provId,
                        Name = name,
                        BaseUrl = baseUrl,
                        ApiKey = primaryKey,
                        ApiKeys = apiKeysList,
                        IsPreset = provId.StartsWith("preset_", StringComparison.OrdinalIgnoreCase)
                    };
                    AiConfigService.Instance.SaveClaudeGatewayProvider(provider);

                    if (string.Equals(AiConfigService.Instance.GetClaudeSelectedGatewayId(), provider.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        CliManagerService.SyncClaudeGatewaySettingsJson(primaryKey, AiConfigService.Instance.GetClaudeSelectedModel(), baseUrl);
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
                            apiKeys = provider.GetEffectiveApiKeys(),
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
                        Id = "custom_" + Guid.NewGuid().ToString("N")[..8],
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
                        apiKey
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
                        apiKey
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
                                limit,
                                label,
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
                    // Patch the live config rather than replacing it, so anything the dashboard
                    // does not send is left alone.
                    ApplyConfigPatch(AiConfigService.Instance.CurrentConfig, cfgProp);
                    AiConfigService.Instance.SaveConfig(AiConfigService.Instance.CurrentConfig);
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "apply_terminal_preset":
                {
                    var presetName = data.TryGetProperty("preset", out var presetProp) ? presetProp.GetString() : null;
                    AiConfigService.Instance.ApplyWorkspacePreset(presetName ?? "Preset_Duo");

#if WINDOWS
                    // Rebuild the standalone terminal workspace if the user has it open.
                    try
                    {
                        if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                        {
                            f.Invoke(() =>
                            {
                                if (_cliWebviewForm != null && !_cliWebviewForm.IsDisposed)
                                {
                                    _cliWebviewForm.RebuildTabs();
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DashboardWebBridge] RebuildTabs after preset failed: {ex.Message}");
                    }
#endif

                    var workspaces = AiConfigService.Instance.GetCliWorkspaces();
                    SendResponse(requestId, true, new
                    {
                        success = true,
                        preset = AiConfigService.Instance.GetTerminalWorkspacePreset(),
                        enabledClis = workspaces.Where(w => w.IsEnabled).Select(w => AiConfigService.NormalizeCliId(w.Id)).ToList()
                    });
                }
                break;

            case "get_terminal_preset":
                {
                    var workspaces = AiConfigService.Instance.GetCliWorkspaces();
                    SendResponse(requestId, true, new
                    {
                        preset = AiConfigService.Instance.GetTerminalWorkspacePreset(),
                        enabledClis = workspaces.Where(w => w.IsEnabled).Select(w => AiConfigService.NormalizeCliId(w.Id)).ToList()
                    });
                }
                break;

            case "get_dependencies_status":
                {
                    var dependencies = await DependencyCheckerService.GetDependenciesStatusAsync();
                    SendResponse(requestId, true, new { dependencies = dependencies.Select(ToDependencyDto).ToList() });
                }
                break;

            case "run_dependency_install":
                {
                    var dependencyId = data.TryGetProperty("id", out var depIdProp) ? depIdProp.GetString() : null;
                    var definition = DependencyCheckerService.Find(dependencyId);

                    if (definition == null)
                    {
                        SendResponse(requestId, false, "Unknown dependency.");
                        break;
                    }

                    // Acknowledge immediately: the installer streams its output as events, so the
                    // UI never has to wait on a long-running script.
                    SendResponse(requestId, true, new { success = true, started = true, id = definition.Id, name = definition.Name });
                    _ = RunDependencyInstallAsync(definition);
                }
                break;

            case "install_all_dependencies":
                {
                    var statuses = await DependencyCheckerService.GetDependenciesStatusAsync();
                    var missing = statuses.Where(s => !s.IsInstalled).ToList();

                    if (missing.Count == 0)
                    {
                        SendResponse(requestId, true, new { started = false, total = 0, names = Array.Empty<string>() });
                        break;
                    }

                    List<DependencyDefinition> queue = [];
                    foreach (var status in missing)
                    {
                        var found = DependencyCheckerService.Find(status.Id);
                        if (found != null) queue.Add(found);
                    }

                    SendResponse(requestId, true, new
                    {
                        started = true,
                        total = queue.Count,
                        names = queue.Select(d => d!.Name).ToArray()
                    });

                    // One at a time on purpose: parallel winget installs contend for the same
                    // package manager and log files, and the outcome becomes unreadable.
                    _ = RunAllDependencyInstallsAsync(queue);
                }
                break;

            case "get_dev_tools":
                // Local reads only: File.Exists per tool. Cheap enough to call whenever the view
                // opens, so the path health on screen is never stale.
                SendResponse(requestId, true, new { devTools = BuildDevToolDtos() });
                break;

            case "detect_ai_clients":
                {
                    // A bounded directory walk, so it runs off the UI thread and reports how far
                    // it got rather than pretending to be exhaustive.
                    var found = await Task.Run(() => InstalledClients.FindInstalledClients());

                    var configured = AiConfigService.Instance.CurrentConfig.DevTools ?? [];
                    var offers = found
                        .Where(f => !configured.Any(d =>
                            !string.IsNullOrWhiteSpace(d.Target) &&
                            (string.Equals(Path.GetFileName(f.Target), Path.GetFileName(d.Target), StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(f.Target, d.Target, StringComparison.OrdinalIgnoreCase))))
                        .Select(f => new
                        {
                            name = f.Name,
                            target = f.Target,
                            isIde = f.IsIde,
                            alreadyConfigured = configured.Any(d =>
                                !string.IsNullOrWhiteSpace(d.Target) &&
                                (string.Equals(Path.GetFileName(f.Target), Path.GetFileName(d.Target), StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(f.Target, d.Target, StringComparison.OrdinalIgnoreCase)))
                        })
                        .ToList();

                    SendResponse(requestId, true, new
                    {
                        found = offers,
                        scannedFor = InstalledClients.KnownClientNames,
                        alreadyConfigured = found.Count - offers.Count
                    });
                }
                break;

            case "launch_tool":
                {
                    string? target = null;
                    string? toolName = null;
                    string? toolId = null;
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
                        toolId = idProp.GetString();
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

                    if (string.IsNullOrWhiteSpace(target) &&
                        (string.Equals(toolId, "antigravity_app", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(toolName, "Antigravity App", StringComparison.OrdinalIgnoreCase)))
                    {
                        target = AiConfigService.FindAntigravityAppPath();
                    }

                    if (string.IsNullOrWhiteSpace(target))
                    {
                        SendResponse(requestId, false, "Target path is empty. Set one with Browse first.");
                        break;
                    }

                    string? toolArguments = data.TryGetProperty("arguments", out var argsProp) ? argsProp.GetString() : null;
                    string? toolWorkingDirectory = data.TryGetProperty("workingDirectory", out var wdProp) ? wdProp.GetString() : null;

                    // The UI sends the tool it launched, so fall back to the stored values.
                    if (!string.IsNullOrWhiteSpace(toolId))
                    {
                        var stored = AiConfigService.Instance.CurrentConfig.DevTools?
                            .FirstOrDefault(d => string.Equals(d.Id, toolId, StringComparison.OrdinalIgnoreCase));
                        if (stored != null)
                        {
                            toolArguments ??= stored.Arguments;
                            toolWorkingDirectory ??= stored.WorkingDirectory;
                        }
                    }

                    var (launched, launchMessage) = LaunchExecutableTarget(
                        target, toolName ?? "Tool", toolArguments ?? "", toolWorkingDirectory ?? "");
                    SendResponse(requestId, launched, new { success = launched, target, message = launchMessage });
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
#if WINDOWS
                    if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                    {
                        f.Invoke(() =>
                        {
                            var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
                            var localAntigravity = Path.Combine(localPrograms, "antigravity");
                            string initDir = Directory.Exists(localAntigravity) ? localAntigravity :
                                            Directory.Exists(localPrograms) ? localPrograms :
                                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                            using var ofd = new OpenFileDialog
                            {
                                Title = string.IsNullOrWhiteSpace(tool) ? "Select Executable Target" : $"Select Target Executable for {tool}",
                                Filter = "Executables (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All Files (*.*)|*.*",
                                InitialDirectory = initDir
                            };
                            if (ofd.ShowDialog(f) == DialogResult.OK)
                            {
                                selectedPath = ofd.FileName;
                            }
                        });
                    }
#endif

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
                        SendResponse(requestId, true, new { toolName = tool, toolId, path = selectedPath });
                    }
                    else
                    {
                        SendResponse(requestId, false, "Selection cancelled");
                    }
                }
                break;

            case "browse_directory":
                {
                    string title = data.TryGetProperty("title", out var bdTitle) ? bdTitle.GetString() ?? "Select a folder" : "Select a folder";

                    string? selectedFolder = null;
#if WINDOWS
                    if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                    {
                        f.Invoke(() =>
                        {
                            using var fbd = new FolderBrowserDialog { Description = title };
                            if (fbd.ShowDialog(f) == DialogResult.OK)
                            {
                                selectedFolder = fbd.SelectedPath;
                            }
                        });
                    }
#endif

                    if (!string.IsNullOrWhiteSpace(selectedFolder))
                    {
                        SendResponse(requestId, true, new { path = selectedFolder });
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

                    var defaults = AiConfigService.GetDefaultToolTargets();
                    var defaultTools = AiConfigService.GetDefaultDevTools();

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
                    SendResponse(requestId, true, new { toolName = tool, toolId, path = defPath });
                }
                break;

            case "open_standalone_terminals":
                try
                {
#if WINDOWS
                    // The standalone window opens on the CLI the dashboard is showing.
                    string targetTab = data.TryGetProperty("cli", out var popCli) ? (popCli.GetString() ?? string.Empty) : string.Empty;
                    if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                    {
                        f.Invoke(() =>
                        {
                            var form = EnsureStandaloneTerminalForm();
                            if (!string.IsNullOrWhiteSpace(targetTab))
                            {
                                var workspaces = AiConfigService.Instance.GetCliWorkspaces();
                                bool isEnabled = workspaces.Any(w => w.IsEnabled && (
                                    string.Equals(AiConfigService.NormalizeCliId(w.Id), AiConfigService.NormalizeCliId(targetTab), StringComparison.OrdinalIgnoreCase) ||
                                    (string.Equals(w.Id, "antigravity_cli", StringComparison.OrdinalIgnoreCase) && CliManagerService.IsAntigravityProfileId(targetTab, out _))
                                ));
                                if (isEnabled)
                                {
                                    form.SwitchTab(targetTab);
                                }
                            }
                            form.ApplyTheme();
                            if (form.WindowState == FormWindowState.Minimized)
                            {
                                form.WindowState = FormWindowState.Normal;
                            }
                            form.Show();
                            form.BringToFront();
                            form.Activate();

                            if (StartupHelper.GetPreference("HideMainOnCliOpen", false))
                            {
                                form.HideMainFormNow();
                            }
                        });
                    }
#endif
                    SendResponse(requestId, true, new { success = true });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DashboardWebBridge] open_standalone_terminals error: {ex}");
                    SendResponse(requestId, false, ex.Message);
                }
                break;

            // Keeping the standalone workspace on the dashboard's CLI makes the two terminal views
            // one session rather than two windows that drift apart.
            case "sync_terminal_tab":
                if (data.TryGetProperty("cli", out var syncCliProp))
                {
#if WINDOWS
                    string syncCli = syncCliProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(syncCli) && _cliWebviewForm != null && !_cliWebviewForm.IsDisposed)
                    {
                        try
                        {
                            var workspaces = AiConfigService.Instance.GetCliWorkspaces();
                            bool isEnabled = workspaces.Any(w => w.IsEnabled && (
                                string.Equals(AiConfigService.NormalizeCliId(w.Id), AiConfigService.NormalizeCliId(syncCli), StringComparison.OrdinalIgnoreCase) ||
                                (string.Equals(w.Id, "antigravity_cli", StringComparison.OrdinalIgnoreCase) && CliManagerService.IsAntigravityProfileId(syncCli, out _))
                            ));
                            if (isEnabled && _mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                            {
                                f.Invoke(() => _cliWebviewForm.SwitchTab(syncCli));
                            }
                        }
                        catch { }
                    }
#endif
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "open_system_shell":
                {
                    string shellName = data.TryGetProperty("shell", out var shEl) ? shEl.GetString() ?? "cmd" : "cmd";
                    bool asAdmin = data.TryGetProperty("asAdmin", out var adEl) && adEl.GetBoolean();
                    bool embedShells = StartupHelper.GetPreference("EmbedShellsInTerminalWorkspace", false);

#if WINDOWS
                    if (embedShells && !asAdmin && _mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                    {
                        f.Invoke(() =>
                        {
                            var form = EnsureStandaloneTerminalForm();
                            if (shellName.Equals("powershell", StringComparison.OrdinalIgnoreCase))
                            {
                                form.OpenEmbeddedShell("powershell", "PowerShell", "powershell.exe");
                            }
                            else
                            {
                                form.OpenEmbeddedShell("cmd", "Command Prompt", "cmd.exe");
                            }

                            if (StartupHelper.GetPreference("HideMainOnCliOpen", false))
                            {
                                form.HideMainFormNow();
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
#else
                    Platform.PlatformHelper.OpenUrlInBrowser("terminal");
#endif
                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "minimize_to_tray":
#if WINDOWS
                if (_mainForm is Form fTray && fTray.IsHandleCreated && !fTray.IsDisposed)
                {
                    fTray.Invoke(() =>
                    {
                        var allowTray = StartupHelper.GetPreference("MinimizeToTray", true);
                        if (allowTray)
                        {
                            fTray.Hide();
                            (fTray as MainForm)?.ShowTrayBalloon("Agents Hub", "Agents Hub is running in the background.");
                        }
                        else
                        {
                            fTray.WindowState = FormWindowState.Minimized;
                        }
                    });
                }
#endif
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
                    if (stProp.TryGetProperty("globalHotkey", out var hotkeyEl))
                    {
                        StartupHelper.SetPreference("GlobalHotkey", hotkeyEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("globalHotkeyModifier", out var ghModEl))
                    {
                        StartupHelper.SetStringPreference("GlobalHotkeyModifier", ghModEl.GetString() ?? "Ctrl+Alt");
                    }
                    if (stProp.TryGetProperty("globalHotkeyKey", out var ghKeyEl))
                    {
                        StartupHelper.SetStringPreference("GlobalHotkeyKey", ghKeyEl.GetString() ?? "H");
                    }
#if WINDOWS
                    if (_mainForm is MainForm mainForm)
                    {
                        try
                        {
                            mainForm.Invoke(() => mainForm.ApplyGlobalHotkey());
                        }
                        catch
                        {
                        }
                    }
#endif
                    if (stProp.TryGetProperty("globalHudHotkey", out var hudHotkeyEl))
                    {
                        StartupHelper.SetPreference("GlobalHudHotkey", hudHotkeyEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("hudHotkeyKey", out var hudKeyEl))
                    {
                        StartupHelper.SetStringPreference("HudHotkeyKey", hudKeyEl.GetString() ?? "F12");
                    }
                    if (stProp.TryGetProperty("hudHotkeyModifier", out var hudModEl))
                    {
                        StartupHelper.SetStringPreference("HudHotkeyModifier", hudModEl.GetString() ?? "None");
                    }
                    if (stProp.TryGetProperty("hudHotkeyTarget", out var hudTargetEl))
                    {
                        StartupHelper.SetStringPreference("HudHotkeyTarget", hudTargetEl.GetString() ?? "cliwebview");
                    }
#if WINDOWS
                    if (_mainForm is MainForm mainFormHud)
                    {
                        try
                        {
                            mainFormHud.Invoke(() => mainFormHud.ApplyGlobalHotkey());
                        }
                        catch
                        {
                        }
                    }
#endif
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
                    if (stProp.TryGetProperty("hideMainOnCliOpen", out var hideMainEl))
                    {
                        StartupHelper.SetPreference("HideMainOnCliOpen", hideMainEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("enableOpenCodeQuota", out var opencodeEl))
                    {
                        StartupHelper.SetPreference("EnableOpenCodeQuota", opencodeEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("enableProtonVpnProvider", out var protonEl))
                    {
                        StartupHelper.SetPreference("EnableProtonVpnProvider", protonEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("enableHardwareMonitor", out var hwEl))
                    {
                        StartupHelper.SetPreference("EnableHardwareMonitor", hwEl.GetBoolean());
                    }
                    if (stProp.TryGetProperty("enableArchitectureGraph", out var archEl))
                    {
                        StartupHelper.SetPreference("EnableArchitectureGraph", archEl.GetBoolean());
                    }
                    // The payload is a partial patch: most callers save one checkbox and send no
                    // theme at all. Only touch the theme when a value was actually supplied. Running
                    // SetTheme unconditionally defaulted the theme to Dark, so toggling any unrelated
                    // preference silently reset the app and rewrote the registry to dark while the
                    // dashboard kept its own light value in browser storage.
                    string? themeStr = null;
                    if (stProp.TryGetProperty("theme", out var themeEl))
                    {
                        var rawTheme = themeEl.GetString();
                        if (!string.IsNullOrWhiteSpace(rawTheme))
                        {
                            themeStr = rawTheme;
                            StartupHelper.SetStringPreference("Theme", rawTheme);
                        }
                    }
                    string? accentStr = null;
                    if (stProp.TryGetProperty("accentColor", out var colEl))
                    {
                        var rawAccent = colEl.GetString();
                        if (!string.IsNullOrWhiteSpace(rawAccent))
                        {
                            accentStr = rawAccent;
                            StartupHelper.SetStringPreference("AccentColor", rawAccent);
                        }
                    }

                    if (themeStr != null || accentStr != null)
                    {
                        var appTheme = themeStr == null
                            ? ThemeManager.CurrentTheme
                            : (themeStr.Equals("light", StringComparison.OrdinalIgnoreCase) ? AppTheme.Light : AppTheme.Dark);
                        ThemeManager.SetTheme(appTheme, accentStr);

#if WINDOWS
                        if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
                        {
                            f.Invoke(() =>
                            {
                                if (_cliWebviewForm != null && !_cliWebviewForm.IsDisposed)
                                {
                                    _cliWebviewForm.ApplyTheme();
                                }
                            });
                        }
#endif
                    }

                    SendResponse(requestId, true, new { success = true });
                }
                break;

            case "save_shortcuts":
                if (data.TryGetProperty("shortcuts", out var scProp))
                {
                    var scList = JsonSerializer.Deserialize<List<DashboardShortcut>>(scProp.GetRawText(), CaseInsensitiveJsonOptions);
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
#if WINDOWS
                if (_mainForm is Form fShort && fShort.IsHandleCreated && !fShort.IsDisposed)
                {
                    fShort.Invoke(() =>
                    {
                        using var ofd = new OpenFileDialog
                        {
                            Title = "Select Application or Script Executable",
                            Filter = "Executable & Scripts (*.exe;*.cmd;*.bat;*.ps1)|*.exe;*.cmd;*.bat;*.ps1|All Files (*.*)|*.*",
                            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                        };
                        if (ofd.ShowDialog(fShort) == DialogResult.OK)
                        {
                            selectedFile = ofd.FileName;
                        }
                    });
                }
#endif
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
                    bool isRunning = ProtonVpnService.IsClientRunning();
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
                    // The payload may still carry a session id; the implementation never used it.
                    string? sDir = data.TryGetProperty("directory", out var dEl) ? dEl.GetString() : (data.TryGetProperty("path", out var pEl) ? pEl.GetString() : null);
                    var ok = FocusOpenCodeSession(sDir);
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
        var systemPrompt = data.TryGetProperty("systemPrompt", out var spProp) ? spProp.GetString() : null;

        double? temperature = null;
        if (data.TryGetProperty("temperature", out var tProp) &&
            tProp.ValueKind == JsonValueKind.Number &&
            tProp.TryGetDouble(out var tVal))
        {
            temperature = tVal;
        }

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
                systemPrompt,
                temperature,
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

    /// <summary>
    /// Installs every missing dependency in sequence.
    ///
    /// The per-tool installer already exists and streams its own progress events, so this only
    /// has to serialise the queue and report where it is. Sequential rather than parallel because
    /// two winget installs at once fight over the same package manager and the output becomes
    /// impossible to read.
    /// </summary>
    private async Task RunAllDependencyInstallsAsync(List<DependencyDefinition> queue)
    {
        var succeeded = new List<string>();
        var failed = new List<string>();

        for (int i = 0; i < queue.Count; i++)
        {
            var definition = queue[i];
            PostEventToUi("dependency_install_all_progress", new
            {
                index = i,
                total = queue.Count,
                id = definition.Id,
                name = definition.Name
            });

            try
            {
                await RunDependencyInstallAsync(definition);

                // RunDependencyInstallAsync reports its own completion; re-reading the status is
                // the only honest way to know whether it actually took.
                var after = await DependencyCheckerService.GetDependenciesStatusAsync();
                var status = after.FirstOrDefault(s => string.Equals(s.Id, definition.Id, StringComparison.OrdinalIgnoreCase));

                if (status?.IsInstalled == true) succeeded.Add(definition.Name);
                else failed.Add(definition.Name);
            }
            catch (Exception ex)
            {
                failed.Add(definition.Name);
                PostDependencyInstallComplete(definition.Id, false, ex.Message, null);
            }
        }

        PostEventToUi("dependency_install_all_complete", new
        {
            total = queue.Count,
            succeeded,
            failed
        });
    }

    /// <summary>
    /// Starts an external target. Returns the outcome rather than showing a dialog, so the
    /// dashboard can report it through the same toast channel as everything else.
    /// </summary>
    private static (bool Launched, string Message) LaunchExecutableTarget(
        string target, string toolName, string arguments = "", string workingDirectory = "")
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return (false, $"No executable path is set for {toolName}.");
        }

        // Backstop for a quoted path that reached here without passing through config load.
        var resolved = DevToolTargets.Normalize(target);

        try
        {
            if (resolved.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                // A UWP entry point is handed to explorer, which ignores arguments and a
                // working directory of its own, so neither is applied here.
                Process.Start(new ProcessStartInfo("explorer.exe", resolved) { UseShellExecute = true });
            }
            else
            {
                var startInfo = new ProcessStartInfo(resolved) { UseShellExecute = true };

                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    startInfo.Arguments = arguments.Trim();
                }

                if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory.Trim()))
                {
                    startInfo.WorkingDirectory = workingDirectory.Trim();
                }

                Process.Start(startInfo);
            }
            return (true, $"{toolName} started.");
        }
        catch (Exception ex)
        {
            return (false, $"{toolName} could not start: {ex.Message}");
        }
    }

    private static void LaunchToolApp(string toolName)
    {
        var target = AiConfigService.Instance.GetToolTarget(toolName, string.Empty);
        _ = LaunchExecutableTarget(target, toolName);
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
#if WINDOWS
            MessageBox.Show($"Failed to launch shortcut '{target}': {ex.Message}", "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
            Console.WriteLine($"Failed to launch shortcut '{target}': {ex.Message}");
#endif
        }
    }

#if WINDOWS
    // LibraryImport, not DllImport: no parameters and an IntPtr return need no marshalling code,
    // so the source generator can emit the call. The rest of the DllImport sites stay as they are
    // until their struct marshalling is checked against the native signatures.
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    public CliWebviewForm? CliWebviewForm => _cliWebviewForm;

    public void ToggleOrShowCliWebview()
    {
        if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
        {
            f.Invoke(() =>
            {
                try
                {
                    if (_cliWebviewForm == null || _cliWebviewForm.IsDisposed)
                    {
                        var created = EnsureStandaloneTerminalForm();
                        created.ApplyTheme();
                        created.Show();
                        created.BringToFront();
                        created.Activate();
                        return;
                    }

                    IntPtr fg = GetForegroundWindow();
                    if (_cliWebviewForm.Visible && _cliWebviewForm.WindowState != FormWindowState.Minimized && fg == _cliWebviewForm.Handle)
                    {
                        _cliWebviewForm.Hide();
                    }
                    else
                    {
                        _cliWebviewForm.ApplyTheme();
                        if (_cliWebviewForm.WindowState == FormWindowState.Minimized)
                        {
                            _cliWebviewForm.WindowState = FormWindowState.Normal;
                        }
                        _cliWebviewForm.Show();
                        _cliWebviewForm.BringToFront();
                        _cliWebviewForm.Activate();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DashboardWebBridge] ToggleOrShowCliWebview error: {ex.Message}");
                }
            });
        }
    }
#endif

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
            version = AppInfo.DisplayVersion,
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
                globalHotkey = StartupHelper.GetPreference("GlobalHotkey", true),
                globalHotkeyModifier = StartupHelper.GetStringPreference("GlobalHotkeyModifier", "Ctrl+Alt"),
                globalHotkeyKey = StartupHelper.GetStringPreference("GlobalHotkeyKey", "H"),
                globalHudHotkey = StartupHelper.GetPreference("GlobalHudHotkey", true),
                hudHotkeyKey = StartupHelper.GetStringPreference("HudHotkeyKey", "F12"),
                hudHotkeyModifier = StartupHelper.GetStringPreference("HudHotkeyModifier", "None"),
                hudHotkeyTarget = StartupHelper.GetStringPreference("HudHotkeyTarget", "cliwebview"),
                maskPublicIp = StartupHelper.GetPreference("MaskPublicIp", false),
                maskShortcutPaths = StartupHelper.GetPreference("MaskShortcutPaths", false),
                embedShellsInTerminalWorkspace = StartupHelper.GetPreference("EmbedShellsInTerminalWorkspace", false),
                hideMainOnCliOpen = StartupHelper.GetPreference("HideMainOnCliOpen", false),
                enableOpenCodeQuota = StartupHelper.GetPreference("EnableOpenCodeQuota", false),
                enableProtonVpnProvider = StartupHelper.GetPreference("EnableProtonVpnProvider", false),
                enableHardwareMonitor = StartupHelper.GetPreference("EnableHardwareMonitor", false),
                enableArchitectureGraph = StartupHelper.GetPreference("EnableArchitectureGraph", false),
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
                profiles = GetProfilesWithEmail(),
                model = "Gemini 3.8 Flash (High)"
            },
            opencode = GetOpenCodeState(),
            aiConfig = GetAiConfigDto(),
            workspaces = AiConfigService.Instance.GetWorkspaces(),
            activeWorkspaceId = AiConfigService.Instance.GetActiveWorkspaceId(),
            tokenTelemetry = ProxyTelemetryService.Instance.GetSummary(),
            terminalWorkspacePreset = AiConfigService.Instance.GetTerminalWorkspacePreset(),
            enabledClis = AiConfigService.Instance.GetCliWorkspaces().Where(w => w.IsEnabled).Select(w => AiConfigService.NormalizeCliId(w.Id)).ToList()
        };
    }

    /// <summary>
    /// The ports that should be reachable from another device: the gateway plus every forwarder
    /// that actually bound. An unbound forwarder is excluded because something else owns that
    /// port, so allowing it through the firewall would achieve nothing.
    /// </summary>
    private IReadOnlyList<int> GetRequiredLanPorts()
    {
        var proxy = _lanProxyService;
        if (proxy == null || !proxy.IsRunning) return [];

        var ports = new List<int> { proxy.ListenPort };
        ports.AddRange(proxy.GetForwarders().Where(f => f.Bound).Select(f => f.Port));
        return [.. ports.Distinct().Order()];
    }

    private async Task<FirewallStatus> GetFirewallStatusAsync(IReadOnlyList<int> requiredPorts, bool force)
    {
        if (!force && _cachedFirewall is { } cached && (DateTime.UtcNow - _cachedFirewallAt).TotalMilliseconds < FirewallCacheMs)
        {
            return cached;
        }

        await _firewallGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!force && _cachedFirewall is { } recheck && (DateTime.UtcNow - _cachedFirewallAt).TotalMilliseconds < FirewallCacheMs)
            {
                return recheck;
            }

            var status = await FirewallHelper
                .GetStatusAsync(requiredPorts, NetworkHelper.GetLocalIPv4())
                .ConfigureAwait(false);

            _cachedFirewall = status;
            _cachedFirewallAt = DateTime.UtcNow;
            return status;
        }
        finally
        {
            _firewallGate.Release();
        }
    }

    private void InvalidateFirewallCache()
    {
        _cachedFirewall = null;
        _cachedFirewallAt = DateTime.MinValue;
    }

    private object BuildLanStateDto(IReadOnlyList<int> requiredPorts)
    {
        var proxy = _lanProxyService;
        var forwarders = proxy?.GetForwarders() ?? [];
        var lanIp = NetworkHelper.GetLocalIPv4();

        return new
        {
            lanIp,
            gatewayPort = proxy?.ListenPort ?? StartupHelper.GetIntPreference("LanPort", 9191),
            targetPort = proxy?.TargetPort ?? _cliManager.HermesService.Port,
            running = proxy?.IsRunning ?? false,
            enabled = StartupHelper.GetPreference("LanProxyEnabled", true),
            bindError = proxy?.LastBindError ?? "",
            requiredPorts,
            forwarders = forwarders.Select(f => new
            {
                port = f.Port,
                label = f.Label,
                address = f.Address,
                bound = f.Bound,
                detail = f.Detail
            }).ToArray()
        };
    }

    private static object ToFirewallDto(FirewallStatus status, IReadOnlyList<int> requiredPorts)
    {
        return new
        {
            querySucceeded = status.QuerySucceeded,
            detail = status.Detail,
            state = status.StateFor(requiredPorts).ToString().ToLowerInvariant(),
            ruleExists = status.RuleExists,
            ruleEnabled = status.RuleEnabled,
            ruleInbound = status.RuleInbound,
            ruleAllow = status.RuleAllow,
            ruleProfile = status.RuleProfile,
            rulePorts = status.RulePorts,
            requiredPorts,
            missingPorts = status.MissingPorts(requiredPorts),
            domainEnabled = status.DomainEnabled,
            privateEnabled = status.PrivateEnabled,
            publicEnabled = status.PublicEnabled,
            activeProfile = status.ActiveProfile,
            firewallOnForActiveProfile = status.FirewallOnForActiveProfile,
            inboundBlocked = status.InboundBlockedByNetworkCategory,
            networkName = status.NetworkName,
            networkCategory = status.NetworkCategory,
            networkInterface = status.NetworkInterface
        };
    }

    private static object[] ToClientDtos(IReadOnlyList<ConnectedClientInfo>? clients)
    {
        if (clients == null || clients.Count == 0) return [];

        return [.. clients.Select(c => (object)new
        {
            ip = c.IpAddress,
            deviceName = c.DeviceName,
            hostName = c.HostName ?? "",
            userAgent = c.UserAgent ?? "",
            deviceType = LanProxyService.DetectDeviceType(c.UserAgent),
            activeConnections = c.ActiveConnections,
            firstSeen = c.FirstSeen.ToString("o"),
            lastActivity = c.LastActivity.ToString("o")
        })];
    }

    private List<object> GetRunningWebPorts(string lanIp, int hermesPort, int lanProxyPort)
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
                isRunning
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

    /// <summary>
    /// Applies a dashboard config payload as a patch instead of a replacement.
    ///
    /// The dashboard sends back only the fields it renders, so a blind replace reset every field
    /// the DTO does not emit. The Claude gateway settings are the live example: they are edited
    /// through their own actions, never appear in the DTO, and were silently wiped back to their
    /// defaults by any unrelated dashboard save. Copying only the properties actually present
    /// means a field the dashboard does not own cannot be clobbered by it.
    ///
    /// Reflection is safe here because the csproj roots the whole AgentsHub assembly
    /// (TrimmerRootAssembly) and reflection-based JSON is already enabled.
    /// </summary>
    private static void ApplyConfigPatch(AiConfig target, JsonElement payload)
    {
        var incoming = JsonSerializer.Deserialize<AiConfig>(payload.GetRawText(), CaseInsensitiveJsonOptions);
        if (incoming == null) return;

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in payload.EnumerateObject())
        {
            present.Add(property.Name);
        }

        foreach (var property in typeof(AiConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite) continue;

            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            if (!present.Contains(jsonName)) continue;

            property.SetValue(target, property.GetValue(incoming));
        }
    }

    /// <summary>
    /// The dev tool list with its launch health attached, so the page can show which tools
    /// actually point at something without asking the user to find out by clicking Launch.
    /// </summary>
    private static List<object> BuildDevToolDtos()
    {
        var cfg = AiConfigService.Instance.CurrentConfig;
        var list = new List<object>();

        foreach (var d in cfg.DevTools ?? [])
        {
            var health = DevToolTargets.Check(d.Target);
            list.Add(new
            {
                id = d.Id,
                name = d.Name,
                description = d.Description,
                target = d.Target,
                arguments = d.Arguments,
                workingDirectory = d.WorkingDirectory,
                isEnabled = d.IsEnabled,
                health = new
                {
                    state = health.State.ToString().ToLowerInvariant(),
                    detail = health.Detail,
                    resolvedTarget = health.ResolvedTarget,
                    kind = health.Kind,
                    launchable = health.Launchable
                }
            });
        }

        return list;
    }

    private static object GetAiConfigDto()
    {
        var cfg = AiConfigService.Instance.CurrentConfig;
        return new
        {
            selectedProviderId = cfg.SelectedProviderId,
            selectedModel = cfg.SelectedModel,
            toolTargets = cfg.ToolTargets,
            devTools = BuildDevToolDtos(),
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
            }).ToList(),
            promptEnhancerTemperature = cfg.PromptEnhancerTemperature,
            activePromptPresetId = cfg.ActivePromptPresetId,
            promptPresets = (cfg.PromptPresets ?? []).Select(p => new
            {
                id = p.Id,
                name = p.Name,
                systemPrompt = p.SystemPrompt,
                isBuiltIn = p.IsBuiltIn
            }).ToList(),
            // Shipped defaults, so the UI can offer a true "Reset" without duplicating the text.
            builtInPromptPresets = AiConfigService.BuildDefaultPromptPresets().Select(p => new
            {
                id = p.Id,
                name = p.Name,
                systemPrompt = p.SystemPrompt
            }).ToList(),
            // Round-trips through save_ai_config so a config save cannot reset the label.
            terminalWorkspacePreset = cfg.TerminalWorkspacePreset,
            // Must round-trip too: save_ai_config replaces the whole config, so omitting these
            // would silently drop every CLI workspace (including user-added ones) on any save.
            cliWorkspaces = (cfg.CliWorkspaces ?? []).Select(w => new
            {
                id = w.Id,
                name = w.Name,
                command = w.Command,
                workingDirectory = w.WorkingDirectory,
                port = w.Port,
                isWebUi = w.IsWebUi,
                isDefault = w.IsDefault,
                isEnabled = w.IsEnabled
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

    private static object ToDependencyDto(DependencyStatus status) => new
    {
        id = status.Id,
        name = status.Name,
        description = status.Description,
        category = status.Category,
        isInstalled = status.IsInstalled,
        version = status.Version,
        path = status.Path,
        installKind = status.InstallKind,
        siteUrl = status.SiteUrl,
        commandPreview = status.CommandPreview
    };

    /// <summary>
    /// Installs a dependency in the background, streaming installer output as
    /// dependency_install_progress events and finishing with dependency_install_complete.
    /// </summary>
    private async Task RunDependencyInstallAsync(DependencyDefinition definition)
    {
        var startedAt = DateTime.UtcNow;

        try
        {
            if (string.Equals(definition.InstallKind, "browser", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(definition.SiteUrl))
                {
                    PostDependencyInstallComplete(definition.Id, false, "No download page is configured for this tool.", null);
                    return;
                }

                Process.Start(new ProcessStartInfo(definition.SiteUrl) { UseShellExecute = true });
                PostDependencyInstallComplete(definition.Id, true, $"Opened {definition.SiteUrl} in your default browser.", null);
                return;
            }

            PostEventToUi("dependency_install_progress", new { id = definition.Id, line = $"Running: {definition.CommandPreview}" });

            // winget is an app-execution alias, so it has to be reached through cmd rather than
            // launched directly. PowerShell runs the downloaded install scripts, and Bypass is
            // what lets them run on machines whose execution policy would otherwise refuse them.
            bool useWinget = string.Equals(definition.InstallKind, "winget", StringComparison.OrdinalIgnoreCase);

            var startInfo = new ProcessStartInfo
            {
                FileName = useWinget ? "cmd.exe" : "powershell.exe",
                Arguments = useWinget
                    ? $"/c {definition.InstallCommand}"
                    : $"-NoProfile -ExecutionPolicy Bypass -Command \"{definition.InstallCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            using var process = new Process { StartInfo = startInfo };
            var buffer = new StringBuilder();

            process.OutputDataReceived += (_, e) => ForwardInstallLine(definition.Id, e.Data, buffer);
            process.ErrorDataReceived += (_, e) => ForwardInstallLine(definition.Id, e.Data, buffer);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                PostDependencyInstallComplete(definition.Id, false, "Installer timed out after 5 minutes.", null);
                return;
            }

            // Re-detect rather than trusting the exit code alone: winget returns non-zero for
            // "already installed", and a tool can land on PATH only after a shell restart.
            var status = DependencyCheckerService.GetStatus(definition.Id);
            bool installed = status is { IsInstalled: true };
            bool succeeded;
            string message;

            if (installed)
            {
                succeeded = true;
                message = process.ExitCode == 0
                    ? $"Installer finished in {(DateTime.UtcNow - startedAt).TotalSeconds:F0}s."
                    : $"Installer exited with code {process.ExitCode}, but {definition.Name} is now detected.";
            }
            else if (process.ExitCode == 0)
            {
                succeeded = false;
                message = "Installer finished, but the tool is still not resolvable. A shell restart may be required.";
            }
            else
            {
                succeeded = false;
                message = $"Installer exited with code {process.ExitCode}.";
            }

            PostDependencyInstallComplete(definition.Id, succeeded, message, status);
        }
        catch (Exception ex)
        {
            PostDependencyInstallComplete(definition.Id, false, ex.Message, null);
        }
    }

    private void ForwardInstallLine(string dependencyId, string? line, StringBuilder buffer)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (buffer.Length < 4000)
        {
            buffer.AppendLine(line);
        }

        PostEventToUi("dependency_install_progress", new { id = dependencyId, line });
    }

    private void PostDependencyInstallComplete(string dependencyId, bool success, string message, DependencyStatus? status)
    {
        PostEventToUi("dependency_install_complete", new
        {
            id = dependencyId,
            success,
            message,
            dependency = status == null ? null : ToDependencyDto(status)
        });
    }

    private void SendJsonMessageToClients(string envelope)
    {
#if WINDOWS
        if (_coreWebView != null)
        {
            if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed && f.InvokeRequired)
            {
                f.BeginInvoke(new Action(() =>
                {
                    try { _coreWebView?.PostWebMessageAsJson(envelope); } catch { }
                }));
            }
            else
            {
                _coreWebView?.PostWebMessageAsJson(envelope);
            }
        }
#endif
        WebMessageBroadcast?.Invoke(envelope);
    }

    private void PostEventToUi(string eventType, object payload)
    {
        try
        {
            var envelope = JsonSerializer.Serialize(new
            {
                type = eventType == "opencode_interactive_prompt" ? "OPENCODE_INTERACTIVE_PROMPT" : eventType,
                @event = eventType,
                payload
            }, CamelCaseJsonOptions);

            SendJsonMessageToClients(envelope);
        }
        catch { }
    }

    public void EmitLog(string source, string level, string message)
    {
        PostEventToUi("live_log_entry", new
        {
            timestamp = DateTime.Now.ToString("HH:mm:ss"),
            tag = source,
            level,
            text = $"[{source}] {message}"
        });
    }

    private void SendResponse(string? requestId, bool success, object? data)
    {
        try
        {
            var envelope = JsonSerializer.Serialize(new
            {
                responseId = requestId,
                success,
                data
            }, CamelCaseJsonOptions);

            SendJsonMessageToClients(envelope);
        }
        catch { }
    }

    private void PostOpenCodeUpdate(OpenCodeStateDto stateDto)
    {
        try
        {
            var envelope = JsonSerializer.Serialize(new
            {
                type = "OPENCODE_UPDATE",
                data = stateDto,
                @event = "opencode_status_changed",
                payload = stateDto
            }, CamelCaseJsonOptions);

            SendJsonMessageToClients(envelope);
        }
        catch { }
    }

    public OpenCodeStateDto GetOpenCodeState()
    {
        return _openCodeWatcher?.GetStateDto() ?? new OpenCodeStateDto();
    }

    // Static because it reads no instance state, which is also why CA1822 flagged it.
    public static bool FocusOpenCodeSession(string? directory)
    {
        return OpenCodeWatcherService.FocusOpenCodeSession(directory);
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
        string scriptPath = Path.Combine(baseDir, "scripts", "fixquota-command.js");
        if (!File.Exists(scriptPath))
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "fixquota-command.js");
            if (File.Exists(fallback)) scriptPath = fallback;
            else
            {
                var legacy = Path.Combine(baseDir, "dist", "fixquota-command.js");
                if (File.Exists(legacy)) scriptPath = legacy;
            }
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
                        old = new { ipv4 = oldIpv4, ipv6 = oldIpv6, country },
                        @new = new { ipv4 = newIpv4, ipv6 = newIpv6, country, flag = countryCode },
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
        string scriptPath = Path.Combine(baseDir, "scripts", "fixquota-command.js");
        if (!File.Exists(scriptPath))
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "fixquota-command.js");
            if (File.Exists(fallback)) scriptPath = fallback;
            else
            {
                var legacy = Path.Combine(baseDir, "dist", "fixquota-command.js");
                if (File.Exists(legacy)) scriptPath = legacy;
            }
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
        await ProtonVpnService.DisconnectAsync();
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
        return [.. _cliManager.ProfileService.GetProfiles()
            .Select(name => (object)new
            {
                name,
                email = AntigravityProfileService.GetAccountEmail(name) ?? string.Empty,
                model = "Gemini 3.8 Flash (High)",
                isRunning = _cliManager.IsProfileRunning(name) || Sidecar.SidecarHostService.Instance.IsProfileActive(name),
                port = _cliManager.GetPortForProfile(name)
            })];
    }

    public void Dispose()
    {
#if WINDOWS
        _streamFlushTimer.Stop();
        _metricsTimer.Stop();
#endif
        _streamFlushTimer.Dispose();
        _metricsTimer.Dispose();
        // NetworkTelemetryService owns its own process-wide sampler and outlives this bridge.
        _promptEnhanceCts?.Dispose();
    }
}
