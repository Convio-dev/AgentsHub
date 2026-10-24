#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentsHub.Services;

public class CliActionPrompt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Action Confirmation Required";
    public string Description { get; set; } = "The agent is requesting approval to execute a command or tool.";
    public string? Command { get; set; }
    public string RawPrompt { get; set; } = "";
}

public sealed partial class CliStreamParser
{
    // High-performance regex to strip ANSI VT100 / CSI escape sequences
    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"^\s*[>❯]\s*")]
    private static partial Regex PromptSymbolRegex();

    [GeneratedRegex(@"<thought>([\s\S]*?)</thought>", RegexOptions.IgnoreCase)]
    private static partial Regex ThoughtTagRegex();

    public static string StripAnsi(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var stripped = AnsiRegex().Replace(input, string.Empty);
        // Normalize line endings
        return stripped.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    public static (string thought, string content) ExtractThoughts(string text)
    {
        if (string.IsNullOrEmpty(text)) return (string.Empty, string.Empty);

        var match = ThoughtTagRegex().Match(text);
        if (match.Success)
        {
            var thought = match.Groups[1].Value.Trim();
            var content = ThoughtTagRegex().Replace(text, string.Empty).Trim();
            return (thought, content);
        }

        return (string.Empty, text);
    }

    public static CliActionPrompt? DetectActionPrompt(string cleanText)
    {
        if (string.IsNullOrWhiteSpace(cleanText)) return null;

        var lines = cleanText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return null;

        string lastLine = lines[^1];
        string recentTail = string.Join("\n", lines.TakeLast(6));

        bool isAction = lastLine.Contains("(y/n)", StringComparison.OrdinalIgnoreCase) ||
                        lastLine.Contains("[y/N]", StringComparison.OrdinalIgnoreCase) ||
                        lastLine.Contains("[Y/n]", StringComparison.OrdinalIgnoreCase) ||
                        lastLine.Contains("Allow this command", StringComparison.OrdinalIgnoreCase) ||
                        lastLine.Contains("Do you want to run", StringComparison.OrdinalIgnoreCase) ||
                        lastLine.Contains("Allow / Deny", StringComparison.OrdinalIgnoreCase) ||
                        lastLine.Contains("Approve execution?", StringComparison.OrdinalIgnoreCase) ||
                        recentTail.Contains("Action required", StringComparison.OrdinalIgnoreCase) ||
                        recentTail.Contains("Press Enter to continue", StringComparison.OrdinalIgnoreCase);

        if (!isAction) return null;

        // Try to identify the command being proposed from previous lines
        string? extractedCommand = null;
        for (int i = lines.Length - 2; i >= Math.Max(0, lines.Length - 6); i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('$') || line.StartsWith('>') || line.StartsWith('#'))
            {
                extractedCommand = line.TrimStart('$', '>', '#', ' ').Trim();
                break;
            }
            if (line.StartsWith("Run:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Execute:", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf(':');
                if (idx >= 0 && idx + 1 < line.Length)
                {
                    extractedCommand = line[(idx + 1)..].Trim();
                    break;
                }
            }
        }

        return new CliActionPrompt
        {
            Title = "Permission / Approval Required",
            Description = lastLine,
            Command = extractedCommand,
            RawPrompt = lastLine
        };
    }

    public static bool IsGenerationCompleted(string cleanTail)
    {
        if (string.IsNullOrWhiteSpace(cleanTail)) return false;

        var lines = cleanTail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return false;

        string lastLine = lines[^1];

        // If actively working indicators are present, not completed
        if (cleanTail.Contains("esc to cancel", StringComparison.OrdinalIgnoreCase) ||
            cleanTail.Contains("Generating...", StringComparison.OrdinalIgnoreCase) ||
            cleanTail.Contains("Thinking...", StringComparison.OrdinalIgnoreCase) ||
            cleanTail.Contains("Running command...", StringComparison.OrdinalIgnoreCase) ||
            cleanTail.Contains("Reading file...", StringComparison.OrdinalIgnoreCase) ||
            cleanTail.Contains("Executing tool", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check if an interactive prompt or terminal prompt returned
        bool isPrompt = lastLine == ">" ||
                        lastLine == "❯" ||
                        lastLine.StartsWith("❯ ") ||
                        lastLine.StartsWith("> ") ||
                        lastLine.Contains("? for shortcuts", StringComparison.OrdinalIgnoreCase) ||
                        lastLine.Contains("Type /help", StringComparison.OrdinalIgnoreCase);

        bool hasContextOrCost = cleanTail.Contains("Cost:", StringComparison.OrdinalIgnoreCase) ||
                                cleanTail.Contains("Context left:", StringComparison.OrdinalIgnoreCase) ||
                                cleanTail.Contains("Tokens:", StringComparison.OrdinalIgnoreCase);

        return isPrompt || hasContextOrCost;
    }

    public static string CleanDisplayChunk(string chunk, string? activePrompt = null)
    {
        if (string.IsNullOrEmpty(chunk)) return string.Empty;

        var cleaned = StripAnsi(chunk);

        // Filter out prompt echoes
        if (!string.IsNullOrWhiteSpace(activePrompt) && cleaned.Trim().Equals(activePrompt.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Remove solitary prompt characters
        if (cleaned.Trim() == ">" || cleaned.Trim() == "❯" || cleaned.Trim() == "$")
        {
            return string.Empty;
        }

        return cleaned;
    }
}
