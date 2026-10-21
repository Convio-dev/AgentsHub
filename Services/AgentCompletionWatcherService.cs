#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgentsHub.Helpers;

namespace AgentsHub.Services;

public sealed partial class AgentCompletionWatcherService : IDisposable
{
    public static AgentCompletionWatcherService Instance { get; } = new();

    private sealed class SessionTracker
    {
        public string Id { get; init; } = "";
        public bool IsExecuting { get; set; }
        public bool HasNotifiedCompletion { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public string LastPrompt { get; set; } = "";
        public StringBuilder TailBuffer { get; } = new();
        public System.Threading.Timer? QuietTimer { get; set; }
    }

    private CliManagerService? _cliManager;
    private Action<string, object>? _postEventToUi;
    private readonly ConcurrentDictionary<string, SessionTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastNotified = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastTranscriptLengths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private bool _isDisposed;

    // ANSI escape code stripper
    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiRegex();

    public void Initialize(CliManagerService cliManager, Action<string, object> postEventToUi)
    {
        _cliManager = cliManager;
        _postEventToUi = postEventToUi;

        _cliManager.InputSentId += OnInputSent;
        _cliManager.TerminalDataReceivedId += OnTerminalDataReceived;
        _cliManager.StatusChangedId += OnCliStatusChanged;

        InitializeTranscriptWatchers();
    }

    private void InitializeTranscriptWatchers()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var geminiBrainDir = Path.Combine(userProfile, ".gemini", "antigravity", "brain");
            if (Directory.Exists(geminiBrainDir))
            {
                WatchTranscriptDirectory(geminiBrainDir);
            }

            var profilesBase = AntigravityProfileService.ProfilesBaseDir;
            if (Directory.Exists(profilesBase))
            {
                WatchTranscriptDirectory(profilesBase);
            }

