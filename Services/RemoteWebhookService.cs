#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HermesHub.Services;

public sealed class RemoteWebhookConfig
{
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = false;

    [JsonPropertyName("discordWebhookUrl")]
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    [JsonPropertyName("mentionRoleId")]
    public string MentionRoleId { get; set; } = string.Empty;

    [JsonPropertyName("notifyOnTaskDone")]
    public bool NotifyOnTaskDone { get; set; } = true;

    [JsonPropertyName("notifyOnError")]
    public bool NotifyOnError { get; set; } = true;

    [JsonPropertyName("notifyOnApproval")]
    public bool NotifyOnApproval { get; set; } = true;

    [JsonPropertyName("webhookAuthToken")]
    public string WebhookAuthToken { get; set; } = Guid.NewGuid().ToString("N")[..16];
}

public sealed class RemoteWebhookService
{
    public static RemoteWebhookService Instance { get; } = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _configPath;
    private RemoteWebhookConfig _config = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public RemoteWebhookService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentsHub");
        Directory.CreateDirectory(appData);
        _configPath = Path.Combine(appData, "remote_webhook.json");

        LoadConfig();
    }

    public RemoteWebhookConfig GetConfig() => _config;

    public void SaveConfig(RemoteWebhookConfig cfg)
    {
        _config = cfg;
        try
        {
            File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, JsonOpts));
        }
        catch { }
    }

    public async Task<bool> SendDiscordNotificationAsync(
        string title, 
        string description, 
        int color = 0x38bdf8,
        System.Collections.Generic.List<(string Name, string Value, bool Inline)>? fields = null)
    {
        if (!_config.IsEnabled || string.IsNullOrWhiteSpace(_config.DiscordWebhookUrl))
            return false;

        try
        {
            var chunks = SplitWithCodeFenceRepair(description, 1800);
            int totalParts = chunks.Count;
            bool allSuccess = true;

            string? mention = null;
            if (!string.IsNullOrWhiteSpace(_config.MentionRoleId))
            {
                var m = _config.MentionRoleId.Trim();
                mention = m.All(char.IsDigit) ? $"<@&{m}>" : m;
            }

            for (int i = 0; i < totalParts; i++)
            {
                var chunkText = chunks[i];
                string partTitle = totalParts > 1 ? $"{title} (Part {i + 1}/{totalParts})" : title;

                var embedObj = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["title"] = partTitle,
                    ["description"] = chunkText,
                    ["color"] = color,
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["footer"] = new { text = totalParts > 1 ? $"Agents Hub v1.0.4 • Part {i + 1} of {totalParts}" : "Agents Hub v1.0.4 Remote Gateway" }
                };

                // Attach fields only on the first part
                if (i == 0 && fields != null && fields.Count > 0)
                {
                    var embedFields = new System.Collections.Generic.List<object>();
                    foreach (var f in fields)
                    {
                        embedFields.Add(new { name = f.Name, value = f.Value, inline = f.Inline });
                    }
                    embedObj["fields"] = embedFields;
                }

                var payload = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["username"] = "Agents Hub Bot",
                    ["avatar_url"] = "https://raw.githubusercontent.com/ZerroDevs/AgentsHub/main/assets/icon.png",
                    ["embeds"] = new[] { embedObj }
                };

                // Mention ping only on the first part
                if (i == 0 && !string.IsNullOrEmpty(mention))
                {
                    payload["content"] = mention;
                }

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await _http.PostAsync(_config.DiscordWebhookUrl, content).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    allSuccess = false;
                }

                // Respect Discord rate limits when multi-chunk messages are sent
                if (totalParts > 1 && i < totalParts - 1)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }

            return allSuccess;
        }
        catch
        {
            return false;
        }
    }

    public static List<string> SplitWithCodeFenceRepair(string text, int maxChunkSize = 1800)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Add("");
            return result;
        }

        string remaining = text.Trim();
        string activeLanguage = "";
        bool insideCodeFence = false;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= maxChunkSize)
            {
                result.Add(remaining);
                break;
            }

            int splitIndex = FindBestSplitPoint(remaining, maxChunkSize);
            string chunk = remaining[..splitIndex];
            remaining = remaining[splitIndex..].TrimStart('\r', '\n');

            // Count code fences inside this chunk
            var matches = System.Text.RegularExpressions.Regex.Matches(chunk, @"```(\w*)");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (!insideCodeFence)
                {
                    insideCodeFence = true;
                    activeLanguage = match.Groups[1].Value;
                }
                else
                {
                    insideCodeFence = false;
                    activeLanguage = "";
                }
            }

            if (insideCodeFence)
            {
                // Unclosed code block: close it for this chunk, and reopen in the next
                chunk += "\n```";
                remaining = $"```{activeLanguage}\n" + remaining;
                insideCodeFence = false; // Next chunk starts with open fence, will be counted
            }

            result.Add(chunk);
        }

        return result;
    }

    private static int FindBestSplitPoint(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text.Length;

        // Try double newline (paragraph boundary)
        int idx = text.LastIndexOf("\n\n", maxLength, StringComparison.Ordinal);
        if (idx > maxLength / 2) return idx + 2;

        // Try single newline
        idx = text.LastIndexOf('\n', maxLength);
        if (idx > maxLength / 2) return idx + 1;

        // Try sentence end
        idx = text.LastIndexOf(". ", maxLength, StringComparison.Ordinal);
        if (idx > maxLength / 2) return idx + 2;

        // Try space
        idx = text.LastIndexOf(' ', maxLength);
        if (idx > maxLength / 2) return idx + 1;

        // Fallback to hard split
        return maxLength;
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var loaded = JsonSerializer.Deserialize<RemoteWebhookConfig>(json, JsonOpts);
                if (loaded != null) _config = loaded;
            }
        }
        catch { }
    }
}
