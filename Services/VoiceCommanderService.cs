#nullable enable
using System;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class VoiceCommanderService
{
    public static VoiceCommanderService Instance { get; } = new();

    public Task<bool> InjectCommandToActiveCliAsync(CliManagerService cliManager, string transcribedText, string? sessionName = null)
    {
        if (string.IsNullOrWhiteSpace(transcribedText)) return Task.FromResult(false);

        var targetSession = !string.IsNullOrEmpty(sessionName)
            ? sessionName
            : "claude_code";

        // Send text cleanly with enter key
        var cleanCommand = transcribedText.Trim();
        cliManager.SendInput(targetSession, cleanCommand + "\r\n");

        SoundpackAudioService.Instance.PlaySoundEvent("checkpoint_created");
        return Task.FromResult(true);
    }
}