            var curWs = AiConfigService.Instance.GetActiveWorkspace();
            if (curWs != null && !string.IsNullOrWhiteSpace(curWs.WorkingDirectory) && Directory.Exists(curWs.WorkingDirectory))
            {
                var logsDir = Path.Combine(curWs.WorkingDirectory, ".system_generated", "logs");
                if (Directory.Exists(logsDir))
                {
                    WatchTranscriptDirectory(logsDir);
                }
            }
        }
        catch { }
    }

    private void WatchTranscriptDirectory(string dirPath)
    {
        try
        {
            var watcher = new FileSystemWatcher(dirPath, "transcript.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            watcher.Changed += OnTranscriptFileChanged;
            watcher.Created += OnTranscriptFileChanged;
            watcher.EnableRaisingEvents = true;
            _fileWatchers.Add(watcher);
        }
        catch { }
    }

    private void OnInputSent(string id, string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        var tracker = _trackers.GetOrAdd(id, sid => new SessionTracker { Id = sid });
        lock (tracker)
        {
            // Enter key sent -> indicates an actual user command
            if (data.Contains('\r') || data.Contains('\n') || data.Length > 2)
            {
                var clean = AnsiRegex().Replace(data, "").Trim();
                if (clean.Length > 1 && !clean.StartsWith("\x1B"))
                {
                    tracker.IsExecuting = true;
                    tracker.HasNotifiedCompletion = false; // Reset completion state for new prompt
                    tracker.StartedAt = DateTime.UtcNow;
                    tracker.LastPrompt = clean;
                    tracker.TailBuffer.Clear();

                    tracker.QuietTimer?.Dispose();
                    tracker.QuietTimer = null;
                }
            }
        }
    }

    private void OnTerminalDataReceived(string id, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var tracker = _trackers.GetOrAdd(id, sid => new SessionTracker { Id = sid });
        lock (tracker)
        {
            // Only transition to executing if an actual tool execution indicator is observed
            // and we have not already notified completion for this prompt
            if (!tracker.IsExecuting && !tracker.HasNotifiedCompletion)
            {
                if (!IsStartupOrGreetingNoise(text) &&
                    (text.Contains("Thinking", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("Explored ", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("Ran ", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("Tool ", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("Edited ", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("Generating...", StringComparison.OrdinalIgnoreCase)))
                {
                    tracker.IsExecuting = true;
                    tracker.StartedAt = DateTime.UtcNow;
                }
            }

            tracker.TailBuffer.Append(text);
            if (tracker.TailBuffer.Length > 30000)
            {
                tracker.TailBuffer.Remove(0, tracker.TailBuffer.Length - 20000);
            }

            // Keep quiet timer running with 3.5s delay to avoid firing during LLM reasoning pauses
            if (tracker.IsExecuting && !tracker.HasNotifiedCompletion)
            {
                tracker.QuietTimer?.Dispose();
                tracker.QuietTimer = new System.Threading.Timer(OnQuietTimerElapsed, tracker, 3500, Timeout.Infinite);
            }
        }
    }

    private void OnQuietTimerElapsed(object? state)
    {
        if (state is not SessionTracker tracker) return;

        string tailText;
        string prompt;
        DateTime startedAt;
        lock (tracker)
        {
            if (!tracker.IsExecuting || tracker.HasNotifiedCompletion) return;
            tailText = tracker.TailBuffer.ToString();
            prompt = tracker.LastPrompt;
            startedAt = tracker.StartedAt;
        }

        string cleanTail = AnsiRegex().Replace(tailText, "");
        var (isCompleted, isError, isApproval, rawSummary) = AnalyzeTerminalOutput(cleanTail, prompt);

        if (isCompleted || isError || isApproval)
        {
            lock (tracker)
            {
                tracker.IsExecuting = false;
                tracker.HasNotifiedCompletion = true; // Lock single completion for this prompt
                tracker.TailBuffer.Clear();
            }

            var duration = DateTime.UtcNow - startedAt;
            string formattedDesc = DiscordMarkdownFormatter.FormatTerminalOutputForDiscord(rawSummary, prompt);
            string brainId = ResolveBrainSessionId(tracker.Id);

            _ = TriggerCompletionAsync(tracker.Id, brainId, formattedDesc, isError, isApproval, duration);
        }
    }

    private static bool IsActivelyWorking(string recentTail)
    {
        if (string.IsNullOrWhiteSpace(recentTail)) return false;

        return recentTail.Contains("esc to cancel", StringComparison.OrdinalIgnoreCase) ||
               recentTail.Contains("Generating...", StringComparison.OrdinalIgnoreCase) ||
               recentTail.Contains("Running command...", StringComparison.OrdinalIgnoreCase) ||
               recentTail.Contains("Reading file...", StringComparison.OrdinalIgnoreCase) ||
               recentTail.Contains("Thinking...", StringComparison.OrdinalIgnoreCase) ||
               recentTail.Contains("Running tool", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool isCompleted, bool isError, bool isApproval, string summary) AnalyzeTerminalOutput(string text, string lastPrompt)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, false, false, "");

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return (false, false, false, "");

        string lastLine = lines[^1];
        string recentTail = string.Join("\n", lines.TakeLast(6));

        // Filter out startup noise and greeting banners
        if (IsStartupOrGreetingNoise(recentTail))
        {
            return (false, false, false, "");
        }

        // 1. Approval prompt checks
        if (lastLine.Contains("(y/n)", StringComparison.OrdinalIgnoreCase) ||
            lastLine.Contains("[y/N]", StringComparison.OrdinalIgnoreCase) ||
            lastLine.Contains("[Y/n]", StringComparison.OrdinalIgnoreCase) ||
            lastLine.Contains("Allow this command", StringComparison.OrdinalIgnoreCase) ||
            lastLine.Contains("Do you want to run", StringComparison.OrdinalIgnoreCase) ||
            lastLine.Contains("Allow / Deny", StringComparison.OrdinalIgnoreCase) ||
            recentTail.Contains("Action required", StringComparison.OrdinalIgnoreCase))
        {
            return (false, false, true, recentTail);
        }

        // 2. Active execution guard: If active execution markers are in recent output, the agent is still busy!
        if (IsActivelyWorking(recentTail))
        {
            return (false, false, false, "");
        }

        // 3. Check for errors
        bool isError = recentTail.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                       recentTail.Contains("Fatal error", StringComparison.OrdinalIgnoreCase) ||
                       recentTail.Contains("Traceback (most recent call last)", StringComparison.OrdinalIgnoreCase);

        // 4. Prompt ready check (Antigravity & Claude Code)
        string lineBeforeLast = lines.Length > 1 ? lines[^2] : "";

        bool isPromptLine = lastLine == ">" ||
                            lastLine.StartsWith('>') ||
                            lastLine.EndsWith('>') ||
                            lastLine == "❯" ||
                            lastLine.StartsWith('❯') ||
                            lastLine.Contains("? for shortcuts", StringComparison.OrdinalIgnoreCase) ||
                            lineBeforeLast.StartsWith('>') ||
                            lineBeforeLast.EndsWith('>');

        bool hasCostOrContext = recentTail.Contains("cost:", StringComparison.OrdinalIgnoreCase) ||
                                recentTail.Contains("Context left:", StringComparison.OrdinalIgnoreCase);

        if (isPromptLine || hasCostOrContext || isError)
        {
            string summary = ExtractCompletionSummary(lines, lastPrompt);
            return (true, isError, false, summary);
        }

        return (false, false, false, "");
    }

    private static bool IsStartupOrGreetingNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        return text.Contains("Hello! What would you like to work on next?", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Verbosity now defaults to medium", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Verbosity is now medium by default", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Tip: Press shift+tab to cycle modes", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Hello! Let me know what you'd like to build", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<SYSTEM_MESSAGE>", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("All your subagents and background tasks have been stopped", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("esc to dismiss", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCompletionSummary(string[] lines, string lastPrompt)
    {
        var relevant = new List<string>();
        foreach (var l in lines.TakeLast(14))
        {
            var clean = l.Trim();
            if (string.IsNullOrWhiteSpace(clean)) continue;
            if (clean.Contains("? for shortcuts", StringComparison.OrdinalIgnoreCase)) continue;
            if (clean.Contains("esc to cancel", StringComparison.OrdinalIgnoreCase)) continue;
            if (clean.Contains("esc to dismiss", StringComparison.OrdinalIgnoreCase)) continue;
            if (clean.StartsWith('>') || clean.StartsWith('❯') || clean == "█") continue;
            relevant.Add(clean);
        }

        string result = string.Join("\n", relevant);
        if (string.IsNullOrWhiteSpace(result))
        {
            result = !string.IsNullOrWhiteSpace(lastPrompt) ? $"Completed command: {lastPrompt}" : "Agent task completed.";
        }
        return result;
    }

    private void OnCliStatusChanged(string id, CliState state, string msg)
    {
        if (state == CliState.Offline)
        {
            if (_trackers.TryGetValue(id, out var tracker))
            {
                lock (tracker)
                {
                    tracker.QuietTimer?.Dispose();
                    tracker.QuietTimer = null;
                    tracker.IsExecuting = false;
                    tracker.TailBuffer.Clear();

                    // Do NOT notify if user intentionally closed or stopped the CLI ("Stopping agy...", "Session closed")
                    bool isUserStop = string.IsNullOrWhiteSpace(msg) ||
                                      msg.StartsWith("Stopping", StringComparison.OrdinalIgnoreCase) ||
                                      msg.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                                      msg.Contains("stopped", StringComparison.OrdinalIgnoreCase);

                    bool isCrash = msg.Contains("code") && !msg.Contains("code 0");

                    if (!isUserStop && isCrash && !tracker.HasNotifiedCompletion)
                    {
                        tracker.HasNotifiedCompletion = true;
                        var duration = DateTime.UtcNow - tracker.StartedAt;
                        string brainId = ResolveBrainSessionId(id);
                        _ = TriggerCompletionAsync(id, brainId, msg, true, false, duration);
                    }
                }
            }
        }
    }

    private void OnTranscriptFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith("transcript.jsonl", StringComparison.OrdinalIgnoreCase)) return;

        Task.Run(async () =>
        {
            await Task.Delay(800).ConfigureAwait(false);
            try
            {
                var fi = new FileInfo(e.FullPath);
                if (!fi.Exists || fi.Length == 0) return;

                // Check if file grew since last inspection to avoid re-triggering on old content
                if (_lastTranscriptLengths.TryGetValue(e.FullPath, out long lastLen) && fi.Length <= lastLen)
                {
                    return;
                }
                _lastTranscriptLengths[e.FullPath] = fi.Length;

                using var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                string? line;
                string? lastLine = null;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line)) lastLine = line;
                }

                if (string.IsNullOrWhiteSpace(lastLine)) return;

                using var doc = JsonDocument.Parse(lastLine);
                var root = doc.RootElement;

                var type = root.TryGetProperty("type", out var tEl) ? tEl.GetString() : null;
                var status = root.TryGetProperty("status", out var sEl) ? sEl.GetString() : null;
                var content = root.TryGetProperty("content", out var cEl) ? cEl.GetString() : "";

                // Ignore startup greetings and system notifications
                if (string.IsNullOrWhiteSpace(content) || IsStartupOrGreetingNoise(content))
                {
                    return;
                }

                // If step contains tool_calls, model is invoking tools (intermediate step) -> DO NOT NOTIFY
                if (root.TryGetProperty("tool_calls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array && tcEl.GetArrayLength() > 0)
                {
                    return;
                }

                // Extract raw brain folder GUID
                string brainId = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(e.FullPath)))) ?? "session";
                if (brainId.StartsWith('.'))
                {
                    brainId = Path.GetFileName(Path.GetDirectoryName(e.FullPath)) ?? "session";
                }

                // Sync live trackers so terminal watcher won't double-fire
                foreach (var tr in _trackers.Values)
                {
                    lock (tr)
                    {
                        tr.IsExecuting = false;
                        tr.HasNotifiedCompletion = true;
                    }
                }

                // Resolve friendly profile/workspace name
                string profileName = ResolveProfileNameFromPath(e.FullPath);

                // Only trigger if PLANNER_RESPONSE with final text content, or terminal ERROR
                if (string.Equals(type, "PLANNER_RESPONSE", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(status, "DONE", StringComparison.OrdinalIgnoreCase))
                {
                    string formatted = DiscordMarkdownFormatter.FormatTerminalOutputForDiscord(content);
                    _ = TriggerCompletionAsync(profileName, brainId, formatted, false, false, TimeSpan.FromSeconds(5));
                }
                else if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    string formatted = DiscordMarkdownFormatter.FormatTerminalOutputForDiscord(content);
                    _ = TriggerCompletionAsync(profileName, brainId, formatted, true, false, TimeSpan.FromSeconds(5));
                }
            }
            catch { }
        });
    }

    public async Task TriggerCompletionAsync(string sessionId, string? brainSessionId, string summary, bool isError, bool isApproval, TimeSpan? duration = null)
    {
        // 12-second deduplication per session & brain ID to prevent double-firing
        var now = DateTime.UtcNow;
        if (_lastNotified.TryGetValue(sessionId, out var last) && (now - last).TotalSeconds < 12)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(brainSessionId))
        {
            if (_lastNotified.TryGetValue(brainSessionId, out var bLast) && (now - bLast).TotalSeconds < 12)
            {
                return;
            }
            _lastNotified[brainSessionId] = now;
        }
        _lastNotified[sessionId] = now;

        string displayName = sessionId;
        if (_cliManager != null)
        {
            var info = _cliManager.GetInfo(sessionId);
            if (!string.IsNullOrWhiteSpace(info.DisplayName))
            {
                displayName = info.DisplayName;
            }
        }

        string rawBrainId = !string.IsNullOrWhiteSpace(brainSessionId) ? brainSessionId : ResolveBrainSessionId(sessionId);

        // 1. Dispatch Audio Cue
        string soundEvent = isError ? "task_failed" : (isApproval ? "approval_needed" : "task_done");
        SoundpackAudioService.Instance.PlaySoundEvent(soundEvent);

        // 2. Dispatch Remote Discord Webhook
        var webhookCfg = RemoteWebhookService.Instance.GetConfig();
        if (webhookCfg.IsEnabled && !string.IsNullOrWhiteSpace(webhookCfg.DiscordWebhookUrl))
        {
            bool shouldSend = (isError && webhookCfg.NotifyOnError) ||
                              (isApproval && webhookCfg.NotifyOnApproval) ||
                              (!isError && !isApproval && webhookCfg.NotifyOnTaskDone);

            if (shouldSend)
            {
                string title = isError
                    ? $"❌ Agent Error: {displayName}"
                    : (isApproval ? $"⚠️ Action Approval Required: {displayName}" : $"✅ Agent Completed: {displayName}");

                int color = isError ? 0xef4444 : (isApproval ? 0xf59e0b : 0x10b981);

                string durationStr = duration.HasValue
                    ? (duration.Value.TotalMinutes >= 1
                        ? $"{(int)duration.Value.TotalMinutes}m {duration.Value.Seconds}s"
                        : $"{duration.Value.Seconds}.{duration.Value.Milliseconds / 100}s")
                    : "Instant";

                var fields = new List<(string Name, string Value, bool Inline)>
                {
                    ("Session", displayName, true),
                    ("Brain ID", $"`{rawBrainId}`", true),
                    ("Status", isError ? "Error" : (isApproval ? "Approval Needed" : "Completed"), true),
                    ("Duration", durationStr, true),
                    ("Time", DateTime.UtcNow.ToString("HH:mm:ss UTC"), true)
                };

                await RemoteWebhookService.Instance.SendDiscordNotificationAsync(
                    title: title,
                    description: string.IsNullOrWhiteSpace(summary) ? "Task finished successfully." : summary,
                    color: color,
                    fields: fields
                ).ConfigureAwait(false);
            }
        }

        // 3. Post Event to UI Dashboard
        _postEventToUi?.Invoke("agent_task_completed", new
        {
            sessionId,
            displayName,
            brainId = rawBrainId,
            summary,
            isError,
            isApproval,
            timestamp = DateTime.UtcNow
        });
    }

    private string ResolveBrainSessionId(string sessionId)
    {
        try
        {
            // If already looks like a GUID, return it
            if (Guid.TryParse(sessionId, out _)) return sessionId;

            // Otherwise, find the newest session directory under UserProfile/.gemini/antigravity/brain
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var brainDir = Path.Combine(userProfile, ".gemini", "antigravity", "brain");
            if (Directory.Exists(brainDir))
            {
                var newest = Directory.GetDirectories(brainDir)
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (newest != null)
                {
                    return newest.Name;
                }
            }
        }
        catch { }

        return sessionId;
    }

    private string ResolveProfileNameFromPath(string transcriptPath)
    {
        try
        {
            // Check if inside a virtual profile folder
            var profilesDir = AntigravityProfileService.ProfilesBaseDir;
            if (transcriptPath.StartsWith(profilesDir, StringComparison.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(profilesDir, transcriptPath);
                var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    return $"Antigravity ({parts[0]})";
                }
            }

            // Fallback to active profile or active workspace
            if (_cliManager != null)
            {
                var activeProf = _cliManager.ProfileService.ActiveProfile;
                if (!string.IsNullOrWhiteSpace(activeProf))
                {
                    return $"Antigravity ({activeProf})";
                }
            }

            var ws = AiConfigService.Instance.GetActiveWorkspace();
            if (ws != null && !string.IsNullOrWhiteSpace(ws.Name))
            {
                return ws.Name;
            }
        }
        catch { }

        return "Antigravity";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var w in _fileWatchers)
        {
            try { w.Dispose(); } catch { }
        }
        _fileWatchers.Clear();

        foreach (var t in _trackers.Values)
        {
            t.QuietTimer?.Dispose();
        }
        _trackers.Clear();
    }
}
