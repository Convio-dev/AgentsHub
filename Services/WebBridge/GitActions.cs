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

    private async Task HandleGetCommitGraphAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        int maxCount = data.TryGetProperty("maxCount", out var mProp) && mProp.TryGetInt32(out var mc) ? mc : 100;
        if (!string.IsNullOrEmpty(repoPath))
        {
            var graph = await GitRepositoryManagerService.Instance.GetCommitGraphAsync(repoPath, maxCount).ConfigureAwait(false);
            SendResponse(requestId, true, new { commits = graph });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleGetCommitDiffAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? hash = data.TryGetProperty("commitHash", out var hProp) ? hProp.GetString() : null;
        if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(hash))
        {
            var diff = await GitRepositoryManagerService.Instance.GetCommitDiffDetailAsync(repoPath, hash).ConfigureAwait(false);
            SendResponse(requestId, diff != null, diff ?? (object)new { error = "Commit not found" });
        }
        else
        {
            SendResponse(requestId, false, "repoPath and commitHash are required");
        }
    }

    private async Task HandleRevertCommitAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? commitHash = data.TryGetProperty("commitHash", out var hProp) ? hProp.GetString() : null;
        string mode = data.TryGetProperty("mode", out var mProp) ? mProp.GetString() ?? "new_branch" : "new_branch";
        string? newBranchName = data.TryGetProperty("newBranchName", out var nbProp) ? nbProp.GetString() : null;

        if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(commitHash))
        {
            var (ok, msg, newHash) = await GitRepositoryManagerService.Instance.RevertCommitAsync(repoPath, commitHash, mode, newBranchName).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = msg, newCommitHash = newHash });
        }
        else
        {
            SendResponse(requestId, false, "repoPath and commitHash are required");
        }
    }

    private async Task HandleCreateBranchAtCommitAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? commitHash = data.TryGetProperty("commitHash", out var hProp) ? hProp.GetString() : null;
        string? branchName = data.TryGetProperty("branchName", out var bProp) ? bProp.GetString() : null;
        bool checkout = !data.TryGetProperty("checkout", out var cProp) || cProp.GetBoolean();

        if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(commitHash) && !string.IsNullOrEmpty(branchName))
        {
            var (ok, msg) = await GitRepositoryManagerService.Instance.CreateBranchAtCommitAsync(repoPath, commitHash, branchName, checkout).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = msg });
        }
        else
        {
            SendResponse(requestId, false, "repoPath, commitHash, and branchName are required");
        }
    }

    private async Task HandleGetWorktreesAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        if (!string.IsNullOrEmpty(repoPath))
        {
            var wts = await GitRepositoryManagerService.Instance.GetWorktreesAsync(repoPath).ConfigureAwait(false);
            SendResponse(requestId, true, new { worktrees = wts });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleAddWorktreeAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? worktreePath = data.TryGetProperty("worktreePath", out var wProp) ? wProp.GetString() : null;
        string? branch = data.TryGetProperty("branch", out var bProp) ? bProp.GetString() : null;
        bool createBranch = data.TryGetProperty("createBranch", out var cbProp) && cbProp.GetBoolean();

        if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(worktreePath) && !string.IsNullOrEmpty(branch))
        {
            var (ok, msg) = await GitRepositoryManagerService.Instance.AddWorktreeAsync(repoPath, worktreePath, branch, createBranch).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = msg });
        }
        else
        {
            SendResponse(requestId, false, "repoPath, worktreePath, and branch are required");
        }
    }

    private async Task HandleRemoveWorktreeAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? worktreePath = data.TryGetProperty("worktreePath", out var wProp) ? wProp.GetString() : null;
        bool force = data.TryGetProperty("force", out var fProp) && fProp.GetBoolean();

        if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(worktreePath))
        {
            var (ok, msg) = await GitRepositoryManagerService.Instance.RemoveWorktreeAsync(repoPath, worktreePath, force).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = msg });
        }
        else
        {
            SendResponse(requestId, false, "repoPath and worktreePath are required");
        }
    }

    private async Task HandleGetStashesAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        if (!string.IsNullOrEmpty(repoPath))
        {
            var stashes = await GitRepositoryManagerService.Instance.GetStashesAsync(repoPath).ConfigureAwait(false);
            SendResponse(requestId, true, new { stashes });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleGetStashDiffAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        int idx = data.TryGetProperty("stashIndex", out var iProp) && iProp.TryGetInt32(out var indexVal) ? indexVal : 0;
        if (!string.IsNullOrEmpty(repoPath))
        {
            var (ok, diff) = await GitRepositoryManagerService.Instance.GetStashDiffAsync(repoPath, idx).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, diffContent = diff });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleCreateStashAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? msg = data.TryGetProperty("message", out var mProp) ? mProp.GetString() : null;
        bool untracked = data.TryGetProperty("includeUntracked", out var uProp) && uProp.GetBoolean();

        if (!string.IsNullOrEmpty(repoPath))
        {
            var (ok, resMsg) = await GitRepositoryManagerService.Instance.CreateStashAsync(repoPath, msg, untracked).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = resMsg });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleApplyStashAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        int idx = data.TryGetProperty("stashIndex", out var iProp) && iProp.TryGetInt32(out var indexVal) ? indexVal : 0;
        bool pop = data.TryGetProperty("pop", out var pProp) && pProp.GetBoolean();

        if (!string.IsNullOrEmpty(repoPath))
        {
            var (ok, resMsg) = await GitRepositoryManagerService.Instance.ApplyStashAsync(repoPath, idx, pop).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = resMsg });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleDropStashAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        int idx = data.TryGetProperty("stashIndex", out var iProp) && iProp.TryGetInt32(out var indexVal) ? indexVal : 0;

        if (!string.IsNullOrEmpty(repoPath))
        {
            var (ok, resMsg) = await GitRepositoryManagerService.Instance.DropStashAsync(repoPath, idx).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = resMsg });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandlePreviewGitMergeAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? sourceBranch = data.TryGetProperty("sourceBranch", out var sbProp) ? sbProp.GetString() : null;
        string? targetBranch = data.TryGetProperty("targetBranch", out var tbProp) ? tbProp.GetString() : null;

        if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(sourceBranch))
        {
            var preview = await GitRepositoryManagerService.Instance.PreviewMergeAsync(repoPath, sourceBranch, targetBranch).ConfigureAwait(false);
            SendResponse(requestId, preview.Success, preview);
        }
        else
        {
            SendResponse(requestId, false, "repoPath and sourceBranch are required");
        }
    }

    private async Task HandleExecuteGitMergeAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        string? sourceBranch = data.TryGetProperty("sourceBranch", out var sbProp) ? sbProp.GetString() : null;
        bool ffOnly = data.TryGetProperty("fastForwardOnly", out var ffProp) && ffProp.GetBoolean();
        string? msg = data.TryGetProperty("message", out var mProp) ? mProp.GetString() : null;

        if (!string.IsNullOrEmpty(repoPath) && !string.IsNullOrEmpty(sourceBranch))
        {
            var res = await GitRepositoryManagerService.Instance.ExecuteMergeAsync(repoPath, sourceBranch, ffOnly, msg).ConfigureAwait(false);
            SendResponse(requestId, res.Success, res);
        }
        else
        {
            SendResponse(requestId, false, "repoPath and sourceBranch are required");
        }
    }

    private async Task HandleGetCodebaseGraphAsync(JsonElement data, string? requestId)
    {
        string? wsPath = null;
        if (data.TryGetProperty("workspacePath", out var pProp)) wsPath = pProp.GetString();
        if (string.IsNullOrWhiteSpace(wsPath))
        {
            var curWs = AiConfigService.Instance.GetActiveWorkspace();
            wsPath = curWs?.WorkingDirectory;
        }
        if (string.IsNullOrWhiteSpace(wsPath))
        {
            wsPath = AppDomain.CurrentDomain.BaseDirectory;
        }

        int maxFiles = 300;
        if (data.TryGetProperty("maxFiles", out var mfProp) && mfProp.TryGetInt32(out var parsedMaxFiles))
        {
            maxFiles = parsedMaxFiles;
        }
        bool forceRefresh = data.TryGetProperty("forceRefresh", out var frProp) && frProp.GetBoolean();

        var graph = await CodebaseGraphAnalysisService.Instance.AnalyzeWorkspaceAsync(wsPath, maxFiles, forceRefresh).ConfigureAwait(false);
        SendResponse(requestId, true, new { graph });
    }

    private async Task HandleCalculateBlastRadiusAsync(JsonElement data, string? requestId)
    {
        string? wsPath = null;
        if (data.TryGetProperty("workspacePath", out var pProp)) wsPath = pProp.GetString();
        if (string.IsNullOrWhiteSpace(wsPath))
        {
            var curWs = AiConfigService.Instance.GetActiveWorkspace();
            wsPath = curWs?.WorkingDirectory;
        }

        string fileId = data.TryGetProperty("fileId", out var fProp) ? fProp.GetString() ?? "" : "";
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            var report = await CodebaseGraphAnalysisService.Instance.CalculateBlastRadiusAsync(wsPath, fileId).ConfigureAwait(false);
            SendResponse(requestId, true, new { report });
        }
        else
        {
            SendResponse(requestId, false, "fileId is required");
        }
    }

    private async Task HandleBatchRepoActionAsync(JsonElement data, string? requestId)
    {
        var idList = new List<string>();
        if (data.TryGetProperty("repoIds", out var idsProp) && idsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in idsProp.EnumerateArray())
            {
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s)) idList.Add(s);
            }
        }
        string batchAction = data.TryGetProperty("batchAction", out var actProp)
            ? actProp.GetString() ?? "fetch"
            : (data.TryGetProperty("action", out var aProp) ? aProp.GetString() ?? "fetch" : "fetch");

        var results = await GitRepositoryManagerService.Instance.BatchActionAsync(idList, batchAction).ConfigureAwait(false);
        SendResponse(requestId, true, new { results });
    }

    private async Task HandleGetGithubAuthStatusAsync(string? requestId)
    {
        var status = await GitHubService.Instance.GetAuthStatusAsync().ConfigureAwait(false);
        SendResponse(requestId, true, status);
    }

    private async Task HandleAutoDetectGithubGitCredentialsAsync(string? requestId)
    {
        var (ok, msg, status) = await GitHubService.Instance.AutoConnectFromGitAsync().ConfigureAwait(false);
        SendResponse(requestId, ok, new { success = ok, message = msg, status });
    }

    private async Task HandleSaveGithubPatAsync(JsonElement data, string? requestId)
    {
        string? token = data.TryGetProperty("token", out var tokProp) ? tokProp.GetString() : null;
        if (!string.IsNullOrEmpty(token))
        {
            var (ok, msg, status) = await GitHubService.Instance.SavePatAsync(token).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = msg, status });
        }
        else
        {
            SendResponse(requestId, false, "Token is required");
        }
    }

    private Task HandleDisconnectGithubAsync(string? requestId)
    {
        {
            bool disconnected = GitHubService.Instance.Disconnect();
            SendResponse(requestId, disconnected, new { success = disconnected });
        }

        return Task.CompletedTask;
    }

    private async Task HandlePublishRepoToGithubAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
        string? repoName = data.TryGetProperty("repoName", out var rnProp) ? rnProp.GetString() : null;
        string? desc = data.TryGetProperty("description", out var dProp) ? dProp.GetString() : "";
        bool isPrivate = data.TryGetProperty("isPrivate", out var ipProp) && ipProp.GetBoolean();

        if (!string.IsNullOrEmpty(repoPath))
        {
            var result = await GitHubService.Instance.PublishRepositoryAsync(repoPath, repoName ?? "", desc ?? "", isPrivate).ConfigureAwait(false);
            SendResponse(requestId, result.Success, result);
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleGetGithubPullRequestsAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
        string state = data.TryGetProperty("state", out var prStateProp) ? prStateProp.GetString() ?? "open" : "open";
        if (!string.IsNullOrEmpty(repoPath))
        {
            var res = await GitHubService.Instance.GetPullRequestsAsync(repoPath, state).ConfigureAwait(false);
            SendResponse(requestId, res.Success, res);
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleCheckoutGithubPullRequestAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
        int prNumber = data.TryGetProperty("prNumber", out var pnProp) ? pnProp.GetInt32() : 0;
        if (!string.IsNullOrEmpty(repoPath) && prNumber > 0)
        {
            var (ok, msg, branch) = await GitHubService.Instance.CheckoutPullRequestAsync(repoPath, prNumber).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = msg, branchName = branch });
        }
        else
        {
            SendResponse(requestId, false, "repoPath and valid prNumber are required");
        }
    }

    private async Task HandleMergeGithubPullRequestAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
        int prNumber = data.TryGetProperty("prNumber", out var pnProp) ? pnProp.GetInt32() : 0;
        string mergeMethod = data.TryGetProperty("mergeMethod", out var mmProp) ? mmProp.GetString() ?? "merge" : "merge";
        string? commitTitle = data.TryGetProperty("commitTitle", out var ctProp) ? ctProp.GetString() : null;
        string? commitMessage = data.TryGetProperty("commitMessage", out var cmProp) ? cmProp.GetString() : null;

        if (!string.IsNullOrEmpty(repoPath) && prNumber > 0)
        {
            var (ok, msg, sha) = await GitHubService.Instance.MergePullRequestAsync(repoPath, prNumber, mergeMethod, commitTitle, commitMessage).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, message = msg, sha });
        }
        else
        {
            SendResponse(requestId, false, "repoPath and valid prNumber are required");
        }
    }

    private async Task HandleGenerateAiCommitMessageAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rpProp) ? rpProp.GetString() : null;
        if (!string.IsNullOrEmpty(repoPath))
        {
            var (ok, subject, body, fullMsg) = await GitHubService.Instance.GenerateCommitMessageFromDiffAsync(repoPath).ConfigureAwait(false);
            SendResponse(requestId, ok, new { success = ok, subject, body, fullMessage = fullMsg });
        }
        else
        {
            SendResponse(requestId, false, "repoPath is required");
        }
    }

    private async Task HandleGetAgentRulesAsync(JsonElement data, string? requestId)
    {
        string? wsId = data.TryGetProperty("workspaceId", out var gWsProp) ? gWsProp.GetString() : null;
        var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(wsId).ConfigureAwait(false);
        SendResponse(requestId, true, overview);
    }

    private async Task HandleSaveAgentRuleAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("rule", out var rProp))
        {
            var rule = JsonSerializer.Deserialize<AgentRuleItem>(rProp.GetRawText(), CaseInsensitiveJsonOptions);
            string? targetWs = data.TryGetProperty("workspaceId", out var sWsProp) ? sWsProp.GetString() : null;
            if (rule != null)
            {
                bool saved = await AgentRulesService.Instance.SaveRuleAsync(rule, targetWs).ConfigureAwait(false);
                var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
                SendResponse(requestId, true, new { success = saved, overview });
                return;
            }
        }
        SendResponse(requestId, false, "Invalid rule payload");
    }

    private async Task HandleDeleteAgentRuleAsync(JsonElement data, string? requestId)
    {
        string? ruleId = data.TryGetProperty("ruleId", out var rProp) ? rProp.GetString() : null;
        string? ruleName = data.TryGetProperty("ruleName", out var rnProp) ? rnProp.GetString() : null;
        string? scope = data.TryGetProperty("scope", out var delScopeProp) ? delScopeProp.GetString() : null;
        string? targetWs = data.TryGetProperty("workspaceId", out var dWsProp) ? dWsProp.GetString() : null;

        if (!string.IsNullOrEmpty(ruleId) || !string.IsNullOrEmpty(ruleName))
        {
            var id = ruleId ?? ruleName ?? "";
            var name = ruleName ?? ruleId ?? "";
            bool deleted = await AgentRulesService.Instance.DeleteRuleAsync(id, name, scope ?? "Project", targetWs).ConfigureAwait(false);
            var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
            SendResponse(requestId, true, new { success = deleted, overview });
        }
        else
        {
            SendResponse(requestId, false, "Rule ID and name are required");
        }
    }

    private async Task HandleToggleWorkspaceRuleAsync(JsonElement data, string? requestId)
    {
        string? wsId = data.TryGetProperty("workspaceId", out var tWsProp) ? tWsProp.GetString() : null;
        string? ruleId = data.TryGetProperty("ruleId", out var rProp) ? rProp.GetString() : null;
        bool enable = data.TryGetProperty("enable", out var enProp) && enProp.GetBoolean();

        if (!string.IsNullOrEmpty(ruleId))
        {
            var targetWs = !string.IsNullOrEmpty(wsId) ? wsId : AiConfigService.Instance.GetActiveWorkspaceId();
            bool toggled = AgentRulesService.Instance.ToggleRuleForWorkspace(targetWs, ruleId, enable);
            var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
            SendResponse(requestId, true, new { success = toggled, overview });
        }
        else
        {
            SendResponse(requestId, false, "Rule identifier is required");
        }
    }

    private async Task HandleSetWorkspaceRuleIsolationAsync(JsonElement data, string? requestId)
    {
        string? wsId = data.TryGetProperty("workspaceId", out var iWsProp) ? iWsProp.GetString() : null;
        bool isolate = data.TryGetProperty("isolate", out var isoProp) && isoProp.GetBoolean();

        var targetWs = !string.IsNullOrEmpty(wsId) ? wsId : AiConfigService.Instance.GetActiveWorkspaceId();
        bool set = AgentRulesService.Instance.SetWorkspaceRuleIsolation(targetWs, isolate);
        var overview = await AgentRulesService.Instance.GetRulesOverviewAsync(targetWs).ConfigureAwait(false);
        SendResponse(requestId, true, new { success = set, overview });
    }

    private async Task HandleGitBranchAtAsync(JsonElement data, string? requestId)
    {
        // Branch from a commit, never reset. `git branch` moves nothing, so the current
        // branch and its commits are untouched even if the checkout step fails.
        string repoPath = data.TryGetProperty("repoPath", out var bp) ? bp.GetString() ?? string.Empty : string.Empty;
        string name = data.TryGetProperty("name", out var bn) ? bn.GetString() ?? string.Empty : string.Empty;
        string hash = data.TryGetProperty("hash", out var bh) ? bh.GetString() ?? string.Empty : string.Empty;
        bool alsoSwitch = data.TryGetProperty("switchTo", out var bs) && bs.ValueKind == JsonValueKind.True;

        bool validName = name.Length > 0
            && System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9._\-/]+$")
            && !name.Contains("..");
        bool validHash = System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-fA-F]{4,40}$");

        if (!Directory.Exists(repoPath) || !validName || !validHash)
        {
            SendResponse(requestId, true, new { created = false, switched = false, error = "Invalid branch name, commit, or repository." });
            return;
        }

        var (branchRc, branchOut) = await GitRepositoryManagerService
            .RunGitAsync(repoPath, ["branch", name, hash]).ConfigureAwait(false);

        if (branchRc != 0)
        {
            SendResponse(requestId, true, new { created = false, switched = false, error = branchOut.Trim() });
            return;
        }

        bool switched = false;
        string? switchError = null;

        if (alsoSwitch)
        {
            // Only after the branch exists, and never forced: a dirty tree may refuse,
            // and that refusal is reported rather than worked around.
            var (coRc, coOut) = await GitRepositoryManagerService
                .RunGitAsync(repoPath, ["checkout", name]).ConfigureAwait(false);
            switched = coRc == 0;
            if (!switched) switchError = coOut.Trim();
        }

        SendResponse(requestId, true, new { created = true, switched, error = switchError });
    }

    private async Task HandleGitCommitGraphAsync(JsonElement data, string? requestId)
    {
        // GetCommitGraphAsync has existed all along; nothing exposed it, so the graph tab
        // rendered an empty list rather than a broken one.
        string? repoPath = data.TryGetProperty("repoPath", out var graphPath) ? graphPath.GetString() : null;
        if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
        {
            SendResponse(requestId, true, new { commits = Array.Empty<object>() });
            return;
        }

        var nodes = await GitRepositoryManagerService.Instance.GetCommitGraphAsync(repoPath).ConfigureAwait(false);
        SendResponse(requestId, true, new { commits = nodes });
    }

    private async Task HandleGetGitStatusAsync(JsonElement data, string? requestId)
    {
        string? repoPath = data.TryGetProperty("repoPath", out var rProp) ? rProp.GetString() : null;
        if (string.IsNullOrEmpty(repoPath))
        {
            var ws = AiConfigService.Instance.GetActiveWorkspace();
            repoPath = ws?.WorkingDirectory;
        }
        if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
        {
            repoPath = Environment.CurrentDirectory;
        }

        var changes = await GitRepositoryManagerService.Instance.GetWorkingTreeChangesAsync(repoPath).ConfigureAwait(false);
        var (rc, rawDiff) = await GitRepositoryManagerService.RunGitAsync(repoPath, ["diff", "--no-color"]).ConfigureAwait(false);
        var files = changes.Select(c => c.Path).ToList();

        SendResponse(requestId, true, new
        {
            repoPath,
            files,
            changes,
            rawDiff = !string.IsNullOrWhiteSpace(rawDiff) ? rawDiff : "No uncommitted diff detected in active workspace.",
            diff = !string.IsNullOrWhiteSpace(rawDiff) ? rawDiff : "No uncommitted diff detected in active workspace."
        });
    }

}