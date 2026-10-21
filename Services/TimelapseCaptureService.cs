#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;

namespace AgentsHub.Services;

public sealed class TimelapseCaptureService
{
    public static TimelapseCaptureService Instance { get; } = new();

    public async Task<string?> SaveTimelapseClipAsync(string base64Data, string? workspaceId = null, string? clipName = null)
    {
        if (string.IsNullOrWhiteSpace(base64Data)) return null;

        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = !string.IsNullOrEmpty(workspaceId)
            ? workspaces.Find(w => string.Equals(w.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
            : AiConfigService.Instance.GetActiveWorkspace();

        var workDir = ws?.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
            workDir = Environment.CurrentDirectory;

        var timelapseDir = Path.Combine(workDir, ".agents", "timelapse");
        Directory.CreateDirectory(timelapseDir);

        var name = string.IsNullOrWhiteSpace(clipName)
            ? $"proof_of_work_{DateTime.UtcNow:yyyyMMdd_HHmmss}.webm"
            : clipName.Trim();

        if (!name.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            name += ".webm";
        }

        var fullPath = Path.Combine(timelapseDir, name);

        try
        {
            // Strip data:video/webm;base64, or similar if present
            var pureBase64 = base64Data;
            var commaIdx = pureBase64.IndexOf(',');
            if (commaIdx >= 0) pureBase64 = pureBase64[(commaIdx + 1)..];

            var bytes = Convert.FromBase64String(pureBase64);
            await File.WriteAllBytesAsync(fullPath, bytes).ConfigureAwait(false);
            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
