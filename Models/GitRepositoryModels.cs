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

    [JsonPropertyName("gitHubUrl")]
    public string? GitHubUrl { get; set; }
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

public sealed class GitCommitGraphNode
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("abbrevHash")]
    public string AbbrevHash { get; set; } = "";

    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = "";

    [JsonPropertyName("authorEmail")]
    public string AuthorEmail { get; set; } = "";

    [JsonPropertyName("authorDate")]
    public string AuthorDate { get; set; } = "";

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("parentHashes")]
    public List<string> ParentHashes { get; set; } = [];

    [JsonPropertyName("refs")]
    public List<string> Refs { get; set; } = [];
}

public sealed class GitCommitDiffDetail
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("parents")]
    public List<string> Parents { get; set; } = [];

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];

    [JsonPropertyName("patch")]
    public string Patch { get; set; } = "";

    [JsonPropertyName("stats")]
    public string Stats { get; set; } = "";
}

public sealed class GitWorktreeItem
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("head")]
    public string Head { get; set; } = "";

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("isBare")]
    public bool IsBare { get; set; }

    [JsonPropertyName("isDetached")]
    public bool IsDetached { get; set; }

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; }

    [JsonPropertyName("lockReason")]
    public string? LockReason { get; set; }

    [JsonPropertyName("isMainWorktree")]
    public bool IsMainWorktree { get; set; }
}

public sealed class GitStashItem
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("refName")]
    public string RefName { get; set; } = "";

    [JsonPropertyName("branch")]
    public string Branch { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
}

public sealed class BatchRepoActionResult
{
    [JsonPropertyName("repoId")]
    public string RepoId { get; set; } = "";

    [JsonPropertyName("repoName")]
    public string RepoName { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("output")]
    public string Output { get; set; } = "";
}

public sealed class GitHubAuthStatus
{
    [JsonPropertyName("isAuthenticated")]
    public bool IsAuthenticated { get; set; }

    [JsonPropertyName("authMethod")]
    public string AuthMethod { get; set; } = "none"; // "cli", "pat", "none"

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("htmlUrl")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = [];

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public sealed class GitHubPublishResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("repoUrl")]
    public string? RepoUrl { get; set; }

    [JsonPropertyName("cloneUrl")]
    public string? CloneUrl { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class GitHubPullRequest
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "open";

    [JsonPropertyName("isDraft")]
    public bool IsDraft { get; set; }

    [JsonPropertyName("htmlUrl")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("userLogin")]
    public string UserLogin { get; set; } = "";

    [JsonPropertyName("userAvatar")]
    public string? UserAvatar { get; set; }

    [JsonPropertyName("headRef")]
    public string HeadRef { get; set; } = "";

    [JsonPropertyName("baseRef")]
    public string BaseRef { get; set; } = "";

    [JsonPropertyName("headSha")]
    public string? HeadSha { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];
}

public sealed class GitHubPullRequestListResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("repoUrl")]
    public string? RepoUrl { get; set; }

    [JsonPropertyName("pullRequests")]
    public List<GitHubPullRequest> PullRequests { get; set; } = [];
}

public sealed class GitMergePreviewResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("sourceBranch")]
    public string SourceBranch { get; set; } = "";

    [JsonPropertyName("targetBranch")]
    public string TargetBranch { get; set; } = "";

    [JsonPropertyName("canMerge")]
    public bool CanMerge { get; set; }

    [JsonPropertyName("isFastForward")]
    public bool IsFastForward { get; set; }

    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; set; }

    [JsonPropertyName("incomingCommitCount")]
    public int IncomingCommitCount { get; set; }

    [JsonPropertyName("incomingCommits")]
    public List<string> IncomingCommits { get; set; } = [];

    [JsonPropertyName("conflictingFiles")]
    public List<string> ConflictingFiles { get; set; } = [];

    [JsonPropertyName("changedFiles")]
    public List<string> ChangedFiles { get; set; } = [];

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class GitMergeExecutionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; set; }

    [JsonPropertyName("output")]
    public string Output { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
