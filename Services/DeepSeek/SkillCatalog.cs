namespace AgentsHub.Services.DeepSeek;

/// <summary>One skill the project ships, as the model sees it.</summary>
internal sealed record SkillEntry(string Name, string Description, string Path);

/// <summary>
/// Finds the project's skills so the DeepSeek engine can offer them.
///
/// This is progressive disclosure, the same idea the CLI engines use: the guide lists names and
/// descriptions only, and the body arrives through `read_skill` when the model asks for it. Sending
/// every SKILL.md up front would spend the whole context on skills the turn does not need.
/// </summary>
internal static class SkillCatalog
{
    private static readonly string[] Roots = [".agents/skills", ".claude/skills", ".deepseek/skills"];

    public static IReadOnlyList<SkillEntry> Discover(string workspaceRoot)
    {
        var found = new List<SkillEntry>();

        foreach (var relative in Roots)
        {
            string dir = Path.Combine(workspaceRoot, relative);
            if (!Directory.Exists(dir)) continue;

            foreach (var skillDir in Directory.EnumerateDirectories(dir))
            {
                string manifest = Path.Combine(skillDir, "SKILL.md");
                if (!File.Exists(manifest)) continue;

                var entry = Read(manifest);
                if (entry is null) continue;
                if (found.Any(s => string.Equals(s.Name, entry.Name, StringComparison.OrdinalIgnoreCase))) continue;

                found.Add(entry);
            }
        }

        return found.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads the front matter for a name and description, falling back to the first heading.</summary>
    private static SkillEntry? Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }

        string name = Path.GetFileName(Path.GetDirectoryName(path)!) ?? "skill";
        string description = string.Empty;

        var lines = text.Split('\n');
        int index = 0;

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            for (index = 1; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line == "---")
                {
                    index++;
                    break;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim().Trim('"', '\'');

                if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && value.Length > 0) name = value;
                if (key.Equals("description", StringComparison.OrdinalIgnoreCase) && value.Length > 0) description = value;
            }
        }

        if (description.Length == 0)
        {
            // No front matter: the first non-empty prose line usually says what it is for.
            var prose = lines.Skip(index)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));

            description = prose is null
                ? string.Empty
                : prose.Length > 160 ? prose[..160] : prose;
        }

        return new SkillEntry(name, description, path);
    }
}
