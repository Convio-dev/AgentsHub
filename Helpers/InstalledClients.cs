using System.Text.Json.Serialization;
using HermesHub.Services;

namespace HermesHub.Helpers;

/// <summary>
/// A desktop AI or developer client/VPN found on this machine but not yet on the Dev Tools page.
/// </summary>
public sealed record InstalledAiClient(string Name, string Target, string Source);

/// <summary>
/// Finds installed AI desktop tools, developer editors, and network clients (e.g. Proton, Cloudflare WARP, WorkBuddy, Devin, FreeBuff).
/// Uses direct well-known candidate paths first, followed by a fast bounded BFS directory walk across user and system program folders.
/// </summary>
public static class InstalledClients
{
    private sealed record KnownClient(
        string Name,
        string[] ExeFileNames,
        string[]? ExplicitPaths = null
    );

    private static readonly KnownClient[] Known =
    [
        // VPNs & Network tools
        new("Proton VPN",
            ["ProtonVPN.Launcher.exe", "ProtonVPN.exe"],
            [
                @"C:\Program Files\Proton\VPN\ProtonVPN.Launcher.exe",
                @"C:\Program Files\Proton\VPN\ProtonVPN.exe",
                @"C:\Program Files (x86)\Proton\VPN\ProtonVPN.Launcher.exe",
                @"C:\Program Files (x86)\Proton\VPN\ProtonVPN.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Proton\VPN\ProtonVPN.Launcher.exe")
            ]),

