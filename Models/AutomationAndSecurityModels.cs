#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentsHub.Models;

public sealed class ScheduledAutomationTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Automated Task";

    [JsonPropertyName("workspaceId")]
    public string WorkspaceId { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = "dotnet test";

    [JsonPropertyName("intervalMinutes")]
    public int IntervalMinutes { get; set; } = 60; // 5, 15, 30, 60, 120, 1440

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("lastRunUtc")]
    public DateTime? LastRunUtc { get; set; }

    [JsonPropertyName("lastExitCode")]
    public int? LastExitCode { get; set; }

    [JsonPropertyName("lastOutput")]
    public string? LastOutput { get; set; }

    [JsonPropertyName("runCount")]
    public int RunCount { get; set; }
}

public sealed class SecurityAuditResult
{
    [JsonPropertyName("score")]
    public int Score { get; set; } = 100;

    [JsonPropertyName("isSafe")]
    public bool IsSafe { get; set; } = true;

    [JsonPropertyName("issuesCount")]
    public int IssuesCount { get; set; }

    [JsonPropertyName("scannedFilesCount")]
    public int ScannedFilesCount { get; set; }

    [JsonPropertyName("issues")]
    public List<SecurityIssueItem> Issues { get; set; } = [];
}

public sealed class SecurityIssueItem
{
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "Warning"; // Critical, Warning, Notice

    [JsonPropertyName("type")]
    public string Type { get; set; } = "HardcodedSecret";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("matchSnippet")]
    public string MatchSnippet { get; set; } = string.Empty;

    [JsonPropertyName("recommendation")]
    public string Recommendation { get; set; } = string.Empty;
}
