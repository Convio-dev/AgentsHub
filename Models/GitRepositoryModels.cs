#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentsHub.Models;

public sealed class RepositoryItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("head")]
    public string? Head { get; set; }

    [JsonPropertyName("isClean")]
    public bool IsClean => StagedCount == 0 && UnstagedCount == 0 && UntrackedCount == 0;

    [JsonPropertyName("stagedCount")]
    public int StagedCount { get; set; }

    [JsonPropertyName("unstagedCount")]
    public int UnstagedCount { get; set; }

    [JsonPropertyName("untrackedCount")]
    public int UntrackedCount { get; set; }

    [JsonPropertyName("ahead")]
    public int Ahead { get; set; }

    [JsonPropertyName("behind")]
    public int Behind { get; set; }

    [JsonPropertyName("upstream")]
    public string? Upstream { get; set; }

    [JsonPropertyName("remotes")]
    public List<RepositoryRemoteItem> Remotes { get; set; } = [];

    [JsonPropertyName("lastCommitDate")]
    public string? LastCommitDate { get; set; }

    [JsonPropertyName("lastCommitMsg")]
    public string? LastCommitMsg { get; set; }

    [JsonPropertyName("lastCommitAuthor")]
    public string? LastCommitAuthor { get; set; }

    [JsonPropertyName("lastCommitHash")]
    public string? LastCommitHash { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active"; // "idea", "active", "paused", "archived"

    [JsonPropertyName("focus")]
    public string? Focus { get; set; }

    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("addedAt")]
    public string AddedAt { get; set; } = DateTime.UtcNow.ToString("O");

    [JsonPropertyName("broken")]
    public bool Broken { get; set; }

    [JsonPropertyName("worktreeCount")]
    public int WorktreeCount { get; set; } = 1;
}

public sealed class RepositoryRemoteItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("fetchUrl")]
    public string FetchUrl { get; set; } = "";

    [JsonPropertyName("pushUrl")]
    public string PushUrl { get; set; } = "";
}

public sealed class GitFileChange
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("originalPath")]
    public string? OriginalPath { get; set; }

    [JsonPropertyName("indexStatus")]
    public string IndexStatus { get; set; } = " ";

    [JsonPropertyName("worktreeStatus")]
    public string WorktreeStatus { get; set; } = " ";

    [JsonPropertyName("isStaged")]
    public bool IsStaged => IndexStatus != " " && IndexStatus != "?";

    [JsonPropertyName("isUnstaged")]
    public bool IsUnstaged => WorktreeStatus != " " || IndexStatus == "?";

    [JsonPropertyName("isUntracked")]
    public bool IsUntracked => IndexStatus == "?" && WorktreeStatus == "?";

    [JsonPropertyName("isConflict")]
    public bool IsConflict => IndexStatus == "U" || WorktreeStatus == "U" ||
                              (IndexStatus == "A" && WorktreeStatus == "A") ||
                              (IndexStatus == "D" && WorktreeStatus == "D");
}

public sealed class GitDiffResult
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("staged")]
    public bool Staged { get; set; }

    [JsonPropertyName("diffContent")]
    public string DiffContent { get; set; } = "";

    [JsonPropertyName("additions")]
    public int Additions { get; set; }

    [JsonPropertyName("deletions")]
    public int Deletions { get; set; }

    [JsonPropertyName("isBinary")]
    public bool IsBinary { get; set; }

    [JsonPropertyName("isTruncated")]
    public bool IsTruncated { get; set; }
}

public sealed class ProjectLauncherItem
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "custom"; // npm, python, cargo, godot, dotnet, ps1, bat, sh, custom

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 50;

    [JsonPropertyName("healthy")]
    public bool Healthy { get; set; } = true;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "detected"; // detected, custom
}

public sealed class RepositoryHealthFinding
{
    [JsonPropertyName("findingId")]
    public string FindingId { get; set; } = "";

    [JsonPropertyName("rule")]
    public string Rule { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "PASS"; // PASS, WARN, FAIL, UNKNOWN, IGNORED

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "INFO"; // INFO, LOW, MEDIUM, HIGH, CRITICAL

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = "";

    [JsonPropertyName("remediation")]
    public string? Remediation { get; set; }

    [JsonPropertyName("isIgnored")]
    public bool IsIgnored { get; set; }
}

public sealed class RepositoryHealthResult
{
    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; set; } = "PASS";

    [JsonPropertyName("findings")]
    public List<RepositoryHealthFinding> Findings { get; set; } = [];

    [JsonPropertyName("evaluatedAt")]
    public string EvaluatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}

public sealed class RepositoryManagerSettings
{
    [JsonPropertyName("roots")]
    public List<string> Roots { get; set; } = [];

    [JsonPropertyName("depth")]
    public int Depth { get; set; } = 4;

    [JsonPropertyName("skipDirs")]
    public List<string> SkipDirs { get; set; } =
    [
        "node_modules", ".venv", "venv", "__pycache__", ".next",
        "dist", "build", ".godot", "AppData", ".cache", ".mono",
        "Intermediate", "Binaries", "Saved", "DerivedDataCache",
        "site-packages", "obj", "bin", ".git"
    ];

    [JsonPropertyName("defaultAgent")]
    public string DefaultAgent { get; set; } = "claude"; // "claude", "antigravity"

    [JsonPropertyName("preferredEditor")]
    public string PreferredEditor { get; set; } = "ask"; // "ask", "auto", "cursor", "antigravity", "code", "devin", "workbuddy", "custom", or dev tool id

    [JsonPropertyName("customEditorPath")]
    public string? CustomEditorPath { get; set; }

    [JsonPropertyName("ignoredHealthRules")]
    public List<string> IgnoredHealthRules { get; set; } = [];
}

public sealed class InstalledEditorDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("isDetected")]
    public bool IsDetected { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "system"; // "system", "devtool", "custom"

    [JsonPropertyName("executablePath")]
    public string? ExecutablePath { get; set; }
}

public sealed class ScanProgressDto
{
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "Scanning";

    [JsonPropertyName("directoriesScanned")]
    public int DirectoriesScanned { get; set; }

    [JsonPropertyName("reposFound")]
    public int ReposFound { get; set; }

    [JsonPropertyName("currentPath")]
    public string CurrentPath { get; set; } = "";

    [JsonPropertyName("isCancelling")]
    public bool IsCancelling { get; set; }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }
}
