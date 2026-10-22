using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentsHub.Models;

public sealed class CodebaseNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = string.Empty;

    [JsonPropertyName("directory")]
    public string Directory { get; set; } = string.Empty;

    [JsonPropertyName("lineCount")]
    public int LineCount { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("inDegree")]
    public int InDegree { get; set; }

    [JsonPropertyName("outDegree")]
    public int OutDegree { get; set; }

    [JsonPropertyName("isOrphan")]
    public bool IsOrphan { get; set; }

    [JsonPropertyName("churn")]
    public int Churn { get; set; }

    [JsonPropertyName("symbols")]
    public List<string> Symbols { get; set; } = [];
}

public sealed class CodebaseEdge
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 1;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "call"; // "import", "call", "reference"
}

public sealed class CodebaseGraphStats
{
    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("totalConnections")]
    public int TotalConnections { get; set; }

    [JsonPropertyName("orphanCount")]
    public int OrphanCount { get; set; }

    [JsonPropertyName("highRiskFiles")]
    public int HighRiskFiles { get; set; }

    [JsonPropertyName("scanDurationMs")]
    public long ScanDurationMs { get; set; }

    [JsonPropertyName("workspaceRoot")]
    public string WorkspaceRoot { get; set; } = string.Empty;
}

public sealed class BlastRadiusReport
{
    [JsonPropertyName("targetFile")]
    public string TargetFile { get; set; } = string.Empty;

    [JsonPropertyName("targetLabel")]
    public string TargetLabel { get; set; } = string.Empty;

    [JsonPropertyName("directCallers")]
    public List<string> DirectCallers { get; set; } = [];

    [JsonPropertyName("secondaryCallers")]
    public List<string> SecondaryCallers { get; set; } = [];

    [JsonPropertyName("safeFilesCount")]
    public int SafeFilesCount { get; set; }

    [JsonPropertyName("totalImpactedCount")]
    public int TotalImpactedCount { get; set; }

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "Low"; // "Low", "Medium", "High", "Critical"

    [JsonPropertyName("promptShield")]
    public string PromptShield { get; set; } = string.Empty;
}

public sealed class CodebaseGraphResult
{
    [JsonPropertyName("nodes")]
    public List<CodebaseNode> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<CodebaseEdge> Edges { get; set; } = [];

    [JsonPropertyName("stats")]
    public CodebaseGraphStats Stats { get; set; } = new();
}
