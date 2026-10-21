#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed class PreCommitSecurityService
{
    public static PreCommitSecurityService Instance { get; } = new();

    private static readonly (string Pattern, string Type, string Severity, string Description)[] Rules =
    [
        (@"AKIA[0-9A-Z]{16}", "HardcodedSecret", "Critical", "AWS Access Key ID detected."),
        (@"sk-ant-[a-zA-Z0-9_\-]{20,}", "HardcodedSecret", "Critical", "Anthropic Claude API key detected."),
        (@"sk-[a-zA-Z0-9]{20,}", "HardcodedSecret", "Critical", "OpenAI / OpenRouter API secret key detected."),
        (@"ghp_[a-zA-Z0-9]{20,}", "HardcodedSecret", "Critical", "GitHub Personal Access Token detected."),
        (@"sbp_[a-zA-Z0-9]{20,}", "HardcodedSecret", "Critical", "Supabase Access Token detected."),
        (@"-----BEGIN\s+(RSA|EC|DSA|OPENSSH)?\s*PRIVATE KEY-----", "HardcodedSecret", "Critical", "Private encryption key block detected."),
        (@"(password|passwd|secret|api_key|apiKey)\s*[:=]\s*[""'][^""'\s]{6,}[""']", "HardcodedSecret", "Warning", "Plaintext password/secret assignment detected."),
        (@"rm\s+-rf\s+[/~]", "DangerousCommand", "Critical", "Destructive root filesystem wipe command detected.")
    ];

    public async Task<SecurityAuditResult> AuditWorkspaceAsync(string? workspaceDir = null)
    {
        var result = new SecurityAuditResult();
        var workDir = !string.IsNullOrWhiteSpace(workspaceDir) && Directory.Exists(workspaceDir)
            ? workspaceDir
            : AiConfigService.Instance.GetActiveWorkspace()?.WorkingDirectory;

        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            return result;
        }

        var filesToScan = GetChangedFilesToScan(workDir);
        result.ScannedFilesCount = filesToScan.Count;

        foreach (var relPath in filesToScan)
        {
            var fullPath = Path.Combine(workDir, relPath);
            if (!File.Exists(fullPath)) continue;

            try
            {
                var lines = await File.ReadAllLinesAsync(fullPath).ConfigureAwait(false);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    foreach (var (pattern, type, severity, desc) in Rules)
                    {
                        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var snippet = match.Value;
                            if (snippet.Length > 8)
                            {
                                snippet = snippet[..4] + "..." + snippet[^3..];
                            }

                            result.Issues.Add(new SecurityIssueItem
                            {
                                Severity = severity,
                                Type = type,
                                FilePath = relPath,
                                LineNumber = i + 1,
                                MatchSnippet = snippet,
                                Recommendation = desc
                            });
                        }
                    }
                }
            }
            catch { }
        }

        result.IssuesCount = result.Issues.Count;
        int penalty = 0;
        foreach (var iss in result.Issues)
        {
            penalty += iss.Severity == "Critical" ? 35 : 12;
        }

        result.Score = Math.Max(0, 100 - penalty);
        result.IsSafe = result.Score >= 70 && !result.Issues.Exists(i => i.Severity == "Critical");

        return result;
    }

    private static List<string> GetChangedFilesToScan(string workDir)
    {
        var files = new List<string>();
        try
        {
            var psi = new ProcessStartInfo("git", "status --porcelain -uall")
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length > 3)
                    {
                        var path = line[3..].Trim().Trim('"');
                        if (!path.StartsWith(".agents", StringComparison.OrdinalIgnoreCase) &&
                            !path.StartsWith(".git", StringComparison.OrdinalIgnoreCase))
                        {
                            files.Add(path);
                        }
                    }
                }
            }
        }
        catch { }

        // Fallback if not git repo: scan recent source files
        if (files.Count == 0)
        {
            try
            {
                foreach (var f in Directory.GetFiles(workDir, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".cs" or ".js" or ".ts" or ".py" or ".json" or ".env" or ".yaml" or ".yml" or ".html")
                    {
                        var rel = Path.GetRelativePath(workDir, f).Replace('\\', '/');
                        if (!rel.StartsWith(".git/") && !rel.StartsWith(".agents/") && !rel.StartsWith("node_modules/"))
                        {
                            files.Add(rel);
                        }
                    }
                }
            }
            catch { }
        }

        return files;
    }
}
