using System.Text.Json.Serialization;

namespace HermesHub.Helpers;

/// <summary>
/// A desktop AI client found on this machine but not yet on the Dev Tools page.
/// </summary>
public sealed record InstalledAiClient(string Name, string Target, string Source);

/// <summary>
/// Finds installed AI desktop clients.
///
/// The Dev Tools page seeds four defaults and then leaves you to type paths for anything else.
/// Common clients installed by a browser download land under LocalAppData or Programs under a
/// folder whose name has nothing to do with the app, which is exactly the case where "Browse"
/// and typing is unreasonable. This walks those roots looking for executables whose names we
/// recognise, so the page can offer them instead.
///
/// Deliberately bounded: depth 3 and a cap on directories visited per root, because a recursive
/// walk of LOCALAPPDATA is not something to run on the UI thread.
/// </summary>
public static class InstalledClients
{
    private sealed record KnownClient(string Name, string ExeFileName);

    // Executable names only. Anything matched is reported with the path it was found at, so a
    // client installed somewhere unusual is still offered without hard-coding its folder.
    private static readonly KnownClient[] Known =
    [
        new("Cursor", "Cursor.exe"),
        new("Windsurf", "Windsurf.exe"),
        new("Cline App", "cline-app.exe"),
        new("OpenCode Desktop", "OpenCode.exe"),
        new("FreeBuff Desktop", "Freebuff.exe"),
        new("Antigravity IDE", "Antigravity IDE.exe"),
        new("Antigravity App", "Antigravity.exe"),
        new("Zed", "Zed.exe"),
        new("Positron", "Positron.exe"),
        new("Continue", "Continue.exe"),
        new("PearAI", "PearAI.exe"),
        new("Trae", "Trae.exe"),
        new("Void Editor", "Void.exe"),
        new("GitHub Desktop", "GitHubDesktop.exe"),
        new("Visual Studio Code", "Code.exe"),
        new("Shopify Magic", "Magic.exe")
    ];

    private const int MaxDepth = 3;
    private const int MaxDirectoriesPerRoot = 4000;

    /// <summary>Client names this scanner knows about, for reporting what was looked for.</summary>
    public static IReadOnlyList<string> KnownClientNames => [.. Known.Select(k => k.Name)];

    public static List<InstalledAiClient> FindInstalledClients()
    {
        var results = new List<InstalledAiClient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in SearchRoots())
        {
            foreach (var known in Known)
            {
                foreach (var found in FindFile(root, known.ExeFileName))
                {
                    var resolved = Path.GetFullPath(found);
                    if (!seen.Add(resolved)) continue;

                    // A build output copy of an app is not the app.
                    if (resolved.Contains("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase) ||
                        resolved.Contains("\\bin\\Release\\", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(new InstalledAiClient(known.Name, resolved, root));
                    break;
                }
            }
        }

        return [.. results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> SearchRoots()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Programs")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> FindFile(string root, string fileName)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        int visited = 0;

        while (queue.Count > 0 && visited < MaxDirectoriesPerRoot)
        {
            var (dir, depth) = queue.Dequeue();
            visited++;

            string[] files;
            try { files = Directory.GetFiles(dir, fileName); }
            catch { continue; }

            foreach (var file in files)
            {
                yield return file;
            }

            if (depth >= MaxDepth) continue;

            string[] directories;
            try { directories = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var child in directories)
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }
}
