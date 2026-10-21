#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AgentsHub.Services;

public sealed class SessionSnapshotData
{
    public string ActiveWorkspaceId { get; set; } = string.Empty;
    public string ActiveView { get; set; } = "dashboard";
    public string ActiveCliTab { get; set; } = "hermes";
    public DateTime LastSavedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> TerminalBuffers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SessionStateService
{
    public static SessionStateService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string SnapshotFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentsHub",
        "session_snapshot.json"
    );

    private readonly Lock _lock = new();

    public SessionSnapshotData? LoadSnapshot()
    {
        lock (_lock)
        {
            if (!File.Exists(SnapshotFilePath)) return null;
            try
            {
                var json = File.ReadAllText(SnapshotFilePath);
                return JsonSerializer.Deserialize<SessionSnapshotData>(json, JsonOpts);
            }
            catch
            {
                return null;
            }
        }
    }

    public void SaveSnapshot(string activeWorkspaceId, string activeView, string activeCliTab, IDictionary<string, string>? buffers = null)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(SnapshotFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var snapshot = new SessionSnapshotData
                {
                    ActiveWorkspaceId = activeWorkspaceId ?? "",
                    ActiveView = activeView ?? "dashboard",
                    ActiveCliTab = activeCliTab ?? "hermes",
                    LastSavedUtc = DateTime.UtcNow,
                    TerminalBuffers = buffers != null ? new Dictionary<string, string>(buffers, StringComparer.OrdinalIgnoreCase) : []
                };

                var json = JsonSerializer.Serialize(snapshot, JsonOpts);
                File.WriteAllText(SnapshotFilePath, json);
            }
            catch { }
        }
    }
}
