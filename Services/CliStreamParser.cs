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

    public static bool IsTerminalChromeOrNoise(string line, string? activePrompt = null)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.Trim();

        // 1. Filter out exact prompt echoes e.g. "> hello" or "hello"
        if (!string.IsNullOrWhiteSpace(activePrompt))
        {
            var p = activePrompt.Trim();
            if (trimmed.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals($"> {p}", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals($"❯ {p}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Terminal mode artifacts e.g. "h0"
        if (trimmed.Equals("h0", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("0h", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Horizontal dividers / box-drawing characters
        // e.g. "____________________", "────────────────────", "┌────────┐"
        if (trimmed.Length >= 2 && trimmed.All(c => c == '─' || c == '-' || c == '_' || c == '=' || c == '┌' || c == '┐' || c == '└' || c == '┘' || c == '│' || c == '╭' || c == '╮' || c == '╰' || c == '╯' || c == ' ' || c == '·' || c == '█'))
        {
            return true;
        }

        // 4. Status lines & cancel hints
        if (trimmed.Contains("esc to cancel", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("esc to dismiss", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("? for shortcuts", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Tip: Press", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Verbosity is now", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Generating...", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Running command...", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Reading file...", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 5. Model status banners embedded with box borders e.g. "Gemini 3.8 Flash · high"
        if ((trimmed.Contains("Gemini ", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Claude ", StringComparison.OrdinalIgnoreCase)) &&
            (trimmed.Contains("Flash", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Pro", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Sonnet", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("high", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("·", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 6. Lone prompt symbols
        if (trimmed == ">" || trimmed == "❯" || trimmed == "$" || trimmed == "█")
        {
            return true;
        }

        return false;
    }

    public static string CleanDisplayChunk(string chunk, string? activePrompt = null)
    {
        if (string.IsNullOrEmpty(chunk)) return string.Empty;

        var stripped = StripAnsi(chunk);
        if (string.IsNullOrWhiteSpace(stripped)) return string.Empty;

        var lines = stripped.Split('\n');
        var keptLines = new List<string>(lines.Length);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (IsTerminalChromeOrNoise(line, activePrompt))
            {
                continue;
            }
            keptLines.Add(line);
        }

        if (keptLines.Count == 0) return string.Empty;

        return string.Join("\n", keptLines);
    }
}
