#nullable enable
using System;
using System.Collections.Generic;

namespace AgentsHub.Models;

public sealed class CustomCliConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ColorTag { get; set; } = "#38bdf8";
    public bool AutoRestartOnCrash { get; set; } = false;
    public bool IsEnabled { get; set; } = true;

    public string GetCommandLine()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return "cmd.exe";
        }

        var exe = ExecutablePath.Trim();
        var args = (Arguments ?? "").Trim();

        // If executable is a batch/cmd file or script, run under cmd.exe
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(args) ? $"cmd.exe /c \"{exe}\"" : $"cmd.exe /c \"{exe}\" {args}";
        }

        if (exe.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return $"powershell.exe -ExecutionPolicy Bypass -File \"{exe}\" {args}".Trim();
        }

        return string.IsNullOrEmpty(args) ? exe : $"{exe} {args}";
    }
}
