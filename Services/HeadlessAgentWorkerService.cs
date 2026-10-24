#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class HeadlessAgentWorkerService : IDisposable
{
    private sealed class ActiveChatSession
    {
        public string WorkerId { get; init; } = "";
        public string SessionId { get; init; } = "";
        public string MessageId { get; set; } = "";
        public string OriginalPrompt { get; set; } = "";
        public bool IsGenerating { get; set; }
        public StringBuilder TailBuffer { get; } = new();
        public System.Threading.Timer? QuietTimer { get; set; }
        public CliActionPrompt? PendingAction { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }

    private readonly CliManagerService _cliManager;
    private readonly Action<string, object> _postEventToUi;
    private readonly ConcurrentDictionary<string, ActiveChatSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDisposed;

    public HeadlessAgentWorkerService(CliManagerService cliManager, Action<string, object> postEventToUi)
    {
        _cliManager = cliManager ?? throw new ArgumentNullException(nameof(cliManager));
        _postEventToUi = postEventToUi ?? throw new ArgumentNullException(nameof(postEventToUi));

        _cliManager.TerminalDataReceivedId += OnTerminalDataReceived;
        _cliManager.StatusChangedId += OnCliStatusChanged;
    }

    public string ResolveSessionId(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId)) return "ClaudeCode";

        workerId = workerId.Trim();

        if (workerId.StartsWith("antigravity_", StringComparison.OrdinalIgnoreCase))
        {
            var profName = workerId["antigravity_".Length..].Trim();
            if (profName.Equals("default", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(profName))
            {
                var activeProf = _cliManager.ProfileService.ActiveProfile;
                if (string.IsNullOrWhiteSpace(activeProf))
                {
                    var profs = _cliManager.ProfileService.GetProfiles();
                    activeProf = profs.Count > 0 ? profs[0] : "Default";
                }
                return CliManagerService.FormatAntigravityProfileId(activeProf);
            }
            return CliManagerService.FormatAntigravityProfileId(profName);
        }

        if (workerId.Equals("claude_code", StringComparison.OrdinalIgnoreCase) ||
            workerId.Equals("claude", StringComparison.OrdinalIgnoreCase) ||
            workerId.Equals("ClaudeCode", StringComparison.OrdinalIgnoreCase))
        {
            return "ClaudeCode";
        }

        if (workerId.Equals("hermes", StringComparison.OrdinalIgnoreCase))
        {
            return "Hermes";
        }

        if (workerId.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
        {
            return workerId["custom_".Length..].Trim();
        }

        return workerId;
    }

    public async Task<object> SendMessageAsync(string workerId, string prompt, string messageId)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new { success = false, error = "Prompt cannot be empty" };
        }

        var sessionId = ResolveSessionId(workerId);
        var info = _cliManager.GetInfo(sessionId);

        // Ensure background CLI process is active
        if (info.State != CliState.Running)
        {
            _cliManager.Start(sessionId);
            // Allow ConPTY initialization and startup banner to settle
            await Task.Delay(1000);
        }

        var session = _sessions.GetOrAdd(sessionId, sid => new ActiveChatSession
        {
            WorkerId = workerId,
            SessionId = sid
        });

        lock (session)
        {
            session.MessageId = messageId;
            session.OriginalPrompt = prompt;
            session.IsGenerating = true;
            session.TailBuffer.Clear();
            session.PendingAction = null;
            session.StartedAt = DateTime.UtcNow;
            session.QuietTimer?.Dispose();
        }

        // Notify UI that generation has commenced
        _postEventToUi("chat_status_changed", new
        {
            workerId,
            sessionId,
            status = "busy",
            text = "Thinking & Generating..."
        });

        // Write prompt to the headless CLI's stdin stream (terminal carriage return)
        _cliManager.SendInput(sessionId, prompt + "\r");

        return new
        {
            success = true,
            workerId,
            sessionId,
            messageId
        };
    }

    public Task<object> RespondActionAsync(string actionId, bool approved, string response)
    {
        // Find session with this action or any waiting session
        ActiveChatSession? targetSession = null;
        foreach (var sess in _sessions.Values)
        {
            lock (sess)
            {
                if (sess.PendingAction != null && (sess.PendingAction.Id == actionId || string.IsNullOrEmpty(actionId)))
                {
                    targetSession = sess;
                    break;
                }
            }
        }

        if (targetSession == null)
        {
            return Task.FromResult<object>(new { success = false, error = "No pending action found matching ID" });
        }

        lock (targetSession)
        {
            targetSession.PendingAction = null;
        }

        var input = string.IsNullOrWhiteSpace(response) ? (approved ? "y\r\n" : "n\r\n") : (response + "\r\n");
        _cliManager.SendInput(targetSession.SessionId, input);

        _postEventToUi("chat_status_changed", new
        {
            workerId = targetSession.WorkerId,
            sessionId = targetSession.SessionId,
            status = "busy",
            text = approved ? "Approved: Executing action..." : "Denied: Resuming..."
        });

        return Task.FromResult<object>(new { success = true });
    }

    public Task<object> StopGenerationAsync(string workerId)
    {
        var sessionId = ResolveSessionId(workerId);
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            lock (session)
            {
                session.IsGenerating = false;
                session.QuietTimer?.Dispose();
                session.PendingAction = null;
            }

            // Send Ctrl+C to halt execution in terminal
            _cliManager.SendInput(sessionId, "\x03");

            _postEventToUi("chat_message_completed", new
            {
                workerId = session.WorkerId,
                messageId = session.MessageId
            });

            _postEventToUi("chat_status_changed", new
            {
                workerId = session.WorkerId,
                sessionId = session.SessionId,
                status = "idle",
                text = "Stopped"
            });
        }

        return Task.FromResult<object>(new { success = true });
    }

    private void OnTerminalDataReceived(string sessionId, string rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return;

        if (!_sessions.TryGetValue(sessionId, out var session)) return;

        lock (session)
        {
            if (!session.IsGenerating) return;

            session.TailBuffer.Append(rawText);
            if (session.TailBuffer.Length > 40_000)
            {
                session.TailBuffer.Remove(0, session.TailBuffer.Length - 25_000);
            }

            // Parse display chunk for activities, thoughts, and clean markdown content
            var parsed = CliStreamParser.ParseDisplayChunk(rawText, session.OriginalPrompt);

            if (parsed.Activities.Count > 0)
            {
                foreach (var act in parsed.Activities)
                {
                    _postEventToUi("chat_activity_stream", new
                    {
                        workerId = session.WorkerId,
                        messageId = session.MessageId,
                        activity = act
                    });
                }
            }

            if (!string.IsNullOrEmpty(parsed.Thought))
            {
                _postEventToUi("chat_thought_stream", new
                {
                    workerId = session.WorkerId,
                    messageId = session.MessageId,
                    thought = parsed.Thought
                });
            }

            if (!string.IsNullOrEmpty(parsed.Content))
            {
                _postEventToUi("chat_token_stream", new
                {
                    workerId = session.WorkerId,
                    messageId = session.MessageId,
                    token = parsed.Content.EndsWith("\n") ? parsed.Content : (parsed.Content + "\n")
                });
            }

            // Detect action confirmations
            string cleanTail = CliStreamParser.StripAnsi(session.TailBuffer.ToString());
            var actionPrompt = CliStreamParser.DetectActionPrompt(cleanTail);
            if (actionPrompt != null && session.PendingAction == null)
            {
                session.PendingAction = actionPrompt;
                _postEventToUi("chat_action_requested", new
                {
                    workerId = session.WorkerId,
                    messageId = session.MessageId,
                    action = new
                    {
                        id = actionPrompt.Id,
                        title = actionPrompt.Title,
                        description = actionPrompt.Description,
                        command = actionPrompt.Command
                    }
                });

                _postEventToUi("chat_status_changed", new
                {
                    workerId = session.WorkerId,
                    sessionId,
                    status = "waiting",
                    text = "Action Confirmation Required"
                });
                return;
            }

            // Reset Quiet Timer for completion detection (3500ms quiet threshold)
            session.QuietTimer?.Dispose();
            session.QuietTimer = new System.Threading.Timer(OnQuietTimerElapsed, session, 3500, Timeout.Infinite);
        }
    }

    private void OnQuietTimerElapsed(object? state)
    {
        if (state is not ActiveChatSession session) return;

        string cleanTail;
        string messageId;
        lock (session)
        {
            if (!session.IsGenerating) return;
            cleanTail = CliStreamParser.StripAnsi(session.TailBuffer.ToString());
            messageId = session.MessageId;
        }

        // Check if output indicates completion or prompt ready
        if (CliStreamParser.IsGenerationCompleted(cleanTail))
        {
            lock (session)
            {
                session.IsGenerating = false;
                session.QuietTimer?.Dispose();
                session.QuietTimer = null;
                session.TailBuffer.Clear();
            }

            _postEventToUi("chat_message_completed", new
            {
                workerId = session.WorkerId,
                messageId
            });

            _postEventToUi("chat_status_changed", new
            {
                workerId = session.WorkerId,
                sessionId = session.SessionId,
                status = "idle",
                text = "Ready"
            });
        }
        else
        {
            // If actively working (e.g. running background command or slow tool), re-arm watchdog to check again
            lock (session)
            {
                if (session.IsGenerating && session.PendingAction == null)
                {
                    session.QuietTimer?.Dispose();
                    session.QuietTimer = new System.Threading.Timer(OnQuietTimerElapsed, session, 2000, Timeout.Infinite);
                }
            }
        }
    }

    private void OnCliStatusChanged(string sessionId, CliState state, string message)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            string statusStr = state switch
            {
                CliState.Running => session.IsGenerating ? "busy" : "idle",
                CliState.Starting => "busy",
                CliState.Error => "error",
                _ => "offline"
            };

            _postEventToUi("chat_status_changed", new
            {
                workerId = session.WorkerId,
                sessionId,
                status = statusStr,
                text = message
            });
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cliManager.TerminalDataReceivedId -= OnTerminalDataReceived;
        _cliManager.StatusChangedId -= OnCliStatusChanged;

        foreach (var session in _sessions.Values)
        {
            session.QuietTimer?.Dispose();
        }
        _sessions.Clear();
    }
}
