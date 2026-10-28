#nullable enable
using System.Diagnostics;

namespace AgentsHub.Services;

/// <summary>
/// Records every console process the host starts, so a window that flashes can be traced to the
/// exact spawn that produced it instead of being guessed at.
///
/// Enabled only when the environment variable <c>AGENTSHUB_SPAWN_LOG=1</c> is set, so it costs
/// nothing in normal use.
/// </summary>
public static class ProcessSpawnLog
{
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("AGENTSHUB_SPAWN_LOG"), "1", StringComparison.Ordinal);

    public static string LogPath { get; } =
        Path.Combine(Path.GetTempPath(), "agentshub-spawns.log");

    public static void Record(string fileName, string? arguments, ProcessStartInfo info)
    {
        if (!Enabled) return;

        try
        {
            string line = string.Join('\t',
                DateTime.Now.ToString("HH:mm:ss.fff"),
                Path.GetFileName(fileName),
                (arguments ?? string.Empty).Replace('\t', ' '),
                $"shell={info.UseShellExecute}",
                $"noWindow={info.CreateNoWindow}",
                $"style={info.WindowStyle}",
                $"cwd={info.WorkingDirectory}");

            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never break the feature it is observing.
        }
    }
}
