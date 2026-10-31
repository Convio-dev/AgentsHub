#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentsHub.Helpers;
using AgentsHub.Models;
#if WINDOWS
using System.Windows.Forms;
using AgentsHub.Forms;
using Microsoft.Web.WebView2.Core;
#endif

namespace AgentsHub.Services;

public sealed partial class DashboardWebBridge : IDisposable
{

    private async Task HandleGetMcpAndSkillsAsync(string? requestId)
    {
        var servers = await McpSkillsService.Instance.GetMcpServersAsync().ConfigureAwait(false);
        var activeWs = AiConfigService.Instance.GetActiveWorkspace();
        var skills = await McpSkillsService.Instance.GetAgentSkillsAsync(activeWs?.WorkingDirectory).ConfigureAwait(false);
        var folders = new
        {
            mcp = await McpSkillsService.Instance.GetSourcesAsync("mcp").ConfigureAwait(false),
            skills = await McpSkillsService.Instance.GetSourcesAsync("skills").ConfigureAwait(false),
        };
        SendResponse(requestId, true, new { servers, skills, folders });
    }

    private async Task HandleAddMcpFolderAsync(JsonElement data, string? requestId)
    {
        // A path may be passed straight in; otherwise the native picker runs so the user
        // never has to type a Windows path.
        string kind = data.TryGetProperty("kind", out var akProp) ? akProp.GetString() ?? "mcp" : "mcp";
        string? folder = data.TryGetProperty("path", out var apProp) ? apProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(folder))
        {
#if WINDOWS
            if (_mainForm is Form f && f.IsHandleCreated && !f.IsDisposed)
            {
                f.Invoke(() =>
                {
                    using var fbd = new FolderBrowserDialog
                    {
                        Description = kind == "skills" ? "Select a skills folder" : "Select an MCP folder"
                    };
                    if (fbd.ShowDialog(f) == DialogResult.OK) folder = fbd.SelectedPath;
                });
            }
#endif
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            SendResponse(requestId, false, "Selection cancelled");
            return;
        }

