#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentsHub.Services;

public sealed class PocketCommanderConfig
{
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = false;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "telegram"; // "telegram", "discord", "both"

    // Telegram Bot
    [JsonPropertyName("telegramBotToken")]
    public string TelegramBotToken { get; set; } = string.Empty;

    [JsonPropertyName("authorizedChatId")]
    public long AuthorizedChatId { get; set; } = 0;

    [JsonPropertyName("authorizedUsername")]
    public string AuthorizedUsername { get; set; } = string.Empty;

    [JsonPropertyName("pairingPin")]
    public string PairingPin { get; set; } = string.Empty;

    // Discord Bot
    [JsonPropertyName("discordBotToken")]
    public string DiscordBotToken { get; set; } = string.Empty;

    [JsonPropertyName("discordAuthorizedUserId")]
    public string DiscordAuthorizedUserId { get; set; } = string.Empty;

    [JsonPropertyName("discordAuthorizedUsername")]
    public string DiscordAuthorizedUsername { get; set; } = string.Empty;

    [JsonPropertyName("discordChannelId")]
    public string DiscordChannelId { get; set; } = string.Empty;

    [JsonPropertyName("discordPairingPin")]
    public string DiscordPairingPin { get; set; } = string.Empty;

    // Permissions & Filters
    [JsonPropertyName("allowPrompts")]
    public bool AllowPrompts { get; set; } = true;

    [JsonPropertyName("allowScreenshots")]
    public bool AllowScreenshots { get; set; } = true;

    [JsonPropertyName("allowGitDiff")]
    public bool AllowGitDiff { get; set; } = true;

    [JsonPropertyName("allowCancel")]
    public bool AllowCancel { get; set; } = true;

    [JsonPropertyName("notifyOnTaskDone")]
    public bool NotifyOnTaskDone { get; set; } = true;

    [JsonPropertyName("notifyOnError")]
    public bool NotifyOnError { get; set; } = true;

    [JsonPropertyName("notifyOnApproval")]
    public bool NotifyOnApproval { get; set; } = true;
}

public sealed class PocketCommanderService : IDisposable
{
    public static PocketCommanderService Instance { get; } = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly string _configPath;
    private PocketCommanderConfig _config = new();
    private CliManagerService? _cliManager;

    // Telegram Polling
    private CancellationTokenSource? _telegramCts;
    private Task? _telegramTask;
    private long _lastTelegramUpdateId = 0;

    // Discord Gateway WebSocket
    private CancellationTokenSource? _discordCts;
    private Task? _discordTask;
    private string _discordApplicationId = string.Empty;

    private readonly Lock _lock = new();

    public event Action<string, object>? StatusChanged;
    public event Action<string, string>? CommandReceived;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PocketCommanderService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentsHub");
        Directory.CreateDirectory(appData);
        _configPath = Path.Combine(appData, "pocket_commander.json");

        LoadConfig();

