#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentsHub.Helpers;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed class GitRepositoryManagerService
{
    public static GitRepositoryManagerService Instance { get; } = new();

    private readonly Lock _lock = new();
    private readonly string _storageDir;
    private readonly string _registryFile;
    private readonly string _settingsFile;
    private readonly string _notesDir;

    private readonly List<RepositoryItem> _repositories = [];
    private RepositoryManagerSettings _settings = new();
    private string? _workingProjectId;

    private CancellationTokenSource? _scanCts;
    private bool _isScanning;
    private ScanProgressDto _lastProgress = new();

    public event Action<ScanProgressDto>? ScanProgressChanged;
    public event Action? RepositoriesChanged;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public GitRepositoryManagerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageDir = Path.Combine(appData, "AgentsHub", "repositories");
        _registryFile = Path.Combine(_storageDir, "registry.json");
        _settingsFile = Path.Combine(_storageDir, "settings.json");
        _notesDir = Path.Combine(_storageDir, "notes");

        EnsureStorageDirectories();
        LoadSettings();
        LoadRegistry();
    }

    private void EnsureStorageDirectories()
    {
        try
        {
            if (!Directory.Exists(_storageDir)) Directory.CreateDirectory(_storageDir);
            if (!Directory.Exists(_notesDir)) Directory.CreateDirectory(_notesDir);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = File.ReadAllText(_settingsFile);
                var loaded = JsonSerializer.Deserialize<RepositoryManagerSettings>(json, JsonOpts);
                if (loaded != null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.PreferredEditor) || loaded.PreferredEditor.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.PreferredEditor = "ask";
                    }
                    _settings = loaded;
                    return;
                }
            }
        }
        catch { }

        // Populate default roots if none configured
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktop = Path.Combine(userProfile, "Desktop");
        var docs = Path.Combine(userProfile, "Documents");

        var roots = new List<string>();
        if (Directory.Exists(desktop)) roots.Add(desktop);
        if (Directory.Exists(docs)) roots.Add(docs);

        _settings.Roots = roots;
        _settings.PreferredEditor = "ask";
        SaveSettings(_settings);
    }

    public void SaveSettings(RepositoryManagerSettings newSettings)
    {
        lock (_lock)
        {
            _settings = newSettings;
            try
            {
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(_settings, JsonOpts));
            }
            catch { }
        }
    }

    public RepositoryManagerSettings GetSettings()
    {
        lock (_lock)
        {
            return _settings;
        }
    }

    private void LoadRegistry()
    {
        try
        {
            if (File.Exists(_registryFile))
            {
                var json = File.ReadAllText(_registryFile);
                var list = JsonSerializer.Deserialize<List<RepositoryItem>>(json, JsonOpts);
                if (list != null)
                {
                    lock (_lock)
                    {
                        _repositories.Clear();
                        _repositories.AddRange(list);
                    }
                }
            }
        }
        catch { }
    }

    private void SaveRegistry()
    {
        try
        {
            List<RepositoryItem> snapshot;
            lock (_lock)
            {
                snapshot = [.. _repositories];
            }
            File.WriteAllText(_registryFile, JsonSerializer.Serialize(snapshot, JsonOpts));
        }
        catch { }
    }

    public List<RepositoryItem> GetRepositories()
    {
        lock (_lock)
        {
            return [.. _repositories];
        }
    }

    public RepositoryItem? GetRepository(string idOrPath)
    {
        lock (_lock)
        {
            return _repositories.FirstOrDefault(r =>
                string.Equals(r.Id, idOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Path, idOrPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string? GetWorkingProjectId()
    {
        lock (_lock)
        {
            return _workingProjectId;
        }
    }

    public RepositoryItem? GetWorkingProject()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_workingProjectId)) return null;
            return _repositories.FirstOrDefault(r => string.Equals(r.Id, _workingProjectId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SetWorkingProject(string? projectId)
    {
        lock (_lock)
        {
            _workingProjectId = projectId;
            foreach (var r in _repositories)
            {
                r.Focus = string.Equals(r.Id, projectId, StringComparison.OrdinalIgnoreCase) ? (r.Focus ?? "Active") : null;
            }
        }
        SaveRegistry();
        RepositoriesChanged?.Invoke();
    }

    public void UpdateRepositoryMetadata(string id, string? status, string? focus, bool? pinned, List<string>? tags, string? notes)
    {
        lock (_lock)
        {
            var repo = _repositories.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (repo == null) return;

            if (status != null) repo.Status = status;
            if (focus != null) repo.Focus = focus;
            if (pinned.HasValue) repo.Pinned = pinned.Value;
            if (tags != null) repo.Tags = tags;
            if (notes != null)
            {
                repo.Notes = notes;
                SaveRepoNotes(repo.Id, notes);
            }
        }
        SaveRegistry();
        RepositoriesChanged?.Invoke();
    }

    public string GetRepoNotes(string repoId)
    {
        try
        {
            var file = Path.Combine(_notesDir, $"{repoId}.md");
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        catch { }
        return "";
    }

    public void SaveRepoNotes(string repoId, string notes)
    {
        try
        {
            var file = Path.Combine(_notesDir, $"{repoId}.md");
            File.WriteAllText(file, notes);
        }
        catch { }
    }

    #region Scan Engine

    public bool IsScanning() => _isScanning;
    public ScanProgressDto GetLastProgress() => _lastProgress;

    public Task<bool> StartScanAsync()
    {
        lock (_lock)
        {
            if (_isScanning) return Task.FromResult(false);
            _isScanning = true;
            _scanCts = new CancellationTokenSource();
        }

        var ct = _scanCts.Token;
        _lastProgress = new ScanProgressDto { Phase = "Starting Discovery", DirectoriesScanned = 0, ReposFound = 0 };
        ScanProgressChanged?.Invoke(_lastProgress);

        _ = Task.Run(async () =>
        {
            try
            {
                var roots = GetSettings().Roots;
                var depth = Math.Max(1, Math.Min(10, GetSettings().Depth));
                var skipDirs = new HashSet<string>(GetSettings().SkipDirs, StringComparer.OrdinalIgnoreCase);

                var discoveredPaths = new ConcurrentBag<string>();
                int dirsScanned = 0;

                foreach (var root in roots)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!Directory.Exists(root)) continue;

                    ScanDirectoryTree(root, root, 0, depth, skipDirs, discoveredPaths, () =>
                    {
                        var count = Interlocked.Increment(ref dirsScanned);
                        if (count % 25 == 0)
                        {
                            _lastProgress = new ScanProgressDto
                            {
                                Phase = "Scanning Directory Trees",
                                DirectoriesScanned = count,
                                ReposFound = discoveredPaths.Count,
                                CurrentPath = root
                            };
                            ScanProgressChanged?.Invoke(_lastProgress);
                        }
                    }, ct);
                }

                if (ct.IsCancellationRequested)
                {
                    _lastProgress.Phase = "Scan Cancelled";
                    _lastProgress.IsCancelling = false;
                    _lastProgress.IsCompleted = true;
                    ScanProgressChanged?.Invoke(_lastProgress);
                    return;
                }

                // Inspect discovered repositories
                _lastProgress.Phase = "Inspecting Git Repositories";
                _lastProgress.ReposFound = discoveredPaths.Count;
                ScanProgressChanged?.Invoke(_lastProgress);

                var updatedRepos = new List<RepositoryItem>();
                int inspected = 0;

                foreach (var path in discoveredPaths)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var repoItem = await InspectRepositoryAsync(path, ct).ConfigureAwait(false);
                        if (repoItem != null)
                        {
                            updatedRepos.Add(repoItem);
                        }
                    }
                    catch { }

                    inspected++;
                    if (inspected % 5 == 0 || inspected == discoveredPaths.Count)
                    {
                        _lastProgress.DirectoriesScanned = dirsScanned;
                        _lastProgress.CurrentPath = path;
                        ScanProgressChanged?.Invoke(_lastProgress);
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    _lastProgress.Phase = "Scan Cancelled";
                    _lastProgress.IsCancelling = false;
                    _lastProgress.IsCompleted = true;
                    ScanProgressChanged?.Invoke(_lastProgress);
                    return;
                }

                lock (_lock)
                {
                    // Merge with existing metadata (curation, notes, pinned, status)
                    var existingMap = _repositories.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
                    var merged = new List<RepositoryItem>();

                    foreach (var fresh in updatedRepos)
                    {
                        if (existingMap.TryGetValue(fresh.Path, out var old))
                        {
                            fresh.Id = old.Id;
                            fresh.Pinned = old.Pinned;
                            fresh.Focus = old.Focus;
                            fresh.Status = old.Status;
                            fresh.Tags = old.Tags;
                            fresh.Notes = old.Notes;
                            fresh.AddedAt = old.AddedAt;
                        }
                        merged.Add(fresh);
                    }

                    _repositories.Clear();
                    _repositories.AddRange(merged.OrderBy(r => r.Name));
                }

                SaveRegistry();

                _lastProgress.Phase = "Scan Completed";
                _lastProgress.IsCompleted = true;
                _lastProgress.ReposFound = _repositories.Count;
                ScanProgressChanged?.Invoke(_lastProgress);
                RepositoriesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GitRepositoryManagerService] Scan failed: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _isScanning = false;
                    _scanCts?.Dispose();
                    _scanCts = null;
                }

                _lastProgress.IsCompleted = true;
                _lastProgress.IsCancelling = false;
                ScanProgressChanged?.Invoke(_lastProgress);
            }
        });

        return Task.FromResult(true);
    }

    public void CancelScan()
    {
        lock (_lock)
        {
            if (_isScanning && _scanCts != null && !_scanCts.IsCancellationRequested)
            {
                _lastProgress.IsCancelling = true;
                _lastProgress.Phase = "Cancelling Scan...";
                ScanProgressChanged?.Invoke(_lastProgress);
                try { _scanCts.Cancel(); } catch { }
            }
        }
    }

    private static void ScanDirectoryTree(string root, string currentDir, int currentDepth, int maxDepth, HashSet<string> skipDirs, ConcurrentBag<string> discovered, Action onDirChecked, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        onDirChecked();

        // Check if currentDir has a .git directory or worktree pointer
        var dotGit = Path.Combine(currentDir, ".git");
        if (Directory.Exists(dotGit) || File.Exists(dotGit))
        {
            discovered.Add(currentDir);
            // Don't recurse into nested directories of a git repository
            return;
        }

        if (currentDepth >= maxDepth) return;

        try
        {
            var subDirs = Directory.GetDirectories(currentDir);
            foreach (var sub in subDirs)
            {
                if (ct.IsCancellationRequested) return;

                var name = Path.GetFileName(sub);
                if (skipDirs.Contains(name) || name.StartsWith('.'))
                {
                    continue;
                }

                ScanDirectoryTree(root, sub, currentDepth + 1, maxDepth, skipDirs, discovered, onDirChecked, ct);
            }
        }
        catch { }
    }

    private async Task<RepositoryItem?> InspectRepositoryAsync(string path, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;

        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;

        var id = "repo_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..12];

        var repo = new RepositoryItem
        {
            Id = id,
            Name = name,
            Path = path,
            AddedAt = DateTime.UtcNow.ToString("O")
        };

        // 1. Branch & HEAD
        var (branchRc, branchOut) = await RunGitAsync(path, ["symbolic-ref", "--quiet", "--short", "HEAD"], ct).ConfigureAwait(false);
        if (branchRc == 0 && !string.IsNullOrWhiteSpace(branchOut))
        {
            repo.Branch = branchOut.Trim();
        }
        else
        {
            var (headRc, headOut) = await RunGitAsync(path, ["rev-parse", "--short", "HEAD"], ct).ConfigureAwait(false);
            if (headRc == 0 && !string.IsNullOrWhiteSpace(headOut))
            {
                repo.Branch = $"(detached: {headOut.Trim()})";
            }
        }

        var (_, fullHead) = await RunGitAsync(path, ["rev-parse", "HEAD"], ct).ConfigureAwait(false);
        repo.Head = fullHead.Trim();

        // 2. Working Tree Dirty Stats (staged, unstaged, untracked)
        var (statusRc, statusOut) = await RunGitAsync(path, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], ct).ConfigureAwait(false);
        if (statusRc == 0)
        {
            int staged = 0, unstaged = 0, untracked = 0;
            var tokens = statusOut.Split('\0');
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (string.IsNullOrEmpty(token) || token.Length < 3) continue;

                var x = token[0];
                var y = token[1];

                if (x == '?' && y == '?')
                {
                    untracked++;
                }
                else
                {
                    if (x != ' ' && x != '?') staged++;
                    if (y != ' ') unstaged++;
                }

                // If rename or copy, skip the secondary original path token
                if ((x == 'R' || x == 'C' || y == 'R' || y == 'C') && i + 1 < tokens.Length)
                {
                    i++;
                }
            }
            repo.StagedCount = staged;
            repo.UnstagedCount = unstaged;
            repo.UntrackedCount = untracked;
        }

        // 3. Ahead / Behind
        var (upRc, upOut) = await RunGitAsync(path, ["rev-parse", "--abbrev-ref", "@{u}"], ct).ConfigureAwait(false);
        if (upRc == 0 && !string.IsNullOrWhiteSpace(upOut))
        {
            repo.Upstream = upOut.Trim();
            var (cntRc, cntOut) = await RunGitAsync(path, ["rev-list", "--left-right", "--count", "HEAD...@{u}"], ct).ConfigureAwait(false);
            if (cntRc == 0 && !string.IsNullOrWhiteSpace(cntOut))
            {
                var parts = cntOut.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[0], out var ahead) && int.TryParse(parts[1], out var behind))
                {
                    repo.Ahead = ahead;
                    repo.Behind = behind;
                }
            }
        }

        // 4. Last Commit Metadata
        var (logRc, logOut) = await RunGitAsync(path, ["log", "-1", "--format=%h%x00%an%x00%aI%x00%s"], ct).ConfigureAwait(false);
        if (logRc == 0 && !string.IsNullOrWhiteSpace(logOut))
        {
            var parts = logOut.Trim().Split('\0');
            if (parts.Length >= 4)
            {
                repo.LastCommitHash = parts[0];
                repo.LastCommitAuthor = parts[1];
                repo.LastCommitDate = parts[2];
                repo.LastCommitMsg = parts[3];
            }
        }

        // 5. Remotes
        var (remRc, remOut) = await RunGitAsync(path, ["remote", "-v"], ct).ConfigureAwait(false);
        if (remRc == 0 && !string.IsNullOrWhiteSpace(remOut))
        {
            var remMap = new Dictionary<string, (string fetch, string push)>(StringComparer.OrdinalIgnoreCase);
            var lines = remOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var remName = parts[0];
                    var url = parts[1];
                    var type = parts[2].Trim('(', ')');

                    if (!remMap.TryGetValue(remName, out var tuple))
                    {
                        tuple = ("", "");
                    }

                    if (type.Equals("fetch", StringComparison.OrdinalIgnoreCase)) tuple.fetch = url;
                    else if (type.Equals("push", StringComparison.OrdinalIgnoreCase)) tuple.push = url;

                    remMap[remName] = tuple;
                }
            }

            repo.Remotes = [.. remMap.Select(kvp => new RepositoryRemoteItem
            {
                Name = kvp.Key,
                FetchUrl = kvp.Value.fetch,
                PushUrl = string.IsNullOrEmpty(kvp.Value.push) ? kvp.Value.fetch : kvp.Value.push
            })];

            foreach (var rem in repo.Remotes)
            {
                var norm = GitHubService.NormalizeGitHubWebUrl(rem.FetchUrl);
                if (norm != null)
                {
                    repo.GitHubUrl = norm;
                    break;
                }
            }
        }

        // 6. Worktrees
        var (wtRc, wtOut) = await RunGitAsync(path, ["worktree", "list", "--porcelain"], ct).ConfigureAwait(false);
        if (wtRc == 0 && !string.IsNullOrWhiteSpace(wtOut))
        {
            repo.WorktreeCount = Math.Max(1, wtOut.Split('\n').Count(l => l.StartsWith("worktree ")));
        }

        return repo;
    }

    #endregion

    #region Git Operations

    public async Task<List<GitFileChange>> GetWorkingTreeChangesAsync(string repoPath)
    {
        var list = new List<GitFileChange>();
        if (!Directory.Exists(repoPath)) return list;

        var (rc, output) = await RunGitAsync(repoPath, ["status", "--porcelain=v1", "-z", "--untracked-files=all"]).ConfigureAwait(false);
        if (rc != 0 || string.IsNullOrEmpty(output)) return list;

        var tokens = output.Split('\0');
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrEmpty(token) || token.Length < 3) continue;

            var x = token[0].ToString();
            var y = token[1].ToString();
            var filePath = token[3..];
            string? originalPath = null;

            if ((x == "R" || x == "C" || y == "R" || y == "C") && i + 1 < tokens.Length)
            {
                originalPath = tokens[++i];
            }

            list.Add(new GitFileChange
            {
                Path = filePath,
                OriginalPath = originalPath,
                IndexStatus = x,
                WorktreeStatus = y
            });
        }

        return list;
    }

    public async Task<GitDiffResult> GetFileDiffAsync(string repoPath, string relativePath, bool staged)
    {
        var result = new GitDiffResult { FilePath = relativePath, Staged = staged };
        if (!Directory.Exists(repoPath)) return result;

        string[] args = staged
            ? ["--literal-pathspecs", "diff", "--cached", "--no-color", "--no-ext-diff", "--", relativePath]
            : ["--literal-pathspecs", "diff", "--no-color", "--no-ext-diff", "--", relativePath];

        var (rc, diffOutput) = await RunGitAsync(repoPath, args).ConfigureAwait(false);

        // If untracked file, git diff produces nothing; read text contents directly
        if (string.IsNullOrEmpty(diffOutput) && !staged)
        {
            var fullPath = Path.Combine(repoPath, relativePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    var fileBytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
                    if (fileBytes.Any(b => b == 0))
                    {
                        result.IsBinary = true;
                        result.DiffContent = "Binary file — diff preview unavailable.";
                        return result;
                    }

                    var text = Encoding.UTF8.GetString(fileBytes);
                    if (text.Length > 200_000)
                    {
                        result.IsTruncated = true;
                        text = text[..200_000] + "\n[Output truncated at 200KB]";
                    }

                    var lines = text.Split('\n');
                    var sb = new StringBuilder();
                    sb.AppendLine($"--- /dev/null");
                    sb.AppendLine($"+++ b/{relativePath}");
                    sb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
                    foreach (var l in lines) sb.AppendLine($"+{l}");

                    result.DiffContent = sb.ToString();
                    result.Additions = lines.Length;
                    return result;
                }
                catch { }
            }
        }

        result.DiffContent = diffOutput;

        // Count additions and deletions
        int adds = 0, dels = 0;
        foreach (var line in diffOutput.Split('\n'))
        {
            if (line.StartsWith('+') && !line.StartsWith("+++")) adds++;
            else if (line.StartsWith('-') && !line.StartsWith("---")) dels++;
        }
        result.Additions = adds;
        result.Deletions = dels;

        return result;
    }

    public async Task<(bool success, string output)> StageChangesAsync(string repoPath, IEnumerable<string> filePaths, bool unstage)
    {
        var files = filePaths.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (files.Count == 0) return (false, "No files specified");

        string[] args;
        if (unstage)
        {
            args = ["--literal-pathspecs", "restore", "--staged", "--", .. files];
        }
        else
        {
            args = ["--literal-pathspecs", "add", "-A", "--", .. files];
        }

        var (rc, outText) = await RunGitAsync(repoPath, args).ConfigureAwait(false);
        return (rc == 0, outText);
    }

    public async Task<(bool success, string output, bool pushed, string pushOutput)> CreateCommitAsync(
        string repoPath, string message, bool stageAll, bool pushToRemote = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return (false, "A commit message is required.", false, "");

        if (stageAll)
        {
            var (addRc, addOut) = await RunGitAsync(repoPath, ["add", "-A"]).ConfigureAwait(false);
            if (addRc != 0) return (false, $"Staging failed: {addOut}", false, "");
        }

        var (rc, outText) = await RunGitAsync(repoPath, ["commit", "-m", message.Trim()]).ConfigureAwait(false);
        if (rc != 0)
        {
            return (false, outText, false, "");
        }

        SoundpackAudioService.Instance.PlaySoundEvent("checkpoint_created", "modern", 60);

        if (!pushToRemote)
        {
            return (true, outText, false, "");
        }

        // Push to live remote
        var (bRc, bOut) = await RunGitAsync(repoPath, ["rev-parse", "--abbrev-ref", "HEAD"]).ConfigureAwait(false);
        string branch = bRc == 0 && !string.IsNullOrWhiteSpace(bOut) ? bOut.Trim() : "HEAD";

        var (pushRc, pushOut) = await RunGitAsync(repoPath, ["push", "-u", "origin", branch], timeoutSeconds: 45).ConfigureAwait(false);
        if (pushRc == 0)
        {
            return (true, $"{outText}\n\n[Pushed to origin/{branch} on live GitHub]", true, pushOut);
        }
        else
        {
            return (true, $"{outText}\n\n[Warning: Commit succeeded locally, but live push failed: {pushOut}]", false, pushOut);
        }
    }

    public async Task<(bool success, string output)> NetworkActionAsync(string repoPath, string operation, string remote, string? branch, bool setUpstream)
    {
        operation = operation.ToLowerInvariant();
        List<string> args;

        switch (operation)
        {
            case "fetch":
                args = ["fetch", remote];
                break;
            case "push":
                args = ["push"];
                if (setUpstream) args.Add("-u");
                args.Add(remote);
                if (!string.IsNullOrEmpty(branch)) args.Add(branch);
                break;
            case "pull":
                args = ["pull", "--ff-only", remote];
                if (!string.IsNullOrEmpty(branch)) args.Add(branch);
                break;
            default:
                return (false, $"Unsupported operation: {operation}");
        }

        var (rc, outText) = await RunGitAsync(repoPath, [.. args]).ConfigureAwait(false);
        return (rc == 0, outText);
    }

    public async Task<(bool success, string output)> SwitchBranchAsync(string repoPath, string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return (false, "Branch name is required.");
        var (rc, outText) = await RunGitAsync(repoPath, ["checkout", branchName.Trim()]).ConfigureAwait(false);
        return (rc == 0, outText);
    }

    public async Task<List<string>> ListBranchesAsync(string repoPath)
    {
        var branches = new List<string>();
        var (rc, outText) = await RunGitAsync(repoPath, ["branch", "-a", "--format=%(refname:short)"]).ConfigureAwait(false);
        if (rc == 0 && !string.IsNullOrWhiteSpace(outText))
        {
            branches = [.. outText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim())];
        }
        return branches;
    }

    public async Task<GitMergePreviewResult> PreviewMergeAsync(string repoPath, string sourceBranch, string? targetBranch = null, CancellationToken ct = default)
    {
        var result = new GitMergePreviewResult
        {
            SourceBranch = sourceBranch?.Trim() ?? "",
            TargetBranch = targetBranch?.Trim() ?? ""
        };

        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            result.Message = "Invalid repository path.";
            return result;
        }

        if (string.IsNullOrWhiteSpace(result.SourceBranch))
        {
            result.Message = "Source branch is required.";
            return result;
        }

        // If targetBranch is empty, resolve HEAD / current branch
        if (string.IsNullOrWhiteSpace(result.TargetBranch))
        {
            var (rcHead, outHead) = await RunGitAsync(repoPath, ["rev-parse", "--abbrev-ref", "HEAD"], ct).ConfigureAwait(false);
            if (rcHead == 0 && !string.IsNullOrWhiteSpace(outHead))
                result.TargetBranch = outHead.Trim();
            else
                result.TargetBranch = "main";
        }

        // 1. Get incoming commits between targetBranch and sourceBranch
        var (rcLog, outLog) = await RunGitAsync(repoPath, ["log", "--oneline", $"{result.TargetBranch}..{result.SourceBranch}"], ct).ConfigureAwait(false);
        if (rcLog == 0 && !string.IsNullOrWhiteSpace(outLog))
        {
            result.IncomingCommits = [.. outLog.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())];
            result.IncomingCommitCount = result.IncomingCommits.Count;
        }

        // 2. Get changed files
        var (rcDiff, outDiff) = await RunGitAsync(repoPath, ["diff", "--name-status", $"{result.TargetBranch}...{result.SourceBranch}"], ct).ConfigureAwait(false);
        if (rcDiff == 0 && !string.IsNullOrWhiteSpace(outDiff))
        {
            result.ChangedFiles = [.. outDiff.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())];
        }

        // 3. Find merge base
        var (rcBase, outBase) = await RunGitAsync(repoPath, ["merge-base", result.TargetBranch, result.SourceBranch], ct).ConfigureAwait(false);
        if (rcBase != 0 || string.IsNullOrWhiteSpace(outBase))
        {
            result.Message = "Branches have unrelated histories or merge base could not be determined.";
            result.CanMerge = false;
            return result;
        }

        string mergeBase = outBase.Trim();

        // 4. Check if fast-forward is possible (mergeBase == target's HEAD)
        var (rcRev, outRev) = await RunGitAsync(repoPath, ["rev-parse", result.TargetBranch], ct).ConfigureAwait(false);
        if (rcRev == 0 && string.Equals(outRev.Trim(), mergeBase, StringComparison.OrdinalIgnoreCase))
        {
            result.IsFastForward = true;
            result.CanMerge = true;
            result.HasConflicts = false;
            result.Success = true;
            result.Message = "Fast-forward merge possible without conflicts.";
            return result;
        }

        // 5. Check conflicts via git merge-tree
        var (rcTree, outTree) = await RunGitAsync(repoPath, ["merge-tree", mergeBase, result.TargetBranch, result.SourceBranch], ct).ConfigureAwait(false);
        if (rcTree == 0 && !string.IsNullOrWhiteSpace(outTree))
        {
            if (outTree.Contains("changed in both") || outTree.Contains("<<<<<") || outTree.Contains("CONFLICT"))
            {
                result.HasConflicts = true;
                result.CanMerge = false;
                result.Message = "Merge conflicts detected.";

                var conflictLines = outTree.Split('\n');
                var conflicts = new List<string>();
                foreach (var cl in conflictLines)
                {
                    if (cl.Contains("changed in both"))
                    {
                        var parts = cl.Split(' ');
                        if (parts.Length > 0) conflicts.Add(parts[^1].Trim());
                    }
                }
                result.ConflictingFiles = [.. conflicts.Distinct()];
            }
            else
            {
                result.CanMerge = true;
                result.HasConflicts = false;
                result.Message = "Automatic 3-way merge cleanly possible.";
            }
        }
        else
        {
            result.CanMerge = true;
            result.Message = "Merge preview calculated.";
        }

        result.Success = true;
        return result;
    }

    public async Task<GitMergeExecutionResult> ExecuteMergeAsync(
        string repoPath,
        string sourceBranch,
        bool fastForwardOnly = false,
        string? message = null,
        CancellationToken ct = default)
    {
        var result = new GitMergeExecutionResult();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            result.Message = "Invalid repository path.";
            return result;
        }

        var args = new List<string> { "merge", sourceBranch.Trim() };
        if (fastForwardOnly)
        {
            args.Add("--ff-only");
        }
        else if (!string.IsNullOrWhiteSpace(message))
        {
            args.Add("-m");
            args.Add(message.Trim());
        }

        var (rc, output) = await RunGitAsync(repoPath, [.. args], ct).ConfigureAwait(false);
        result.Output = output;
        if (rc == 0)
        {
            result.Success = true;
            result.Message = "Merge completed successfully.";
        }
        else
        {
            result.Success = false;
            result.HasConflicts = output.Contains("CONFLICT") || output.Contains("Automatic merge failed");
            result.Message = result.HasConflicts ? "Merge failed with conflicts." : $"Merge failed: {output}";
        }

        return result;
    }

    public async Task<(bool success, string message, string? newCommitHash)> RevertCommitAsync(
        string repoPath,
        string commitHash,
        string mode,
        string? newBranchName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return (false, "Invalid repository path.", null);
        if (string.IsNullOrWhiteSpace(commitHash))
            return (false, "Commit hash is required.", null);

        commitHash = commitHash.Trim();
        mode = mode?.Trim().ToLowerInvariant() ?? "new_branch";

        // 1. Pre-flight check: Ensure working directory is clean
        var (statusRc, statusOut) = await RunGitAsync(repoPath, ["status", "--porcelain=v1"], ct).ConfigureAwait(false);
        if (statusRc != 0)
        {
            return (false, $"Failed to check repository status: {statusOut}", null);
        }

        if (!string.IsNullOrWhiteSpace(statusOut))
        {
            return (false, "Working directory has uncommitted or unstaged changes. Please stash or commit them before reverting.", null);
        }

        // 2. Identify current branch
        var (bRc, bOut) = await RunGitAsync(repoPath, ["symbolic-ref", "--quiet", "--short", "HEAD"], ct).ConfigureAwait(false);
        string currentBranch = bRc == 0 && !string.IsNullOrWhiteSpace(bOut) ? bOut.Trim() : "HEAD";

        string targetBranch = currentBranch;
        bool createdNewBranch = false;

        // 3. If mode is new_branch, create and switch to it first
        if (mode == "new_branch")
        {
            if (string.IsNullOrWhiteSpace(newBranchName))
            {
                newBranchName = $"revert-{commitHash[..Math.Min(7, commitHash.Length)]}";
            }
            newBranchName = newBranchName.Trim();

            var (cbRc, cbOut) = await RunGitAsync(repoPath, ["checkout", "-b", newBranchName], ct).ConfigureAwait(false);
            if (cbRc != 0)
            {
                return (false, $"Failed to create and switch to branch '{newBranchName}': {cbOut}", null);
            }
            createdNewBranch = true;
            targetBranch = newBranchName;
        }

        // 4. Run git revert
        var (revRc, revOut) = await RunGitAsync(repoPath, ["revert", "--no-edit", commitHash], ct, timeoutSeconds: 30).ConfigureAwait(false);

        if (revRc == 0)
        {
            var (hRc, hOut) = await RunGitAsync(repoPath, ["rev-parse", "--short", "HEAD"], ct).ConfigureAwait(false);
            string newHash = hRc == 0 && !string.IsNullOrWhiteSpace(hOut) ? hOut.Trim() : "";

            string msg = createdNewBranch
                ? $"Successfully reverted commit {commitHash[..Math.Min(7, commitHash.Length)]} on new branch '{targetBranch}' (Commit: {newHash})."
                : $"Successfully reverted commit {commitHash[..Math.Min(7, commitHash.Length)]} on branch '{targetBranch}' (Commit: {newHash}).";

            return (true, msg, newHash);
        }

        // 5. Conflict or Failure detected! Abort revert cleanly to protect user's working copy
        await RunGitAsync(repoPath, ["revert", "--abort"], ct).ConfigureAwait(false);

        // If we created a temporary new branch for the revert, switch back and cleanup
        if (createdNewBranch)
        {
            await RunGitAsync(repoPath, ["checkout", currentBranch], ct).ConfigureAwait(false);
            await RunGitAsync(repoPath, ["branch", "-D", targetBranch], ct).ConfigureAwait(false);
        }

        return (false, $"Revert failed due to merge conflicts or errors with recent changes. The revert was automatically aborted to keep your files safe.\n\nGit details:\n{revOut.Trim()}", null);
    }

    public async Task<(bool success, string message)> CreateBranchAtCommitAsync(
        string repoPath,
        string commitHash,
        string branchName,
        bool checkout = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return (false, "Invalid repository path.");
        if (string.IsNullOrWhiteSpace(commitHash))
            return (false, "Commit hash is required.");
        if (string.IsNullOrWhiteSpace(branchName))
            return (false, "Branch name is required.");

        branchName = branchName.Trim();
        commitHash = commitHash.Trim();

        var args = checkout
            ? new[] { "checkout", "-b", branchName, commitHash }
            : new[] { "branch", branchName, commitHash };

        var (rc, outText) = await RunGitAsync(repoPath, args, ct).ConfigureAwait(false);
        if (rc == 0)
        {
            string msg = checkout
                ? $"Created and checked out branch '{branchName}' at commit {commitHash[..Math.Min(7, commitHash.Length)]}."
                : $"Created branch '{branchName}' at commit {commitHash[..Math.Min(7, commitHash.Length)]}.";
            return (true, msg);
        }

        return (false, string.IsNullOrWhiteSpace(outText) ? "Failed to create branch." : outText.Trim());
    }

    #endregion

    #region Launchers & Diagnostics

    public List<ProjectLauncherItem> DetectLaunchers(string repoPath)
    {
        var list = new List<ProjectLauncherItem>();
        if (!Directory.Exists(repoPath)) return list;

        // 1. Node.js / NPM Scripts
        var pkgPath = Path.Combine(repoPath, "package.json");
        if (File.Exists(pkgPath))
        {
            try
            {
                var pkgText = File.ReadAllText(pkgPath);
                using var doc = JsonDocument.Parse(pkgText);
                if (doc.RootElement.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in scripts.EnumerateObject())
                    {
                        list.Add(new ProjectLauncherItem
                        {
                            Label = $"npm run {prop.Name}",
                            Type = "npm",
                            Command = "npm",
                            Args = ["run", prop.Name],
                            Cwd = repoPath,
                            Priority = prop.Name is "dev" or "start" ? 10 : 30
                        });
                    }
                }
            }
            catch { }
        }

        // 2. Python
        if (File.Exists(Path.Combine(repoPath, "pyproject.toml")) || File.Exists(Path.Combine(repoPath, "requirements.txt")))
        {
            if (File.Exists(Path.Combine(repoPath, "manage.py")))
            {
                list.Add(new ProjectLauncherItem
                {
                    Label = "python manage.py runserver",
                    Type = "python",
                    Command = "python",
                    Args = ["manage.py", "runserver"],
                    Cwd = repoPath,
                    Priority = 15
                });
            }
            else if (File.Exists(Path.Combine(repoPath, "main.py")))
            {
                list.Add(new ProjectLauncherItem
                {
                    Label = "python main.py",
                    Type = "python",
                    Command = "python",
                    Args = ["main.py"],
                    Cwd = repoPath,
                    Priority = 20
                });
            }
            else if (File.Exists(Path.Combine(repoPath, "app.py")))
            {
                list.Add(new ProjectLauncherItem
                {
                    Label = "python app.py",
                    Type = "python",
                    Command = "python",
                    Args = ["app.py"],
                    Cwd = repoPath,
                    Priority = 20
                });
            }
        }

        // 3. Rust / Cargo
        if (File.Exists(Path.Combine(repoPath, "Cargo.toml")))
        {
            list.Add(new ProjectLauncherItem
            {
                Label = "cargo run",
                Type = "cargo",
                Command = "cargo",
                Args = ["run"],
                Cwd = repoPath,
                Priority = 15
            });
        }

        // 4. .NET
        var slnOrCsproj = Directory.GetFiles(repoPath, "*.sln").Concat(Directory.GetFiles(repoPath, "*.csproj")).FirstOrDefault();
        if (!string.IsNullOrEmpty(slnOrCsproj))
        {
            list.Add(new ProjectLauncherItem
            {
                Label = "dotnet run",
                Type = "dotnet",
                Command = "dotnet",
                Args = ["run"],
                Cwd = repoPath,
                Priority = 15
            });
        }

        // 5. Godot
        if (File.Exists(Path.Combine(repoPath, "project.godot")))
        {
            list.Add(new ProjectLauncherItem
            {
                Label = "Godot Project Run",
                Type = "godot",
                Command = "godot",
                Args = [],
                Cwd = repoPath,
                Priority = 10
            });
        }

        // 6. Scripts (run.bat, start.ps1)
        if (File.Exists(Path.Combine(repoPath, "run.bat")))
        {
            list.Add(new ProjectLauncherItem
            {
                Label = "run.bat",
                Type = "bat",
                Command = "cmd.exe",
                Args = ["/c", "run.bat"],
                Cwd = repoPath,
                Priority = 25
            });
        }
        if (File.Exists(Path.Combine(repoPath, "start.ps1")))
        {
            list.Add(new ProjectLauncherItem
            {
                Label = "start.ps1",
                Type = "ps1",
                Command = "powershell.exe",
                Args = ["-ExecutionPolicy", "Bypass", "-File", "start.ps1"],
                Cwd = repoPath,
                Priority = 25
            });
        }

        return [.. list.OrderBy(l => l.Priority)];
    }

    public bool ExecuteLauncher(string repoPath, ProjectLauncherItem launcher)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = launcher.Command,
                WorkingDirectory = string.IsNullOrEmpty(launcher.Cwd) ? repoPath : launcher.Cwd,
                UseShellExecute = true
            };
            foreach (var arg in launcher.Args) psi.ArgumentList.Add(arg);
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GitRepositoryManagerService] Failed to execute launcher: {ex.Message}");
            return false;
        }
    }

    public RepositoryHealthResult EvaluateHealth(RepositoryItem repo)
    {
        var findings = new List<RepositoryHealthFinding>();
        var ignoredRules = new HashSet<string>(GetSettings().IgnoredHealthRules, StringComparer.OrdinalIgnoreCase);

        // 1. Accessibility
        if (!Directory.Exists(repo.Path))
        {
            findings.Add(new RepositoryHealthFinding
            {
                FindingId = "repo_accessible",
                Rule = "repository_accessible",
                Status = "FAIL",
                Severity = "CRITICAL",
                Title = "Repository directory missing",
                Explanation = $"The folder '{repo.Path}' cannot be accessed or was deleted.",
                Remediation = "Restore the folder or remove this repository from inventory."
            });
            return new RepositoryHealthResult { OverallStatus = "FAIL", Findings = findings };
        }

        // 2. Working Tree Cleanliness
        if (!repo.IsClean)
        {
            var isIgnored = ignoredRules.Contains("working_tree_clean");
            findings.Add(new RepositoryHealthFinding
            {
                FindingId = "working_tree_clean",
                Rule = "working_tree_clean",
                Status = isIgnored ? "IGNORED" : "WARN",
                Severity = "LOW",
                Title = "Uncommitted changes present",
                Explanation = $"Repository has {repo.StagedCount} staged, {repo.UnstagedCount} unstaged, and {repo.UntrackedCount} untracked files.",
                Remediation = "Review changes in the Changes tab and commit or discard them.",
                IsIgnored = isIgnored
            });
        }
        else
        {
            findings.Add(new RepositoryHealthFinding
            {
                FindingId = "working_tree_clean",
                Rule = "working_tree_clean",
                Status = "PASS",
                Severity = "INFO",
                Title = "Working tree clean",
                Explanation = "All files are tracked and committed."
            });
        }

        // 3. Remote Tracking
        if (repo.Remotes.Count == 0)
        {
            var isIgnored = ignoredRules.Contains("remote_presence");
            findings.Add(new RepositoryHealthFinding
            {
                FindingId = "remote_presence",
                Rule = "remote_presence",
                Status = isIgnored ? "IGNORED" : "WARN",
                Severity = "LOW",
                Title = "No remote configured",
                Explanation = "Repository exists only on local disk without a remote mirror or backup.",
                Remediation = "Configure a remote origin on GitHub, GitLab, or personal server.",
                IsIgnored = isIgnored
            });
        }

        // 4. Unpushed Commits
        if (repo.Ahead > 0)
        {
            var isIgnored = ignoredRules.Contains("unpushed_commits");
            findings.Add(new RepositoryHealthFinding
            {
                FindingId = "unpushed_commits",
                Rule = "unpushed_commits",
                Status = isIgnored ? "IGNORED" : "WARN",
                Severity = "LOW",
                Title = $"{repo.Ahead} unpushed commit(s)",
                Explanation = $"Local branch is ahead of '{repo.Upstream ?? "origin"}' by {repo.Ahead} commit(s).",
                Remediation = "Push your commits to remote repository.",
                IsIgnored = isIgnored
            });
        }

        // 5. Standard Documentation
        CheckFilePresence(repo.Path, ["README.md", "README", "README.txt"], "readme_presence", "README Documentation", "Project documentation file found.", "No README documentation file was detected.", findings, ignoredRules);
        CheckFilePresence(repo.Path, ["LICENSE", "LICENSE.md", "COPYING"], "license_presence", "Open Source License", "License file found.", "No LICENSE file was detected.", findings, ignoredRules);
        CheckFilePresence(repo.Path, [".gitignore"], "gitignore_presence", "Git Ignore Rules", ".gitignore file present.", "No .gitignore file detected. Build artifacts may be tracked accidentally.", findings, ignoredRules);

        var overall = findings.Any(f => f.Status == "FAIL" && !f.IsIgnored) ? "FAIL" :
                      findings.Any(f => f.Status == "WARN" && !f.IsIgnored) ? "WARN" : "PASS";

        return new RepositoryHealthResult
        {
            OverallStatus = overall,
            Findings = findings
        };
    }

    private static void CheckFilePresence(string repoPath, string[] candidateNames, string rule, string title, string passText, string warnText, List<RepositoryHealthFinding> findings, HashSet<string> ignoredRules)
    {
        bool present = candidateNames.Any(c => File.Exists(Path.Combine(repoPath, c)));
        bool isIgnored = ignoredRules.Contains(rule);

        findings.Add(new RepositoryHealthFinding
        {
            FindingId = rule,
            Rule = rule,
            Status = present ? "PASS" : (isIgnored ? "IGNORED" : "WARN"),
            Severity = present ? "INFO" : "LOW",
            Title = title,
            Explanation = present ? passText : warnText,
            Remediation = present ? null : $"Add a {candidateNames[0]} file to project root.",
            IsIgnored = isIgnored
        });
    }

    public string ExportInventory(string format)
    {
        var repos = GetRepositories();
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(repos, JsonOpts);
        }

        // Markdown format
        var sb = new StringBuilder();
        sb.AppendLine("# Repository Inventory Export");
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*\n");
        sb.AppendLine("| Name | Branch | Status | Working Tree | Upstream | Path |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
        foreach (var r in repos)
        {
            var wt = r.IsClean ? "Clean" : $"+{r.StagedCount} ~{r.UnstagedCount} ?{r.UntrackedCount}";
            var sync = r.Ahead > 0 || r.Behind > 0 ? $"↑{r.Ahead} ↓{r.Behind}" : "Synced";
            sb.AppendLine($"| **{r.Name}** | `{r.Branch ?? "-"}` | `{r.Status}` | `{wt}` | `{sync}` | `{r.Path}` |");
        }
        return sb.ToString();
    }

    public (bool success, string message) OpenInEditor(string repoPath, string? targetEditor = null)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return (false, "Repository path is invalid or does not exist.");
        }

        var settings = GetSettings();
        var pref = targetEditor ?? settings.PreferredEditor ?? "ask";

        try
        {
            // 0. Explorer or Ask
            if (string.Equals(pref, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{repoPath}\"") { UseShellExecute = true });
                return (true, "Opened in File Explorer");
            }

            if (string.Equals(pref, "ask", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Preferred editor is set to 'Always Ask'. Please choose an editor.");
            }

            // 0b. Direct match against configured AI Dev Tools (synced with DevTools view)
            var devTools = AiConfigService.Instance.CurrentConfig.DevTools ?? [];
            var matchedTool = devTools.FirstOrDefault(d =>
                string.Equals(d.Id, pref, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Name, pref, StringComparison.OrdinalIgnoreCase));

            if (matchedTool != null && !string.IsNullOrWhiteSpace(matchedTool.Target))
            {
                var normTarget = DevToolTargets.Normalize(matchedTool.Target);
                if (normTarget.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", normTarget) { UseShellExecute = true });
                    return (true, $"Opened in {matchedTool.Name}");
                }
                else if (File.Exists(normTarget))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = normTarget,
                        Arguments = string.IsNullOrWhiteSpace(matchedTool.Arguments) ? $"\"{repoPath}\"" : $"{matchedTool.Arguments} \"{repoPath}\"",
                        WorkingDirectory = string.IsNullOrWhiteSpace(matchedTool.WorkingDirectory) ? repoPath : matchedTool.WorkingDirectory,
                        UseShellExecute = true
                    });
                    return (true, $"Opened in {matchedTool.Name}");
                }
            }

            // 1. Explicit Custom Editor
            if (pref.Equals("custom", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(settings.CustomEditorPath) && File.Exists(settings.CustomEditorPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = settings.CustomEditorPath,
                        Arguments = $"\"{repoPath}\"",
                        UseShellExecute = true
                    });
                    return (true, $"Opened in {Path.GetFileNameWithoutExtension(settings.CustomEditorPath)}");
                }
            }

            // 2. Locate editor by preference or auto-detection
            var editorExe = FindEditorExecutable(pref, settings.CustomEditorPath);
            if (!string.IsNullOrEmpty(editorExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = editorExe,
                    Arguments = $"\"{repoPath}\"",
                    UseShellExecute = true
                });
                return (true, $"Opened in {Path.GetFileNameWithoutExtension(editorExe)}");
            }

            // 3. Command line fallbacks (code, cursor, agy, devin)
            string[] cmdCandidates = pref.ToLowerInvariant() switch
            {
                "cursor" => ["cursor"],
                "code" or "vscode" => ["code"],
                "antigravity" => ["agy"],
                "devin" => ["devin"],
                "workbuddy" => ["workbuddy"],
                "rider" => ["rider"],
                "sublime" => ["subl"],
                "auto" => ["code", "cursor", "agy"],
                _ => []
            };

            foreach (var cmd in cmdCandidates)
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {cmd} \"{repoPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    if (p != null) return (true, $"Opened using '{cmd}' command");
                }
                catch { }
            }

            // 4. Fallback to explorer.exe
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{repoPath}\"") { UseShellExecute = true });
            return (true, "Opened repository folder in Explorer (no editor detected).");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to launch editor: {ex.Message}");
        }
    }

    public List<InstalledEditorDto> GetInstalledEditors()
    {
        var settings = GetSettings();
        var list = new List<InstalledEditorDto>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Sync from AI Dev Tools (tools flagged as IDE or matching known IDE executable/name)
        var devTools = AiConfigService.Instance.CurrentConfig.DevTools ?? [];
        foreach (var tool in devTools)
        {
            if (!tool.IsEnabled) continue;
            bool isIde = tool.IsIde ?? InstalledClients.IsKnownIde(tool.Name, tool.Target);
            if (!isIde) continue;

            var normTarget = DevToolTargets.Normalize(tool.Target);
            bool exists = !string.IsNullOrWhiteSpace(normTarget) && (File.Exists(normTarget) || normTarget.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase));
            string id = tool.Id.ToLowerInvariant();
            if (seenIds.Add(id))
            {
                list.Add(new InstalledEditorDto
                {
                    Id = tool.Id,
                    Name = tool.Name,
                    Description = !string.IsNullOrWhiteSpace(tool.Description) ? tool.Description : "Configured in AI Dev Tools",
                    IsDetected = exists,
                    Source = "devtool",
                    ExecutablePath = tool.Target
                });
            }
        }

        // 2. Standard built-in IDE detection
        var wellKnown = new (string id, string name, string desc)[]
        {
            ("cursor", "Cursor", "AI-first code editor"),
            ("antigravity", "Antigravity IDE", "Autonomous pair-programming IDE"),
            ("code", "Visual Studio Code", "Extensible code editor"),
            ("devin", "Devin", "AI software engineer workspace"),
            ("workbuddy", "WorkBuddy AI", "AI workspace companion"),
            ("rider", "JetBrains Rider", "Fast .NET & Game dev IDE"),
            ("idea", "IntelliJ IDEA", "Capable Java & Kotlin IDE"),
            ("pycharm", "PyCharm", "Python IDE for developers"),
            ("devenv", "Visual Studio", "Full-featured Windows IDE"),
            ("sublime", "Sublime Text", "Sophisticated text editor"),
            ("notepadpp", "Notepad++", "Free source code editor")
        };

        foreach (var (id, name, desc) in wellKnown)
        {
            if (seenIds.Contains(id) || list.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            var exe = FindEditorExecutable(id, null);
            bool isDetected = !string.IsNullOrEmpty(exe);

            // Include primary editors or any detected tool
            if (isDetected || id is "cursor" or "antigravity" or "code" or "devin" or "workbuddy")
            {
                seenIds.Add(id);
                list.Add(new InstalledEditorDto
                {
                    Id = id,
                    Name = name,
                    Description = desc,
                    IsDetected = isDetected,
                    Source = "system",
                    ExecutablePath = exe
                });
            }
        }

        // 3. File Explorer
        list.Add(new InstalledEditorDto
        {
            Id = "explorer",
            Name = "File Explorer",
            Description = "Windows File Explorer folder view",
            IsDetected = true,
            Source = "system"
        });

        // 4. Custom Executable if configured
        list.Add(new InstalledEditorDto
        {
            Id = "custom",
            Name = "Custom Executable...",
            Description = string.IsNullOrWhiteSpace(settings.CustomEditorPath) ? "Select custom editor executable" : settings.CustomEditorPath,
            IsDetected = !string.IsNullOrWhiteSpace(settings.CustomEditorPath) && File.Exists(settings.CustomEditorPath),
            Source = "custom",
            ExecutablePath = settings.CustomEditorPath
        });

        return list;
    }

    private static string? FindEditorExecutable(string preferred, string? customPath)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new List<(string key, string path)>();

        // Custom path if set
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            candidates.Add(("custom", customPath));
        }

        // Cursor
        candidates.Add(("cursor", Path.Combine(localAppData, @"Programs\cursor\Cursor.exe")));
        candidates.Add(("cursor", Path.Combine(progFiles, @"Cursor\Cursor.exe")));
        candidates.Add(("cursor", Path.Combine(userProfile, @"scoop\apps\cursor\current\Cursor.exe")));

        // Antigravity IDE
        candidates.Add(("antigravity", Path.Combine(localAppData, @"Programs\Antigravity IDE\Antigravity IDE.exe")));
        candidates.Add(("antigravity", Path.Combine(progFiles, @"Antigravity IDE\Antigravity IDE.exe")));
        candidates.Add(("antigravity", Path.Combine(localAppData, @"Programs\antigravity\Antigravity.exe")));

        // Visual Studio Code
        candidates.Add(("code", Path.Combine(localAppData, @"Programs\Microsoft VS Code\Code.exe")));
        candidates.Add(("code", Path.Combine(progFiles, @"Microsoft VS Code\Code.exe")));
        candidates.Add(("code", Path.Combine(progFilesX86, @"Microsoft VS Code\Code.exe")));
        candidates.Add(("code", Path.Combine(localAppData, @"Programs\Microsoft VS Code Insiders\Code - Insiders.exe")));
        candidates.Add(("code", Path.Combine(localAppData, @"Programs\VSCodium\VSCodium.exe")));
        candidates.Add(("code", Path.Combine(userProfile, @"scoop\apps\vscode\current\Code.exe")));

        // Devin
        candidates.Add(("devin", Path.Combine(localAppData, @"Programs\Devin\Devin.exe")));
        candidates.Add(("devin", Path.Combine(localAppData, @"Devin\Devin.exe")));
        candidates.Add(("devin", Path.Combine(appData, @"Devin\Devin.exe")));
        candidates.Add(("devin", Path.Combine(progFiles, @"Devin\Devin.exe")));

        // WorkBuddy
        candidates.Add(("workbuddy", Path.Combine(localAppData, @"Programs\WorkBuddy\WorkBuddy.exe")));
        candidates.Add(("workbuddy", Path.Combine(localAppData, @"WorkBuddy\WorkBuddy.exe")));
        candidates.Add(("workbuddy", Path.Combine(appData, @"WorkBuddy\WorkBuddy.exe")));
        candidates.Add(("workbuddy", Path.Combine(progFiles, @"WorkBuddy\WorkBuddy.exe")));

        // JetBrains Rider
        candidates.Add(("rider", @"C:\Program Files\JetBrains\JetBrains Rider\bin\rider64.exe"));
        candidates.Add(("rider", Path.Combine(localAppData, @"Programs\Rider\bin\rider64.exe")));

        // JetBrains IntelliJ IDEA
        candidates.Add(("idea", @"C:\Program Files\JetBrains\IntelliJ IDEA\bin\idea64.exe"));
        candidates.Add(("idea", Path.Combine(localAppData, @"Programs\IntelliJ IDEA\bin\idea64.exe")));

        // JetBrains PyCharm
        candidates.Add(("pycharm", @"C:\Program Files\JetBrains\PyCharm\bin\pycharm64.exe"));
        candidates.Add(("pycharm", Path.Combine(localAppData, @"Programs\PyCharm\bin\pycharm64.exe")));

        // Visual Studio (devenv)
        candidates.Add(("devenv", @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"));
        candidates.Add(("devenv", @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe"));
        candidates.Add(("devenv", @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe"));

        // Sublime Text
        candidates.Add(("sublime", @"C:\Program Files\Sublime Text\sublime_text.exe"));
        candidates.Add(("sublime", @"C:\Program Files\Sublime Text 3\sublime_text.exe"));

        // Notepad++
        candidates.Add(("notepadpp", @"C:\Program Files\Notepad++\notepad++.exe"));
        candidates.Add(("notepadpp", @"C:\Program Files (x86)\Notepad++\notepad++.exe"));

        // Check if preferred matches any specific candidate
        if (!string.Equals(preferred, "auto", StringComparison.OrdinalIgnoreCase) && !string.Equals(preferred, "ask", StringComparison.OrdinalIgnoreCase))
        {
            var match = candidates.FirstOrDefault(c => string.Equals(c.key, preferred, StringComparison.OrdinalIgnoreCase) && File.Exists(c.path));
            return match.path;
        }

        // Auto mode: return first existing detected editor
        if (string.Equals(preferred, "auto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var c in candidates)
            {
                if (File.Exists(c.path)) return c.path;
            }
        }

        return null;
    }

    #endregion

    #region Commit Graph & Details

    public async Task<List<GitCommitGraphNode>> GetCommitGraphAsync(string repoPath, int maxCount = 100, CancellationToken ct = default)
    {
        var list = new List<GitCommitGraphNode>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return list;

        int count = Math.Clamp(maxCount, 10, 300);
        var (rc, output) = await RunGitAsync(repoPath, ["log", $"-n{count}", "--topo-order", "--pretty=format:%H%x00%h%x00%an%x00%ae%x00%aI%x00%s%x00%P%x00%d"], ct).ConfigureAwait(false);
        if (rc != 0 || string.IsNullOrWhiteSpace(output)) return list;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var parts = line.Split('\0');
            if (parts.Length < 6) continue;

            List<string> parents = (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
                ? [.. parts[6].Split(' ', StringSplitOptions.RemoveEmptyEntries)]
                : [];

            List<string> refs = [];
            if (parts.Length > 7 && !string.IsNullOrWhiteSpace(parts[7]))
            {
                var clean = parts[7].Trim().TrimStart('(').TrimEnd(')');
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    refs = [.. clean.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim())
                        .Where(r => !string.IsNullOrEmpty(r))];
                }
            }

            list.Add(new GitCommitGraphNode
            {
                Hash = parts[0].Trim(),
                AbbrevHash = parts[1].Trim(),
                AuthorName = parts[2].Trim(),
                AuthorEmail = parts[3].Trim(),
                AuthorDate = parts[4].Trim(),
                Subject = parts[5].Trim(),
                ParentHashes = parents,
                Refs = refs
            });
        }

        return list;
    }

    public async Task<GitCommitDiffDetail?> GetCommitDiffDetailAsync(string repoPath, string commitHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath) || string.IsNullOrWhiteSpace(commitHash))
            return null;

        var (rcMeta, outMeta) = await RunGitAsync(repoPath, ["show", "--quiet", "--pretty=format:%H%x00%an%x00%aI%x00%s%x00%P%x00%B", commitHash], ct).ConfigureAwait(false);
        if (rcMeta != 0 || string.IsNullOrWhiteSpace(outMeta)) return null;

        var parts = outMeta.Split('\0');
        if (parts.Length < 6) return null;

        var (rcStat, outStat) = await RunGitAsync(repoPath, ["show", "--stat", "--oneline", commitHash], ct).ConfigureAwait(false);
        var (rcPatch, outPatch) = await RunGitAsync(repoPath, ["show", "--patch", "--no-color", commitHash], ct).ConfigureAwait(false);
        var (rcFiles, outFiles) = await RunGitAsync(repoPath, ["diff-tree", "--no-commit-id", "--name-only", "-r", commitHash], ct).ConfigureAwait(false);

        List<string> fileList = (rcFiles == 0 && !string.IsNullOrWhiteSpace(outFiles))
            ? [.. outFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim().TrimEnd('\r'))]
            : [];

        List<string> parents = !string.IsNullOrWhiteSpace(parts[4])
            ? [.. parts[4].Split(' ', StringSplitOptions.RemoveEmptyEntries)]
            : [];

        return new GitCommitDiffDetail
        {
            Hash = parts[0].Trim(),
            Author = parts[1].Trim(),
            Date = parts[2].Trim(),
            Message = parts[5].Trim(),
            Parents = parents,
            Files = fileList,
            Patch = rcPatch == 0 ? outPatch : "",
            Stats = rcStat == 0 ? outStat : ""
        };
    }

    #endregion

    #region Git Worktrees

    public async Task<List<GitWorktreeItem>> GetWorktreesAsync(string repoPath, CancellationToken ct = default)
    {
        var list = new List<GitWorktreeItem>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return list;

        var (rc, output) = await RunGitAsync(repoPath, ["worktree", "list", "--porcelain"], ct).ConfigureAwait(false);
        if (rc != 0 || string.IsNullOrWhiteSpace(output)) return list;

        var lines = output.Split('\n');
        GitWorktreeItem? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current != null && !string.IsNullOrWhiteSpace(current.Path))
                {
                    current.IsMainWorktree = string.Equals(Path.GetFullPath(current.Path), Path.GetFullPath(repoPath), StringComparison.OrdinalIgnoreCase);
                    list.Add(current);
                    current = null;
                }
                continue;
            }

            if (line.StartsWith("worktree "))
            {
                if (current != null && !string.IsNullOrWhiteSpace(current.Path))
                {
                    current.IsMainWorktree = string.Equals(Path.GetFullPath(current.Path), Path.GetFullPath(repoPath), StringComparison.OrdinalIgnoreCase);
                    list.Add(current);
                }
                current = new GitWorktreeItem { Path = line[9..].Trim() };
            }
            else if (current != null)
            {
                if (line.StartsWith("HEAD ")) current.Head = line[5..].Trim();
                else if (line.StartsWith("branch "))
                {
                    var b = line[7..].Trim();
                    if (b.StartsWith("refs/heads/")) b = b[11..];
                    current.Branch = b;
                }
                else if (line == "bare") current.IsBare = true;
                else if (line == "detached") current.IsDetached = true;
                else if (line.StartsWith("locked"))
                {
                    current.IsLocked = true;
                    var r = line.Length > 6 ? line[6..].Trim() : "";
                    if (!string.IsNullOrEmpty(r)) current.LockReason = r;
                }
            }
        }

        if (current != null && !string.IsNullOrWhiteSpace(current.Path))
        {
            current.IsMainWorktree = string.Equals(Path.GetFullPath(current.Path), Path.GetFullPath(repoPath), StringComparison.OrdinalIgnoreCase);
            list.Add(current);
        }

        return list;
    }

    public async Task<(bool success, string message)> AddWorktreeAsync(string repoPath, string worktreePath, string branch, bool createBranch, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return (false, "Invalid repository path.");
        if (string.IsNullOrWhiteSpace(worktreePath)) return (false, "Worktree path cannot be empty.");
        if (string.IsNullOrWhiteSpace(branch)) return (false, "Branch name cannot be empty.");

        var fullWorktreePath = Path.GetFullPath(worktreePath);
        string[] args = createBranch
            ? ["worktree", "add", "-b", branch, fullWorktreePath]
            : ["worktree", "add", fullWorktreePath, branch];

        var (rc, output) = await RunGitAsync(repoPath, args, ct, timeoutSeconds: 30).ConfigureAwait(false);
        if (rc == 0)
        {
            return (true, $"Worktree created at {fullWorktreePath} on branch '{branch}'");
        }
        return (false, $"Failed to add worktree: {output}");
    }

    public async Task<(bool success, string message)> RemoveWorktreeAsync(string repoPath, string worktreePath, bool force, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return (false, "Invalid repository path.");
        if (string.IsNullOrWhiteSpace(worktreePath)) return (false, "Worktree path cannot be empty.");

        var fullPath = Path.GetFullPath(worktreePath);
        if (string.Equals(fullPath, Path.GetFullPath(repoPath), StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Cannot remove the main working tree.");
        }

        string[] args = force
            ? ["worktree", "remove", "--force", fullPath]
            : ["worktree", "remove", fullPath];

        var (rc, output) = await RunGitAsync(repoPath, args, ct, timeoutSeconds: 30).ConfigureAwait(false);
        if (rc == 0)
        {
            return (true, $"Worktree removed: {Path.GetFileName(fullPath)}");
        }
        return (false, $"Failed to remove worktree: {output}");
    }

    #endregion

    #region Git Stash

    public async Task<List<GitStashItem>> GetStashesAsync(string repoPath, CancellationToken ct = default)
    {
        var list = new List<GitStashItem>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return list;

        var (rc, output) = await RunGitAsync(repoPath, ["stash", "list", "--pretty=format:%gd%x00%aI%x00%s"], ct).ConfigureAwait(false);
        if (rc != 0 || string.IsNullOrWhiteSpace(output)) return list;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int idx = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var parts = line.Split('\0');
            if (parts.Length < 3) continue;

            string refName = parts[0].Trim();
            string date = parts[1].Trim();
            string fullMsg = parts[2].Trim();
            string branch = "";
            string msg = fullMsg;

            if (fullMsg.StartsWith("WIP on ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = fullMsg[7..];
                var colon = rest.IndexOf(':');
                if (colon > 0)
                {
                    branch = rest[..colon].Trim();
                    msg = rest[(colon + 1)..].Trim();
                }
            }
            else if (fullMsg.StartsWith("On ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = fullMsg[3..];
                var colon = rest.IndexOf(':');
                if (colon > 0)
                {
                    branch = rest[..colon].Trim();
                    msg = rest[(colon + 1)..].Trim();
                }
            }

            list.Add(new GitStashItem
            {
                Index = idx++,
                RefName = refName,
                Date = date,
                Branch = branch,
                Message = msg
            });
        }

        return list;
    }

    public async Task<(bool success, string diffContent)> GetStashDiffAsync(string repoPath, int stashIndex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return (false, "Invalid repository path.");
        string stashRef = $"stash@{{{stashIndex}}}";
        var (rc, output) = await RunGitAsync(repoPath, ["stash", "show", "-p", "--no-color", stashRef], ct).ConfigureAwait(false);
        if (rc == 0) return (true, output);
        return (false, output);
    }

    public async Task<(bool success, string message)> CreateStashAsync(string repoPath, string? message, bool includeUntracked, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return (false, "Invalid repository path.");

        var args = new List<string> { "stash", "push" };
        if (!string.IsNullOrWhiteSpace(message))
        {
            args.Add("-m");
            args.Add(message.Trim());
        }
        if (includeUntracked)
        {
            args.Add("-u");
        }

        var (rc, output) = await RunGitAsync(repoPath, [.. args], ct).ConfigureAwait(false);
        if (rc == 0)
        {
            return (true, string.IsNullOrWhiteSpace(output) ? "Stash created successfully." : output.Trim());
        }
        return (false, $"Failed to create stash: {output}");
    }

    public async Task<(bool success, string message)> ApplyStashAsync(string repoPath, int stashIndex, bool pop, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return (false, "Invalid repository path.");
        string stashRef = $"stash@{{{stashIndex}}}";
        string action = pop ? "pop" : "apply";

        var (rc, output) = await RunGitAsync(repoPath, ["stash", action, stashRef], ct).ConfigureAwait(false);
        if (rc == 0)
        {
            return (true, $"Stash {stashRef} {action}ed successfully.");
        }
        return (false, $"Failed to {action} stash: {output}");
    }

    public async Task<(bool success, string message)> DropStashAsync(string repoPath, int stashIndex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return (false, "Invalid repository path.");
        string stashRef = $"stash@{{{stashIndex}}}";

        var (rc, output) = await RunGitAsync(repoPath, ["stash", "drop", stashRef], ct).ConfigureAwait(false);
        if (rc == 0)
        {
            return (true, $"Stash {stashRef} dropped.");
        }
        return (false, $"Failed to drop stash: {output}");
    }

    #endregion

    #region Multi-Repo Batch Operations

    public async Task<List<BatchRepoActionResult>> BatchActionAsync(List<string> repoIds, string action, CancellationToken ct = default)
    {
        var results = new List<BatchRepoActionResult>();
        if (repoIds == null || repoIds.Count == 0) return results;

        List<RepositoryItem> targetRepos;
        lock (_lock)
        {
            targetRepos = [.. _repositories
                .Where(r => repoIds.Contains(r.Id, StringComparer.OrdinalIgnoreCase) && Directory.Exists(r.Path))];
        }

        var tasks = targetRepos.Select(async repo =>
        {
            var res = new BatchRepoActionResult
            {
                RepoId = repo.Id,
                RepoName = repo.Name,
                Action = action
            };

            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "fetch":
                        (int fRc, string fOut) = await RunGitAsync(repo.Path, ["fetch", "--all", "--prune"], ct, timeoutSeconds: 30).ConfigureAwait(false);
                        res.Success = fRc == 0;
                        res.Output = fOut;
                        res.Message = fRc == 0 ? "Fetched remotes successfully." : $"Fetch failed: {fOut}";
                        if (res.Success)
                        {
                            var updated = await InspectRepositoryAsync(repo.Path, ct).ConfigureAwait(false);
                            if (updated != null)
                            {
                                lock (_lock)
                                {
                                    var idx = _repositories.FindIndex(r => r.Id == repo.Id);
                                    if (idx >= 0) _repositories[idx] = updated;
                                }
                            }
                        }
                        break;

                    case "pull_clean":
                        if (!repo.IsClean)
                        {
                            res.Success = false;
                            res.Message = "Skipped: repository has uncommitted changes.";
                            break;
                        }
                        if (repo.Behind == 0)
                        {
                            res.Success = true;
                            res.Message = "Already up to date.";
                            break;
                        }
                        (int pRc, string pOut) = await RunGitAsync(repo.Path, ["pull", "--ff-only"], ct, timeoutSeconds: 30).ConfigureAwait(false);
                        res.Success = pRc == 0;
                        res.Output = pOut;
                        res.Message = pRc == 0 ? "Pulled latest changes cleanly." : $"Pull failed: {pOut}";
                        if (res.Success)
                        {
                            var updated = await InspectRepositoryAsync(repo.Path, ct).ConfigureAwait(false);
                            if (updated != null)
                            {
                                lock (_lock)
                                {
                                    var idx = _repositories.FindIndex(r => r.Id == repo.Id);
                                    if (idx >= 0) _repositories[idx] = updated;
                                }
                            }
                        }
                        break;

                    case "status":
                        var stRepo = await InspectRepositoryAsync(repo.Path, ct).ConfigureAwait(false);
                        if (stRepo != null)
                        {
                            lock (_lock)
                            {
                                var idx = _repositories.FindIndex(r => r.Id == repo.Id);
                                if (idx >= 0) _repositories[idx] = stRepo;
                            }
                            res.Success = true;
                            res.Message = $"Status updated: {stRepo.Branch} (Staged: {stRepo.StagedCount}, Unstaged: {stRepo.UnstagedCount})";
                        }
                        break;

                    default:
                        res.Success = false;
                        res.Message = $"Unsupported batch action: {action}";
                        break;
                }
            }
            catch (Exception ex)
            {
                res.Success = false;
                res.Message = ex.Message;
            }

            return res;
        });

        var completed = await Task.WhenAll(tasks).ConfigureAwait(false);
        SaveRegistry();
        RepositoriesChanged?.Invoke();
        return [.. completed];
    }

    #endregion

    #region Helper Execution

    internal static async Task<(int returnCode, string output)> RunGitAsync(string workingDir, string[] args, CancellationToken ct = default, int timeoutSeconds = 15)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        Process? proc = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GIT_ASKPASS"] = "echo";
            psi.Environment["SSH_ASKPASS"] = "echo";

            foreach (var a in args) psi.ArgumentList.Add(a);

            proc = new Process { StartInfo = psi };
            proc.Start();

            var outTask = proc.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var errTask = proc.StandardError.ReadToEndAsync(linkedCts.Token);

            await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            var combined = string.IsNullOrEmpty(stdout) ? stderr : stdout;
            return (proc.ExitCode, combined);
        }
        catch (OperationCanceledException)
        {
            try { if (proc != null && !proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            return (-1, "Git operation timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            try { if (proc != null && !proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            return (-1, ex.Message);
        }
        finally
        {
            proc?.Dispose();
        }
    }

    #endregion
}