        var list = await McpSkillsService.Instance.AddSourceAsync(kind, folder).ConfigureAwait(false);
        SendResponse(requestId, true, new { folders = list });
    }

    private async Task HandleRemoveMcpFolderAsync(JsonElement data, string? requestId)
    {
        string kind = data.TryGetProperty("kind", out var rkProp) ? rkProp.GetString() ?? "mcp" : "mcp";
        string? folder = data.TryGetProperty("path", out var rpProp) ? rpProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(folder))
        {
            SendResponse(requestId, false, "Folder path is required");
            return;
        }

        var list = await McpSkillsService.Instance.RemoveSourceAsync(kind, folder).ConfigureAwait(false);
        SendResponse(requestId, true, new { folders = list });
    }

    private async Task HandleSaveMcpServerAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("server", out var sProp))
        {
            var s = JsonSerializer.Deserialize<McpServerItem>(sProp.GetRawText(), CaseInsensitiveJsonOptions);
            if (s != null)
            {
                bool saved = await McpSkillsService.Instance.SaveMcpServerAsync(s).ConfigureAwait(false);
                SendResponse(requestId, true, new { success = saved });
                return;
            }
        }
        SendResponse(requestId, false, "Invalid MCP server payload");
    }

    private async Task HandleDeleteMcpServerAsync(JsonElement data, string? requestId)
    {
        string? id = data.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        if (!string.IsNullOrEmpty(id))
        {
            bool deleted = await McpSkillsService.Instance.DeleteMcpServerAsync(id).ConfigureAwait(false);
            SendResponse(requestId, true, new { success = deleted });
        }
        else
        {
            SendResponse(requestId, false, "Server ID is required");
        }
    }

    private async Task HandleToggleSkillAsync(JsonElement data, string? requestId)
    {
        string? dir = data.TryGetProperty("directoryPath", out var dProp) ? dProp.GetString() : null;
        bool enabled = data.TryGetProperty("enabled", out var eProp) && eProp.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(dir))
        {
            SendResponse(requestId, false, "Skill directory is required");
            return;
        }

        bool ok = McpSkillsService.SetSkillEnabled(dir, enabled);
        var activeWs = AiConfigService.Instance.GetActiveWorkspace();
        var skills = ok
            ? await McpSkillsService.Instance.GetAgentSkillsAsync(activeWs?.WorkingDirectory).ConfigureAwait(false)
            : null;

        SendResponse(requestId, ok, new { success = ok, skills });
    }

    private async Task HandleSaveMcpAuthAsync(JsonElement data, string? requestId)
    {
        string? serverName = data.TryGetProperty("serverName", out var sProp) ? sProp.GetString() : null;
        string? keyName = data.TryGetProperty("keyName", out var kProp) ? kProp.GetString() : null;
        string? keyValue = data.TryGetProperty("keyValue", out var vProp) ? vProp.GetString() : null;

        if (!string.IsNullOrEmpty(serverName) && !string.IsNullOrEmpty(keyName))
        {
            bool saved = await McpSkillsService.Instance.SaveMcpAuthKeyAsync(serverName, keyName, keyValue ?? "").ConfigureAwait(false);
            var servers = await McpSkillsService.Instance.GetMcpServersAsync().ConfigureAwait(false);
            SendResponse(requestId, true, new { success = saved, servers });
        }
        else
        {
            SendResponse(requestId, false, "Server name and key name are required");
        }
    }

    private async Task HandleTestMcpAuthAsync(JsonElement data, string? requestId)
    {
        string? serverName = data.TryGetProperty("serverName", out var sProp) ? sProp.GetString() : null;
        string? keyValue = data.TryGetProperty("keyValue", out var vProp) ? vProp.GetString() : null;

        var testRes = await McpSkillsService.Instance.TestMcpAuthAsync(serverName ?? "", keyValue ?? "").ConfigureAwait(false);
        SendResponse(requestId, true, testRes);
    }

    private async Task HandleGetSkillContentAsync(JsonElement data, string? requestId)
    {
        string? skillPath = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
        if (!string.IsNullOrEmpty(skillPath))
        {
            var (content, name, description, scope, tokenCount) = await McpSkillsService.Instance.GetSkillContentAsync(skillPath).ConfigureAwait(false);
            SendResponse(requestId, true, new { content, name, description, scope, tokenCount, path = skillPath });
        }
        else
        {
            SendResponse(requestId, false, "Path is required");
        }
    }

    private async Task HandleSaveSkillContentAsync(JsonElement data, string? requestId)
    {
        string? skillPath = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
        string? name = data.TryGetProperty("name", out var nProp) ? nProp.GetString() : "skill";
        string? description = data.TryGetProperty("description", out var dProp) ? dProp.GetString() : "";
        string? content = data.TryGetProperty("content", out var cProp) ? cProp.GetString() : "";

        if (!string.IsNullOrEmpty(skillPath))
        {
            bool saved = await McpSkillsService.Instance.SaveSkillContentAsync(skillPath, name ?? "skill", description ?? "", content ?? "").ConfigureAwait(false);
            SendResponse(requestId, saved, new { success = saved });
        }
        else
        {
            SendResponse(requestId, false, "Path is required");
        }
    }

    private async Task HandleCreateProjectSkillAsync(JsonElement data, string? requestId)
    {
        string? wsId = data.TryGetProperty("workspaceId", out var wProp) ? wProp.GetString() : null;
        string? name = data.TryGetProperty("name", out var nProp) ? nProp.GetString() : "custom-skill";
        string? description = data.TryGetProperty("description", out var dProp) ? dProp.GetString() : "Agent capability skill";
        string? content = data.TryGetProperty("content", out var cProp) ? cProp.GetString() : "";
        string? skillScope = data.TryGetProperty("scope", out var skillScopeProp) ? skillScopeProp.GetString() : null;

        var skill = await McpSkillsService.Instance.CreateProjectSkillAsync(
            wsId, name ?? "custom-skill", description ?? "", content ?? "", skillScope).ConfigureAwait(false);
        if (skill != null)
        {
            SendResponse(requestId, true, new { success = true, skill });
        }
        else
        {
            SendResponse(requestId, false, "Failed to create project skill");
        }
    }

    private async Task HandleForkSkillToWorkspaceAsync(JsonElement data, string? requestId)
    {
        string? sourcePath = data.TryGetProperty("sourcePath", out var spProp) ? spProp.GetString() : null;
        string? targetWsId = data.TryGetProperty("workspaceId", out var twProp) ? twProp.GetString() : null;

        if (!string.IsNullOrEmpty(sourcePath))
        {
            var skill = await McpSkillsService.Instance.ForkSkillToWorkspaceAsync(sourcePath, targetWsId).ConfigureAwait(false);
            if (skill != null)
            {
                SendResponse(requestId, true, new { success = true, skill });
            }
            else
            {
                SendResponse(requestId, false, "Failed to fork skill to workspace");
            }
        }
        else
        {
            SendResponse(requestId, false, "Source path is required");
        }
    }

    private Task HandleDeleteProjectSkillAsync(JsonElement data, string? requestId)
    {
        {
            string? skillPath = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
            if (!string.IsNullOrEmpty(skillPath))
            {
                bool deleted = McpSkillsService.Instance.DeleteProjectSkill(skillPath);
                SendResponse(requestId, deleted, new { success = deleted });
            }
            else
            {
                SendResponse(requestId, false, "Path is required");
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleRunNpxCommandAsync(JsonElement data, string? requestId)
    {
        {
            string? command = data.TryGetProperty("command", out var cProp) ? cProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(command))
            {
                SendResponse(requestId, false, "A command is required");
                return Task.CompletedTask;
            }

            // Already running: report it instead of silently replacing the user's session.
            if (NpxInstallHost.IsRunning)
            {
                SendResponse(requestId, false, $"'{NpxInstallHost.CurrentCommand}' is still running");
                return Task.CompletedTask;
            }

            // Scope decides where the installer runs, and therefore what it writes to:
            // "global" targets the user profile, "workspace" targets the active project.
            string? scope = data.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() : null;
            bool isGlobal = string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase);

            string? cwd = isGlobal
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : AiConfigService.Instance.GetActiveWorkspace()?.WorkingDirectory;

            bool started = NpxInstallHost.Start(command, string.IsNullOrWhiteSpace(cwd) ? null : cwd);

            // A failure must come back as text: an object payload reaches the UI as
            // "[object Object]", which is exactly the useless error that was showing.
            if (!started)
            {
                SendResponse(requestId, false, "Could not start the install console.");
                return Task.CompletedTask;
            }

            SendResponse(requestId, true, new { started = true, command });
        }

        return Task.CompletedTask;
    }

    private Task HandleSendNpxInputAsync(JsonElement data, string? requestId)
    {
        {
            string? input = data.TryGetProperty("input", out var iProp) ? iProp.GetString() : null;
            NpxInstallHost.Write(input ?? string.Empty);
            SendResponse(requestId, true, new { sent = true });
        }

        return Task.CompletedTask;
    }

    private Task HandleStopNpxCommandAsync(string? requestId)
    {
        NpxInstallHost.Stop();
        SendResponse(requestId, true, new { stopped = true });

        return Task.CompletedTask;
    }

    private Task HandleOpenMcpConfigAsync(string? requestId)
    {
        {
            // `~/.agents/mcp.json` is where this app registers servers now, so it is the file
            // to open. It is created when missing, so the button always has something to show.
            var configPath = McpSkillsService.AgentsMcpConfigPath;
            if (!File.Exists(configPath))
            {
                try
                {
                    var folder = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                    File.WriteAllText(configPath, "{\n  \"mcpServers\": {}\n}\n");
                }
                catch (Exception ex)
                {
                    SendResponse(requestId, false, $"Could not create {configPath}: {ex.Message}");
                    return Task.CompletedTask;
                }
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(configPath)
                {
                    UseShellExecute = true
                });
                SendResponse(requestId, true, new { opened = true, path = configPath });
            }
            catch (Exception ex)
            {
                SendResponse(requestId, false, $"Could not open the config: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleRevealSkillInExplorerAsync(JsonElement data, string? requestId)
    {
        {
            string? path = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
            bool opened = !string.IsNullOrEmpty(path) && McpSkillsService.RevealInExplorer(path);
            SendResponse(requestId, opened, new { success = opened });
        }

        return Task.CompletedTask;
    }

    private Task HandleOpenSkillInEditorAsync(JsonElement data, string? requestId)
    {
        {
            string? path = data.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
            bool opened = !string.IsNullOrEmpty(path) && McpSkillsService.OpenInEditor(path);
            SendResponse(requestId, opened, new { success = opened });
        }

        return Task.CompletedTask;
    }

    private Task HandleLaunchInteractiveNpxInstallerAsync(JsonElement data, string? requestId)
    {
        {
            string? command = data.TryGetProperty("command", out var cProp) ? cProp.GetString() : null;
            string? targetWsId = data.TryGetProperty("workspaceId", out var npxWsProp) ? npxWsProp.GetString() : null;
            string? mode = data.TryGetProperty("mode", out var mProp) ? mProp.GetString() : "terminal";

            var workspaces = AiConfigService.Instance.GetWorkspaces();
            var ws = !string.IsNullOrEmpty(targetWsId)
                ? workspaces.FirstOrDefault(w => string.Equals(w.Id, targetWsId, StringComparison.OrdinalIgnoreCase))
                : AiConfigService.Instance.GetActiveWorkspace();

            var workDir = ws?.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
            {
                workDir = Environment.CurrentDirectory;
            }

            if (!string.IsNullOrWhiteSpace(command))
            {
                if (mode == "window")
                {
                    Process.Start(ShellInDirectory(workDir, command));
                    SendResponse(requestId, true, new { success = true, mode = "window" });
                }
                else
                {
                    var targetSession = ResolveTerminalSessionId(ws?.SecondaryCli ?? "powershell");
                    _cliManager.SendInput(targetSession, $"cd \"{workDir}\"\r\n{command}\r\n");
                    SendResponse(requestId, true, new { success = true, mode = "terminal", session = targetSession });
                }
            }
            else
            {
                SendResponse(requestId, false, "Command is required");
            }
        }

        return Task.CompletedTask;
    }

}