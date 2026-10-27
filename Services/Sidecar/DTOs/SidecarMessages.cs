#nullable enable
using System.Text.Json.Serialization;

namespace AgentsHub.Services.Sidecar.DTOs;

public enum AgentState
{
    Idle,
    Thinking,
    ExecutingTool,
    Streaming,
    Completed,
    Error,
    Aborted
}

// --- Outbound Requests (Host -> Sidecar) ---

public class BaseRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
}

public class AuthRequest : BaseRequest
{
    public AuthRequest() => Action = "auth";

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

public class PingRequest : BaseRequest
{
    public PingRequest() => Action = "ping";
}

public class StartTaskRequest : BaseRequest
{
    public StartTaskRequest() => Action = "start_task";

    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }
}

public class AbortTaskRequest : BaseRequest
{
    public AbortTaskRequest() => Action = "abort_task";

    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class ExecuteToolRequest : BaseRequest
{
    public ExecuteToolRequest() => Action = "execute_tool";

    [JsonPropertyName("callId")]
    public string CallId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public Dictionary<string, object?> Params { get; set; } = [];

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; set; }
}

public class TerminalInputRequest : BaseRequest
{
    public TerminalInputRequest() => Action = "terminal_input";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
}

public class TerminalResizeRequest : BaseRequest
{
    public TerminalResizeRequest() => Action = "terminal_resize";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cols")]
    public int Cols { get; set; } = 120;

    [JsonPropertyName("rows")]
    public int Rows { get; set; } = 32;
}

public class SendChatRequest : BaseRequest
{
    public SendChatRequest() => Action = "send_chat";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    /// <summary>Reasoning effort for this turn, passed to the CLI as <c>--effort</c>.</summary>
    [JsonPropertyName("reasoningEffort")]
    public string? ReasoningEffort { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }
}

public class CheckQuotaRequest : BaseRequest
{
    public CheckQuotaRequest() => Action = "check_quota";

    /// <summary>Profile whose quota pool to read. Empty means the active profile.</summary>
    [JsonPropertyName("profile")]
    public string? Profile { get; set; }
}

public class SwitchAccountRequest : BaseRequest
{
    public SwitchAccountRequest() => Action = "switch_account";

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}

public class ListAccountsRequest : BaseRequest
{
    public ListAccountsRequest() => Action = "list_accounts";
}

public class RefreshTokenRequest : BaseRequest
{
    public RefreshTokenRequest() => Action = "refresh_token";

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;
}

public class StartSessionRequest : BaseRequest
{
    public StartSessionRequest() => Action = "start_session";

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

public class StopSessionRequest : BaseRequest
{
    public StopSessionRequest() => Action = "stop_session";

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;
}

// --- Inbound Events (Sidecar -> Host) ---

public class BaseSidecarEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

public class ThinkingEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("thinkingTokens")]
    public int? ThinkingTokens { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }
}

public class TokenUsageInfo
{
    [JsonPropertyName("inputTokens")]
    public int? InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public int? OutputTokens { get; set; }

    [JsonPropertyName("thinkingTokens")]
    public int? ThinkingTokens { get; set; }

    [JsonPropertyName("totalTokens")]
    public int? TotalTokens { get; set; }
}

public class TokenUsageEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("usage")]
    public TokenUsageInfo? Usage { get; set; }
}

public class QuotaUpdateEvent : BaseSidecarEvent
{
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("quotaPercent")]
    public int QuotaPercent { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

public class ModelInfoEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("reasoningEffort")]
    public string? ReasoningEffort { get; set; }
}

public class ChatTokenEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class SessionStatusEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "idle";

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("quotaPercent")]
    public int? QuotaPercent { get; set; }

    [JsonPropertyName("tokens")]
    public TokenUsageInfo? Tokens { get; set; }
}

public class AccountProfileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}

public class AccountStateEvent : BaseSidecarEvent
{
    [JsonPropertyName("activeAccount")]
    public string ActiveAccount { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("profiles")]
    public List<AccountProfileInfo> Profiles { get; set; } = [];
}

public class ServerReadyEvent : BaseSidecarEvent
{
    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("arch")]
    public string Arch { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public class PongEvent : BaseSidecarEvent
{
    [JsonPropertyName("replyTo")]
    public string? ReplyTo { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("activeSessionsCount")]
    public int ActiveSessionsCount { get; set; }

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("uptimeSec")]
    public int? UptimeSec { get; set; }
}

public class SidecarStatusEvent : BaseSidecarEvent
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "online";

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("activeSessionsCount")]
    public int ActiveSessionsCount { get; set; }

    [JsonPropertyName("uptimeSec")]
    public int UptimeSec { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.0.0";
}

public class AgentStateEvent : BaseSidecarEvent
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "idle";

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class StreamDeltaEvent : BaseSidecarEvent
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("index")]
    public int Index { get; set; }
}

public class ToolStartEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("callId")]
    public string CallId { get; set; } = string.Empty;

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public System.Text.Json.JsonElement Params { get; set; }
}

public class ToolOutputEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("callId")]
    public string CallId { get; set; } = string.Empty;

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    public System.Text.Json.JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }
}

public class TaskCompletedSummary
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("tokensUsed")]
    public int? TokensUsed { get; set; }

    [JsonPropertyName("stepsCount")]
    public int StepsCount { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("completedAt")]
    public string? CompletedAt { get; set; }
}

public class TaskCompletedEvent : BaseSidecarEvent
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public TaskCompletedSummary? Summary { get; set; }
}

public class TerminalDataEvent : BaseSidecarEvent
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
}

public class SidecarErrorEvent : BaseSidecarEvent
{
    [JsonPropertyName("replyTo")]
    public string? ReplyTo { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
