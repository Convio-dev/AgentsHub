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

    private Task HandleGetSoundpackConfigAsync(string? requestId)
    {
        {
            var cfg = SoundpackAudioService.Instance.GetConfig();
            SendResponse(requestId, true, cfg);
        }

        return Task.CompletedTask;
    }

    private Task HandleSaveSoundpackConfigAsync(JsonElement data, string? requestId)
    {
        {
            if (data.TryGetProperty("config", out var spCfgProp))
            {
                var cfg = JsonSerializer.Deserialize<SoundpackAudioConfig>(spCfgProp.GetRawText(), CaseInsensitiveJsonOptions);
                if (cfg != null)
                {
                    SoundpackAudioService.Instance.SaveConfig(cfg);
                    SendResponse(requestId, true, new { success = true, config = cfg });
                    return Task.CompletedTask;
                }
            }
            SendResponse(requestId, false, "Invalid soundpack config payload");
        }

        return Task.CompletedTask;
    }

    private Task HandlePlaySoundEventAsync(JsonElement data, string? requestId)
    {
        {
            string? eventName = data.TryGetProperty("eventName", out var evProp) ? evProp.GetString() : "task_done";
            string? theme = data.TryGetProperty("theme", out var thProp) ? thProp.GetString() : null;
            int? volume = data.TryGetProperty("volume", out var volProp) ? volProp.GetInt32() : null;
            bool force = data.TryGetProperty("force", out var fProp) && fProp.GetBoolean();

            SoundpackAudioService.Instance.PlaySoundEvent(eventName ?? "task_done", theme, volume, force);
            SendResponse(requestId, true, new { success = true });
        }

        return Task.CompletedTask;
    }

    private async Task HandleInjectVoiceCommandAsync(JsonElement data, string? requestId)
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

    private Task HandleGetScheduledAutomationsAsync(JsonElement data, string? requestId)
    {
        {
            string? wsId = data.TryGetProperty("workspaceId", out var sAutoWsProp) ? sAutoWsProp.GetString() : null;
            var tasks = ScheduledAutomationService.Instance.GetTasks(wsId);
            SendResponse(requestId, true, new { tasks });
        }

        return Task.CompletedTask;
    }

    private Task HandleSaveScheduledAutomationAsync(JsonElement data, string? requestId)
    {
        {
            if (data.TryGetProperty("task", out var taskProp))
            {
                var task = JsonSerializer.Deserialize<ScheduledAutomationTask>(taskProp.GetRawText(), CaseInsensitiveJsonOptions);
                if (task != null)
                {
                    ScheduledAutomationService.Instance.SaveTask(task);
                    SendResponse(requestId, true, new { success = true, task });
                    return Task.CompletedTask;
                }
            }
            SendResponse(requestId, false, "Invalid automation task payload");
        }

        return Task.CompletedTask;
    }

    private Task HandleDeleteScheduledAutomationAsync(JsonElement data, string? requestId)
    {
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

        return Task.CompletedTask;
    }

    private async Task HandleTriggerScheduledAutomationAsync(JsonElement data, string? requestId)
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

    private Task HandleGetRemoteWebhookConfigAsync(string? requestId)
    {
        {
            var cfg = RemoteWebhookService.Instance.GetConfig();
            SendResponse(requestId, true, cfg);
        }

        return Task.CompletedTask;
    }

    private Task HandleSaveRemoteWebhookConfigAsync(JsonElement data, string? requestId)
    {
        {
            if (data.TryGetProperty("config", out var rCfgProp))
            {
                var cfg = JsonSerializer.Deserialize<RemoteWebhookConfig>(rCfgProp.GetRawText(), CaseInsensitiveJsonOptions);
                if (cfg != null)
                {
                    RemoteWebhookService.Instance.SaveConfig(cfg);
                    SendResponse(requestId, true, new { success = true, config = cfg });
                    return Task.CompletedTask;
                }
            }
            SendResponse(requestId, false, "Invalid webhook config payload");
        }

        return Task.CompletedTask;
    }

    private async Task HandleTestRemoteWebhookAsync(JsonElement data, string? requestId)
    {
        string? title = data.TryGetProperty("title", out var titProp) ? titProp.GetString() : "Test Notification";
        string? desc = data.TryGetProperty("description", out var descProp) ? descProp.GetString() : "Agents Hub webhook test.";

        bool sent = await RemoteWebhookService.Instance.SendDiscordNotificationAsync(title ?? "Test", desc ?? "Test").ConfigureAwait(false);
        SendResponse(requestId, sent, new { success = sent });
    }

    private Task HandleGetPocketCommanderConfigAsync(string? requestId)
    {
        {
            var cfg = PocketCommanderService.Instance.GetConfig();
            SendResponse(requestId, true, cfg);
        }

        return Task.CompletedTask;
    }

    private Task HandleSavePocketCommanderConfigAsync(JsonElement data, string? requestId)
    {
        {
            if (data.TryGetProperty("config", out var pCfgProp))
            {
                var cfg = JsonSerializer.Deserialize<PocketCommanderConfig>(pCfgProp.GetRawText(), CaseInsensitiveJsonOptions);
                if (cfg != null)
                {
                    PocketCommanderService.Instance.SaveConfig(cfg);
                    SendResponse(requestId, true, new { success = true, config = cfg });
                    return Task.CompletedTask;
                }
            }
            SendResponse(requestId, false, "Invalid pocket commander config payload");
        }

        return Task.CompletedTask;
    }

    private async Task HandleTestPocketCommanderTokenAsync(JsonElement data, string? requestId)
    {
        string token = data.TryGetProperty("token", out var tProp) ? tProp.GetString() ?? "" : "";
        string platform = data.TryGetProperty("platform", out var pProp) ? pProp.GetString() ?? "telegram" : "telegram";

        var (success, username, error) = platform.Equals("discord", StringComparison.OrdinalIgnoreCase)
            ? await PocketCommanderService.Instance.TestDiscordBotConnectionAsync(token).ConfigureAwait(false)
            : await PocketCommanderService.Instance.TestTelegramBotConnectionAsync(token).ConfigureAwait(false);

        SendResponse(requestId, success, new { success, username, error, platform });
    }

    private Task HandleGeneratePocketPairingPinAsync(JsonElement data, string? requestId)
    {
        {
            string platform = data.TryGetProperty("platform", out var pProp) ? pProp.GetString() ?? "telegram" : "telegram";
            string newPin = PocketCommanderService.Instance.GenerateNewPairingPin(platform);
            SendResponse(requestId, true, new { pin = newPin, platform });
        }

        return Task.CompletedTask;
    }

    private Task HandleUnpairPocketCommanderAsync(JsonElement data, string? requestId)
    {
        {
            string platform = data.TryGetProperty("platform", out var pProp) ? pProp.GetString() ?? "all" : "all";
            PocketCommanderService.Instance.UnpairDevice(platform);
            var cfg = PocketCommanderService.Instance.GetConfig();
            SendResponse(requestId, true, new { success = true, config = cfg });
        }

        return Task.CompletedTask;
    }

    private Task HandleGetDiscordRpcStatusAsync(string? requestId)
    {
        {
            var status = DiscordRpcService.Instance.GetStatus();
            SendResponse(requestId, true, status);
        }

        return Task.CompletedTask;
    }

    private async Task HandleSaveDiscordRpcSettingsAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("settings", out var sProp))
        {
            var s = JsonSerializer.Deserialize<DiscordRpcSettings>(sProp.GetRawText(), CaseInsensitiveJsonOptions);
            if (s != null)
            {
                await DiscordRpcService.Instance.SaveSettingsAsync(s).ConfigureAwait(false);
                SendResponse(requestId, true, new { success = true, status = DiscordRpcService.Instance.GetStatus() });
                return;
            }
        }
        SendResponse(requestId, false, "Invalid Discord RPC settings payload");
    }

    private async Task HandleToggleDiscordRpcAsync(JsonElement data, string? requestId)
    {
        var status = DiscordRpcService.Instance.GetStatus();
        var current = status.Settings;
        bool newEnabled = data.TryGetProperty("enabled", out var enProp) ? enProp.GetBoolean() : !current.Enabled;
        current.Enabled = newEnabled;
        await DiscordRpcService.Instance.SaveSettingsAsync(current).ConfigureAwait(false);
        SendResponse(requestId, true, new { success = true, enabled = newEnabled, status = DiscordRpcService.Instance.GetStatus() });
    }

    private async Task HandleTestDiscordRpcAsync(string? requestId)
    {
        await DiscordRpcService.Instance.UpdateActivityAsync().ConfigureAwait(false);
        SendResponse(requestId, true, DiscordRpcService.Instance.GetStatus());
    }

    private async Task HandleSaveTimelapseClipAsync(JsonElement data, string? requestId)
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

}