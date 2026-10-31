#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed class ScheduledAutomationService : IDisposable
{
    public static ScheduledAutomationService Instance { get; } = new();

    private readonly Lock _lock = new();
    private readonly System.Threading.Timer _timer;
    private readonly string _storagePath;
    private List<ScheduledAutomationTask> _tasks = [];
    private bool _isEvaluating = false;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ScheduledAutomationService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentsHub");
        Directory.CreateDirectory(appData);
        _storagePath = Path.Combine(appData, "automations.json");

        LoadTasks();

        // Check every 30 seconds
        _timer = new System.Threading.Timer(async _ => await EvaluateDueTasksAsync().ConfigureAwait(false), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    public List<ScheduledAutomationTask> GetTasks(string? workspaceId = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(workspaceId)) return [.. _tasks];
            return [.. _tasks.Where(t => string.Equals(t.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))];
        }
    }

    public void SaveTask(ScheduledAutomationTask task)
    {
        lock (_lock)
        {
            var idx = _tasks.FindIndex(t => string.Equals(t.Id, task.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _tasks[idx] = task;
            }
            else
            {
                _tasks.Add(task);
            }
            PersistTasks();
        }
    }

    public bool DeleteTask(string taskId)
    {
        lock (_lock)
        {
            var removed = _tasks.RemoveAll(t => string.Equals(t.Id, taskId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                PersistTasks();
                return true;
            }
            return false;
        }
    }

    public async Task<bool> TriggerTaskNowAsync(string taskId)
    {
        ScheduledAutomationTask? task;
        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.OrdinalIgnoreCase));
        }
        if (task == null) return false;

        await ExecuteTaskAsync(task).ConfigureAwait(false);
        return true;
    }

    private async Task EvaluateDueTasksAsync()
    {
        if (_isEvaluating) return;
        _isEvaluating = true;

        try
        {
            List<ScheduledAutomationTask> dueTasks;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                dueTasks = [.. _tasks.Where(t => t.IsEnabled && (!t.LastRunUtc.HasValue || now - t.LastRunUtc.Value >= TimeSpan.FromMinutes(Math.Max(1, t.IntervalMinutes))))];
            }

            foreach (var task in dueTasks)
            {
                await ExecuteTaskAsync(task).ConfigureAwait(false);
            }
        }
        catch { }
        finally
        {
            _isEvaluating = false;
        }
    }

    private async Task ExecuteTaskAsync(ScheduledAutomationTask task)
    {
        var workspaces = AiConfigService.Instance.GetWorkspaces();
        var ws = workspaces.FirstOrDefault(w => string.Equals(w.Id, task.WorkspaceId, StringComparison.OrdinalIgnoreCase)) ?? AiConfigService.Instance.GetActiveWorkspace();

        var workDir = ws?.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
            workDir = Environment.CurrentDirectory;

        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{task.Command}\"")
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();

                await proc.WaitForExitAsync().ConfigureAwait(false);
                var stdout = await outputTask.ConfigureAwait(false);
                var stderr = await errorTask.ConfigureAwait(false);

                task.LastRunUtc = DateTime.UtcNow;
                task.LastExitCode = proc.ExitCode;
                task.RunCount++;

                var combined = (stdout + "\n" + stderr).Trim();
                if (combined.Length > 500) combined = combined[^497..];
                task.LastOutput = combined;

                if (proc.ExitCode == 0)
                {
                    SoundpackAudioService.Instance.PlaySoundEvent("task_done");
                }
                else
                {
                    SoundpackAudioService.Instance.PlaySoundEvent("task_failed");
                }

                lock (_lock)
                {
                    PersistTasks();
                }
            }
        }
        catch (Exception ex)
        {
            task.LastRunUtc = DateTime.UtcNow;
            task.LastExitCode = -1;
            task.LastOutput = "Execution error: " + ex.Message;
            lock (_lock)
            {
                PersistTasks();
            }
        }
    }

    private void LoadTasks()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var json = File.ReadAllText(_storagePath);
                var loaded = JsonSerializer.Deserialize<List<ScheduledAutomationTask>>(json, JsonOpts);
                if (loaded != null) _tasks = loaded;
            }
        }
        catch { }
    }

    private void PersistTasks()
    {
        try
        {
            var json = JsonSerializer.Serialize(_tasks, JsonOpts);
            File.WriteAllText(_storagePath, json);
        }
        catch { }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
