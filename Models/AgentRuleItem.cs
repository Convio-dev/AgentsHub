#nullable enable
using System;
using System.Text.Json.Serialization;

namespace HermesHub.Models;

public sealed class AgentRuleItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "Project"; // "Global", "Project"

    [JsonPropertyName("workspaceId")]
    public string? WorkspaceId { get; set; }

    [JsonPropertyName("sourceFilePath")]
    public string SourceFilePath { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("isExcludedByIsolation")]
    public bool IsExcludedByIsolation { get; set; } = false;

    [JsonPropertyName("tokenFootprintApprox")]
    public int TokenFootprintApprox { get; set; }

    [JsonPropertyName("lastModifiedUtc")]
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
}
