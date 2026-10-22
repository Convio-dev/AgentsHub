using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed partial class CodebaseGraphAnalysisService
{
    public static CodebaseGraphAnalysisService Instance { get; } = new();

    private readonly ConcurrentDictionary<string, CodebaseGraphResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".gemini", ".idea", "bin", "obj", "node_modules", "dist", "build", "out",
        ".agents", "TestResults", "coverage", ".cache"
    };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".ts", ".jsx", ".tsx", ".html", ".css", ".json", ".py", ".md"
    };

    [GeneratedRegex(@"\b(?:class|interface|enum|record|struct)\s+([A-Za-z0-9_]+)\b", RegexOptions.Compiled)]
    private static partial Regex CsharpTypeDefinitionRegex();

    [GeneratedRegex(@"(?:import\s+(?:(?:[\w*\s{},]*)\s+from\s+)?['""]([^'""]+)['""]|require\(\s*['""]([^'""]+)['""]\s*\))", RegexOptions.Compiled)]
    private static partial Regex JsImportRegex();

    [GeneratedRegex(@"<(?:script|link)[^>]+(?:src|href)=['""]([^'""]+)['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlRefRegex();

    private CodebaseGraphAnalysisService() { }

    /// <summary>
    /// Analyzes the codebase in targetPath and builds an interactive dependency graph.
    /// </summary>
    public async Task<CodebaseGraphResult> AnalyzeWorkspaceAsync(string? workspacePath, int maxFiles = 300, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            workspacePath = AppDomain.CurrentDomain.BaseDirectory;
        }

        string root = Path.GetFullPath(workspacePath);
        if (!forceRefresh && _cache.TryGetValue(root, out var cached))
        {
            return cached;
        }

        var sw = Stopwatch.StartNew();

        var collectedFiles = await Task.Run(() =>
        {
            var files = new List<FileInfo>();
            ScanDirectoryRecursive(new DirectoryInfo(root), files, maxFiles);
            return files;
        }).ConfigureAwait(false);

        var nodes = new List<CodebaseNode>();
        var fileToSymbols = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var symbolToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fi in collectedFiles)
        {
            string relPath = GetRelativePath(root, fi.FullName).Replace('\\', '/');
            string ext = fi.Extension.ToLowerInvariant();
            int lineCount = 0;
            string content = string.Empty;

            try
            {
                content = File.ReadAllText(fi.FullName);
                lineCount = content.Split('\n').Length;
            }
            catch { }

            fileContents[relPath] = content;

            var node = new CodebaseNode
            {
                Id = relPath,
                Label = fi.Name,
                Extension = ext,
                Directory = Path.GetDirectoryName(relPath)?.Replace('\\', '/') ?? string.Empty,
                SizeBytes = fi.Length,
                LineCount = lineCount
            };

            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ext == ".cs")
            {
                var matches = CsharpTypeDefinitionRegex().Matches(content);
                foreach (Match m in matches)
                {
                    if (m.Groups.Count > 1 && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                    {
                        string sym = m.Groups[1].Value;
                        symbols.Add(sym);
                        if (!symbolToFile.ContainsKey(sym))
                        {
                            symbolToFile[sym] = relPath;
                        }
                    }
                }
            }

            node.Symbols = [.. symbols];
            fileToSymbols[relPath] = symbols;
            nodes.Add(node);
        }

        var edges = new List<CodebaseEdge>();
        var edgeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddEdge(string src, string dst, string kind)
        {
            if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) return;
            string key = $"{src}->{dst}";
            if (edgeSet.Add(key))
            {
                edges.Add(new CodebaseEdge
                {
                    Source = src,
                    Target = dst,
                    Kind = kind,
                    Weight = 1
                });
            }
        }

        // Build edges based on extracted symbols and file references
        foreach (var srcNode in nodes)
        {
            string srcPath = srcNode.Id;
            string content = fileContents.TryGetValue(srcPath, out var c) ? c : string.Empty;
            if (string.IsNullOrWhiteSpace(content)) continue;

            string ext = srcNode.Extension;

            if (ext == ".cs")
            {
                foreach (var (sym, targetPath) in symbolToFile)
                {
                    if (string.Equals(srcPath, targetPath, StringComparison.OrdinalIgnoreCase)) continue;
                    // Check if content references symbol with word boundary
                    if (Regex.IsMatch(content, $@"\b{Regex.Escape(sym)}\b"))
                    {
                        AddEdge(srcPath, targetPath, "call");
                    }
                }
            }
            else if (ext is ".js" or ".ts" or ".jsx" or ".tsx")
            {
                var matches = JsImportRegex().Matches(content);
                foreach (Match m in matches)
                {
                    string refSpec = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
                    if (string.IsNullOrWhiteSpace(refSpec)) continue;

                    string baseFileName = Path.GetFileName(refSpec);
                    var candidate = nodes.FirstOrDefault(n =>
                        n.Label.StartsWith(baseFileName, StringComparison.OrdinalIgnoreCase));
                    if (candidate != null)
                    {
                        AddEdge(srcPath, candidate.Id, "import");
                    }
                }
            }
            else if (ext == ".html")
            {
                var matches = HtmlRefRegex().Matches(content);
                foreach (Match m in matches)
                {
                    string refSpec = m.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(refSpec)) continue;

                    string baseFileName = Path.GetFileName(refSpec.Split('?')[0]);
                    var candidate = nodes.FirstOrDefault(n =>
                        string.Equals(n.Label, baseFileName, StringComparison.OrdinalIgnoreCase));
                    if (candidate != null)
                    {
                        AddEdge(srcPath, candidate.Id, "reference");
                    }
                }
            }
        }

        // Calculate degrees and orphan status
        var inDegreeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var outDegreeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            outDegreeCounts[edge.Source] = outDegreeCounts.GetValueOrDefault(edge.Source) + 1;
            inDegreeCounts[edge.Target] = inDegreeCounts.GetValueOrDefault(edge.Target) + 1;
        }

        foreach (var node in nodes)
        {
            node.InDegree = inDegreeCounts.GetValueOrDefault(node.Id, 0);
            node.OutDegree = outDegreeCounts.GetValueOrDefault(node.Id, 0);
            // Orphan: no caller depends on this file and it's not a root entry point
            node.IsOrphan = node.InDegree == 0 && !IsKnownEntryFile(node.Label);
        }

        sw.Stop();

        var result = new CodebaseGraphResult
        {
            Nodes = nodes,
            Edges = edges,
            Stats = new CodebaseGraphStats
            {
                TotalFiles = nodes.Count,
                TotalConnections = edges.Count,
                OrphanCount = nodes.Count(n => n.IsOrphan),
                HighRiskFiles = nodes.Count(n => n.InDegree >= 4),
                ScanDurationMs = sw.ElapsedMilliseconds,
                WorkspaceRoot = root
            }
        };

        _cache[root] = result;
        return result;
    }

    /// <summary>
    /// Computes the exact blast radius (impacted callers) for a target file.
    /// </summary>
    public async Task<BlastRadiusReport> CalculateBlastRadiusAsync(string? workspacePath, string fileId)
    {
        var graph = await AnalyzeWorkspaceAsync(workspacePath).ConfigureAwait(false);
        var targetNode = graph.Nodes.FirstOrDefault(n =>
            string.Equals(n.Id, fileId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Label, fileId, StringComparison.OrdinalIgnoreCase));

        string targetId = targetNode?.Id ?? fileId;
        string targetLabel = targetNode?.Label ?? Path.GetFileName(fileId);

        // Find direct callers: edges where Target == targetId (Source calls targetId)
        var directCallers = graph.Edges
            .Where(e => string.Equals(e.Target, targetId, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Find secondary callers: edges where Target is in directCallers
        var secondaryCallers = graph.Edges
            .Where(e => directCallers.Contains(e.Target, StringComparer.OrdinalIgnoreCase) &&
                        !string.Equals(e.Source, targetId, StringComparison.OrdinalIgnoreCase) &&
                        !directCallers.Contains(e.Source, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int totalImpacted = directCallers.Count + secondaryCallers.Count;
        int safeFiles = Math.Max(0, graph.Nodes.Count - totalImpacted - 1);

        string riskLevel = directCallers.Count switch
        {
            0 => "Low",
            1 or 2 => "Medium",
            3 or 4 => "High",
            _ => "Critical"
        };

        var sb = new StringBuilder();
        sb.AppendLine($"[BLAST-RADIUS REFACTOR SHIELD]");
        sb.AppendLine($"Target File: {targetLabel} ({targetId})");
        sb.AppendLine($"Impact Analysis: {directCallers.Count} Direct Callers (High Risk), {secondaryCallers.Count} Secondary Callers.");
        sb.AppendLine();
        sb.AppendLine("Direct Dependent Callers:");
        if (directCallers.Count > 0)
        {
            foreach (var d in directCallers) sb.AppendLine($"- {d}");
        }
        else
        {
            sb.AppendLine("- None (File is a leaf or unreferenced)");
        }

        if (secondaryCallers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Secondary Callers:");
            foreach (var s in secondaryCallers) sb.AppendLine($"- {s}");
        }

        sb.AppendLine();
        sb.AppendLine("SAFETY DIRECTIVES FOR AGENT:");
        sb.AppendLine("1. Maintain backward compatibility for all public methods, classes, and events called by the above files.");
        sb.AppendLine("2. Do not rename, change parameter order, or delete signatures without updating or overloading.");
        sb.AppendLine("3. Verify zero compilation or runtime breaks across the above blast-radius dependents.");

        return new BlastRadiusReport
        {
            TargetFile = targetId,
            TargetLabel = targetLabel,
            DirectCallers = directCallers,
            SecondaryCallers = secondaryCallers,
            SafeFilesCount = safeFiles,
            TotalImpactedCount = totalImpacted,
            RiskLevel = riskLevel,
            PromptShield = sb.ToString()
        };
    }

    private static void ScanDirectoryRecursive(DirectoryInfo dir, List<FileInfo> collected, int maxFiles)
    {
        if (collected.Count >= maxFiles) return;
        if (ExcludedDirectories.Contains(dir.Name)) return;

        try
        {
            foreach (var file in dir.GetFiles())
            {
                if (collected.Count >= maxFiles) return;
                if (SupportedExtensions.Contains(file.Extension))
                {
                    collected.Add(file);
                }
            }

            foreach (var sub in dir.GetDirectories())
            {
                if (collected.Count >= maxFiles) return;
                if (!ExcludedDirectories.Contains(sub.Name))
                {
                    ScanDirectoryRecursive(sub, collected, maxFiles);
                }
            }
        }
        catch { }
    }

    private static string GetRelativePath(string fromPath, string toPath)
    {
        try
        {
            var fromUri = new Uri(fromPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var toUri = new Uri(toPath);
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            return Uri.UnescapeDataString(relativeUri.ToString());
        }
        catch
        {
            return toPath;
        }
    }

    private static bool IsKnownEntryFile(string fileName)
    {
        return fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("MainForm.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("dashboard.js", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);
    }
}
