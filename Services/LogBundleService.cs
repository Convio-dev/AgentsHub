using System.Text;
using System.Text.RegularExpressions;

namespace AgentsHub.Services;

/// <summary>
/// Packages the engine logs into one file that is safe to hand to somebody else.
///
/// A log is the only thing that explains a failed turn, and it is also the thing most likely to
/// contain a session token, a cookie value, an email address or the account name embedded in a path.
/// So redaction is not a nicety here: the bundle is only useful if it can be shared, and it is only
/// shareable if it carries none of those.
///
/// Every pattern below is applied to the whole file before anything is written. A redaction that
/// misses is worse than no bundle at all, so the rules are deliberately broad: a mangled stack trace
/// costs a little debugging, a leaked token costs an account.
/// </summary>
internal static class LogBundleService
{
    /// <summary>Patterns that must never survive into the bundle, in application order.</summary>
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        // A bearer header, however it was logged.
        (new Regex(@"(?i)\b(bearer\s+)[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled), "$1<redacted>"),

        // Provider key shapes.
        (new Regex(@"\bsk-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled), "<redacted-key>"),
        (new Regex(@"\bgh[pousr]_[A-Za-z0-9]{16,}", RegexOptions.Compiled), "<redacted-token>"),
        (new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled), "<redacted-aws>"),
        (new Regex(@"\bxox[baprs]-[A-Za-z0-9\-]{10,}", RegexOptions.Compiled), "<redacted-slack>"),

        // JSON fields that hold credentials, whatever the surrounding formatting.
        (
            new Regex(
                @"(?i)""(user_?token|access_?token|refresh_?token|api_?key|authorization|cookie|password|secret)""\s*:\s*""[^""]*""",
                RegexOptions.Compiled),
            "\"$1\": \"<redacted>\""
        ),

        // Query-string and header style assignments.
        (
            new Regex(@"(?i)\b(token|api_?key|access_?token|auth)\s*=\s*[^&\s""]{8,}", RegexOptions.Compiled),
            "$1=<redacted>"
        ),

        // The account name, which appears inside every profile path.
        (new Regex(@"(?i)[A-Z]:\\Users\\[^\\\s""']+", RegexOptions.Compiled), @"%USERPROFILE%"),
        (new Regex(@"(?i)/Users/[^/\s""']+", RegexOptions.Compiled), "%USERPROFILE%"),
        (new Regex(@"(?i)/home/[^/\s""']+", RegexOptions.Compiled), "%USERPROFILE%"),

        // Anything that looks like a mailbox.
        (
            new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled),
            "<email>"
        ),
    ];

    /// <summary>Applies every rule. Public within the assembly so a test can call it directly.</summary>
    internal static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        foreach (var (pattern, replacement) in Rules)
        {
            text = pattern.Replace(text, replacement);
        }

        return text;
    }

    /// <summary>
    /// Writes one bundle and returns where it landed.
    ///
    /// Plain text rather than an archive: the recipient can read it in any editor, and there is no
    /// dependency to go missing on the machine that receives it.
    /// </summary>
    internal static (string Path, int Files, long Bytes) Export()
    {
        string temp = Path.GetTempPath();
        var logs = Directory.Exists(temp)
            ? Directory.GetFiles(temp, "agentshub-*.log").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

        var bundle = new StringBuilder();
        bundle.AppendLine("Agents Hub log bundle");
        bundle.AppendLine($"generated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        bundle.AppendLine($"machine  {Environment.OSVersion.VersionString}");
        bundle.AppendLine($"runtime  {Environment.Version}");
        bundle.AppendLine($"logs     {logs.Count}");
        bundle.AppendLine();
        bundle.AppendLine("Tokens, keys, cookies, e-mail addresses and account paths are redacted below.");
        bundle.AppendLine(new string('=', 78));

        foreach (string file in logs)
        {
            bundle.AppendLine();
            bundle.AppendLine($"### {Path.GetFileName(file)}");
            bundle.AppendLine();

            try
            {
                // Shared read: the engines may be writing to it right now.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                bundle.AppendLine(Redact(reader.ReadToEnd()));
            }
            catch (Exception ex)
            {
                bundle.AppendLine($"[could not read this file: {ex.GetType().Name}]");
            }
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string path = Path.Combine(temp, $"agentshub-logs-{stamp}.txt");

        File.WriteAllText(path, bundle.ToString(), new UTF8Encoding(false));

        long bytes = new FileInfo(path).Length;
        return (path, logs.Count, bytes);
    }
}