        if (_config.IsEnabled)
        {
            StartServices();
        }
    }

    public void Initialize(CliManagerService cliManager)
    {
        _cliManager = cliManager;
    }

    public PocketCommanderConfig GetConfig()
    {
        lock (_lock)
        {
            bool needSave = false;
            if (string.IsNullOrWhiteSpace(_config.PairingPin))
            {
                _config.PairingPin = GenerateNewPin();
                needSave = true;
            }
            if (string.IsNullOrWhiteSpace(_config.DiscordPairingPin))
            {
                _config.DiscordPairingPin = GenerateNewPin();
                needSave = true;
            }
            if (needSave) SaveConfigInternal();

            return _config;
        }
    }

    public void SaveConfig(PocketCommanderConfig cfg)
    {
        lock (_lock)
        {
            _config = cfg;
            if (string.IsNullOrWhiteSpace(_config.PairingPin))
            {
                _config.PairingPin = GenerateNewPin();
            }
            if (string.IsNullOrWhiteSpace(_config.DiscordPairingPin))
            {
                _config.DiscordPairingPin = GenerateNewPin();
            }
            SaveConfigInternal();

            if (_config.IsEnabled)
            {
                StartServices();
            }
            else
            {
                StopServices();
            }
        }
    }

    public string GenerateNewPairingPin(string platform = "telegram")
    {
        lock (_lock)
        {
            string pin = GenerateNewPin();
            if (platform.Equals("discord", StringComparison.OrdinalIgnoreCase))
            {
                _config.DiscordPairingPin = pin;
            }
            else
            {
                _config.PairingPin = pin;
            }
            SaveConfigInternal();
            return pin;
        }
    }

    public void UnpairDevice(string platform = "all")
    {
        lock (_lock)
        {
            if (platform.Equals("all", StringComparison.OrdinalIgnoreCase) || platform.Equals("telegram", StringComparison.OrdinalIgnoreCase))
            {
                _config.AuthorizedChatId = 0;
                _config.AuthorizedUsername = string.Empty;
                _config.PairingPin = GenerateNewPin();
            }
            if (platform.Equals("all", StringComparison.OrdinalIgnoreCase) || platform.Equals("discord", StringComparison.OrdinalIgnoreCase))
            {
                _config.DiscordAuthorizedUserId = string.Empty;
                _config.DiscordAuthorizedUsername = string.Empty;
                _config.DiscordChannelId = string.Empty;
                _config.DiscordPairingPin = GenerateNewPin();
            }
            SaveConfigInternal();
        }
    }

    public async Task<(bool success, string botUsername, string errorMessage)> TestTelegramBotConnectionAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, "", "Telegram Bot Token is empty.");

        try
        {
            string url = $"https://api.telegram.org/bot{token.Trim()}/getMe";
            var res = await _http.GetAsync(url).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
                return (false, "", $"Telegram API returned HTTP {(int)res.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
            {
                string username = "";
                if (doc.RootElement.TryGetProperty("result", out var resProp))
                {
                    if (resProp.TryGetProperty("username", out var uProp)) username = uProp.GetString() ?? "";
                }
                return (true, username, "");
            }

            return (false, "", "Telegram API returned ok: false");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    public async Task<(bool success, string botUsername, string errorMessage)> TestDiscordBotConnectionAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, "", "Discord Bot Token is empty.");

        try
        {
            string url = "https://discord.com/api/v10/users/@me";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token.Trim()}");

            var res = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
                return (false, "", $"Discord API returned HTTP {(int)res.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            string username = "";
            if (doc.RootElement.TryGetProperty("username", out var uProp))
            {
                username = uProp.GetString() ?? "";
            }

            return (true, username, "");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    public void StartServices()
    {
        StopServices();

        bool hasTelegram = !string.IsNullOrWhiteSpace(_config.TelegramBotToken);
        bool hasDiscord = !string.IsNullOrWhiteSpace(_config.DiscordBotToken);

        bool runTelegram = (_config.Platform == "telegram" || _config.Platform == "both" || (!hasDiscord && hasTelegram)) && hasTelegram;
        bool runDiscord = (_config.Platform == "discord" || _config.Platform == "both" || (!hasTelegram && hasDiscord)) && hasDiscord;

        if (runTelegram)
        {
            _telegramCts = new CancellationTokenSource();
            _telegramTask = Task.Run(() => TelegramPollingLoopAsync(_telegramCts.Token));
        }

        if (runDiscord)
        {
            _discordCts = new CancellationTokenSource();
            _discordTask = Task.Run(() => DiscordGatewayLoopAsync(_discordCts.Token));
        }

        StatusChanged?.Invoke("services_started", new { isTelegramActive = runTelegram, isDiscordActive = runDiscord });
    }

    public void StopServices()
    {
        if (_telegramCts != null)
        {
            try { _telegramCts.Cancel(); _telegramCts.Dispose(); } catch { }
            _telegramCts = null;
        }

        if (_discordCts != null)
        {
            try { _discordCts.Cancel(); _discordCts.Dispose(); } catch { }
            _discordCts = null;
        }

        StatusChanged?.Invoke("services_stopped", new { });
    }

    // =========================================================================
    // TELEGRAM BOT LONG-POLLING ENGINE
    // =========================================================================

    private async Task TelegramPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string token = _config.TelegramBotToken.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    await Task.Delay(2000, ct).ConfigureAwait(false);
                    continue;
                }

                string url = $"https://api.telegram.org/bot{token}/getUpdates?offset={_lastTelegramUpdateId + 1}&timeout=20";
                var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                        doc.RootElement.TryGetProperty("result", out var resultsProp) && resultsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var update in resultsProp.EnumerateArray())
                        {
                            if (update.TryGetProperty("update_id", out var idProp))
                            {
                                _lastTelegramUpdateId = idProp.GetInt64();
                            }

                            await ProcessTelegramUpdateAsync(update, ct).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    await Task.Delay(3000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(3000, ct).ConfigureAwait(false); }
        }
    }

    private async Task ProcessTelegramUpdateAsync(JsonElement update, CancellationToken ct)
    {
        try
        {
            // 1. Callback Queries (Inline Keyboard Buttons)
            if (update.TryGetProperty("callback_query", out var cbQuery))
            {
                string queryId = cbQuery.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? "") : "";
                string data = cbQuery.TryGetProperty("data", out var dProp) ? (dProp.GetString() ?? "") : "";
                long chatId = 0;
                if (cbQuery.TryGetProperty("message", out var msgProp) &&
                    msgProp.TryGetProperty("chat", out var chatProp) &&
                    chatProp.TryGetProperty("id", out var cIdProp))
                {
                    chatId = cIdProp.GetInt64();
                }

                if (chatId == 0 || _config.AuthorizedChatId == 0 || chatId != _config.AuthorizedChatId)
                {
                    await AnswerTelegramCallbackAsync(queryId, "Unauthorized device.", ct).ConfigureAwait(false);
                    return;
                }

                await AnswerTelegramCallbackAsync(queryId, "Processing command...", ct).ConfigureAwait(false);
                await ExecuteCommandForPlatformAsync("telegram", chatId.ToString(), data.Replace("action:", "/"), ct).ConfigureAwait(false);
                return;
            }

            // 2. Direct Message
            if (!update.TryGetProperty("message", out var msg)) return;
            if (!msg.TryGetProperty("chat", out var cProp) || !cProp.TryGetProperty("id", out var cidProp)) return;

            long mChatId = cidProp.GetInt64();
            string text = msg.TryGetProperty("text", out var tProp) ? (tProp.GetString() ?? "").Trim() : "";
            string username = "";
            if (msg.TryGetProperty("from", out var fromProp))
            {
                if (fromProp.TryGetProperty("username", out var uProp)) username = uProp.GetString() ?? "";
                else if (fromProp.TryGetProperty("first_name", out var fProp)) username = fProp.GetString() ?? "User";
            }

            if (string.IsNullOrWhiteSpace(text)) return;

            CommandReceived?.Invoke(text, $"Telegram: {username}");

            bool isAuthorized = (_config.AuthorizedChatId != 0 && _config.AuthorizedChatId == mChatId);

            if (!isAuthorized)
            {
                string cleanInput = text.Replace("-", "").Replace(" ", "").Trim();
                string cleanPin = _config.PairingPin.Replace("-", "").Replace(" ", "").Trim();

                if (!string.IsNullOrWhiteSpace(cleanPin) &&
                    (string.Equals(cleanInput, cleanPin, StringComparison.OrdinalIgnoreCase) || cleanInput.EndsWith(cleanPin, StringComparison.OrdinalIgnoreCase)))
                {
                    lock (_lock)
                    {
                        _config.AuthorizedChatId = mChatId;
                        _config.AuthorizedUsername = string.IsNullOrWhiteSpace(username) ? $"ID:{mChatId}" : $"@{username}";
                        SaveConfigInternal();
                    }

                    string welcome = "🎉 *AgentsHub Pocket Commander Paired Successfully (Telegram)!*\n\n" +
                                     $"Authorized Device: *{_config.AuthorizedUsername}*\n\n" +
                                     "Available Commands:\n" +
                                     "• `/status` - Agent status & system health\n" +
                                     "• `/prompt <text>` - Inject prompt into active agent\n" +
                                     "• `/diff` - View working tree git diff\n" +
                                     "• `/screen` - Receive desktop screenshot\n" +
                                     "• `/stop` - Interrupt active agent session (SIGINT)\n" +
                                     "• `/help` - Show command cheat sheet";

                    await SendTelegramMessageAsync(mChatId, welcome, CreateMainInlineKeyboard(), ct).ConfigureAwait(false);
                    StatusChanged?.Invoke("telegram_device_paired", new { chatId = mChatId, username = _config.AuthorizedUsername });
                    return;
                }

                string unauthorizedMsg = "🔒 *AgentsHub Pocket Commander: Unauthorized Device*\n\n" +
                                         "To pair your phone with AgentsHub:\n" +
                                         "1. Open AgentsHub on your PC\n" +
                                         "2. Navigate to *Settings -> Webhooks & Discord -> Pocket Commander*\n" +
                                         "3. Reply to this bot with the **6-digit Telegram Pairing PIN**.";

                await SendTelegramMessageAsync(mChatId, unauthorizedMsg, null, ct).ConfigureAwait(false);
                return;
            }

            await ExecuteCommandForPlatformAsync("telegram", mChatId.ToString(), text, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Telegram Update Error: {ex.Message}");
        }
    }

    // =========================================================================
    // DISCORD BOT GATEWAY WEBSOCKET ENGINE (ZERO OPEN PORTS)
    // =========================================================================

    private async Task DiscordGatewayLoopAsync(CancellationToken ct)
    {
        // 37377 = GUILDS (1) | GUILD_MESSAGES (512) | DIRECT_MESSAGES (4096) | MESSAGE_CONTENT (32768)
        int currentIntents = 37377;

        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            using var wsSendLock = new SemaphoreSlim(1, 1);
            CancellationTokenSource? heartbeatCts = null;

            async Task SendWsPayloadAsync(object payload, CancellationToken sendCt)
            {
                await wsSendLock.WaitAsync(sendCt).ConfigureAwait(false);
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, sendCt).ConfigureAwait(false);
                    }
                }
                finally
                {
                    wsSendLock.Release();
                }
            }

            try
            {
                string token = _config.DiscordBotToken.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    await Task.Delay(3000, ct).ConfigureAwait(false);
                    continue;
                }

                await ws.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), ct).ConfigureAwait(false);

                byte[] buffer = new byte[8192];
                var sb = new StringBuilder();
                int? lastSequence = null;

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Check if close was due to Disallowed Intent (4014)
                        if ((int?)ws.CloseStatus == 4014)
                        {
                            Console.WriteLine("[DiscordGateway] Disallowed Intent (4014). Falling back to non-privileged intents (4609: Guilds + GuildMessages + DirectMessages).");
                            currentIntents = 4609; // GUILDS (1) | GUILD_MESSAGES (512) | DIRECT_MESSAGES (4096)
                        }
                        else if ((int?)ws.CloseStatus == 4004)
                        {
                            Console.WriteLine("[DiscordGateway] Authentication failed (4004). Token is invalid.");
                            StatusChanged?.Invoke("discord_error", new { error = "Invalid Discord Bot Token." });
                            await Task.Delay(15000, ct).ConfigureAwait(false);
                        }
                        break;
                    }

                    string jsonText = sb.ToString();
                    if (string.IsNullOrWhiteSpace(jsonText)) continue;

                    using var doc = JsonDocument.Parse(jsonText);
                    var root = doc.RootElement;

                    int op = root.TryGetProperty("op", out var opProp) ? opProp.GetInt32() : -1;
                    if (root.TryGetProperty("s", out var sProp) && sProp.ValueKind == JsonValueKind.Number)
                    {
                        lastSequence = sProp.GetInt32();
                    }

                    // Opcode 1: Server requested an immediate heartbeat
                    if (op == 1)
                    {
                        await SendWsPayloadAsync(new { op = 1, d = lastSequence }, ct).ConfigureAwait(false);
                    }
                    // Opcode 7 or 9: Reconnect / Invalid Session -> break to restart loop
                    else if (op == 7 || op == 9)
                    {
                        Console.WriteLine($"[DiscordGateway] Gateway requested reconnect (op: {op}). Reconnecting...");
                        break;
                    }
                    // Opcode 10: Hello -> Start heartbeat & Identify
                    else if (op == 10 && root.TryGetProperty("d", out var dProp))
                    {
                        int heartbeatInterval = dProp.TryGetProperty("heartbeat_interval", out var hbProp) ? hbProp.GetInt32() : 41250;

                        heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        _ = Task.Run(async () =>
                        {
                            while (!heartbeatCts.Token.IsCancellationRequested && ws.State == WebSocketState.Open)
                            {
                                try
                                {
                                    await Task.Delay(heartbeatInterval, heartbeatCts.Token).ConfigureAwait(false);
                                    await SendWsPayloadAsync(new { op = 1, d = lastSequence }, heartbeatCts.Token).ConfigureAwait(false);
                                }
                                catch { break; }
                            }
                        }, heartbeatCts.Token);

                        // Send Identify (Opcode 2) with Discord-compliant $ properties and online presence
                        var identifyPayload = new Dictionary<string, object>
                        {
                            ["op"] = 2,
                            ["d"] = new Dictionary<string, object>
                            {
                                ["token"] = token,
                                ["intents"] = currentIntents,
                                ["properties"] = new Dictionary<string, string>
                                {
                                    ["$os"] = "windows",
                                    ["$browser"] = "AgentsHub",
                                    ["$device"] = "AgentsHub"
                                },
                                ["presence"] = new Dictionary<string, object>
                                {
                                    ["status"] = "online",
                                    ["afk"] = false,
                                    ["activities"] = new object[]
                                    {
                                        new Dictionary<string, object>
                                        {
                                            ["name"] = "AgentsHub Remote",
                                            ["type"] = 0
                                        }
                                    }
                                }
                            }
                        };
                        await SendWsPayloadAsync(identifyPayload, ct).ConfigureAwait(false);
                    }
                    // Opcode 0: Dispatch Events
                    else if (op == 0)
                    {
                        string eventType = root.TryGetProperty("t", out var tProp) ? (tProp.GetString() ?? "") : "";
                        if (eventType == "READY")
                        {
                            Console.WriteLine("[DiscordGateway] Connected successfully. Bot status is Online.");
                            StatusChanged?.Invoke("discord_ready", new { status = "online" });

                            if (root.TryGetProperty("d", out var readyData))
                            {
                                string appId = "";
                                if (readyData.TryGetProperty("application", out var appProp) && appProp.TryGetProperty("id", out var idProp))
                                {
                                    appId = idProp.GetString() ?? "";
                                }
                                if (string.IsNullOrEmpty(appId) && readyData.TryGetProperty("user", out var uProp) && uProp.TryGetProperty("id", out var uidProp))
                                {
                                    appId = uidProp.GetString() ?? "";
                                }
                                if (!string.IsNullOrEmpty(appId))
                                {
                                    _discordApplicationId = appId;
                                    _ = Task.Run(() => RegisterDiscordSlashCommandsAsync(appId, token));
                                }
                            }
                        }
                        else if (eventType == "INTERACTION_CREATE" && root.TryGetProperty("d", out var interactionData))
                        {
                            var clonedData = interactionData.Clone();
                            _ = Task.Run(() => ProcessDiscordInteractionAsync(clonedData, ct));
                        }
                        else if (eventType == "GUILD_CREATE" && root.TryGetProperty("d", out var guildData))
                        {
                            string gId = guildData.TryGetProperty("id", out var gid) ? (gid.GetString() ?? "") : "";
                            if (!string.IsNullOrEmpty(gId) && !string.IsNullOrEmpty(_discordApplicationId))
                            {
                                _ = Task.Run(() => ClearGuildSlashCommandsDirectAsync(_discordApplicationId, gId, token));
                            }
                        }
                        else if (eventType == "MESSAGE_CREATE" && root.TryGetProperty("d", out var msgData))
                        {
                            await ProcessDiscordMessageAsync(msgData, ct).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"Discord Gateway Error: {ex.Message}");
                await Task.Delay(4000, ct).ConfigureAwait(false);
            }
            finally
            {
                heartbeatCts?.Cancel();
                heartbeatCts?.Dispose();
            }
        }
    }

    private async Task ProcessDiscordMessageAsync(JsonElement msgData, CancellationToken ct)
    {
        try
        {
            if (msgData.TryGetProperty("author", out var authorProp))
            {
                if (authorProp.TryGetProperty("bot", out var bProp) && bProp.GetBoolean())
                    return; // Ignore bots
            }

            string authorId = authorProp.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? "") : "";
            string authorName = authorProp.TryGetProperty("username", out var uProp) ? (uProp.GetString() ?? "User") : "User";
            string channelId = msgData.TryGetProperty("channel_id", out var cProp) ? (cProp.GetString() ?? "") : "";
            string content = msgData.TryGetProperty("content", out var cntProp) ? (cntProp.GetString() ?? "").Trim() : "";

            if (string.IsNullOrWhiteSpace(channelId)) return;

            // Strip any bot user mention: e.g. <@1234567890> or <@!1234567890>
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<@!?[0-9]+>\s*", "").Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                // Check if the bot was mentioned without text or when MESSAGE_CONTENT intent is disabled
                if (msgData.TryGetProperty("mentions", out var mentionsProp) && mentionsProp.ValueKind == JsonValueKind.Array)
                {
                    bool botMentioned = false;
                    foreach (var m in mentionsProp.EnumerateArray())
                    {
                        if (m.TryGetProperty("id", out var mId) && mId.GetString() == _discordApplicationId)
                        {
                            botMentioned = true;
                            break;
                        }
                    }

                    if (botMentioned)
                    {
                        string hint = "👋 **AgentsHub Remote is active!**\n\n" +
                                      "• Type `/` in this channel to use **Slash Commands** (`/status`, `/prompt`, `/screen`, `/diff`, `/stop`).\n" +
                                      "• Or mention me with a command: `@AgentsHub status`, `@AgentsHub screen`, `@AgentsHub stop`.\n" +
                                      "• To use prefix commands like `!status` without @mention, enable **Message Content Intent** in Discord Developer Portal (`Bot -> Privileged Gateway Intents`).";
                        await SendDiscordMessageAsync(channelId, hint, ct: ct).ConfigureAwait(false);
                    }
                }
                return;
            }

            CommandReceived?.Invoke(content, $"Discord: {authorName}");

            bool isAuthorized = (!string.IsNullOrEmpty(_config.DiscordAuthorizedUserId) && _config.DiscordAuthorizedUserId == authorId);

            if (!isAuthorized)
            {
                string cleanInput = content.Replace("-", "").Replace(" ", "").Trim();
                string cleanPin = _config.DiscordPairingPin.Replace("-", "").Replace(" ", "").Trim();

                if (!string.IsNullOrWhiteSpace(cleanPin) && cleanInput.Contains(cleanPin, StringComparison.OrdinalIgnoreCase))
                {
                    lock (_lock)
                    {
                        _config.DiscordAuthorizedUserId = authorId;
                        _config.DiscordAuthorizedUsername = $"@{authorName}";
                        _config.DiscordChannelId = channelId;
                        SaveConfigInternal();
                    }

                    string welcome = "🎉 **AgentsHub Pocket Commander Paired Successfully (Discord)!**\n\n" +
                                     $"Authorized User: <@{authorId}>\n\n" +
                                     "Available Commands:\n" +
                                     "• `!status` or `/status` - Agent & system health\n" +
                                     "• `!prompt <text>` or `/prompt <text>` - Inject prompt into active agent\n" +
                                     "• `!diff` or `/diff` - View working tree git diff\n" +
                                     "• `!screen` or `/screen` - Receive desktop screenshot\n" +
                                     "• `!stop` or `/stop` - Interrupt active agent session (SIGINT)\n" +
                                     "• `!help` - Show command cheat sheet";

                    await SendDiscordMessageAsync(channelId, welcome, ct: ct).ConfigureAwait(false);
                    StatusChanged?.Invoke("discord_device_paired", new { userId = authorId, username = authorName, channelId });
                    return;
                }

                // If unauthorized and sent a command
                if (content.StartsWith('!') || content.StartsWith('/'))
                {
                    string unauthorizedMsg = "🔒 **AgentsHub Pocket Commander: Unauthorized Discord User**\n\n" +
                                             "To pair your Discord account with AgentsHub Desktop:\n" +
                                             "1. Open AgentsHub on your PC\n" +
                                             "2. Navigate to **Settings -> Webhooks & Discord -> Pocket Commander**\n" +
                                             "3. Select **Discord Bot** and reply to this bot with your **6-digit Discord Pairing PIN** (e.g. `" + _config.DiscordPairingPin + "`).";

                    await SendDiscordMessageAsync(channelId, unauthorizedMsg, ct: ct).ConfigureAwait(false);
                }
                return;
            }

            // If authorized, remember the latest channelId so alerts are pushed to where the user is active
            if (_config.DiscordChannelId != channelId)
            {
                lock (_lock)
                {
                    _config.DiscordChannelId = channelId;
                    SaveConfigInternal();
                }
            }

            await ExecuteCommandForPlatformAsync("discord", channelId, content, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Discord Message Processing Error: {ex.Message}");
        }
    }

    // =========================================================================
    // DISCORD NATIVE APPLICATION SLASH COMMANDS REGISTRATION & ROUTING
    // =========================================================================

    private async Task RegisterDiscordSlashCommandsAsync(string applicationId, string token)
    {
        try
        {
            string url = $"https://discord.com/api/v10/applications/{applicationId}/commands";
            var commands = new object[]
            {
                new
                {
                    name = "status",
                    description = "View active agent status and system health",
                    type = 1
                },
                new
                {
                    name = "prompt",
                    description = "Send a prompt instruction to the active agent",
                    type = 1,
                    options = new[]
                    {
                        new
                        {
                            name = "text",
                            description = "Instruction or prompt text to execute",
                            type = 3,
                            required = true
                        }
                    }
                },
                new
                {
                    name = "diff",
                    description = "View git diff summary for current workspace",
                    type = 1
                },
                new
                {
                    name = "screen",
                    description = "Capture a desktop screenshot",
                    type = 1
                },
                new
                {
                    name = "stop",
                    description = "Interrupt active agent session (SIGINT Ctrl+C)",
                    type = 1
                },
                new
                {
                    name = "pair",
                    description = "Pair this Discord account with AgentsHub",
                    type = 1,
                    options = new[]
                    {
                        new
                        {
                            name = "pin",
                            description = "6-digit pairing PIN shown in AgentsHub Settings",
                            type = 3,
                            required = true
                        }
                    }
                },
                new
                {
                    name = "help",
                    description = "Show AgentsHub command cheat sheet",
                    type = 1
                }
            };

            string jsonCommands = JsonSerializer.Serialize(commands);

            // 1. Register Global Slash Commands (used in DMs and global fallback)
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
            req.Content = new StringContent(jsonCommands, Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                Console.WriteLine("[Discord] Global slash commands registered successfully.");
            }
            else
            {
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Discord] Register global slash commands returned {(int)res.StatusCode}: {body}");
            }

            // 2. Clear any lingering guild-specific commands so commands never appear duplicated
            string guildsUrl = "https://discord.com/api/v10/users/@me/guilds";
            using var gReq = new HttpRequestMessage(HttpMethod.Get, guildsUrl);
            gReq.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
            var gRes = await _http.SendAsync(gReq).ConfigureAwait(false);
            if (gRes.IsSuccessStatusCode)
            {
                var gBody = await gRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var gDoc = JsonDocument.Parse(gBody);
                if (gDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var guild in gDoc.RootElement.EnumerateArray())
                    {
                        string gId = guild.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? "") : "";
                        if (!string.IsNullOrEmpty(gId))
                        {
                            await ClearGuildSlashCommandsDirectAsync(applicationId, gId, token).ConfigureAwait(false);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Failed to register slash commands: {ex.Message}");
        }
    }

    private async Task ClearGuildSlashCommandsDirectAsync(string applicationId, string guildId, string token)
    {
        try
        {
            string guildUrl = $"https://discord.com/api/v10/applications/{applicationId}/guilds/{guildId}/commands";
            using var req = new HttpRequestMessage(HttpMethod.Put, guildUrl);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");
            req.Content = new StringContent("[]", Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Discord] Purged duplicate guild commands for guild {guildId}.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Failed to clear guild commands for {guildId}: {ex.Message}");
        }
    }

    private async Task<bool> RespondDiscordInteractionAsync(string interactionId, string interactionToken, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(interactionId) || string.IsNullOrWhiteSpace(interactionToken)) return false;

        if (text.Length > 1980)
        {
            text = text[..1940] + "\n...[truncated]";
        }

        try
        {
            string url = $"https://discord.com/api/v10/interactions/{interactionId}/{interactionToken}/callback";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            var payload = new
            {
                type = 4, // CHANNEL_MESSAGE_WITH_SOURCE
                data = new
                {
                    content = text
                }
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Discord] Respond interaction error {(int)res.StatusCode}: {err}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Respond interaction exception: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> DeferDiscordInteractionAsync(string interactionId, string interactionToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(interactionId) || string.IsNullOrWhiteSpace(interactionToken)) return false;

        try
        {
            string url = $"https://discord.com/api/v10/interactions/{interactionId}/{interactionToken}/callback";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent("{\"type\":5}", Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Discord] Defer interaction error {(int)res.StatusCode}: {err}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Defer interaction exception: {ex.Message}");
            return false;
        }
    }

    private async Task SendDiscordInteractionFollowupAsync(string appId, string interactionToken, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId)) appId = _discordApplicationId;
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(interactionToken)) return;

        if (text.Length > 1980)
        {
            text = text[..1940] + "\n...[truncated]";
        }

        try
        {
            string url = $"https://discord.com/api/v10/webhooks/{appId}/{interactionToken}/messages/@original";
            using var req = new HttpRequestMessage(HttpMethod.Patch, url);
            var payload = new Dictionary<string, object> { ["content"] = text };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Discord] Interaction Followup failed {(int)res.StatusCode}: {err}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] SendInteractionFollowup Error: {ex.Message}");
        }
    }

    private async Task SendDiscordInteractionPhotoAsync(string appId, string channelId, string interactionToken, byte[] photoBytes, string caption, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId)) appId = _discordApplicationId;
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(interactionToken)) return;

        bool patched = false;
        try
        {
            string url = $"https://discord.com/api/v10/webhooks/{appId}/{interactionToken}/messages/@original";
            using var req = new HttpRequestMessage(HttpMethod.Patch, url);

            using var form = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(photoBytes);
            byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            form.Add(byteContent, "files[0]", "screen.jpg");

            var payload = new { content = caption };
            form.Add(new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), "payload_json");

            req.Content = form;
            var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            patched = res.IsSuccessStatusCode;
            if (!patched)
            {
                var err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Discord] Photo patch failed {(int)res.StatusCode}: {err}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Photo patch exception: {ex.Message}");
        }

        if (!patched)
        {
            await SendDiscordInteractionFollowupAsync(appId, interactionToken, caption, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(channelId))
            {
                await SendDiscordPhotoAsync(channelId, photoBytes, caption, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessDiscordInteractionAsync(JsonElement interactionData, CancellationToken ct)
    {
        try
        {
            int interactionType = interactionData.TryGetProperty("type", out var typProp) ? typProp.GetInt32() : 0;
            if (interactionType != 2) return; // 2 = APPLICATION_COMMAND

            string interactionId = interactionData.TryGetProperty("id", out var iidProp) ? (iidProp.GetString() ?? "") : "";
            string interactionToken = interactionData.TryGetProperty("token", out var tokProp) ? (tokProp.GetString() ?? "") : "";
            string channelId = interactionData.TryGetProperty("channel_id", out var cProp) ? (cProp.GetString() ?? "") : "";

            string appId = interactionData.TryGetProperty("application_id", out var aIdProp) ? (aIdProp.GetString() ?? "") : "";
            if (!string.IsNullOrEmpty(appId))
            {
                _discordApplicationId = appId;
            }
            else
            {
                appId = _discordApplicationId;
            }

            if (string.IsNullOrWhiteSpace(interactionId) || string.IsNullOrWhiteSpace(interactionToken)) return;

            JsonElement userElem = default;
            if (interactionData.TryGetProperty("member", out var memProp) && memProp.TryGetProperty("user", out var u1))
                userElem = u1;
            else if (interactionData.TryGetProperty("user", out var u2))
                userElem = u2;

            string authorId = userElem.ValueKind != JsonValueKind.Undefined && userElem.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? "") : "";
            string authorName = userElem.ValueKind != JsonValueKind.Undefined && userElem.TryGetProperty("username", out var nameProp) ? (nameProp.GetString() ?? "User") : "User";

            string commandName = "";
            var optionsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (interactionData.TryGetProperty("data", out var dataProp))
            {
                if (dataProp.TryGetProperty("name", out var cmdProp)) commandName = cmdProp.GetString() ?? "";
                if (dataProp.TryGetProperty("options", out var optsProp) && optsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var opt in optsProp.EnumerateArray())
                    {
                        string optName = opt.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                        string optVal = opt.TryGetProperty("value", out var vProp)
                            ? (vProp.ValueKind == JsonValueKind.String ? (vProp.GetString() ?? "") : vProp.ToString())
                            : "";
                        if (!string.IsNullOrEmpty(optName)) optionsMap[optName] = optVal;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(commandName)) return;

            CommandReceived?.Invoke("/" + commandName + (optionsMap.Count > 0 ? " " + string.Join(" ", optionsMap.Values) : ""), $"Discord: {authorName}");

            bool isAuthorized = (!string.IsNullOrEmpty(_config.DiscordAuthorizedUserId) && _config.DiscordAuthorizedUserId == authorId);

            if (!isAuthorized)
            {
                if (commandName.Equals("pair", StringComparison.OrdinalIgnoreCase))
                {
                    optionsMap.TryGetValue("pin", out var enteredPin);
                    string cleanInput = (enteredPin ?? "").Replace("-", "").Replace(" ", "").Trim();
                    string cleanPin = _config.DiscordPairingPin.Replace("-", "").Replace(" ", "").Trim();

                    if (!string.IsNullOrWhiteSpace(cleanPin) && cleanInput.Contains(cleanPin, StringComparison.OrdinalIgnoreCase))
                    {
                        lock (_lock)
                        {
                            _config.DiscordAuthorizedUserId = authorId;
                            _config.DiscordAuthorizedUsername = $"@{authorName}";
                            _config.DiscordChannelId = channelId;
                            SaveConfigInternal();
                        }

                        string welcome = "🎉 **AgentsHub Pocket Commander Paired Successfully (Discord)!**\n\n" +
                                         $"Authorized User: <@{authorId}>\n\n" +
                                         "Available Commands:\n" +
                                         "• `/status` (or `!status`) - Agent & system health\n" +
                                         "• `/prompt <text>` (or `!prompt <text>`) - Inject prompt into active agent\n" +
                                         "• `/diff` (or `!diff`) - View working tree git diff\n" +
                                         "• `/screen` (or `!screen`) - Receive desktop screenshot\n" +
                                         "• `/stop` (or `!stop`) - Interrupt active agent session (SIGINT)\n" +
                                         "• `/help` - Show command cheat sheet";

                        await RespondDiscordInteractionAsync(interactionId, interactionToken, welcome, ct).ConfigureAwait(false);
                        StatusChanged?.Invoke("discord_device_paired", new { userId = authorId, username = authorName, channelId });
                        return;
                    }
                    else
                    {
                        await RespondDiscordInteractionAsync(interactionId, interactionToken, "❌ Invalid Pairing PIN. Please check the 6-digit PIN in AgentsHub Settings.", ct).ConfigureAwait(false);
                        return;
                    }
                }

                string unauthorizedMsg = "🔒 **AgentsHub Pocket Commander: Unauthorized Discord User**\n\n" +
                                         "To pair your Discord account with AgentsHub Desktop:\n" +
                                         "1. Open AgentsHub on your PC\n" +
                                         "2. Navigate to **Settings -> Webhooks & Discord -> Pocket Commander**\n" +
                                         "3. Run `/pair pin:<6-digit-PIN>` or reply to this bot with your PIN (e.g. `" + _config.DiscordPairingPin + "`).";

                await RespondDiscordInteractionAsync(interactionId, interactionToken, unauthorizedMsg, ct).ConfigureAwait(false);
                return;
            }

            if (_config.DiscordChannelId != channelId)
            {
                lock (_lock)
                {
                    _config.DiscordChannelId = channelId;
                    SaveConfigInternal();
                }
            }

            // Authorized interaction commands
            if (commandName.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                string statusMsg = BuildStatusCommand();
                await RespondDiscordInteractionAsync(interactionId, interactionToken, statusMsg, ct).ConfigureAwait(false);
            }
            else if (commandName.Equals("prompt", StringComparison.OrdinalIgnoreCase))
            {
                optionsMap.TryGetValue("text", out var promptText);
                string reply = BuildPromptCommand(promptText ?? "");
                await RespondDiscordInteractionAsync(interactionId, interactionToken, reply, ct).ConfigureAwait(false);
            }
            else if (commandName.Equals("diff", StringComparison.OrdinalIgnoreCase))
            {
                string diffMsg = await BuildDiffCommandAsync(ct).ConfigureAwait(false);
                await RespondDiscordInteractionAsync(interactionId, interactionToken, diffMsg, ct).ConfigureAwait(false);
            }
            else if (commandName.Equals("screen", StringComparison.OrdinalIgnoreCase))
            {
                if (!_config.AllowScreenshots)
                {
                    await RespondDiscordInteractionAsync(interactionId, interactionToken, "⚠️ Remote screenshots are disabled in AgentsHub Settings.", ct).ConfigureAwait(false);
                    return;
                }

                // Screen takes a moment to capture and encode, so defer interaction first
                await DeferDiscordInteractionAsync(interactionId, interactionToken, ct).ConfigureAwait(false);

                var photoBytes = CaptureDesktopSnapshot();
                if (photoBytes != null && photoBytes.Length > 0)
                {
                    string caption = $"Desktop Snapshot ({DateTime.UtcNow:HH:mm:ss} UTC)";
                    await SendDiscordInteractionPhotoAsync(appId, channelId, interactionToken, photoBytes, caption, ct).ConfigureAwait(false);
                }
                else
                {
                    await SendDiscordInteractionFollowupAsync(appId, interactionToken, "❌ Failed to capture desktop screenshot.", ct).ConfigureAwait(false);
                }
            }
            else if (commandName.Equals("stop", StringComparison.OrdinalIgnoreCase) || commandName.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                string cancelReply = BuildCancelCommand();
                await RespondDiscordInteractionAsync(interactionId, interactionToken, cancelReply, ct).ConfigureAwait(false);
            }
            else if (commandName.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                await RespondDiscordInteractionAsync(interactionId, interactionToken, BuildHelpMessage(), ct).ConfigureAwait(false);
            }
            else
            {
                await RespondDiscordInteractionAsync(interactionId, interactionToken, "❓ Unknown command. Type `/help` or `!help` for available commands.", ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Process Interaction Error: {ex.Message}");
        }
    }

    // =========================================================================
    // UNIFIED COMMAND ROUTER (WORKS FOR BOTH TELEGRAM & DISCORD)
    // =========================================================================

    private async Task ExecuteCommandForPlatformAsync(string platform, string targetId, string rawCommand, CancellationToken ct)
    {
        string cmd = rawCommand.Trim();
        // Normalize leading ! to /
        if (cmd.StartsWith('!')) cmd = "/" + cmd[1..];
        if (!cmd.StartsWith('/'))
        {
            if (cmd.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("screen", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("diff", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                cmd = "/" + cmd;
            }
            else if (cmd.StartsWith("prompt ", StringComparison.OrdinalIgnoreCase))
            {
                cmd = "/" + cmd;
            }
        }

        if (cmd.StartsWith("/help", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await SendReplyAsync(platform, targetId, BuildHelpMessage(), ct).ConfigureAwait(false);
        }
        else if (cmd.StartsWith("/status", StringComparison.OrdinalIgnoreCase))
        {
            string msg = BuildStatusCommand();
            await SendReplyAsync(platform, targetId, msg, ct).ConfigureAwait(false);
        }
        else if (cmd.StartsWith("/prompt", StringComparison.OrdinalIgnoreCase))
        {
            string promptBody = cmd.Length > 7 ? cmd[7..].Trim() : "";
            string msg = BuildPromptCommand(promptBody);
            await SendReplyAsync(platform, targetId, msg, ct).ConfigureAwait(false);
        }
        else if (cmd.StartsWith("/diff", StringComparison.OrdinalIgnoreCase))
        {
            string msg = await BuildDiffCommandAsync(ct).ConfigureAwait(false);
            await SendReplyAsync(platform, targetId, msg, ct).ConfigureAwait(false);
        }
        else if (cmd.StartsWith("/screen", StringComparison.OrdinalIgnoreCase))
        {
            await HandleScreenshotCommandAsync(platform, targetId, ct).ConfigureAwait(false);
        }
        else if (cmd.StartsWith("/stop", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("/cancel", StringComparison.OrdinalIgnoreCase))
        {
            string msg = BuildCancelCommand();
            await SendReplyAsync(platform, targetId, msg, ct).ConfigureAwait(false);
        }
        else
        {
            if (_config.AllowPrompts && !cmd.StartsWith('/'))
            {
                string msg = BuildPromptCommand(cmd);
                await SendReplyAsync(platform, targetId, msg, ct).ConfigureAwait(false);
            }
            else
            {
                await SendReplyAsync(platform, targetId, "❓ Unknown command. Type `/help` or `!help` for available commands.", ct).ConfigureAwait(false);
            }
        }
    }

    private string BuildStatusCommand()
    {
        try
        {
            var curWs = AiConfigService.Instance.GetActiveWorkspace();
            string wsName = curWs?.Name ?? "HermesOn";
            string wsDir = curWs?.WorkingDirectory ?? AppDomain.CurrentDomain.BaseDirectory;

            var runningInfos = _cliManager?.GetAllInfos().Where(c => c.State == CliState.Running).ToList() ?? [];
            string agentStatus = runningInfos.Count > 0
                ? $"🟢 **Running ({runningInfos.Count} active)**\nAgents: `{string.Join(", ", runningInfos.Select(c => c.DisplayName))}`"
                : "⚪ **Idle (No agents actively running)**";

            return $"📊 **AgentsHub Desktop Status**\n\n" +
                   $"**Workspace:** `{wsName}`\n" +
                   $"**Directory:** `{wsDir}`\n\n" +
                   $"**Agent Engine:**\n{agentStatus}\n\n" +
                   $"**Host:** `{Environment.MachineName}`\n" +
                   $"**Time:** `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`";
        }
        catch (Exception ex)
        {
            return $"❌ Failed to get status: {ex.Message}";
        }
    }

    private string BuildPromptCommand(string prompt)
    {
        if (!_config.AllowPrompts)
        {
            return "⚠️ Remote prompt injection is disabled in AgentsHub Settings.";
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Usage: `/prompt <instruction for agent>`";
        }

        try
        {
            var running = _cliManager?.GetAllInfos().Where(c => c.State == CliState.Running).ToList() ?? [];
            string targetSessionId = running.Count > 0 ? running[0].Id : "antigravity";

            _cliManager?.SendInput(targetSessionId, prompt + "\n");

            return $"🚀 **Prompt Injected into [{targetSessionId}]**\n\n" +
                   $"> {prompt}\n\n" +
                   "Agent is processing your instruction. You will receive a push notification when complete.";
        }
        catch (Exception ex)
        {
            return $"❌ Failed to inject prompt: {ex.Message}";
        }
    }

    private async Task<string> BuildDiffCommandAsync(CancellationToken ct)
    {
        if (!_config.AllowGitDiff)
        {
            return "⚠️ Remote git diff is disabled in AgentsHub Settings.";
        }

        try
        {
            var curWs = AiConfigService.Instance.GetActiveWorkspace();
            string repoPath = curWs?.WorkingDirectory ?? AppDomain.CurrentDomain.BaseDirectory;

            var (statusRc, status) = await GitRepositoryManagerService.RunGitAsync(repoPath, ["status", "--short"], ct).ConfigureAwait(false);
            var (diffRc, diffStat) = await GitRepositoryManagerService.RunGitAsync(repoPath, ["diff", "--stat"], ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(diffStat))
            {
                return "🌳 **Git Working Tree is Clean**\nZero uncommitted changes in workspace.";
            }

            return "📄 **Workspace Git Status Summary:**\n\n" +
                   "```diff\n" +
                   (string.IsNullOrWhiteSpace(status) ? "Clean" : status.Trim()) +
                   "\n```\n\n" +
                   (string.IsNullOrWhiteSpace(diffStat) ? "" : "**Diff Stats:**\n```\n" + diffStat.Trim() + "\n```");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to inspect git diff: {ex.Message}";
        }
    }

    private string BuildCancelCommand()
    {
        if (!_config.AllowCancel)
        {
            return "⚠️ Remote agent interrupt is disabled in AgentsHub Settings.";
        }

        try
        {
            var running = _cliManager?.GetAllInfos().Where(c => c.State == CliState.Running).ToList() ?? [];
            if (running.Count == 0)
            {
                return "ℹ️ No agents are currently running to stop.";
            }

            foreach (var s in running)
            {
                _cliManager?.SendInput(s.Id, "\x03"); // SIGINT
            }

            return $"🛑 Sent interrupt signal (`Ctrl+C`) to: `{string.Join(", ", running.Select(r => r.DisplayName))}`";
        }
        catch (Exception ex)
        {
            return $"❌ Failed to send cancel signal: {ex.Message}";
        }
    }

    private static string BuildHelpMessage()
    {
        return "🤖 **AgentsHub Pocket Commander Commands:**\n\n" +
               "• `/status` (or `!status`) — View active agent, git branch, and system load\n" +
               "• `/prompt <text>` (or `!prompt <text>`) — Inject prompt into active agent terminal\n" +
               "• `/diff` (or `!diff`) — Inspect recent git changes and file diffs\n" +
               "• `/screen` (or `!screen`) — Capture desktop screenshot\n" +
               "• `/stop` (or `!stop`) — Send Ctrl+C interrupt to running agent\n" +
               "• `/pair <pin>` (or `!pair <pin>`) — Pair Discord user account";
    }

    private static byte[]? CaptureDesktopSnapshot()
    {
        try
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            using var bmp = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Screenshot capture error: {ex.Message}");
            return null;
        }
    }

    private async Task HandleScreenshotCommandAsync(string platform, string targetId, CancellationToken ct)
    {
        if (!_config.AllowScreenshots)
        {
            await SendReplyAsync(platform, targetId, "⚠️ Remote screenshots are disabled in AgentsHub Settings.", ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await SendReplyAsync(platform, targetId, "📸 Capturing desktop snapshot...", ct).ConfigureAwait(false);

            byte[]? imageBytes = CaptureDesktopSnapshot();
            if (imageBytes != null && imageBytes.Length > 0)
            {
                string caption = $"🖥️ AgentsHub Desktop Snapshot ({DateTime.UtcNow:HH:mm:ss} UTC)";
                if (platform == "telegram" && long.TryParse(targetId, out long tChatId))
                {
                    await SendTelegramPhotoAsync(tChatId, imageBytes, caption, ct).ConfigureAwait(false);
                }
                else if (platform == "discord")
                {
                    await SendDiscordPhotoAsync(targetId, imageBytes, caption, ct).ConfigureAwait(false);
                }
            }
            else
            {
                await SendReplyAsync(platform, targetId, "❌ Failed to capture desktop screenshot.", ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await SendReplyAsync(platform, targetId, $"❌ Failed to send screenshot: {ex.Message}", ct).ConfigureAwait(false);
        }
    }

    private async Task SendReplyAsync(string platform, string targetId, string text, CancellationToken ct)
    {
        if (platform == "telegram" && long.TryParse(targetId, out long tChatId))
        {
            await SendTelegramMessageAsync(tChatId, text, CreateMainInlineKeyboard(), ct).ConfigureAwait(false);
        }
        else if (platform == "discord")
        {
            await SendDiscordMessageAsync(targetId, text, ct: ct).ConfigureAwait(false);
        }
    }

    // =========================================================================
    // TASK COMPLETION PUSH NOTIFICATIONS
    // =========================================================================

    public async Task SendTaskCompletionAlertAsync(string displayName, string summary, bool isError, bool isApproval, TimeSpan? duration)
    {
        if (!_config.IsEnabled) return;

        bool shouldNotify = (isError && _config.NotifyOnError) ||
                           (isApproval && _config.NotifyOnApproval) ||
                           (!isError && !isApproval && _config.NotifyOnTaskDone);

        if (!shouldNotify) return;

        string icon = isError ? "❌" : (isApproval ? "⚠️" : "✅");
        string statusTitle = isError ? "Agent Error Encountered" : (isApproval ? "Human Approval Required" : "Agent Task Completed");

        string durStr = duration.HasValue
            ? (duration.Value.TotalMinutes >= 1
                ? $"{(int)duration.Value.TotalMinutes}m {duration.Value.Seconds}s"
                : $"{duration.Value.Seconds}.{duration.Value.Milliseconds / 100}s")
            : "Instant";

        string cleanSummary = string.IsNullOrWhiteSpace(summary) ? "Task finished successfully." : summary;

        // 1. Send Telegram Alert
        if ((_config.Platform == "telegram" || _config.Platform == "both") &&
            _config.AuthorizedChatId != 0 && !string.IsNullOrWhiteSpace(_config.TelegramBotToken))
        {
            try
            {
                string msg = $"{icon} *{statusTitle}*\n\n" +
                             $"*Agent Session:* `{displayName}`\n" +
                             $"*Duration:* `{durStr}`\n" +
                             $"*Time:* `{DateTime.UtcNow:HH:mm:ss} UTC`\n\n" +
                             $"*Summary:*\n>{cleanSummary}";

                await SendTelegramMessageAsync(_config.AuthorizedChatId, msg, CreateMainInlineKeyboard()).ConfigureAwait(false);
            }
            catch { }
        }

        // 2. Send Discord Bot Channel Alert
        if ((_config.Platform == "discord" || _config.Platform == "both") &&
            !string.IsNullOrWhiteSpace(_config.DiscordChannelId) && !string.IsNullOrWhiteSpace(_config.DiscordBotToken))
        {
            try
            {
                int color = isError ? 0xef4444 : (isApproval ? 0xf59e0b : 0x10b981);
                var embed = new Dictionary<string, object>
                {
                    ["title"] = $"{icon} {statusTitle}: {displayName}",
                    ["description"] = cleanSummary,
                    ["color"] = color,
                    ["fields"] = new[]
                    {
                        new { name = "Duration", value = durStr, inline = true },
                        new { name = "Status", value = isError ? "Error" : (isApproval ? "Approval Needed" : "Completed"), inline = true },
                        new { name = "Time", value = DateTime.UtcNow.ToString("HH:mm:ss UTC"), inline = true }
                    },
                    ["footer"] = new { text = "AgentsHub Pocket Commander" }
                };

                await SendDiscordMessageAsync(_config.DiscordChannelId, "", embed: embed).ConfigureAwait(false);
            }
            catch { }
        }
    }

    // =========================================================================
    // REST API CALL HELPERS
    // =========================================================================

    private async Task SendTelegramMessageAsync(long chatId, string text, object? replyMarkup = null, CancellationToken ct = default)
    {
        string token = _config.TelegramBotToken.Trim();
        if (string.IsNullOrWhiteSpace(token)) return;

        string url = $"https://api.telegram.org/bot{token}/sendMessage";
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "Markdown"
        };
        if (replyMarkup != null) payload["reply_markup"] = replyMarkup;

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        await _http.PostAsync(url, content, ct).ConfigureAwait(false);
    }

    private async Task SendTelegramPhotoAsync(long chatId, byte[] photoBytes, string caption, CancellationToken ct = default)
    {
        string token = _config.TelegramBotToken.Trim();
        if (string.IsNullOrWhiteSpace(token)) return;

        string url = $"https://api.telegram.org/bot{token}/sendPhoto";
        var byteContent = new ByteArrayContent(photoBytes);
        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId.ToString()), "chat_id" },
            { new StringContent(caption), "caption" },
            { byteContent, "photo", "screen.jpg" }
        };

        await _http.PostAsync(url, form, ct).ConfigureAwait(false);
    }

    private async Task AnswerTelegramCallbackAsync(string queryId, string text, CancellationToken ct = default)
    {
        string token = _config.TelegramBotToken.Trim();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(queryId)) return;

        string url = $"https://api.telegram.org/bot{token}/answerCallbackQuery";
        var payload = new Dictionary<string, object>
        {
            ["callback_query_id"] = queryId,
            ["text"] = text
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        await _http.PostAsync(url, content, ct).ConfigureAwait(false);
    }

    private async Task SendDiscordMessageAsync(string channelId, string text, object? embed = null, CancellationToken ct = default)
    {
        string token = _config.DiscordBotToken.Trim();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(channelId)) return;

        // Truncate to Discord 2000 character limit to prevent 400 Bad Request
        if (text.Length > 1980)
        {
            text = text[..1940] + "\n...[truncated]";
        }

        string url = $"https://discord.com/api/v10/channels/{channelId}/messages";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");

        var payload = new Dictionary<string, object?> { ["content"] = text };
        if (embed != null) payload["embeds"] = new[] { embed };

        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.WriteLine($"[Discord] SendMessage failed {(int)res.StatusCode}: {err}");
        }
    }

    private async Task SendDiscordPhotoAsync(string channelId, byte[] photoBytes, string caption, CancellationToken ct = default)
    {
        string token = _config.DiscordBotToken.Trim();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(channelId)) return;

        string url = $"https://discord.com/api/v10/channels/{channelId}/messages";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bot {token}");

        var byteContent = new ByteArrayContent(photoBytes);
        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        var payload = new { content = caption };

        using var form = new MultipartFormDataContent
        {
            { byteContent, "files[0]", "screen.jpg" },
            { new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), "payload_json" }
        };

        req.Content = form;
        await _http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static object CreateMainInlineKeyboard()
    {
        return new
        {
            inline_keyboard = new[]
            {
                new[]
                {
                    new { text = "📊 Status", callback_data = "action:status" },
                    new { text = "📄 Git Diff", callback_data = "action:diff" }
                },
                new[]
                {
                    new { text = "📸 Screenshot", callback_data = "action:screen" },
                    new { text = "🛑 Stop Agent", callback_data = "action:stop" }
                }
            }
        };
    }

    private static string GenerateNewPin()
    {
        var rnd = Random.Shared;
        return $"{rnd.Next(100, 999)}-{rnd.Next(100, 999)}";
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var loaded = JsonSerializer.Deserialize<PocketCommanderConfig>(json, JsonOpts);
                if (loaded != null) _config = loaded;
            }
        }
        catch { }
    }

    private void SaveConfigInternal()
    {
        try
        {
            File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, JsonOpts));
        }
        catch { }
    }

    public void Dispose()
    {
        StopServices();
        _http.Dispose();
    }
}
