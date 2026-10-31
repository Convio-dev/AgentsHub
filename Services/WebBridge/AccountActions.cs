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

    private Task HandleSetProfileSkipPermissionsAsync(JsonElement data, string? requestId)
    {
        // Persisted per profile and shared with the terminal tab's own checkbox, so chat and
        // terminal can never disagree about whether full access is on.
        {
            string? profile = data.TryGetProperty("profile", out var psElem) ? psElem.GetString() : null;
            if (string.IsNullOrWhiteSpace(profile))
            {
                profile = _cliManager.ProfileService.ActiveProfile;
            }
            if (string.IsNullOrWhiteSpace(profile))
            {
                SendResponse(requestId, false, "No profile is configured.");
                return Task.CompletedTask;
            }
            if (!data.TryGetProperty("skipPermissions", out var pvElem) ||
                pvElem.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                SendResponse(requestId, false, "skipPermissions must be true or false.");
                return Task.CompletedTask;
            }

            bool value = pvElem.GetBoolean();
            _cliManager.ProfileService.SetProfileSkipPermissions(profile, value);
            SendResponse(requestId, true, new { profile, skipPermissions = value });
        }

        return Task.CompletedTask;
    }

    private Task HandleGrantPermissionAsync(JsonElement data, string? requestId)
    {
        // "Allow" for a denied tool means writing the rule the CLI asked for: headless mode has
        // no prompt to answer. Only the rule shape is validated, nothing else in the file moves.
        {
            string? profile = data.TryGetProperty("profile", out var gpElem) ? gpElem.GetString() : null;
            if (string.IsNullOrWhiteSpace(profile))
            {
                profile = _cliManager.ProfileService.ActiveProfile;
            }
            string? rule = data.TryGetProperty("rule", out var grElem) ? grElem.GetString() : null;

            if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(rule))
            {
                SendResponse(requestId, false, "A profile and a rule are required.");
                return Task.CompletedTask;
            }

            bool inEffect = _cliManager.ProfileService.AddPermissionAllowRule(profile, rule);
            if (inEffect)
            {
                SendResponse(requestId, true, new { profile, rule });
            }
            else
            {
                SendResponse(requestId, false, $"'{rule}' could not be written to the profile's settings.");
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleRevokePermissionAsync(JsonElement data, string? requestId)
    {
        // Undoes a grant. "Run once" and "allow for this conversation" are emulated by writing
        // the rule and removing it again, because the settings file is the only lever headless
        // `agy` reads.
        {
            string? profile = data.TryGetProperty("profile", out var rpElem) ? rpElem.GetString() : null;
            if (string.IsNullOrWhiteSpace(profile))
            {
                profile = _cliManager.ProfileService.ActiveProfile;
            }
            string? rule = data.TryGetProperty("rule", out var rrElem) ? rrElem.GetString() : null;

            if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(rule))
            {
                SendResponse(requestId, false, "A profile and a rule are required.");
                return Task.CompletedTask;
            }

            bool removed = _cliManager.ProfileService.RemovePermissionAllowRule(profile, rule);
            if (removed)
            {
                SendResponse(requestId, true, new { profile, rule });
            }
            else
            {
                SendResponse(requestId, false, $"'{rule}' is still present in the profile's settings.");
            }
        }

        return Task.CompletedTask;
    }

    private async Task HandleCheckProfileQuotaAsync(JsonElement data, string? requestId)
    {
        // On-demand quota read for one profile. Never touches other profiles: the sidecar polls
        // with that profile's own virtual home, so each account keeps its own pool.
        {
            string? profile = data.TryGetProperty("profile", out var pqElem) ? pqElem.GetString() : null;
            if (string.IsNullOrWhiteSpace(profile))
            {
                profile = _cliManager.ProfileService.ActiveProfile;
            }

            if (string.IsNullOrWhiteSpace(profile) || string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
            {
                SendResponse(requestId, false, "No profile is configured to check.");
                return;
            }

            await Sidecar.SidecarHostService.Instance.CheckProfileQuotaAsync(profile).ConfigureAwait(false);
            SendResponse(requestId, true, new { requested = true, profile });
        }
    }

    private async Task HandleSwitchAccountAsync(JsonElement data, string? requestId)
    {
        string activeFallback = _cliManager.ProfileService.ActiveProfile ?? "default";
        string profile = data.TryGetProperty("profile", out var pElem) ? pElem.GetString() ?? activeFallback : activeFallback;
        if (string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
        {
            profile = activeFallback;
        }
        string? provider = data.TryGetProperty("provider", out var prElem) ? prElem.GetString() : null;

        await Sidecar.SidecarHostService.Instance.SwitchAccountAsync(profile, provider).ConfigureAwait(false);
        SendResponse(requestId, true, new { success = true, profile });
    }

    private async Task HandleListAccountsAsync(string? requestId)
    {
        await Sidecar.SidecarHostService.Instance.ListAccountsAsync().ConfigureAwait(false);
        SendResponse(requestId, true, new { requested = true });
    }

    private async Task HandleRefreshTokenAsync(JsonElement data, string? requestId)
    {
        string profile = data.TryGetProperty("profile", out var pElem) ? pElem.GetString() ?? "Default" : "Default";
        await Sidecar.SidecarHostService.Instance.RefreshTokenAsync(profile).ConfigureAwait(false);
        SendResponse(requestId, true, new { requested = true, profile });
    }

    private Task HandleGetActiveProfileEmailAsync(string? requestId)
    {
        {
            var actProf = _cliManager.ProfileService.ActiveProfile;
            var actEmail = AntigravityProfileService.GetAccountEmail(actProf) ?? string.Empty;
            SendResponse(requestId, true, new { profile = actProf, email = actEmail });
        }

        return Task.CompletedTask;
    }

    private async Task HandleSwitchAntigravityProfileAsync(JsonElement data, string? requestId)
    {
        string? pName = null;
        if (data.TryGetProperty("profile", out var profProp))
            pName = profProp.GetString();
        else if (data.TryGetProperty("profileName", out var profNameProp))
            pName = profNameProp.GetString();

        if (!string.IsNullOrWhiteSpace(pName))
        {
            await _cliManager.SwitchAntigravityProfileAsync(pName);
            SendResponse(requestId, true, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
        }
    }

    private async Task HandleStartAntigravityInstanceAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("profile", out var startProfProp))
        {
            var pName = startProfProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(pName))
            {
                bool isRawTerminal = data.TryGetProperty("rawTerminal", out var rtProp) && rtProp.GetBoolean();
                if (isRawTerminal)
                {
                    _cliManager.StartAntigravityProfile(pName);
                    SendResponse(requestId, true, new { success = true, profile = pName, isSidecar = false, port = _cliManager.GetPortForProfile(pName), profiles = GetProfilesWithEmail() });
                }
                else
                {
                    var curWs = AiConfigService.Instance.GetActiveWorkspace();
                    string? cwd = curWs?.WorkingDirectory;
                    string model = "Gemini 3.8 Flash (High)";
                    await _cliManager.SwitchAntigravityProfileAsync(pName).ConfigureAwait(false);
                    await Sidecar.SidecarHostService.Instance.StartProfileSessionAsync(pName, cwd, "gemini-3.8-flash-high").ConfigureAwait(false);
                    PostEventToUi("antigravity_instance_status_changed", new { profile = pName, isRunning = true, model });
                    PostEventToUi("profile_changed", new { activeProfile = pName, model, profiles = GetProfilesWithEmail() });
                    SendResponse(requestId, true, new { success = true, profile = pName, isSidecar = true, model, profiles = GetProfilesWithEmail() });
                }
            }
        }
    }

    private async Task HandleStopAntigravityInstanceAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("profile", out var stopProfProp))
        {
            var pName = stopProfProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(pName))
            {
                await Sidecar.SidecarHostService.Instance.StopProfileSessionAsync(pName).ConfigureAwait(false);
                await _cliManager.StopAntigravityProfileAsync(pName).ConfigureAwait(false);
                PostEventToUi("antigravity_instance_status_changed", new { profile = pName, isRunning = false });
                PostEventToUi("profile_changed", new { activeProfile = pName, profiles = GetProfilesWithEmail() });
                SendResponse(requestId, true, new { success = true, profile = pName, profiles = GetProfilesWithEmail() });
            }
        }
    }

    private async Task HandleCreateAntigravityProfileAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("profileName", out var newNameProp))
        {
            var newName = newNameProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(newName))
            {
                var profiles = _cliManager.ProfileService.GetProfiles();
                if (profiles.Contains(newName, StringComparer.OrdinalIgnoreCase))
                {
                    SendResponse(requestId, false, $"A profile named '{newName}' already exists.");
                    return;
                }
                await _cliManager.CreateAndSwitchNewProfileAsync(newName);
                SendResponse(requestId, true, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
            }
        }
    }

    private Task HandleRenameAntigravityProfileAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("oldName", out var oldProp) && data.TryGetProperty("newName", out var newProp))
        {
            var oldName = oldProp.GetString()?.Trim();
            var newName = newProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(oldName) && !string.IsNullOrWhiteSpace(newName))
            {
                var ok = _cliManager.ProfileService.RenameProfile(oldName, newName);
                SendResponse(requestId, ok, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleDeleteAntigravityProfileAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("profileName", out var delProp))
        {
            var pName = delProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(pName))
            {
                var ok = _cliManager.ProfileService.DeleteProfile(pName);
                SendResponse(requestId, ok, new { activeProfile = _cliManager.ProfileService.ActiveProfile, profiles = GetProfilesWithEmail() });
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleOpenProfileFolderAsync(JsonElement data, string? requestId)
    {
        if (data.TryGetProperty("profileName", out var folderProp))
        {
            var pName = folderProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(pName))
            {
                var profileDir = Path.Combine(AntigravityProfileService.ProfilesBaseDir, pName);
                if (Directory.Exists(profileDir))
                {
                    Process.Start(new ProcessStartInfo(
                        Services.Platform.PlatformHelper.IsWindows ? "explorer.exe"
                            : Services.Platform.PlatformHelper.IsMacOS ? "open" : "xdg-open",
                        profileDir) { UseShellExecute = true });
                    SendResponse(requestId, true, null);
                }
                else
                {
                    SendResponse(requestId, false, "Profile folder not found.");
                }
            }
        }

        return Task.CompletedTask;
    }

    private object[] GetProfilesWithEmail()
    {
        return [.. _cliManager.ProfileService.GetProfiles()
            .Select(name => (object)new
            {
                name,
                email = AntigravityProfileService.GetAccountEmail(name) ?? string.Empty,
                model = "Gemini 3.8 Flash (High)",
                isRunning = _cliManager.IsProfileRunning(name) || Sidecar.SidecarHostService.Instance.IsProfileActive(name),
                port = _cliManager.GetPortForProfile(name),
                // Shared with the terminal tab's checkbox, so chat and terminal cannot disagree.
                skipPermissions = _cliManager.ProfileService.GetProfileSkipPermissions(name)
            })];
    }

}