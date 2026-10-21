#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class FileChangeEventItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty; // Created, Modified, Deleted, Renamed
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss");
    public long FileSizeBytes { get; set; }
}

public sealed class FileWatchdogService : IDisposable
{
    public static FileWatchdogService Instance { get; } = new();

    private FileSystemWatcher? _watcher;
    private string? _currentWatchedDir;
    private readonly ConcurrentQueue<FileChangeEventItem> _recentChanges = new();
    private readonly ConcurrentDictionary<string, DateTime> _debounceMap = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxEvents = 100;

    public event Action<FileChangeEventItem>? FileChanged;

    public void WatchDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            Stop();
            return;
        }

        if (string.Equals(_currentWatchedDir, directory, StringComparison.OrdinalIgnoreCase) && _watcher != null)
        {
            return;
        }

        Stop();
        _currentWatchedDir = directory;

        try
        {
            _watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };

            _watcher.Created += (s, e) => OnFileEvent(e.FullPath, "Created");
            _watcher.Changed += (s, e) => OnFileEvent(e.FullPath, "Modified");
            _watcher.Deleted += (s, e) => OnFileEvent(e.FullPath, "Deleted");
            _watcher.Renamed += (s, e) => OnFileEvent(e.FullPath, "Renamed");

            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _currentWatchedDir = null;
    }

    public List<FileChangeEventItem> GetRecentChanges()
    {
        return [.. _recentChanges.Reverse()];
    }

    public void ClearRecentChanges()
    {
        _recentChanges.Clear();
    }

    public async Task<string> GetFileDiffAsync(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_currentWatchedDir)) return "No active workspace monitored.";
        if (string.IsNullOrWhiteSpace(relativePath)) return "Path is required.";

        var cleanRelPath = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(cleanRelPath) && cleanRelPath.StartsWith(_currentWatchedDir, StringComparison.OrdinalIgnoreCase))
        {
            cleanRelPath = cleanRelPath[_currentWatchedDir.Length..].TrimStart(Path.DirectorySeparatorChar);
        }

        var fullPath = Path.IsPathRooted(cleanRelPath) ? cleanRelPath : Path.Combine(_currentWatchedDir, cleanRelPath);
        var gitRelPath = cleanRelPath.Replace('\\', '/');
        var gitDir = Path.Combine(_currentWatchedDir, ".git");

        if (Directory.Exists(gitDir))
        {
            try
            {
                // 1. Run git diff against HEAD to inspect all staged and unstaged changes
                var psi = new ProcessStartInfo("git", $"diff HEAD -U3 -- \"{gitRelPath}\"")
                {
                    WorkingDirectory = _currentWatchedDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var diff = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    await proc.WaitForExitAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(diff)) return diff.Trim();
                }

                // 2. If git diff HEAD is empty, inspect porcelain status
                var statusPsi = new ProcessStartInfo("git", $"status --porcelain -- \"{gitRelPath}\"")
                {
                    WorkingDirectory = _currentWatchedDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var sProc = Process.Start(statusPsi);
                if (sProc != null)
                {
                    var status = (await sProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false)).Trim();
                    await sProc.WaitForExitAsync().ConfigureAwait(false);

                    if (status.StartsWith("??") && File.Exists(fullPath))
                    {
                        var content = await File.ReadAllLinesAsync(fullPath).ConfigureAwait(false);
                        var sb = new StringBuilder();
                        sb.AppendLine("--- /dev/null (Untracked new file)");
                        sb.AppendLine($"+++ b/{gitRelPath}");
                        foreach (var line in content) sb.AppendLine($"+{line}");
                        return sb.ToString().TrimEnd();
                    }

                    if (status.StartsWith("D ") || status.StartsWith(" D") || !File.Exists(fullPath))
                    {
                        return $"--- a/{gitRelPath}\n+++ /dev/null\n(File deleted from workspace)";
                    }

                    if (string.IsNullOrEmpty(status))
                    {
                        return "No uncommitted modifications detected for this file (matches HEAD).";
                    }
                }
            }
            catch { }
        }

        if (File.Exists(fullPath))
        {
            var lines = await File.ReadAllLinesAsync(fullPath).ConfigureAwait(false);
            var sb = new StringBuilder();
            sb.AppendLine($"+++ {cleanRelPath} ({lines.Length} lines)");
            foreach (var l in lines) sb.AppendLine($" {l}");
            return sb.ToString();
        }

        return "File diff unavailable or file removed.";
    }

    public async Task<bool> RevertFileAsync(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_currentWatchedDir) || string.IsNullOrWhiteSpace(relativePath)) return false;

        var cleanRelPath = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(cleanRelPath) && cleanRelPath.StartsWith(_currentWatchedDir, StringComparison.OrdinalIgnoreCase))
        {
            cleanRelPath = cleanRelPath[_currentWatchedDir.Length..].TrimStart(Path.DirectorySeparatorChar);
        }

        var fullPath = Path.IsPathRooted(cleanRelPath) ? cleanRelPath : Path.Combine(_currentWatchedDir, cleanRelPath);
        var gitRelPath = cleanRelPath.Replace('\\', '/');
        var gitDir = Path.Combine(_currentWatchedDir, ".git");

        if (Directory.Exists(gitDir))
        {
            try
            {
                var statusPsi = new ProcessStartInfo("git", $"status --porcelain -- \"{gitRelPath}\"")
                {
                    WorkingDirectory = _currentWatchedDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var sProc = Process.Start(statusPsi);
                if (sProc != null)
                {
                    var status = (await sProc.StandardOutput.ReadToEndAsync().ConfigureAwait(false)).Trim();
                    await sProc.WaitForExitAsync().ConfigureAwait(false);

                    if (status.StartsWith("??") && File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        return true;
                    }
                }

                var psi = new ProcessStartInfo("git", $"checkout HEAD -- \"{gitRelPath}\"")
                {
                    WorkingDirectory = _currentWatchedDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync().ConfigureAwait(false);
                    if (proc.ExitCode == 0) return true;
                }
            }
            catch { }
        }

        return false;
    }

    private void OnFileEvent(string fullPath, string changeType)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;

        // Filter out noisy paths
        var normalized = fullPath.Replace('\\', '/');
        if (normalized.Contains("/.git/") ||
            normalized.Contains("/node_modules/") ||
            normalized.Contains("/bin/") ||
            normalized.Contains("/obj/") ||
            normalized.Contains("/dist/") ||
            normalized.Contains("/.vs/") ||
            normalized.Contains("/.idea/") ||
            normalized.Contains("/WebView2Data/") ||
            normalized.EndsWith(".tmp") ||
            normalized.EndsWith(".log"))
        {
            return;
        }

        // Debounce: ignore multiple rapid events for same file within 400ms
        var now = DateTime.UtcNow;
        if (_debounceMap.TryGetValue(fullPath, out var lastTime) && (now - lastTime).TotalMilliseconds < 400)
        {
            return;
        }
        _debounceMap[fullPath] = now;

        string relPath = fullPath;
        if (!string.IsNullOrEmpty(_currentWatchedDir) && fullPath.StartsWith(_currentWatchedDir, StringComparison.OrdinalIgnoreCase))
        {
            relPath = fullPath[_currentWatchedDir.Length..].TrimStart('\\', '/');
        }

        long size = 0;
        try
        {
            if (File.Exists(fullPath)) size = new FileInfo(fullPath).Length;
        }
        catch { }

        var item = new FileChangeEventItem
        {
            Path = fullPath,
            RelativePath = relPath,
            ChangeType = changeType,
            FileSizeBytes = size,
            Timestamp = DateTime.Now.ToString("HH:mm:ss")
        };

        _recentChanges.Enqueue(item);
        while (_recentChanges.Count > MaxEvents) _recentChanges.TryDequeue(out _);

        FileChanged?.Invoke(item);
    }

    public void Dispose()
    {
        Stop();
    }
}
