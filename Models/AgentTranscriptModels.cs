#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentsHub.Models;

public sealed class AgentSessionSummary
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("workspaceId")]
    public string? WorkspaceId { get; set; }

    [JsonPropertyName("startTimeUtc")]
    public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastActiveUtc")]
    public DateTime LastActiveUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    [JsonPropertyName("stepCount")]
    public int StepCount { get; set; }

    [JsonPropertyName("toolCallCount")]
    public int ToolCallCount { get; set; }

    [JsonPropertyName("hasErrors")]
    public bool HasErrors { get; set; }

    [JsonPropertyName("transcriptPath")]
    public string TranscriptPath { get; set; } = string.Empty;

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "Antigravity"; // Antigravity, Claude, Hub, OpenCode
}

public sealed class TranscriptStepItem
{
    [JsonPropertyName("stepIndex")]
    public int StepIndex { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // USER_INPUT, PLANNER_RESPONSE, TOOL_CALL

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonPropertyName("toolCalls")]
    public List<TranscriptToolCallItem> ToolCalls { get; set; } = [];

    [JsonPropertyName("status")]
    public string Status { get; set; } = "DONE"; // DONE, ERROR
}

public sealed class TranscriptToolCallItem
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("argumentsJson")]
    public string ArgumentsJson { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "DONE";
}

public sealed class WorkspaceSnapshotItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("workspaceId")]
    public string WorkspaceId { get; set; } = string.Empty;

    [JsonPropertyName("timestampUtc")]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Manual Snapshot";

    [JsonPropertyName("triggerType")]
    public string TriggerType { get; set; } = "Manual"; // Manual, BeforeTask, PreInstaller, AutoWatchdog

    [JsonPropertyName("modifiedFilesCount")]
    public int ModifiedFilesCount { get; set; }

    [JsonPropertyName("snapshotPath")]
    public string SnapshotPath { get; set; } = string.Empty;

    [JsonPropertyName("gitHash")]
    public string? GitHash { get; set; }
}
