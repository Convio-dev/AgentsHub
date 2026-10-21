#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentsHub.Services;

public static partial class DiscordMarkdownFormatter
{
    // Strips ANSI control sequences (colors, cursor movements, erase line)
    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiRegex();

    // Horizontal divider lines in terminal (dashes, underscores, box drawing bars)
    [GeneratedRegex(@"^[\s\-_=─━┄┅┈┉]{4,}$", RegexOptions.Multiline)]
    private static partial Regex DividerRegex();

    // Box drawing framing artifacts at start/end of lines
    [GeneratedRegex(@"^[┃│┆┊|]\s?", RegexOptions.Multiline)]
    private static partial Regex BoxBorderPrefixRegex();

    [GeneratedRegex(@"\s?[┃│┆┊|]$", RegexOptions.Multiline)]
    private static partial Regex BoxBorderSuffixRegex();

    // Noise lines that appear on agent launch, prompt tips, and status bars
    private static readonly string[] NoiseKeywords =
    [
        "Verbosity now defaults to medium",
        "Verbosity is now medium by default",
        "You can change your preferred verbosity",
        "anytime in /config > Verbosity",
        "esc to dismiss",
        "esc to cancel",
        "Tip: Press shift+tab",
        "? for shortcuts",
        "o build, change, or test next.",
        "o shortcuts",
        "Generating...",
        "Thinking..."
    ];

