#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed class TranscriptInspectorService
{
    public static TranscriptInspectorService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<AgentSessionSummary>> GetSessionsAsync(string? workspaceDir = null)
    {
        return await Task.Run<List<AgentSessionSummary>>(() =>
        {
            var list = new List<AgentSessionSummary>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var searchDirs = new List<string>();

            // 1. AppData brain directory
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var geminiAntigravity = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "brain");
            if (Directory.Exists(geminiAntigravity)) searchDirs.Add(geminiAntigravity);

            // 2. Workspace directory
            if (!string.IsNullOrWhiteSpace(workspaceDir) && Directory.Exists(workspaceDir))
            {
                var wsLogs = Path.Combine(workspaceDir, ".system_generated", "logs");
                if (Directory.Exists(wsLogs)) searchDirs.Add(wsLogs);
            }

            // Search brain folders
            foreach (var baseDir in searchDirs)
            {
                try
                {
                    if (baseDir.EndsWith("logs", StringComparison.OrdinalIgnoreCase))
                    {
                        var file = Path.Combine(baseDir, "transcript.jsonl");
                        if (File.Exists(file)) AddSessionFromFile(file, list, seenPaths);
                    }
                    else
                    {
                        foreach (var sessionDir in Directory.GetDirectories(baseDir))
                        {
                            var transcriptPath = Path.Combine(sessionDir, ".system_generated", "logs", "transcript.jsonl");
                            if (File.Exists(transcriptPath))
                            {
                                AddSessionFromFile(transcriptPath, list, seenPaths);
                            }
                            else
                            {
                                // direct logs folder
                                var directPath = Path.Combine(sessionDir, "transcript.jsonl");
                                if (File.Exists(directPath)) AddSessionFromFile(directPath, list, seenPaths);
                            }
                        }
                    }
                }
                catch { }
            }

            return [.. list.OrderByDescending(s => s.LastActiveUtc)];
        }).ConfigureAwait(false);
    }

    private static void AddSessionFromFile(string transcriptPath, List<AgentSessionSummary> list, HashSet<string> seenPaths)
    {
        if (!seenPaths.Add(transcriptPath)) return;

        try
        {
            var fileInfo = new FileInfo(transcriptPath);
            if (fileInfo.Length == 0) return;

            string goal = "Agent Session";
            int stepCount = 0;
            int toolCallCount = 0;
            bool hasErrors = false;
            DateTime startTime = fileInfo.CreationTimeUtc;

            // Read first few lines for goal and quick summary
            using (var reader = new StreamReader(transcriptPath))
            {
                string? line;
                int readLines = 0;
                while ((line = reader.ReadLine()) != null && readLines < 300)
                {
                    readLines++;
                    stepCount++;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (goal == "Agent Session" && root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "USER_INPUT")
                        {
                            if (root.TryGetProperty("content", out var cEl))
                            {
                                var txt = cEl.GetString();
                                if (!string.IsNullOrWhiteSpace(txt))
                                {
                                    goal = txt.Trim().Replace("\r", " ").Replace("\n", " ");
                                    if (goal.Length > 120) goal = goal[..117] + "...";
                                }
                            }
                        }

                        if (root.TryGetProperty("tool_calls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array)
                        {
                            toolCallCount += tcEl.GetArrayLength();
                        }

                        if (root.TryGetProperty("status", out var sEl) && sEl.GetString() == "ERROR")
                        {
                            hasErrors = true;
                        }
                    }
                    catch { }
                }
            }

            var sessionId = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(transcriptPath)))) ?? Guid.NewGuid().ToString("N");
            if (sessionId.StartsWith(".")) sessionId = Path.GetFileName(Path.GetDirectoryName(transcriptPath)) ?? "session";

            list.Add(new AgentSessionSummary
            {
                SessionId = sessionId,
                TranscriptPath = transcriptPath,
                Goal = goal,
                StepCount = stepCount,
                ToolCallCount = toolCallCount,
                HasErrors = hasErrors,
                StartTimeUtc = startTime,
                LastActiveUtc = fileInfo.LastWriteTimeUtc,
                Engine = "Antigravity"
            });
        }
        catch { }
    }

    public async Task<List<TranscriptStepItem>> GetSessionStepsAsync(string transcriptPath)
    {
        var steps = new List<TranscriptStepItem>();
        if (!File.Exists(transcriptPath)) return steps;

        try
        {
            var lines = await File.ReadAllLinesAsync(transcriptPath).ConfigureAwait(false);
            int index = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                index++;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var type = root.TryGetProperty("type", out var tEl) ? tEl.GetString() ?? "STEP" : "STEP";
                    var status = root.TryGetProperty("status", out var sEl) ? sEl.GetString() ?? "DONE" : "DONE";
                    var content = root.TryGetProperty("content", out var cEl) ? cEl.GetString() ?? "" : "";
                    var thinking = root.TryGetProperty("thinking", out var thEl) ? thEl.GetString() : null;

                    var time = DateTime.UtcNow;
                    if (root.TryGetProperty("created_at", out var timeEl) && timeEl.TryGetDateTime(out var dt))
                    {
                        time = dt.ToUniversalTime();
                    }

                    var toolCalls = new List<TranscriptToolCallItem>();
                    if (root.TryGetProperty("tool_calls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcEl.EnumerateArray())
                        {
                            var toolName = "tool";
                            var argsJson = "";
                            var toolStatus = "DONE";
                            string? output = null;

                            if (tc.TryGetProperty("function", out var fEl))
                            {
                                if (fEl.TryGetProperty("name", out var nEl)) toolName = nEl.GetString() ?? toolName;
                                if (fEl.TryGetProperty("arguments", out var aEl)) argsJson = aEl.ToString();
                            }
                            else if (tc.TryGetProperty("toolName", out var tnEl))
                            {
                                toolName = tnEl.GetString() ?? toolName;
                            }

                            if (tc.TryGetProperty("output", out var oEl)) output = oEl.GetString();
                            if (tc.TryGetProperty("status", out var tsEl)) toolStatus = tsEl.GetString() ?? "DONE";

                            toolCalls.Add(new TranscriptToolCallItem
                            {
                                ToolName = toolName,
                                ArgumentsJson = argsJson,
                                Output = output,
                                Status = toolStatus
                            });
                        }
                    }

                    steps.Add(new TranscriptStepItem
                    {
                        StepIndex = index,
                        Type = type,
                        Status = status,
                        Content = content,
                        Thinking = thinking,
                        Timestamp = time,
                        ToolCalls = toolCalls
                    });
                }
                catch { }
            }
        }
        catch { }

        return steps;
    }

    public bool DeleteSession(string transcriptPath)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath)) return false;

        try
        {
            if (File.Exists(transcriptPath))
            {
                var logsDir = Path.GetDirectoryName(transcriptPath);
                var systemGenDir = logsDir != null ? Path.GetDirectoryName(logsDir) : null;
                var sessionDir = systemGenDir != null ? Path.GetDirectoryName(systemGenDir) : null;

                // If located under an Antigravity "brain" folder, safely remove the entire session folder
                if (sessionDir != null && string.Equals(Path.GetFileName(Path.GetDirectoryName(sessionDir)), "brain", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(sessionDir, true);
                    return true;
                }

                // If in a workspace, delete transcript and companion transcript_full files
                File.Delete(transcriptPath);
                if (logsDir != null)
                {
                    var fullFile = Path.Combine(logsDir, "transcript_full.jsonl");
                    if (File.Exists(fullFile)) File.Delete(fullFile);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete session: {ex.Message}");
        }

        return false;
    }
}