        new("Cloudflare WARP",
            ["Cloudflare WARP.exe", "warp-cli.exe", "Cloudflare One.exe", "CloudflareWARP.exe"],
            [
                @"C:\Program Files\Cloudflare\Cloudflare WARP\Cloudflare WARP.exe",
                @"C:\Program Files (x86)\Cloudflare\Cloudflare WARP\Cloudflare WARP.exe",
                @"C:\Program Files\Cloudflare\Cloudflare One\Cloudflare One.exe",
                @"C:\Program Files (x86)\Cloudflare\Cloudflare One\Cloudflare One.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Cloudflare WARP\Cloudflare WARP.exe")
            ]),

        new("Tailscale",
            ["tailscale-ipn.exe", "Tailscale.exe"],
            [
                @"C:\Program Files\Tailscale\tailscale-ipn.exe",
                @"C:\Program Files\Tailscale\Tailscale.exe",
                @"C:\Program Files (x86)\Tailscale\tailscale-ipn.exe"
            ]),

        new("WireGuard",
            ["wireguard.exe"],
            [
                @"C:\Program Files\WireGuard\wireguard.exe",
                @"C:\Program Files (x86)\WireGuard\wireguard.exe"
            ]),

        new("Mullvad VPN",
            ["Mullvad VPN.exe"],
            [
                @"C:\Program Files\Mullvad VPN\Mullvad VPN.exe",
                @"C:\Program Files (x86)\Mullvad VPN\Mullvad VPN.exe"
            ]),

        new("ZeroTier One",
            ["ZeroTier One.exe"],
            [
                @"C:\Program Files\ZeroTier\One\ZeroTier One.exe",
                @"C:\Program Files (x86)\ZeroTier\One\ZeroTier One.exe"
            ]),

        new("OpenVPN GUI",
            ["openvpn-gui.exe"],
            [
                @"C:\Program Files\OpenVPN\bin\openvpn-gui.exe",
                @"C:\Program Files (x86)\OpenVPN\bin\openvpn-gui.exe"
            ]),

        // AI Tools & Workspace Apps requested by user
        new("FreeBuff Desktop",
            ["Freebuff.exe", "freebuff.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\@codebufffreebuff-desktop\Freebuff.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Freebuff\Freebuff.exe"),
                @"C:\Program Files\Freebuff\Freebuff.exe",
                @"C:\Program Files (x86)\Freebuff\Freebuff.exe"
            ]),

        new("WorkBuddy",
            ["WorkBuddy.exe", "workbuddy.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\WorkBuddy\WorkBuddy.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"WorkBuddy\WorkBuddy.exe"),
                @"C:\Program Files\WorkBuddy\WorkBuddy.exe",
                @"C:\Program Files (x86)\WorkBuddy\WorkBuddy.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"WorkBuddy\WorkBuddy.exe")
            ]),

        new("Devin",
            ["Devin.exe", "devin.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Devin\Devin.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Devin\Devin.exe"),
                @"C:\Program Files\Devin\Devin.exe",
                @"C:\Program Files (x86)\Devin\Devin.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Devin\Devin.exe")
            ]),

        // AI IDEs, Editors & Tools
        new("Antigravity App",
            ["Antigravity.exe"],
            [
                AiConfigService.FindAntigravityAppPath() ?? string.Empty,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\antigravity\Antigravity.exe"),
                @"C:\Program Files\antigravity\Antigravity.exe"
            ]),

        new("Antigravity IDE",
            ["Antigravity IDE.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Antigravity IDE\Antigravity IDE.exe"),
                @"C:\Program Files\Antigravity IDE\Antigravity IDE.exe"
            ]),

        new("Cursor",
            ["Cursor.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\cursor\Cursor.exe"),
                @"C:\Program Files\Cursor\Cursor.exe",
                @"C:\Program Files (x86)\Cursor\Cursor.exe"
            ]),

        new("Windsurf",
            ["Windsurf.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Windsurf\Windsurf.exe"),
                @"C:\Program Files\Windsurf\Windsurf.exe",
                @"C:\Program Files (x86)\Windsurf\Windsurf.exe"
            ]),

        new("Cline App",
            ["cline-app.exe", "Cline.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Cline\cline-app.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Cline\cline-app.exe"),
                @"C:\Program Files\Cline\cline-app.exe"
            ]),

        new("OpenCode Desktop",
            ["OpenCode.exe", "opencode-cli.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\@opencodedesktop\OpenCode.exe"),
                @"C:\Program Files\OpenCode\OpenCode.exe"
            ]),

        new("Visual Studio Code",
            ["Code.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe"),
                @"C:\Program Files\Microsoft VS Code\Code.exe",
                @"C:\Program Files (x86)\Microsoft VS Code\Code.exe"
            ]),

        new("Zed",
            ["Zed.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Zed\Zed.exe"),
                @"C:\Program Files\Zed\Zed.exe"
            ]),

        new("Trae",
            ["Trae.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Trae\Trae.exe"),
                @"C:\Program Files\Trae\Trae.exe"
            ]),

        new("PearAI",
            ["PearAI.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\PearAI\PearAI.exe"),
                @"C:\Program Files\PearAI\PearAI.exe"
            ]),

        new("Positron",
            ["Positron.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Positron\Positron.exe"),
                @"C:\Program Files\Positron\Positron.exe"
            ]),

        new("Void Editor",
            ["Void.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Void\Void.exe"),
                @"C:\Program Files\Void\Void.exe"
            ]),

        new("Continue",
            ["Continue.exe"]),

        new("Ollama",
            ["ollama app.exe", "ollama.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama\ollama app.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama\ollama.exe"),
                @"C:\Program Files\Ollama\ollama.exe"
            ]),

        new("LM Studio",
            ["LM Studio.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\LM Studio\LM Studio.exe"),
                @"C:\Program Files\LM Studio\LM Studio.exe"
            ]),

        new("Jan",
            ["Jan.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Jan\Jan.exe"),
                @"C:\Program Files\Jan\Jan.exe"
            ]),

        new("GitHub Desktop",
            ["GitHubDesktop.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"GitHubDesktop\GitHubDesktop.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\GitHub Desktop\GitHubDesktop.exe")
            ]),

        new("Aider",
            ["aider.exe"]),

        new("Shopify Magic",
            ["Magic.exe"]),

        new("Claude",
            ["Claude.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Claude\Claude.exe"),
                @"C:\Program Files\Claude\Claude.exe"
            ]),

        new("ChatGPT",
            ["ChatGPT.exe"],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ChatGPT\ChatGPT.exe"),
                @"C:\Program Files\ChatGPT\ChatGPT.exe"
            ])
    ];

    private const int MaxDepth = 3;
    private const int MaxDirsPerRoot = 800;

    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".vs", "WindowsPowerShell", "Common Files", "MSBuild",
        "Microsoft.NET", "Reference Assemblies", "dotnet", "Windows Kits", "Microsoft SDKs",
        "Windows Defender", "Windows Mail", "Windows Media Player", "Windows NT", "Windows Photo Viewer",
        "WindowsInstallationAssistant", "DIFX", "Microsoft GameInput", "Microsoft Update Health Tools"
    };

    /// <summary>Client names this scanner knows about, for reporting what was looked for.</summary>
    public static IReadOnlyList<string> KnownClientNames => [.. Known.Select(k => k.Name)];

    public static List<InstalledAiClient> FindInstalledClients()
    {
        var results = new List<InstalledAiClient>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Direct candidate paths first (fastest and most accurate)
        foreach (var known in Known)
        {
            if (known.ExplicitPaths == null) continue;
            foreach (var path in known.ExplicitPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    if (File.Exists(path))
                    {
                        var resolved = Path.GetFullPath(path);
                        if (!IsBuildArtifact(resolved) && seenTargets.Add(resolved))
                        {
                            seenNames.Add(known.Name);
                            results.Add(new InstalledAiClient(known.Name, resolved, "Default Location"));
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        // 2. Build multi-lookup map for directory traversal: exeFileName -> list of matching KnownClient
        var exeMap = new Dictionary<string, List<KnownClient>>(StringComparer.OrdinalIgnoreCase);
        foreach (var known in Known)
        {
            foreach (var exe in known.ExeFileNames)
            {
                if (!exeMap.TryGetValue(exe, out var list))
                {
                    list = [];
                    exeMap[exe] = list;
                }
                list.Add(known);
            }
        }

        // 3. Single-pass bounded BFS traversal over search roots
        foreach (var root in SearchRoots())
        {
            ScanDirectory(root, exeMap, seenNames, seenTargets, results);
        }

        return [.. results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> SearchRoots()
    {
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[]
        {
            Path.Combine(localApp, "Programs"),
            localApp,
            Path.Combine(appData, "Programs"),
            progFiles,
            progFilesX86,
            Path.Combine(localApp, "Microsoft", "WindowsApps")
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate) && seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static void ScanDirectory(
        string root,
        Dictionary<string, List<KnownClient>> exeMap,
        HashSet<string> seenNames,
        HashSet<string> seenTargets,
        List<InstalledAiClient> results)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        int visited = 0;

        while (queue.Count > 0 && visited < MaxDirsPerRoot)
        {
            var (dir, depth) = queue.Dequeue();
            visited++;

            try
            {
                var files = Directory.GetFiles(dir, "*.exe");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    if (exeMap.TryGetValue(fileName, out var matchedClients))
                    {
                        var resolved = Path.GetFullPath(file);
                        if (IsBuildArtifact(resolved)) continue;

                        foreach (var client in matchedClients)
                        {
                            if (!seenNames.Contains(client.Name) && seenTargets.Add(resolved))
                            {
                                seenNames.Add(client.Name);
                                results.Add(new InstalledAiClient(client.Name, resolved, root));
                            }
                        }
                    }
                }
            }
            catch { }

            if (depth >= MaxDepth) continue;

            try
            {
                var subDirs = Directory.GetDirectories(dir);
                foreach (var sub in subDirs)
                {
                    var subName = Path.GetFileName(sub);
                    if (SkipFolders.Contains(subName)) continue;
                    if (subName.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                        subName.StartsWith("crash", StringComparison.OrdinalIgnoreCase)) continue;

                    queue.Enqueue((sub, depth + 1));
                }
            }
            catch { }
        }
    }

    private static bool IsBuildArtifact(string path) =>
        path.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\obj\Debug\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\obj\Release\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\unins", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\uninstall", StringComparison.OrdinalIgnoreCase);
}