    public static string FormatTerminalOutputForDiscord(string rawText, string? userPrompt = null)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return !string.IsNullOrWhiteSpace(userPrompt)
                ? $"**Goal:** {userPrompt}\n\n*Task finished.*"
                : "*Task finished.*";
        }

        // 1. Strip ANSI escape codes
        string text = AnsiRegex().Replace(rawText, "");

        // 2. Normalize newlines
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 3. Remove horizontal dividers and terminal box borders
        text = DividerRegex().Replace(text, "");
        text = BoxBorderPrefixRegex().Replace(text, "");
        text = BoxBorderSuffixRegex().Replace(text, "");

        // 4. Clean noise lines
        var rawLines = text.Split('\n');
        var cleanLines = new List<string>();

        foreach (var line in rawLines)
        {
            var trimmed = line.Trim();

            // Filter out cursor block and prompt indicators
            if (trimmed == "█" || trimmed == ">" || trimmed == "❯" || trimmed == "$") continue;

            // Check noise keywords
            bool isNoise = false;
            foreach (var kw in NoiseKeywords)
            {
                if (trimmed.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    isNoise = true;
                    break;
                }
            }
            if (isNoise) continue;

            // Clean leading prompt symbols on output lines
            if (trimmed.StartsWith("> ") && trimmed.Length > 2)
            {
                trimmed = trimmed[2..].Trim();
            }

            cleanLines.Add(trimmed);
        }

        // 5. Collapse excessive consecutive blank lines into at most one blank line
        var collapsedLines = new List<string>();
        bool prevEmpty = false;
        foreach (var l in cleanLines)
        {
            if (string.IsNullOrWhiteSpace(l))
            {
                if (!prevEmpty)
                {
                    collapsedLines.Add("");
                    prevEmpty = true;
                }
            }
            else
            {
                collapsedLines.Add(l);
                prevEmpty = false;
            }
        }

        string cleanedText = string.Join("\n", collapsedLines).Trim();
        if (string.IsNullOrWhiteSpace(cleanedText))
        {
            return !string.IsNullOrWhiteSpace(userPrompt)
                ? $"**Goal:** {userPrompt}\n\n*Task finished successfully.*"
                : "*Task finished successfully.*";
        }

        // 6. Detect code sections and wrap in markdown code blocks if not already enclosed
        return EncloseCodeBlocksWithLanguage(cleanedText, userPrompt);
    }

    private static string EncloseCodeBlocksWithLanguage(string text, string? userPrompt)
    {
        // If the text already has fenced markdown code blocks, preserve them and ensure language tag exists
        if (text.Contains("```"))
        {
            return FormatExistingCodeBlocks(text, userPrompt);
        }

        // Check if output looks like code or command execution
        var lines = text.Split('\n');
        var codeBlockLines = new List<string>();
        bool inCodeMode = false;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(userPrompt))
        {
            sb.AppendLine($"**Goal:** {userPrompt}\n");
        }

        foreach (var line in lines)
        {
            bool lineIsCode = IsLineLikelyCode(line);

            if (lineIsCode)
            {
                inCodeMode = true;
                codeBlockLines.Add(line);
            }
            else
            {
                if (inCodeMode)
                {
                    // Flush accumulated code block
                    if (codeBlockLines.Count > 0)
                    {
                        string lang = DetectLanguage(codeBlockLines);
                        sb.AppendLine($"```{lang}");
                        foreach (var cl in codeBlockLines) sb.AppendLine(cl);
                        sb.AppendLine("```");
                        codeBlockLines.Clear();
                    }
                    inCodeMode = false;
                }
                sb.AppendLine(line);
            }
        }

        // Flush remaining code block
        if (codeBlockLines.Count > 0)
        {
            string lang = DetectLanguage(codeBlockLines);
            sb.AppendLine($"```{lang}");
            foreach (var cl in codeBlockLines) sb.AppendLine(cl);
            sb.AppendLine("```");
        }

        return sb.ToString().Trim();
    }

    private static string FormatExistingCodeBlocks(string text, string? userPrompt)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(userPrompt) && !text.Contains(userPrompt))
        {
            sb.AppendLine($"**Goal:** {userPrompt}\n");
        }

        var lines = text.Split('\n');
        bool insideFence = false;
        var currentBlock = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```"))
            {
                if (!insideFence)
                {
                    insideFence = true;
                    string existingLang = trimmed.Length > 3 ? trimmed[3..].Trim() : "";
                    if (!string.IsNullOrWhiteSpace(existingLang))
                    {
                        sb.AppendLine($"```{existingLang}");
                    }
                    else
                    {
                        currentBlock.Clear();
                    }
                }
                else
                {
                    insideFence = false;
                    if (currentBlock.Count > 0)
                    {
                        string lang = DetectLanguage(currentBlock);
                        sb.AppendLine($"```{lang}");
                        foreach (var cbLine in currentBlock) sb.AppendLine(cbLine);
                        currentBlock.Clear();
                    }
                    sb.AppendLine("```");
                }
            }
            else if (insideFence)
            {
                currentBlock.Add(line);
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        // Close any dangling fence
        if (insideFence && currentBlock.Count > 0)
        {
            string lang = DetectLanguage(currentBlock);
            sb.AppendLine($"```{lang}");
            foreach (var cbLine in currentBlock) sb.AppendLine(cbLine);
            sb.AppendLine("```");
        }

        return sb.ToString().Trim();
    }

    private static bool IsLineLikelyCode(string line)
    {
        var t = line.Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;

        return t.StartsWith("def ") ||
               t.StartsWith("import ") ||
               t.StartsWith("from ") ||
               t.StartsWith("class ") ||
               t.StartsWith("return ") ||
               t.StartsWith("print(") ||
               t.StartsWith("using System") ||
               t.StartsWith("public ") ||
               t.StartsWith("private ") ||
               t.StartsWith("protected ") ||
               t.StartsWith("namespace ") ||
               t.StartsWith("const ") ||
               t.StartsWith("let ") ||
               t.StartsWith("var ") ||
               t.StartsWith("function ") ||
               t.StartsWith("console.log") ||
               t.StartsWith("export ") ||
               t.StartsWith("async ") ||
               t.StartsWith("await ") ||
               t.StartsWith("if (") ||
               t.StartsWith("for (") ||
               t.StartsWith("while (") ||
               t.StartsWith("diff --git") ||
               t.StartsWith("index ") ||
               t.StartsWith("@@ ") ||
               t.StartsWith("python ") ||
               t.StartsWith("npm ") ||
               t.StartsWith("git ") ||
               t.StartsWith("dotnet ") ||
               t.StartsWith("$ ") ||
               t.StartsWith("{") ||
               t.EndsWith("};") ||
               t.EndsWith("{") ||
               (t.Contains(':') && (t.StartsWith("\"") || t.StartsWith('\'')));
    }

    public static string DetectLanguage(IEnumerable<string> lines)
    {
        string joined = string.Join("\n", lines);

        if (joined.Contains("diff --git") || joined.Contains("@@ -")) return "diff";
        if (joined.Contains("def ") || joined.Contains("print(") || joined.Contains("elif ") || joined.Contains("__main__")) return "python";
        if (joined.Contains("using System") || joined.Contains("namespace ") || joined.Contains("public class") || joined.Contains("Console.WriteLine")) return "csharp";
        if (joined.Contains("console.log") || joined.Contains("const ") || joined.Contains("function(") || joined.Contains("import React")) return "javascript";
        if (joined.TrimStart().StartsWith('{') && joined.TrimEnd().EndsWith('}')) return "json";
        if (joined.Contains("SELECT ") || joined.Contains("INSERT INTO") || joined.Contains("CREATE TABLE")) return "sql";
        if (joined.Contains("<div") || joined.Contains("<!DOCTYPE html>") || joined.Contains("<html")) return "html";
        if (joined.Contains("$ ") || joined.Contains("python ") || joined.Contains("npm ") || joined.Contains("git ")) return "bash";

        return "text";
    }
}
