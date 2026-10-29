using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsHub.Services.DeepSeek;

namespace AgentsHub.Services.Qwen;

/// <summary>
/// The tool protocol as Qwen needs it: the same actions and the same executor, with a parser that
/// understands what a rendered page gives us.
///
/// Why this file exists. Qwen's answer is read from the DOM, and a rendered markdown block loses its
/// ``` fences: the language label and the line-number gutter arrive as ordinary text, and the object
/// arrives bare. Teaching <see cref="DeepSeekTools"/> about that changed behaviour for the DeepSeek
/// engine too, which reads its answers from the API and never sees the problem. Each engine now owns
/// its own parsing, and the shared file stays exactly as it was.
///
/// Execution is deliberately shared: <see cref="Execute"/> only touches the filesystem.
/// </summary>
internal static class QwenTools
{
    /// <summary>
    /// The protocol, plus one rule that only matters here.
    ///
    /// Qwen is a chat product with its own notion of tools, and it sometimes answers "tool list_dir
    /// does not exist" instead of emitting a block. The shared guide has no reason to say otherwise,
    /// so the rule is added to this copy only.
    /// </summary>
    public static string Guide(string root, IReadOnlyList<SkillEntry> skills, IReadOnlyList<McpToolInfo>? mcpTools = null)
    {
        string guide = DeepSeekTools.Guide(root, skills, mcpTools);

        const string rule =
            "              10. These tools are provided by the client, not by you. Never say a tool \"does not exist\"\n" +
            "                  and never try to call one through your own interface: a JSON block is the only\n" +
            "                  channel. If you cannot produce one, say in words what you need instead.\n";

        // Rule 10 belongs with the other rules, before the closing instruction.
        int closing = guide.LastIndexOf("Reply with one short sentence", StringComparison.Ordinal);
        return closing < 0 ? guide : guide[..closing] + rule + "\n            " + guide[closing..];
    }

    public static bool RequiresApproval(string action) => DeepSeekTools.RequiresApproval(action);

    public static (string Label, string Kind) Describe(string action) => DeepSeekTools.Describe(action);

    public static string Preview(string action, JsonElement args) => DeepSeekTools.Preview(action, args);

    public static (string Summary, string Body) Execute(string action, JsonElement args, string root) =>
        DeepSeekTools.Execute(action, args, root);

    public static IReadOnlyList<string> Actions => DeepSeekTools.Actions;

    /// <summary>
    /// Fenced blocks first, exactly as the shared parser reads them. When there are none — which is
    /// every answer from a rendered page — fall back to bare objects carrying a known action.
    /// </summary>
    public static List<JsonElement> Extract(string answer)
    {
        var calls = DeepSeekTools.Extract(answer);
        if (calls.Count > 0) return calls;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, _, element) in ScanBareActions(answer))
        {
            if (seen.Add(element.GetRawText())) calls.Add(element);
        }

        return calls;
    }

    /// <summary>The settled answer: no action object, no language label, no line-number gutter.</summary>
    public static string Strip(string answer)
    {
        string cleaned = DeepSeekTools.Strip(answer);
        var bare = ScanBareActions(cleaned);

        if (bare.Count == 0) return cleaned.Trim();

        cleaned = RemoveSpans(cleaned, bare);
        return RemoveActionDebris(cleaned).Trim();
    }

    /// <summary>
    /// What to show while the answer is still being written.
    ///
    /// Holds back an object that is not finished yet: the transcript appends, so anything streamed
    /// once can never be taken back, and a half-written JSON block is the worst thing to leak.
    /// </summary>
    public static string Visible(string partial)
    {
        string text = DeepSeekTools.Visible(partial);

        int unclosed = IndexOfUnclosedBrace(text);
        if (unclosed >= 0)
        {
            text = text[..unclosed];
        }

        var bare = ScanBareActions(text);
        if (bare.Count > 0)
        {
            text = RemoveSpans(text, bare);
        }

        return unclosed >= 0 || bare.Count > 0 ? RemoveActionDebris(text) : text;
    }

    /// <summary>
    /// Finds action objects that are not inside a fenced block.
    ///
    /// Only objects carrying a known `action` are accepted, which keeps ordinary JSON in an answer
    /// untouched. Braces inside strings and escaped quotes are skipped.
    /// </summary>
    private static List<(int Start, int Length, JsonElement Element)> ScanBareActions(string answer)
    {
        var found = new List<(int, int, JsonElement)>();

        for (int i = 0; i < answer.Length; i++)
        {
            if (answer[i] != '{') continue;

            int depth = 0;
            bool inString = false;
            bool escape = false;
            int end = -1;

            for (int j = i; j < answer.Length; j++)
            {
                char c = answer[j];

                if (escape) { escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) { end = j; break; }
                }
            }

            if (end < 0) continue;

            string slice = answer.Substring(i, end - i + 1);
            try
            {
                using var doc = JsonDocument.Parse(slice);
                var root = doc.RootElement;

                bool isAction = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("action", out var action)
                    && action.ValueKind == JsonValueKind.String
                    && DeepSeekTools.Actions.Contains(action.GetString(), StringComparer.Ordinal);

                if (isAction)
                {
                    found.Add((i, slice.Length, root.Clone()));
                    i = end;
                }
            }
            catch (JsonException)
            {
                // Not an object we can use; keep scanning after this brace.
            }
        }

        return found;
    }

    private static string RemoveSpans(string text, List<(int Start, int Length, JsonElement Element)> spans)
    {
        var builder = new StringBuilder(text.Length);
        int cursor = 0;

        foreach (var (start, length, _) in spans)
        {
            if (start < cursor) continue;
            builder.Append(text, cursor, start - cursor);
            cursor = start + length;
        }

        builder.Append(text, cursor, text.Length - cursor);
        return builder.ToString();
    }

    /// <summary>
    /// Drops the scraps a rendered code block leaves once its object is removed: the language label
    /// ("tool", "json") and the line-number gutter ("1").
    /// </summary>
    private static string RemoveActionDebris(string text) =>
        Regex.Replace(text, @"(?m)^[ \t]*(?:tool|json|\d{1,3})[ \t]*\r?\n?", string.Empty);

    /// <summary>Position of a brace that is never closed, or -1 when every brace is balanced.</summary>
    private static int IndexOfUnclosedBrace(string text)
    {
        int depth = 0;
        int openedAt = -1;
        bool inString = false;
        bool escape = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{')
            {
                if (depth == 0) openedAt = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth <= 0) { depth = 0; openedAt = -1; }
            }
        }

        return depth > 0 ? openedAt : -1;
    }
}
